// Additions by sts2-pilot-trainer, not part of the vendored upstream snapshot.
// Copyright (c) 2026 Zachary Landes. MIT (see the repository LICENSE).
//
// The upstream stubs cover the Godot surface sts2-cli's own code paths reach.
// This project reaches a few more, and a missing *type* is worse than a missing
// method: the runtime fails to load every type that mentions it, so a single gap
// here takes out the save subsystem entirely rather than one call.
//
// Recorded in CHANGES.md. Behaviour follows Godot's documented semantics, because
// these run inside the game's own path handling and a plausible-looking variant
// would fail somewhere far from here.

using System.Text;
using Sts2PilotTrainer.IO;

namespace Godot;

public static class StringExtensions
{
    /// <summary>Godot: the path with the final component removed, protocol kept.</summary>
    public static string GetBaseDir(this string instance)
    {
        var end = instance.LastIndexOf('/') + 1;
        if (end == 0) return "";
        // Keep the leading slash of an absolute path, and "res://" style prefixes.
        return end == 1 ? "/" : instance[..(end - 1)];
    }

    /// <summary>Godot: the final path component.</summary>
    public static string GetFile(this string instance)
    {
        var start = instance.LastIndexOf('/') + 1;
        return start <= 0 ? instance : instance[start..];
    }

    /// <summary>Godot: joins two path fragments with exactly one separator.</summary>
    public static string PathJoin(this string instance, string file)
    {
        if (instance.Length == 0) return file;
        return instance[^1] == '/' || (file.Length > 0 && file[0] == '/')
            ? instance + file
            : instance + "/" + file;
    }

    /// <summary>Godot: "some_value" or "someValue" becomes "Some Value".</summary>
    public static string Capitalize(this string instance)
    {
        var words = instance.ToSnakeCase().Replace('_', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new StringBuilder();
        foreach (var word in words)
        {
            if (result.Length > 0) result.Append(' ');
            result.Append(char.ToUpperInvariant(word[0])).Append(word[1..].ToLowerInvariant());
        }
        return result.ToString();
    }

    /// <summary>Godot: "someValue" or "SomeValue" becomes "some_value".</summary>
    public static string ToSnakeCase(this string instance)
    {
        var result = new StringBuilder();
        for (var i = 0; i < instance.Length; i++)
        {
            var c = instance[i];
            if (char.IsUpper(c) && i > 0 && (char.IsLower(instance[i - 1]) || char.IsDigit(instance[i - 1])))
            {
                result.Append('_');
            }
            result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }
}

/// <summary>
/// Where a headless run is allowed to write.
///
/// The player's installed game and save files are read-only inputs to this
/// project. The engine, however, expects a writable user directory and will
/// create, rewrite and delete files in it as a matter of course. Rather than
/// trust that no code path reaches for the real one, every writable path is
/// rewritten to land under a sandbox this process owns, and paths that look like
/// the real save directory are refused outright.
/// </summary>
public static class HeadlessSandbox
{
    private static string _root = Path.Combine(WorktreeLocator.Find(), "build", "sandbox");

    /// <summary>The sandbox directory. Created on demand; safe to delete between runs.</summary>
    public static string Root
    {
        get
        {
            Directory.CreateDirectory(_root);
            return _root;
        }
    }

    /// <summary>Points the sandbox somewhere else, e.g. a per-test directory.</summary>
    public static void SetRoot(string path) =>
        _root = WorktreePath.Require(path);

    /// <summary>
    /// Turns a Godot virtual path into a real one inside the sandbox. An absolute
    /// path that already points at the sandbox passes through; anything else that
    /// looks like a real game or save location is a bug worth failing loudly on,
    /// because the alternative is silently mutating the player's saves.
    /// </summary>
    public static string Globalize(string path)
    {
        if (path.StartsWith("user://", StringComparison.Ordinal))
        {
            return ResolveInsideRoot(path["user://".Length..]);
        }

        if (path.StartsWith("res://", StringComparison.Ordinal))
        {
            // Packed game resources. Nothing here can read them, and returning a
            // sandbox path means a miss rather than a hit on something unrelated.
            return ResolveInsideRoot(Path.Combine("res", path["res://".Length..]));
        }

        if (!Path.IsPathRooted(path))
        {
            return ResolveInsideRoot(path);
        }

        Guard(path);
        return Path.GetFullPath(path);
    }

    /// <summary>Refuses a write outside the sandbox.</summary>
    public static void Guard(string path)
    {
        try
        {
            PathContainment.RequireContained(_root, path);
        }
        catch (PathContainmentException)
        {
            throw new UnauthorizedAccessException(
                $"Refusing a headless filesystem operation outside the sandbox: '{Path.GetFullPath(path)}'. " +
                "The installed game and saves are read-only inputs to this project. This is a bug in the host, " +
                "not in the game.");
        }
    }

    private static string ResolveInsideRoot(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        Guard(full);
        return full;
    }
}

public partial class DirAccess
{
    /// <summary>Godot: deletes a file or empty directory, relative to this instance.</summary>
    public Error Remove(string path)
    {
        try
        {
            var target = HeadlessSandbox.Globalize(
                Path.IsPathRooted(path) ? path : Path.Combine(_path, path));
            if (File.Exists(target)) File.Delete(target);
            else if (Directory.Exists(target)) Directory.Delete(target);
            return Error.Ok;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch
        {
            return Error.Failed;
        }
    }

    /// <summary>Godot: deletes an absolute path without opening a directory first.</summary>
    public static Error RemoveAbsolute(string path)
    {
        try
        {
            var target = HeadlessSandbox.Globalize(path);
            if (File.Exists(target)) File.Delete(target);
            else if (Directory.Exists(target)) Directory.Delete(target);
            return Error.Ok;
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch
        {
            return Error.Failed;
        }
    }
}

// ── Control surface the trainer's result panel draws with ───────────────────
//
// The panel is built out of stock Godot nodes rather than a Control subclass of
// its own: the mod compiles without Godot's source generators, so a subclass would
// have no generated bridge and none of its overrides would ever be dispatched.
// Everything below is a member of the real GodotSharp the mod compiles against; it
// is stubbed here so the same code loads in the mod's game-free tests.

/// <summary>Godot: how a label's text sits inside its box vertically.</summary>
public enum VerticalAlignment { Top, Center, Bottom, Fill }

public partial class Label
{
    public HorizontalAlignment HorizontalAlignment { get; set; }
    public VerticalAlignment VerticalAlignment { get; set; }
    public bool ClipText { get; set; }
}

public partial class TextureRect
{
    public enum ExpandModeEnum { KeepSize, IgnoreSize, FitWidth, FitWidthProportional, FitHeight, FitHeightProportional }

    public enum StretchModeEnum
    {
        Scale, Tile, Keep, KeepCentered, KeepAspect, KeepAspectCentered, KeepAspectCovered,
    }

    public ExpandModeEnum ExpandMode { get; set; }
    public StretchModeEnum StretchMode { get; set; }
}

public class StyleBox : Resource
{
    public void SetContentMarginAll(float offset) { }
}

public class StyleBoxFlat : StyleBox
{
    public Color BgColor { get; set; }
    public Color BorderColor { get; set; }
    public void SetBorderWidthAll(int width) { }
    public void SetCornerRadiusAll(int radius) { }
}

public partial class Control
{
    public void AddThemeFontOverride(StringName name, Font font) { }
    public void AddThemeColorOverride(StringName name, Color color) { }
    public void AddThemeStyleboxOverride(StringName name, StyleBox stylebox) { }
}

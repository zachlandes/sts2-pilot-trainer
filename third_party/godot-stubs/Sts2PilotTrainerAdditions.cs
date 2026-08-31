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
    private static string _root = Path.Combine(Path.GetTempPath(), "sts2-pilot-trainer-sandbox");

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
    public static void SetRoot(string path) => _root = Path.GetFullPath(path);

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
            return Path.Combine(Root, path["user://".Length..].Replace('/', Path.DirectorySeparatorChar));
        }

        if (path.StartsWith("res://", StringComparison.Ordinal))
        {
            // Packed game resources. Nothing here can read them, and returning a
            // sandbox path means a miss rather than a hit on something unrelated.
            return Path.Combine(Root, "res", path["res://".Length..].Replace('/', Path.DirectorySeparatorChar));
        }

        Guard(path);
        return path;
    }

    /// <summary>Refuses a write anywhere that is meant to be read-only.</summary>
    public static void Guard(string path)
    {
        if (!Path.IsPathRooted(path)) return;
        var full = Path.GetFullPath(path);
        if (full.StartsWith(Path.GetFullPath(_root), StringComparison.Ordinal)) return;

        foreach (var forbidden in new[] { "SlayTheSpire2", "steamapps" })
        {
            if (full.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException(
                    $"Refusing a headless filesystem operation on '{forbidden}': the installed game and its " +
                    "saves are read-only inputs to this project. This is a bug in the host, not in the game.");
            }
        }
    }
}

public partial class DirAccess
{
    /// <summary>Godot: deletes a file or empty directory, relative to this instance.</summary>
    public Error Remove(string path)
    {
        try
        {
            var target = Path.IsPathRooted(path) ? path : Path.Combine(_path, path);
            HeadlessSandbox.Guard(target);
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
            HeadlessSandbox.Guard(path);
            if (File.Exists(path)) File.Delete(path);
            else if (Directory.Exists(path)) Directory.Delete(path);
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

namespace Sts2PilotTrainer.IO;

/// <summary>
/// The rule that keeps every writer in this project out of somebody's game
/// installation, stated once.
///
/// It is a component test rather than a prefix test on purpose: a Steam library can
/// be anywhere, and the thing that identifies one is a directory named <c>Steam</c>,
/// <c>steamapps</c> or <c>Slay the Spire 2</c> somewhere along the path. The
/// bootstrap refuses an output directory this matches, and the mod's store refuses a
/// write there, for the same reason - the game is a read-only input.
///
/// <c>Steam</c> is matched exactly, and that is deliberate rather than an oversight.
/// Valve's own directory is <c>Steam</c> on macOS and Windows, and on Linux a library
/// carries <c>steamapps</c> anyway - while the game's <em>user data</em> directory
/// has a platform level named <c>steam</c> in lower case
/// (<c>SlayTheSpire2/steam/&lt;account&gt;/profile1</c>), which is where the mod's own
/// store lives and is not an installation. Matching case-insensitively refused the
/// store's own root. The other two follow the host filesystem's casing semantics.
///
/// It is not the containment rule and does not replace it. Containment answers "is
/// this inside the one root I own"; this answers "is this somewhere nothing here may
/// ever write". Both are applied, because a store root that was itself inside a game
/// installation would satisfy containment perfectly.
/// </summary>
public static class ProtectedInstallPath
{
    private static readonly string[] FileSystemProtectedComponents = ["steamapps", "Slay the Spire 2"];

    public static bool HasProtectedComponent(string path)
    {
        var full = PathContainment.ResolveExistingPath(path);
        var comparison = IsCaseInsensitiveFileSystem(full)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return full
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(component =>
                component.Equals("Steam", StringComparison.Ordinal) ||
                FileSystemProtectedComponents.Any(protectedComponent =>
                    component.Equals(protectedComponent, comparison)));
    }

    private static bool IsCaseInsensitiveFileSystem(string path)
    {
        var existing = path;
        while (!Directory.Exists(existing) && !File.Exists(existing))
        {
            var parent = Path.GetDirectoryName(existing);
            if (parent is null || parent == existing) return true;
            existing = parent;
        }

        var directory = Path.GetDirectoryName(existing);
        var name = Path.GetFileName(existing);
        if (directory is null || name.Length == 0)
        {
            var entry = Directory.EnumerateFileSystemEntries(existing)
                .FirstOrDefault(candidate => ToggleCase(Path.GetFileName(candidate)) is not null);
            if (entry is null) return true;
            directory = Path.GetDirectoryName(entry);
            name = Path.GetFileName(entry);
        }

        var alternateName = ToggleCase(name);
        if (directory is null || alternateName is null) return true;
        var alternate = Path.Combine(directory, alternateName);
        return Directory.Exists(alternate) || File.Exists(alternate);
    }

    private static string? ToggleCase(string value)
    {
        var characters = value.ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            if (!char.IsLetter(characters[index])) continue;
            characters[index] = char.IsUpper(characters[index])
                ? char.ToLowerInvariant(characters[index])
                : char.ToUpperInvariant(characters[index]);
            return new string(characters);
        }

        return null;
    }

    public static string RequireUnprotected(string path)
    {
        var full = Path.GetFullPath(path);
        if (HasProtectedComponent(full))
        {
            throw new ProtectedInstallPathException(
                $"Path '{full}' lies inside a Steam or Slay the Spire 2 directory, which nothing here writes to.");
        }
        return full;
    }
}

public sealed class ProtectedInstallPathException(string message) : InvalidOperationException(message);

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

    private static StringComparison FileSystemComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public static bool HasProtectedComponent(string path) =>
        Path.GetFullPath(path)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(component =>
                component.Equals("Steam", StringComparison.Ordinal) ||
                FileSystemProtectedComponents.Any(protectedComponent =>
                    component.Equals(protectedComponent, FileSystemComparison)));

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

using Godot;
using MegaCrit.Sts2.Core.Saves;
using Sts2PilotTrainer.IO;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// Everything Runmobile writes, and the only thing in this mod that writes at all.
///
/// The mod's posture has been "reads your game, never writes to it" and stays that
/// way about the game: a save, a profile, a run history, a settings file and another
/// mod's files are not this mod's to touch, and the measured proof of that is the
/// protected-files ledger (<c>scripts/protected-files.sh</c>). What changes as
/// Runmobile grows is that the mod has files of its own - recordings a player made,
/// their progress through a recording, a derived boundary cache - and they need a
/// place. That place is under <c>user://Runmobile/</c> and there is no second one.
///
/// <para><b>Scoped the way the game scopes its own saves.</b> The root is not flat:
/// beneath <c>user://Runmobile/</c> it mirrors the platform, account and profile
/// scope the game resolved for itself, so two Steam accounts on one machine, and two
/// profiles on one account, do not share a library - and a modded session lands where
/// the game put its own modded profile. The scope is taken from
/// <see cref="UserDataPathProvider.GetProfileScopedBasePath(int, PlatformType?, ulong?)"/>,
/// the game's own answer, and re-rooted; this mod resolves no account identity of its
/// own and has no second way to ask.</para>
///
/// <para>Those identifiers are local path scoping and nothing else. They are not part
/// of a recording's identity and never travel: nothing exported, uploaded or shared
/// carries a platform directory, an account id or a profile number.</para>
///
/// One writer rather than a rule everybody remembers. Three things hold here and
/// nowhere else:
///
/// <list type="bullet">
/// <item>every path is resolved and checked against the root with
/// <see cref="PathContainment.RequireContained"/>, so a traversal, an absolute path
/// and a sibling directory whose name merely starts with the root's are all refused
/// before anything is opened;</item>
/// <item>every path is checked for a <c>Steam</c>, <c>steamapps</c> or
/// <c>Slay the Spire 2</c> component, because a root that was itself inside a game
/// installation would satisfy containment perfectly;</item>
/// <item>a whole-file write goes through <see cref="AtomicFile"/> - a temporary
/// sibling and a move - so a crash mid-write leaves the previous file rather than
/// half of a new one.</item>
/// </list>
///
/// <see cref="PrepareForWrite"/> rather than <see cref="Write(string,string)"/> is
/// the containment gate, because not every future write is a whole file: the recorder
/// appends to a journal and flushes it at room and fight boundaries, which is a
/// different write mode and the same one place to check where it may write.
///
/// It is not <see cref="ProfileWriteBarrier"/> and does not replace it. The barrier
/// suppresses the <em>game's</em> writes while a trainer run is live and has nothing
/// to say about this mod's own files; this store is where those files go. Together
/// they are the whole of "protected files stay byte-identical outside
/// <c>user://Runmobile/</c>".
/// </summary>
internal static class RunmobileStore
{
    /// <summary>The one Godot path this mod ever writes under. Everything the store
    /// holds is inside the profile scope beneath it.</summary>
    internal const string UserPath = "user://Runmobile/";

    private const string UserScheme = "user://";

    private static Func<string>? _rootForTesting;

    /// <summary>
    /// The store's root for the profile this game is running as, resolved for each operation.
    ///
    /// <c>ProjectSettings.GlobalizePath</c> is the game's own answer for where
    /// <c>user://</c> is, so this mod never derives the player's data directory
    /// itself. The answer is checked before it is used: a game whose user directory
    /// resolved inside its own installation is one this mod declines to write in at
    /// all.
    /// </summary>
    internal static string Root => ResolveRoot(
        _rootForTesting?.Invoke() ?? ProjectSettings.GlobalizePath(ScopedUserPath()));

    /// <summary>
    /// The full path of an entry in the store, refused unless it is inside it.
    ///
    /// <paramref name="relativePath"/> is relative to the root and may name a
    /// subdirectory. Nothing is created; this is the check on its own, for a caller
    /// that wants to know where a file would go without deciding to write it.
    /// </summary>
    internal static string PathOf(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException(
                "The store is asked for a named entry, not for its root.", nameof(relativePath));
        }

        var root = Root;
        var candidate = Path.Combine(root, relativePath);
        var contained = PathContainment.RequireContained(root, candidate);
        if (contained == root)
        {
            throw new PathContainmentException(
                $"'{relativePath}' names the store's own root rather than an entry in it.");
        }

        return ProtectedInstallPath.RequireUnprotected(contained);
    }

    /// <summary>
    /// The gate every write goes through: the path, checked, with its directory in
    /// place. What a caller then does with the file - replace it whole, or append to
    /// it - is the caller's, and this is where it is established that it may write
    /// there at all.
    /// </summary>
    internal static string PrepareForWrite(string relativePath)
    {
        var path = PathOf(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    /// <summary>Writes one whole entry, atomically, refusing every path outside the
    /// store.</summary>
    internal static void Write(string relativePath, string content) =>
        AtomicFile.WriteAllText(PrepareForWrite(relativePath), content);

    /// <summary>Reads one entry, or null when it is not there yet. Refuses the same
    /// paths a write does, so a read cannot be the way out of the store.</summary>
    internal static string? Read(string relativePath)
    {
        var path = PathOf(relativePath);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    /// <summary>Whether an entry exists. Same refusals.</summary>
    internal static bool Exists(string relativePath) => File.Exists(PathOf(relativePath));

    /// <summary>
    /// Where this store lives, as a <c>user://</c> path: the game's own profile scope,
    /// re-rooted under <see cref="UserPath"/>.
    /// </summary>
    private static string ScopedUserPath()
    {
        var saves = SaveManager.Instance
            ?? throw new InvalidOperationException(
                "This game has no SaveManager, so Runmobile cannot tell whose files these would be.");
        if (!saves.IsProfileInitialized)
        {
            throw new InvalidOperationException(
                "This game has not chosen a save profile yet, so Runmobile cannot tell whose files these " +
                "would be.");
        }

        return ScopedUserPath(UserDataPathProvider.GetProfileScopedBasePath(saves.CurrentProfileId));
    }

    /// <summary>
    /// Re-roots the game's own profile-scoped path under this mod's directory.
    ///
    /// Taken as a whole string rather than reassembled from a platform, an account
    /// and a profile number: the game decides what that scope is, including the
    /// <c>modded</c> level it inserts for a modded session, and a second assembly of
    /// the same parts here is a second mechanism that would drift.
    /// </summary>
    internal static string ScopedUserPath(string gameProfileBasePath)
    {
        if (!gameProfileBasePath.StartsWith(UserScheme, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"This game's save scope is '{gameProfileBasePath}', which is not under '{UserScheme}', so " +
                "Runmobile has nowhere it may write.");
        }

        var scope = gameProfileBasePath[UserScheme.Length..].Trim('/');
        if (scope.Length == 0)
        {
            throw new InvalidOperationException(
                "This game reported an empty save scope, so Runmobile cannot tell whose files these would be.");
        }

        return $"{UserPath}{scope}/";
    }

    /// <summary>
    /// Establishes the root from the game's own answer, for tests and for
    /// <see cref="Root"/>. Kept apart from the property so the rules can be exercised
    /// in a process with no game, against a root under the test's own temporary
    /// directory.
    /// </summary>
    internal static string ResolveRoot(string globalizedPath)
    {
        if (string.IsNullOrWhiteSpace(globalizedPath))
        {
            throw new InvalidOperationException(
                $"This game did not say where '{UserPath}' is, so Runmobile has nowhere it may write.");
        }

        return ProtectedInstallPath.RequireUnprotected(Path.GetFullPath(globalizedPath));
    }

    /// <summary>
    /// Points the store at a root of the caller's choosing. For tests only: nothing
    /// in the mod calls it, and the game's own answer is the only root a player's
    /// process ever has.
    /// </summary>
    internal static void UseRootForTesting(string? root)
    {
        if (root is null)
        {
            _rootForTesting = null;
            return;
        }

        var resolved = ResolveRoot(root);
        _rootForTesting = () => resolved;
    }

    internal static void UseRootProviderForTesting(Func<string> rootProvider) =>
        _rootForTesting = rootProvider ?? throw new ArgumentNullException(nameof(rootProvider));
}

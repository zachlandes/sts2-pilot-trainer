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
/// <para><b>Removing is a write and goes through the same gate.</b> A player owns the
/// disk their recordings are on, so the store can be asked to take one back off it -
/// through <see cref="Remove"/>, which names one file at a time and refuses a
/// directory. The temporary publication workspace is the sole directory-shaped
/// exception and is removed through <see cref="RemoveTree"/> after the retention owner
/// names that workspace. Nothing here decides <em>which</em> files: that is
/// <c>RecordingRetention</c>'s, and it is deliberately somewhere else, because the one
/// operation in this mod that cannot be undone should not also be the one that picks
/// its own targets.</para>
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
    /// all, and a scoped root that resolves outside <c>user://</c> - a
    /// <c>Runmobile</c> directory that is a symlink to somewhere else - is refused by
    /// the same containment rule every entry goes through, because a store outside the
    /// game's user data is one the protected-files ledger cannot measure.
    /// </summary>
    internal static string Root
    {
        get
        {
            if (_rootForTesting is not null) return ResolveRoot(_rootForTesting());

            return ResolveRoot(
                ProjectSettings.GlobalizePath(ScopedUserPath()),
                ProjectSettings.GlobalizePath(UserScheme));
        }
    }

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
    /// How many bytes one entry occupies, and nothing else about it.
    ///
    /// A read, and the reason it is here rather than at the caller is the same reason
    /// <see cref="Read"/> is: a caller that resolved its own path would be a caller
    /// outside the containment gate, and measuring a file is enough to say whether it
    /// is there. The settings row that tells a player what their runs take on disk sums
    /// this over the names <c>RecordingLibrary.Index</c> returns.
    ///
    /// An entry that is not there occupies nothing, which is the answer a sum wants: a
    /// recording removed between the listing and the measuring is not a hole in the
    /// figure. A directory is refused for the same reason <see cref="Remove"/> refuses
    /// one - the store holds files, and a caller asking this about a directory is a
    /// caller who thinks it holds something else.
    /// </summary>
    internal static long SizeOf(string relativePath)
    {
        var path = PathOf(relativePath);
        if (Directory.Exists(path))
        {
            throw new InvalidOperationException(
                $"'{relativePath}' is a directory. This store measures files it wrote, one at a time.");
        }

        var file = new FileInfo(path);
        return file.Exists ? file.Length : 0L;
    }

    /// <summary>
    /// The names of the files directly inside one of the store's own directories, in a
    /// stable order, or nothing when that directory is not there yet.
    ///
    /// Names rather than paths, so a caller deciding what to do with them has to come
    /// back through <see cref="PathOf"/> to name one - which is the containment check
    /// again. Files only: the store has no nested libraries and a caller that listed a
    /// directory here would be a caller preparing to walk out of one.
    /// </summary>
    internal static IReadOnlyList<string> ListFileNames(string relativeDirectory)
    {
        var path = PathOf(relativeDirectory);
        return Directory.Exists(path)
            ? [
                .. Directory.EnumerateFiles(path)
                    .Select(Path.GetFileName)
                    .OfType<string>()
                    .Order(StringComparer.Ordinal),
            ]
            : [];
    }

    /// <summary>
    /// Removes one entry, and says whether there was one to remove.
    ///
    /// A deletion is a write and goes through the same gate every other write does: a
    /// traversal, an absolute path and a path inside a game installation are all
    /// refused before anything is opened. It is the one write here that cannot be
    /// undone, which is why it names a file and never a directory - a store that could
    /// be asked to remove a directory is one that can be asked to remove its own root,
    /// and the blast radius of a mistake stops being one file.
    /// </summary>
    internal static bool Remove(string relativePath)
    {
        var path = PathOf(relativePath);
        if (Directory.Exists(path))
        {
            throw new InvalidOperationException(
                $"'{relativePath}' is a directory. This store removes files it wrote, one at a time.");
        }

        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    internal static void RemoveTree(string root, string relativeDirectory)
    {
        var fullRoot = Path.GetFullPath(root);
        var directory = ProtectedInstallPath.RequireUnprotected(
            PathContainment.RequireContained(fullRoot, Path.Combine(fullRoot, relativeDirectory)));
        if (Path.GetRelativePath(fullRoot, directory) == ".")
            throw new PathContainmentException("The store cannot remove its own root.");
        RejectLinkedComponents(fullRoot, directory);
        if (!Directory.Exists(directory)) return;

        var files = new List<string>();
        var directories = new List<string>();
        Audit(directory);
        foreach (var file in files) File.Delete(CheckedEntry(file));
        foreach (var child in directories.OrderByDescending(path => path.Length))
            Directory.Delete(CheckedEntry(child));

        string CheckedEntry(string path) => ProtectedInstallPath.RequireUnprotected(
            PathContainment.RequireContained(directory, path));

        static void RejectLinkedComponents(string allowedRoot, string path)
        {
            var relative = Path.GetRelativePath(allowedRoot, path);
            var current = allowedRoot;
            foreach (var component in relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, component);
                FileSystemInfo entry = Directory.Exists(current)
                    ? new DirectoryInfo(current)
                    : new FileInfo(current);
                if (entry.LinkTarget is not null ||
                    entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new PathContainmentException(
                        $"Linked path component '{entry.FullName}' cannot be removed.");
                }

                if (!entry.Exists) return;
            }
        }

        void Audit(string current)
        {
            var checkedCurrent = CheckedEntry(current);
            if ((File.GetAttributes(checkedCurrent) & FileAttributes.ReparsePoint) != 0)
                throw new PathContainmentException($"Linked directory '{checkedCurrent}' cannot be removed.");
            directories.Add(checkedCurrent);

            foreach (var entry in Directory.EnumerateFileSystemEntries(checkedCurrent))
            {
                var checkedEntry = CheckedEntry(entry);
                var attributes = File.GetAttributes(checkedEntry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new PathContainmentException($"Linked entry '{checkedEntry}' cannot be removed.");
                if ((attributes & FileAttributes.Directory) != 0) Audit(checkedEntry);
                else files.Add(checkedEntry);
            }
        }
    }

    /// <summary>
    /// Where this store lives, as a <c>user://</c> path: the game's own profile scope,
    /// re-rooted under <see cref="UserPath"/>.
    /// </summary>
    private static string ScopedUserPath()
    {
        var saves = SaveManager.Instance
            ?? throw new StoreNotReadyException(
                "This game has no SaveManager, so Runmobile cannot tell whose files these would be.");
        if (!saves.IsProfileInitialized)
        {
            throw new StoreNotReadyException(
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
    ///
    /// <paramref name="userDirectory"/> is where the game says <c>user://</c> is, and
    /// the root has to resolve inside it: the store's own directory being a symlink
    /// elsewhere would otherwise put every contained write outside the game's user
    /// data, where the protected-files ledger cannot see it.
    /// </summary>
    internal static string ResolveRoot(string globalizedPath, string? userDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(globalizedPath))
        {
            throw new InvalidOperationException(
                $"This game did not say where '{UserPath}' is, so Runmobile has nowhere it may write.");
        }

        var root = ProtectedInstallPath.RequireUnprotected(Path.GetFullPath(globalizedPath));
        return userDirectory is null ? root : PathContainment.RequireContained(userDirectory, root);
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

/// <summary>
/// The store saying it cannot yet be asked, because the game has not said whose files
/// these would be.
///
/// A type of its own rather than a message to read, because it is the one failure a
/// surface may name to a player: choosing a save profile resolves it, and it happens
/// on the ordinary path into the main menu. Every other refusal is a fault this mod
/// cannot describe honestly, and a screen that told a player to choose a profile over
/// one would be stating a cause nobody established.
/// </summary>
internal sealed class StoreNotReadyException(string message) : InvalidOperationException(message);

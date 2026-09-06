using System.Globalization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Every file one recording is made of, keyed by the run it is a recording of.
/// </summary>
/// <param name="RunId">The recording's own name, as <see cref="RecordingLibrary.Name"/>
/// composed it.</param>
/// <param name="StartedUtc">When the run began, read back out of that name.</param>
/// <param name="FileNames">The files this recording occupies, in a stable order.
/// A recording that finished has two - a journal and a manifest - and one whose run is
/// still being played, or whose game stopped before the manifest was written, has the
/// journal alone.</param>
public sealed record RecordingFiles(string RunId, DateTime StartedUtc, IReadOnlyList<string> FileNames);

/// <summary>
/// What a directory of recordings holds, and which of them a retention policy removes.
///
/// The recorder writes two files per run and nothing removes them, so a player who
/// keeps playing keeps accumulating - which makes "how many are kept" a real setting
/// and "remove them all" a real thing to ask for. Both are the same question with a
/// different number, which is why <see cref="Cull"/> is one method: keeping none is a
/// purge and keeping fifty is a policy.
///
/// It is here rather than in the mod because it is knowledge about the format - what a
/// recording is called, which files it is made of, and when its run began - and the
/// format has to outlive a build. The mod owns the disk: it lists a directory, hands
/// the names here, and removes what comes back.
///
/// <para><b>A name that is not one of ours is not ours to delete.</b> Every file is
/// matched against the shape <see cref="Name"/> writes and its two known extensions,
/// and anything else - a player's note, a manifest they copied in, a file a later build
/// writes under a name this one does not know - is not in the index and therefore never
/// removed. Refusing rather than approximating is the rule everywhere else in this
/// project; here it is also the difference between a retention policy and a directory
/// wipe.</para>
/// </summary>
public static class RecordingLibrary
{
    /// <summary>What every recording made inside the player's own game is called
    /// first. It says where the recording came from rather than who made it.</summary>
    public const string NamePrefix = "native-";

    /// <summary>The extension a finished recording's manifest carries.</summary>
    public const string ManifestExtension = ".replay.json";

    /// <summary>How the moment a run began is written into its name.</summary>
    private const string StampFormat = "yyyyMMdd-HHmmss";

    /// <summary>The two files a recording is made of, in the order they are listed.
    /// The journal is written as the run is played and the manifest at the end of
    /// it.</summary>
    private static readonly string[] Extensions = [RunJournal.FileExtension, ManifestExtension];

    /// <summary>
    /// How a recording made in the player's own game is named.
    ///
    /// The seed and the moment the run began, and nothing else. It has to be unique
    /// among a player's recordings - two runs on one seed are two recordings - and it
    /// must carry nothing about who made it: no account, no machine, no profile. A
    /// timestamp says when a run was played, which the manifest's own build date
    /// already implies, and says nothing about whose it was.
    ///
    /// Composing and reading the name are one owner on purpose. The moment a run began
    /// is the only ordering a library of recordings has that survives being copied to
    /// another disk, so a second parser written against this shape is a second thing to
    /// get wrong.
    /// </summary>
    public static string Name(string seed, DateTimeOffset startedUtc) =>
        $"{NamePrefix}{Sanitise(seed)}-" +
        startedUtc.UtcDateTime.ToString(StampFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether this is a name <see cref="Name"/> could have written for a run on this
    /// seed.
    ///
    /// Here rather than at a caller, because it is the same knowledge <see cref="Name"/>
    /// has and a second reader of that shape is a second thing to keep in step. What it
    /// buys is a caller that can narrow a directory of recordings to the ones a given run
    /// could be, before opening any of them.
    /// </summary>
    public static bool NamesRunOn(string runId, string seed) =>
        StartedUtc(runId) is not null &&
        runId.StartsWith($"{NamePrefix}{Sanitise(seed)}-", StringComparison.Ordinal);

    /// <summary>
    /// When the run behind a recording began, or null when this is not a name
    /// <see cref="Name"/> wrote.
    /// </summary>
    public static DateTime? StartedUtc(string runId)
    {
        if (runId is null || !runId.StartsWith(NamePrefix, StringComparison.Ordinal)) return null;

        // native-<seed>-<date>-<time>, and the seed is stripped to letters and digits
        // when the name is composed, so there are exactly four parts however odd the
        // seed was.
        var parts = runId.Split('-');
        if (parts.Length != 4) return null;
        if (parts[1].Length == 0 || !parts[1].All(char.IsLetterOrDigit)) return null;

        return DateTime.TryParseExact(
            $"{parts[2]}-{parts[3]}",
            StampFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var started)
            ? started
            : null;
    }

    /// <summary>
    /// The recordings a directory holds, newest first, from the names of the files in
    /// it.
    ///
    /// File names rather than paths, because where the directory is belongs to whoever
    /// listed it. Names that are not a recording's are left out entirely rather than
    /// reported as an unknown kind: nothing downstream has anything to do with them.
    /// </summary>
    public static IReadOnlyList<RecordingFiles> Index(IEnumerable<string> fileNames)
    {
        var files = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var fileName in fileNames)
        {
            if (RunIdOf(fileName) is not { } runId) continue;
            if (!files.TryGetValue(runId, out var owned)) files[runId] = owned = [];
            owned.Add(fileName);
        }

        return
        [
            .. files
                .Select(entry => new RecordingFiles(
                    entry.Key,
                    StartedUtc(entry.Key)!.Value,
                    [.. entry.Value.Order(StringComparer.Ordinal)]))
                .OrderByDescending(recording => recording.StartedUtc)
                .ThenBy(recording => recording.RunId, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// The recordings a policy of keeping <paramref name="keepNewest"/> of them
    /// removes, newest of those first.
    ///
    /// One method for the policy and for the purge, because they are the same question:
    /// keeping none removes every recording there is, and a negative number names
    /// nothing at all, which is how a caller with no readable policy asks for no
    /// removals. Nothing is deleted here - this says which files a caller that owns
    /// the disk may remove.
    /// </summary>
    public static IReadOnlyList<RecordingFiles> Cull(IEnumerable<string> fileNames, int keepNewest) =>
        keepNewest < 0 ? [] : [.. Index(fileNames).Skip(keepNewest)];

    /// <summary>
    /// Which recording a file belongs to, or null when it belongs to none.
    ///
    /// The extension is matched first and the name that is left has to be one
    /// <see cref="Name"/> could have written, so a file named for a recording with an
    /// extension this build does not know, and a file with a known extension under a
    /// name this build cannot read, are both left where they are.
    /// </summary>
    private static string? RunIdOf(string fileName)
    {
        foreach (var extension in Extensions)
        {
            if (!fileName.EndsWith(extension, StringComparison.Ordinal)) continue;
            var runId = fileName[..^extension.Length];
            return StartedUtc(runId) is null ? null : runId;
        }

        return null;
    }

    private static string Sanitise(string seed) =>
        new string([.. seed.Where(char.IsLetterOrDigit)]) is { Length: > 0 } cleaned ? cleaned : "unseeded";
}

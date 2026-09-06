using MegaCrit.Sts2.Core.Logging;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// What the library reads off this computer, and the one thing it writes.
///
/// Every path goes through <see cref="RunmobileStore"/>, which is the mod's one
/// writer and the one place containment is checked. This adds no path rule of its
/// own: it names entries under the recorder's own directory and asks the store for
/// them.
///
/// <para><b>A recording is read, never inferred from its name.</b> Which files are a
/// recording's is <see cref="RecordingLibrary"/>'s answer - the same index the retention
/// policy culls by, so a run the library lists and a run retention counts are the same
/// set - and what a row <em>says</em> comes from reading the manifest itself. A file
/// whose name looks like a recording and whose contents this build cannot read is
/// skipped with a line in the log, rather than listed as a run nobody can open: the
/// failure mode this project exists to prevent is a surface that shows something
/// plausible instead of refusing.</para>
///
/// <para>Two writes and no others: the progress record, which holds fight ordinals and
/// nothing else, and the verdict cache. See <see cref="RunProgress"/> for why the first
/// can never become a resume, and <see cref="RunVerdictCache"/> for why the second is a
/// hint about a menu button and never evidence about a run.</para>
/// </summary>
internal static class RunLibraryStore
{
    /// <summary>Where the recorder puts what it writes. Named from the recorder rather
    /// than repeated, so one directory has one name.</summary>
    internal static string RecordingsDirectory => RunRecorder.RecordingsDirectory;

    /// <summary>
    /// Every recording of the player's own that this build can read, newest run first,
    /// with when its run began.
    ///
    /// Every one of them is deserialized, which is what makes this expensive at the
    /// retention default of fifty: a caller that only needs to know whether anything is
    /// playable answers before it gets here. See <see cref="RunLibrary.HasAnythingToShow"/>.
    ///
    /// The time is the run's own start, read back out of the recording's name by the
    /// owner that composed it, rather than the file's timestamp on this disk. That is
    /// the ordering a library keeps when it is copied to another machine, which a
    /// modification time is not.
    /// </summary>
    internal static IReadOnlyList<StoredRecording> MyRecordings()
    {
        var recordings = new List<StoredRecording>();
        foreach (var indexed in RecordingLibrary.Index(RunmobileStore.ListFileNames(RecordingsDirectory)))
        {
            if (ManifestNameOf(indexed) is not { } fileName) continue;

            var entry = $"{RecordingsDirectory}/{fileName}";
            try
            {
                var json = RunmobileStore.Read(entry);
                if (json is null) continue;
                recordings.Add(new StoredRecording(
                    ManifestJson.Deserialize(json),
                    new DateTimeOffset(indexed.StartedUtc, TimeSpan.Zero)));
            }
            catch (Exception ex)
            {
                // Named rather than counted: a player whose recording this build cannot
                // read should be able to say which one from the log.
                Log.Error(
                    $"[{RunmobileMod.ModId}] skipping {entry}, which this build cannot read: " +
                    $"{ex.GetType().Name}: {ex.Message}", 2);
            }
        }

        return recordings;
    }

    /// <summary>
    /// The run ids of every finished recording on this computer, newest run first.
    ///
    /// Read off the directory index and nothing else, so this deserializes no manifest
    /// at all - which is what makes the Compendium's per-open question affordable. A run
    /// still being played has a journal and no manifest and is not one of these.
    /// </summary>
    internal static IReadOnlyList<string> StoredRunIds() =>
        [
            .. RecordingLibrary.Index(RunmobileStore.ListFileNames(RecordingsDirectory))
                .Where(recording => ManifestNameOf(recording) is not null)
                .Select(recording => recording.RunId),
        ];

    /// <summary>
    /// One recording of the player's own, by its run id, or null when this computer has
    /// no readable recording of that run.
    ///
    /// One file read rather than the whole set: the id names the recording in the
    /// directory index, so opening a run costs the manifest of that run and no other.
    /// </summary>
    internal static ReplayManifest? RecordingFor(string runId)
    {
        var indexed = RecordingLibrary.Index(RunmobileStore.ListFileNames(RecordingsDirectory))
            .FirstOrDefault(recording => string.Equals(recording.RunId, runId, StringComparison.Ordinal));
        if (indexed is null || ManifestNameOf(indexed) is not { } fileName) return null;

        var entry = $"{RecordingsDirectory}/{fileName}";
        try
        {
            var json = RunmobileStore.Read(entry);
            if (json is null) return null;

            // The recorder names a recording's files after the run they record, so the
            // two agree. Where they do not, this is not that run: answering with it
            // would stand somebody in a run they did not ask for.
            var recording = ManifestJson.Deserialize(json);
            return string.Equals(recording.RunId, runId, StringComparison.Ordinal) ? recording : null;
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] skipping {entry}, which this build cannot read: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
            return null;
        }
    }

    /// <summary>
    /// What the player's own runs occupy on this computer: every file the recorder
    /// wrote, journals included, because that is what a player would find if they went
    /// and looked.
    ///
    /// Counted over the index rather than over the directory, so the number under the
    /// list is a size of exactly the files "Keep or remove them in Settings" would
    /// remove. A note a player left in there is not a run and is not theirs to have
    /// counted against them.
    /// </summary>
    internal static long MyRunsBytes() =>
        RecordingLibrary.Index(RunmobileStore.ListFileNames(RecordingsDirectory))
            .SelectMany(recording => recording.FileNames)
            .Sum(name => RunmobileStore.SizeOf($"{RecordingsDirectory}/{name}"));

    /// <summary>
    /// The fights this player has played from, per recording.
    ///
    /// A record this build cannot read is forgotten rather than guessed at: the cost is
    /// a player seeing "Continue: play from fight 1", and the cost of guessing is a
    /// screen that lies about what they have played.
    /// </summary>
    internal static RunProgress ReadProgress()
    {
        try
        {
            return RunProgress.Read(RunmobileStore.Read(RunProgress.FileName));
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not read {RunProgress.FileName}, so this session starts " +
                $"from no progress: {ex.GetType().Name}: {ex.Message}", 2);
            return RunProgress.Empty;
        }
    }

    /// <summary>
    /// What this game has judged about each recording, per build.
    ///
    /// A cache this build cannot read is forgotten rather than guessed at, the same
    /// direction the progress record fails in: the cost is a menu button that waits
    /// until the browser has been opened once. See <see cref="RunVerdictCache"/> for why
    /// nothing a player reads about a run may come from it.
    /// </summary>
    internal static RunVerdictCache ReadVerdicts()
    {
        try
        {
            return RunVerdictCache.Read(RunmobileStore.Read(RunVerdictCache.FileName));
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not read {RunVerdictCache.FileName}, so the Compendium " +
                $"card waits for the browser: {ex.GetType().Name}: {ex.Message}", 2);
            return RunVerdictCache.Empty;
        }
    }

    /// <summary>
    /// Records the verdicts the browser reached this time it was opened, and says
    /// whether anything changed.
    ///
    /// Only what was judged, because an entry is a record that a judgement happened.
    /// Nothing here is load-bearing: losing this file costs a menu button one browser
    /// open, so a write that fails is logged and swallowed.
    /// </summary>
    internal static bool RecordVerdicts(string build, IReadOnlyDictionary<string, RunVerdict> judged)
    {
        if (judged.Count == 0) return false;

        try
        {
            var cache = ReadVerdicts();
            var updated = cache.WithJudged(build, judged);
            if (ReferenceEquals(updated, cache)) return false;
            RunmobileStore.Write(RunVerdictCache.FileName, updated.Write());
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not record what this game judged: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
            return false;
        }
    }

    private static string? ManifestNameOf(RecordingFiles recording) =>
        recording.FileNames.FirstOrDefault(name =>
            name.EndsWith(RecordingLibrary.ManifestExtension, StringComparison.Ordinal));

    /// <summary>
    /// Records that this player has played from one fight of one recording, and says
    /// whether anything changed.
    ///
    /// Nothing here is load-bearing for a replay: losing this file loses the pips and
    /// the number in Continue, and nothing else. So a write that fails is logged and
    /// swallowed rather than taking a player out of the fight they were about to
    /// enter.
    /// </summary>
    internal static bool RecordFightPlayed(string runId, int fight)
    {
        try
        {
            var progress = ReadProgress();
            var updated = progress.WithFightPlayed(runId, fight);
            if (ReferenceEquals(updated, progress)) return false;
            RunmobileStore.Write(RunProgress.FileName, updated.Write());
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not record progress through {runId}: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
            return false;
        }
    }

}

/// <summary>One recording on this computer: what it says, and when its run
/// began.</summary>
internal sealed record StoredRecording(ReplayManifest Recording, DateTimeOffset? Started);

using MegaCrit.Sts2.Core.Logging;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// What the library reads off this computer, and the one thing it writes.
///
/// Every path goes through <see cref="RunmobileStore"/>, which is the mod's one
/// writer and the one place containment is checked. This adds no path rule of its
/// own: it names entries under the recorder's own directory and asks the store for
/// them.
///
/// <para><b>A recording is read, never inferred from its name.</b> The library lists
/// the recorder's directory, keeps the manifests, and reads each one - so what a row
/// says is what the recording says. A file whose name looks like a recording and whose
/// contents this build cannot read is skipped with a line in the log, rather than
/// listed as a run nobody can open: the failure mode this project exists to prevent is
/// a surface that shows something plausible instead of refusing.</para>
///
/// <para>The one write is the progress record, which holds fight ordinals and nothing
/// else. See <see cref="RunProgress"/> for why it can never become a resume.</para>
/// </summary>
internal static class RunLibraryStore
{
    /// <summary>Where the recorder puts what it writes. Named from the recorder rather
    /// than repeated, so one directory has one name.</summary>
    internal static string RecordingsDirectory => RunRecorder.RecordingsDirectory;

    /// <summary>The extension a finished recording's manifest carries.</summary>
    internal const string ManifestExtension = ".replay.json";

    /// <summary>The file names under the recorder's directory that are finished
    /// recordings. A run still being played has a journal and no manifest, and is not
    /// a run anybody can play from yet.</summary>
    internal static IReadOnlyList<string> ManifestFileNames() =>
        [
            .. RunmobileStore.ListFileNames(RecordingsDirectory)
                .Where(name => name.EndsWith(ManifestExtension, StringComparison.Ordinal)),
        ];

    /// <summary>
    /// Every recording of the player's own that this build can read, with when its
    /// file was last written.
    ///
    /// The write time is the library's ordering and nothing else. It is a fact about
    /// the file on this disk rather than about the run, which is why it never travels:
    /// nothing exported carries it.
    /// </summary>
    internal static IReadOnlyList<StoredRecording> MyRecordings()
    {
        var recordings = new List<StoredRecording>();
        foreach (var fileName in ManifestFileNames())
        {
            var entry = $"{RecordingsDirectory}/{fileName}";
            try
            {
                var json = RunmobileStore.Read(entry);
                if (json is null) continue;
                recordings.Add(new StoredRecording(
                    ManifestJson.Deserialize(json),
                    LastWritten(entry),
                    RunmobileStore.SizeOf(entry)));
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

    /// <summary>What the player's own runs occupy on this computer: every file the
    /// recorder wrote, journals included, because that is what a player would find if
    /// they went and looked.</summary>
    internal static long MyRunsBytes() =>
        RunmobileStore.ListFileNames(RecordingsDirectory)
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

    private static DateTimeOffset? LastWritten(string entry)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(RunmobileStore.PathOf(entry)), TimeSpan.Zero);
        }
        catch (Exception)
        {
            // A run whose time nobody could read sorts last rather than being given one.
            return null;
        }
    }
}

/// <summary>One recording on this computer: what it says, when its file was last
/// written, and what it costs.</summary>
internal sealed record StoredRecording(
    ReplayManifest Recording, DateTimeOffset? LastWritten, long Bytes);

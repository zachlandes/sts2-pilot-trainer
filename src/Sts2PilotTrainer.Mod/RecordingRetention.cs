using System.Globalization;
using MegaCrit.Sts2.Core.Logging;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// What this mod does with the disk space its own recordings take, and the player's
/// way of taking it back.
///
/// The recorder writes two real files per run into the player's user directory and
/// nothing ever removed one, so a player who keeps playing keeps accumulating. Two
/// answers, and they are the same operation with a different number:
/// <c>keep_recent_runs</c> is a standing policy and <c>purge_my_runs</c> is a one-shot
/// act that keeps none. <see cref="RecordingLibrary.Cull"/> decides which recordings
/// either one names; this owns the disk and the moment.
///
/// <para><b>The moment is the mod's first chosen save profile, and that is not an
/// accident.</b> It cannot be mod start: the game has no chosen save profile then, so
/// the store cannot yet say whose files these are, and asking it throws. That is the
/// whole of the condition - whether the engine layer could adopt this game is a
/// different question, and a player who asked for their runs to be removed is answered
/// either way. It runs once for each save profile this process plays as - the store is
/// resolved per operation and two profiles do not share a library, so a policy applied
/// to one says nothing about the other - and it latches a profile only once it has
/// actually run against it, so a call made before the store could answer is retried at
/// the next one rather than swallowed. <see cref="RunmobileMod.EnsureAdopted"/> is where it is called
/// from, and no journal is being appended to when it runs: the recorder opens one only
/// after asking the same gate, and it has let go of the run it was recording before the
/// singleplayer menu this is called from can be reached again.</para>
///
/// <para><b>It removes recordings, and nothing else.</b> Every file it names came back
/// from <see cref="RecordingLibrary"/> as part of a recording this build recognises,
/// and every removal goes through <see cref="RunmobileStore.Remove"/>, which refuses
/// anything outside the store the same way a write does. A save, a profile, run
/// history, another mod's files and a file a player put in the recordings directory
/// themselves are all outside what this can name.</para>
///
/// <para>A consequence, stated rather than hidden: a run the player saved and has not
/// finished is a recording like any other, so a cap small enough to reach it removes
/// the journal of a run still on the game's Continue. Continuing that run afterwards
/// starts a journal that did not witness the run's start, and the recorder refuses such
/// a recording rather than publishing it. The default keeps fifty runs, which puts that
/// out of reach of anybody who has not asked for it.</para>
/// </summary>
internal static class RecordingRetention
{
    private static readonly Lock Gate = new();

    private static readonly HashSet<string> Applied = new(StringComparer.Ordinal);

    /// <summary>
    /// Applies the player's policy once for the save profile this game is running as,
    /// and says nothing at all when there was nothing to do.
    ///
    /// The profile is the store's own answer and is asked for here the same way an
    /// operation asks: a player who switches profile gets their second profile's
    /// settings honoured against their second profile's recordings, because the first
    /// one having been answered says nothing about it.
    ///
    /// Failure is reported and not latched. The store throwing here means the profile
    /// was not ready or the disk refused, both of which the next adopted moment may
    /// answer differently, and neither of which is a reason to take the mod down.
    /// </summary>
    internal static void ApplyOnce()
    {
        lock (Gate)
        {
            string root;
            try
            {
                root = RunmobileStore.Root;
                if (Applied.Contains(root)) return;
                Apply(RunmobileSettings.Read());
            }
            catch (Exception ex)
            {
                Log.Error(
                    $"[{RunmobileMod.ModId}] could not apply what settings.json says about keeping your " +
                    $"runs: {ex.GetType().Name}: {ex.Message}", 2);
                return;
            }

            Applied.Add(root);
        }
    }

    /// <summary>
    /// Removes what <paramref name="settings"/> says to remove, and returns how many
    /// runs went.
    ///
    /// The count is of runs this call removed a file of, not of runs the library
    /// named: a recording that was already gone by the time the delete reached it - a
    /// second process on the same profile, or a player emptying the directory by hand
    /// - is not something to tell them was removed.
    ///
    /// A purge clears its own request afterwards rather than before: a game that
    /// stopped part way through finishes the job at the next launch, which is the
    /// direction a player who asked for everything to go wants it to fail in.
    /// </summary>
    internal static int Apply(RunmobileSettings settings)
    {
        var keep = settings.PurgeMyRuns ? 0 : settings.KeepRecentRuns;
        var removing = RecordingLibrary.Cull(
            RunmobileStore.ListFileNames(RunRecorder.RecordingsDirectory), keep);

        var removed = 0;
        foreach (var recording in removing)
        {
            var went = false;
            foreach (var file in recording.FileNames)
            {
                went |= RunmobileStore.Remove($"{RunRecorder.RecordingsDirectory}/{file}");
            }

            if (went) removed++;
        }

        if (settings.PurgeMyRuns) RunmobileSettings.ClearPurgeRequest();

        Announce(settings, removed);
        return removed;
    }

    /// <summary>
    /// What the player reads in the game's log about their own files.
    ///
    /// Said whenever something was removed, and said for a purge even when there was
    /// nothing to remove - somebody asked for an act and deserves to know it happened,
    /// where a policy that had nothing to do is not news.
    /// </summary>
    private static void Announce(RunmobileSettings settings, int removed)
    {
        var count = removed.ToString(CultureInfo.InvariantCulture);
        if (settings.PurgeMyRuns)
        {
            Log.Info(
                $"[{RunmobileMod.ModId}] purged your recorded runs: {count} removed. purge_my_runs is back " +
                "off in settings.json.", 2);
            return;
        }

        if (removed == 0) return;

        Log.Info(
            $"[{RunmobileMod.ModId}] keeping your " +
            $"{settings.KeepRecentRuns.ToString(CultureInfo.InvariantCulture)} most recent runs: {count} " +
            "older one(s) removed.", 2);
    }

    /// <summary>Lets a test run more than one policy against one store. Nothing in the
    /// mod calls it: a player's process applies each profile's policy once.</summary>
    internal static void ForgetForTesting()
    {
        lock (Gate) Applied.Clear();
    }
}

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
/// <para><b>The moment is the mod's first adopted game, and that is not an
/// accident.</b> It cannot be mod start: the game has no chosen save profile then, so
/// the store cannot yet say whose files these are.
/// <see cref="RunmobileMod.EnsureAdopted"/> is the mod's one "there is demonstrably a
/// running game" gate and every path that reaches the store goes through it first -
/// the recorder asks it before it computes a journal path at all - so a removal here
/// can never race a journal being appended to. It runs once per process, and it latches
/// only once it has actually run, so a call made before the store could answer is
/// retried at the next one rather than swallowed.</para>
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

    private static bool _applied;

    /// <summary>
    /// Applies the player's policy once in this process, and says nothing at all when
    /// there was nothing to do.
    ///
    /// Failure is reported and not latched. The store throwing here means the profile
    /// was not ready or the disk refused, both of which the next adopted moment may
    /// answer differently, and neither of which is a reason to take the mod down.
    /// </summary>
    internal static void ApplyOnce()
    {
        lock (Gate)
        {
            if (_applied) return;

            try
            {
                Apply(RunmobileSettings.Read());
            }
            catch (Exception ex)
            {
                Log.Error(
                    $"[{RunmobileMod.ModId}] could not apply what settings.json says about keeping your " +
                    $"runs: {ex.GetType().Name}: {ex.Message}", 2);
                return;
            }

            _applied = true;
        }
    }

    /// <summary>
    /// Removes what <paramref name="settings"/> says to remove, and returns how many
    /// runs went.
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

        foreach (var file in removing.SelectMany(recording => recording.FileNames))
        {
            RunmobileStore.Remove($"{RunRecorder.RecordingsDirectory}/{file}");
        }

        if (settings.PurgeMyRuns) (settings with { PurgeMyRuns = false }).Save();

        Announce(settings, removing.Count);
        return removing.Count;
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
    /// mod calls it: a player's process applies the policy once.</summary>
    internal static void ForgetForTesting()
    {
        lock (Gate) _applied = false;
    }
}

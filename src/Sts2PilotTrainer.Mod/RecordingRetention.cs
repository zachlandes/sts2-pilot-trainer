using System.Globalization;
using MegaCrit.Sts2.Core.Logging;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

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
/// either one names; this owns the disk and the moment. It also owns the reading a
/// player is shown of that disk - <see cref="OnDisk"/> - because a surface that listed
/// the directory for itself would be a second thing to keep in step with the removal.
///
/// <para><b>The unasked-for moment is the mod's first chosen save profile, and that is
/// not an accident.</b> A player pressing Remove on the settings row is the other
/// moment and answers to none of what follows: it is a person acting, through
/// <see cref="PurgeNow"/>, and it happens where they pressed. It cannot be mod start: the game has no chosen save profile then, so
/// the store cannot yet say whose files these are, and asking it throws. That is the
/// whole of the condition - whether the engine layer could adopt this game is a
/// different question, and a player who asked for their runs to be removed is answered
/// either way. It runs once for each save profile this process plays as - the store is
/// resolved per operation and two profiles do not share a library, so a policy applied
/// to one says nothing about the other - and it latches a profile only once it has
/// actually run against it, so a call made before the store could answer is retried at
/// the next one rather than swallowed. Two callers ask, and neither is behind an
/// adoption verdict: the singleplayer-menu patch asks first and unconditionally, ahead
/// of any question about which modules contributed a card, and
/// <see cref="RunmobileMod.EnsureAdopted"/> asks again so the recorder's own first
/// adopted moment is covered. No journal is being appended to when it runs: the
/// recorder opens one only after passing that adoption gate, and it has let go of the
/// run it was recording before the singleplayer menu can be reached again.</para>
///
/// <para><b>It removes recordings, and nothing else.</b> Every file it names came back
/// from <see cref="RecordingLibrary"/> as part of a recording this build recognises,
/// and every removal goes through <see cref="RunmobileStore.Remove"/>, which refuses
/// anything outside the store the same way a write does. A save, a profile, run
/// history, another mod's files and a file a player put in the recordings directory
/// themselves are all outside what this can name.</para>
///
/// <para><b>The run the game can currently Continue is never named.</b> Neither a cap
/// nor a purge removes its journal, because a run whose journal went missing under it
/// is one the recorder would pick up again at its next room and record as a run it had
/// watched from the start - a claim about what was observed that nobody established.
/// One file left where a player asked for everything to go is the smaller wrong, and
/// the log says it was left. Which run that is comes from
/// <see cref="ContinuableRun"/>, which asks the game and refuses where it cannot
/// answer.</para>
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
                Apply(RunmobileSettings.Read(), ContinuableRun.StartedUtc());
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
    /// What the player's recorded runs are on this disk right now: how many, what they
    /// take, and the policy standing over them.
    ///
    /// Here because this is already the one place that knows where recordings live and
    /// which files each is made of. A settings row that listed the directory itself
    /// would be a second thing to keep in step with the removal, and the two would be
    /// read a frame apart.
    ///
    /// It measures the files <see cref="RecordingLibrary"/> recognises and no others, so
    /// the figure is what this mod's own runs occupy rather than what is in the
    /// directory - a file a player put there themselves is not this mod's to count, for
    /// the same reason it is not this mod's to delete.
    ///
    /// The policy is the player's own where their file could be read, and the default
    /// standing in for it where it could not. Which of the two it is travels with it,
    /// because the row may neither name the sentinel nor be written from while the file
    /// it would write into is one this build refuses.
    /// </summary>
    internal static MyRunsFacts OnDisk()
    {
        var recordings = RecordingLibrary.Index(
            RunmobileStore.ListFileNames(RunRecorder.RecordingsDirectory));
        var bytes = recordings
            .SelectMany(recording => recording.FileNames)
            .Sum(file => RunmobileStore.SizeOf($"{RunRecorder.RecordingsDirectory}/{file}"));

        var settings = RunmobileSettings.Read();
        return new MyRunsFacts(
            recordings.Count,
            bytes,
            settings.Readable ? settings.KeepRecentRuns : RunmobileSettings.DefaultKeepRecentRuns,
            SettingsReadable: settings.Readable);
    }

    /// <summary>
    /// Removes every recorded run, now, because the player just asked for it on a
    /// screen - and returns how many went.
    ///
    /// The same operation as a purge requested in the file, reached the other way
    /// round. It is written to the file first and applied second, so a game that stops
    /// in between finishes at the next main menu rather than forgetting that anybody
    /// asked; <see cref="Apply"/> then takes the request back out, which is what stops
    /// it repeating for ever.
    ///
    /// Not behind the once-per-profile latch <see cref="ApplyOnce"/> keeps, and not
    /// latching one: that latch is there so a standing policy is applied once as a
    /// profile is entered, and this is a person pressing a control. It refuses rather
    /// than approximating - the store, the settings file and the game's answer about the
    /// continuable run all throw here rather than returning a guess - so the row that
    /// called it can say nothing happened instead of showing a receipt for work that did
    /// not happen.
    /// </summary>
    internal static int PurgeNow()
    {
        lock (Gate)
        {
            var continuable = ContinuableRun.StartedUtc();
            RunmobileSettings.RequestPurge();
            return Apply(RunmobileSettings.Read() with { PurgeMyRuns = true }, continuable);
        }
    }

    /// <summary>
    /// Removes what <paramref name="settings"/> says to remove, and returns how many
    /// runs went.
    ///
    /// The recording of the run this game can continue is not among them, whatever
    /// <paramref name="continuableRunStartedUtc"/> says is continuable; null says
    /// there is no such run.
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
    internal static int Apply(RunmobileSettings settings, DateTime? continuableRunStartedUtc)
    {
        var keep = settings.PurgeMyRuns ? 0 : settings.KeepRecentRuns;
        var named = RecordingLibrary.Cull(
            RunmobileStore.ListFileNames(RunRecorder.RecordingsDirectory), keep);
        var removing = continuableRunStartedUtc is { } continuable
            ? named.Where(recording => recording.StartedUtc != continuable).ToList()
            : named;

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

        Announce(settings, removed, named.Count - removing.Count);
        return removed;
    }

    /// <summary>
    /// What the player reads in the game's log about their own files.
    ///
    /// Said whenever something was removed, and said for a purge even when there was
    /// nothing to remove - somebody asked for an act and deserves to know it happened,
    /// where a policy that had nothing to do is not news.
    /// </summary>
    private static void Announce(RunmobileSettings settings, int removed, int keptContinuable)
    {
        var count = removed.ToString(CultureInfo.InvariantCulture);
        var kept = keptContinuable > 0
            ? " The run you can still continue was left, so continuing it stays a recording of a run this " +
              "mod watched from the start."
            : string.Empty;
        if (settings.PurgeMyRuns)
        {
            Log.Info(
                $"[{RunmobileMod.ModId}] purged your recorded runs: {count} removed. purge_my_runs is back " +
                $"off in settings.json.{kept}", 2);
            return;
        }

        if (removed == 0) return;

        Log.Info(
            $"[{RunmobileMod.ModId}] keeping your " +
            $"{settings.KeepRecentRuns.ToString(CultureInfo.InvariantCulture)} most recent runs: {count} " +
            $"older one(s) removed.{kept}", 2);
    }

    /// <summary>
    /// Lets the next singleplayer menu apply the standing policy again for the save
    /// profile this game is running as.
    ///
    /// The latch above exists so a policy is applied once as a profile is entered, and
    /// a policy the player has just changed has not been applied at all. Without this a
    /// row that says runs will go at the next main menu is describing the next launch:
    /// the latch is already closed for this profile and swallows the call.
    ///
    /// Nothing is removed here, which is the point of forgetting rather than applying:
    /// a standing policy acts at the main menu, and a screen that deleted files as the
    /// number moved would be performing the act it is describing.
    ///
    /// This profile only. Another profile's policy was applied against its own
    /// recordings, and a write made while playing as this one says nothing about it.
    /// </summary>
    internal static void ReapplyPolicyAtNextMenu()
    {
        lock (Gate) Applied.Remove(RunmobileStore.Root);
    }

    /// <summary>Lets a test run more than one policy against one store. Nothing in the
    /// mod calls it: a player's process applies each profile's policy once.</summary>
    internal static void ForgetForTesting()
    {
        lock (Gate) Applied.Clear();
    }
}

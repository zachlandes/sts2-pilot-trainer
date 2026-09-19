using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// Watches the player play their own run and writes it down as a native recording.
///
/// It is the recorded-fight journey's observer widened to a whole run. The same principle
/// holds: it issues nothing, decides nothing, and changes nothing about the run. Every
/// patch here reads its arguments and returns; the run saves normally, the write
/// barrier is never raised, and the only thing written is this mod's own journal and
/// manifest under <see cref="RunmobileStore"/>.
///
/// What it watches is <see cref="EngineCommands"/> read from the other end. The driver
/// calls those members to make a recorded decision; a player clicking makes the game
/// call the same members, and this is there when it does. That shared table is what
/// keeps the two halves from drifting: a verb the driver can replay is a verb this can
/// record, and <c>RunRecorderTests</c> asserts the two lists are the same.
///
/// The rules about what any of it means are not here. <see cref="RunCapture"/> owns the
/// recording - the ordering, the per-fight delegation, the boundaries, the continuity
/// question - and this owns three things a pure class cannot: which game member
/// corresponds to which decision, what its arguments are, and <em>when</em> the engine
/// has settled enough for a reading to be worth taking. That last one is the same job
/// <see cref="PlayerFightObserver"/> does inside a fight, which is why the fight is
/// handed to that observer rather than watched a second way here.
/// </summary>
internal sealed class RunRecorder : IDisposable
{
    /// <summary>How long to wait for the engine to finish what a decision started.
    /// The same budget the headless drain and the fight observer give.</summary>
    internal const double SettleBudgetSeconds = 30.0;

    internal const double SettlePollSeconds = 0.05;

    /// <summary>How long to wait for a run to exist after the game said one was
    /// starting. Generous, because continuing a saved run loads a scene.</summary>
    private const double AttachBudgetSeconds = 60.0;

    /// <summary>Where a recording lives in the store, under the profile scope.</summary>
    internal const string RecordingsDirectory = "recordings";

    private static readonly Lock Gate = new();

    /// <summary>Whether the console was used before this recorder attached to the run
    /// it is being started for. Applied to the capture at attach, because a command
    /// used in that stretch is in the run's history and nothing later could recover it.
    /// A static field of a framework type, so this type still lays out before the
    /// sibling assemblies are resolvable - see docs/in-game-host.md.</summary>
    private static bool _consoleUsedBeforeAttach;

    private readonly RunCapture _capture;
    private readonly string _journalPath;
    private readonly Queue<PendingDecision> _pending = new();
    private readonly List<ScreenAnswer> _screenAnswers = [];
    private readonly List<PendingSave> _saves = [];

    private PlayerFightObserver? _observer;

    /// <summary>The in-fight action the observer has opened and not yet closed, held
    /// by name rather than as an <see cref="ActionVerb"/>: a value of a sibling
    /// assembly's type in a field here decides this type's layout, and the game loads
    /// this assembly's types before this mod can say where that sibling is. It
    /// carries the reading taken when the action began, which is the decision's
    /// before-state.</summary>
    private (string Verb, IReadOnlyDictionary<string, string> Args, TakenReading Before)? _openFightStep;

    private bool _pumping;
    private bool _disposed;
    private bool _finished;

    /// <summary>
    /// Every reading a decision begins from takes a ticket, issued in order, and the
    /// ticket is open from the prefix that read it until the pump has dealt with the
    /// decision. A save the game asks for while any ticket is open is placed on the
    /// latest one - the member executing is the one most recently read - and is
    /// written once that decision is. Two decisions can be open at once: the loot
    /// screen's skip is still settling when the map node is pressed, and the
    /// arrival's save was placed on the skip by a rule that knew only "something is
    /// in flight".
    /// </summary>
    private long _tickets;

    private readonly HashSet<long> _openTickets = [];

    internal RunRecorder(RunCapture capture, string journalPath)
    {
        _capture = capture;
        _journalPath = journalPath;
    }

    /// <summary>The run being recorded right now, or null when none is.</summary>
    internal static RunRecorder? Active { get; private set; }

    /// <summary>
    /// Whether the game's identity can be read for a recording in this process, and how.
    ///
    /// In the retail client the recorder attaches only once the mod has adopted the
    /// running game, because until then the build and content it would write down come
    /// out of the prepared copy on disk rather than out of the game the run is being
    /// played in - three true values from a source nobody established. That is what
    /// <see cref="RunmobileMod.EnsureAdopted"/> asks, and it is the default.
    ///
    /// A headless process is the other host, and it reaches this exactly once - from a
    /// test that drives the recorder over a real headless engine. There <em>is</em> no
    /// running game to adopt, and <c>AdoptRunningGame</c> refuses a process that started
    /// its own headless engine by design; but the identity <see cref="LiveRun"/> writes
    /// there comes from the prepared copy honestly, because that is the engine this
    /// process is actually running. So a headless attach supplies a source that says the
    /// engine is up rather than one that adopts, and the recorder reads its identity the
    /// same way the arbiter does. Reset to the default when the test is done, the way
    /// the store's test root is.
    /// </summary>
    internal static Func<bool> GameIdentitySource { get; set; } = RunmobileMod.EnsureAdopted;

    /// <summary>
    /// How this process waits for the engine to settle after a decision, supplied by the
    /// attach site. The retail client's is the scene tree's own timer; a headless attach
    /// supplies one driven by the arbiter's drain, because there are no frames to wait on.
    /// See <see cref="SettleClock"/>.
    /// </summary>
    internal static SettleClock Clock { get; set; } = SettleClock.SceneTree;

    /// <summary>Puts the process-wide seams back to the retail defaults, for a headless
    /// test that set them.</summary>
    internal static void ResetHostSeamsForTesting()
    {
        GameIdentitySource = RunmobileMod.EnsureAdopted;
        Clock = SettleClock.SceneTree;
    }

    /// <summary>What the capture holds, for a test and for a log line.</summary>
    internal RunCapture Capture => _capture;

    /// <summary>
    /// Whether the recorder is ready for the next decision: every decision announced
    /// has been read after, and inside a fight the observer is watching.
    ///
    /// The one reading a driver paces on, exposed so that no driver reads the queue
    /// and the observer reflectively. It is a courtesy the retail soak extends to the
    /// recorder rather than a rule the recorder needs: a decision made before this is
    /// true is closed or refused by <see cref="CloseStrandedDecisions"/> and
    /// <see cref="WatchTheFightBefore"/> either way, and a driver that waits for it
    /// never meets the refusal. A decision whose work has handed the run to the player
    /// - a card screen, a bundle or relic screen, or a card prompt up - counts as
    /// settled, because it is answered by the player's next action and settles once
    /// that answer is given; a driver waiting for it to settle first would wait for
    /// ever. With no recording live there is nothing to wait for.
    /// </summary>
    internal static bool Settled
    {
        get
        {
            var recorder = Active;
            if (recorder is null || recorder._finished) return true;

            bool settling;
            lock (Gate) settling = recorder._pending.Any(decision => decision.ReadAfter is null);
            if (settling && !AScreenIsWaitingOnThePlayer()) return false;

            return !InAFight() || recorder._observer is not null;
        }
    }

    /// <summary>Whether the run has been handed to the player inside a decision's own
    /// work, on a surface the recorder holds open: the count the settle stands down
    /// for, plus the card prompt, whose answer is held rather than announced.</summary>
    private static bool AScreenIsWaitingOnThePlayer() =>
        CardScreensUp.Count > 0 || BundleScreen.Open is not null || RelicScreen.Open is not null ||
        CardPrompts.Open is not null;

    /// <summary>Where this recording's journal is being written.</summary>
    internal string JournalPath => _journalPath;


    // ── Lifecycle ────────────────────────────────────────────────────────────────

    /// <summary>
    /// A run exists. Attaches to it, or says in the log why it will not.
    ///
    /// The same entry point for a new run and for one continued from the game's own
    /// save, because the difference is not this method's to know: a run is identified
    /// by its seed and the moment it began, both of which survive a reload, so whether
    /// there is already a journal for it is the question - and the answer is on disk.
    /// </summary>
    internal static void NoticeRun()
    {
        try
        {
            if (Active is not null) return;
            if (ProfileWriteBarrier.IsActive)
            {
                // A trainer run. It is this mod's own construction rather than the
                // player's run, it is deliberately not saved, and recording it would
                // publish somebody else's recording back as the player's own.
                return;
            }

            // Cleared at the start of a run rather than only at the end of one, so a
            // command typed at the main menu is never carried into the run that
            // follows it. Before any refusal below, because a run this recorder does
            // not attach to still ends the stretch the mark would have belonged to.
            lock (Gate) _consoleUsedBeforeAttach = false;

            if (!RunmobileSettings.Read().RecordMyRuns) return;

            _ = AttachWhenReady();
        }
        catch (Exception ex)
        {
            Log.Error($"[{RunmobileMod.ModId}] the recorder could not attach: {ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// The game's own funnel for a run that is over, whichever way it ended.
    ///
    /// <c>RunManager.OnEnded</c> is what writes the run-history entry, so it is the one
    /// place that is reached by a win, a death and a give-up alike, and
    /// <c>IsAbandoned</c> is what tells the last of those from the second.
    /// </summary>
    internal static void RunEnded(bool isVictory)
    {
        var recorder = Active;
        if (recorder is null) return;

        try
        {
            var abandoned = RunManager.Instance?.IsAbandoned ?? false;
            recorder.End(abandoned ? "abandoned" : isVictory ? "won" : "lost");
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not finish the recording: {ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// The run is being torn down. Detaches, keeping whatever the journal holds.
    ///
    /// A run left through the main menu is not over - the game saved it and the player
    /// can continue it - so nothing is finalised and nothing is discarded. The journal
    /// is the recording until a later session picks it back up.
    /// </summary>
    internal static void RunTornDown()
    {
        lock (Gate) _consoleUsedBeforeAttach = false;
        RewardsOffered.Forget();
        CrystalSphereOpened.Forget();

        var recorder = Active;
        if (recorder is null) return;

        Active = null;
        recorder.FinishIfStillWaitingForTheFightToEnd();
        recorder.Dispose();
    }

    /// <summary>
    /// The developer console was used.
    ///
    /// Installing this mod turns the game's full console on for the player
    /// (<c>NDevConsole</c> reads <c>ModManager.IsRunningModded()</c> when it decides
    /// whether to register the debug commands), so this is a reachable state in an
    /// ordinary modded session rather than a developer-only one. What a command did to
    /// the run is not among the decisions the history holds, so the run is recorded to
    /// its end, kept, and never publishable.
    ///
    /// Taken whether or not a recording is live, because the two cases are different
    /// and neither is "nothing happened": with a recording, the mark goes on it now;
    /// without one, the mark is held until a recording of this run exists, and dropped
    /// when the next run starts.
    ///
    /// Held unconditionally rather than only while there is a run to read. Continuing a
    /// saved run is asynchronous, so between the game saying a run is starting and the
    /// run existing there is nothing to read, and a command used in that stretch is in
    /// the run's history exactly like one used a second later.
    /// </summary>
    internal static void ConsoleCommandUsed()
    {
        try
        {
            if (Active is { } recorder)
            {
                recorder.NoticeConsoleCommand();
                return;
            }

            // No recording yet. Whether one of this run ever exists is the attach's
            // question; if it does, this is part of what it holds.
            lock (Gate) _consoleUsedBeforeAttach = true;
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not mark this recording as one the console was used in: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Marks this recording as one the console was used in, and writes the mark to the
    /// journal before anything else happens.
    ///
    /// Appended the moment it is seen, for the same reason a refusal is: a mark only
    /// the running session knows about is one a crash takes with it, and the session
    /// after it would publish a run the console had been used in.
    ///
    /// A recording that has finished takes no more marks. Its manifest is already
    /// written and says what the run was, and the run's last decision is behind it -
    /// so a command used on the score screen is not in the history this recording
    /// holds, and marking the journal for it would leave two artifacts disagreeing
    /// about the same field with nothing raised anywhere.
    /// </summary>
    private void NoticeConsoleCommand()
    {
        if (_finished || _disposed) return;

        Append(_journalPath, _capture.MarkNonStandard());
        Log.Warn(
            $"[{RunmobileMod.ModId}] the console was used in this run, so its recording is kept and is not " +
            "publishable", 2);
    }

    /// <summary>
    /// Waits for the run to be a run, then attaches to it.
    ///
    /// Two things have to have happened before the opening reading is worth taking.
    /// The run has to exist: continuing a saved run is asynchronous, so the method that
    /// starts it returns long before the game has one. And the run has to be standing
    /// in its room, which <see cref="HasEnteredItsRoom"/> owns: on a new run that is
    /// its first, where the headless replay's own opening reading is taken -
    /// <c>RunDriver.EnterFirstRoom</c> before the first action - and a reading taken
    /// on the near side of it would describe a floor the recording then claims to
    /// arrive on; on a continued run it is the room the save names, re-entered.
    /// </summary>
    private static async Task AttachWhenReady()
    {
        try
        {
            var deadline = RecordedFightRun.LetTheGameRun(AttachBudgetSeconds);
            while (true)
            {
                if (Active is not null || ProfileWriteBarrier.IsActive) return;
                if (HasEnteredItsRoom()) break;

                if (deadline.IsCompleted)
                {
                    Log.Warn(
                        $"[{RunmobileMod.ModId}] no run had begun " +
                        $"{AttachBudgetSeconds.ToString(CultureInfo.InvariantCulture)}s after the game said one " +
                        "was starting, so this one is not being recorded.", 2);
                    return;
                }

                var poll = RecordedFightRun.LetTheGameRun(SettlePollSeconds);
                if (await Task.WhenAny(poll, deadline) != poll) continue;
                await poll;
            }

            // And then the engine's own work, so the reading is of a settled run rather
            // than one still building the room it just entered.
            if (await Settle(
                    null,
                    handedOverTicket: null,
                    () => Active is not null || ProfileWriteBarrier.IsActive
                        ? "another recording or a trainer run took this game first."
                        : RunWentAway()) is { } unsettled)
            {
                Log.Warn($"[{RunmobileMod.ModId}] not recording this run: {unsettled}", 2);
                return;
            }

            _ = Attach();
        }
        catch (Exception ex)
        {
            // A run the recorder cannot describe is a run it does not record. It is
            // never a run it half-records: a history that begins in the middle of one
            // replays perfectly into a different run.
            Log.Error(
                $"[{RunmobileMod.ModId}] not recording this run: {ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// The half of the attach that decides, once there is a settled run to decide
    /// about.
    ///
    /// Separate from the waiting half because they answer different questions and only
    /// one of them needs a scene tree: everything above waits for the game to have a
    /// run, and everything here reads that run and either records it or says which
    /// rule refused it. The answer is a value rather than only a line in the log, so a
    /// caller can tell one refusal from another.
    /// </summary>
    internal static RunAttachment Attach()
    {
        if (Active is not null || ProfileWriteBarrier.IsActive)
        {
            return RunAttachment.AlreadyRecordingOrATrainerRun;
        }

        if (LiveRun.State is not { } run) return RunAttachment.TheRunWentAway;

        // The reading that decides, taken here because this is the first moment
        // there is demonstrably a run to read: its networking and its player list
        // both exist, and neither of them is what the member the game called was
        // named. Anything but a singleplayer run is refused - the MVP records,
        // replays and enters those only, and a history of a run more than one
        // person made holds decisions this client never got to see.
        var session = GameSessionWatch.Observed;
        if (!RunSession.MayBeRecorded(session))
        {
            Log.Info(
                $"[{RunmobileMod.ModId}] not recording this run: it is {RunSession.Describe(session)}, and " +
                "this build records singleplayer runs only.", 2);
            return RunAttachment.NotASingleplayerRun;
        }

        // Asked here rather than assumed: until the engine layer has taken this
        // client, the identity below is read out of the prepared copy on disk
        // instead of out of the game the run is being played in - three true values
        // from a source nobody established. Adoption is the mod's, not the mode
        // card's, and this module contributes no card to trigger it, so a trainer
        // that refuses on some future build must not leave the recorder reading
        // from somewhere else.
        if (!GameIdentitySource())
        {
            Log.Warn(
                $"[{RunmobileMod.ModId}] not recording this run: the mod could not take this running " +
                "game, so the recording could not say which build it was played on.", 2);
            return RunAttachment.CouldNotTakeTheGame;
        }

        var startedUtc = LiveRun.RunStartedUtc();
        var runId = RecordingLibrary.Name(run.Rng.StringSeed, startedUtc);
        var journalPath = $"{RecordingsDirectory}/{runId}{RunJournal.FileExtension}";
        var (sample, digest) = LiveRun.Read();
        var clock = LiveRun.RunClockMs();

        RunCapture capture;
        if (RunmobileStore.Read(journalPath) is { } existing)
        {
            var journal = RunJournal.Parse(existing);
            capture = RunCapture.Resume(journal, sample, digest);
            var resumeRefusals = capture.Refusals.Count;

            // Before anything is appended, because an append onto a fragment a
            // crash left behind produces a line no later session can read.
            if (RunJournal.RepairTruncatedTail(existing) is { } repair)
            {
                RunmobileStore.Write(journalPath, repair.Text);
                if (repair.LostARecord)
                {
                    capture.MarkBroken(
                        "The last entry in this journal was cut short by a crash while it was being " +
                        "written, so the decision it was recording is not in this recording.");
                }
            }

            // A break this resume decided on is a fact only this session knows, and
            // the session after it would compare its own live digest against a
            // journal that says nothing about the hole. Appended before the
            // recorder is live, so a crash between here and the next decision still
            // leaves the refusal on the file.
            foreach (var raised in capture.Refusals.Skip(journal.Refusals.Count))
            {
                Append(journalPath, RunJournal.RenderRefusal(raised));
            }

            // The rollback receipt makes the discarded branch survive another quit.
            // Appended before the recorder is live, so no repeated sequence number
            // can be written before the line that explains why it is legitimate, and
            // after the refusal, so a crash between the two leaves a file the next
            // session resumes and rolls back again rather than one it cannot read.
            if (capture.ResumptionRecord is { } resumption)
            {
                Append(journalPath, resumption);
            }

            Log.Info(
                $"[{RunmobileMod.ModId}] continuing the recording of {runId} at decision " +
                $"{capture.NextSeq.ToString(CultureInfo.InvariantCulture)}; continuity {capture.Continuity}", 2);
            if (capture.Discarded.Count > journal.Discarded.Count)
            {
                var branch = capture.Discarded[^1];
                Log.Info(
                    $"[{RunmobileMod.ModId}] the game returned this run to " +
                    $"{(branch.RollbackToSeq == -1 ? "its start" : $"decision {Number(branch.RollbackToSeq)}")}" +
                    $"{(branch.Reload ? " by a reload of an older save" : ", its latest save")}; " +
                    $"{Number(branch.Actions.Count)} decision(s) made after it are kept as a discarded branch", 2);
            }
            if (capture.Refusal is { } refusal) Log.Warn($"[{RunmobileMod.ModId}] {refusal}", 2);

            // Where this resume refused, the fields the game came back different in,
            // beside the refusal: every diagnosis of a broken Continue so far had to
            // be rebuilt from the journal because the log said only that it broke.
            // Bounded, because a hand or a deck is a long value and a log line is not
            // the place for the whole reading.
            if (resumeRefusals > journal.Refusals.Count)
            {
                Log.Warn($"[{RunmobileMod.ModId}] {DescribeResumeDifferences(journal.Entries[^1], sample)}", 2);
            }
            if (capture.Stop is { } stopped)
            {
                Log.Warn(
                    $"[{RunmobileMod.ModId}] this recording stopped at decision {Number(stopped.Decision.Seq)}, " +
                    $"which the recorder could not name ({ManifestValidator.Describe(stopped.Decision)}), so " +
                    "nothing this session plays is recorded past it", 2);
            }
        }
        else
        {
            capture = RunCapture.Begin(new RunRecordingStart
            {
                RunId = runId,
                RecorderVersion = RecorderVersion,
                Identity = LiveRun.ReadIdentity(run),
                State = sample,
                Digest = digest,
                RunClockMs = clock,
            });
            RunmobileStore.Write(journalPath, capture.Journal.Render());
            Log.Info($"[{RunmobileMod.ModId}] recording this run as {runId}", 2);
        }

        BeginRecording(capture, journalPath);
        return RunAttachment.Attached;
    }

    /// <summary>
    /// The recording becomes the live one: everything between a capture this session
    /// will write to and the recorder the game's patches then reach.
    /// </summary>
    internal static void BeginRecording(RunCapture capture, string journalPath)
    {
        var recorder = new RunRecorder(capture, journalPath);

        // A console command used between the run starting and this attaching is in
        // this run's history and nothing later could recover it, so it is applied
        // here rather than dropped. Before Active is published, so the mark is on
        // the file before the first decision that follows it.
        bool early;
        lock (Gate)
        {
            early = _consoleUsedBeforeAttach;
            _consoleUsedBeforeAttach = false;
        }

        if (early) recorder.NoticeConsoleCommand();

        // A journal whose last decision left a fight live resumes with that fight
        // still live, and nothing else here would ever ask: the question is asked
        // after each decision, and a resumed session has not made one yet. Left
        // unasked, every card play and ended turn of the rest of that fight goes
        // unrecorded while the recording still reports a continuous watch.
        //
        // Before Active is published rather than after, so that a throw from here
        // lands in the caller's catch with nothing recording: a log line saying the run
        // is not being recorded while it is would tell the player the opposite of
        // what happened.
        recorder.StartOrStopWatchingTheFight();

        Active = recorder;
    }

    private const int ResumeDifferenceFields = 8;
    private const int ResumeDifferenceValueLength = 160;

    /// <summary>The fields the resumed run differs in from the journal's last
    /// reading, as one bounded line for the log.</summary>
    internal static string DescribeResumeDifferences(
        RunJournalEntry last, IReadOnlyDictionary<string, string> live)
    {
        var differences = ReplayTrace.Differences(last.State, live);
        var shown = differences
            .Take(ResumeDifferenceFields)
            .Select(line => line.Length > ResumeDifferenceValueLength
                ? line[..(ResumeDifferenceValueLength - 3)] + "..."
                : line);
        var more = differences.Count > ResumeDifferenceFields
            ? $"; and {Number(differences.Count - ResumeDifferenceFields)} more"
            : "";
        return $"the run resumed differs from the journal's reading after decision {Number(last.Seq)} " +
               $"({last.Verb}) in {Number(differences.Count)} field(s): {string.Join("; ", shown)}{more}";
    }

    /// <summary>
    /// Whether the run exists, has a floor, and is standing in its room yet.
    ///
    /// The room is asked for as well as the floor because a continued run has the one
    /// before the other. A new run gets its first floor inside the map-point entry that
    /// then enters the room, so the floor count alone was the whole question for it. A
    /// continued run carries its floor count on the save and is at act floor 0 from
    /// <c>SetUpSavedSingleplayer</c> until <c>LoadIntoLatestMapCoord</c> re-enters the
    /// coordinate the save names - <c>EnterMapPointInternal</c> is the one thing that
    /// sets the act floor and the game does not save it - and a reading taken in that
    /// gap is a state the journal never saw. An honest Continue at Neow's room resumed
    /// as a broken watch that way, with every field but the act floor agreeing.
    ///
    /// A combat room is stood in before its fight is open: the room is pushed, its
    /// assets are loaded over the frames after, and only then is the combat set up and
    /// the opening hand dealt. A reading taken in that gap is not in combat and every
    /// combat field the journal's room-entry decision carries is missing from it, so a
    /// Continue into a live fight resumed as a broken watch the same way. The fight is
    /// asked the question <see cref="LiveRun.ReadyForThePlayer(RunState)"/> owns, as the
    /// settle after a map move asks it; a room restored already finished has no fight
    /// to wait for.
    ///
    /// A projection of a run the game is still building throws rather than answering,
    /// and that is a "not yet" rather than a failure: this is polled from the moment
    /// the game says a run is starting, which is well before it has one. Anything else
    /// wrong here is still a not-yet on this poll and is the deadline's to give up on,
    /// so a run is never half-attached to because one reading came too early.
    /// </summary>
    internal static bool HasEnteredItsRoom()
    {
        try
        {
            return LiveRun.State is { CurrentRoom: { } room } run && Floor(LiveRun.Sample()) >= 1 &&
                   (room is not CombatRoom { IsPreFinished: false } || LiveRun.ReadyForThePlayer(run));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Which build of the recorder is writing, so a defect found in one is
    /// traceable to everything it wrote. The same answer the mod set in the same
    /// recording gives, because <see cref="RunmobileVersion"/> reports the stamp every
    /// assembly here takes from the manifest the game reads that mod set out of.</summary>
    internal static string RecorderVersion => RunmobileVersion.Recorder;

    // ── Decisions ────────────────────────────────────────────────────────────────

    /// <summary>A decision the game has just been asked to make. Called from the
    /// prefix of the member that makes it, which is where the state it begins from
    /// is still the state in front of the player.</summary>
    internal static void Announce(
        ActionVerb verb, IReadOnlyDictionary<string, string> args, Task? engineWork = null,
        bool settlesOnceHandedToThePlayer = false)
    {
        if (ReadBefore(verb.ToString()) is { } before)
        {
            AnnounceByName(verb.ToString(), args, engineWork, before, settlesOnceHandedToThePlayer);
        }
    }

    /// <summary>
    /// The reading of the state a decision begins from, or null with the recording
    /// refused.
    ///
    /// Taken in the prefix of the member the decision goes through, before the engine
    /// has done anything about it: that is the instant a comparison at verification
    /// asks about, and the only moment it can be read. It is also the instant the
    /// decisions announced before this one are closed on, or found to overlap it
    /// (<see cref="CloseStrandedDecisions"/>), and that is asked here rather than
    /// where the decision is announced because a decision announced from a postfix
    /// has its own work running by then; asked there, the engine reads busy with this
    /// decision's work and the earlier one is refused for it.
    /// </summary>
    /// <param name="closesTheDecisionsBefore">Whether this reading closes the
    /// decisions still settling ahead of it. Every decision a person makes does; the
    /// one that does not is read from inside another decision's work and is
    /// announced with its own after-reading beside the decision it happened inside.</param>
    private static TakenReading? ReadBefore(string verb, bool closesTheDecisionsBefore = true)
    {
        var recorder = Active;
        if (recorder is null || recorder._finished) return null;

        TakenReading before;
        try
        {
            var (sample, digest) = LiveRun.Read();
            before = new TakenReading(sample, digest, LiveRun.RunClockMs(), recorder.OpenTicket());
        }
        catch (Exception ex)
        {
            recorder.Refuse(
                $"A {verb} was announced and the state it began from could not be read: " +
                $"{ex.GetType().Name}: {ex.Message}");
            return null;
        }

        if (closesTheDecisionsBefore && !recorder.CloseStrandedDecisions(before, verb))
        {
            recorder.PlaceTheSavesAskedDuringTheDecision(before.Ticket);
            return null;
        }

        return before;
    }

    /// <summary>
    /// A decision the engine has just finished, inside another decision's work.
    ///
    /// Its state is read here rather than after a settle, because the settle waits for
    /// the engine and the engine is still busy with the decision this one happened
    /// inside. Waiting would hand this decision the state that one left, which is the
    /// batching the pump exists to prevent: two decisions on one state, and the
    /// second's effects attributed to the first.
    /// </summary>
    internal static void AnnounceAsAlreadyFinished(
        ActionVerb verb, IReadOnlyDictionary<string, string> args, TakenReading before)
    {
        var recorder = Active;
        if (recorder is null || recorder._finished) return;

        TakenReading reading;
        try
        {
            var (sample, digest) = LiveRun.Read();
            reading = new TakenReading(sample, digest, LiveRun.RunClockMs());
        }
        catch (Exception ex)
        {
            recorder.Refuse(
                $"A {verb} finished and the state it left could not be read: " +
                $"{ex.GetType().Name}: {ex.Message}");
            return;
        }

        AnnounceAsAlreadyFinished(verb, args, before, reading);
    }

    /// <summary>The same, with the state the decision left already read by the
    /// caller: a set the map move declines carries the move's own reading, because a
    /// reading taken at the funnel is of a run partway through the move.</summary>
    internal static void AnnounceAsAlreadyFinished(
        ActionVerb verb, IReadOnlyDictionary<string, string> args, TakenReading before, TakenReading reading)
    {
        var recorder = Active;
        if (recorder is null || recorder._finished) return;

        lock (Gate)
        {
            recorder._pending.Enqueue(new PendingDecision(verb.ToString(), args, null, before, reading));
            if (recorder._pumping) return;
            recorder._pumping = true;
        }

        _ = recorder.Pump();
    }

    /// <summary>
    /// The same, for a decision already held by name - which the patches that read
    /// their arguments in a prefix and announce in the postfix beside it hold it as.
    ///
    /// Queued rather than recorded, because what a decision left behind can only be
    /// read once the engine has finished doing it. The arguments are read now, while
    /// the shelf still holds the thing that was bought and the hand still holds the
    /// card that was played; the state is read at the other end of the settle.
    /// </summary>
    private static void AnnounceByName(
        string verb, IReadOnlyDictionary<string, string> args, Task? engineWork, TakenReading before,
        bool settlesOnceHandedToThePlayer = false)
    {
        var recorder = Active;
        if (recorder is null || recorder._finished) return;

        lock (Gate)
        {
            recorder._pending.Enqueue(new PendingDecision(
                verb, args, engineWork, before, SettlesOnceHandedToThePlayer: settlesOnceHandedToThePlayer));
            if (recorder._pumping) return;
            recorder._pumping = true;
        }

        _ = recorder.Pump();
    }

    /// <summary>
    /// A card prompt answered, with the prompt as the entry point observed it and the
    /// cards that came back.
    ///
    /// Held rather than recorded, because a card prompt is answered from inside the
    /// call that opened it: the decision that opened it has not settled yet, and the
    /// format records the picks immediately after it. Which is also how the driver
    /// replays them.
    /// </summary>
    internal static void CardPromptAnswered(CardPrompts.Prompt prompt, IReadOnlyList<CardModel> chosen)
    {
        var recorder = Active;
        if (recorder is null || recorder._finished) return;

        recorder.HoldCardPromptAnswers(prompt, chosen);
    }

    /// <summary>
    /// What a prompt's answer is to this recording, decided from what the prompt
    /// established and nothing else.
    ///
    /// Three of the prompt's states are not an answer to write. Two prompts open at
    /// once is a state nothing can order, so both are refused by name. A prompt the
    /// engine answered itself - the fight ending, nothing to offer, the candidates
    /// inside the minimum - is no decision, in this recording or in the replay that
    /// reads it. And a prompt that settled without the recorder ever seeing the engine
    /// read it is one whose offered list was never established, so the answer cannot
    /// be placed in it and is refused rather than placed in a list read at some other
    /// moment.
    ///
    /// What an offered prompt's answer is depends on what it asked for. A prompt that
    /// asked for exactly N is answered by N picks, and <c>ManifestCardSelector</c>
    /// replays exactly that many; any other count is one the format has no way to
    /// state for that prompt, so it stops the recording here, naming the count. A
    /// prompt that asked for a range - "exhaust up to 3", a choose-a-card screen that
    /// can be skipped - leaves the count to the player, so its answer is the picks and
    /// then a <see cref="ActionVerb.ConfirmCardScreen"/> saying how many there were,
    /// none included; the selector reads the count back from that record, so a prompt
    /// declined replays as declined rather than as a refusal in front of a player.
    /// </summary>
    internal void HoldCardPromptAnswers(CardPrompts.Prompt prompt, IReadOnlyList<CardModel> chosen)
    {
        if (prompt.Conflict is { } other)
        {
            HoldScreenAnswerStop(MetAtScreen(
                PromptName(prompt), prompt.Screen,
                "Another card prompt was open while this one was asked, so the recorder cannot say which " +
                "answer belongs to which.",
                ("other_prompt", $"{nameof(CardSelectCmd)}.{other}"), ("chosen", Number(chosen.Count))));
            return;
        }

        switch (prompt.State)
        {
            case CardPrompts.PromptState.EngineAnswered:
                return;

            case CardPrompts.PromptState.Asked:
                HoldScreenAnswerStop(MetAtScreen(
                    PromptName(prompt), prompt.Screen,
                    "The prompt settled before the engine paused for the player, so the recorder never saw " +
                    "what it offered and cannot say which option was picked.",
                    ("card_ids", string.Join(",", chosen.Select(card => card.Id.ToString()))),
                    ("chosen", Number(chosen.Count))));
                return;
        }

        var offered = prompt.Offered!;
        var askedForARange = prompt.MinSelect < prompt.MaxSelect;
        var outsideWhatItAsked = askedForARange
            ? chosen.Count < prompt.MinSelect || chosen.Count > prompt.MaxSelect
            : chosen.Count != prompt.MaxSelect;
        if (outsideWhatItAsked)
        {
            HoldScreenAnswerStop(MetAtScreen(
                PromptName(prompt), prompt.Screen,
                askedForARange
                    ? "The prompt was answered with a count outside the range it asked for, which no screen " +
                      "of this build confirms, so the recorder cannot say what was decided."
                    : "The prompt asked for exactly that many picks and was answered with another count, and " +
                      "this format states such a prompt's answer only as exactly that many.",
                ("card_ids", string.Join(",", chosen.Select(card => card.Id.ToString()))),
                ("chosen", Number(chosen.Count)), ("min_select", Number(prompt.MinSelect)),
                ("max_select", Number(prompt.MaxSelect)), ("offered", Number(offered.Count))));
            return;
        }

        HoldCardScreenAnswers(PromptName(prompt), prompt.Screen, offered, chosen);
        if (askedForARange) HoldCardScreenConfirmation(chosen.Count);
    }

    /// <summary>
    /// Holds the <see cref="ActionVerb.ConfirmCardScreen"/> that ends a range prompt's
    /// picks, after them, with how many there were.
    ///
    /// Held even where a pick could not be placed and a stop is already among the
    /// answers: the stop outranks everything held beside it, so the confirmation is
    /// then never written.
    /// </summary>
    private void HoldCardScreenConfirmation(int count)
    {
        lock (Gate)
        {
            _screenAnswers.Add(new ScreenAnswer(
                nameof(ActionVerb.ConfirmCardScreen),
                new SortedDictionary<string, string>(StringComparer.Ordinal) { ["count"] = Number(count) }));
        }
    }

    /// <summary>The game's own name for a prompt: its entry point, on its command class.</summary>
    private static string PromptName(CardPrompts.Prompt prompt) => $"{nameof(CardSelectCmd)}.{prompt.EntryPoint}";

    /// <summary>
    /// Maps each card that came back to its position in what the prompt offered, and
    /// holds one <see cref="ActionVerb.SelectCardFromScreen"/> per card.
    /// </summary>
    /// <param name="prompt">The game's own name for what asked, so a stop names it.</param>
    /// <param name="screen">What the retail client draws for it, as the stop's
    /// discriminator.</param>
    internal void HoldCardScreenAnswers(
        string prompt, string? screen, IReadOnlyList<CardModel> offered, IEnumerable<CardModel> chosen)
    {
        var taken = new List<(string CardId, int Index)>();
        foreach (var card in chosen)
        {
            var index = -1;
            for (var candidate = 0; candidate < offered.Count; candidate++)
            {
                if (!ReferenceEquals(offered[candidate], card)) continue;
                index = candidate;
                break;
            }

            if (index < 0)
            {
                // Seen and not nameable: a position the recorder guessed would replay as
                // a different decision, so the decision this prompt answers stops the
                // recording instead.
                HoldScreenAnswerStop(MetAtScreen(
                    prompt, screen,
                    "The card is not one of the cards the prompt offered, so the recorder cannot say which " +
                    "option was picked.",
                    ("card_id", card.Id.ToString()), ("offered", Number(offered.Count))));
                return;
            }

            taken.Add((card.Id.ToString(), index));
        }

        // Every answer from one prompt is resolved before any of them is nominated
        // against, because the alternative has to be a position none of them took. It
        // is per answer rather than one for the prompt: what each nominates is another
        // copy of its own card.
        var offeredIds = offered.Select(card => card.Id.ToString()).ToList();
        var positions = taken.Select(pick => pick.Index).ToList();

        lock (Gate)
        {
            foreach (var (cardId, index) in taken)
            {
                var args = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["card_id"] = cardId,
                    ["option_index"] = Number(index),
                };
                if (Corruption.NominateScreenOption(offeredIds, index, positions) is { } alternative)
                {
                    args[Corruption.AlternativeOptionIndex] = Number(alternative);
                }

                _screenAnswers.Add(new ScreenAnswer(nameof(ActionVerb.SelectCardFromScreen), args));
            }
        }
    }

    /// <summary>
    /// A bundle screen answered, with what it offered and the position that came back.
    ///
    /// A bundle has no id of its own, so its identity is its cards' ids joined in the
    /// order the prompt listed them - the same value the driver checks the answer
    /// against. Held like a card screen's answer, because the prompt is answered
    /// inside the decision that obtained the relic that opened it.
    /// </summary>
    internal static void BundleScreenAnswered(
        IReadOnlyList<IReadOnlyList<CardModel>> offered, int index)
    {
        var recorder = Active;
        if (recorder is null || recorder._finished) return;

        if (index < 0 || index >= offered.Count)
        {
            StopAtScreenAnswer(MetAtScreen(
                nameof(CardSelectCmd.FromChooseABundleScreen), null,
                "The position is not one of the bundles the screen offered, so the recorder cannot say which " +
                "was picked.",
                ("option_index", Number(index)), ("offered", Number(offered.Count))));
            return;
        }

        var args = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["card_ids"] = string.Join(",", offered[index].Select(card => card.Id.ToString())),
            ["option_index"] = Number(index),
        };
        var positions = new[] { index };
        if (Corruption.NominateScreenOption(
                [.. offered.Select(bundle => string.Join(",", bundle.Select(card => card.Id.ToString())))],
                index, positions) is { } alternative)
        {
            args[Corruption.AlternativeOptionIndex] = Number(alternative);
        }

        lock (Gate)
        {
            recorder._screenAnswers.Add(new ScreenAnswer(nameof(ActionVerb.SelectBundleFromScreen), args));
        }
    }

    /// <summary>A relic screen answered. Nothing on v0.111.0 opens one; the patch is
    /// there so a build that does is recorded rather than refused.</summary>
    internal static void RelicScreenAnswered(IReadOnlyList<RelicModel> offered, int index)
    {
        var recorder = Active;
        if (recorder is null || recorder._finished) return;

        if (index < 0 || index >= offered.Count)
        {
            StopAtScreenAnswer(MetAtScreen(
                nameof(RelicSelectCmd.FromChooseARelicScreen), null,
                "The position is not one of the relics the screen offered, so the recorder cannot say which " +
                "was picked.",
                ("option_index", Number(index)), ("offered", Number(offered.Count))));
            return;
        }

        var args = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["relic_id"] = offered[index].Id.ToString(),
            ["option_index"] = Number(index),
        };

        lock (Gate)
        {
            recorder._screenAnswers.Add(new ScreenAnswer(nameof(ActionVerb.SelectRelicFromScreen), args));
        }
    }

    /// <summary>
    /// Waits for the engine to finish, giving it no credit for time a person spent at a
    /// card screen.
    ///
    /// <em>The engine's budget measures only the engine's own time.</em> That is the one
    /// rule here, and it is why the screen count and the budget are read by the same
    /// loop rather than one before the other. A screen that is up suspends the budget;
    /// when it comes down the engine gets the whole of it, counted from that moment. It
    /// has to hold however many screens one decision puts up - a card reward whose hook
    /// allows a second card closes its screen and opens another, and a budget started
    /// before the first would be charging the player for the second.
    ///
    /// A person deciding is given no budget at all, so <paramref name="stopped"/> is
    /// what keeps a wait on one from outliving the run it was waiting for. It returns
    /// the reason rather than a flag, because the two callers stop for different reasons
    /// and a sentence written for one of them would be false in the other.
    ///
    /// The engine's own work is waited for as well as its queue, where a decision
    /// handed one over, with one exception a caller opts into per decision: work that
    /// has handed the run to the player. An event option that offers rewards awaits
    /// the set until the player has dealt with it, and those are the player's next
    /// decisions; the option has settled once the set is on offer, and
    /// <paramref name="handedToThePlayer"/> is how the caller says so. It is asked only
    /// where the work is still open, so a decision whose work finishes on its own is
    /// read once it has; a caller that passes none waits for the work to finish, and
    /// a decision whose own work begins a set and awaits it runs the budget out.
    ///
    /// The count, the stop, the poll and the budget arrive as arguments for the same
    /// reason <see cref="PlayerFightObserver.WaitUntilSettled"/>'s do: waiting is a rule
    /// about those things, and handed them it can be exercised on a machine with no game.
    ///
    /// <paramref name="closedByTheNext"/> is the pump's own: the decision being waited
    /// for may have been read after by the decision that followed it, at the instant
    /// that one was announced with the engine already quiet (<see cref="CloseStrandedDecisions"/>),
    /// and a wait that went on polling for a state already taken would read the state
    /// the next decision left instead.
    /// </summary>
    /// <returns>Null once the engine has settled, or the sentence saying why the wait
    /// ended without it.</returns>
    internal static async Task<string?> WaitForTheEngine(
        Func<int> open,
        Func<string?> stopped,
        Task? engineWork,
        Func<bool> idle,
        Func<Task> newBudget,
        Func<Task> nextPoll,
        Func<string, string> unsettled,
        Func<bool>? handedToThePlayer = null,
        Func<bool>? closedByTheNext = null)
    {
        Task? budget = null;
        var idleTicks = 0;
        var waitedForAScreen = false;

        while (true)
        {
            if (stopped() is { } why) return why;
            if (closedByTheNext?.Invoke() == true) return null;

            if (open() > 0)
            {
                // The budget is discarded rather than paused, so the next one starts
                // from the moment the last screen comes down. A screen that goes up a
                // second time is a second stretch of somebody thinking, and the engine
                // gets its whole budget back either way.
                budget = null;
                idleTicks = 0;
                waitedForAScreen = true;
                await nextPoll();
                continue;
            }

            budget ??= newBudget();
            if (budget.IsCompleted) return unsettled(Spent(waitedForAScreen));

            await nextPoll();
            if (closedByTheNext?.Invoke() == true) return null;

            // A screen that went up during the poll scores no idle tick; the top of the
            // loop then throws the budget away. Nor does an engine that has not yet said
            // its own work is finished: the queue reading empty is not the same claim as
            // the queue having been seen to drain, and the two ticks are a debounce on
            // top of that signal rather than a replacement for it.
            idleTicks = open() == 0 && WorkIsDone(engineWork, handedToThePlayer) && idle()
                ? idleTicks + 1
                : 0;
            if (idleTicks >= 2) return null;
        }
    }

    /// <summary>Whether the work a decision handed over is finished, or has handed the
    /// run to the player and so will not finish until the player's next decision.</summary>
    private static bool WorkIsDone(Task? engineWork, Func<bool>? handedToThePlayer) =>
        engineWork is null || engineWork.IsCompleted || handedToThePlayer?.Invoke() == true;

    /// <summary>
    /// What the budget that ran out was measuring, in words.
    ///
    /// The card screen is named only where this wait actually stood down for one. Most
    /// decisions open none, and a refusal that told a reader the clock started from a
    /// screen closing would be describing something nobody watched - and it is not a log
    /// line: it goes through <see cref="Refuse"/> into the journal and out as the reason
    /// the manifest gives for a broken recording.
    /// </summary>
    private static string Spent(bool waitedForAScreen) =>
        $"within {SettleBudgetSeconds.ToString(CultureInfo.InvariantCulture)} seconds" +
        (waitedForAScreen ? " of the last card screen closing" : string.Empty);

    /// <summary>
    /// A card reward screen answered, by the position it reports.
    ///
    /// A position among the cards is the card that came back, and the loot decision
    /// that opened the screen is written as <see cref="ActionVerb.TakeCard"/> with it.
    /// A position past the cards is one of the reward's alternatives, and the same
    /// decision is written as <see cref="ActionVerb.TakeCardRewardAlternative"/>
    /// naming it: on this build every alternative ends the selection, so the two are
    /// the same click answered two ways.
    /// </summary>
    internal static void CardRewardAnswered(
        IReadOnlyList<CardModel> offered, IReadOnlyList<CardRewardAlternative> alternatives, int? option)
    {
        var recorder = Active;
        if (recorder is null || recorder._finished) return;

        recorder.HoldCardRewardAnswer(offered, alternatives, option);
    }

    /// <summary>
    /// Shelves one card reward's answer for the card-reward decision that opened its
    /// screen, which is the only decision <see cref="Commit"/> hands it to.
    /// </summary>
    internal void HoldCardRewardAnswer(
        IReadOnlyList<CardModel> offered, IReadOnlyList<CardRewardAlternative> alternatives, int? option)
    {
        // No option means the screen was dismissed rather than answered, which reaches
        // the loot screen as a skip and is recorded there.
        if (option is not { } index) return;

        if (index < 0 || index >= offered.Count + alternatives.Count)
        {
            StopAtScreenAnswer(MetAtScreen(
                nameof(NCardRewardSelectionScreen), null,
                "The option is past the cards and alternatives the screen offered, so the recording cannot " +
                "say what was taken.",
                ("option_index", Number(index)), ("offered", Number(offered.Count)),
                ("alternatives", Number(alternatives.Count))));
            return;
        }

        if (index >= offered.Count)
        {
            var args = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["option_id"] = alternatives[index - offered.Count].OptionId,
                ["option_index"] = Number(index),
            };

            lock (Gate)
            {
                _screenAnswers.Add(new ScreenAnswer(nameof(ActionVerb.TakeCardRewardAlternative), args));
            }

            return;
        }

        var reward = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["card_id"] = offered[index].Id.ToString(),
            ["option_index"] = Number(index),
        };

        // The two appear together or not at all, which is what the validator
        // requires of them: an id with no position names no decision to take.
        if (Corruption.NominateCard([.. offered.Select(card => card.Id.ToString())], index) is { } alternative)
        {
            reward[Corruption.AlternativeCardId] = alternative.CardId;
            reward[Corruption.AlternativeOptionIndex] = Number(alternative.OptionIndex);
        }

        lock (Gate)
        {
            _screenAnswers.Add(new ScreenAnswer(nameof(ActionVerb.TakeCard), reward));
        }
    }

    /// <summary>
    /// Records each queued decision once the engine has finished with it.
    ///
    /// One at a time and in order, because each decision's reading is of the state
    /// <em>it</em> left: a batch settled together would give two decisions one state
    /// and put the second one's effects on the first.
    ///
    /// The head of the queue can be taken from under this loop: a decision announced
    /// while the head was still settling, with the engine already quiet, closes the
    /// head with its own before-reading and may commit it there and then
    /// (<see cref="CloseStrandedDecisions"/>). So the settle stands down the moment
    /// the head has been read after, and the head is dequeued only where it is still
    /// this loop's to dequeue.
    /// </summary>
    private async Task Pump()
    {
        while (true)
        {
            PendingDecision next;
            lock (Gate)
            {
                if (_pending.Count == 0 || _disposed || _finished)
                {
                    _pumping = false;
                    return;
                }

                // A stopped recording takes no decision, and nothing here waits on the
                // engine for one it will not take.
                if (_capture.Stop is { } stop)
                {
                    Log.Info(
                        $"[{RunmobileMod.ModId}] {Number(_pending.Count)} decision(s) arrived after the " +
                        $"recording stopped at decision {Number(stop.Decision.Seq)}, so they are not recorded", 2);
                    _pending.Clear();
                    _pumping = false;
                    return;
                }

                next = _pending.Peek();
            }

            string? unsettled;
            try
            {
                // A decision that arrived with its state already read does not settle:
                // it finished inside another decision's work, and the engine will not
                // go quiet until that one has finished too.
                unsettled = next.ReadAfter is not null
                    ? null
                    : await Settle(
                        next.EngineWork,
                        next.SettlesOnceHandedToThePlayer ? next.Before.Ticket : null,
                        () => _disposed || _finished
                            ? "The recording ended before this decision could be read."
                            : RunWentAway(),
                        () => next.ReadAfter is not null);
            }
            catch (Exception ex)
            {
                lock (Gate)
                {
                    if (_pending.Count > 0 && ReferenceEquals(_pending.Peek(), next)) _pending.Dequeue();
                }

                Refuse($"A {next.Verb} could not be recorded: {ex.GetType().Name}: {ex.Message}");
                PlaceTheSavesAskedDuringTheDecision(next.Before.Ticket);
                continue;
            }

            lock (Gate)
            {
                // A wait that ended because there is no recording left has nothing
                // to refuse: the run is over, and Finish has already said how many
                // decisions it never read.
                if (_disposed || _finished)
                {
                    _pumping = false;
                    return;
                }

                // Taken by the decision after it while this loop was waiting
                if (_pending.Count == 0 || !ReferenceEquals(_pending.Peek(), next)) continue;

                _pending.Dequeue();
            }

            Take(next, unsettled);
        }
    }

    /// <summary>
    /// Records one decision taken off the queue, with the verdict of its settle.
    ///
    /// The pump's body for a decision it has dequeued, and the same body a synchronous
    /// drain of already-read decisions runs (<see cref="TakeEveryDecisionReadAfter"/>),
    /// so a decision is recorded the same way whichever took it off the queue.
    /// </summary>
    /// <param name="unsettled">Why the engine never settled for it, or null where it
    /// did or where the decision brought its reading with it.</param>
    private void Take(PendingDecision next, string? unsettled)
    {
        try
        {
            if (unsettled is not null)
            {
                Refuse($"A {next.Verb} could not be read: {unsettled}");
                return;
            }

            // A decision the recorder saw and could not name, reached in its turn.
            if (next.Unmapped is { } met)
            {
                StopAt(met, next.Before);
                return;
            }

            if (NothingHappened(next) && !AnsweredPastTheCards(next))
            {
                // The player opened a screen and backed out of it, or the engine
                // turned the decision down. Recording it would put an action in the
                // history that a replay would make differently, and the two
                // together are what say it: the engine said no, and the run's
                // complete state - draw order and every random stream included - is
                // where it was before. A card reward answered past its cards with
                // the alternative that leaves it on the screen reads the same way -
                // the reward was not taken and nothing changed - and is a decision
                // all the same, the loot screen's Skip, which the replay makes with
                // its own verb; dropped here, its held answer went to the next
                // click on the same reward and broke the recording.
                Log.Info(
                    $"[{RunmobileMod.ModId}] a {next.Verb} was not taken and the run is unchanged, so it " +
                    "is not recorded", 2);
                return;
            }

            Commit(next.Verb, next.Args, next.Before, next.ReadAfter);
        }
        catch (Exception ex)
        {
            Refuse($"A {next.Verb} could not be recorded: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // Whatever became of the decision, a save the game asked for while
            // it was in flight holds the state the history now ends in: after
            // the decision where it was written, and after the one before it
            // where the engine turned it down and left the run as it was.
            PlaceTheSavesAskedDuringTheDecision(next.Before.Ticket);
        }
    }

    /// <summary>
    /// Reads after every decision still settling, at the instant a new one is read,
    /// and says whether the new one may be acted on.
    ///
    /// The run-level counterpart of <see cref="CloseStrandedFightStep"/>, and the same
    /// rule: a decision announced while the one before it is still settling has its
    /// before-reading taken at an instant the pump has not read yet. Where the earlier
    /// decision's work is done and the engine is quiet, that instant <em>is</em> the
    /// state the earlier decision left - the pump's debounce had simply not fired - so
    /// the earlier decision is closed with this reading and nothing is lost. Where the
    /// earlier decision's work is still in flight, the two overlap: the earlier one's
    /// after-reading, taken later by the pump, would carry this decision's effects, and
    /// this one's before-reading is of a run partway through a decision. The recording
    /// says so instead, and the new decision is not recorded, because a run recorded
    /// that way replays into a different run.
    ///
    /// Before this rule the window was silent. A driver that issued the next decision
    /// as soon as the engine allowed it wrote the second decision's effects into the
    /// first's after-reading three times in one retail session, and the recording
    /// finished <c>integrity = complete</c> every time; for a person the window is a
    /// click within a poll of a screen becoming ready. A decision that brought its
    /// reading with it - one finished inside another's work - is not settling and is
    /// left alone.
    /// </summary>
    /// <param name="before">The new decision's before-reading, taken in its prefix.</param>
    /// <param name="verb">What the new decision is, for the refusal.</param>
    /// <returns>Whether the new decision may be recorded.</returns>
    private bool CloseStrandedDecisions(TakenReading before, string verb)
    {
        List<PendingDecision> settling;
        lock (Gate)
        {
            settling = _pending.Where(decision => decision.ReadAfter is null).ToList();
        }

        if (settling.Count == 0) return true;

        var quiet = EngineIsQuiet();
        var stillWorking = settling.FirstOrDefault(decision =>
            !quiet ||
            !WorkIsDone(
                decision.EngineWork,
                decision.SettlesOnceHandedToThePlayer ? () => HandedToThePlayerDuring(decision.Before.Ticket) : null));
        if (stillWorking is not null)
        {
            var reason =
                $"A {verb} was made while the {stillWorking.Verb} before it was still being carried out by the " +
                "engine, so the recording cannot say what state either of them left.";
            Refuse(reason);
            return false;
        }

        var reading = new TakenReading(before.Sample, before.Digest, before.RunClockMs);
        foreach (var decision in settling) decision.ClosedByTheNext = reading;
        return true;
    }

    /// <summary>
    /// Records, now and in order, every decision at the head of the queue that has
    /// already been read after, and stops at the first that has not.
    ///
    /// For the one caller that cannot wait on the pump's next poll: a fight's first
    /// action is about to reach the executor, the decision that opened the fight has
    /// just been closed with the reading that action begins from, and the observer
    /// that will watch the action attaches only once that decision is committed. The
    /// pump finds its head gone and carries on with whatever is left.
    /// </summary>
    private void TakeEveryDecisionReadAfter()
    {
        while (true)
        {
            PendingDecision next;
            lock (Gate)
            {
                if (_pending.Count == 0 || _disposed || _finished || _capture.Stop is not null) return;
                next = _pending.Peek();
                if (next.ReadAfter is null) return;
                _pending.Dequeue();
            }

            Take(next, null);
        }
    }

    /// <summary>
    /// Whether the engine turned this decision down and left the run exactly as it was.
    ///
    /// Both halves, because neither is enough on its own. A task that came back false
    /// does not always mean nothing happened - the engine reports a reward taken
    /// through the reward as well as through the call - and an unchanged run does not
    /// always mean nothing was decided: skipping a loot screen and leaving a chest's
    /// relic behind are both decisions the format records precisely <em>because</em>
    /// the engine discards them silently and the run looks the same either way. Those
    /// two hand back nothing to be false about, so they are never reached here.
    /// </summary>
    private bool NothingHappened(PendingDecision decision) =>
        decision.EngineWork is Task<bool> { IsCompletedSuccessfully: true, Result: false } &&
        string.Equals(
            _capture.LastDigest, decision.ReadAfter?.Digest ?? LiveRun.Project().Digest(), StringComparison.Ordinal);

    /// <summary>Whether a card-reward decision holds an answer the screen gave past
    /// its cards: an alternative, which is a decision whether or not it completed
    /// the reward.</summary>
    private bool AnsweredPastTheCards(PendingDecision decision)
    {
        if (!string.Equals(decision.Verb, nameof(ActionVerb.TakeCard), StringComparison.Ordinal)) return false;
        lock (Gate)
        {
            return _screenAnswers.Any(answer =>
                string.Equals(answer.Verb, nameof(ActionVerb.TakeCardRewardAlternative), StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Waits for the engine to finish what a decision started.
    ///
    /// Two questions: the engine task the decision handed back, where there was one,
    /// and the run's own action queue. Both, because they are different halves of the
    /// same work - entering a map node is a task that builds a room, and playing a card
    /// is a queue that drains - and a reading taken between them would be of a run
    /// halfway through a decision.
    /// </summary>
    /// <param name="handedOverTicket">The ticket of a decision whose work is finished
    /// once the engine has handed the run to the player inside it, or null for every
    /// other decision, whose work is waited for until it finishes.</param>
    /// <param name="stopped">Why there is no longer a recording to settle for, or null
    /// while there still is. <see cref="WaitForTheEngine"/> says why it is a sentence.</param>
    /// <returns>Null once the engine has settled, or the sentence saying what it was
    /// still waiting for.</returns>
    private static Task<string?> Settle(
        Task? engineWork, long? handedOverTicket, Func<string?> stopped, Func<bool>? closedByTheNext = null) =>
        WaitForTheEngine(
            () => CardScreensUp.Count + (BundleScreen.Open is null ? 0 : 1) + (RelicScreen.Open is null ? 0 : 1),
            stopped,
            engineWork,
            // The queue is asked twice with a tick between, because a decision that has
            // not enqueued its work yet reads as an engine with nothing to do.
            //
            // An idle queue is not the whole of it when a fight is open. A map move into
            // a combat room completes its task, and drains its queue, before the opening
            // hand is dealt: read there and the decision's after-state has no hand and no
            // energy. That state is real, no player ever acted from it, and it is what
            // every combat-start boundary this recorder wrote was anchored to until a
            // retail run proved none of them could reproduce. LiveRun owns the question
            // and RecordedFightEntry asks the same one; a fight that has ended answers by
            // not being in combat, so a lethal card settles here rather than waiting for
            // a turn that never comes.
            EngineIsQuiet,
            Clock.Budget,
            Clock.Poll,
            spent => $"The engine did not settle {spent}, so the recorder cannot say what state this " +
                     "decision left.",
            handedOverTicket is { } ticket ? () => HandedToThePlayerDuring(ticket) : null,
            closedByTheNext);

    /// <summary>
    /// Whether the engine has nothing in flight right now: the executor idle, the
    /// action queue empty, and where a fight is open, the player able to act.
    ///
    /// One reading of the same instant, asked by the settle on every poll and by
    /// <see cref="Finish"/> once, at the run's end, of the decision the run ended
    /// inside.
    /// </summary>
    private static bool EngineIsQuiet() =>
        RunManager.Instance is { ActionExecutor.IsRunning: false } manager &&
        manager.ActionQueueSet.IsEmpty &&
        (!LiveRun.InCombat || LiveRun.ReadyForThePlayer());

    /// <summary>
    /// Whether the engine has handed the run to the player inside the work of the
    /// decision holding <paramref name="ticket"/>: a rewards set on offer, or the
    /// Crystal Sphere's screen up, begun while that decision was the one executing.
    ///
    /// Asked for an event option's, a purchase's, a rest option's and a reward
    /// claim's work and for nothing else, which <see cref="PendingDecision.SettlesOnceHandedToThePlayer"/>
    /// carries: the two places such a task waits on the player rather than on the
    /// engine, each read where the engine begins the wait. A card prompt, the bundle
    /// screen and the relic screen are not here, because the settle stands down for
    /// each on its own count and the decision's task finishes once it is answered -
    /// the picks are the decision's own answer, recorded after it, rather than
    /// decisions of the run. Work waiting on anything else runs the settle's budget
    /// out and refuses the decision, naming it, which is what a screen this build adds
    /// and nothing here watches should do.
    ///
    /// Asked of the decision and not of the run, because a set on offer is the usual
    /// state of a loot screen: a reward claimed off it has work of its own - the gold
    /// flying, the card landing in the deck - and that work is waited for as before,
    /// with the set the claim is answering still open beside it. A purchase, a rest and
    /// a claim are here because a relic bought at the shop, a heal under Tiny Mailbox
    /// and a relic claimed that opens rewards of its own - one Neow's Bones deals -
    /// offer a set from inside their own task - Orrery's and Cauldron's
    /// <c>AfterObtained</c>, the heal's potions - and that task finishes only once the
    /// set is answered, by the decisions recorded after it; read as unsettled, every
    /// such purchase, rest and claim in normal play was refused. The driver replays them the same way
    /// (<c>RunDriver.SettleOrHandOver</c>), so the reading each host takes is of the
    /// same state: the set on offer and the engine quiet. Every other decision waits
    /// for its own task whatever it began.
    /// </summary>
    private static bool HandedToThePlayerDuring(long ticket) =>
        RewardsOffered.OnOfferSince(ticket) || CrystalSphereOpened.OpenSince(ticket);

    /// <summary>Why a settle should stop because the run itself went away, or null while
    /// it is still being played.</summary>
    private static string? RunWentAway() =>
        RunManager.Instance is { IsInProgress: true }
            ? null
            : "The run ended while the recorder was reading it.";

    /// <summary>
    /// Writes one decision, and the card-prompt picks it pulled out of the player,
    /// into the capture and the journal.
    ///
    /// The picks share this decision's reading because that is what they are: a card
    /// prompt is answered inside the call that opened it, so the state after the
    /// prompt's answer and the state after the decision are the same state. The
    /// headless driver reads them back the same way - the selection is confirmed and
    /// changes nothing - so the two traces have the same shape.
    /// </summary>
    internal void Commit(
        string verbName, IReadOnlyDictionary<string, string> args, TakenReading before, TakenReading? taken = null)
    {
        if (!Enum.TryParse<ActionVerb>(verbName, out var verb))
        {
            Refuse($"A '{verbName}' was announced, which is not a decision this format names.");
            return;
        }

        // Read now unless this decision brought the state it left with it, which one
        // that finished inside another decision's work has to.
        var after = taken?.AsStateReading() ?? LiveRun.Read().AsStateReading();
        var clock = taken is null ? LiveRun.RunClockMs() : taken.RunClockMs;

        // A card reward's answer belongs to the card-reward decision that opened its
        // screen and to nothing else, so only that decision takes one off the shelf.
        // The pump commits decisions one at a time behind a settle, and a reward
        // answer can arrive while an earlier decision is still settling; a commit that
        // took every answer on the shelf handed that one to the wrong decision, wrote
        // it as a loot click of its own, and left the real card-reward decision with
        // no answer at all.
        List<ScreenAnswer> answers;
        lock (Gate)
        {
            var takesRewardAnswers = verb == ActionVerb.TakeCard;
            answers = _screenAnswers.Where(answer => takesRewardAnswers || !IsCardRewardAnswer(answer)).ToList();
            _screenAnswers.RemoveAll(answer => takesRewardAnswers || !IsCardRewardAnswer(answer));
        }

        // A screen this decision opened answered with something the recorder could
        // not name, so the decision is the one the recording stops at: written without
        // its answer it would be one a replay makes differently.
        if (StopAmong(answers) is { } met)
        {
            StopAt(met, before);
            return;
        }

        // A card reward names what came back itself - the card, or the alternative
        // that ended the selection instead - so the loot decision is written with that
        // answer's own verb and arguments. Exactly one, because the engine asks the
        // screen once per click and every alternative this build records ends the
        // selection: a second answer is one no click of this reward produced, and a
        // duplicate written down would replay as a decision nobody made. A player who
        // presses Skip and opens the same reward again is two clicks, and each is
        // committed here on its own with its one answer. Every other screen is
        // answered by the selections recorded after the decision that opened it.
        if (verb == ActionVerb.TakeCard)
        {
            var reward = answers.Where(IsCardRewardAnswer).ToList();
            if (reward.Count != 1)
            {
                Refuse(
                    $"A card reward was taken and the recorder saw " +
                    $"{reward.Count.ToString(CultureInfo.InvariantCulture)} answer(s) to it. Exactly one thing " +
                    "comes off a card reward, and a recording that could not say which cannot be replayed.");
                return;
            }

            // The answer names what came back and the loot click names which card
            // reward it came off, so the position travels from the click to the
            // answer's own verb
            verb = Enum.Parse<ActionVerb>(reward[0].Verb);
            var answered = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, value) in reward[0].Args) answered[name] = value;
            if (args.TryGetValue(RewardKinds.IndexArgument, out var position))
            {
                answered[RewardKinds.IndexArgument] = position;
            }

            args = answered;
            answers.Remove(reward[0]);
        }

        Write(_capture.Record(verb, args, before.AsStateReading(), after, clock));
        WriteAnswers(answers, after, clock);

        StartOrStopWatchingTheFight();
    }

    /// <summary>Whether a shelved answer is a card reward's own - the card, or the
    /// alternative that ended the selection instead - which only a card-reward
    /// decision may take.</summary>
    private static bool IsCardRewardAnswer(ScreenAnswer answer) =>
        answer.Verb is nameof(ActionVerb.TakeCard) or nameof(ActionVerb.TakeCardRewardAlternative);

    /// <summary>
    /// Records the screen answers a decision pulled out of the player, sharing that
    /// decision's after-reading on both sides.
    ///
    /// They share it because that is what they are: a screen is answered from inside
    /// the call that opened it, so the state after the screen's answer and the state
    /// after the decision are the same state - and the headless driver's trace has
    /// the same shape, a step that confirms the answer and changes nothing.
    /// </summary>
    private void WriteAnswers(IEnumerable<ScreenAnswer> answers, StateReading reading, int? clock)
    {
        foreach (var answer in answers)
        {
            if (!Enum.TryParse<ActionVerb>(answer.Verb, out var verb))
            {
                Refuse($"A screen answered as '{answer.Verb}', which is not a decision this format names.");
                continue;
            }

            Write(_capture.Record(verb, answer.Args, reading, reading, clock));
        }
    }

    /// <summary>
    /// Appends one line to the journal.
    ///
    /// Appended rather than rewritten, which is the whole reason the journal is a line
    /// per decision: finishing a write means finishing a line, so a crash leaves a real
    /// recording of the part of the run that happened rather than half of a document
    /// describing all of it.
    /// </summary>
    private void Write(RunJournalEntry entry) => Append(_journalPath, RunJournal.RenderEntry(entry));

    private static void Append(string journalPath, string line) =>
        File.AppendAllText(RunmobileStore.PrepareForWrite(journalPath), line);

    /// <summary>
    /// Hands a fight that has just started to the observer, and takes it back when it
    /// ends.
    ///
    /// The fight goes through <see cref="PlayerFightObserver"/> rather than through the
    /// queue above, because inside a fight the question of when the engine has settled
    /// is a harder one and that class already answers it: an ended turn settles when
    /// the player's next turn begins, and a killing blow settles when the combat
    /// manager says the fight is over.
    /// </summary>
    private void StartOrStopWatchingTheFight()
    {
        if (_capture.Fight is not null && _observer is null)
        {
            // Refused rather than returned from: a fight the recording holds open and
            // nothing is watching is the silent gap this whole path exists to close.
            // In a fight is what the observer needs to attach at all, and after a
            // decision it is true by construction - the sample that opened the fight
            // said so - so the disagreement is reachable only on a resumed session
            // whose game came back somewhere the journal does not describe.
            if (!InAFight() || LiveRun.State is not { Players.Count: > 0 } run)
            {
                Refuse(
                    "The recording holds a fight open and this game is not in one the recorder can watch, so " +
                    "every decision left in that fight would go unrecorded.");
                _capture.Fight?.MarkIncomplete(
                    "The recorder picked this run back up somewhere it could not watch the fight from.");
                return;
            }

            // The recorder draws nothing, so it has nothing to re-derive when a sample
            // is taken; the transport's callback is the recorded-fight journey's. The
            // observer settles on this recorder's own clock, so a headless attach's
            // fight is drained the same way its decisions are.
            _observer = PlayerFightObserver.Start(
                run.Players[0], LiveRun.Sample, FightSink(), () => { }, () => { }, Clock,
                unmapped: StopAtFightAction);
            return;
        }

        if (_capture.Fight is null && _observer is not null)
        {
            _observer.Dispose();
            _observer = null;
        }
    }

    /// <summary>
    /// A fight action is about to be enqueued and the observer that records it may not
    /// be watching yet: attaches it at the fight's start rather than a poll later.
    ///
    /// The observer attaches when the decision that opened the fight is committed, and
    /// that decision settles on a poll: the fight is ready for the player, the pump
    /// sees it quiet twice, and only then is the map move written and the observer
    /// started. An action requested inside that window reached an executor nobody was
    /// subscribed to and went unrecorded, with the recording still reporting
    /// <c>integrity = complete</c> - a potion drunk on the first turn of a fight, in
    /// one retail session, and the three Strikes of another. So the fight's first
    /// action is where the fight-opening decision is closed instead: the state this
    /// action begins from is the state that decision left, the decision is committed
    /// here and now, and the observer is attached before the action reaches the
    /// executor. Where that decision's work is still in flight, or where nothing is
    /// pending and still nothing is watching, the action would go unrecorded, and the
    /// recording says so rather than carrying on complete.
    ///
    /// Asked of every claimed action and acting only on the five a fight is made of,
    /// inside a fight, with no observer; a potion used outside a fight is a decision of
    /// its own patch and never reaches this.
    /// </summary>
    private void WatchTheFightBefore(GameAction action)
    {
        if (_finished || _capture.Stop is not null) return;
        if (_observer is not null || !PlayerFightObserver.IsAFightDecision(action) || !InAFight()) return;

        // Read the way any decision is read, which closes the fight-opening decision
        // on this instant or refuses; the ticket is this reading's own and is placed
        // at once, since the observer takes the action's own reading at the executor
        var now = ReadBefore(action.GetType().Name);
        if (now is null) return;

        TakeEveryDecisionReadAfter();
        PlaceTheSavesAskedDuringTheDecision(now.Ticket);

        if (_observer is null && _capture.Stop is null)
        {
            Refuse(
                $"A {action.GetType().Name} was requested inside a fight before the recorder was watching it, so " +
                "what it did would go unrecorded.");
        }
    }

    /// <summary>
    /// The fight's samples, translated into decisions of the run.
    ///
    /// <see cref="PlayerFightObserver"/> speaks in the before-and-after of one fight
    /// and <see cref="RunCapture"/> speaks in the settled state after each decision of
    /// a run. This is the whole of the translation: hold the verb and arguments the
    /// observer opened a step with, and record the decision when it closes that step.
    /// The fight's own rules stay with the <see cref="FightCapture"/> the run capture
    /// keeps for it.
    ///
    /// Built out of delegates rather than by implementing the interface here, because
    /// the game enumerates this assembly's types before this mod can tell the runtime
    /// where <c>Sts2PilotTrainer.Replay</c> is; a type in here that implemented an
    /// interface from there would fail to load and take the whole mod with it. See
    /// <see cref="DelegatingFightSampleSink"/>.
    /// </summary>
    private IFightSampleSink FightSink() => new DelegatingFightSampleSink(
        beginStep: (verb, args, before, previousFinished) =>
        {
            if (!CloseStrandedFightStep(verb, before, previousFinished)) return;

            _openFightStep = (verb, args, ReadingOf(before));
        },
        beginStepWithUnresolvedArgument: (verb, args, before, previousFinished, member, unresolved) =>
        {
            // The decision before this one is closed the same way the ordinary path
            // closes it.
            if (!CloseStrandedFightStep(verb, before, previousFinished)) return;

            StopAtFightStep(member, verb, args, ReadingOf(before), unresolved);
        },
        resumeStep: ResumeFightStep,
        completeStep: CloseFightStep,
        finish: FinishFight,
        markIncomplete: Refuse);

    /// <summary>
    /// A decision inside a fight the observer saw and could describe only in part.
    ///
    /// The format requires the argument, and a decision written without it is one
    /// nobody can replay, so the recording stops at it with what the observer did read.
    /// What is written down is the game's own action - <paramref name="member"/> is the
    /// <c>GameAction</c> type the observer met, <c>PlayCardAction</c> and not the
    /// format's <c>PlayCard</c> - because a stop names what the recorder met for a
    /// later build to read, and the verb is this recorder's translation of it; the verb
    /// travels as the discriminator instead, so the stop still says which decision the
    /// format would have made of it.
    /// </summary>
    internal void StopAtFightStep(
        string member, string verb, IReadOnlyDictionary<string, string> args, TakenReading before,
        string unresolved)
    {
        StopAtFightStep(new UnmappedFacts(UnmappedDecision.MemberSeam, member, verb, Args(args), unresolved), before);
    }

    /// <summary>
    /// The one writer of a stop inside a fight, whatever seam met the decision: the
    /// fight's capture is marked incomplete with the same sentence the stop carries,
    /// and the recording stops at the state the decision began from.
    /// </summary>
    private void StopAtFightStep(UnmappedFacts met, TakenReading before)
    {
        _capture.Fight?.MarkIncomplete(met.Note ?? $"The recorder met {met.Seam} {met.Name} inside a fight.");
        StopAt(met, before);
    }

    /// <summary>
    /// A game action the observer met at the executor and nothing here claims.
    ///
    /// The observer's default hands over what it hands <see cref="FightSink"/> for a
    /// decision - the state sampled before the action and whether the step still open
    /// had finished - so the decision before the stranger is closed exactly as it is
    /// before any decision, on this sample, and the stop stands at the ordinal after
    /// it rather than taking that ordinal and dropping the step when its after-sample
    /// arrives. The stop names the action's own type, at the same seam
    /// <see cref="ActionRequested"/> names one at.
    /// </summary>
    private void StopAtFightAction(
        GameAction action, IReadOnlyDictionary<string, string> before, bool previousFinished)
    {
        if (!CloseStrandedFightStep(action.GetType().Name, before, previousFinished)) return;

        StopAtFightStep(MetAtNetAction(action), ReadingOf(before));
    }

    /// <summary>
    /// Closes the decision still open when another begins, and says whether the one
    /// beginning may be acted on.
    ///
    /// Two actions can arrive with nothing between them, and the difference between a
    /// sample that is exact and one that is a guess is whether the open one had already
    /// finished executing. Where it had, the state this action begins from <em>is</em>
    /// the state that one left. Where it had not, the two overlap: the after-state and
    /// the boundary digest read here would describe an instant the engine was never
    /// settled in, with part of the first action still outstanding, so the recording
    /// says so instead - the same answer <see cref="FightCapture.BeginStep"/> gives,
    /// because a run recorded that way replays into a different run.
    ///
    /// Both entries into this sink go through here, so neither can acquire its own
    /// sequencing rule.
    /// </summary>
    private bool CloseStrandedFightStep(
        string verb, IReadOnlyDictionary<string, string> before, bool previousActionFinished)
    {
        if (_openFightStep is not { } stranded) return true;

        _openFightStep = null;
        if (!previousActionFinished)
        {
            var reason =
                $"A '{verb}' began while the '{stranded.Verb}' before it had not been sampled afterwards, so " +
                "the recording cannot say what each of them did.";
            Refuse(reason);
            _capture.Fight?.MarkIncomplete(reason);
            return false;
        }

        CommitFightStep(stranded.Verb, stranded.Args, stranded.Before, before);
        return true;
    }

    /// <summary>
    /// The open decision's action paused for a choice and is carrying on. Nothing to
    /// record: the decision closes when the action finishes, as it would have without
    /// the pause. With no decision open it is the refusal <see cref="FightCapture.ResumeStep"/>
    /// raises, said here because this sink holds the open step rather than that capture.
    /// </summary>
    private void ResumeFightStep()
    {
        if (_openFightStep is not null) return;

        const string reason =
            "An action resumed after a player's choice with no decision open, so the recording did not see " +
            "it begin and cannot say what it did.";
        Refuse(reason);
        _capture.Fight?.MarkIncomplete(reason);
    }

    private void CloseFightStep(IReadOnlyDictionary<string, string> after)
    {
        if (_openFightStep is not { } open) return;
        _openFightStep = null;
        CommitFightStep(open.Verb, open.Args, open.Before, after);
    }

    /// <summary>
    /// The observer's sample with the digest of the same instant beside it.
    ///
    /// The sink speaks in samples and a journal line carries the whole digest too; the
    /// observer samples the moment an action begins and this runs inside that same
    /// call, so the two are readings of one instant.
    /// </summary>
    private TakenReading ReadingOf(IReadOnlyDictionary<string, string> sample)
    {
        return new TakenReading(sample, LiveRun.Project().Digest(), LiveRun.RunClockMs(), OpenTicket());
    }

    /// <summary>How many decisions are read and not yet dealt with.</summary>
    internal int OpenTicketCount
    {
        get
        {
            lock (Gate) return _openTickets.Count;
        }
    }

    /// <summary>The ticket read most recently and still open, or 0 where none is:
    /// the decision whose work the engine is inside right now, by the rule
    /// <see cref="NoticeSave"/> places a save by.</summary>
    private long LatestOpenTicket
    {
        get
        {
            lock (Gate) return _openTickets.Count == 0 ? 0 : _openTickets.Max();
        }
    }

    /// <summary>A ticket for a decision being read now, open until the pump or the
    /// fight observer has dealt with it.</summary>
    private long OpenTicket()
    {
        lock (Gate)
        {
            var ticket = ++_tickets;
            _openTickets.Add(ticket);
            return ticket;
        }
    }

    /// <summary>
    /// The fight is over.
    ///
    /// A fight that ended with no action open is refused rather than passed over, which
    /// is the refusal <see cref="FightCapture.Finish"/> raises and which reaching it
    /// through <see cref="CloseFightStep"/> would swallow. Left unsaid it is worse than
    /// a gap: the run's capture keeps the fight live, so the next decision the player
    /// makes out of combat - claiming loot, moving on the map - is recorded as an action
    /// inside that fight and closes it as one fought to its end.
    /// </summary>
    private void FinishFight(IReadOnlyDictionary<string, string> final)
    {
        if (_openFightStep is not null)
        {
            CloseFightStep(final);
        }
        else
        {
            const string reason =
                "The fight ended with no action being sampled, so its end belongs to nothing the recording " +
                "holds. The history is not a continuous account of this run.";
            _capture.Fight?.MarkIncomplete(reason);
            Refuse(reason);
        }

        // The run ended inside this fight and waited for exactly this
        if (_outcomeAwaitingFightEnd is { } pending)
        {
            _outcomeAwaitingFightEnd = null;
            Finish(pending);
        }
    }

    /// <summary>
    /// Records one decision made inside a fight, with the state the observer sampled
    /// after it.
    ///
    /// The digest is read here rather than handed over, because a sink speaks in
    /// samples and a boundary is identified by the whole canonical state. Both are
    /// readings of the same instant: the observer takes its sample the moment the
    /// engine settles and this runs inside that same call.
    ///
    /// The one step that is not read here is the one that ends the fight while the
    /// run goes on. The observer's sample is taken when the killing action finishes,
    /// and the engine is not settled there: it goes on to end the combat, take the
    /// fight-won save and roll the rewards, and rolling the rewards moves a random
    /// stream. A reading taken before that names a state the run is never in again -
    /// the headless replay reads the same step once the engine has drained, and the
    /// game's own Continue restores the fight-won save into a run that has rolled
    /// its rewards - so a resume after a loot-screen quit could never find the state
    /// the game came back in. That step goes through the pump like a decision made
    /// outside a fight, and is read once the engine has settled, in its turn before
    /// anything the loot screen then announces. A fight the run was lost in is read
    /// here: the run is over and nothing rolls, and <see cref="Finish"/> follows.
    /// </summary>
    private void CommitFightStep(
        string verb,
        IReadOnlyDictionary<string, string> args,
        TakenReading before,
        IReadOnlyDictionary<string, string> after)
    {
        if (_finished || _disposed) return;

        if (_outcomeAwaitingFightEnd is null && _capture.Stop is null &&
            !string.Equals(after.GetValueOrDefault("combat.outcome"), "in_progress", StringComparison.Ordinal))
        {
            lock (Gate)
            {
                _pending.Enqueue(new PendingDecision(verb, args, null, before, FightEnd: true));
                if (_pumping) return;
                _pumping = true;
            }

            _ = Pump();
            return;
        }

        // The fight is still watched past a stop, so its end is still seen; what it
        // decides is not recorded, the way nothing after a stop is.
        if (_capture.Stop is { } stop)
        {
            Log.Info(
                $"[{RunmobileMod.ModId}] a {verb} inside a fight came after the recording stopped at decision " +
                $"{Number(stop.Decision.Seq)}, so it is not recorded", 2);
            return;
        }

        try
        {
            if (!Enum.TryParse<ActionVerb>(verb, out var parsed))
            {
                Refuse($"The fight observer announced '{verb}', which is not a decision this format names.");
                return;
            }

            var digest = LiveRun.Project().Digest();
            var clock = LiveRun.RunClockMs();

            List<ScreenAnswer> answers;
            lock (Gate)
            {
                answers = _screenAnswers.ToList();
                _screenAnswers.Clear();
            }

            // As in Commit: a screen this step opened answered with something the
            // recorder could not name, and the step is where the recording stops.
            if (StopAmong(answers) is { } met)
            {
                StopAt(met, before);
                return;
            }

            var reading = new StateReading(after, digest);
            Write(_capture.Record(parsed, args, before.AsStateReading(), reading, clock));
            WriteAnswers(answers, reading, clock);

            StartOrStopWatchingTheFight();
        }
        catch (Exception ex)
        {
            Refuse($"A {verb} inside a fight could not be recorded: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // The save a won fight takes is asked inside the killing play, and holds
            // the state that play left.
            PlaceTheSavesAskedDuringTheDecision(before.Ticket);
        }
    }

    // ── The game's own saves ─────────────────────────────────────────────────────

    /// <summary>
    /// The game asked for a save of the run. Called from the postfix on
    /// <c>SaveManager.SaveRun</c> with the task the game handed back.
    ///
    /// The game saves at five moments and nowhere else: as the run begins, at every
    /// map-point arrival before the room is rolled, when a fight is won, when an
    /// ancient event finishes, and again with the reload count on Continue. The
    /// Continue button restores the latest of them, so where they were is what tells
    /// the game's own return to its save from a reload of an older one, and the
    /// recorder is the only thing in a position to write it down. The run-start save
    /// and the Continue's own come before this recorder attaches and are never
    /// noticed; the first is what a rollback to the opening reading returns to, and
    /// the second is the save the run was just restored from.
    /// </summary>
    internal static void SaveAsked(Task? saveTask)
    {
        var recorder = Active;
        if (recorder is null || recorder._finished || recorder._disposed) return;
        if (ProfileWriteBarrier.IsActive) return;

        try
        {
            recorder.NoticeSave(saveTask);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] the game's save could not be noted, so a Continue from it would read " +
                $"as a reload: {ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Places the save in the history and waits for it to land.
    ///
    /// Two things have to be known before the line is written, and they arrive
    /// separately. Which decision the save holds the state after: a save asked while
    /// a decision is in flight - the map move that saves on arrival, the card play
    /// that ends the fight, the event option that finishes an ancient event - holds
    /// the state that decision leaves, and is placed once the decision is written;
    /// one asked with nothing in flight holds the state after the last decision
    /// recorded. In flight is from the prefix that reads the decision's before-state
    /// to the pump writing it: the member executes between the two, and the map
    /// move's save is asked inside it, before its postfix has announced anything.
    /// Which decision, where two are in flight, is the one read most recently. And whether the save reached the disk: the Continue button restores
    /// what is there, so a save the game asked for and never finished writing is no
    /// save point, and a line for it would read the honest Continue after it as a
    /// reload of an older save. The game's own task says when it has landed.
    /// </summary>
    private void NoticeSave(Task? saveTask)
    {
        var pending = new PendingSave();
        lock (Gate)
        {
            if (_openTickets.Count == 0) pending.AfterSeq = _capture.NextSeq - 1;
            else pending.Ticket = _openTickets.Max();
            _saves.Add(pending);
        }

        _ = AwaitSave(pending, saveTask);
    }

    private async Task AwaitSave(PendingSave pending, Task? saveTask)
    {
        try
        {
            if (saveTask is not null) await saveTask;
        }
        catch (Exception ex)
        {
            lock (Gate) _saves.Remove(pending);
            Log.Warn(
                $"[{RunmobileMod.ModId}] the game's save did not complete, so it is not a save point of this " +
                $"recording: {ex.GetType().Name}: {ex.Message}", 2);
            return;
        }

        lock (Gate) pending.Landed = true;
        WriteSavePointIfSettled(pending);
    }

    /// <summary>
    /// The decision holding <paramref name="ticket"/> is on the file, or was dropped:
    /// every save asked while it was the one executing holds the state the history
    /// now ends in.
    ///
    /// Only that ticket closes, and a ticket is the one thing that places a save. An
    /// older ticket still open is not necessarily stale: the loot screen's skip is
    /// declined from inside the map move that leaves the room, so the move's ticket
    /// is older than the skip's and is still executing when the skip commits, and the
    /// arrival's save is asked after that. A ticket whose decision never reaches the
    /// pump - a prefix read, and the member threw - holds its saves until the
    /// recording finishes, where <see cref="DropTheSavesNeverPlaced"/> lets them go.
    /// Placing one on whatever decision came later would name a decision the save
    /// does not hold, and a Continue to that decision would then read as the game's
    /// own return rather than the reload it is; a save never placed only costs an
    /// honest Continue its continuity, which is the safe side.
    /// </summary>
    private void PlaceTheSavesAskedDuringTheDecision(long ticket)
    {
        List<PendingSave> placed;
        lock (Gate)
        {
            _openTickets.Remove(ticket);
            placed = [];
            foreach (var save in _saves.Where(save => save.AfterSeq is null && save.Ticket == ticket))
            {
                save.AfterSeq = _capture.NextSeq - 1;
                placed.Add(save);
            }
        }

        foreach (var save in placed) WriteSavePointIfSettled(save);
    }

    /// <summary>The saves still waiting on a ticket at the end of the recording are
    /// let go, and said once, by count: the decision each was asked during never
    /// reached the file, so nothing can say which decision the save holds.</summary>
    private void DropTheSavesNeverPlaced()
    {
        int unplaced;
        lock (Gate)
        {
            unplaced = _saves.RemoveAll(save => save.AfterSeq is null);
        }

        if (unplaced == 0) return;

        Log.Warn(
            $"[{RunmobileMod.ModId}] {Number(unplaced)} save(s) the game asked for were never placed in this " +
            "recording, because the decision each was asked during was never recorded; a Continue from one " +
            "of them would read as a reload of an older save", 2);
    }

    /// <summary>Writes the save point once it is both placed and landed, whichever
    /// came second.</summary>
    private void WriteSavePointIfSettled(PendingSave save)
    {
        int afterSeq;
        lock (Gate)
        {
            if (!save.Landed || save.AfterSeq is not { } placed) return;
            if (!_saves.Remove(save)) return;
            afterSeq = placed;
        }

        if (_finished || _disposed) return;

        try
        {
            if (_capture.MarkSavePoint(afterSeq) is { } line)
            {
                Journal(line, "the game's save point", "a reload of an older save");
            }
        }
        catch (ManifestException ex)
        {
            Log.Warn(
                $"[{RunmobileMod.ModId}] the game's save could not be placed in this recording, so a Continue " +
                $"from it would read as a reload: {ex.Message}", 2);
        }
    }

    /// <summary>A save the game asked for: which decision it holds the state after,
    /// once that is known, and whether it has reached the disk.</summary>
    private sealed class PendingSave
    {
        internal int? AfterSeq;
        internal long Ticket;
        internal bool Landed;
    }

    // ── The bookmark ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Exactly the facts the bookmark tag is derived over, read off the active
    /// recorder, or none where nothing is recording.
    ///
    /// Four are the capture's own. The fifth is this recorder's: whether a map move is
    /// announced and not yet recorded. The capture cannot say that - a move reaches it
    /// only once the engine has settled at the other end, by which time the next fight
    /// is on screen and its first turn has begun - and a tag derived without it drew
    /// the previous fight's bookmark over the opening frames of the next.
    /// </summary>
    internal static FightMarkFacts FightMarkFacts()
    {
        var recorder = Active;
        if (recorder is null || recorder._disposed)
        {
            return new FightMarkFacts(false, null, null, MovedOn: false, LeavingTheFloor: false, Bookmarked: false);
        }

        var capture = recorder._capture;
        var fight = capture.LastEndedFight;
        return new FightMarkFacts(
            true,
            capture.State,
            fight,
            capture.MovedOnFromLastFight,
            recorder.MapMoveAnnounced(),
            fight is { } ended && capture.IsBookmarked(ended));
    }

    /// <summary>
    /// Whether a map move has been announced to this recorder and is still waiting for
    /// the engine to settle before it is recorded.
    ///
    /// The queue is the one place a decision is between announced and recorded, and a
    /// map move is in it from the node being pressed - <see cref="MapMove"/>'s postfix
    /// - to the room at the other end being built and its first fight, if any, begun.
    /// A move the engine turned down leaves the queue unrecorded, and the tag comes
    /// back with it, because the run is still on the floor it never left.
    /// </summary>
    private bool MapMoveAnnounced()
    {
        lock (Gate)
        {
            return _pending.Any(
                decision => string.Equals(decision.Verb, nameof(ActionVerb.MapMove), StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The bookmark was pressed: the fight that just ended is marked, or unmarked
    /// where it already was.
    ///
    /// Nothing here decides whether a press is possible; <see cref="FightMark.For"/>
    /// draws the control only where it is, and this asks the capture for the same
    /// fight. A press with no such fight is nothing rather than an error.
    /// </summary>
    internal static void ToggleBookmark()
    {
        var recorder = Active;
        if (recorder is null || recorder._disposed) return;

        try
        {
            var capture = recorder._capture;
            if (capture.LastEndedFight is not { } fight || capture.MovedOnFromLastFight || recorder.MapMoveAnnounced())
            {
                return;
            }

            recorder.Bookmark(fight, !capture.IsBookmarked(fight));
        }
        catch (Exception ex)
        {
            Log.Error($"[{RunmobileMod.ModId}] the bookmark could not be saved: {ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Appends the press to the journal, and where the run has already ended, writes
    /// the manifest again.
    ///
    /// A lost fight is bookmarked on the game's death screen, and by then the manifest
    /// is on disk: the game writes its run history in <c>RunManager.OnEnded</c> and
    /// draws the screen afterwards, and <see cref="Finish"/> follows the first. So the
    /// press reaches a file that exists, through the same path, the same serializer
    /// and the same containment gate <see cref="Finish"/> wrote it with. The rewrite
    /// changes nothing but <c>source.native.bookmarks</c>, which the identity of the
    /// run, the history hash and every boundary digest are unaffected by.
    ///
    /// Both writes happen before the capture holds the press, so a write that fails
    /// leaves the tag drawing what is on disk rather than a mark that reached nothing.
    /// </summary>
    private void Bookmark(int fight, bool on)
    {
        _capture.MarkBookmark(fight, on, line =>
        {
            Append(_journalPath, line);
            if (!_finished) return;

            var path = $"{RecordingsDirectory}/{_capture.RunId}{RecordingLibrary.ManifestExtension}";
            RunmobileStore.Write(path, ManifestJson.Serialize(_capture.ToManifest()) + "\n");
        });
    }

    // ── Finishing ────────────────────────────────────────────────────────────────

    /// <summary>The outcome the run ended with while its last fight was still
    /// live, held until the engine says that fight is over.</summary>
    private string? _outcomeAwaitingFightEnd;

    /// <summary>
    /// The run is over. Finishes the recording now, or once the fight it ended
    /// inside has ended.
    ///
    /// The game ends a lost run from inside the enemy turn that killed the player -
    /// <c>RunManager.OnEnded</c> and the death screen come first, and the combat
    /// manager processes its pending loss and raises <c>CombatEnded</c> afterwards.
    /// A recording finished at the first of those has the killing turn still open
    /// and the fight left live, so the lost fight got no end, no boundary and no
    /// line, and the history stopped one decision short of where the run did while
    /// reporting a continuous watch. So a loss with a fight live waits for the
    /// engine's own word, which <see cref="FinishFight"/> receives with the reading
    /// the fight ended in, and finishes then. A win or a give-up has no such fight
    /// live and finishes here.
    /// </summary>
    private void End(string outcome)
    {
        if (_finished) return;

        if (string.Equals(outcome, "lost", StringComparison.Ordinal) && _capture.Fight is not null && _observer is not null)
        {
            _outcomeAwaitingFightEnd = outcome;
            return;
        }

        Finish(outcome);
    }

    /// <summary>
    /// The safety net under <see cref="End"/>: a lost run torn down before the engine
    /// ended its fight is still finished, with the fight left as it stood. Said in
    /// the log, because a recording finished here is one whose death screen offered
    /// no bookmark.
    /// </summary>
    private void FinishIfStillWaitingForTheFightToEnd()
    {
        if (_outcomeAwaitingFightEnd is not { } pending || _finished) return;

        Log.Warn(
            $"[{RunmobileMod.ModId}] the run was torn down before the engine ended the fight it was lost in, " +
            "so the recording is finished with that fight left open", 2);
        _outcomeAwaitingFightEnd = null;
        Finish(pending);
    }

    private void Finish(string outcome)
    {
        if (_finished) return;
        _finished = true;

        _observer?.Dispose();
        _observer = null;

        // The step that ended the run's last fight may still be waiting for the engine
        // to settle, and the run ending is the engine settling: it is read now, in its
        // turn, so a won run's killing blow is in the history it finishes.
        //
        // So is the decision the run ended inside. The game wins a run from the
        // Architect's PROCEED, and RunManager.OnEnded runs within that very call -
        // synchronously, under Instant fast mode, before the pump has polled once -
        // so the decision is still queued when this runs. Its settling and the run's
        // end are the same instant: OnEnded is called with the run's final state,
        // before GuaranteeKillAllPlayers kills the player creature, and this postfix
        // reads it there. Taken only where it is the one decision left and the engine
        // is quiet, which is the settle's own condition asked once; a decision still
        // in flight, or one of several, has no state this reading could honestly be.
        var settledByTheEnd = new List<PendingDecision>();
        lock (Gate)
        {
            while (_pending.Count > 0 && _pending.Peek().FightEnd) settledByTheEnd.Add(_pending.Dequeue());
            if (_pending.Count == 1 && EngineIsQuiet()) settledByTheEnd.Add(_pending.Dequeue());
        }

        foreach (var step in settledByTheEnd)
        {
            try
            {
                if (step.Unmapped is { } met)
                {
                    StopAt(met, step.Before);
                }
                else
                {
                    Commit(step.Verb, step.Args, step.Before, step.ReadAfter);
                }
            }
            catch (Exception ex)
            {
                Refuse($"A {step.Verb} the run ended on could not be recorded: {ex.GetType().Name}: {ex.Message}");
            }

            PlaceTheSavesAskedDuringTheDecision(step.Before.Ticket);
        }

        // A decision announced and not yet read is a decision this recording cannot
        // describe. Said out loud rather than dropped: the history would be missing it,
        // and a history missing decisions replays into a different run.
        int stranded;
        lock (Gate) stranded = _pending.Count;
        if (stranded > 0)
        {
            Refuse(
                $"The run ended with {stranded.ToString(CultureInfo.InvariantCulture)} decision(s) the " +
                "recorder had not finished reading, so the history stops short of where the run did.");
        }

        DropTheSavesNeverPlaced();

        // Read the same registry again at the far end of the run. A lazy patch can
        // attach after the start reading and affect gameplay while the recording
        // still names only the earlier environment; the two readings travel through
        // the journal and manifest together so the preflight can refuse that claim.
        Append(_journalPath, _capture.RecordPatchRosterAtRunEnd(HarmonyRoster.Read()));
        _capture.Finish(outcome);

        var manifest = _capture.ToManifest();
        var path = $"{RecordingsDirectory}/{_capture.RunId}{RecordingLibrary.ManifestExtension}";
        RunmobileStore.Write(path, ManifestJson.Serialize(manifest) + "\n");

        var problems = ManifestValidator.Validate(manifest);
        Log.Info(
            $"[{RunmobileMod.ModId}] recorded {_capture.RunId}: {outcome}, " +
            $"{manifest.Actions.Count.ToString(CultureInfo.InvariantCulture)} decision(s), " +
            $"{manifest.Boundaries.Count.ToString(CultureInfo.InvariantCulture)} boundary/boundaries, " +
            $"continuity {_capture.Continuity}, integrity {_capture.Integrity}, written to {path}", 2);

        if (!problems.IsValid)
        {
            // Said out loud rather than swallowed. A recording this build cannot
            // validate is still written - it is what happened - and the player deserves
            // to know it will not pass a gate rather than discovering it later.
            Log.Warn(
                $"[{RunmobileMod.ModId}] this recording does not validate:\n{problems.Describe()}", 2);
        }
    }

    /// <summary>
    /// This recording cannot account for the run continuously.
    ///
    /// Written to the journal as well as held, because a broken watch is the one fact
    /// about a recording nothing downstream can establish: a session continued after
    /// this one would find a journal whose last digest matches the live game and
    /// publish the hole as a continuous account of the run.
    /// </summary>
    internal void Refuse(string reason)
    {
        // Past a stop nothing is recorded, so nothing past it can go unrecorded: the
        // history already ends at the decision the recorder could not name, and a hole
        // marked after it would claim a watch that stopped and started again, which
        // is not what happened.
        if (_capture.Stop is { } stop)
        {
            Log.Info(
                $"[{RunmobileMod.ModId}] after the recording stopped at decision " +
                $"{Number(stop.Decision.Seq)}: {reason}", 2);
            return;
        }

        var line = _capture.MarkBroken(reason);
        Log.Warn($"[{RunmobileMod.ModId}] {reason}", 2);
        Journal(line, "the refusal above", "continuous");
    }

    /// <summary>
    /// The recorder saw a decision and could not name it, and stops here.
    ///
    /// The other thing a refusal can be, and the one <see cref="Refuse"/> is not. The
    /// watch has no hole: the decision was seen, at the seam it was seen at, and what
    /// was met is written down raw so a later build can say what it was. The recording
    /// ends here with its integrity <c>unmapped</c> and its continuity untouched, which
    /// is what the format means by a stop; marking it broken instead would report a
    /// watch that stopped and started again, a cause that did not happen.
    ///
    /// Nothing past the stop is recorded, and nothing past it is refused either: a
    /// history that skipped a decision and carried on would replay into a run that
    /// never made it, so the capture takes no decision after this one.
    /// </summary>
    /// <param name="met">What was met, as the game named it.</param>
    /// <param name="before">The state the decision began from, which is the state the
    /// recording ends in.</param>
    internal void StopAt(UnmappedFacts met, TakenReading before)
    {
        if (_capture.Stop is { } already)
        {
            Log.Info(
                $"[{RunmobileMod.ModId}] the recording had already stopped at decision " +
                $"{Number(already.Decision.Seq)} when it met {met.Seam} {met.Name}", 2);
            return;
        }

        var seq = _capture.NextSeq;
        var decision = new UnmappedDecision
        {
            Seq = seq,
            Seam = met.Seam,
            Name = met.Name,
            Discriminator = met.Discriminator,
            Args = met.Args,
            Evidence = FactEvidence.AtActionOrdinal(seq, before.RunClockMs, met.Note),
        };

        string line;
        try
        {
            line = _capture.MarkUnmapped(decision, before.AsStateReading());
        }
        catch (ManifestException ex)
        {
            // Not stopped, so the decision went unrecorded and the watch has a hole
            // after all - which is the one claim Refuse makes.
            Refuse(
                $"The recorder met {ManifestValidator.Describe(decision)} and could not stop the recording " +
                $"there: {ex.Message}");
            return;
        }

        Log.Warn(
            $"[{RunmobileMod.ModId}] the recording stopped at decision {Number(seq)}, which the recorder " +
            $"could not name: {ManifestValidator.Describe(decision)}. Everything before it is kept and nothing " +
            "after it is recorded.", 2);
        Journal(line, "the stop above", "recording past its stop");
    }

    /// <summary>
    /// A decision met at a member the recorder patches, outside a fight, which it saw
    /// and could not name.
    ///
    /// Queued behind the decisions still settling rather than stopping the capture on
    /// the spot, because a stop stands at the ordinal after the last decision recorded
    /// and the decisions ahead of it in the queue are recorded first. Its reading is
    /// taken now, in the prefix, the way every decision's is; a stop item brings it as
    /// its after-reading too, so the pump takes it without waiting on the engine.
    /// </summary>
    private static void StopAtDecision(UnmappedFacts met)
    {
        var recorder = Active;
        if (recorder is null || recorder._finished) return;
        // The stop's reading is the state the recording ends in, and it was taken now,
        // so the decisions still settling ahead of it are closed on it the way any
        // decision closes them
        if (ReadBefore(met.Name) is not { } before) return;

        lock (Gate)
        {
            recorder._pending.Enqueue(
                new PendingDecision(met.Name, met.Args, null, before, before, met));
            if (recorder._pumping) return;
            recorder._pumping = true;
        }

        _ = recorder.Pump();
    }

    /// <summary>
    /// A screen answered with something the recorder could not name.
    ///
    /// Held beside the screen's answers rather than stopping the capture now, because
    /// the screen was answered from inside the decision that opened it and that
    /// decision has not settled. When it does, the stop stands at that decision's
    /// ordinal, with its before-reading: recorded without its answer, the decision
    /// would be one a replay makes differently.
    /// </summary>
    private static void StopAtScreenAnswer(UnmappedFacts met)
    {
        var recorder = Active;
        if (recorder is null || recorder._finished) return;

        recorder.HoldScreenAnswerStop(met);
    }

    internal void HoldScreenAnswerStop(UnmappedFacts met)
    {
        lock (Gate)
        {
            _screenAnswers.Add(new ScreenAnswer(met.Name, met.Args, met));
        }
    }

    /// <summary>The stop a screen's answers carry, if one of them is one.</summary>
    private static UnmappedFacts? StopAmong(IEnumerable<ScreenAnswer> answers) =>
        answers.Select(answer => answer.Unmapped).FirstOrDefault(met => met is not null);

    /// <summary>
    /// Writes a refusal's or a stop's line to the journal, or says why it could not.
    /// </summary>
    /// <param name="what">The log line above this one, for the error to point at.</param>
    /// <param name="misread">What a session continued from a journal without the line
    /// would take the recording for.</param>
    private void Journal(string line, string what, string misread)
    {
        try
        {
            Append(_journalPath, line);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] {what} could not be written to the journal, so a session " +
                $"continued from it would read this recording as {misread}: {ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _observer?.Dispose();
        _observer = null;
    }

    /// <summary>
    /// A decision waiting for the engine to finish it.
    ///
    /// The verb is a name rather than an <see cref="ActionVerb"/>, and so is the one in
    /// <see cref="Decision"/> and <see cref="_openFightStep"/>: a field holding a value
    /// of a sibling assembly's type decides this assembly's layout, and the game loads
    /// its types before this mod can say where that sibling is. The names are parsed
    /// back at the one place that records them.
    /// </summary>
    /// <param name="SettlesOnceHandedToThePlayer">Whether the decision's work is read as
    /// finished once the engine has handed the run to the player inside it, which
    /// <see cref="HandedToThePlayerDuring"/> reads and only an event option's work
    /// says; every other decision waits for its own task to finish.</param>
    private sealed record PendingDecision(
        string Verb, IReadOnlyDictionary<string, string> Args, Task? EngineWork,
        TakenReading Before, TakenReading? Reading = null, UnmappedFacts? Unmapped = null,
        bool FightEnd = false, bool SettlesOnceHandedToThePlayer = false)
    {
        /// <summary>
        /// The reading the decision after this one began from, where that decision was
        /// announced with the engine already quiet and this one's work done: the state
        /// this decision left, read at the instant it was left rather than after the
        /// pump's next poll. Set by <see cref="CloseStrandedDecisions"/> and by nothing
        /// else; the pump reads it in place of a settle.
        /// </summary>
        public TakenReading? ClosedByTheNext { get; set; }

        /// <summary>The state this decision left, where it has been read: brought with
        /// the decision, or taken by the one after it.</summary>
        public TakenReading? ReadAfter => Reading ?? ClosedByTheNext;
    }

    /// <summary>
    /// A decision the recorder saw and could not name, as the game named it.
    ///
    /// Strings and nothing else, for the reason <see cref="PendingDecision"/>'s verb is
    /// a name: this is a field of two records in this assembly, and a field of a
    /// sibling assembly's type decides this assembly's layout before the mod can say
    /// where that sibling is. It becomes an <c>UnmappedDecision</c> at the one place
    /// that stops the recording, where the ordinal it stands at is known.
    /// </summary>
    /// <param name="Seam">Where it was seen: one of <c>UnmappedDecision.Seams</c>.</param>
    /// <param name="Name">The game's own name for the thing - the member it went
    /// through, or the screen it was answered on.</param>
    /// <param name="Discriminator">The subtype or kind the seam distinguishes by, where
    /// it has one.</param>
    /// <param name="Args">What the game handed over, as strings, uninterpreted.</param>
    /// <param name="Note">What the recorder could not do with it, in its own words,
    /// carried on the stop's evidence.</param>
    internal sealed record UnmappedFacts(
        string Seam, string Name, string? Discriminator, IReadOnlyDictionary<string, string> Args,
        string? Note = null);

    /// <summary>A decision met at a member this recorder patches, named by the
    /// member's declaring type and name the way the format asks for it. A constructor
    /// is passed under the runtime's own spelling, <c>.ctor</c>, and renders as one
    /// dotted name.</summary>
    internal static UnmappedFacts MetAtMember(
        Type declaringType, string member, string? discriminator, string note,
        params (string Name, string Value)[] args) =>
        new(
            UnmappedDecision.MemberSeam, $"{declaringType.Name}.{member.TrimStart('.')}", discriminator,
            Args(args), note);

    /// <summary>A decision met as a game action nothing here claims, at the seam every
    /// locally issued action enters by or at the executor about to run it: named by the
    /// action's own type, which is the game's name for the thing.</summary>
    internal static UnmappedFacts MetAtNetAction(GameAction action) =>
        new(
            UnmappedDecision.NetActionSeam, action.GetType().Name, action.ActionType.ToString(),
            Args(("owner_id", action.OwnerId.ToString(CultureInfo.InvariantCulture))),
            "No patch or observer of this recorder claims this game action, so the decision it carries " +
            "cannot be named.");

    /// <summary>A decision met as the answer to one of the game's own screens.</summary>
    internal static UnmappedFacts MetAtScreen(
        string screen, string? discriminator, string note, params (string Name, string Value)[] args) =>
        new(UnmappedDecision.PlayerChoiceSeam, screen, discriminator, Args(args), note);

    /// <summary>
    /// A reading of the run taken at one instant: the sample, the complete digest and
    /// the run clock.
    ///
    /// Every decision carries two - the one taken in the prefix of the member it goes
    /// through, which is the state it began from, and the one taken once the engine
    /// settled. A decision the engine performs synchronously inside another decision's
    /// work brings its after-reading with it as well, and <see cref="RewardsSkipped"/>
    /// says why: the ambient settle waits for the engine to go quiet, and the engine
    /// does not go quiet until the decision this one happened inside has finished - by
    /// which time the run is somewhere else entirely.
    /// </summary>
    internal sealed record TakenReading(
        IReadOnlyDictionary<string, string> Sample, string Digest, int? RunClockMs, long Ticket = 0)
    {
        internal StateReading AsStateReading() => new(Sample, Digest);
    }

    /// <summary>A decision read in a prefix and announced in the postfix beside it,
    /// where the engine hands back a task that says when it is finished. It carries
    /// the reading the prefix took, which is the state the decision began from.</summary>
    private sealed record Decision(string Verb, IReadOnlyDictionary<string, string> Args, TakenReading Before);

    /// <summary>
    /// One answer a screen gave, as the decision the format records it as.
    ///
    /// The verb is a name rather than an <see cref="ActionVerb"/> for the reason
    /// <see cref="PendingDecision"/>'s is. The arguments are complete, the alternative a
    /// screen also offered included: what a screen offered is only visible while it is
    /// open, and it is what <c>take-a-different-card</c> and
    /// <c>enchant-a-different-card</c> take instead.
    /// </summary>
    private readonly record struct ScreenAnswer(
        string Verb, IReadOnlyDictionary<string, string> Args, UnmappedFacts? Unmapped = null);

    private static IReadOnlyDictionary<string, string> Args(params (string Name, string Value)[] args) =>
        new SortedDictionary<string, string>(
            args.ToDictionary(arg => arg.Name, arg => arg.Value, StringComparer.Ordinal), StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> Args(IReadOnlyDictionary<string, string> args) =>
        new SortedDictionary<string, string>(
            args.ToDictionary(arg => arg.Key, arg => arg.Value, StringComparer.Ordinal), StringComparer.Ordinal);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Which floor a sampled reading is of, or -1 where it does not say.</summary>
    private static int Floor(IReadOnlyDictionary<string, string> sample) =>
        sample.TryGetValue("run.total_floor", out var value) &&
        int.TryParse(value, System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out var floor)
            ? floor
            : -1;

    // ── The patches ──────────────────────────────────────────────────────────────
    //
    // Every one of them reads and returns. None issues a command, none changes an
    // argument, and none changes what the game decides. The two that touch a returned
    // task hand back exactly what the game produced, having looked at it on the way
    // past - which is the only way to see the answer a screen gave, because the engine
    // pulls that answer through a seam a player's client fills rather than through a
    // command anything else could observe.

    /// <summary>
    /// Every decision this recorder can write down.
    ///
    /// Listed rather than derived from the patches, because two of them are watched
    /// through <see cref="PlayerFightObserver"/> rather than through a patch of their
    /// own and a list read off the patch classes would quietly be short. What makes it
    /// checkable is that it has to equal <see cref="EngineCommands"/>'s mapped set:
    /// the driver replays a verb by calling the game's own member and this records one
    /// by watching the same member, so a verb one side has and the other does not is a
    /// recording that cannot be replayed or a replay of something nobody can record.
    /// </summary>
    internal static IReadOnlyList<ActionVerb> RecordedVerbs { get; } =
    [
        ActionVerb.ChooseNeowBlessing,
        ActionVerb.ChooseEventOption,
        ActionVerb.MapMove,
        ActionVerb.PlayCard,
        ActionVerb.EndTurn,
        ActionVerb.UndoEndTurn,
        ActionVerb.ClaimReward,
        ActionVerb.TakeCard,
        ActionVerb.TakeCardRewardAlternative,
        ActionVerb.SkipRewards,
        ActionVerb.SelectCardFromScreen,
        ActionVerb.ConfirmCardScreen,
        ActionVerb.SelectBundleFromScreen,
        ActionVerb.SelectRelicFromScreen,
        ActionVerb.ChooseRestSiteOption,
        ActionVerb.TakeChestRelic,
        ActionVerb.SkipChestRelic,
        ActionVerb.ProceedToNextAct,
        ActionVerb.ShopPurchase,
        ActionVerb.UsePotion,
        ActionVerb.DiscardPotion,
        ActionVerb.RevealCrystalSphereCell,
    ];

    /// <summary>
    /// The private funnel every declined reward set passes through.
    ///
    /// Named as a string because it is private. <see cref="RewardsSkipped"/> says why
    /// the funnel rather than the public call the driver makes, and
    /// <see cref="RecorderModule"/> refuses to install at all if a build renames it, so
    /// it cannot go quietly missing.
    /// </summary>
    internal const string SkipRewardsSetMember = "SkipRewardsSet";

    /// <summary>
    /// The private funnel every console command passes through, whichever way it was
    /// entered.
    ///
    /// The game has two public entries - <c>ProcessCommand(string)</c> for a command
    /// typed on this client and <c>ProcessNetCommand</c> for one a peer sent - and
    /// both reach this three-argument overload. Watching it rather than either entry
    /// is what makes one patch cover both, and the same reason <c>SkipRewardsSet</c>
    /// is watched rather than the call the driver makes.
    ///
    /// It is deliberately not the queue. A console command reaches the action queue
    /// only in a networked game, and as two types rather than one: the game's own
    /// generated <c>INetActionSubtypes</c> list holds the eleven <c>Net*</c> structs,
    /// of which the console's is <c>NetConsoleCmdGameAction</c>, and the action it
    /// builds and puts on the queue is <c>ConsoleCmdGameAction</c>. In singleplayer
    /// <c>DevConsole.ProcessCommand</c> takes the local branch and builds neither, and
    /// singleplayer is the only kind of run this recorder records. A
    /// watch on the queue would therefore have seen a console command in exactly the
    /// runs that are never recorded and none of the runs that are.
    /// </summary>
    internal const string ProcessConsoleCommandMember = "ProcessCommand";

    /// <summary>The three-argument overload's parameters, because
    /// <c>DevConsole</c> has a one-argument <c>ProcessCommand</c> beside it and a
    /// lookup by name alone is ambiguous.</summary>
    internal static Type[] ProcessConsoleCommandArguments => [typeof(Player), typeof(string), typeof(string[])];

    /// <summary>Every patch class this module installs, listed rather than discovered:
    /// <c>PatchAll</c> over the assembly would install the recorded-fight journey's too.</summary>
    internal static IReadOnlyList<Type> PatchClasses { get; } =
    [
        typeof(NewRun), typeof(ContinuedRun), typeof(RunOver), typeof(RunTeardown), typeof(RunSaved),
        typeof(EventOption), typeof(OptionChosen), typeof(RewardsOffered), typeof(CrystalSphereOpened),
        typeof(MapMove), typeof(RewardTaken), typeof(RewardsSkipped),
        typeof(RestSiteOptionTaken), typeof(ChestRelicTaken), typeof(ChestRelicSkipped),
        typeof(ActAdvanced), typeof(ShopPurchased), typeof(ShopCardRemovalPurchased),
        typeof(CardRewardScreen), typeof(PotionUsed), typeof(PotionDiscarded),
        typeof(ConsoleCommand), typeof(BundleScreen), typeof(RelicScreen), typeof(ChoiceSynced),
        typeof(CrystalSphereCellRevealed), typeof(ActionRequested),
    ];

    /// <summary>
    /// Every console command the game accepted, in a run or out of one.
    ///
    /// A postfix on the console's own funnel, reading the answer the console gave. Only
    /// a command it accepted counts: a typo is not a console command in the run's
    /// history, and the game's own <c>CmdResult.success</c> is what tells them apart -
    /// which is a reading rather than a judgement about which command names are real.
    ///
    /// Every accepted command counts, including the ones that only print something.
    /// Which of the game's commands change a run is not a judgement this mod is in a
    /// position to make, and the captain's ruling is about the console having been used
    /// at all. A run this was wrong about is one that stays on the player's disk and is
    /// refused for publication, which is the cheap direction to be wrong in.
    ///
    /// It reads and returns, like every patch here: the command runs exactly as the
    /// game wrote it and the recording is what changes.
    /// </summary>
    [HarmonyPatch(typeof(DevConsole), ProcessConsoleCommandMember,
        [typeof(Player), typeof(string), typeof(string[])])]
    internal static class ConsoleCommand
    {
        [HarmonyPostfix]
        internal static void After(CmdResult __result)
        {
            if (!__result.success) return;

            ConsoleCommandUsed();
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewSingleplayer))]
    internal static class NewRun
    {
        [HarmonyPostfix]
        internal static void After() => NoticeRun();
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedSingleplayer))]
    internal static class ContinuedRun
    {
        [HarmonyPostfix]
        internal static void After() => NoticeRun();
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded))]
    internal static class RunOver
    {
        [HarmonyPostfix]
        internal static void After(bool isVictory) => RunEnded(isVictory);
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
    internal static class RunTeardown
    {
        [HarmonyPostfix]
        internal static void After() => RunTornDown();
    }

    /// <summary>
    /// The game saving the run, at the member every one of its save sites reaches.
    ///
    /// A postfix, so it reads the task the game handed back and changes nothing about
    /// the save; the headless host's own prefix on this member and the trainer's write
    /// barrier both skip the original, and a postfix runs either way. The write
    /// barrier's case is never a recorded run, and <see cref="SaveAsked"/> refuses it
    /// besides.
    /// </summary>
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRun), [typeof(AbstractRoom), typeof(bool)])]
    internal static class RunSaved
    {
        [HarmonyPostfix]
        internal static void After(Task? __result) => SaveAsked(__result);
    }

    /// <summary>
    /// An event option, which is also how the opening blessing is chosen.
    ///
    /// The two are the same engine member and different verbs, because a blessing is
    /// offered before the run has a floor and the format records it without an event
    /// id. Told apart by the event's own model type rather than by the decision's
    /// position, so a run whose first decision is not Neow's is still recorded
    /// correctly.
    ///
    /// Announced in the prefix, as every decision is, and handed the option's own
    /// work from inside the call. The member returns nothing: the synchronizer starts
    /// the option's task and keeps it to itself, so a decision announced without it
    /// settled on the action queue alone, and the queue is idle while an option is
    /// between two of its awaits. In the retail client that gap is a real stretch of
    /// time - Brain Leech's RIP loses the health, awaits the player creature's hit
    /// animation, and only then rolls the card reward it offers - and the reading taken
    /// inside it named a state no replay holds: the health gone and the reward not yet
    /// rolled, so that every sampled field agreed with the replay and the random
    /// streams did not. The work announced here is a stand-in the engine's own task
    /// completes, handed over by <see cref="OptionChosen"/>; the settle then waits for
    /// the option's work to finish, or for the engine to hand the run to the player
    /// inside it (<see cref="HandedToThePlayerDuring"/>, which this decision alone opts
    /// into), and the reading is of the state the replay's own drain reaches.
    ///
    /// The announcement stays in the prefix because the Architect's PROCEED ends the
    /// run inside this very call: <see cref="Finish"/> reads the one decision still
    /// pending there, and a decision announced only by the postfix would arrive after
    /// the recording had finished and be lost without a word.
    /// </summary>
    [HarmonyPatch(typeof(EventSynchronizer), nameof(EventSynchronizer.ChooseLocalOption))]
    internal static class EventOption
    {
        /// <summary>The stand-in for the option's work, open from the prefix to the
        /// postfix; completed by the engine's own task once <see cref="OptionChosen"/>
        /// has handed it over, or by the postfix where the engine started none.</summary>
        private static TaskCompletionSource? _work;

        /// <summary>Whether the engine's own task has been handed over for the
        /// decision being read, so the stand-in follows it rather than the return.</summary>
        private static bool _followed;

        /// <summary>Whether a decision has been announced in the prefix and the member
        /// has not returned yet: the engine is inside it, and the option task started
        /// meanwhile is this decision's.</summary>
        internal static bool Reading => _work is not null;

        [HarmonyPrefix]
        internal static void Before(EventSynchronizer __instance, int index)
        {
            _work = null;
            _followed = false;
            if (Active is null) return;

            try
            {
                var model = __instance.GetLocalEvent();
                var options = model.CurrentOptions;
                if (index < 0 || index >= options.Count)
                {
                    StopAtDecision(MetAtMember(
                        typeof(EventSynchronizer), nameof(EventSynchronizer.ChooseLocalOption), model.Id.ToString(),
                        "The option is not one the event offers, so the recorder cannot say which one was chosen.",
                        ("option_index", Number(index)), ("offered", Number(options.Count))));
                    return;
                }

                // The option's own key beside its position, by the rule the driver
                // checks it with: what lets a build that reordered the options refuse
                // rather than take whatever sits at that index.
                var key = RunDriver.OptionKey(options[index]);
                var work = new TaskCompletionSource();
                _work = work;
                Announce(
                    model is Neow ? ActionVerb.ChooseNeowBlessing : ActionVerb.ChooseEventOption,
                    model is Neow
                        ? Args(("option_index", Number(index)), ("option_key", key))
                        : Args(("event_id", model.Id.ToString()), ("option_index", Number(index)), ("option_key", key)),
                    work.Task,
                    settlesOnceHandedToThePlayer: true);
            }
            catch (Exception ex)
            {
                _work = null;
                Active?.Refuse($"An event option could not be read: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>The engine's own task for the option, handed over by
        /// <see cref="OptionChosen"/> while the member is executing: the stand-in
        /// completes when it does.</summary>
        internal static void WorkStarted(Task task)
        {
            if (_work is not { } work) return;
            _followed = true;
            task.ContinueWith(
                _ => work.TrySetResult(),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        /// <summary>The member has returned. An option the engine started no task for
        /// has no work to wait on, and its stand-in completes here.</summary>
        [HarmonyPostfix]
        internal static void After()
        {
            if (_work is not { } work) return;
            var followed = _followed;
            _work = null;
            _followed = false;
            if (!followed) work.TrySetResult();
        }
    }

    /// <summary>
    /// The task an event option's work runs as, read on its way past.
    ///
    /// <c>EventOption.Chosen</c> is what the synchronizer starts for the option the
    /// player chose, and the postfix hands its task to <see cref="EventOption"/> only
    /// while that member is executing: the event room calls the same method directly
    /// for a Proceed button, which is no decision of the run, and a task started
    /// outside the window belongs to nothing this recorder is reading. The task itself
    /// is returned exactly as the game produced it.
    /// </summary>
    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Events.EventOption), nameof(MegaCrit.Sts2.Core.Events.EventOption.Chosen))]
    internal static class OptionChosen
    {
        [HarmonyPostfix]
        internal static void After(Task __result)
        {
            if (EventOption.Reading) EventOption.WorkStarted(__result);
        }
    }

    /// <summary>
    /// A rewards set put on offer to the player, watched where the engine begins one.
    ///
    /// The task the synchronizer hands back completes when the set does - every reward
    /// taken, or the set skipped - so a set whose task is still open is one the player
    /// is looking at. It is the reading <see cref="HandedToThePlayerDuring"/> asks: a
    /// decision whose own work offers rewards and then awaits them, as an event
    /// option that offers a card does, has settled once the set is on offer, because
    /// the decisions that answer the set are the player's next ones and are recorded
    /// on their own.
    /// </summary>
    [HarmonyPatch(typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.BeginRewardsSet))]
    internal static class RewardsOffered
    {
        private static Task? _set;
        private static long _during;

        /// <summary>Every set begun for the local player with the task that completes
        /// when it does, in the order begun: the synchronizer's own stack, which it
        /// keeps private, pops a set exactly when that task completes.</summary>
        private static readonly List<(RewardsSet Set, Task Completion)> _begun = [];

        /// <summary>Whether the player has a rewards set on offer right now that was
        /// begun inside the work of the decision holding <paramref name="ticket"/>.</summary>
        internal static bool OnOfferSince(long ticket) => _set is { IsCompleted: false } && _during == ticket;

        /// <summary>The set a reward clicked now is taken off: the latest begun and not
        /// yet completed, which is the top of the synchronizer's own stack and the list
        /// it reads the click's <c>rewardIndex</c> from. Null where none is open.</summary>
        internal static RewardsSet? OnOffer
        {
            get
            {
                lock (_begun)
                {
                    _begun.RemoveAll(entry => entry.Completion.IsCompleted);
                    return _begun.Count == 0 ? null : _begun[^1].Set;
                }
            }
        }

        [HarmonyPostfix]
        internal static void After(RewardsSet set, Task __result)
        {
            if (!MegaCrit.Sts2.Core.Context.LocalContext.IsMe(set.Player)) return;
            _set = __result;
            _during = Active?.LatestOpenTicket ?? 0;
            lock (_begun)
            {
                _begun.RemoveAll(entry => entry.Completion.IsCompleted);
                _begun.Add((set, __result));
            }
        }

        internal static void Forget()
        {
            _set = null;
            _during = 0;
            lock (_begun) _begun.Clear();
        }
    }

    /// <summary>
    /// The Crystal Sphere's own screen put up, watched where the engine plays the
    /// minigame.
    ///
    /// The minigame's task completes when the sphere is done with, and while it is
    /// open every cell the player reveals is a decision of its own; the option that
    /// opened it awaits the whole game and has settled once the sphere is up, for the
    /// reason <see cref="RewardsOffered"/> gives for a rewards set.
    /// </summary>
    [HarmonyPatch(typeof(CrystalSphereMinigame), nameof(CrystalSphereMinigame.PlayMinigame))]
    internal static class CrystalSphereOpened
    {
        private static Task? _minigame;
        private static long _during;

        /// <summary>Whether the sphere is in front of the player right now, put up
        /// inside the work of the decision holding <paramref name="ticket"/>.</summary>
        internal static bool OpenSince(long ticket) => _minigame is { IsCompleted: false } && _during == ticket;

        [HarmonyPostfix]
        internal static void After(Task __result)
        {
            _minigame = __result;
            _during = Active?.LatestOpenTicket ?? 0;
        }

        internal static void Forget()
        {
            _minigame = null;
            _during = 0;
        }
    }

    /// <summary>
    /// One move on the map, with the sibling node the player could have walked to
    /// instead.
    ///
    /// Read in a prefix, because both halves are only true before the move: the act the
    /// run is in, and which nodes the node being left leads to. Afterwards the run is
    /// standing somewhere else and the honest answer would be about a different
    /// decision.
    /// </summary>
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterMapCoord))]
    internal static class MapMove
    {
        private static Decision? _decision;

        /// <summary>Whether a decision has been read in the prefix and not yet
        /// announced by the postfix: the engine is inside the member, and any choice
        /// synced meanwhile is this decision's.</summary>
        internal static bool Reading => _decision is not null;

        /// <summary>The reading the move began from, while the engine is inside the
        /// member; what <see cref="RewardsSkipped"/> reads a set the move declines
        /// from.</summary>
        internal static TakenReading? BeforeTheMove => _decision?.Before;

        [HarmonyPrefix]
        internal static void Before(MapCoord coord)
        {
            _decision = null;
            if (Active is null) return;

            try
            {
                // The act is read from the run rather than from the coordinate, which
                // carries only a row and a column; a move never crosses acts, so the
                // act the run is in is the act the move is in.
                var act = LiveRun.State?.CurrentActIndex ?? 0;
                var args = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["act"] = Number(act),
                    ["row"] = Number(coord.row),
                    ["column"] = Number(coord.col),
                };

                if (Corruption.NominateColumn(coord.col, ReachableColumns()) is { } alternative)
                {
                    args[Corruption.AlternativeColumn] = Number(alternative);
                }

                if (ReadBefore(nameof(ActionVerb.MapMove)) is { } before)
                {
                    _decision = new Decision(nameof(ActionVerb.MapMove), args, before);
                }
            }
            catch (Exception ex)
            {
                Active?.Refuse($"A map move could not be read: {ex.GetType().Name}: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        internal static void After(Task __result)
        {
            if (_decision is not { } decision) return;
            _decision = null;
            AnnounceByName(decision.Verb, decision.Args, __result, decision.Before);
        }

        /// <summary>
        /// Which columns the node the run is standing on leads to.
        ///
        /// Reachability is decided exactly the way <c>RunDriver.MoveToMapNode</c>
        /// decides it - through <see cref="MapTravelRule"/>, the game's own travel rule,
        /// which is the whole next row under a free-travel hook and the current point's
        /// children otherwise, less any node whose type is
        /// <see cref="MapPointType.Unassigned"/> - so a nominated node is one a replay
        /// can actually enter. Empty where the run has no current node yet, which is a
        /// move with nothing to nominate rather than a failure.
        /// </summary>
        private static IEnumerable<int> ReachableColumns()
        {
            if (LiveRun.State is not { Map: { } map } run) return [];
            if (run.CurrentMapCoord is not { } current) return [];
            if (map.GetPoint(current.col, current.row) is not { } point) return [];

            return MapTravelRule.TravelableFrom(run, map, point)
                .Where(child => child is { PointType: not MapPointType.Unassigned })
                .Select(child => child.coord.col)
                .ToList();
        }
    }

    /// <summary>
    /// One reward taken off a loot screen.
    ///
    /// A card reward is a different verb because it opens a second screen and the
    /// format records which card came back; the arguments for it are filled in from
    /// that screen's answer when the decision is committed.
    /// </summary>
    [HarmonyPatch(typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SelectLocalReward))]
    internal static class RewardTaken
    {
        private static Decision? _decision;

        /// <summary>Whether a decision has been read in the prefix and not yet
        /// announced by the postfix: the engine is inside the member, and any choice
        /// synced meanwhile is this decision's.</summary>
        internal static bool Reading => _decision is not null;

        [HarmonyPrefix]
        internal static void Before(Reward reward)
        {
            _decision = null;
            if (Active is null) return;

            try
            {
                // The kind by the one reader the driver uses too, and the id beside it
                // for the kinds that name a thing a build could have changed.
                var kind = LootRewards.KindOf(reward);

                // The position is the reward's place in the set the engine is about to
                // read the click's own rewardIndex off, never a position on the screen,
                // and it is what tells two rewards of one kind apart. A reward that is
                // not on the set the recorder saw offered has no position it can write.
                var position = RewardsOffered.OnOffer?.Rewards.IndexOf(reward) ?? -1;
                if (position < 0)
                {
                    StopAtDecision(MetAtMember(
                        typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SelectLocalReward), kind,
                        "The reward is not on the rewards set the recorder saw offered, so the recording cannot " +
                        "say which reward of the set was taken.",
                        ("reward", reward.GetType().Name)));
                    return;
                }

                IReadOnlyDictionary<string, string>? args = null;
                var verb = nameof(ActionVerb.ClaimReward);
                if (reward is CardReward)
                {
                    verb = nameof(ActionVerb.TakeCard);
                    args = Args((RewardKinds.IndexArgument, Number(position)));
                }
                else if (RewardKinds.All.Contains(kind, StringComparer.Ordinal))
                {
                    var id = LootRewards.IdOf(reward);
                    var idArgument = RewardKinds.IdArgument(kind);
                    if (idArgument is not null && id is null)
                    {
                        StopAtDecision(MetAtMember(
                            typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SelectLocalReward), kind,
                            "This build did not give the recorder the reward's id, so the recording cannot say " +
                            "what was claimed.",
                            ("reward", reward.GetType().Name)));
                        return;
                    }

                    args = idArgument is null
                        ? Args(("reward_type", kind), (RewardKinds.IndexArgument, Number(position)))
                        : Args(("reward_type", kind), (idArgument, id!), (RewardKinds.IndexArgument, Number(position)));
                }

                if (args is null)
                {
                    StopAtDecision(MetAtMember(
                        typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SelectLocalReward), kind,
                        "This format has no verb for that kind of reward, so the recording cannot say what was " +
                        "claimed.",
                        ("reward", reward.GetType().Name)));
                    return;
                }

                if (ReadBefore(verb) is { } before) _decision = new Decision(verb, args, before);
            }
            catch (Exception ex)
            {
                Active?.Refuse($"A reward could not be read: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>The task the engine handed back finishes when the reward is
        /// finished - including the card screen a card reward opens - which is the
        /// moment there is a state worth reading; or, for a relic whose own work
        /// offers a set of its own, once that set is on offer
        /// (<see cref="HandedToThePlayerDuring"/>).</summary>
        [HarmonyPostfix]
        internal static void After(Task<bool> __result)
        {
            if (_decision is not { } decision) return;
            _decision = null;
            AnnounceByName(decision.Verb, decision.Args, __result, decision.Before, settlesOnceHandedToThePlayer: true);
        }
    }

    /// <summary>
    /// A loot screen dismissed with something still on it.
    ///
    /// Watched at the private <c>SkipRewardsSet</c>, which every declined set funnels
    /// through, rather than at the public <c>SkipLocalRewardsSet</c> the headless
    /// driver calls. In the retail client those are not the same thing, and the
    /// difference cost a whole evidence run: a post-combat loot screen is
    /// <em>terminal</em>, so pressing Skip takes <c>NRewardsScreen</c>'s
    /// <c>ProceedFromTerminalRewardsScreen</c> branch and never calls
    /// <c>SkipLocalRewardsSet</c> at all. What actually declines the leftovers is
    /// <c>BeforeLeavingRoom</c> on the way out of the room, and it calls this. The
    /// funnel catches both, the driver's own call included, because
    /// <c>SkipLocalRewardsSet</c> reaches it too.
    ///
    /// A recording missing this does not lose a detail, it stops reproducing: the
    /// arbiter refuses the following map move rather than walk away from an open set,
    /// which is exactly how the gap was found.
    ///
    /// It takes no arguments and filters on no player. The set it is handed is typed by
    /// a class private to the synchronizer, which this assembly cannot name, and a
    /// recorder that only ever describes the run in front of it has no second player to
    /// confuse it with. It fires once per set actually declined, which is what the
    /// format wants: one decision per screen walked away from.
    /// </summary>
    [HarmonyPatch(typeof(RewardsSetSynchronizer), SkipRewardsSetMember)]
    internal static class RewardsSkipped
    {
        /// <summary>
        /// Announced after the set is declined, and read there rather than after a
        /// settle.
        ///
        /// Both halves matter. A prefix would read the state before the rewards were
        /// declined, which is the state the decision started from. And the ambient
        /// settle would read it long after: <c>BeforeLeavingRoom</c> runs as part of
        /// the map move, so the engine is not quiet until the room has been left and
        /// the next fight has opened - and the recorder now waits for that fight to be
        /// ready for the player before it reads anything. A skip settled that way
        /// carries the state of the room after the one it happened in, byte-identical
        /// to the move's own, and <c>RunCoverage</c> then attributes the fight's start
        /// to the skip rather than to the room entry. That is the batching the pump
        /// exists to prevent, arriving by a route the pump could not see, and it is
        /// what made every combat-start boundary after a skip point one decision too
        /// early.
        ///
        /// Nothing here waits, because there is nothing to wait for: the set is
        /// declined synchronously and the state it left is final when this returns.
        ///
        /// A set the map move declines is read from the reading the move began from,
        /// both before and after, rather than here. <c>EnterMapPointInternal</c> has
        /// already advanced the act floor and the coordinate to the node being walked
        /// to when it leaves the room, so a reading taken at the funnel is of a run
        /// partway through the move - the next node's floor and coordinate under the
        /// room being left - and no replay holds that state: the driver declines the
        /// set as its own decision, before the move, from the state the player walked
        /// away from the loot screen in. That state is the one the move's prefix read,
        /// and declining a set changes nothing the projection reads (the run looks the
        /// same either way, which is why the format records the skip at all), so it is
        /// the state the skip left too. Every store journal of this build carried the
        /// move's partial state on its skips, and the coverage read the wrong floor
        /// off them.
        /// </summary>
        private static TakenReading? _before;
        private static bool _readFromTheMove;

        /// <summary>The state the set was declined from: the move's own reading where
        /// the move is what declines it, read here otherwise.</summary>
        [HarmonyPrefix]
        internal static void Before()
        {
            _before = null;
            _readFromTheMove = false;
            if (Active is not { } recorder) return;

            if (MapMove.BeforeTheMove is { } move)
            {
                // Its own ticket, so a save asked while the move runs on is still the
                // move's to place and not this decision's.
                _before = move with { Ticket = recorder.OpenTicket() };
                _readFromTheMove = true;
                return;
            }

            // Read from inside the funnel, which is inside whatever reached it, and
            // announced with its own after-reading: it closes nothing ahead of it
            _before = ReadBefore(nameof(ActionVerb.SkipRewards), closesTheDecisionsBefore: false);
        }

        [HarmonyPostfix]
        internal static void After()
        {
            if (_before is not { } before) return;
            _before = null;
            if (_readFromTheMove)
            {
                _readFromTheMove = false;
                AnnounceAsAlreadyFinished(
                    ActionVerb.SkipRewards, Args(), before,
                    new TakenReading(before.Sample, before.Digest, before.RunClockMs));
                return;
            }

            AnnounceAsAlreadyFinished(ActionVerb.SkipRewards, Args(), before);
        }
    }

    [HarmonyPatch(typeof(RestSiteSynchronizer), nameof(RestSiteSynchronizer.ChooseLocalOption))]
    internal static class RestSiteOptionTaken
    {
        private static Decision? _decision;

        /// <summary>Whether a decision has been read in the prefix and not yet
        /// announced by the postfix: the engine is inside the member, and any choice
        /// synced meanwhile is this decision's.</summary>
        internal static bool Reading => _decision is not null;

        [HarmonyPrefix]
        internal static void Before(RestSiteSynchronizer __instance, int index)
        {
            _decision = null;
            if (Active is null) return;

            try
            {
                var options = __instance.GetLocalOptions();
                if (index < 0 || index >= options.Count)
                {
                    StopAtDecision(MetAtMember(
                        typeof(RestSiteSynchronizer), nameof(RestSiteSynchronizer.ChooseLocalOption), null,
                        "The option is not one the rest site offers, so the recorder cannot say which one was " +
                        "chosen.",
                        ("option_index", Number(index)), ("offered", Number(options.Count))));
                    return;
                }

                if (ReadBefore(nameof(ActionVerb.ChooseRestSiteOption)) is { } before)
                {
                    _decision = new Decision(
                        nameof(ActionVerb.ChooseRestSiteOption),
                        Args(("option_id", options[index].OptionId), ("option_index", Number(index))),
                        before);
                }
            }
            catch (Exception ex)
            {
                Active?.Refuse($"A rest site option could not be read: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>Settled once the engine has handed the run to the player inside
        /// the option's work, for the heal whose rewards a relic adds; see
        /// <see cref="HandedToThePlayerDuring"/>.</summary>
        [HarmonyPostfix]
        internal static void After(Task<bool> __result)
        {
            if (_decision is not { } decision) return;
            _decision = null;
            AnnounceByName(decision.Verb, decision.Args, __result, decision.Before, settlesOnceHandedToThePlayer: true);
        }
    }

    [HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.PickRelicLocally))]
    internal static class ChestRelicTaken
    {
        [HarmonyPrefix]
        internal static void Before(TreasureRoomRelicSynchronizer __instance, int? index)
        {
            if (Active is null) return;

            try
            {
                // The skip's own path on this build: SkipRelicLocally is PickRelicLocally
                // with no index, and ChestRelicSkipped has announced it already
                if (index is null) return;

                var position = index.Value;
                var relics = __instance.CurrentRelics;
                if (relics is null || position < 0 || position >= relics.Count)
                {
                    StopAtDecision(MetAtMember(
                        typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.PickRelicLocally),
                        null,
                        "The position is not one this chest offers, so the recorder cannot say which relic was " +
                        "taken.",
                        ("option_index", Number(position)),
                        ("offered", relics is null ? "none" : Number(relics.Count))));
                    return;
                }

                Announce(
                    ActionVerb.TakeChestRelic,
                    Args(("relic_id", relics[position].Id.ToString()), ("option_index", Number(position))));
            }
            catch (Exception ex)
            {
                Active?.Refuse($"A chest relic could not be read: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.SkipRelicLocally))]
    internal static class ChestRelicSkipped
    {
        [HarmonyPrefix]
        internal static void Before()
        {
            if (Active is null) return;
            Announce(ActionVerb.SkipChestRelic, Args());
        }
    }

    [HarmonyPatch(typeof(ActChangeSynchronizer), nameof(ActChangeSynchronizer.SetLocalPlayerReady))]
    internal static class ActAdvanced
    {
        [HarmonyPrefix]
        internal static void Before()
        {
            if (Active is null) return;
            Announce(ActionVerb.ProceedToNextAct, Args());
        }
    }

    /// <summary>
    /// One purchase from the merchant, named by the shelf it came off.
    ///
    /// Read before the purchase rather than after, because afterwards the shelf entry
    /// is sold and the only honest answer would be the position of nothing.
    ///
    /// A purchase the engine makes for itself is not a decision and is not recorded:
    /// Lord's Parasol buys the whole shop as the merchant is entered, through the same
    /// member with <c>ignoreCost</c> set, which no button press sets, from inside the
    /// map move's own work. Recorded, those purchases stood in the journal before the
    /// move that opened the shop - the move is written once the engine has settled at
    /// the other end - and a replay refused the first of them in the room the move
    /// left; unrecorded, the replay's own move reproduces them, since the engine and
    /// not the player makes them. That is proven headlessly only: in the retail client
    /// the relic's purchases run on scene-tree timers, unawaited from
    /// <c>AfterRoomEntered</c>, so the move can settle and be written before they and
    /// the relic's forced removal prompt have happened, and the move's after-reading
    /// and the prompt's place in the journal are a known limitation owed to the
    /// retail-timing follow-up.
    /// </summary>
    [HarmonyPatch(typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper))]
    internal static class ShopPurchased
    {
        private static Decision? _decision;

        /// <summary>Whether a decision has been read in the prefix and not yet
        /// announced by the postfix: the engine is inside the member, and any choice
        /// synced meanwhile is this decision's.</summary>
        internal static bool Reading => _decision is not null;

        /// <summary>Settled once the engine has handed the run to the player inside
        /// the purchase's work, for the relic whose <c>AfterObtained</c> offers a set;
        /// see <see cref="HandedToThePlayerDuring"/>.</summary>
        [HarmonyPostfix]
        internal static void After(Task<bool> __result)
        {
            if (_decision is not { } decision) return;
            _decision = null;
            AnnounceByName(decision.Verb, decision.Args, __result, decision.Before, settlesOnceHandedToThePlayer: true);
        }

        [HarmonyPrefix]
        internal static void Before(MerchantEntry __instance, MerchantInventory? inventory, bool ignoreCost)
        {
            _decision = null;
            if (Active is null) return;
            if (ignoreCost) return;

            try
            {
                if (inventory is null)
                {
                    StopAtDecision(MetAtMember(
                        typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper), __instance.GetType().Name,
                        "The merchant has no inventory, so the recorder cannot say which shelf it came off."));
                    return;
                }

                if (ReadBefore(nameof(ActionVerb.ShopPurchase)) is not { } before) return;

                if (__instance is MerchantCardRemovalEntry)
                {
                    _decision = new Decision(
                        nameof(ActionVerb.ShopPurchase), Args(("kind", ShopPurchaseKinds.CardRemoval)), before);
                    return;
                }

                foreach (var (kind, shelf) in Shelves(inventory))
                {
                    var index = shelf.IndexOf(__instance);
                    if (index < 0) continue;

                    var id = IdOf(__instance);
                    if (id is null)
                    {
                        StopAtDecision(MetAtMember(
                            typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper),
                            __instance.GetType().Name,
                            "This build did not give the recorder the entry's id, so the recording cannot say " +
                            "what was bought.",
                            ("kind", kind), ("option_index", Number(index))));
                        return;
                    }

                    _decision = new Decision(
                        nameof(ActionVerb.ShopPurchase),
                        Args(
                            ("kind", kind),
                            ("option_index", Number(index)),
                            (ShopPurchaseKinds.IdArgument(kind)!, id)),
                        before);
                    return;
                }

                StopAtDecision(MetAtMember(
                    typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper), __instance.GetType().Name,
                    "The entry is not on any shelf this recorder knows, so the recording cannot say what it was."));
            }
            catch (Exception ex)
            {
                Active?.Refuse($"A shop purchase could not be read: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static IEnumerable<(string Kind, List<MerchantEntry> Shelf)> Shelves(
            MerchantInventory inventory)
        {
            yield return (ShopPurchaseKinds.CharacterCard, [.. inventory.CharacterCardEntries]);
            yield return (ShopPurchaseKinds.ColorlessCard, [.. inventory.ColorlessCardEntries]);
            yield return (ShopPurchaseKinds.Relic, [.. inventory.RelicEntries]);
            yield return (ShopPurchaseKinds.Potion, [.. inventory.PotionEntries]);
        }

        private static string? IdOf(MerchantEntry entry) => entry switch
        {
            MerchantCardEntry card => card.CreationResult?.Card.Id.ToString(),
            MerchantRelicEntry relic => relic.Model?.Id.ToString(),
            MerchantPotionEntry potion => potion.Model?.Id.ToString(),
            _ => null,
        };
    }

    /// <summary>
    /// The merchant's card-removal service, which needs its own patch to be seen at all.
    ///
    /// <see cref="ShopPurchased"/> watches <c>MerchantEntry.OnTryPurchaseWrapper</c>,
    /// and that is not the method a removal goes through.
    /// <c>MerchantCardRemovalEntry</c> declares its own three-argument
    /// <c>OnTryPurchaseWrapper</c> which <em>shadows</em> the base's two-argument one
    /// rather than overriding it, because the base is not virtual; C# binds that
    /// statically, so a patch on the base never fires for a removal and the client calls
    /// the derived method directly. The player paid gold and lost a card and the
    /// recording said nothing about either, which is how a real run was found
    /// unreproducible.
    ///
    /// It reuses <see cref="ShopPurchased"/>'s own reading rather than repeating it, so
    /// the two cannot come to answer differently about what a purchase is. Exactly one
    /// of them runs for any given call, because which method executes is decided by the
    /// caller's static type.
    /// </summary>
    [HarmonyPatch(
        typeof(MerchantCardRemovalEntry),
        nameof(MerchantCardRemovalEntry.OnTryPurchaseWrapper),
        [typeof(MerchantInventory), typeof(bool), typeof(bool)])]
    internal static class ShopCardRemovalPurchased
    {
        [HarmonyPrefix]
        internal static void Before(MerchantCardRemovalEntry __instance, MerchantInventory? inventory, bool ignoreCost) =>
            ShopPurchased.Before(__instance, inventory, ignoreCost);

        [HarmonyPostfix]
        internal static void After(Task<bool> __result) => ShopPurchased.After(__result);
    }

    /// <summary>
    /// Reads what a card prompt the shell watched was answered with.
    ///
    /// The prompt itself is <see cref="CardPrompts"/>'s - a prompt being up, and the
    /// list it offers, are facts about the game that both features read - and which
    /// card came off which offered list is this one's, so the recorder subscribes
    /// rather than patching the entry points a second time.
    ///
    /// Which is also why each handler carries its own try/catch: what a failure to read
    /// an answer means is this feature's to say, and it says it by marking the recording
    /// broken and writing the sentence into the journal. Left to the shell's guard it
    /// would be a log line, and the recording would carry on missing a decision while
    /// still reporting a continuous watch - a claim nobody established.
    /// </summary>
    internal static void ReadTheAnswers()
    {
        CardPrompts.Answered = (prompt, chosen) =>
        {
            try
            {
                CardPromptAnswered(prompt, chosen);
            }
            catch (Exception ex)
            {
                Refuse($"A card prompt's answer could not be read: {ex.GetType().Name}: {ex.Message}");
            }
        };

        CardScreensUp.RewardAnswered = option =>
        {
            try
            {
                CardRewardAnswered(CardRewardScreen.Offered, CardRewardScreen.Alternatives, option);
            }
            catch (Exception ex)
            {
                Refuse($"A card reward's answer could not be read: {ex.GetType().Name}: {ex.Message}");
            }
        };

        static void Refuse(string reason) => Active?.Refuse(reason);
    }

    /// <summary>
    /// The screen a card reward puts up, which answers with a position rather than a
    /// card.
    ///
    /// Its own patch because it is not one of the grid screens: it offers the reward's
    /// cards and its alternatives on one list, and the position it reports is into
    /// that combined list.
    /// </summary>
    [HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.ShowScreen))]
    internal static class CardRewardScreen
    {
        /// <summary>What the card reward now on screen offered, in the order it
        /// offered them - which is the order the position reported back indexes
        /// into.</summary>
        internal static IReadOnlyList<CardModel> Offered { get; private set; } = [];

        /// <summary>The alternatives the same screen offered past the cards, in the
        /// order the position reported back continues into.</summary>
        internal static IReadOnlyList<CardRewardAlternative> Alternatives { get; private set; } = [];

        [HarmonyPostfix]
        internal static void After(
            IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> extraOptions)
        {
            if (Active is null) return;
            Offered = [.. options.Select(option => option.Card)];
            Alternatives = [.. extraOptions];
        }
    }

    /// <summary>
    /// The bundle screen Scroll Boxes opens, watched where the engine asks.
    ///
    /// The prompt has no seam of its own - <c>ICardSelector</c> has no bundle member -
    /// so what is watched is the prompt's entry point, for what it offered, and the
    /// choice the client then syncs, for what came back. What is held open is the call
    /// that opened it and nothing wider: the two branches the engine answers itself -
    /// a fight that is ending and an empty prompt - ask nobody anything and so open
    /// nothing, and a call that has settled holds nothing open either. Without that,
    /// an unrelated prompt's answer is read as this one's and the recording states a
    /// decision nobody made.
    /// </summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseABundleScreen))]
    internal static class BundleScreen
    {
        internal static IReadOnlyList<IReadOnlyList<CardModel>>? Open { get; set; }

        [HarmonyPrefix]
        internal static void Before(IReadOnlyList<IReadOnlyList<CardModel>> bundles) =>
            Opened(bundles, CombatManager.Instance is { IsEnding: true });

        /// <summary>What this call opened, from what it was asked and the reading the
        /// engine's own early returns are taken from.</summary>
        internal static void Opened(IReadOnlyList<IReadOnlyList<CardModel>> bundles, bool combatIsEnding) =>
            Open = combatIsEnding || bundles.Count == 0 ? null : bundles;

        [HarmonyPostfix]
        internal static void After(Task<IEnumerable<CardModel>>? __result) =>
            CloseWhenSettled(__result, Open, () => Open, prompt => Open = prompt);
    }

    /// <summary>The relic screen, watched and scoped the same way. Nothing on v0.111.0
    /// opens one.</summary>
    [HarmonyPatch(typeof(RelicSelectCmd), nameof(RelicSelectCmd.FromChooseARelicScreen))]
    internal static class RelicScreen
    {
        internal static IReadOnlyList<RelicModel>? Open { get; set; }

        [HarmonyPrefix]
        internal static void Before(IReadOnlyList<RelicModel> relics) => Opened(relics);

        internal static void Opened(IReadOnlyList<RelicModel> relics) =>
            Open = relics.Count == 0 ? null : relics;

        [HarmonyPostfix]
        internal static void After(Task<RelicModel?>? __result) =>
            CloseWhenSettled(__result, Open, () => Open, prompt => Open = prompt);
    }

    /// <summary>
    /// Drops the prompt a call opened once that call has settled, and only while it is
    /// still the one open.
    ///
    /// The answer is read while the call is still waiting for it, so a call that has
    /// settled is one whose prompt was either already read or never put to a player at
    /// all. Either way, nothing that happens afterwards is its answer.
    /// </summary>
    private static void CloseWhenSettled<TPrompt, TResult>(
        Task<TResult>? call, TPrompt? opened, Func<TPrompt?> read, Action<TPrompt?> write)
        where TPrompt : class
    {
        if (opened is null) return;

        if (call is null)
        {
            write(null);
            return;
        }

        call.ContinueWith(
            _ =>
            {
                if (ReferenceEquals(read(), opened)) write(null);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>
    /// Every game action a player issues, at the one member all of them enter by.
    ///
    /// <c>RequestEnqueue</c> is the local origin of every game action in a
    /// singleplayer game - the eleven types the game's net actions become all pass
    /// through it - so it is where a type nothing here claims is met once and by
    /// name, before the executor runs it. <see cref="NetActionClaims"/> is the
    /// account: a claimed type is recorded by its own patch or by the fight observer
    /// and takes the path it takes today; the engine's own bookkeeping is excused;
    /// the console's marks the run non-standard, the way the console's own patch
    /// does; and a stranger stops the recording here, naming its type. An action the
    /// synchronizer defers past the enemy turn re-enters this member when the player's
    /// turn begins, so each action instance is classified once. A stranger the fight
    /// observer <see cref="PlayerFightObserver.Watches"/> is left to the observer,
    /// which meets it at the executor with the fight's own step open and closes that
    /// step first; stopped here, at the request, the stop would stand at the open
    /// step's ordinal and drop it.
    /// </summary>
    [HarmonyPatch(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.RequestEnqueue))]
    internal static class ActionRequested
    {
        private static readonly ConditionalWeakTable<GameAction, object> Classified = [];
        private static readonly object Once = new();

        [HarmonyPrefix]
        internal static void Before(GameAction action)
        {
            if (Active is not { } recorder) return;
            if (!Classified.TryAdd(action, Once)) return;

            try
            {
                switch (NetActionClaims.For(action.GetType())?.Disposition)
                {
                    case NetActionClaims.Disposition.Claimed:
                        recorder.WatchTheFightBefore(action);
                        return;
                    case NetActionClaims.Disposition.EngineDriven:
                        return;
                    case NetActionClaims.Disposition.NonStandard:
                        ConsoleCommandUsed();
                        return;
                    default:
                        if (recorder._observer is { } observer && observer.Watches(action)) return;

                        StopAtDecision(MetAtNetAction(action));
                        return;
                }
            }
            catch (Exception ex)
            {
                recorder.Refuse($"A game action could not be classified: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// The client's own answer to a prompt, read as it is synced.
    ///
    /// Every locally answered prompt passes through here; the two whose prompt this
    /// recorder is holding open are read, and a prompt is closed by the sync that
    /// answers it whether or not there is a recording to read it into. Every other
    /// answer is one of a decision already being recorded - a card prompt this
    /// recorder holds open or a card reward's screen up, a prompt the game's own
    /// selector answers in both hosts, or a choice made inside a decision the recorder
    /// has announced and not yet settled, the way a rest site's Mend asks which player
    /// to heal - and an answer that is none of those is a decision nothing here names,
    /// which stops the recording rather than going by unread.
    /// </summary>
    [HarmonyPatch(typeof(PlayerChoiceSynchronizer), nameof(PlayerChoiceSynchronizer.SyncLocalChoice))]
    internal static class ChoiceSynced
    {
        [HarmonyPrefix]
        internal static void Before(PlayerChoiceResult result)
        {
            var bundles = BundleScreen.Open;
            var relics = RelicScreen.Open;
            BundleScreen.Open = null;
            RelicScreen.Open = null;

            if (Active is not { } recorder) return;

            try
            {
                if (bundles is not null)
                {
                    BundleScreenAnswered(bundles, result.AsIndex());
                    return;
                }

                if (relics is not null)
                {
                    RelicScreenAnswered(relics, result.AsIndex());
                    return;
                }

                if (IsAnAnswerToADecisionBeingRecorded(recorder)) return;

                StopAtDecision(new UnmappedFacts(
                    UnmappedDecision.PlayerChoiceSeam,
                    $"{nameof(PlayerChoiceSynchronizer)}.{nameof(PlayerChoiceSynchronizer.SyncLocalChoice)}",
                    result.ChoiceType.ToString(),
                    Args(("index", result.AsIndexOrNull() is { } index ? Number(index) : "")),
                    "This choice was synced with no prompt or screen this recorder holds open, no selector of " +
                    "the game's own answering, and no decision of the run announced and unsettled, so nothing " +
                    "here names the decision it answers."));
            }
            catch (Exception ex)
            {
                Active?.Refuse($"A screen's answer could not be read: {ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>Whether a synced choice belongs to a decision something here is
        /// already recording, read the way each of those records it: a prompt or
        /// screen held open, the game's own selector answering, a decision announced
        /// to the pump and not yet settled, or one read in a member's prefix and not
        /// yet announced by its postfix - the card reward's pick, and Mend's choice of
        /// whom to heal, both happen inside that window.</summary>
        private static bool IsAnAnswerToADecisionBeingRecorded(RunRecorder recorder)
        {
            if (CardPrompts.Open is not null || CardScreensUp.Count > 0) return true;
            if (CardSelectCmd.Selector is { } selector && CardPrompts.IsTheGamesOwn(selector)) return true;
            if (MapMove.Reading || RewardTaken.Reading || RestSiteOptionTaken.Reading || ShopPurchased.Reading ||
                CrystalSphereCellRevealed.Reading)
            {
                return true;
            }

            lock (Gate) return recorder._pending.Count > 0;
        }
    }

    /// <summary>
    /// One cell of the Crystal Sphere revealed, with the tool it was revealed with.
    ///
    /// The tool is the minigame's own at the moment of the click, which is what
    /// decides how many cells the click reveals; a reveal recorded without it would
    /// replay as a different reveal. Read in the prefix, while the cell is still
    /// hidden, and announced with the task the minigame hands back.
    /// </summary>
    [HarmonyPatch(typeof(CrystalSphereMinigame), nameof(CrystalSphereMinigame.CellClicked))]
    internal static class CrystalSphereCellRevealed
    {
        private static Decision? _decision;

        /// <summary>Whether a decision has been read in the prefix and not yet
        /// announced by the postfix: the engine is inside the member, and any choice
        /// synced meanwhile is this decision's.</summary>
        internal static bool Reading => _decision is not null;

        [HarmonyPrefix]
        internal static void Before(CrystalSphereMinigame __instance, CrystalSphereCell clickedCell)
        {
            _decision = null;
            if (Active is null) return;

            try
            {
                var tool = __instance.CrystalSphereTool switch
                {
                    CrystalSphereMinigame.CrystalSphereToolType.Small => CrystalSphereTools.Small,
                    CrystalSphereMinigame.CrystalSphereToolType.Big => CrystalSphereTools.Big,
                    _ => null,
                };

                if (tool is null)
                {
                    StopAtDecision(MetAtMember(
                        typeof(CrystalSphereMinigame), nameof(CrystalSphereMinigame.CellClicked),
                        __instance.CrystalSphereTool.ToString(),
                        "The tool is not one this format names, so the recorder cannot say what the click " +
                        "revealed.",
                        ("x", Number(clickedCell.X)), ("y", Number(clickedCell.Y))));
                    return;
                }

                if (ReadBefore(nameof(ActionVerb.RevealCrystalSphereCell)) is { } before)
                {
                    _decision = new Decision(
                        nameof(ActionVerb.RevealCrystalSphereCell),
                        Args(("tool", tool), ("x", Number(clickedCell.X)), ("y", Number(clickedCell.Y))),
                        before);
                }
            }
            catch (Exception ex)
            {
                Active?.Refuse($"A Crystal Sphere reveal could not be read: {ex.GetType().Name}: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        internal static void After(Task __result)
        {
            if (_decision is not { } decision) return;
            _decision = null;
            AnnounceByName(decision.Verb, decision.Args, __result, decision.Before);
        }
    }


    /// <summary>
    /// A potion drunk outside a fight.
    ///
    /// Inside one the same drink arrives at <see cref="PlayerFightObserver"/> through
    /// the action executor, and this and that would record it twice. Told apart by
    /// whether the combat manager says a fight is in progress rather than by whether an
    /// observer happens to be attached: the engine itself decides the same way - the
    /// discard action is constructed with <c>CombatManager.Instance?.IsInProgress</c> -
    /// and an attachment is this mod's own bookkeeping, which lags the fight by a
    /// decision at each end.
    ///
    /// No target is read. Out of combat there is no enemy to name, and everything else
    /// a potion aims at is the engine's own default, which is exactly what the driver
    /// replays when the argument is absent.
    /// </summary>
    [HarmonyPatch(typeof(PotionModel), nameof(PotionModel.EnqueueManualUse))]
    internal static class PotionUsed
    {
        [HarmonyPrefix]
        internal static void Before(PotionModel __instance)
        {
            if (Active is null || InAFight()) return;

            try
            {
                if (SlotOf(__instance) is not { } slot)
                {
                    StopAtDecision(MetAtMember(
                        typeof(PotionModel), nameof(PotionModel.EnqueueManualUse), null,
                        "The potion is not on the belt this recorder can see, so the recording cannot say which " +
                        "slot it came off.",
                        ("potion_id", __instance.Id.ToString())));
                    return;
                }

                Announce(
                    ActionVerb.UsePotion,
                    Args(("potion_id", __instance.Id.ToString()), ("slot_index", Number(slot))));
            }
            catch (Exception ex)
            {
                Active?.Refuse($"A potion use could not be read: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// A potion thrown away outside a fight, which is how room is made for a reward.
    ///
    /// The constructor rather than a method, because that is the member
    /// <see cref="EngineCommands"/> maps and the only one the discard goes through; it
    /// runs before the action reaches the queue, while the slot still holds the potion.
    /// Guarded the same way <see cref="PotionUsed"/> is, and for the same reason.
    /// </summary>
    [HarmonyPatch(typeof(DiscardPotionGameAction), MethodType.Constructor,
        typeof(Player), typeof(uint), typeof(bool))]
    internal static class PotionDiscarded
    {
        [HarmonyPrefix]
        internal static void Before(uint potionSlotIndex)
        {
            if (Active is null || InAFight()) return;

            try
            {
                var slot = (int)potionSlotIndex;
                var slots = LiveRun.State is { Players.Count: > 0 } run ? run.Players[0].PotionSlots : null;
                if (slots is null || slot < 0 || slot >= slots.Count || slots[slot] is not { } potion)
                {
                    StopAtDecision(MetAtMember(
                        typeof(DiscardPotionGameAction), ConstructorInfo.ConstructorName, null,
                        "The slot holds nothing this recorder can see, so the recording cannot say which potion " +
                        "was given up.",
                        ("slot_index", Number(slot))));
                    return;
                }

                Announce(
                    ActionVerb.DiscardPotion,
                    Args(("potion_id", potion.Id.ToString()), ("slot_index", Number(slot))));
            }
            catch (Exception ex)
            {
                Active?.Refuse($"A potion discard could not be read: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    /// <summary>Whether a fight is being fought right now, which is where
    /// <see cref="PlayerFightObserver"/> rather than a patch here is watching the
    /// belt.</summary>
    private static bool InAFight() => CombatManager.Instance?.IsInProgress ?? false;

    /// <summary>Which belt slot a potion is in, or null when the run's own player is
    /// not holding it.</summary>
    private static int? SlotOf(PotionModel potion)
    {
        if (LiveRun.State is not { Players.Count: > 0 } run) return null;

        var slots = run.Players[0].PotionSlots;
        for (var slot = 0; slot < slots.Count; slot++)
        {
            if (ReferenceEquals(slots[slot], potion)) return slot;
        }

        return null;
    }
}

/// <summary>
/// What the deciding half of an attach answered about a run.
///
/// One value per way <see cref="RunRecorder.Attach"/> can end, so the answer is
/// something a caller can act on rather than only a line in the player's log. The refusals are kept apart rather
/// than collapsed into one "no" for the same reason <see cref="RunSessionKind"/>'s
/// are: they are different facts about the run, and the difference between "this is
/// not a singleplayer run" and "the mod could not take this game" is the difference
/// between two rules.
/// </summary>
internal enum RunAttachment
{
    /// <summary>The recorder is now recording this run.</summary>
    Attached,

    /// <summary>Another recording or a trainer run had this game.</summary>
    AlreadyRecordingOrATrainerRun,

    /// <summary>The run went away between the settle and the reading.</summary>
    TheRunWentAway,

    /// <summary>The reading said this is not a singleplayer run, which is the only
    /// kind this build records.</summary>
    NotASingleplayerRun,

    /// <summary>The mod could not take this running game, so the recording could not
    /// say which build it was played on.</summary>
    CouldNotTakeTheGame,
}

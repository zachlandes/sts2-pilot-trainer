using System.Globalization;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// Watches the player play their own run and writes it down as a native recording.
///
/// It is the Combat Trainer's observer widened to a whole run. The same principle
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

    private const double SettlePollSeconds = 0.05;

    /// <summary>How long to wait for a run to exist after the game said one was
    /// starting. Generous, because continuing a saved run loads a scene.</summary>
    private const double AttachBudgetSeconds = 60.0;

    /// <summary>Where a recording lives in the store, under the profile scope.</summary>
    internal const string RecordingsDirectory = "recordings";

    private static readonly Lock Gate = new();

    private readonly RunCapture _capture;
    private readonly string _journalPath;
    private readonly Queue<PendingDecision> _pending = new();
    private readonly List<CardScreenPick> _screenPicks = [];

    private PlayerFightObserver? _observer;

    /// <summary>The in-fight action the observer has opened and not yet closed, held
    /// by name rather than as an <see cref="ActionVerb"/>: a value of a sibling
    /// assembly's type in a field here decides this type's layout, and the game loads
    /// this assembly's types before this mod can say where that sibling is.</summary>
    private (string Verb, IReadOnlyDictionary<string, string> Args)? _openFightStep;

    private bool _pumping;
    private bool _disposed;
    private bool _finished;

    /// <summary>
    /// How many card screens are open in front of the player right now.
    ///
    /// Static because the screens are: there is one game and one person looking at it.
    /// It is what keeps a settle from finishing while somebody is still choosing - a
    /// card screen suspends inside the call that opened it and the engine's queue goes
    /// idle while it waits, so "the queue is empty" is true of a run that has not
    /// finished making its decision.
    ///
    /// Four things hold together here, and each of the first three has been broken once
    /// by a change that only had the others in mind:
    ///
    /// It counts screens up in this process, not screens a recorder happened to be
    /// watching. <see cref="WhileOnScreen{T}"/> is the only thing that touches it and it
    /// takes and gives back in one try/finally, so every increment has its decrement and
    /// there is no bare decrement for a caller to reach. That is what makes it balanced,
    /// and what makes going below zero impossible rather than clamped.
    ///
    /// Being balanced, it cannot go stale, so nothing resets it. Both of the game's card
    /// screens complete their own completion source in <c>_ExitTree</c> - the grid
    /// cancels, the reward screen faults - so a screen torn down with its run still ends
    /// the task this waits on, and the finally still runs. A reset would be the only way
    /// left to lose a screen that is genuinely up.
    ///
    /// Waiting on it costs no budget. A screen is up for as long as somebody is looking
    /// at it; the engine's settle budget bounds the engine, which should always settle,
    /// and a person is not the engine.
    ///
    /// The wait ends when the recorder does. Without that, a run left to the main menu
    /// spins a scene-tree timer every poll for the rest of the session, outliving the
    /// recording it was waiting for.
    /// </summary>
    private static int _screensOpen;

    private RunRecorder(RunCapture capture, string journalPath)
    {
        _capture = capture;
        _journalPath = journalPath;
    }

    /// <summary>The run being recorded right now, or null when none is.</summary>
    internal static RunRecorder? Active { get; private set; }

    /// <summary>What the capture holds, for a test and for a log line.</summary>
    internal RunCapture Capture => _capture;

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
            recorder.Finish(abandoned ? "abandoned" : isVictory ? "won" : "lost");
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
        var recorder = Active;
        if (recorder is null) return;

        Active = null;
        recorder.Dispose();
    }

    /// <summary>
    /// Waits for the run to be a run, then attaches to it.
    ///
    /// Two things have to have happened before the opening reading is worth taking.
    /// The run has to exist: continuing a saved run is asynchronous, so the method that
    /// starts it returns long before the game has one. And the run has to have entered
    /// its first room, because that is where the headless replay's own opening reading
    /// is taken - <c>RunDriver.EnterFirstRoom</c> before the first action - and a
    /// reading taken on the near side of it would describe a floor the recording then
    /// claims to arrive on.
    /// </summary>
    private static async Task AttachWhenReady()
    {
        try
        {
            var deadline = RecordedFightRun.LetTheGameRun(AttachBudgetSeconds);
            while (true)
            {
                if (Active is not null || ProfileWriteBarrier.IsActive) return;
                if (HasEnteredItsFirstRoom()) break;

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
                    () => Active is not null || ProfileWriteBarrier.IsActive
                        ? "another recording or a trainer run took this game first."
                        : RunWentAway()) is { } unsettled)
            {
                Log.Warn($"[{RunmobileMod.ModId}] not recording this run: {unsettled}", 2);
                return;
            }

            if (Active is not null || ProfileWriteBarrier.IsActive) return;
            if (LiveRun.State is not { } run) return;

            var startedUtc = LiveRun.RunStartedUtc();
            var runId = LiveRun.NameRecording(run.Rng.StringSeed, startedUtc);
            var journalPath = $"{RecordingsDirectory}/{runId}{RunJournal.FileExtension}";
            var (sample, digest) = LiveRun.Read();
            var clock = LiveRun.RunClockMs();

            RunCapture capture;
            if (RunmobileStore.Read(journalPath) is { } existing)
            {
                var journal = RunJournal.Parse(existing);
                capture = RunCapture.Resume(journal, digest);

                // A break this resume decided on is a fact only this session knows, and
                // the session after it would compare its own live digest against a
                // journal that says nothing about the hole. Appended before the
                // recorder is live, so a crash between here and the next decision still
                // leaves the refusal on the file.
                foreach (var reason in capture.Refusals.Skip(journal.Refusals.Count))
                {
                    Append(journalPath, RunJournal.RenderRefusal(reason));
                }

                Log.Info(
                    $"[{RunmobileMod.ModId}] continuing the recording of {runId} at decision " +
                    $"{capture.NextSeq.ToString(CultureInfo.InvariantCulture)}; continuity {capture.Continuity}", 2);
                if (capture.Refusal is { } refusal) Log.Warn($"[{RunmobileMod.ModId}] {refusal}", 2);
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

            var recorder = new RunRecorder(capture, journalPath);

            // A journal whose last decision left a fight live resumes with that fight
            // still live, and nothing else here would ever ask: the question is asked
            // after each decision, and a resumed session has not made one yet. Left
            // unasked, every card play and ended turn of the rest of that fight goes
            // unrecorded while the recording still reports a continuous watch.
            //
            // Before Active is published rather than after, so that a throw from here
            // lands in the catch below with nothing recording: a log line saying the run
            // is not being recorded while it is would tell the player the opposite of
            // what happened.
            recorder.StartOrStopWatchingTheFight();

            Active = recorder;
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
    /// Whether the run exists and has entered its first room yet.
    ///
    /// A projection of a run the game is still building throws rather than answering,
    /// and that is a "not yet" rather than a failure: this is polled from the moment
    /// the game says a run is starting, which is well before it has one. Anything else
    /// wrong here is still a not-yet on this poll and is the deadline's to give up on,
    /// so a run is never half-attached to because one reading came too early.
    /// </summary>
    private static bool HasEnteredItsFirstRoom()
    {
        try
        {
            return LiveRun.State is not null && Floor(LiveRun.Sample()) >= 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Which build of the recorder is writing, so a defect found in one is
    /// traceable to everything it wrote.</summary>
    internal static string RecorderVersion =>
        $"runmobile-recorder/{typeof(RunRecorder).Assembly.GetName().Version?.ToString() ?? "0.0.0"}";

    // ── Decisions ────────────────────────────────────────────────────────────────

    /// <summary>A decision the game has just been asked to make.</summary>
    internal static void Announce(
        ActionVerb verb, IReadOnlyDictionary<string, string> args, Task? engineWork = null) =>
        AnnounceByName(verb.ToString(), args, engineWork);

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
        string verb, IReadOnlyDictionary<string, string> args, Task? engineWork)
    {
        var recorder = Active;
        if (recorder is null || recorder._finished) return;

        lock (Gate)
        {
            recorder._pending.Enqueue(new PendingDecision(verb, args, engineWork));
            if (recorder._pumping) return;
            recorder._pumping = true;
        }

        _ = recorder.Pump();
    }

    /// <summary>
    /// A card screen answered, with what it offered and what came back.
    ///
    /// Held rather than recorded, because a card screen is answered from inside the
    /// call that opened it: the decision that opened it has not settled yet, and the
    /// format records the picks immediately after it. Which is also how the driver
    /// replays them.
    /// </summary>
    internal static void CardScreenAnswered(
        IReadOnlyList<CardModel> offered, IEnumerable<CardModel> chosen)
    {
        var recorder = Active;
        if (recorder is null || recorder._finished) return;

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
                recorder.Refuse(
                    $"A card screen returned {card.Id}, which is not one of the " +
                    $"{offered.Count.ToString(CultureInfo.InvariantCulture)} card(s) it offered. The recorder " +
                    "cannot say which option was picked, and a position it guessed would replay as a " +
                    "different decision.");
                return;
            }

            taken.Add((card.Id.ToString(), index));
        }

        // Every answer from one screen is resolved before any of them is nominated
        // against, because the alternative has to be a position none of them took. It
        // is per answer rather than one for the screen: what each nominates is another
        // copy of its own card.
        var offeredIds = offered.Select(card => card.Id.ToString()).ToList();
        var positions = taken.Select(pick => pick.Index).ToList();

        lock (Gate)
        {
            foreach (var (cardId, index) in taken)
            {
                recorder._screenPicks.Add(new CardScreenPick(
                    cardId, index, Corruption.NominateScreenOption(offeredIds, index, positions)));
            }
        }
    }

    /// <summary>
    /// Counts one card screen for as long as the game's own task for it is outstanding.
    ///
    /// The only thing that touches <see cref="_screensOpen"/>, and it takes and gives
    /// back in one try/finally. A prefix and a postfix that each decided separately drifted
    /// exactly once - a run torn down between them left the count above zero for the rest
    /// of the session - and a pair of take/give methods would let the next caller do it
    /// again. There is nothing here to call twice or to call alone.
    /// </summary>
    internal static async Task<T> WhileOnScreen<T>(Task<T> screen)
    {
        Interlocked.Increment(ref _screensOpen);
        try
        {
            return await screen;
        }
        finally
        {
            Interlocked.Decrement(ref _screensOpen);
        }
    }

    /// <summary>How many card screens are open, for a test that drives one.</summary>
    internal static int ScreensOpen => Volatile.Read(ref _screensOpen);

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
    /// The count, the stop, the poll and the budget arrive as arguments for the same
    /// reason <see cref="PlayerFightObserver.WaitUntilSettled"/>'s do: waiting is a rule
    /// about those things, and handed them it can be exercised on a machine with no game.
    /// </summary>
    /// <returns>Null once the engine has settled, or the sentence saying why the wait
    /// ended without it.</returns>
    internal static async Task<string?> WaitForTheEngine(
        Func<int> open,
        Func<string?> stopped,
        Func<bool> idle,
        Func<Task> newBudget,
        Func<Task> nextPoll,
        string unsettled)
    {
        Task? budget = null;
        var idleTicks = 0;

        while (true)
        {
            if (stopped() is { } why) return why;

            if (open() > 0)
            {
                // The budget is discarded rather than paused, so the next one starts
                // from the moment the last screen comes down. A screen that goes up a
                // second time is a second stretch of somebody thinking, and the engine
                // gets its whole budget back either way.
                budget = null;
                idleTicks = 0;
                await nextPoll();
                continue;
            }

            budget ??= newBudget();
            if (budget.IsCompleted) return unsettled;

            await nextPoll();

            // A screen that went up during the poll scores no idle tick; the top of the
            // loop then throws the budget away.
            idleTicks = open() == 0 && idle() ? idleTicks + 1 : 0;
            if (idleTicks >= 2) return null;
        }
    }

    /// <summary>A card reward screen answered, by the position it reports.</summary>
    internal static void CardRewardAnswered(IReadOnlyList<CardModel> offered, int? option)
    {
        var recorder = Active;
        if (recorder is null || recorder._finished) return;

        // No option means the screen was dismissed rather than answered, which reaches
        // the loot screen as a skip and is recorded there.
        if (option is not { } index) return;

        if (index < 0 || index >= offered.Count)
        {
            // Past the cards is one of the reward's alternatives - a reroll or a swap -
            // which this format has no verb for. Refused rather than dropped: a
            // decision nobody wrote down is one the replay would make differently.
            recorder.Refuse(
                $"A card reward was answered with option " +
                $"{index.ToString(CultureInfo.InvariantCulture)}, which is past the " +
                $"{offered.Count.ToString(CultureInfo.InvariantCulture)} card(s) it offered and is therefore " +
                "one of its alternatives. This format has no verb for those, so the recording cannot say what " +
                "was taken.");
            return;
        }

        var alternative = Corruption.NominateCard(
            [.. offered.Select(card => card.Id.ToString())], index);

        lock (Gate)
        {
            recorder._screenPicks.Add(new CardScreenPick(
                offered[index].Id.ToString(), index, alternative?.OptionIndex, alternative?.CardId));
        }
    }

    /// <summary>
    /// Records each queued decision once the engine has finished with it.
    ///
    /// One at a time and in order, because each decision's reading is of the state
    /// <em>it</em> left: a batch settled together would give two decisions one state
    /// and put the second one's effects on the first.
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

                next = _pending.Peek();
            }

            var taken = false;
            try
            {
                var unsettled = await Settle(
                    next.EngineWork,
                    () => _disposed || _finished
                        ? "The recording ended before this decision could be read."
                        : RunWentAway());
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

                    _pending.Dequeue();
                    taken = true;
                }

                if (unsettled is not null)
                {
                    Refuse($"A {next.Verb} could not be read: {unsettled}");
                    continue;
                }

                if (NothingHappened(next))
                {
                    // The player opened a screen and backed out of it, or the engine
                    // turned the decision down. Recording it would put an action in the
                    // history that a replay would make differently, and the two
                    // together are what say it: the engine said no, and the run's
                    // complete state - draw order and every random stream included - is
                    // where it was before.
                    Log.Info(
                        $"[{RunmobileMod.ModId}] a {next.Verb} was not taken and the run is unchanged, so it " +
                        "is not recorded", 2);
                    continue;
                }

                Commit(next.Verb, next.Args);
            }
            catch (Exception ex)
            {
                // Only this decision is dropped. Taking another off the queue here
                // would lose one nobody has looked at yet, which is how a history ends
                // up missing a decision it never even refused.
                if (!taken)
                {
                    lock (Gate)
                    {
                        if (_pending.Count > 0) _pending.Dequeue();
                    }
                }

                Refuse($"A {next.Verb} could not be recorded: {ex.GetType().Name}: {ex.Message}");
            }
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
        string.Equals(_capture.LastDigest, LiveRun.Project().Digest(), StringComparison.Ordinal);

    /// <summary>
    /// Waits for the engine to finish what a decision started.
    ///
    /// Two questions: the engine task the decision handed back, where there was one,
    /// and the run's own action queue. Both, because they are different halves of the
    /// same work - entering a map node is a task that builds a room, and playing a card
    /// is a queue that drains - and a reading taken between them would be of a run
    /// halfway through a decision.
    /// </summary>
    /// <param name="stopped">Why there is no longer a recording to settle for, or null
    /// while there still is. <see cref="WaitForTheEngine"/> says why it is a sentence.</param>
    /// <returns>Null once the engine has settled, or the sentence saying what it was
    /// still waiting for.</returns>
    private static Task<string?> Settle(Task? engineWork, Func<string?> stopped) =>
        WaitForTheEngine(
            () => ScreensOpen,
            stopped,
            // The queue is asked twice with a tick between, because a decision that has
            // not enqueued its work yet reads as an engine with nothing to do.
            () => (engineWork is null || engineWork.IsCompleted) &&
                  RunManager.Instance is { ActionExecutor.IsRunning: false } manager &&
                  manager.ActionQueueSet.IsEmpty,
            () => RecordedFightRun.LetTheGameRun(SettleBudgetSeconds),
            () => RecordedFightRun.LetTheGameRun(SettlePollSeconds),
            $"The engine did not settle within " +
            $"{SettleBudgetSeconds.ToString(CultureInfo.InvariantCulture)} seconds of the last card screen " +
            "closing, so the recorder cannot say what state this decision left.");

    /// <summary>Why a settle should stop because the run itself went away, or null while
    /// it is still being played.</summary>
    private static string? RunWentAway() =>
        RunManager.Instance is { IsInProgress: true }
            ? null
            : "The run ended while the recorder was reading it.";

    /// <summary>
    /// Writes one decision, and the card-screen picks it pulled out of the player,
    /// into the capture and the journal.
    ///
    /// The picks share this decision's reading because that is what they are: a card
    /// screen is answered inside the call that opened it, so the state after the
    /// screen's answer and the state after the decision are the same state. The
    /// headless driver reads them back the same way - the selection is confirmed and
    /// changes nothing - so the two traces have the same shape.
    /// </summary>
    private void Commit(string verbName, IReadOnlyDictionary<string, string> args)
    {
        if (!Enum.TryParse<ActionVerb>(verbName, out var verb))
        {
            Refuse($"A '{verbName}' was announced, which is not a decision this format names.");
            return;
        }

        var (sample, digest) = LiveRun.Read();
        var clock = LiveRun.RunClockMs();

        List<CardScreenPick> picks;
        lock (Gate)
        {
            picks = _screenPicks.ToList();
            _screenPicks.Clear();
        }

        // A card reward names the card it took itself; every other screen is answered
        // by the selections recorded after the decision that opened it.
        if (verb == ActionVerb.TakeCard)
        {
            if (picks.Count != 1)
            {
                Refuse(
                    $"A card reward was taken and the recorder saw " +
                    $"{picks.Count.ToString(CultureInfo.InvariantCulture)} screen answer(s) for it. Exactly " +
                    "one card comes off a card reward, and a recording that could not say which cannot be " +
                    "replayed.");
                return;
            }

            var reward = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["card_id"] = picks[0].CardId,
                ["option_index"] = picks[0].OptionIndex.ToString(CultureInfo.InvariantCulture),
            };

            // The two appear together or not at all, which is what the validator
            // requires of them: an id with no position names no decision to take.
            if (picks[0] is { AlternativeCardId: { } alternativeCard, AlternativeOptionIndex: { } alternativeIndex })
            {
                reward[Corruption.AlternativeCardId] = alternativeCard;
                reward[Corruption.AlternativeOptionIndex] =
                    alternativeIndex.ToString(CultureInfo.InvariantCulture);
            }

            args = reward;
            picks.Clear();
        }

        Write(_capture.Record(verb, args, sample, digest, clock));
        WritePicks(picks, sample, digest, clock);

        StartOrStopWatchingTheFight();
    }

    /// <summary>
    /// Records the card screens a decision pulled out of the player, sharing that
    /// decision's reading.
    ///
    /// They share it because that is what they are: a card screen is answered from
    /// inside the call that opened it, so the state after the screen's answer and the
    /// state after the decision are the same state.
    /// </summary>
    private void WritePicks(
        IEnumerable<CardScreenPick> picks,
        IReadOnlyDictionary<string, string> after,
        string digest,
        int? clock)
    {
        foreach (var pick in picks)
        {
            var args = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["card_id"] = pick.CardId,
                ["option_index"] = pick.OptionIndex.ToString(CultureInfo.InvariantCulture),
            };

            if (pick.AlternativeOptionIndex is { } alternative)
            {
                args[Corruption.AlternativeOptionIndex] = alternative.ToString(CultureInfo.InvariantCulture);
            }

            Write(_capture.Record(ActionVerb.SelectCardFromScreen, args, after, digest, clock));
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
            // is taken; the transport's callback is the Combat Trainer's.
            _observer = PlayerFightObserver.Start(
                run.Players[0], LiveRun.Sample, FightSink(), () => { }, () => { });
            return;
        }

        if (_capture.Fight is null && _observer is not null)
        {
            _observer.Dispose();
            _observer = null;
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
        beginStep: (verb, args, before, _) =>
        {
            if (_openFightStep is { } stranded)
            {
                // The observer only reaches this where the previous action had already
                // finished, so the state that closed it is the state this one starts
                // from and nothing is guessed.
                CommitFightStep(stranded.Verb, stranded.Args, before);
            }

            _openFightStep = (verb, args);
        },
        completeStep: CloseFightStep,
        discardOpenStep: () => _openFightStep = null,
        finish: FinishFight,
        markIncomplete: Refuse);

    private void CloseFightStep(IReadOnlyDictionary<string, string> after)
    {
        if (_openFightStep is not { } open) return;
        _openFightStep = null;
        CommitFightStep(open.Verb, open.Args, after);
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
            return;
        }

        const string reason =
            "The fight ended with no action being sampled, so its end belongs to nothing the recording holds. " +
            "The history is not a continuous account of this run.";
        _capture.Fight?.MarkIncomplete(reason);
        Refuse(reason);
    }

    /// <summary>
    /// Records one decision made inside a fight, with the state the observer sampled
    /// after it.
    ///
    /// The digest is read here rather than handed over, because a sink speaks in
    /// samples and a boundary is identified by the whole canonical state. Both are
    /// readings of the same instant: the observer takes its sample the moment the
    /// engine settles and this runs inside that same call.
    /// </summary>
    private void CommitFightStep(
        string verb, IReadOnlyDictionary<string, string> args, IReadOnlyDictionary<string, string> after)
    {
        if (_finished || _disposed) return;

        try
        {
            if (!Enum.TryParse<ActionVerb>(verb, out var parsed))
            {
                Refuse($"The fight observer announced '{verb}', which is not a decision this format names.");
                return;
            }

            var digest = LiveRun.Project().Digest();
            var clock = LiveRun.RunClockMs();

            List<CardScreenPick> picks;
            lock (Gate)
            {
                picks = _screenPicks.ToList();
                _screenPicks.Clear();
            }

            Write(_capture.Record(parsed, args, after, digest, clock));
            WritePicks(picks, after, digest, clock);

            StartOrStopWatchingTheFight();
        }
        catch (Exception ex)
        {
            Refuse($"A {verb} inside a fight could not be recorded: {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ── Finishing ────────────────────────────────────────────────────────────────

    private void Finish(string outcome)
    {
        if (_finished) return;
        _finished = true;

        _observer?.Dispose();
        _observer = null;

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

        _capture.Finish(outcome);

        var manifest = _capture.ToManifest();
        var path = $"{RecordingsDirectory}/{_capture.RunId}.replay.json";
        RunmobileStore.Write(path, ManifestJson.Serialize(manifest) + "\n");

        var problems = ManifestValidator.Validate(manifest);
        Log.Info(
            $"[{RunmobileMod.ModId}] recorded {_capture.RunId}: {outcome}, " +
            $"{manifest.Actions.Count.ToString(CultureInfo.InvariantCulture)} decision(s), " +
            $"{manifest.Boundaries.Count.ToString(CultureInfo.InvariantCulture)} boundary/boundaries, " +
            $"continuity {_capture.Continuity}, written to {path}", 2);

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
    private void Refuse(string reason)
    {
        var line = _capture.MarkBroken(reason);
        Log.Warn($"[{RunmobileMod.ModId}] {reason}", 2);

        try
        {
            Append(_journalPath, line);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] the refusal above could not be written to the journal, so a session " +
                $"continued from it would read this recording as continuous: {ex.GetType().Name}: {ex.Message}", 2);
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
    private sealed record PendingDecision(
        string Verb, IReadOnlyDictionary<string, string> Args, Task? EngineWork);

    /// <summary>A decision read in a prefix and announced in the postfix beside it,
    /// where the engine hands back a task that says when it is finished.</summary>
    private sealed record Decision(string Verb, IReadOnlyDictionary<string, string> Args);

    /// <summary>
    /// One answer a card screen gave, with the alternative that screen also offered.
    ///
    /// The alternative is part of the answer rather than a second reading of it: what a
    /// screen offered is only visible while it is open, and it is what
    /// <c>take-a-different-card</c> and <c>enchant-a-different-card</c> take instead.
    /// Null where the screen offered nothing else, which is a decision with no
    /// alternative rather than one nobody looked for.
    /// </summary>
    private readonly record struct CardScreenPick(
        string CardId,
        int OptionIndex,
        int? AlternativeOptionIndex = null,
        string? AlternativeCardId = null);

    private static IReadOnlyDictionary<string, string> Args(params (string Name, string Value)[] args) =>
        new SortedDictionary<string, string>(
            args.ToDictionary(arg => arg.Name, arg => arg.Value, StringComparer.Ordinal), StringComparer.Ordinal);

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
        ActionVerb.ClaimReward,
        ActionVerb.TakeCard,
        ActionVerb.SkipRewards,
        ActionVerb.SelectCardFromScreen,
        ActionVerb.ChooseRestSiteOption,
        ActionVerb.TakeChestRelic,
        ActionVerb.SkipChestRelic,
        ActionVerb.ProceedToNextAct,
        ActionVerb.ShopPurchase,
        ActionVerb.UsePotion,
        ActionVerb.DiscardPotion,
    ];

    /// <summary>Every patch class this module installs, listed rather than discovered:
    /// <c>PatchAll</c> over the assembly would install the Combat Trainer's too.</summary>
    internal static IReadOnlyList<Type> PatchClasses { get; } =
    [
        typeof(NewRun), typeof(ContinuedRun), typeof(RunOver), typeof(RunTeardown),
        typeof(EventOption), typeof(MapMove), typeof(RewardTaken), typeof(RewardsSkipped),
        typeof(RestSiteOptionTaken), typeof(ChestRelicTaken), typeof(ChestRelicSkipped),
        typeof(ActAdvanced), typeof(ShopPurchased), typeof(CardScreen), typeof(CardRewardScreen),
        typeof(CardRewardAnswer), typeof(PotionUsed), typeof(PotionDiscarded),
    ];

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
    /// An event option, which is also how the opening blessing is chosen.
    ///
    /// The two are the same engine member and different verbs, because a blessing is
    /// offered before the run has a floor and the format records it without an event
    /// id. Told apart by the event's own model type rather than by the decision's
    /// position, so a run whose first decision is not Neow's is still recorded
    /// correctly.
    /// </summary>
    [HarmonyPatch(typeof(EventSynchronizer), nameof(EventSynchronizer.ChooseLocalOption))]
    internal static class EventOption
    {
        [HarmonyPrefix]
        internal static void Before(EventSynchronizer __instance, int index)
        {
            if (Active is null) return;

            try
            {
                var model = __instance.GetLocalEvent();
                Announce(
                    model is Neow ? ActionVerb.ChooseNeowBlessing : ActionVerb.ChooseEventOption,
                    model is Neow
                        ? Args(("option_index", Number(index)))
                        : Args(("event_id", model.Id.ToString()), ("option_index", Number(index))));
            }
            catch (Exception ex)
            {
                Active?.Refuse($"An event option could not be read: {ex.GetType().Name}: {ex.Message}");
            }
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

                _decision = new Decision(nameof(ActionVerb.MapMove), args);
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
            AnnounceByName(decision.Verb, decision.Args, __result);
        }

        /// <summary>
        /// Which columns the node the run is standing on leads to.
        ///
        /// Reachability is decided exactly the way <c>RunDriver.MoveToMapNode</c>
        /// decides it - a child of the current point whose type is not
        /// <see cref="MapPointType.Unassigned"/> - so a nominated node is one a replay
        /// can actually enter. Empty where the run has no current node yet, which is a
        /// move with nothing to nominate rather than a failure.
        /// </summary>
        private static IEnumerable<int> ReachableColumns()
        {
            if (LiveRun.State is not { Map: { } map } run) return [];
            if (run.CurrentMapCoord is not { } current) return [];
            if (map.GetPoint(current.col, current.row) is not { } point) return [];

            return point.Children
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

        [HarmonyPrefix]
        internal static void Before(Reward reward)
        {
            _decision = null;
            if (Active is null) return;

            _decision = reward switch
            {
                CardReward => new Decision(nameof(ActionVerb.TakeCard), Args()),
                GoldReward => new Decision(nameof(ActionVerb.ClaimReward), Args(("reward_type", "gold"))),
                PotionReward => new Decision(nameof(ActionVerb.ClaimReward), Args(("reward_type", "potion"))),
                _ => null,
            };

            if (_decision is null)
            {
                Active?.Refuse(
                    $"A {reward.GetType().Name} was taken off a loot screen, and this format has no verb " +
                    "for that kind of reward. The recording cannot say what was claimed.");
            }
        }

        /// <summary>The task the engine handed back finishes when the reward is
        /// finished - including the card screen a card reward opens - which is the
        /// moment there is a state worth reading.</summary>
        [HarmonyPostfix]
        internal static void After(Task<bool> __result)
        {
            if (_decision is not { } decision) return;
            _decision = null;
            AnnounceByName(decision.Verb, decision.Args, __result);
        }
    }

    [HarmonyPatch(typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SkipLocalRewardsSet))]
    internal static class RewardsSkipped
    {
        [HarmonyPrefix]
        internal static void Before()
        {
            if (Active is null) return;
            Announce(ActionVerb.SkipRewards, Args());
        }
    }

    [HarmonyPatch(typeof(RestSiteSynchronizer), nameof(RestSiteSynchronizer.ChooseLocalOption))]
    internal static class RestSiteOptionTaken
    {
        private static Decision? _decision;

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
                    Active?.Refuse(
                        $"A rest site option {Number(index)} was chosen and the rest site offers " +
                        $"{Number(options.Count)}. The recorder cannot say which one that was.");
                    return;
                }

                _decision = new Decision(
                    nameof(ActionVerb.ChooseRestSiteOption),
                    Args(("option_id", options[index].OptionId), ("option_index", Number(index))));
            }
            catch (Exception ex)
            {
                Active?.Refuse($"A rest site option could not be read: {ex.GetType().Name}: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        internal static void After(Task<bool> __result)
        {
            if (_decision is not { } decision) return;
            _decision = null;
            AnnounceByName(decision.Verb, decision.Args, __result);
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
                var relics = __instance.CurrentRelics;
                if (index is not { } position || relics is null || position < 0 || position >= relics.Count)
                {
                    Active?.Refuse(
                        "A treasure chest's relic was picked at a position this chest does not offer, so the " +
                        "recorder cannot say which relic was taken.");
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
    /// </summary>
    [HarmonyPatch(typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper))]
    internal static class ShopPurchased
    {
        private static Decision? _decision;

        [HarmonyPostfix]
        internal static void After(Task<bool> __result)
        {
            if (_decision is not { } decision) return;
            _decision = null;
            AnnounceByName(decision.Verb, decision.Args, __result);
        }

        [HarmonyPrefix]
        internal static void Before(MerchantEntry __instance, MerchantInventory? inventory)
        {
            _decision = null;
            if (Active is null) return;

            try
            {
                if (inventory is null)
                {
                    Active?.Refuse(
                        "Something was bought from a merchant with no inventory, so the recorder cannot say " +
                        "which shelf it came off.");
                    return;
                }

                if (__instance is MerchantCardRemovalEntry)
                {
                    _decision = new Decision(
                        nameof(ActionVerb.ShopPurchase), Args(("kind", ShopPurchaseKinds.CardRemoval)));
                    return;
                }

                foreach (var (kind, shelf) in Shelves(inventory))
                {
                    var index = shelf.IndexOf(__instance);
                    if (index < 0) continue;

                    var id = IdOf(__instance);
                    if (id is null) break;

                    _decision = new Decision(
                        nameof(ActionVerb.ShopPurchase),
                        Args(
                            ("kind", kind),
                            ("option_index", Number(index)),
                            (ShopPurchaseKinds.IdArgument(kind)!, id)));
                    return;
                }

                Active?.Refuse(
                    $"A {__instance.GetType().Name} was bought and it is not on any shelf this recorder " +
                    "knows, so the recording cannot say what it was.");
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
    /// Every card screen the game opens over a pile, a deck or a hand.
    ///
    /// One patch for all of them, because they share the base that holds both halves
    /// of the answer: the list the screen offered and the cards that came back. The
    /// returned task is handed on unchanged, having been looked at.
    /// </summary>
    [HarmonyPatch(typeof(NCardGridSelectionScreen), nameof(NCardGridSelectionScreen.CardsSelected))]
    internal static class CardScreen
    {
        [HarmonyPostfix]
        internal static void After(
            NCardGridSelectionScreen __instance, ref Task<IEnumerable<CardModel>> __result) =>
            __result = Observe(__instance, __result);

        internal static async Task<IEnumerable<CardModel>> Observe(
            NCardGridSelectionScreen screen, Task<IEnumerable<CardModel>> inner)
        {
            var chosen = await WhileOnScreen(inner);

            try
            {
                if (OfferedCards(screen) is { } offered) CardScreenAnswered(offered, chosen);
                else
                {
                    Active?.Refuse(
                        "A card screen answered and this build does not expose what it offered, so the " +
                        "recorder cannot say which option was picked.");
                }
            }
            catch (Exception ex)
            {
                Active?.Refuse($"A card screen's answer could not be read: {ex.GetType().Name}: {ex.Message}");
            }

            return chosen;
        }

        /// <summary>The list the screen was built with. Read by name and refused loudly
        /// when a build no longer has it, because a screen whose options nobody can see
        /// is a decision nobody can record.</summary>
        internal static IReadOnlyList<CardModel>? OfferedCards(NCardGridSelectionScreen screen) =>
            typeof(NCardGridSelectionScreen)
                .GetField("_cards", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(screen) as IReadOnlyList<CardModel>;
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

        [HarmonyPostfix]
        internal static void After(IReadOnlyList<CardCreationResult> options)
        {
            if (Active is null) return;
            Offered = [.. options.Select(option => option.Card)];
        }
    }

    /// <summary>The position that screen came back with, which is what the format
    /// records as the card reward's option index.</summary>
    [HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.OptionSelected))]
    internal static class CardRewardAnswer
    {
        [HarmonyPostfix]
        internal static void After(ref Task<int?> __result) => __result = Observe(__result);

        internal static async Task<int?> Observe(Task<int?> inner)
        {
            var option = await WhileOnScreen(inner);

            try
            {
                CardRewardAnswered(CardRewardScreen.Offered, option);
            }
            catch (Exception ex)
            {
                Active?.Refuse($"A card reward's answer could not be read: {ex.GetType().Name}: {ex.Message}");
            }

            return option;
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
                    Active?.Refuse(
                        $"A {__instance.Id} was drunk and it is not on the belt this recorder can see, so the " +
                        "recording cannot say which slot it came off.");
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
                    Active?.Refuse(
                        $"A potion was discarded from slot {Number(slot)}, which holds nothing this recorder " +
                        "can see, so the recording cannot say which potion was given up.");
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

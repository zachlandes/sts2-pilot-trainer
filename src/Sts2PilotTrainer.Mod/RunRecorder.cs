using System.Globalization;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.RestSite;
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
        var deadline = RecordedFightRun.LetTheGameRun(AttachBudgetSeconds);
        while (true)
        {
            if (Active is not null || ProfileWriteBarrier.IsActive) return;
            if (LiveRun.State is not null && Floor(LiveRun.Sample()) >= 1) break;

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
        if (!await Settle(null)) return;

        try
        {
            if (Active is not null || ProfileWriteBarrier.IsActive) return;
            if (LiveRun.State is not { } run) return;

            // A screen counter left over from a previous run would make every settle in
            // this one wait for a screen nobody is looking at.
            Interlocked.Exchange(ref _screensOpen, 0);

            var startedUtc = LiveRun.RunStartedUtc();
            var runId = LiveRun.NameRecording(run.Rng.StringSeed, startedUtc);
            var journalPath = $"{RecordingsDirectory}/{runId}{RunJournal.FileExtension}";
            var (sample, digest) = LiveRun.Read();
            var clock = LiveRun.RunClockMs();

            RunCapture capture;
            if (RunmobileStore.Read(journalPath) is { } existing)
            {
                capture = RunCapture.Resume(RunJournal.Parse(existing), digest);
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

            Active = new RunRecorder(capture, journalPath);
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

    /// <summary>Which build of the recorder is writing, so a defect found in one is
    /// traceable to everything it wrote.</summary>
    internal static string RecorderVersion =>
        $"runmobile-recorder/{typeof(RunRecorder).Assembly.GetName().Version?.ToString() ?? "0.0.0"}";

    // ── Decisions ────────────────────────────────────────────────────────────────

    /// <summary>
    /// A decision the game has just been asked to make.
    ///
    /// Queued rather than recorded, because what a decision left behind can only be
    /// read once the engine has finished doing it. The arguments are read now, while
    /// the shelf still holds the thing that was bought and the hand still holds the
    /// card that was played; the state is read at the other end of the settle.
    /// </summary>
    internal static void Announce(
        ActionVerb verb, IReadOnlyDictionary<string, string> args, Task? engineWork = null)
    {
        var recorder = Active;
        if (recorder is null || recorder._finished) return;

        lock (Gate)
        {
            recorder._pending.Enqueue(new PendingDecision(verb.ToString(), args, engineWork));
            if (recorder._pumping) return;
            recorder._pumping = true;
        }

        _ = recorder.Pump();
    }

    /// <summary>The same, for a decision already held by name.</summary>
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

            lock (Gate)
            {
                recorder._screenPicks.Add(new CardScreenPick(card.Id.ToString(), index));
            }
        }
    }

    /// <summary>A card screen has gone up in front of the player.</summary>
    internal static void ScreenOpened() => Interlocked.Increment(ref _screensOpen);

    /// <summary>That screen has been answered.</summary>
    internal static void ScreenClosed() => Interlocked.Decrement(ref _screensOpen);

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

        lock (Gate)
        {
            recorder._screenPicks.Add(new CardScreenPick(offered[index].Id.ToString(), index));
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
                var settled = await Settle(next.EngineWork);
                lock (Gate)
                {
                    if (_disposed)
                    {
                        _pumping = false;
                        return;
                    }

                    _pending.Dequeue();
                    taken = true;
                }

                if (!settled)
                {
                    Refuse(
                        $"The engine did not settle within " +
                        $"{SettleBudgetSeconds.ToString(CultureInfo.InvariantCulture)} seconds after a " +
                        $"{next.Verb}, so the recorder cannot say what state it left.");
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
    private static async Task<bool> Settle(Task? engineWork)
    {
        // A person deciding is not an engine failing to settle, so the budget does not
        // start until every screen in front of them has been answered. A screen nobody
        // ever answers is a game that has stopped, which is a bigger problem than a
        // recording.
        while (_screensOpen > 0) await RecordedFightRun.LetTheGameRun(SettlePollSeconds);

        var deadline = RecordedFightRun.LetTheGameRun(SettleBudgetSeconds);

        if (engineWork is not null && !engineWork.IsCompleted)
        {
            if (await Task.WhenAny(engineWork, deadline) != engineWork) return false;
        }

        // The queue is asked twice with a tick between, because a decision that has not
        // enqueued its work yet reads as an engine with nothing to do.
        var idleTicks = 0;
        while (idleTicks < 2)
        {
            if (deadline.IsCompleted) return false;

            var poll = RecordedFightRun.LetTheGameRun(SettlePollSeconds);
            if (await Task.WhenAny(poll, deadline) != poll) return false;
            await poll;

            var manager = RunManager.Instance;
            if (manager is null || !manager.IsInProgress) return false;
            idleTicks = !manager.ActionExecutor.IsRunning && manager.ActionQueueSet.IsEmpty ? idleTicks + 1 : 0;
        }

        return true;
    }

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

            args = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["card_id"] = picks[0].CardId,
                ["option_index"] = picks[0].OptionIndex.ToString(CultureInfo.InvariantCulture),
            };
            picks.Clear();
        }

        Write(_capture.Record(verb, args, sample, digest, clock));

        foreach (var pick in picks)
        {
            Write(_capture.Record(
                ActionVerb.SelectCardFromScreen,
                new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["card_id"] = pick.CardId,
                    ["option_index"] = pick.OptionIndex.ToString(CultureInfo.InvariantCulture),
                },
                sample,
                digest,
                clock));
        }

        StartOrStopWatchingTheFight();
    }

    /// <summary>
    /// Appends one line to the journal.
    ///
    /// Appended rather than rewritten, which is the whole reason the journal is a line
    /// per decision: finishing a write means finishing a line, so a crash leaves a real
    /// recording of the part of the run that happened rather than half of a document
    /// describing all of it.
    /// </summary>
    private void Write(RunJournalEntry entry) =>
        File.AppendAllText(RunmobileStore.PrepareForWrite(_journalPath), RunJournal.RenderEntry(entry));

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
            if (LiveRun.State is not { Players.Count: > 0 } run) return;
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
        finish: CloseFightStep,
        markIncomplete: Refuse);

    private void CloseFightStep(IReadOnlyDictionary<string, string> after)
    {
        if (_openFightStep is not { } open) return;
        _openFightStep = null;
        CommitFightStep(open.Verb, open.Args, after);
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
            foreach (var pick in picks)
            {
                Write(_capture.Record(
                    ActionVerb.SelectCardFromScreen,
                    new SortedDictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["card_id"] = pick.CardId,
                        ["option_index"] = pick.OptionIndex.ToString(CultureInfo.InvariantCulture),
                    },
                    after,
                    digest,
                    clock));
            }

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

    private void Refuse(string reason)
    {
        _capture.MarkBroken(reason);
        Log.Warn($"[{RunmobileMod.ModId}] {reason}", 2);
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

    private readonly record struct CardScreenPick(string CardId, int OptionIndex);

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
        typeof(CardRewardAnswer),
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

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterMapCoord))]
    internal static class MapMove
    {
        [HarmonyPostfix]
        internal static void After(MapCoord coord, Task __result)
        {
            if (Active is null) return;

            try
            {
                // The act is read from the run rather than from the coordinate, which
                // carries only a row and a column; a move never crosses acts, so the
                // act the run is in is the act the move is in.
                var act = LiveRun.State?.CurrentActIndex ?? 0;
                Announce(
                    ActionVerb.MapMove,
                    Args(("act", Number(act)), ("row", Number(coord.row)), ("column", Number(coord.col))),
                    __result);
            }
            catch (Exception ex)
            {
                Active?.Refuse($"A map move could not be read: {ex.GetType().Name}: {ex.Message}");
            }
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
        [HarmonyPrefix]
        internal static void Before()
        {
            if (Active is null) return;
            ScreenOpened();
        }

        [HarmonyPostfix]
        internal static void After(
            NCardGridSelectionScreen __instance, ref Task<IEnumerable<CardModel>> __result)
        {
            if (Active is null) return;
            __result = Observe(__instance, __result);
        }

        private static async Task<IEnumerable<CardModel>> Observe(
            NCardGridSelectionScreen screen, Task<IEnumerable<CardModel>> inner)
        {
            IEnumerable<CardModel> chosen;
            try
            {
                chosen = await inner;
            }
            finally
            {
                ScreenClosed();
            }

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
        [HarmonyPrefix]
        internal static void Before()
        {
            if (Active is null) return;
            ScreenOpened();
        }

        [HarmonyPostfix]
        internal static void After(ref Task<int?> __result)
        {
            if (Active is null) return;
            __result = Observe(__result);
        }

        private static async Task<int?> Observe(Task<int?> inner)
        {
            int? option;
            try
            {
                option = await inner;
            }
            finally
            {
                ScreenClosed();
            }

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
}

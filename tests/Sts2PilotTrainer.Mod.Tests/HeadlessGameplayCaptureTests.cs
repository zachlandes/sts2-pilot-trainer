using System.Globalization;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The recorder failure class the captain hit in ordinary play, caught headlessly in
/// the merge suite instead of by playing.
///
/// These drive a real run through the actual recorder and its fight observer - attached
/// to a headless engine, played by a scripted player - and then a fresh replay of what
/// the recorder wrote. Two seams make that possible and are the first two tests: the
/// attach identity, which the retail client gets from adopting the running game and a
/// headless attach gets from the engine it is running
/// (<see cref="RunRecorder.GameIdentitySource"/>); and the settle clock, which the
/// retail client runs on the scene tree's timer and a headless attach runs on the
/// arbiter's drain (<see cref="RunRecorder.Clock"/>, <see cref="PumpedSettleClock"/>).
/// Before them the recorder could not settle a decision or close a fight step in a
/// process with no scene tree, so no whole run had ever been recorded and replayed
/// without the client.
///
/// The natural-run test is the end-to-end proof the captain's concern demands, and the
/// slice-6 fixture deferred from slice 3: a normal run, its cards dealt by the seed
/// alone, finishes <c>continuity = continuous</c> and <c>integrity = complete</c>, the
/// validator accepts it, and a fresh replay of its own actions reproduces every declared
/// boundary digest and the trace the recorder captured. It uses Survivor - a card in the
/// Silent's starting deck, so it opens an in-combat discard prompt on every seed with no
/// sweep - which drives an in-combat <c>FromHandForDiscard</c> prompt through the whole
/// recorder and observer.
///
/// The coverage split is deliberate and this is it. Survivor is the natural-run proof:
/// the one in-combat prompt shape driven through <c>RunRecorder.Attach</c>, the
/// <c>PlayerFightObserver</c>, and a full-chain replay of the recording it wrote. The
/// other in-combat prompt shapes the report lists - Headbutt over the discard pile, a
/// choose-a-card screen taken and skipped, an "up to N" answered with fewer and with
/// none, and a prompt the game's own selector answers - are covered as
/// capture-versus-replay equivalence in <c>CardPromptCaptureTests</c> and
/// <c>CardPromptOfferTests</c>, on the same merge gate, and this suite does not
/// duplicate them. Their card is put in the hand with the engine's own command at fight
/// start, which changes the combat state after the recorder has sampled the fight's
/// boundary; the observer's fight capture refuses a gap between two samples rather than
/// bridging it, so a staged card cannot go through it and those tests answer the prompt
/// without the observer on purpose. The natural run is the one that exercises an
/// in-combat prompt through the observer, because its card was dealt before the
/// boundary, by the seed.
///
/// A fresh replay of the natural run's actions is done in this process rather than
/// through <c>./scripts/arbiter replay</c>: a recording captured headlessly carries the
/// headless host's own patch roster, which the retail environment preflight in front of
/// the CLI's replay correctly refuses - that gate is about a clean retail game, which is
/// orthogonal to whether the recorded actions reproduce. The in-process replay is
/// <see cref="Engine.Arbiter.ReplayStartedRun"/>, the same method the CLI's replay runs past
/// that gate, so what "reproduces every declared boundary digest" means here is what it
/// means at the command line. <c>arbiter validate</c> is exercised in-process through
/// <see cref="ManifestValidator"/>, the validator itself.
/// </summary>
public sealed class HeadlessGameplayCaptureTests : IDisposable
{
    private const string Seed = "P1L0TTRA1NER";

    /// <summary>A seed whose first act, played alone, the whole-act journey's own rules
    /// win on the cheapest route to its boss. Found by searching seeds, because the
    /// fixture's seed was chosen for a three-act run and its act plays differently
    /// alone; a claim about this journey's rules on this build and nothing else.</summary>
    private const string WonRunSeed = "A249YBES73";
    private static readonly string[] Acts = ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"];

    /// <summary>Where the recordings these tests write are kept: a directory named by
    /// this variable, left in place for a demo to read, or a temporary one deleted
    /// with the test.</summary>
    private const string KeepRecordingsVariable = "HEADLESS_CAPTURE_RECORDINGS";

    private readonly string _root = Path.Combine(
        Environment.GetEnvironmentVariable(KeepRecordingsVariable) is { Length: > 0 } kept
            ? Path.Combine(kept, $"headless-capture-{Guid.NewGuid():N}")
            : Path.Combine(Path.GetTempPath(), $"headless-capture-{Guid.NewGuid():N}"),
        "Runmobile", "steam", "test", "profile1");

    public HeadlessGameplayCaptureTests()
    {
        EngineHost.Start();
        Directory.CreateDirectory(_root);
        RunmobileStore.UseRootForTesting(_root);
    }

    public void Dispose()
    {
        if (RunManager.Instance is { IsInProgress: true } manager) manager.CleanUp();
        RunmobileStore.UseRootForTesting(null);
        RunRecorder.ResetHostSeamsForTesting();
        HeadlessEngine.Forget();
        if (Environment.GetEnvironmentVariable(KeepRecordingsVariable) is { Length: > 0 }) return;
        var sandbox = _root[.._root.IndexOf("Runmobile", StringComparison.Ordinal)];
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
    }

    // ── Seam 1: the attach identity ──────────────────────────────────────────────

    /// <summary>
    /// The headless identity source is what lets the recorder attach; the retail one
    /// cannot.
    ///
    /// The retail default asks the mod to adopt the running game, which
    /// <c>EngineHost.AdoptRunningGame</c> refuses in a process that started its own
    /// headless engine - so the default cannot attach here, and that refusal is what made
    /// the whole recorder path unreachable headlessly. The headless source says the engine
    /// is up rather than adopting one, and the identity <see cref="LiveRun"/> then writes
    /// comes from the prepared copy, honestly, because that is the engine this process
    /// runs.
    /// </summary>
    [GameFact]
    public void TheHeadlessIdentitySourceIsWhatLetsTheRecorderAttach()
    {
        using var recording = Patched();
        var session = StartRun("CHARACTER.SILENT");
        using var driver = new RunDriver(session);
        driver.EnterFirstRoom();

        RunRecorder.GameIdentitySource = RunmobileMod.EnsureAdopted;
        Assert.Equal(RunAttachment.CouldNotTakeTheGame, RunRecorder.Attach());
        Assert.Null(RunRecorder.Active);

        RunRecorder.GameIdentitySource = HeadlessIdentity;
        Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());
        Assert.NotNull(RunRecorder.Active);
        Assert.Equal("v0.111.0", RunRecorder.Active!.Capture.Identity.BuildVersion);
        Assert.Equal("CHARACTER.SILENT", RunRecorder.Active.Capture.Identity.Character);
    }

    // ── Seam 2: the settle clock ─────────────────────────────────────────────────

    /// <summary>
    /// The recorder records a run only on a clock that hands the process back; the scene
    /// tree's throws for want of a scene tree and nothing settles.
    ///
    /// Both the recorder's between-decision settle and the fight observer's after-action
    /// settle wait on the clock the attach site supplies. On the scene-tree clock every
    /// wait faults for want of a scene tree, so the run's first decisions are refused and
    /// no in-fight play is ever committed - the state that meant no whole run was ever
    /// recorded headlessly. On the pumped clock the same run settles through the arbiter's
    /// drain and records its fight with no refusal.
    /// </summary>
    [GameFact]
    public void TheRecorderSettlesOnlyOnASuppliedClock()
    {
        using var recording = Patched();

        var onSceneTree = CaptureFirstInFightPlay(SettleClock.SceneTree);
        Assert.NotEmpty(onSceneTree.Refusals);
        Assert.DoesNotContain(onSceneTree.Actions, action => action.Verb == ActionVerb.PlayCard);

        RunRecorder.RunTornDown();
        var freshRoot = Path.Combine(Path.GetDirectoryName(_root)!, "profile2");
        Directory.CreateDirectory(freshRoot);
        RunmobileStore.UseRootForTesting(freshRoot);

        var onPump = CaptureFirstInFightPlay(new PumpedSettleClock());
        Assert.Empty(onPump.Refusals);
        Assert.Contains(onPump.Actions, action => action.Verb == ActionVerb.PlayCard);
    }

    // ── The natural-run continuity fixture ───────────────────────────────────────

    /// <summary>
    /// A normal run finishes complete and replayable.
    ///
    /// The Silent plays their own starting deck - Survivor and all - through the real
    /// recorder and observer, wins the first fight, and abandons the run. What the
    /// recorder wrote is continuous, complete, and witnessed from run start; the validator
    /// accepts it; and a fresh replay of its own actions reproduces every declared
    /// boundary digest and the trace the recorder captured. This is the end-to-end proof
    /// that a run played naturally through this path records and replays, deferred to here
    /// from slice 3.
    /// </summary>
    [GameFact]
    public void ANaturalRunFinishesContinuousAndCompleteAndReplaysEveryBoundary()
    {
        using var recording = Patched();
        RunRecorder.GameIdentitySource = HeadlessIdentity;
        RunRecorder.Clock = new PumpedSettleClock();

        var (manifest, capture) = CaptureNaturalSilentRun();

        var native = manifest.Source.Native!;
        Assert.Equal(NativeSource.ContinuousContinuity, native.Continuity);
        Assert.Equal(NativeSource.CompleteIntegrity, native.Integrity);
        Assert.True(native.WitnessedRunStart.Value);
        Assert.Empty(capture.Refusals);

        var validation = ManifestValidator.Validate(manifest);
        Assert.True(validation.IsValid, validation.Describe());

        Assert.Contains(manifest.Boundaries, boundary => boundary.IsCombatStart);

        var replay = FreshReplayFromSeed(manifest);

        // Every boundary the recording declares reproduces at its own coordinate.
        foreach (var declared in manifest.Boundaries)
        {
            var derived = replay.Boundaries.SingleOrDefault(candidate =>
                candidate.Kind == declared.Kind && candidate.Fight == declared.Fight &&
                candidate.Floor == declared.Floor && candidate.Turn == declared.Turn);
            Assert.True(derived is not null, $"the replay did not reach {declared.Describe()}");
            Assert.Equal(declared.Digest.Value, derived!.Digest.Value);
        }

        AssertSameTrace(capture.Trace, replay.Trace);
    }

    // ── The won run ──────────────────────────────────────────────────────────────

    /// <summary>
    /// A run played to its victory finishes won, continuous and complete, with the
    /// decision it was won on in the history, and replays.
    ///
    /// The game wins a run from the Architect's event room: the last boss's reward
    /// screen issues the same act-change vote every act ends with, <c>EnterNextAct</c>
    /// opens the victory room in place of an act, and the event's PROCEED calls
    /// <c>RunManager.WinRun</c>, which calls <c>OnEnded(true)</c> inside that very
    /// press. Under Instant fast mode the two animations before it are skipped, so
    /// <c>OnEnded</c> runs synchronously before the recorder's pump has polled once
    /// and PROCEED is still a pending decision when the recording is finished. Headless
    /// <c>Cmd.Wait</c> is neutralised, so this process is that setting exactly; before
    /// the recorder read the decision the run ended inside, every won recording made
    /// this way finished <c>continuity = broken</c> with its last decision stranded,
    /// and the replay driver refused the last act's transition besides.
    ///
    /// The act is walked by the whole-act fixture's own journey
    /// (<see cref="SyntheticFixtureGenerator.WalkTheAct"/>) on a run of that act alone,
    /// so its boss is the run's last, with the recorder watching every decision go
    /// through the game members it patches; the card rewards it takes are handed to the
    /// recorder by <see cref="HeadlessCardRewardScreen"/>, standing in for the screen
    /// this process never draws. The recording is then replayed fresh from the seed,
    /// the way every recording here is, and held to its declared boundaries and its
    /// trace.
    /// </summary>
    [GameFact]
    public void AWonRunFinishesContinuousWithTheDecisionItWasWonOnAndReplays()
    {
        using var recording = Patched();
        RunRecorder.GameIdentitySource = HeadlessIdentity;
        RunRecorder.Clock = new PumpedSettleClock();

        var (manifest, capture) = CaptureWonRun();

        var native = manifest.Source.Native!;
        Assert.Equal("won", native.Outcome);
        Assert.Equal(NativeSource.ContinuousContinuity, native.Continuity);
        Assert.Equal(NativeSource.CompleteIntegrity, native.Integrity);
        Assert.Empty(capture.Refusals);

        var last = manifest.Actions[^1];
        Assert.Equal(ActionVerb.ChooseEventOption, last.Verb);
        Assert.Equal("EVENT.THE_ARCHITECT", last.Args["event_id"]);
        Assert.Equal("PROCEED", last.Args["option_key"]);
        Assert.Contains(manifest.Actions, action => action.Verb == ActionVerb.ProceedToNextAct);

        var validation = ManifestValidator.Validate(manifest);
        Assert.True(validation.IsValid, validation.Describe());

        var replay = FreshReplayFromSeed(manifest);
        foreach (var declared in manifest.Boundaries)
        {
            var derived = replay.Boundaries.SingleOrDefault(candidate =>
                candidate.Kind == declared.Kind && candidate.Fight == declared.Fight &&
                candidate.Floor == declared.Floor && candidate.Turn == declared.Turn);
            Assert.True(derived is not null, $"the replay did not reach {declared.Describe()}");
            Assert.Equal(declared.Digest.Value, derived!.Digest.Value);
        }

        AssertSameTrace(capture.Trace, replay.Trace);
    }

    /// <summary>Walks the whole-act journey through the recorder on a run of that act
    /// alone, then the victory room's dialogue to PROCEED, and hands back what the
    /// recorder wrote when the game ended the run.</summary>
    private (ReplayManifest Manifest, RunCapture Capture) CaptureWonRun()
    {
        if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
        var session = new GameSession();
        session.StartRun(WonRunSeed, "CHARACTER.IRONCLAD", 0, "standard", [Acts[0]]);
        using var driver = new RunDriver(session);
        driver.ImproviseUnrecordedCardSelections();
        driver.EnterFirstRoom();
        Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());

        var walked = SyntheticFixtureGenerator.WalkTheAct(session, driver, [], DrainSettles, visitEveryRoomType: false);

        Assert.True(session.RunState.CurrentRoom is { IsVictoryRoom: true }, "the last act's transition did not open the victory room");
        var recorder = RunRecorder.Active!;
        var capture = recorder.Capture;

        // The Architect's lines, then PROCEED: each is the event's only option
        var seq = walked.Count;
        for (var presses = 0; presses < 20 && capture.State == RunCaptureState.Recording; presses++)
        {
            var options = RunManager.Instance.EventSynchronizer!.GetLocalEvent().CurrentOptions;
            Assert.Single(options);
            driver.Apply(Record(seq++, ActionVerb.ChooseEventOption,
                ("event_id", "EVENT.THE_ARCHITECT"), ("option_index", "0"), ("option_key", RunDriver.OptionKey(options[0]))));
            DrainSettles();
        }

        Assert.Equal(RunCaptureState.Finished, capture.State);
        var manifest = ManifestJson.Deserialize(File.ReadAllText(
            Path.Combine(_root, "recordings", $"{capture.RunId}.replay.json")));
        return (manifest, capture);
    }

    // ── Capturing a natural Silent run ───────────────────────────────────────────

    private (ReplayManifest Manifest, RunCapture Capture) CaptureNaturalSilentRun()
    {
        using var fight = EnterFirstFight("CHARACTER.SILENT");
        var player = fight.Session.RunState.Players[0];
        var survivorPlayed = 0;

        using (CardSelectCmd.SuspendSelectorForTest())
        using (CardSelectCmd.PushSelector(new DiscardFirstOffered(), localOnly: true))
        {
            for (var turn = 0; turn < 40 && Outcome(fight.Session) == "in_progress"; turn++)
            {
                while (Outcome(fight.Session) == "in_progress")
                {
                    var hand = player.PlayerCombatState!.Hand.Cards;
                    var survivor = hand.ToList().FindIndex(card => card.Id.ToString() == "CARD.SURVIVOR");
                    var index = survivor >= 0 && hand[survivor].CanPlay(out _, out _)
                        ? survivor
                        : hand.ToList().FindIndex(card => card.CanPlay(out _, out _));
                    if (index < 0) break;

                    var card = player.PlayerCombatState!.Hand.Cards[index];
                    if (card.Id.ToString() == "CARD.SURVIVOR") survivorPlayed++;
                    Play(card);
                }

                if (Outcome(fight.Session) != "in_progress") break;
                EndTurn(player);
            }
        }

        Assert.True(survivorPlayed > 0, "Survivor was never drawn, so the discard prompt was never exercised.");
        Assert.Equal("victory", Outcome(fight.Session));

        var manifestPath = FinishAsAbandoned(RunRecorder.Active!);
        var capture = RunRecorder.Active!.Capture;
        var manifest = ManifestJson.Deserialize(File.ReadAllText(manifestPath));
        return (manifest, capture);
    }

    // ── The fresh in-process replay of a natural run's own actions ────────────────

    private static (ReplayTrace Trace, IReadOnlyList<ReplayBoundary> Boundaries) FreshReplayFromSeed(
        ReplayManifest manifest)
    {
        if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();

        var session = new GameSession();
        session.StartRun(
            manifest.Environment.Seed.Value,
            manifest.Environment.Character.Value,
            manifest.Environment.Ascension.Value,
            manifest.Environment.GameMode.Value,
            manifest.Environment.Acts.Value,
            RecordedFightEntry.SuppliedProgressFor(manifest));

        try
        {
            var runIdentity = Preflight.EvaluateStartedRun(manifest.Environment);
            Assert.True(runIdentity.Matches, "the started run is not the run the recording describes");

            var outcome = Engine.Arbiter.ReplayStartedRun(
                session, manifest, runIdentity, stopAfterSeq: null, gameModeOverride: null);
            Assert.True(
                outcome.Report.Status == VerificationStatus.Verified,
                $"the replay was {outcome.Report.Status}: {string.Join("; ", outcome.Report.Diagnostics)}");
            return (outcome.Report.Trace!, outcome.Report.Boundaries);
        }
        finally
        {
            if (RunManager.Instance is { IsInProgress: true } manager) manager.CleanUp();
        }
    }

    private static void AssertSameTrace(ReplayTrace captured, ReplayTrace replayed)
    {
        Assert.Equal(
            captured.Steps.Select(step => (step.Seq, step.Verb)),
            replayed.Steps.Select(step => (step.Seq, step.Verb)));

        foreach (var (capturedStep, replayedStep) in captured.Steps.Zip(replayed.Steps))
        {
            Assert.True(
                ReplayTrace.SameSample(capturedStep.Before, replayedStep.Before),
                $"step {capturedStep.Seq} ({capturedStep.Verb}) before-sample diverged");
            Assert.True(
                ReplayTrace.SameSample(capturedStep.After, replayedStep.After),
                $"step {capturedStep.Seq} ({capturedStep.Verb}) after-sample diverged");
        }
    }

    // ── The first in-fight play, for the settle-clock seam ───────────────────────

    /// <summary>Runs the whole capture - setup and the first in-fight play - on
    /// <paramref name="clock"/>, so the clock under test is the one every settle runs on.
    /// On the scene-tree clock the decision settles fault and no fight is ever opened, so
    /// the play reaches no observer; the run's active recorder is returned either way.</summary>
    private RunCapture CaptureFirstInFightPlay(SettleClock clock)
    {
        RunRecorder.GameIdentitySource = HeadlessIdentity;
        RunRecorder.Clock = clock;

        using var fight = EnterFirstFight("CHARACTER.SILENT");
        var player = fight.Session.RunState.Players[0];

        using (CardSelectCmd.SuspendSelectorForTest())
        using (CardSelectCmd.PushSelector(new DiscardFirstOffered(), localOnly: true))
        {
            var index = player.PlayerCombatState!.Hand.Cards.ToList().FindIndex(card => card.CanPlay(out _, out _));
            Assert.True(index >= 0, "no card in the opening hand could be played");
            var card = player.PlayerCombatState!.Hand.Cards[index];
            RunManager.Instance.ActionQueueSet.EnqueueWithoutSynchronizing(new PlayCardAction(card, Target(card)));
            Pump.Drain();
            DrainSettles();
        }

        return RunRecorder.Active!.Capture;
    }

    // ── The run and the fight ────────────────────────────────────────────────────

    /// <summary>A run in its first fight, with the recorder attached and watching. The
    /// driver stays alive so the loot a won fight offers is parked rather than shown; the
    /// fight itself is played on a local selector, past the driver's suspended one.</summary>
    private sealed class Fight(GameSession session, RunDriver driver) : IDisposable
    {
        internal GameSession Session { get; } = session;

        public void Dispose() => driver.Dispose();
    }

    private Fight EnterFirstFight(string character)
    {
        var session = StartRun(character);
        var driver = new RunDriver(session);
        driver.EnterFirstRoom();

        Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());

        driver.Apply(Record(0, ActionVerb.ChooseNeowBlessing, ("option_index", "0")));
        DrainSettles();

        var edge = FirstEdge(session);
        driver.Apply(Record(1, ActionVerb.MapMove,
            ("act", N(session.RunState.CurrentActIndex)),
            ("row", N(edge.ToRow)), ("column", N(edge.ToColumn))));
        DrainSettles();

        Assert.Equal("true", Field(session, "combat.in_progress"));
        return new Fight(session, driver);
    }

    private GameSession StartRun(string character)
    {
        if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
        var session = new GameSession();
        session.StartRun(Seed, character, 0, "standard", Acts);
        return session;
    }

    private void Play(CardModel card)
    {
        RunManager.Instance.ActionQueueSet.EnqueueWithoutSynchronizing(new PlayCardAction(card, Target(card)));
        Pump.Drain();
        DrainSettles();
    }

    private void EndTurn(Player player)
    {
        RunManager.Instance.ActionQueueSet.EnqueueWithoutSynchronizing(
            new EndPlayerTurnAction(player, player.PlayerCombatState!.TurnNumber));
        Pump.Drain();
        DrainSettles();
    }

    /// <summary>Abandons the run after the fight: an honest, complete recording. The
    /// engine's own abandon path is a Godot wait this process cannot run, so its flag is
    /// set the way <c>Abandon</c> sets it and the recording is finished the way the
    /// <c>OnEnded</c> patch finishes it.</summary>
    private string FinishAsAbandoned(RunRecorder recorderBeforeFinish)
    {
        var runId = recorderBeforeFinish.Capture.RunId;
        typeof(RunManager).GetProperty("IsAbandoned")!.SetValue(RunManager.Instance, true);
        RunRecorder.RunEnded(isVictory: false);
        return Path.Combine(_root, "recordings", $"{runId}.replay.json");
    }

    private static void DrainSettles()
    {
        if (RunRecorder.Clock is PumpedSettleClock pumped) pumped.Drain();
    }

    // ── Playing cards ────────────────────────────────────────────────────────────

    private static Creature? Target(CardModel card) =>
        card.TargetType == TargetType.AnyEnemy
            ? CombatManager.Instance!.DebugOnlyGetState()!.Enemies.First(enemy => enemy is { IsAlive: true })
            : null;

    /// <summary>Answers a hand or pile prompt with the first card offered, which every
    /// discard-one prompt in these runs has to offer.</summary>
    private sealed class DiscardFirstOffered : ICardSelector
    {
        public Task<IEnumerable<CardModel>> GetSelectedCards(
            IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            var list = options.ToList();
            IEnumerable<CardModel> chosen = list.Count > 0 && maxSelect > 0 ? [list[0]] : [];
            return Task.FromResult(chosen);
        }

        public CardRewardSelection GetSelectedCardReward(
            IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives) =>
            throw new NotSupportedException("No card reward is answered in these fights.");
    }

    // ── Patch installation and process seams ─────────────────────────────────────

    private static bool HeadlessIdentity() => EngineHost.Origin == EngineOrigin.HeadlessHost;

    /// <summary>Installs exactly the shell and recorder patches the recorder needs to
    /// watch a run, and takes them back off when the test is done, so a process that runs
    /// another test after this one is the one it was.</summary>
    private static Patches Patched() => new();

    private sealed class Patches : IDisposable
    {
        private readonly Harmony _harmony = new($"headless-gameplay-capture.{Guid.NewGuid():N}");
        private readonly Action<CardPrompts.Prompt, IReadOnlyList<CardModel>>? _previousAnswered = CardPrompts.Answered;
        private readonly Action<int?>? _previousReward = CardScreensUp.RewardAnswered;

        internal Patches()
        {
            foreach (var type in CardScreensUp.PatchClasses) _harmony.CreateClassProcessor(type).Patch();
            foreach (var type in CardPrompts.PatchClasses) _harmony.CreateClassProcessor(type).Patch();
            foreach (var type in RunRecorder.PatchClasses) _harmony.CreateClassProcessor(type).Patch();
            _harmony.CreateClassProcessor(typeof(HeadlessCardRewardScreen)).Patch();
            RunRecorder.ReadTheAnswers();
        }

        public void Dispose()
        {
            if (RunManager.Instance is { IsInProgress: true } manager) manager.CleanUp();
            RunRecorder.RunTornDown();
            _harmony.UnpatchAll(_harmony.Id);
            CardPrompts.Answered = _previousAnswered;
            CardScreensUp.RewardAnswered = _previousReward;
            CardPrompts.Forget();
        }
    }

    /// <summary>
    /// The card reward's screen, stood in for where a headless process answers it.
    ///
    /// The recorder reads a card reward's answer off the client's own
    /// <c>NCardRewardSelectionScreen</c>, which this process never draws; the driver
    /// answers the same reward through its <see cref="ManifestCardSelector"/>, the
    /// engine's own seam, from inside the same call. This hands the recorder what the
    /// screen would have: the cards and alternatives offered, in the order the screen
    /// lists them, and the position that came back.
    /// </summary>
    [HarmonyPatch(typeof(ManifestCardSelector), nameof(ManifestCardSelector.GetSelectedCardReward))]
    private static class HeadlessCardRewardScreen
    {
        [HarmonyPostfix]
        internal static void After(
            IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives,
            CardRewardSelection __result)
        {
            var offered = options.Select(option => option.Card).ToList();
            int? position = __result.card is { } card
                ? offered.IndexOf(card)
                : __result.alternative is { } alternative
                    ? offered.Count + alternatives.ToList().IndexOf(alternative)
                    : null;
            RunRecorder.CardRewardAnswered(offered, alternatives, position);
        }
    }

    // ── Small readers ────────────────────────────────────────────────────────────

    private static string Outcome(GameSession session) => Field(session, "combat.outcome");

    private static string Field(GameSession session, string field) =>
        CanonicalStateProjection.Project(session.RunState).Fields[field];

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static MapEdge FirstEdge(GameSession session)
    {
        var current = Field(session, "run.map_coord");
        var separator = current.IndexOf('c');
        var row = int.Parse(current.AsSpan(1, separator - 1), CultureInfo.InvariantCulture);
        var column = int.Parse(current.AsSpan(separator + 1), CultureInfo.InvariantCulture);
        return session.CurrentMapTopology().Edges
            .Where(edge => edge.FromRow == row && edge.FromColumn == column)
            .OrderBy(edge => edge.ToColumn).First();
    }

    private static ActionRecord Record(int seq, ActionVerb verb, params (string Key, string Value)[] args) => new()
    {
        Seq = seq,
        Verb = verb,
        Args = new SortedDictionary<string, string>(
            args.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal),
        Source = FactSource.Declared,
    };
}

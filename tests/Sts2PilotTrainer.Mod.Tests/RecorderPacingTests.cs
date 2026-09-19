using System.Globalization;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The window the retail soak probe found in the recorder, closed: a decision made
/// before the recorder had read after the one before it was misattributed or
/// dropped, and the recording finished <c>integrity = complete</c>.
///
/// Three shapes, each measured on v0.111.0 in the retail client. A card played inside
/// the poll between a fight becoming ready and the map move into it being written
/// reached an executor nobody was subscribed to, and its effects went into the map
/// move's after-reading; a potion drunk in the same window went unrecorded with the
/// belt reading empty a decision later; and a rest option pressed while the map move
/// into the rest site was still settling put the upgraded card into the move's
/// after-reading and a screen answer no screen asked for into the journal. For a
/// person the window is a click within a poll of a screen becoming ready; for a
/// driver it is every decision. Each test stands the window in headlessly, by making
/// the next decision before the recorder's clock is pumped, and asks the question
/// the standard asks: does the recording say what happened, at parity or in a
/// refusal, and never silently something else.
/// </summary>
public sealed class RecorderPacingTests : IDisposable
{
    private const string Seed = "67L571H38L";
    private const string BrainLeechSeed = "3FJVKR7VFR";
    private const string Character = "CHARACTER.IRONCLAD";
    private static readonly string[] Acts = ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"];

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"recorder-pacing-{Guid.NewGuid():N}", "Runmobile", "steam", "test", "profile1");

    private readonly PumpedSettleClock _clock = new();

    public RecorderPacingTests()
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
        var sandbox = _root[.._root.IndexOf("Runmobile", StringComparison.Ordinal)];
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
    }

    /// <summary>
    /// A fight's first action, requested the way a click requests it while the map
    /// move into the fight is still settling, closes the move with the state the
    /// action begins from, attaches the observer ahead of the executor, and is
    /// recorded; the recording replays at parity.
    ///
    /// Before this rule the action ran unwatched, the move's after-reading was taken
    /// once the action had finished - a hand short one card, an enemy short six
    /// health - and the recording said nothing.
    /// </summary>
    [GameFact]
    public void AFightActionRequestedBeforeTheMapMoveWasReadAfterClosesTheMoveAndIsRecorded()
    {
        using var recording = Patched();
        var session = StartRun();
        using var driver = new RunDriver(session);
        driver.ImproviseUnrecordedCardSelections();
        driver.EnterFirstRoom();
        Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());
        var capture = RunRecorder.Active!.Capture;

        Apply(driver, capture, ActionVerb.ChooseNeowBlessing, ("option_index", "0"));
        var fight = NextNode(session, MapPointType.Monster);
        var moveSeq = capture.NextSeq;

        // The move, and no pump of the recorder's clock: the fight is ready for the
        // player and the move is still the recorder's to read after
        driver.Apply(Record(moveSeq, ActionVerb.MapMove,
            ("act", N(session.RunState.CurrentActIndex)), ("row", N(fight.row)), ("column", N(fight.col))));
        Assert.True(LiveRun.ReadyForThePlayer(session.RunState), "the fight is not ready for the player");
        Assert.Equal(moveSeq, capture.NextSeq);
        Assert.False(RunRecorder.Settled, "the recorder reads as settled with the move unread");
        var theStateTheMoveLeft = LiveRun.Read().Digest;

        // The first card, requested the way a click requests it
        var player = session.RunState.Players[0];
        var hand = player.PlayerCombatState!.Hand.Cards;
        var index = Enumerable.Range(0, hand.Count).First(i => hand[i].CanPlay(out _, out _) && hand[i].Type == CardType.Attack);
        var card = hand[index];
        var target = card.TargetType == TargetType.AnyEnemy ? FirstAliveEnemy() : null;
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(card, target));

        // The move was read after and written at the request, ahead of the executor
        Assert.Equal(moveSeq + 1, capture.NextSeq);
        Assert.True(RunRecorder.Settled, "the recorder did not attach the observer at the request");
        Pump.Drain();
        _clock.Drain();

        Assert.Empty(capture.Refusals);
        var moved = capture.Trace.Steps[^2];
        var played = capture.Trace.Steps[^1];
        Assert.Equal(nameof(ActionVerb.MapMove), moved.Verb);
        Assert.Equal(nameof(ActionVerb.PlayCard), played.Verb);
        Assert.Equal(card.Id.ToString(), played.Args["card_id"]);
        Assert.Equal(theStateTheMoveLeft, moved.AfterDigest);
        Assert.Equal(moved.AfterDigest, played.BeforeDigest);

        // The rest of the fight through the driver, so the recording carries the
        // fight to its end and its boundary
        PlayToVictory(driver, capture, session);
        if (driver.UnclaimedRewardKinds.Contains("gold", StringComparer.Ordinal))
        {
            Apply(driver, capture, ActionVerb.ClaimReward, ("reward_type", "gold"));
        }
        Assert.Empty(capture.Refusals);

        var manifest = FinishAsAbandoned(RunRecorder.Active!);
        var validation = ManifestValidator.Validate(manifest);
        Assert.True(validation.IsValid, validation.Describe());
        var parity = TraceParity.Compare(capture.Trace, FreshReplay(manifest));
        Assert.True(parity.AtParity, parity.Describe());
    }

    /// <summary>
    /// A decision made once the previous one's work has finished, with the engine
    /// quiet, but before the recorder's next poll read it, closes the previous one
    /// with its own before-reading: the state is the same state, and nothing is lost
    /// or refused.
    /// </summary>
    [GameFact]
    public void ADecisionMadeAfterThePriorFinishedButBeforeItWasReadClosesThePriorAndIsRecorded()
    {
        using var recording = Patched();
        var session = StartRun();
        using var driver = new RunDriver(session);
        driver.ImproviseUnrecordedCardSelections();
        driver.EnterFirstRoom();
        Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());
        var capture = RunRecorder.Active!.Capture;

        // The blessing, and no pump of the recorder's clock: its work is done and the
        // engine quiet, and it is still the recorder's to read after
        var blessingSeq = capture.NextSeq;
        driver.Apply(Record(blessingSeq, ActionVerb.ChooseNeowBlessing, ("option_index", "0")));
        Assert.Equal(blessingSeq, capture.NextSeq);
        Assert.False(RunRecorder.Settled);
        var theStateTheBlessingLeft = LiveRun.Read().Digest;

        var first = NextNode(session);
        Apply(driver, capture, ActionVerb.MapMove,
            ("act", N(session.RunState.CurrentActIndex)), ("row", N(first.row)), ("column", N(first.col)));

        Assert.Empty(capture.Refusals);
        var blessed = Assert.Single(capture.Trace.Steps, step => step.Verb == nameof(ActionVerb.ChooseNeowBlessing));
        var moved = capture.Trace.Steps[^1];
        Assert.Equal(nameof(ActionVerb.MapMove), moved.Verb);
        // The blessing's own after-reading is the state the move began from, whether
        // or not the blessing's screen answer stands between them sharing it
        Assert.Equal(theStateTheBlessingLeft, blessed.AfterDigest);
        Assert.Equal(theStateTheBlessingLeft, moved.BeforeDigest);

        PlayToVictory(driver, capture, session);
        if (driver.UnclaimedRewardKinds.Contains("gold", StringComparer.Ordinal))
        {
            Apply(driver, capture, ActionVerb.ClaimReward, ("reward_type", "gold"));
        }
        Assert.Empty(capture.Refusals);

        var manifest = FinishAsAbandoned(RunRecorder.Active!);
        var validation = ManifestValidator.Validate(manifest);
        Assert.True(validation.IsValid, validation.Describe());
        var parity = TraceParity.Compare(capture.Trace, FreshReplay(manifest));
        Assert.True(parity.AtParity, parity.Describe());
    }

    /// <summary>
    /// A decision made while the previous one's work is still in flight is refused,
    /// naming both, rather than recorded: the previous decision's after-reading would
    /// carry this one's effects and this one's before-reading is of a run partway
    /// through a decision.
    ///
    /// Stood in with the retail client's hit animation: Brain Leech's RIP awaits it
    /// before rolling its reward, so the option's work is in flight for as long as the
    /// test holds the animation, with the queue idle - which is exactly the state the
    /// old recorder would have read the next decision in.
    /// </summary>
    [GameFact]
    public void ADecisionMadeWhileThePriorsWorkIsInFlightIsRefusedByName()
    {
        using var recording = Patched();
        var session = StartRun(BrainLeechSeed);
        using var driver = new RunDriver(session);
        driver.ImproviseUnrecordedCardSelections();
        driver.EnterFirstRoom();
        Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());
        var capture = RunRecorder.Active!.Capture;

        Apply(driver, capture, ActionVerb.ChooseNeowBlessing, ("option_index", "0"));
        var fight = NextNode(session, MapPointType.Monster, leadingTo: MapPointType.Unknown);
        Apply(driver, capture, ActionVerb.MapMove,
            ("act", N(session.RunState.CurrentActIndex)), ("row", N(fight.row)), ("column", N(fight.col)));
        PlayToVictory(driver, capture, session);
        if (driver.UnclaimedRewardKinds.Contains("gold", StringComparer.Ordinal))
        {
            Apply(driver, capture, ActionVerb.ClaimReward, ("reward_type", "gold"));
        }
        if (driver.UnclaimedRewardKinds.Count > 0) Apply(driver, capture, ActionVerb.SkipRewards);
        var unknown = NextNode(session, MapPointType.Unknown);
        Apply(driver, capture, ActionVerb.MapMove,
            ("act", N(session.RunState.CurrentActIndex)), ("row", N(unknown.row)), ("column", N(unknown.col)));
        Assert.IsType<EventRoom>(session.RunState.CurrentRoom);
        var model = RunManager.Instance.EventSynchronizer!.GetLocalEvent();
        Assert.Equal("EVENT.BRAIN_LEECH", model.Id.ToString());
        var rip = model.CurrentOptions.ToList().FindIndex(option => RunDriver.OptionKey(option) == "BRAIN_LEECH.pages.INITIAL.options.RIP");
        Assert.True(rip >= 0, "Brain Leech offers no RIP option on this build");

        var animation = new TaskCompletionSource();
        HitAnimation.Pending = animation;
        var seq = capture.NextSeq;
        RunManager.Instance.EventSynchronizer!.ChooseLocalOption(rip);
        Pump.Drain();
        for (var frame = 0; frame < 2; frame++) _clock.Tick();
        Assert.Equal(seq, capture.NextSeq);
        Assert.False(RunRecorder.Settled);

        // The next decision, announced the way its own patch announces it, with the
        // option's work still held on the animation
        RunRecorder.Announce(ActionVerb.UsePotion, new Dictionary<string, string> { ["potion_id"] = "POTION.NONE", ["slot_index"] = "0" });

        animation.SetResult();
        HitAnimation.Pending = null;
        Pump.Drain();
        _clock.Drain();

        var refusal = Assert.Single(capture.Refusals);
        Assert.Contains("A UsePotion was made while the ChooseEventOption before it was still being carried out", refusal.Reason, StringComparison.Ordinal);
        Assert.Equal(NativeSource.BrokenContinuity, capture.Continuity);
        Assert.Equal(seq + 1, capture.NextSeq);
        Assert.Equal(nameof(ActionVerb.ChooseEventOption), capture.Trace.Steps[^1].Verb);
    }

    // ── The run ─────────────────────────────────────────────────────────────────

    private static GameSession StartRun(string seed = Seed)
    {
        if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
        var session = new GameSession();
        session.StartRun(seed, Character, 0, "standard", Acts);
        return session;
    }

    private void Apply(RunDriver driver, RunCapture capture, ActionVerb verb, params (string Key, string Value)[] args)
    {
        driver.Apply(Record(capture.NextSeq, verb, args));
        _clock.Drain();
    }

    private void PlayToVictory(RunDriver driver, RunCapture capture, GameSession session)
    {
        for (var turn = 0; turn < 40 && Field(session, "combat.outcome") == "in_progress"; turn++)
        {
            while (Field(session, "combat.outcome") == "in_progress")
            {
                var hand = session.RunState.Players[0].PlayerCombatState!.Hand.Cards;
                var playable = Enumerable.Range(0, hand.Count).Where(i => hand[i].CanPlay(out _, out _)).ToList();
                var index = playable.FirstOrDefault(i => hand[i].Type == CardType.Attack, playable.Count > 0 ? playable[0] : -1);
                if (index < 0) break;
                var card = hand[index];
                var alive = CombatManager.Instance!.DebugOnlyGetState()!.Enemies.Count(enemy => enemy is { IsAlive: true });
                Apply(driver, capture, ActionVerb.PlayCard,
                [
                    ("card_id", card.Id.ToString()),
                    ("hand_index", N(index)),
                    .. card.TargetType == TargetType.AnyEnemy && alive > 1 ? new[] { ("target_index", "0") } : [],
                ]);
            }

            if (Field(session, "combat.outcome") != "in_progress") break;
            Apply(driver, capture, ActionVerb.EndTurn);
        }

        Assert.Equal("victory", Field(session, "combat.outcome"));
    }

    private static Creature? FirstAliveEnemy() =>
        CombatManager.Instance!.DebugOnlyGetState()!.Enemies.FirstOrDefault(enemy => enemy is { IsAlive: true, IsHittable: true });

    private static MapCoord NextNode(GameSession session, MapPointType? type = null, MapPointType? leadingTo = null)
    {
        var coord = session.RunState.CurrentMapCoord!.Value;
        var current = session.RunState.Map!.GetPoint(coord.col, coord.row)!;
        var next = current.Children
            .Where(child => child.PointType != MapPointType.Unassigned)
            .Where(child => type is null || child.PointType == type)
            .Where(child => leadingTo is null || child.Children.Any(grandchild => grandchild.PointType == leadingTo))
            .OrderBy(child => child.coord.col)
            .First();
        return new MapCoord(next.coord.col, next.coord.row);
    }

    private ReplayManifest FinishAsAbandoned(RunRecorder recorder)
    {
        var runId = recorder.Capture.RunId;
        typeof(RunManager).GetProperty("IsAbandoned")!.SetValue(RunManager.Instance, true);
        RunRecorder.RunEnded(isVictory: false);
        return ManifestJson.Deserialize(File.ReadAllText(Path.Combine(_root, "recordings", $"{runId}.replay.json")));
    }

    private static ReplayTrace FreshReplay(ReplayManifest manifest)
    {
        if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
        var session = new GameSession();
        session.StartRun(
            manifest.Environment.Seed.Value, manifest.Environment.Character.Value,
            manifest.Environment.Ascension.Value, manifest.Environment.GameMode.Value,
            manifest.Environment.Acts.Value, RecordedFightEntry.SuppliedProgressFor(manifest));
        try
        {
            var identity = Preflight.EvaluateStartedRun(manifest.Environment);
            Assert.True(identity.Matches, "the started run is not the run the recording describes");
            var outcome = Engine.Arbiter.ReplayStartedRun(session, manifest, identity, stopAfterSeq: null, gameModeOverride: null);
            Assert.True(
                outcome.Report.Status == VerificationStatus.Verified,
                $"the replay was {outcome.Report.Status}: {string.Join("; ", outcome.Report.Diagnostics)}");
            return outcome.Report.Trace!;
        }
        finally
        {
            if (RunManager.Instance is { IsInProgress: true } manager) manager.CleanUp();
        }
    }

    // ── The retail hit animation, stood in ───────────────────────────────────────

    private static class HitAnimation
    {
        internal static TaskCompletionSource? Pending;

        internal static void Install(Harmony harmony) =>
            harmony.Patch(
                AccessTools.Method(typeof(CreatureCmd), nameof(CreatureCmd.TriggerAnim)),
                prefix: new HarmonyMethod(typeof(HitAnimation), nameof(Before)));

        internal static bool Before(ref Task __result)
        {
            if (Pending is not { } pending) return true;
            __result = pending.Task;
            return false;
        }
    }

    // ── Patch installation and process seams ─────────────────────────────────────

    private static bool HeadlessIdentity() => EngineHost.Origin == EngineOrigin.HeadlessHost;

    private Patches Patched()
    {
        RunRecorder.GameIdentitySource = HeadlessIdentity;
        RunRecorder.Clock = _clock;
        return new Patches();
    }

    private sealed class Patches : IDisposable
    {
        private readonly Harmony _harmony = new($"recorder-pacing.{Guid.NewGuid():N}");
        private readonly Action<CardPrompts.Prompt, IReadOnlyList<CardModel>>? _previousAnswered = CardPrompts.Answered;
        private readonly Action<int?>? _previousReward = CardScreensUp.RewardAnswered;

        internal Patches()
        {
            foreach (var type in CardScreensUp.PatchClasses) _harmony.CreateClassProcessor(type).Patch();
            foreach (var type in CardPrompts.PatchClasses) _harmony.CreateClassProcessor(type).Patch();
            foreach (var type in RunRecorder.PatchClasses) _harmony.CreateClassProcessor(type).Patch();
            _harmony.CreateClassProcessor(typeof(HeadlessCardRewardScreen)).Patch();
            HitAnimation.Install(_harmony);
            RunRecorder.ReadTheAnswers();
        }

        public void Dispose()
        {
            HitAnimation.Pending = null;
            if (RunManager.Instance is { IsInProgress: true } manager) manager.CleanUp();
            RunRecorder.RunTornDown();
            _harmony.UnpatchAll(_harmony.Id);
            CardPrompts.Answered = _previousAnswered;
            CardScreensUp.RewardAnswered = _previousReward;
            CardPrompts.Forget();
        }
    }

    // ── Small readers ────────────────────────────────────────────────────────────

    private static string Field(GameSession session, string field) =>
        CanonicalStateProjection.Project(session.RunState).Fields[field];

    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static ActionRecord Record(int seq, ActionVerb verb, params (string Key, string Value)[] args) => new()
    {
        Seq = seq,
        Verb = verb,
        Args = new SortedDictionary<string, string>(
            args.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal),
        Source = FactSource.Declared,
    };
}

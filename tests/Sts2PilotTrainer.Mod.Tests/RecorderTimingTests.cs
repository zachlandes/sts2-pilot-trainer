using System.Globalization;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The two recorder timing defects the release bar's first measurement found on
/// v0.111.0, reproduced headlessly and held to the parity oracle.
///
/// Both are readings taken at the wrong instant relative to the engine's own work,
/// and both showed up only in the retail client, where the engine's work spans real
/// time: an event option's card reward is rolled after the player creature's hit
/// animation, which the headless engine has none of; and the loot screen's leftovers
/// are declined by the map move that leaves the room, which the headless driver never
/// does because it declines them as a decision of its own. Each test stands the
/// retail condition in headlessly - a hit animation that takes frames, a move made
/// the way a clicked node makes it - and asks the question the standard asks: does
/// the reading the recorder wrote name the state a fresh replay reaches.
/// </summary>
public sealed class RecorderTimingTests : IDisposable
{
    /// <summary>The whole-act fixture's seed and character: a starting deck that
    /// opens no prompt of its own, so a fight to the loot screen is cards played and
    /// turns ended and nothing else.</summary>
    private const string Seed = "67L571H38L";
    private const string Character = "CHARACTER.IRONCLAD";
    private static readonly string[] Acts = ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"];

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"recorder-timing-{Guid.NewGuid():N}", "Runmobile", "steam", "test", "profile1");

    private readonly PumpedSettleClock _clock = new();

    public RecorderTimingTests()
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
    /// An event option that rolls its reward after an animation is read once the
    /// reward is on offer, not during the animation.
    ///
    /// Brain Leech's RIP loses the health, awaits the player creature's hit animation
    /// and only then rolls the card reward it offers. The retail client's action queue
    /// is idle while the animation plays, so a recorder that settled on the queue
    /// alone read the option there - the health gone, the reward not yet rolled - and
    /// the ascension-6 store run diverged at that decision with every sampled field
    /// agreeing and the random streams apart. The animation is stood in for here by a
    /// task the test completes after frames have gone by; the recorder has to leave the
    /// decision unread through them and read it once the set is on offer, which is the
    /// state the replay's own drain reaches.
    /// </summary>
    [GameFact]
    public void AnEventOptionThatRollsItsRewardAfterAnAnimationIsReadOnceTheRewardIsOnOffer()
    {
        using var recording = Patched();
        var session = StartRun();
        using var driver = new RunDriver(session);
        driver.EnterFirstRoom();
        Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());
        var capture = RunRecorder.Active!.Capture;

        EnterEvent(ModelDb.Event<BrainLeech>());
        var options = RunManager.Instance.EventSynchronizer!.GetLocalEvent().CurrentOptions;
        var rip = options.ToList().FindIndex(option => RunDriver.OptionKey(option) == "BRAIN_LEECH.pages.INITIAL.options.RIP");
        Assert.True(rip >= 0, "Brain Leech offers no RIP option on this build");
        var healthBefore = Field(session, "player.hp");

        var animation = new TaskCompletionSource();
        HitAnimation.Pending = animation;
        var seq = capture.NextSeq;
        driver.Apply(Record(seq, ActionVerb.ChooseEventOption,
            ("event_id", "EVENT.BRAIN_LEECH"), ("option_index", N(rip)), ("option_key", RunDriver.OptionKey(options[rip]))));

        // The health is gone and the reward is not rolled: the retail client's gap
        Assert.NotEqual(healthBefore, Field(session, "player.hp"));
        Assert.Null(driver.OpenCardReward);
        var duringTheAnimation = LiveRun.Read().Digest;

        // Frames go by with the queue idle. The recorder must not read here.
        for (var frame = 0; frame < 4; frame++) _clock.Tick();
        Assert.Equal(seq, capture.NextSeq);

        animation.SetResult();
        HitAnimation.Pending = null;
        Pump.Drain();
        Assert.NotNull(driver.OpenCardReward);
        var onOffer = LiveRun.Read().Digest;
        Assert.NotEqual(duringTheAnimation, onOffer);

        _clock.Drain();
        Assert.Equal(seq + 1, capture.NextSeq);
        Assert.Empty(capture.Refusals);
        var step = capture.Trace.Steps[^1];
        Assert.Equal(nameof(ActionVerb.ChooseEventOption), step.Verb);
        Assert.Equal(onOffer, step.AfterDigest);
    }

    /// <summary>
    /// A loot screen's leftovers declined by the map move that leaves the room are
    /// read from the state the move began from.
    ///
    /// In the retail client a terminal loot screen is walked away from, not skipped:
    /// the node the player clicks enters the map coordinate, and <c>BeforeLeavingRoom</c>
    /// declines what was left on the screen from inside that move, after the move has
    /// already advanced the act floor and the coordinate to the node being walked to.
    /// The recorder read the skip at that funnel and wrote the next node's floor and
    /// coordinate under the room being left, a state no replay holds: the driver
    /// declines the set as its own decision before the move. So the move is made here
    /// the way the clicked node makes it, the engine's own <c>EnterMapCoord</c> with
    /// the set still on offer, and the recording is held to a fresh replay through the
    /// parity oracle, which is where the store journals of this build fail.
    /// </summary>
    [GameFact]
    public void ASetTheMapMoveDeclinesIsReadFromTheStateTheMoveBeganFrom()
    {
        using var recording = Patched();
        var session = StartRun();
        using var driver = new RunDriver(session);
        driver.ImproviseUnrecordedCardSelections();
        driver.EnterFirstRoom();
        Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());
        var capture = RunRecorder.Active!.Capture;

        Apply(driver, capture, ActionVerb.ChooseNeowBlessing, ("option_index", "0"));
        var first = NextNode(session);
        Apply(driver, capture, ActionVerb.MapMove,
            ("act", N(session.RunState.CurrentActIndex)), ("row", N(first.row)), ("column", N(first.col)));
        Assert.Equal("true", Field(session, "combat.in_progress"));

        PlayToVictory(driver, capture, session);
        if (driver.UnclaimedRewardKinds.Contains("gold", StringComparer.Ordinal))
        {
            Apply(driver, capture, ActionVerb.ClaimReward, ("reward_type", "gold"));
        }

        Assert.True(driver.UnclaimedRewardKinds.Count > 0, "the loot screen has nothing left to walk away from");
        var claimed = capture.Trace.Steps[^1];
        var floorBeforeTheMove = (
            Field(session, "run.act_floor"), Field(session, "run.map_coord"), Field(session, "run.total_floor"));

        // The clicked node: the engine's own entry, with the set still on offer
        var next = NextNode(session);
        var move = RunManager.Instance.EnterMapCoord(next);
        Pump.Drain();
        Assert.True(move.IsCompleted, "the map move did not finish headlessly");
        _clock.Drain();

        // The standard's own question first: the recording replays decision for
        // decision, which is where the store journals of this build fail
        var manifest = FinishAsAbandoned(RunRecorder.Active!);
        Assert.Empty(capture.Refusals);
        var validation = ManifestValidator.Validate(manifest);
        Assert.True(validation.IsValid, validation.Describe());
        var parity = TraceParity.Compare(capture.Trace, FreshReplay(manifest));
        Assert.True(parity.AtParity, parity.Describe());
        Assert.Empty(parity.OpeningDifferences);

        // And the shape of it: the skip stands on the state the move began from,
        // either side, and the move begins from the same state
        var skip = capture.Trace.Steps[^2];
        var moved = capture.Trace.Steps[^1];
        Assert.Equal(nameof(ActionVerb.SkipRewards), skip.Verb);
        Assert.Equal(nameof(ActionVerb.MapMove), moved.Verb);
        Assert.Equal(floorBeforeTheMove, Floor(skip.Before));
        Assert.Equal(floorBeforeTheMove, Floor(skip.After));
        Assert.Equal(floorBeforeTheMove, Floor(moved.Before));
        Assert.Equal(claimed.AfterDigest, skip.BeforeDigest);
        Assert.Equal(skip.BeforeDigest, skip.AfterDigest);
        Assert.Equal(skip.AfterDigest, moved.BeforeDigest);
    }

    // ── The run ─────────────────────────────────────────────────────────────────

    private static GameSession StartRun()
    {
        if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
        var session = new GameSession();
        session.StartRun(Seed, Character, 0, "standard", Acts);
        return session;
    }

    /// <summary>Enters the given event's room from wherever the run stands, through
    /// the game's own debug entry, so a test can put a chosen event in front of the
    /// recorder without a seed whose map rolls it. The event is a parameter rather
    /// than a type argument: a generic constraint naming a game type is read at test
    /// discovery, before the game assembly can be resolved, and takes the whole test
    /// assembly down with it.</summary>
    private static void EnterEvent(EventModel model)
    {
        var entered = RunManager.Instance.EnterRoomDebug(RoomType.Event, MapPointType.Unknown, model, showTransition: false);
        Pump.Drain();
        Assert.True(entered.IsCompleted, "the event room did not open headlessly");
        Assert.IsType<EventRoom>(RunManager.Instance.DebugOnlyGetState()!.CurrentRoom);
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

    /// <summary>The first node the run's node leads to.</summary>
    private static MapCoord NextNode(GameSession session)
    {
        var coord = session.RunState.CurrentMapCoord!.Value;
        var current = session.RunState.Map!.GetPoint(coord.col, coord.row)!;
        var next = current.Children.Where(child => child.PointType != MapPointType.Unassigned).OrderBy(child => child.coord.col).First();
        return new MapCoord(next.coord.col, next.coord.row);
    }

    /// <summary>Abandons the run: an honest, complete recording. The engine's own
    /// abandon path is a Godot wait this process cannot run, so its flag is set the
    /// way <c>Abandon</c> sets it and the recording is finished the way the
    /// <c>OnEnded</c> patch finishes it.</summary>
    private ReplayManifest FinishAsAbandoned(RunRecorder recorder)
    {
        var runId = recorder.Capture.RunId;
        typeof(RunManager).GetProperty("IsAbandoned")!.SetValue(RunManager.Instance, true);
        RunRecorder.RunEnded(isVictory: false);
        return ManifestJson.Deserialize(File.ReadAllText(Path.Combine(_root, "recordings", $"{runId}.replay.json")));
    }

    /// <summary>A fresh replay of the recording's own actions from its seed, in this
    /// process and past the retail preflight a headless recording's patch roster
    /// rightly fails: <see cref="Engine.Arbiter.ReplayStartedRun"/>, the loop the CLI's
    /// <c>parity</c> runs.</summary>
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

    /// <summary>
    /// The player creature's hit animation, as the retail client has it: a task that
    /// completes frames later. Headlessly there is no creature node and the engine's
    /// own <c>TriggerAnim</c> completes at once, which is exactly what hid the reading
    /// defect from every headless capture; while a stand-in is pending the animation
    /// is the test's to finish. Patched by hand rather than by attribute, because an
    /// attribute naming a game type is read at test discovery, before the game
    /// assembly can be resolved, and takes the whole test assembly down with it.
    /// </summary>
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
        private readonly Harmony _harmony = new($"recorder-timing.{Guid.NewGuid():N}");
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

    private static (string ActFloor, string MapCoord, string TotalFloor) Floor(IReadOnlyDictionary<string, string> sample) =>
        (sample["run.act_floor"], sample["run.map_coord"], sample["run.total_floor"]);

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

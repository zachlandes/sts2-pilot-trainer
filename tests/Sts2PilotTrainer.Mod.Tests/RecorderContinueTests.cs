using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// An honest Continue keeps the recording, caught headlessly.
///
/// The captain's report: take the opening blessing, leave to the main menu, press
/// Continue, and the version overlay reads RECORDING STOPPED in Neow's room. The
/// journal's last digest and the run the game came back in differed in exactly one
/// field, <c>run.act_floor</c>, read as 1 after the blessing and 0 at the resume.
///
/// The game does not save the act floor: <c>RunManager.EnterMapPointInternal</c> is
/// the one thing that sets it, and a continued run is at act floor 0 from
/// <c>SetUpSavedSingleplayer</c> until <c>LoadIntoLatestMapCoord</c> re-enters the
/// coordinate the save names. The recorder attached as soon as the run had a floor
/// count - a continued run has one on the save before it has a room - so it read the
/// run one step before the game had finished continuing it, and
/// <see cref="RunCapture.Resume"/> was handed a state the journal had never seen. The
/// resume digest was right to refuse it; the reading was taken at the wrong moment.
///
/// A Continue into a live fight has the same gap one step later. The game's save at a
/// floor arrival carries no room, so continuing it re-enters the coordinate and
/// generates the fight afresh: the room is pushed, its assets load over the frames
/// after, and only then is the combat set up. A reading between the push and the
/// combat is not in combat, so it is neither the journal's room-entry state nor any
/// other, and the captain's own mid-fight Continues resumed broken with "this game is
/// not in one the recorder can watch" - a fight opening under the recorder's nose.
///
/// The retail continue sequence is driven here the way <see cref="GameSession.RestoreSavedRun"/>
/// drives it, from the saves the game itself took, with the recorder's readiness asked
/// at the moment the retail attach used to read and again once the run is where the
/// game's own Continue leaves it. Headlessly nothing yields inside those gaps, so each
/// is observed from a prefix on the game member that ends it.
///
/// Two more Continues break for a different reason, one the timing above cannot
/// reach: the state itself. A finished fight stays on the player until the next fight
/// and the projection keeps emitting it, so every reading taken at a shop, a rest
/// site, an event or a loot screen after a won fight carries that fight's residue,
/// and the game's save carries none of it - <c>SerializableRun</c> has no combat
/// member. Continued from that save, the run reads as a moment the journal never saw
/// in exactly the residue fields and nothing else. The last two tests hold the resume
/// to comparing what a save can carry once the complete digests have disagreed, from
/// the game's own saves, with the Ironclad on the whole-act seed because its first
/// two fights are won on attacks alone; the one card screen on the way, the opening
/// blessing's, is answered by the driver's own improvisation.
/// </summary>
public sealed class RecorderContinueTests : IDisposable
{
    private const string Seed = "P1L0TTRA1NER";
    private static readonly string[] Acts = ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"];

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"recorder-continue-{Guid.NewGuid():N}", "Runmobile", "steam", "test", "profile1");

    public RecorderContinueTests()
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

    [GameFact]
    public void AnHonestContinueAtNeowsRoomKeepsTheRecordingContinuous()
    {
        using var recording = Patched();
        RunRecorder.GameIdentitySource = HeadlessIdentity;
        RunRecorder.Clock = new PumpedSettleClock();

        // The run, recorded through its blessing, and the save the game itself took
        // when Neow's event finished - the save Continue reads.
        InterceptedRunSave? save = null;
        string lastDigest;
        string runId;
        using (RunSaveInterception.Collect(taken => save = taken))
        {
            var session = StartRun();
            using var driver = new RunDriver(session);
            driver.EnterFirstRoom();
            Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());

            driver.Apply(Record(0, ActionVerb.ChooseNeowBlessing, ("option_index", "0")));
            DrainSettles();

            var capture = RunRecorder.Active!.Capture;
            Assert.Equal(1, capture.NextSeq);
            Assert.Equal("1", capture.Trace.Steps[^1].After["run.act_floor"]);
            runId = capture.RunId;
            lastDigest = capture.LastDigest;

            // Leaving to the main menu: the game tears the run down and the recorder
            // detaches, keeping the journal.
            RunManager.Instance.CleanUp();
        }

        Assert.Null(RunRecorder.Active);
        Assert.NotNull(save);
        Assert.Equal("Event", save.PreFinishedRoom);
        Assert.True(File.Exists(Path.Combine(_root, "recordings", $"{runId}{RunJournal.FileExtension}")));

        // Continue. Between the run existing and the game re-entering its room the
        // act floor reads 0, which is the state the recorder used to read.
        using (BeforeTheRoomIsEntered(() =>
               {
                   Assert.Equal("0", LiveRun.Sample()["run.act_floor"]);
                   Assert.NotEqual(lastDigest, LiveRun.Read().Digest);
                   Assert.False(RunRecorder.HasEnteredItsRoom(), "the recorder was ready to read a run the game had not finished continuing");
               }))
        {
            var continued = new GameSession();
            continued.RestoreSavedRun(save.Json);
            using var driver = new RunDriver(continued);

            Assert.True(RunRecorder.HasEnteredItsRoom());
            Assert.Equal(lastDigest, LiveRun.Read().Digest);
            Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());

            var resumed = RunRecorder.Active!.Capture;
            Assert.Equal(runId, resumed.RunId);
            Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
            Assert.Empty(resumed.Refusals);
            Assert.Null(resumed.Stop);
            Assert.Equal(1, resumed.NextSeq);

            // And the run goes on being recorded from where the journal left off.
            var edge = FirstEdge(continued);
            driver.Apply(Record(1, ActionVerb.MapMove,
                ("act", N(continued.RunState.CurrentActIndex)),
                ("row", N(edge.ToRow)), ("column", N(edge.ToColumn))));
            DrainSettles();

            Assert.Equal(2, resumed.NextSeq);
            Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
            Assert.Empty(resumed.Refusals);
            Assert.Equal("true", Field(continued, "combat.in_progress"));
        }
    }

    [GameFact]
    public void AnHonestContinueIntoALiveFightResumesAtTheFightsEntryAndKeepsWatching()
    {
        using var recording = Patched();
        RunRecorder.GameIdentitySource = HeadlessIdentity;
        RunRecorder.Clock = new PumpedSettleClock();

        // The run, recorded into its first fight and one card past its entry, and the
        // save the game itself took at the floor arrival - before the room was rolled,
        // which is why continuing it opens the fight afresh.
        var saves = new List<InterceptedRunSave>();
        string entryDigest;
        string runId;
        using (RunSaveInterception.Collect(saves.Add))
        {
            var session = StartRun();
            using var driver = new RunDriver(session);
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

            var capture = RunRecorder.Active!.Capture;
            entryDigest = capture.LastDigest;
            runId = capture.RunId;

            PlayFirstPlayable(session);
            Assert.Equal(3, capture.NextSeq);
            Assert.Equal(ActionVerb.PlayCard, capture.Actions[^1].Verb);

            RunManager.Instance.CleanUp();
        }

        Assert.Null(RunRecorder.Active);
        var arrival = Assert.Single(saves, taken => taken.IsFloorEntry && taken.ActFloor == 2);

        // Continue. The room is pushed before its fight is set up, and at that moment
        // the run is in no combat at all.
        using (BeforeTheFightIsSetUp(() =>
               {
                   Assert.IsType<CombatRoom>(LiveRun.State!.CurrentRoom);
                   Assert.False(LiveRun.InCombat);
                   Assert.NotEqual(entryDigest, LiveRun.Read().Digest);
                   Assert.False(RunRecorder.HasEnteredItsRoom(), "the recorder was ready to read a fight the game had not opened");
               }))
        {
            var continued = new GameSession();
            continued.RestoreSavedRun(arrival.Json);
            Pump.Drain();
            using var driver = new RunDriver(continued);

            Assert.True(RunRecorder.HasEnteredItsRoom());
            Assert.Equal(entryDigest, LiveRun.Read().Digest);
            Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());

            // The game's own rollback of the live fight to its entry: the card played
            // before the quit is a discarded branch, the watch is continuous, and it
            // resumes at the fight's next decision.
            var resumed = RunRecorder.Active!.Capture;
            Assert.Equal(runId, resumed.RunId);
            Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
            Assert.Empty(resumed.Refusals);
            Assert.Equal(2, resumed.NextSeq);
            var discarded = Assert.Single(resumed.Discarded);
            Assert.Equal(1, discarded.RollbackToSeq);
            Assert.Contains(discarded.Actions, action => action.Verb == ActionVerb.PlayCard);

            PlayFirstPlayable(continued);
            Assert.Equal(3, resumed.NextSeq);
            Assert.Equal(ActionVerb.PlayCard, resumed.Actions[^1].Verb);
            Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
            Assert.Empty(resumed.Refusals);
        }
    }

    /// <summary>
    /// Arrive at a shop straight after a won fight, quit, continue: the recorder's
    /// reading at the arrival carries the finished fight and the restored run does
    /// not, and nothing else differs, so nothing happened that the recorder missed.
    /// </summary>
    [GameFact]
    public void AnHonestContinueAtAShopArrivalAfterAWonFightResumesContinuously()
    {
        using var recording = Patched();
        RunRecorder.GameIdentitySource = HeadlessIdentity;
        RunRecorder.Clock = new PumpedSettleClock();

        var saves = new List<InterceptedRunSave>();
        string lastDigest;
        IReadOnlyDictionary<string, string> lastReading;
        string runId;
        int seq;
        using (RunSaveInterception.Collect(saves.Add))
        {
            var session = StartIronclad();
            using var driver = new RunDriver(session);
            driver.ImproviseUnrecordedCardSelections();
            driver.EnterFirstRoom();
            Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());
            var actions = new List<ActionRecord>();

            Apply(driver, actions, ActionVerb.ChooseNeowBlessing, ("option_index", "0"));
            DrainSettles();
            foreach (var node in RouteTo(session, MapPointType.Shop))
            {
                Move(driver, actions, session, node);
                if (node.PointType == MapPointType.Shop) break;
                Assert.Equal(MapPointType.Monster, node.PointType);
                PlayToVictory(driver, actions, session);
                TakeGoldAndSkipTheRest(driver, actions);
            }

            Assert.Equal(RoomType.Shop, session.RunState.CurrentRoom!.RoomType);

            var capture = RunRecorder.Active!.Capture;
            Assert.Empty(capture.Refusals);
            lastDigest = capture.LastDigest;
            lastReading = capture.Trace.Steps[^1].After;
            Assert.True(ReplayTrace.CarriesFinishedCombat(lastReading), "the arrival reading carries no finished fight");
            runId = capture.RunId;
            seq = capture.NextSeq;

            RunManager.Instance.CleanUp();
        }

        Assert.Null(RunRecorder.Active);
        var arrival = saves.Last(taken => taken.IsFloorEntry);

        var continued = new GameSession();
        using var continuedDriver = new RunDriver(continued);
        continuedDriver.ImproviseUnrecordedCardSelections();
        continued.RestoreSavedRun(arrival.Json);
        Pump.Drain();

        Assert.True(RunRecorder.HasEnteredItsRoom());
        var (live, liveDigest) = LiveRun.Read();
        Assert.NotEqual(lastDigest, liveDigest);
        Assert.False(ReplayTrace.CarriesFinishedCombat(live));
        Assert.All(ReplayTrace.Differences(lastReading, live),
            difference => Assert.StartsWith("combat.", difference, StringComparison.Ordinal));

        Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());
        var resumed = RunRecorder.Active!.Capture;
        Assert.Equal(runId, resumed.RunId);
        Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
        Assert.Empty(resumed.Refusals);
        Assert.Empty(resumed.Discarded);
        Assert.Equal(seq, resumed.NextSeq);
    }

    /// <summary>
    /// Claim the gold on the loot screen, quit, continue: the game restores its
    /// fight-won save, so the loot screen comes back with the gold on offer again and
    /// the run carries no combat state. Under the fight-only rollback rule the match
    /// is the killing play, a finished fight and not a room entry, so the resume is a
    /// rewind that keeps the claim as a discarded branch: whole, playable, not a hole.
    /// </summary>
    [GameFact]
    public void AnHonestContinueOnTheLootScreenIsARewindNotAHole()
    {
        using var recording = Patched();
        RunRecorder.GameIdentitySource = HeadlessIdentity;
        RunRecorder.Clock = new PumpedSettleClock();

        var saves = new List<InterceptedRunSave>();
        string runId;
        int killingPlay;
        using (RunSaveInterception.Collect(saves.Add))
        {
            var session = StartIronclad();
            using var driver = new RunDriver(session);
            driver.ImproviseUnrecordedCardSelections();
            driver.EnterFirstRoom();
            Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());
            var actions = new List<ActionRecord>();

            Apply(driver, actions, ActionVerb.ChooseNeowBlessing, ("option_index", "0"));
            DrainSettles();
            MoveTo(driver, actions, session, MapPointType.Monster);
            PlayToVictory(driver, actions, session);
            var capture = RunRecorder.Active!.Capture;
            killingPlay = capture.NextSeq - 1;
            Apply(driver, actions, ActionVerb.ClaimReward, ("reward_type", "gold"));
            DrainSettles();
            Assert.Equal(ActionVerb.ClaimReward, capture.Actions[^1].Verb);
            runId = capture.RunId;

            RunManager.Instance.CleanUp();
        }

        Assert.Null(RunRecorder.Active);
        var won = saves.Last();
        Assert.Equal("Monster", won.PreFinishedRoom);

        // The driver first, so the rewards the restored room re-offers are parked the
        // way the client's own loot screen holds them.
        var continued = new GameSession();
        using var continuedDriver = new RunDriver(continued);
        continuedDriver.ImproviseUnrecordedCardSelections();
        continued.RestoreSavedRun(won.Json);
        Pump.Drain();

        Assert.True(RunRecorder.HasEnteredItsRoom());
        Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());
        var resumed = RunRecorder.Active!.Capture;
        Assert.Equal(runId, resumed.RunId);
        Assert.NotEqual(NativeSource.BrokenContinuity, resumed.Continuity);
        Assert.Equal(NativeSource.RewoundContinuity, resumed.Continuity);
        Assert.Equal(RunCaptureState.Recording, resumed.State);
        Assert.Equal(killingPlay + 1, resumed.NextSeq);
        var discarded = Assert.Single(resumed.Discarded);
        Assert.Equal(killingPlay, discarded.RollbackToSeq);
        Assert.True(discarded.Reload);
        Assert.Equal(ActionVerb.ClaimReward, Assert.Single(discarded.Actions).Verb);
    }

    /// <summary>The line the recorder logs beside a refusal names the fields, in
    /// order, and no more than eight of them.</summary>
    [Fact]
    public void TheResumeDifferenceLineIsBounded()
    {
        var last = new RunJournalEntry
        {
            Seq = 7,
            Verb = "MapMove",
            State = Enumerable.Range(0, 12).ToDictionary(i => $"field.{i:d2}", i => "old", StringComparer.Ordinal),
            Digest = "sha256:" + new string('a', 64),
        };
        var live = Enumerable.Range(0, 12).ToDictionary(i => $"field.{i:d2}", i => "new", StringComparer.Ordinal);

        var line = RunRecorder.DescribeResumeDifferences(last, live);

        Assert.StartsWith("the run resumed differs from the journal's reading after decision 7 (MapMove) in 12 field(s): ", line, StringComparison.Ordinal);
        Assert.Contains("field.00: old -> new", line, StringComparison.Ordinal);
        Assert.Contains("field.07: old -> new", line, StringComparison.Ordinal);
        Assert.DoesNotContain("field.08", line, StringComparison.Ordinal);
        Assert.EndsWith("; and 4 more", line, StringComparison.Ordinal);
    }

    // ── The moment between the run and its room ──────────────────────────────────

    /// <summary>Runs <paramref name="observe"/> when the retail continue sequence is
    /// about to re-enter the run's room: the run exists, its save is applied, and
    /// nothing has been entered yet.</summary>
    private static IDisposable BeforeTheRoomIsEntered(Action observe)
    {
        var harmony = new Harmony($"recorder-continue.{Guid.NewGuid():N}");
        Observation.Observe = observe;
        harmony.Patch(
            AccessTools.Method(typeof(RunManager), nameof(RunManager.LoadIntoLatestMapCoord)),
            prefix: new HarmonyMethod(typeof(Observation), nameof(Observation.Before)));
        return new Unpatch(harmony);
    }

    /// <summary>Runs <paramref name="observe"/> when a combat room the run already
    /// stands in is about to have its fight set up.</summary>
    private static IDisposable BeforeTheFightIsSetUp(Action observe)
    {
        var harmony = new Harmony($"recorder-continue.{Guid.NewGuid():N}");
        Observation.Observe = observe;
        harmony.Patch(
            AccessTools.Method(typeof(CombatManager), nameof(CombatManager.SetUpCombat)),
            prefix: new HarmonyMethod(typeof(Observation), nameof(Observation.Before)));
        return new Unpatch(harmony);
    }

    private static class Observation
    {
        internal static Action? Observe;

        internal static void Before() => Observe?.Invoke();
    }

    private sealed class Unpatch(Harmony harmony) : IDisposable
    {
        public void Dispose()
        {
            Observation.Observe = null;
            harmony.UnpatchAll(harmony.Id);
        }
    }

    // ── The run ──────────────────────────────────────────────────────────────────

    private static GameSession StartRun()
    {
        if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
        var session = new GameSession();
        session.StartRun(Seed, "CHARACTER.SILENT", 0, "standard", Acts);
        return session;
    }

    private static void DrainSettles()
    {
        if (RunRecorder.Clock is PumpedSettleClock pumped) pumped.Drain();
    }

    private static void PlayFirstPlayable(GameSession session)
    {
        var player = session.RunState.Players[0];
        var hand = player.PlayerCombatState!.Hand.Cards;
        var index = hand.ToList().FindIndex(card => card.CanPlay(out _, out _));
        Assert.True(index >= 0, "no card in the hand could be played");
        var card = hand[index];
        var target = card.TargetType == TargetType.AnyEnemy
            ? CombatManager.Instance!.DebugOnlyGetState()!.Enemies.First(enemy => enemy is { IsAlive: true })
            : null;
        RunManager.Instance.ActionQueueSet.EnqueueWithoutSynchronizing(new PlayCardAction(card, target));
        Pump.Drain();
        DrainSettles();
    }

    private static bool HeadlessIdentity() => EngineHost.Origin == EngineOrigin.HeadlessHost;

    private static Patches Patched() => new();

    private sealed class Patches : IDisposable
    {
        private readonly Harmony _harmony = new($"recorder-continue-patches.{Guid.NewGuid():N}");
        private readonly Action<CardPrompts.Prompt, IReadOnlyList<CardModel>>? _previousAnswered = CardPrompts.Answered;
        private readonly Action<int?>? _previousReward = CardScreensUp.RewardAnswered;

        internal Patches()
        {
            foreach (var type in CardScreensUp.PatchClasses) _harmony.CreateClassProcessor(type).Patch();
            foreach (var type in CardPrompts.PatchClasses) _harmony.CreateClassProcessor(type).Patch();
            foreach (var type in RunRecorder.PatchClasses) _harmony.CreateClassProcessor(type).Patch();
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

    // ── Small readers ────────────────────────────────────────────────────────────

    private static string Field(GameSession session, string field) =>
        CanonicalStateProjection.Project(session.RunState).Fields[field];

    private static string N(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static MapEdge FirstEdge(GameSession session)
    {
        var current = Field(session, "run.map_coord");
        var separator = current.IndexOf('c');
        var row = int.Parse(current.AsSpan(1, separator - 1), System.Globalization.CultureInfo.InvariantCulture);
        var column = int.Parse(current.AsSpan(separator + 1), System.Globalization.CultureInfo.InvariantCulture);
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

    // ── An Ironclad run that wins on attacks alone ───────────────────────────────

    private const string IroncladSeed = "67L571H38L";

    private static GameSession StartIronclad()
    {
        if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
        var session = new GameSession();
        session.StartRun(IroncladSeed, "CHARACTER.IRONCLAD", 0, "standard", Acts);
        return session;
    }

    private static void Apply(
        RunDriver driver, List<ActionRecord> actions, ActionVerb verb, params (string Key, string Value)[] args)
    {
        var action = Record(actions.Count, verb, args);
        driver.Apply(action);
        actions.Add(action);
        DrainSettles();
    }

    private static MapPoint Current(GameSession session)
    {
        var coord = session.RunState.CurrentMapCoord!.Value;
        return session.RunState.Map!.GetPoint(coord.col, coord.row)!;
    }

    private static void MoveTo(RunDriver driver, List<ActionRecord> actions, GameSession session, MapPointType type) =>
        Move(driver, actions, session,
            Current(session).Children.Where(child => child.PointType == type).OrderBy(child => child.coord.col).First());

    private static void Move(RunDriver driver, List<ActionRecord> actions, GameSession session, MapPoint next) =>
        Apply(driver, actions, ActionVerb.MapMove,
            ("act", N(session.RunState.CurrentActIndex)),
            ("row", N(next.coord.row)), ("column", N(next.coord.col)));

    /// <summary>The shortest route from the run's node to the nearest node of the
    /// given type through ordinary fights only, so every room on the way is one
    /// <see cref="PlayToVictory"/> has a rule for.</summary>
    private static IReadOnlyList<MapPoint> RouteTo(GameSession session, MapPointType type)
    {
        var start = Current(session);
        var queue = new Queue<(MapPoint Node, List<MapPoint> Path)>();
        queue.Enqueue((start, []));
        var seen = new HashSet<MapPoint> { start };
        while (queue.Count > 0)
        {
            var (node, path) = queue.Dequeue();
            foreach (var child in node.Children.OrderBy(child => child.coord.col))
            {
                if (!seen.Add(child)) continue;
                var next = new List<MapPoint>(path) { child };
                if (child.PointType == type) return next;
                if (child.PointType == MapPointType.Monster) queue.Enqueue((child, next));
            }
        }

        throw new InvalidOperationException($"no route to a {type} node through ordinary fights on this seed");
    }

    /// <summary>The first playable attack, else the first playable card, to the end
    /// of the fight: the whole-act fixture's own rule, which wins this seed's first
    /// two fights.</summary>
    private static void PlayToVictory(RunDriver driver, List<ActionRecord> actions, GameSession session)
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
                Apply(driver, actions, ActionVerb.PlayCard,
                [
                    ("card_id", card.Id.ToString()),
                    ("hand_index", N(index)),
                    .. card.TargetType == TargetType.AnyEnemy && alive > 1 ? new[] { ("target_index", "0") } : [],
                ]);
            }

            if (Field(session, "combat.outcome") != "in_progress") break;
            Apply(driver, actions, ActionVerb.EndTurn);
        }

        Assert.Equal("victory", Field(session, "combat.outcome"));
    }

    private static void TakeGoldAndSkipTheRest(RunDriver driver, List<ActionRecord> actions)
    {
        if (driver.UnclaimedRewardKinds.Contains("gold", StringComparer.Ordinal))
        {
            Apply(driver, actions, ActionVerb.ClaimReward, ("reward_type", "gold"));
        }

        if (driver.UnclaimedRewardKinds.Count > 0) Apply(driver, actions, ActionVerb.SkipRewards);
    }
}

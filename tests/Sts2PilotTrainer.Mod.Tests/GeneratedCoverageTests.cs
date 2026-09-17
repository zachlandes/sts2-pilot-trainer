using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// Decision points the committed corpus never reaches, reached by a generated walk
/// through the real recorder, replayed, and held to parity.
///
/// The coverage number excuses a point no committed recording exercises with a
/// sentence a build can be held to; these rows are what a headless host can hold it
/// to. Each row points the whole-act journey at one decision through a
/// <see cref="WalkPolicy"/> - decline a card reward on its own screen, take a named
/// rest option, buy from a named shelf, discard or drink a potion on the map, skip
/// the chest - plays the act through the recorder the way the won-run proof does,
/// replays what the recorder wrote, holds the replay to the journal decision for
/// decision through the same oracle <c>parity</c> uses, and asserts the recording
/// projects to the point the row is for. The recordings are written to a temporary
/// store and not committed, so <c>coverage --corpus manifests</c> still reads those
/// points as excused; the excusal names this test as the thing that reaches them.
///
/// What no row here can reach is what the headless host has no screen for, which
/// <c>docs/headless-fidelity.md</c> names, and the undo of an ended turn, which the
/// retail client offers only in the window before the enemy turn begins and this
/// process runs the enemy turn inside the end-turn decision.
/// </summary>
public sealed class GeneratedCoverageTests : IDisposable
{
    /// <summary>The whole-act fixture's own seed and acts: the one run the journey's
    /// rules are known to carry through every room type of the first act, which is
    /// what a row that needs a shop, a rest site or a chest on its route asks for.
    /// Each walk stops at the end of the floor its ask was met on and the recording is
    /// finished as abandoned, the way the natural-run proof finishes one, because the
    /// journey's survival rules were tuned for its own choices and not the row's.</summary>
    private const string Seed = "67L571H38L";
    private static readonly string[] Acts = ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"];

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"generated-coverage-{Guid.NewGuid():N}", "Runmobile", "steam", "test", "profile1");

    public GeneratedCoverageTests()
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

    public static IEnumerable<object[]> Rows() =>
    [
        ["decline the first card reward", "verb  TakeCardRewardAlternative", "card-reward-alternative  Skip"],
        ["rest HEAL", "rest-option  HEAL", ""],
        ["rest SMITH", "rest-option  SMITH", ""],
        ["shop relic", "shop-kind  relic", ""],
        ["shop potion", "shop-kind  potion", ""],
        ["shop colorless_card", "shop-kind  colorless_card", ""],
        ["skip the chest", "verb  SkipChestRelic", ""],
        ["take the chest", "verb  TakeChestRelic", ""],
        ["discard a potion on the map", "verb  DiscardPotion", ""],
        ["drink a potion on the map", "verb  UsePotion", ""],
    ];

    [GameTheory]
    [MemberData(nameof(Rows))]
    public void AGeneratedWalkReachesThePointRecordsItAndReplaysToParity(string row, string point, string alsoPoint)
    {
        using var recording = Patched();
        RunRecorder.GameIdentitySource = () => EngineHost.Origin == EngineOrigin.HeadlessHost;
        RunRecorder.Clock = new PumpedSettleClock();

        var (manifest, capture) = WalkTheActThroughTheRecorder(PolicyFor(row));

        var native = manifest.Source.Native!;
        Assert.True(
            native.Continuity == NativeSource.ContinuousContinuity && capture.Refusals.Count == 0,
            $"the recording is {native.Continuity}: {string.Join("; ", capture.Refusals.Select(refusal => refusal.Reason))}");
        Assert.True(
            native.Integrity == NativeSource.CompleteIntegrity,
            $"the recording is {native.Integrity}: {(capture.Stop is { } stop ? ManifestValidator.Describe(stop.Decision) : "")}");

        var points = DecisionFacts.Of(manifest).Select(reached => reached.ToString()).ToList();
        Assert.Contains(point, points);
        if (alsoPoint.Length > 0) Assert.Contains(alsoPoint, points);

        var replay = FreshReplay(manifest);
        Assert.True(
            replay.Report.Status == VerificationStatus.Verified,
            $"the replay was {replay.Report.Status}: {string.Join("; ", replay.Report.Diagnostics)}");
        var parity = TraceParity.Compare(capture.Trace, replay.Report.Trace!);
        Assert.True(parity.AtParity, parity.Describe());
        Assert.Null(parity.OpeningHiddenState);
    }

    /// <summary>The rows name every point <c>DecisionExcusals</c> credits to this test.</summary>
    [GameFact]
    public void EveryPointExcusedOntoThisTestHasARow()
    {
        var rows = Rows().SelectMany(row => new[] { (string)row[1], (string)row[2] }).Where(point => point.Length > 0).ToHashSet(StringComparer.Ordinal);
        var credited = DecisionExcusals.All
            .Where(excusal => excusal.Value.Contains(nameof(GeneratedCoverageTests), StringComparison.Ordinal))
            .Select(excusal => excusal.Key.ToString())
            .ToList();

        Assert.NotEmpty(credited);
        Assert.All(credited, point => Assert.Contains(point, rows));
    }

    private static WalkPolicy PolicyFor(string row) => row switch
    {
        "decline the first card reward" => new WalkPolicy { DeclineTheFirstCardReward = true },
        "rest HEAL" => new WalkPolicy { RestOption = "HEAL" },
        "rest SMITH" => new WalkPolicy { RestOption = "SMITH" },
        "shop relic" => new WalkPolicy { ShopKind = ShopPurchaseKinds.Relic },
        "shop potion" => new WalkPolicy { ShopKind = ShopPurchaseKinds.Potion },
        "shop colorless_card" => new WalkPolicy { ShopKind = ShopPurchaseKinds.ColorlessCard },
        "skip the chest" => new WalkPolicy { SkipTheChest = true },
        "take the chest" => new WalkPolicy { TakeTheChest = true },
        "discard a potion on the map" => new WalkPolicy { DiscardAPotionOnTheMap = true },
        "drink a potion on the map" => new WalkPolicy { DrinkAPotionOnTheMap = true },
        _ => throw new ArgumentOutOfRangeException(nameof(row), row, "no such row"),
    };

    // ── The walk, recorded, and its replay ──────────────────────────────────────

    /// <summary>The first act alone on the journey's route through every room type,
    /// through the recorder, ending in the victory room the Architect's PROCEED opens;
    /// the shape of the won-run proof in <c>HeadlessGameplayCaptureTests</c>.</summary>
    private (ReplayManifest Manifest, RunCapture Capture) WalkTheActThroughTheRecorder(WalkPolicy policy)
    {
        if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
        var session = new GameSession();
        session.StartRun(Seed, "CHARACTER.IRONCLAD", 0, "standard", Acts);
        using var driver = new RunDriver(session);
        driver.ImproviseUnrecordedCardSelections();
        driver.EnterFirstRoom();
        Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());

        SyntheticFixtureGenerator.WalkTheAct(
            session, driver, [], DrainSettles, visitEveryRoomType: true, policy with { StopOnceMet = true });

        // Abandoned after the act, the way the natural-run proof abandons: the
        // engine's own abandon path is a Godot wait this process cannot run, so its
        // flag is set the way Abandon sets it and the recording finished the way the
        // OnEnded patch finishes it
        var capture = RunRecorder.Active!.Capture;
        typeof(RunManager).GetProperty("IsAbandoned")!.SetValue(RunManager.Instance, true);
        RunRecorder.RunEnded(isVictory: false);

        Assert.True(
            capture.State == RunCaptureState.Finished,
            $"the recording is {capture.State}: {string.Join("; ", capture.Refusals.Select(refusal => refusal.Reason))}" +
            (capture.Stop is { } stop ? $"; stopped at {ManifestValidator.Describe(stop.Decision)}" : "") +
            $"; actions: {string.Join(" ", capture.Actions.Select(action => $"{action.Seq}:{action.Verb}"))}");
        var manifest = ManifestJson.Deserialize(File.ReadAllText(
            Path.Combine(_root, "recordings", $"{capture.RunId}.replay.json")));
        return (manifest, capture);
    }

    private static ArbiterOutcome FreshReplay(ReplayManifest manifest)
    {
        if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
        var session = new GameSession();
        session.StartRun(
            manifest.Environment.Seed.Value, manifest.Environment.Character.Value, manifest.Environment.Ascension.Value,
            manifest.Environment.GameMode.Value, manifest.Environment.Acts.Value,
            RecordedFightEntry.SuppliedProgressFor(manifest));
        try
        {
            var runIdentity = Preflight.EvaluateStartedRun(manifest.Environment);
            Assert.True(runIdentity.Matches, "the started run is not the run the recording describes");
            return Engine.Arbiter.ReplayStartedRun(session, manifest, runIdentity, stopAfterSeq: null, gameModeOverride: null);
        }
        finally
        {
            if (RunManager.Instance is { IsInProgress: true } manager) manager.CleanUp();
        }
    }

    private static void DrainSettles()
    {
        if (RunRecorder.Clock is PumpedSettleClock pumped) pumped.Drain();
    }

    private static Patches Patched() => new();

    private sealed class Patches : IDisposable
    {
        private readonly Harmony _harmony = new($"generated-coverage.{Guid.NewGuid():N}");
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
}

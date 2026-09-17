using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The whole-act journey played through the real recorder into a temporary store,
/// and a fresh replay of what it wrote: the shape of the won-run proof in
/// <c>HeadlessGameplayCaptureTests</c>, shared by every test that points the journey
/// at one decision through a <see cref="WalkPolicy"/> and holds the recording to
/// parity - the excused coverage rows and the regressions for a path the driver once
/// refused. One harness rather than one per class, because the store, the patches
/// and the teardown are what make a recorded walk honest and a second copy of them
/// is a second thing to keep alive.
/// </summary>
internal sealed class RecordedActWalk : IDisposable
{
    /// <summary>The whole-act fixture's own seed: the one run the journey's rules are
    /// known to carry through every room type of the first act.</summary>
    internal const string FixtureSeed = "67L571H38L";

    internal static readonly string[] Acts = ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"];

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"recorded-walk-{Guid.NewGuid():N}", "Runmobile", "steam", "test", "profile1");

    private readonly Harmony _harmony = new($"recorded-walk.{Guid.NewGuid():N}");

    // Held untyped: a field typed over a game type is laid out when this class loads,
    // which is before a test's first line has started the engine
    private readonly Delegate? _previousAnswered;
    private readonly Delegate? _previousReward;

    /// <summary>Started from a test class's constructor as well, so the game assembly
    /// is resolvable before the test method that builds this harness is prepared.</summary>
    internal RecordedActWalk()
    {
        EngineHost.Start();
        Directory.CreateDirectory(_root);
        RunmobileStore.UseRootForTesting(_root);

        _previousAnswered = CardPrompts.Answered;
        _previousReward = CardScreensUp.RewardAnswered;
        foreach (var type in CardScreensUp.PatchClasses) _harmony.CreateClassProcessor(type).Patch();
        foreach (var type in CardPrompts.PatchClasses) _harmony.CreateClassProcessor(type).Patch();
        foreach (var type in RunRecorder.PatchClasses) _harmony.CreateClassProcessor(type).Patch();
        _harmony.CreateClassProcessor(typeof(HeadlessCardRewardScreen)).Patch();
        RunRecorder.ReadTheAnswers();

        RunRecorder.GameIdentitySource = () => EngineHost.Origin == EngineOrigin.HeadlessHost;
        RunRecorder.Clock = new PumpedSettleClock();
    }

    public void Dispose()
    {
        if (RunManager.Instance is { IsInProgress: true } manager) manager.CleanUp();
        RunRecorder.RunTornDown();
        _harmony.UnpatchAll(_harmony.Id);
        CardPrompts.Answered = (Action<CardPrompts.Prompt, IReadOnlyList<MegaCrit.Sts2.Core.Models.CardModel>>?)_previousAnswered;
        CardScreensUp.RewardAnswered = (Action<int?>?)_previousReward;
        CardPrompts.Forget();

        RunmobileStore.UseRootForTesting(null);
        RunRecorder.ResetHostSeamsForTesting();
        HeadlessEngine.Forget();
        var sandbox = _root[.._root.IndexOf("Runmobile", StringComparison.Ordinal)];
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
    }

    /// <summary>What a recorded walk produced: the manifest the recorder wrote, its
    /// capture, and whether the policy's ask was met on the way.</summary>
    internal sealed record Recorded(ReplayManifest Manifest, RunCapture Capture, bool AskMet);

    /// <summary>
    /// The first act alone on the journey's route, through the recorder, on a fresh
    /// run of the seed: stopped where the policy says and finished as abandoned, the
    /// way the natural-run proof finishes one, because the journey's survival rules
    /// were tuned for its own choices and not the policy's.
    /// </summary>
    internal Recorded Walk(WalkPolicy policy, string seed = FixtureSeed, bool visitEveryRoomType = true)
    {
        if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
        var session = new GameSession();
        session.StartRun(seed, "CHARACTER.IRONCLAD", 0, "standard", Acts);
        using var driver = new RunDriver(session);
        driver.ImproviseUnrecordedCardSelections();
        driver.EnterFirstRoom();
        Assert.Equal(RunAttachment.Attached, RunRecorder.Attach());

        var walk = SyntheticFixtureGenerator.WalkTheAct(
            session, driver, [], DrainSettles, visitEveryRoomType, policy with { StopOnceMet = true });

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
        return new Recorded(manifest, capture, walk.AskMet);
    }

    /// <summary>The recording as the recorder left it: continuous, complete, and
    /// refused nowhere.</summary>
    internal static void AssertWhole(Recorded recorded)
    {
        var native = recorded.Manifest.Source.Native!;
        Assert.True(
            native.Continuity == NativeSource.ContinuousContinuity && recorded.Capture.Refusals.Count == 0,
            $"the recording is {native.Continuity}: {string.Join("; ", recorded.Capture.Refusals.Select(refusal => refusal.Reason))}");
        Assert.True(
            native.Integrity == NativeSource.CompleteIntegrity,
            $"the recording is {native.Integrity}: {(recorded.Capture.Stop is { } stop ? ManifestValidator.Describe(stop.Decision) : "")}");
    }

    /// <summary>A fresh replay of the recording through the CLI's own loop past the
    /// retail preflight, held to the journal decision for decision through the same
    /// oracle <c>parity</c> uses.</summary>
    internal static ArbiterOutcome ReplayToParity(Recorded recorded)
    {
        var replay = FreshReplay(recorded.Manifest);
        Assert.True(
            replay.Report.Status == VerificationStatus.Verified,
            $"the replay was {replay.Report.Status}: {string.Join("; ", replay.Report.Diagnostics)}");
        var parity = TraceParity.Compare(recorded.Capture.Trace, replay.Report.Trace!);
        Assert.True(parity.AtParity, parity.Describe());
        Assert.Empty(parity.OpeningDifferences);
        return replay;
    }

    internal static ArbiterOutcome FreshReplay(ReplayManifest manifest)
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
}

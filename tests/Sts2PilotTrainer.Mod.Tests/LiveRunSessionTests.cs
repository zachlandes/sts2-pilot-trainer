using System.Reflection;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The reading every recording is gated on, taken against a real run.
///
/// <c>LiveRun.ReadSession</c> is asked at attach and one answer passes, so a build on
/// which an ordinary singleplayer run reads as anything else is a mod that records
/// nothing at all and says so only in the log. The rest of the rule is checked without
/// the game - <c>RunSessionTests</c> owns what a kind permits, and
/// <c>GameSessionWatchTests</c> owns the no-run and multiplayer answers - and this is
/// the half that needs a run in progress to mean anything.
///
/// The run is started the way every headless caller starts one, and this process is
/// put back as it was found afterwards: a started headless engine and a live run are
/// both process-wide, and the rest of this assembly reads a client that has neither.
/// </summary>
[Collection(nameof(LiveRunSessionTests))]
[CollectionDefinition(nameof(LiveRunSessionTests), DisableParallelization = true)]
public sealed class LiveRunSessionTests
{
    [GameFact]
    public void AnOrdinarySingleplayerRunReadsAsOne()
    {
        var fixture = SyntheticReplayFixture.Create();

        Assert.Equal(RunSessionKind.NoRunInProgress, LiveRun.ReadSession());

        try
        {
            new GameSession().StartRun(
                fixture.Environment.Seed.Value,
                fixture.Environment.Character.Value,
                fixture.Environment.Ascension.Value,
                fixture.Environment.GameMode.Value,
                fixture.Environment.Acts.Value);

            Assert.Equal(RunSessionKind.Singleplayer, LiveRun.ReadSession());
            Assert.True(RunSession.MayBeRecorded(LiveRun.ReadSession()));
        }
        finally
        {
            EndTheRun();
            ForgetTheHeadlessEngine();
        }

        Assert.Equal(RunSessionKind.NoRunInProgress, LiveRun.ReadSession());
    }

    private static void EndTheRun()
    {
        var manager = GameType("MegaCrit.Sts2.Core.Runs.RunManager");
        var instance = manager.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)!.GetValue(null);
        if (instance is null) return;
        if ((bool)manager.GetProperty("IsInProgress")!.GetValue(instance)! is false) return;

        var cleanUp = manager.GetMethod("CleanUp", BindingFlags.Public | BindingFlags.Instance)!;
        cleanUp.Invoke(instance, cleanUp.GetParameters().Select(_ => Type.Missing).ToArray());
    }

    /// <summary>
    /// Puts <see cref="EngineHost"/> back to the process it was called in.
    ///
    /// Starting the headless engine is how a run is started at all, and it is also
    /// what <c>AdoptRunningGame</c> refuses on: a process that has started its own
    /// engine has no running game to adopt, and <c>ModHostBoundaryTests</c> asks that
    /// question of this same process. So the flags are put back rather than left for
    /// whichever test happens to run next. The engine itself stays initialised, which
    /// is what makes a second start a no-op.
    /// </summary>
    private static void ForgetTheHeadlessEngine()
    {
        Field("_started").SetValue(null, false);
        Field("<Origin>k__BackingField").SetValue(null, EngineOrigin.None);
        Field("<Startup>k__BackingField").SetValue(null, null);
    }

    private static FieldInfo Field(string name) =>
        typeof(EngineHost).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"EngineHost has no {name}; this reset is out of date.");

    private static Type GameType(string name)
    {
        _ = EngineHost.StartupPhase();
        return AppDomain.CurrentDomain.GetAssemblies()
            .Single(assembly => assembly.GetName().Name == "sts2")
            .GetType(name)!;
    }
}

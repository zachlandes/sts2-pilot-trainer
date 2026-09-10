using Sts2PilotTrainer.Engine;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The two restore paths refuse the host they are not for, in words that name the
/// other one, so a wrong call is an instruction rather than a run with no scene behind
/// it or a scene with no run in it.
/// </summary>
public sealed class RestoreInRunningGameTests
{
    [GameFact]
    public async Task TheInClientRestoreRefusesAHeadlessProcessAndNamesTheHeadlessPath()
    {
        _ = EngineHost.StartupPhase();
        HeadlessEngine.Forget();

        var refusal = await Assert.ThrowsAsync<EngineException>(
            () => new GameSession().PrepareRestoreInRunningGame("{}"));

        Assert.Contains("no running game", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("RestoreSavedRun", refusal.Message, StringComparison.Ordinal);
    }

    [GameFact]
    public void TheHeadlessRestoreRefusesARunningClientAndNamesTheInClientPath()
    {
        _ = EngineHost.StartupPhase();
        HeadlessEngine.Forget();
        typeof(EngineHost).GetProperty(nameof(EngineHost.Origin))!.SetValue(null, EngineOrigin.RunningGame);
        try
        {
            var refusal = Assert.Throws<EngineException>(() => new GameSession().RestoreSavedRun("{}"));

            Assert.Contains("PrepareRestoreInRunningGame", refusal.Message, StringComparison.Ordinal);
        }
        finally
        {
            HeadlessEngine.Forget();
        }
    }
}

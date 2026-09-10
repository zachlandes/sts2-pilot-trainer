namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What a native-scene fact does on a given machine, which is the part that used to be
/// wrong: these facts are the only check on a role's node path, and the state that let
/// the ledger-row defect reach a player is a prepared game whose pack was not found,
/// reported as green.
/// </summary>
public sealed class NativeSceneAvailabilityTests
{
    [Fact]
    public void WithoutAPreparedGameThereIsNothingToCheckAndTheFactSkips()
    {
        var availability = NativeScenes.Decide(gamePrepared: false, pack: null);

        Assert.Equal(NativeScenes.AvailabilityState.NotBuilt, availability.State);
        Assert.Contains("./scripts/build.sh", availability.Message);
    }

    [Fact]
    public void APreparedGameWithItsPackRuns()
    {
        var availability = NativeScenes.Decide(gamePrepared: true, pack: "/somewhere/Slay the Spire 2.pck");

        Assert.Equal(NativeScenes.AvailabilityState.Run, availability.State);
        Assert.Null(availability.Message);
    }

    [Fact]
    public void APreparedGameWhosePackWasNotFoundFailsRatherThanSkipping()
    {
        var availability = NativeScenes.Decide(gamePrepared: true, pack: null);

        Assert.Equal(NativeScenes.AvailabilityState.Unresolved, availability.State);
        Assert.Contains("STS2_GAME_PCK", availability.Message);
        Assert.Contains("--game-dir", availability.Message);
    }
}

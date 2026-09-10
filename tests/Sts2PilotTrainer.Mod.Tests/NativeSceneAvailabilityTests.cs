namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What a native-scene fact does on a given machine, which is the part that used to be
/// wrong twice over: these facts are the only check on a role's node path, so a skip
/// that reads as green is how the ledger-row defect reached a player, and a red that
/// blames the role table for a Steam update is a different lie about the same table.
/// </summary>
public sealed class NativeSceneAvailabilityTests
{
    private static readonly NativeScenes.BuildIdentity Prepared = new("v0.111.0", "41cef1ea", 1172974615);
    private static readonly NativeScenes.PackSearch Found = new("/somewhere/Slay the Spire 2.pck", null);

    [Fact]
    public void WithoutAPreparedGameThereIsNothingToCheckAndTheFactSkips()
    {
        var availability = NativeScenes.Decide(
            prepared: false, preparedBuild: null, pack: new NativeScenes.PackSearch(null, null), installed: null);

        Assert.Equal(NativeScenes.AvailabilityState.NotPrepared, availability.State);
        Assert.Contains("./scripts/build.sh", availability.Message);
    }

    [Fact]
    public void APackFromThePreparedBuildRuns()
    {
        var availability = NativeScenes.Decide(prepared: true, Prepared, Found, Prepared);

        Assert.Equal(NativeScenes.AvailabilityState.Run, availability.State);
    }

    [Fact]
    public void APackFromAnotherBuildSkipsAndNamesBothBuilds()
    {
        var installed = new NativeScenes.BuildIdentity("v0.112.0", "abc12345", 99);

        var availability = NativeScenes.Decide(prepared: true, Prepared, Found, installed);

        Assert.Equal(NativeScenes.AvailabilityState.BuildMismatch, availability.State);
        Assert.Contains("v0.111.0", availability.Message);
        Assert.Contains("41cef1ea", availability.Message);
        Assert.Contains("v0.112.0", availability.Message);
        Assert.Contains("abc12345", availability.Message);
        Assert.Contains("./scripts/build.sh", availability.Message);
    }

    /// <summary>
    /// The same build rebuilt is a different assembly hash and the same commit, and it
    /// is still not the build the role table was read from.
    /// </summary>
    [Fact]
    public void APackWhoseAssemblyHashDiffersSkipsEvenAtTheSameCommit()
    {
        var installed = Prepared with { MainAssemblyHash = Prepared.MainAssemblyHash + 1 };

        var availability = NativeScenes.Decide(prepared: true, Prepared, Found, installed);

        Assert.Equal(NativeScenes.AvailabilityState.BuildMismatch, availability.State);
    }

    [Fact]
    public void APreparedGameWithNoPackAnywhereSkipsNamingTheOverride()
    {
        var availability = NativeScenes.Decide(
            prepared: true, Prepared, new NativeScenes.PackSearch(null, null), installed: null);

        Assert.Equal(NativeScenes.AvailabilityState.NoPack, availability.State);
        Assert.Contains("STS2_GAME_PCK", availability.Message);
        Assert.Contains("--archive", availability.Message);
        Assert.DoesNotContain("--game-dir", availability.Message);
    }

    /// <summary>
    /// A mistyped override is a different fact from no installation, and saying so is
    /// the difference between one edit and a search.
    /// </summary>
    [Fact]
    public void AnOverrideNamingNothingSaysSoRatherThanReportingNoInstallation()
    {
        var search = new NativeScenes.PackSearch(
            null, "STS2_GAME_PCK names '/nope/missing.pck', which is not a file.");

        var availability = NativeScenes.Decide(prepared: true, Prepared, search, installed: null);

        Assert.Equal(NativeScenes.AvailabilityState.NoPack, availability.State);
        Assert.Contains("/nope/missing.pck", availability.Message);
    }

    /// <summary>
    /// An identity that could not be read is not a match, and it is not a pass either:
    /// the pack cannot be tied to the prepared assemblies at all.
    /// </summary>
    [Fact]
    public void AnUnreadableBuildIdentitySkipsRatherThanRunning()
    {
        var availability = NativeScenes.Decide(prepared: true, preparedBuild: null, Found, installed: Prepared);

        Assert.Equal(NativeScenes.AvailabilityState.BuildMismatch, availability.State);
        Assert.Contains("release_info.json.copy", availability.Message);
    }
}

using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// The refusal a fight that never finished opening is stopped with.
///
/// A half-open fight was refused by the boundary, which compares card by card and
/// reported the recording's five-card hand against the one card dealt so far. That
/// sentence is true about the comparison and wrong about what happened, and a player
/// reading it concludes the recording is broken. This guards that the timeout's own
/// sentence says the fight did not open rather than that something did not match.
/// </summary>
public sealed class RefusalCopyTests
{
    [Fact]
    public void APlayFromClickAndACodeLookupUseTheSameBuildRefusal()
    {
        var expected = Fixtures.Identity();
        var actual = BuildOf(expected) with { BuildVersion = "v0.110.0" };
        var fields = EnvironmentPreflight.Build(expected, actual);
        var run = LibraryRun.From(Fixtures.Recording(), RunOrigin.Recent, RunVerdict.Absent);
        var lookup = RunBrowser.Lookup(run.EntryId, [run], actual.BuildVersion);

        Assert.Equal(lookup.Body, LibraryCopy.PlayFromBuildRefusal(fields));
        Assert.Empty(RunBrowser.For(LibraryTab.Community, [run], actual.BuildVersion).Groups);
        Assert.True(lookup.Refused);
    }

    [Fact]
    public void AMatchingBuildHasNoPlayFromRefusal()
    {
        var expected = Fixtures.Identity();
        Assert.Null(LibraryCopy.PlayFromBuildRefusal(EnvironmentPreflight.Build(expected, BuildOf(expected))));
    }

    [Theory]
    [InlineData("build_date_utc")]
    [InlineData("content_hash")]
    public void ABuildWithTheSameVersionButDifferentIdentityStillRefuses(string field)
    {
        var expected = Fixtures.Identity();
        var actual = field == "build_date_utc"
            ? BuildOf(expected) with { BuildDateUtc = "2000.01.01" }
            : BuildOf(expected) with { ContentHash = "1" };

        Assert.Equal(LibraryCopy.LookupRefusedNoLongerMatches,
            LibraryCopy.PlayFromBuildRefusal(EnvironmentPreflight.Build(expected, actual)));
    }

    private static LocalBuild BuildOf(EnvironmentIdentity identity) =>
        new(identity.BuildVersion.Value, identity.BuildDateUtc.Value, identity.ContentHash.Value);

    [Fact]
    public void TheRefusalSaysTheFightDidNotOpenRatherThanThatSomethingDidNotMatch()
    {
        var refusal = TrainerCopy.FightDidNotOpen;

        Assert.StartsWith("The fight didn't finish opening", refusal, StringComparison.Ordinal);
        Assert.Contains(TrainerCopy.RefusalNoHarm, refusal, StringComparison.Ordinal);
    }
}

using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Replay.Tests;

public sealed class ModeProbeBindingTests
{
    private static readonly ModeProbeBinding Standard = new(
        "standard",
        "schema",
        "run",
        "video",
        "version",
        "commit",
        "seed",
        "actions",
        ["modifier-a", "modifier-b"]);

    [Fact]
    public void MatchingModifierProbeMayBeClassified()
    {
        var result = ModeProbeBindingComparer.Compare(
            Standard,
            Standard with { Source = "modifier:Terminal" });

        Assert.True(result.MayClassify);
        Assert.Empty(result.Mismatches);
    }

    [Fact]
    public void ModifierProbeFromAnotherBuildIsRefusedBeforeClassification()
    {
        var result = ModeProbeBindingComparer.Compare(
            Standard,
            Standard with { Source = "modifier:Terminal", BuildCommit = "other" });

        var mismatch = Assert.Single(result.Mismatches);
        Assert.False(result.MayClassify);
        Assert.Equal("build_commit", mismatch.Field);
        Assert.Equal("modifier:Terminal", mismatch.CandidateSource);
    }
}

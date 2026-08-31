using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Replay.Tests;

public sealed class PublicationEvidenceBindingTests
{
    private static readonly PublicationEvidenceBinding Mode = new(
        "mode-discrimination",
        true,
        "run",
        "video",
        "v0.111.0",
        "commit",
        "seed",
        "actions",
        "state");

    [Fact]
    public void MatchingPassingEvidenceIsBound()
    {
        var result = PublicationEvidenceBindingComparer.Compare(
            Mode,
            Mode with { Source = "baselib-reachability" });

        Assert.True(result.Passed);
        Assert.Empty(result.Mismatches);
    }

    [Theory]
    [InlineData("run_id")]
    [InlineData("video_id")]
    [InlineData("build_version")]
    [InlineData("build_commit")]
    [InlineData("seed")]
    [InlineData("action_history_hash")]
    [InlineData("final_state_sha256")]
    public void PassingArtifactsForDifferentHistoriesAreRefused(string field)
    {
        var other = field switch
        {
            "run_id" => Mode with { Source = "baselib-reachability", RunId = "other" },
            "video_id" => Mode with { Source = "baselib-reachability", VideoId = "other" },
            "build_version" => Mode with { Source = "baselib-reachability", BuildVersion = "other" },
            "build_commit" => Mode with { Source = "baselib-reachability", BuildCommit = "other" },
            "seed" => Mode with { Source = "baselib-reachability", Seed = "other" },
            "action_history_hash" => Mode with { Source = "baselib-reachability", ActionHistoryHash = "other" },
            "final_state_sha256" => Mode with { Source = "baselib-reachability", FinalStateSha256 = "other" },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

        var result = PublicationEvidenceBindingComparer.Compare(Mode, other);

        var mismatch = Assert.Single(result.Mismatches);
        Assert.False(result.Passed);
        Assert.Equal(field, mismatch.Field);
        Assert.Equal("mode-discrimination", mismatch.LeftSource);
        Assert.Equal("baselib-reachability", mismatch.RightSource);
    }
}

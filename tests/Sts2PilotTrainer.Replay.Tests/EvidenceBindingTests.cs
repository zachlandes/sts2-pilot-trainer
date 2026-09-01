using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// Binding is what stops a stale report on disk being assembled into a verdict
/// alongside a fresh one, so every field it compares has a test that changes only
/// that field.
/// </summary>
public sealed class EvidenceBindingTests
{
    private static readonly (string Field, string Value)[] PublicationFields =
    [
        ("internal_pass", "True"),
        ("run_id", "run"),
        ("video_id", "video"),
        ("build_version", "v0.111.0"),
        ("build_commit", "commit"),
        ("seed", "seed"),
        ("action_history_hash", "actions"),
        ("final_state_sha256", "state"),
    ];

    private static readonly (string Field, string Value)[] ModeProbeFields =
    [
        ("schema", "schema"),
        ("run_id", "run"),
        ("video_id", "video"),
        ("build_version", "v0.111.0"),
        ("build_commit", "commit"),
        ("seed", "seed"),
        ("action_history_hash", "actions"),
        ("available_modifier_types", "modifier-a\nmodifier-b"),
    ];

    [Fact]
    public void EvidenceAboutTheSameReconstructionIsBound()
    {
        var result = EvidenceBindingComparer.Compare(
            EvidenceBinding.Of("mode-discrimination", PublicationFields),
            EvidenceBinding.Of("baselib-reachability", PublicationFields));

        Assert.True(result.Bound);
        Assert.Empty(result.Mismatches);
    }

    [Theory]
    [InlineData("internal_pass")]
    [InlineData("run_id")]
    [InlineData("video_id")]
    [InlineData("build_version")]
    [InlineData("build_commit")]
    [InlineData("seed")]
    [InlineData("action_history_hash")]
    [InlineData("final_state_sha256")]
    public void EvidenceDifferingInAnyBoundFieldIsRefused(string field)
    {
        var result = EvidenceBindingComparer.Compare(
            EvidenceBinding.Of("mode-discrimination", PublicationFields),
            EvidenceBinding.Of("baselib-reachability", Changed(PublicationFields, field)));

        var mismatch = Assert.Single(result.Mismatches);
        Assert.False(result.Bound);
        Assert.Equal(field, mismatch.Field);
        Assert.Equal("mode-discrimination", mismatch.LeftSource);
        Assert.Equal("baselib-reachability", mismatch.RightSource);
    }

    [Fact]
    public void AProbeFromAnotherBuildIsRefusedBeforeItCanBeClassified()
    {
        var result = EvidenceBindingComparer.Compare(
            EvidenceBinding.Of("standard", ModeProbeFields),
            EvidenceBinding.Of("modifier:Terminal", Changed(ModeProbeFields, "build_commit")));

        var mismatch = Assert.Single(result.Mismatches);
        Assert.False(result.Bound);
        Assert.Equal("build_commit", mismatch.Field);
        Assert.Equal("modifier:Terminal", mismatch.RightSource);
    }

    [Fact]
    public void AProbeOfferingADifferentModifierSpaceIsRefused()
    {
        // A build with a different modifier list is enumerating a different space, and
        // a parity claim over the wrong space is worse than no claim.
        var result = EvidenceBindingComparer.Compare(
            EvidenceBinding.Of("standard", ModeProbeFields),
            EvidenceBinding.Of("modifier:Terminal", Changed(ModeProbeFields, "available_modifier_types")));

        Assert.False(result.Bound);
        Assert.Equal("available_modifier_types", Assert.Single(result.Mismatches).Field);
    }

    [Fact]
    public void AReportThatStoppedEmittingABoundFieldIsRefusedRatherThanSkipped()
    {
        // The drift most worth catching: a probe that quietly drops a field would
        // otherwise bind to anything, because there would be nothing left to disagree.
        var result = EvidenceBindingComparer.Compare(
            EvidenceBinding.Of("mode-discrimination", PublicationFields),
            EvidenceBinding.Of(
                "baselib-reachability", PublicationFields.Where(entry => entry.Field != "seed")));

        var mismatch = Assert.Single(result.Mismatches);
        Assert.False(result.Bound);
        Assert.Equal("seed", mismatch.Field);
        Assert.Equal("<absent>", mismatch.RightValue);
    }

    private static IEnumerable<(string Field, string Value)> Changed(
        IEnumerable<(string Field, string Value)> fields, string field) =>
        fields.Select(entry => entry.Field == field ? (entry.Field, "other") : entry);
}

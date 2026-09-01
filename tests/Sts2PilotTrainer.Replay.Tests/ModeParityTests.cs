using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The parity decision behind the game-mode condition. Every modifier this build
/// offers happens to land in one bucket for the shipped VOD, so the branch that
/// would actually refuse publication is never taken by real data. It is exercised
/// here instead, because a classifier only ever observed saying one thing has not
/// been shown able to say the other.
/// </summary>
public class ModeParityTests
{
    private static readonly ModeParityInputs Baseline =
        new(CompletedHistory: true, AllCheckpointsPassed: true, "sha256:checkpoints", "sha256:state");

    [Fact]
    public void IdenticalResultsAreInvisible()
    {
        Assert.Equal(ModeParityClass.Invisible, ModeParity.Classify(Baseline, Baseline));
        Assert.False(ModeParity.LeavesModeOpen(ModeParity.Classify(Baseline, Baseline)));
    }

    [Fact]
    public void DifferentCheckpointsAreVisibleAndSoExcludedByTheRecording()
    {
        var candidate = Baseline with { CheckpointSha256 = "sha256:other" };
        Assert.Equal(ModeParityClass.CheckpointVisible, ModeParity.Classify(Baseline, candidate));
        Assert.False(ModeParity.LeavesModeOpen(ModeParity.Classify(Baseline, candidate)));
    }

    [Fact]
    public void AFailedCheckpointIsVisibleEvenWhenItsHashWouldMatch()
    {
        var candidate = Baseline with { AllCheckpointsPassed = false };
        Assert.Equal(ModeParityClass.CheckpointVisible, ModeParity.Classify(Baseline, candidate));
    }

    [Fact]
    public void AnIncompleteHistoryIsVisible()
    {
        var candidate = Baseline with { CompletedHistory = false };
        Assert.Equal(ModeParityClass.CheckpointVisible, ModeParity.Classify(Baseline, candidate));
    }

    /// <summary>
    /// The refusing case: the checkpoints agree, so the footage cannot tell these two
    /// configurations apart, and the resulting state does not. Path-specific parity
    /// must not be claimed here.
    /// </summary>
    [Fact]
    public void MatchingCheckpointsWithADifferentStateLeavesTheModeOpen()
    {
        var candidate = Baseline with { BehavioralStateSha256 = "sha256:elsewhere" };
        var classification = ModeParity.Classify(Baseline, candidate);
        Assert.Equal(ModeParityClass.StateOnlyDivergence, classification);
        Assert.True(ModeParity.LeavesModeOpen(classification));
    }

    [Fact]
    public void WireNamesAreStableAcrossEveryClassification()
    {
        Assert.Equal("checkpoint_visible", ModeParity.WireName(ModeParityClass.CheckpointVisible));
        Assert.Equal("invisible", ModeParity.WireName(ModeParityClass.Invisible));
        Assert.Equal("state_only_divergence", ModeParity.WireName(ModeParityClass.StateOnlyDivergence));
    }
}

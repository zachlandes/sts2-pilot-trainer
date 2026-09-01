namespace Sts2PilotTrainer.Replay;

/// <summary>What one candidate mode configuration changes, relative to the verified baseline.</summary>
public enum ModeParityClass
{
    /// <summary>Changes something a checkpoint watches, so the recording already rules it out.</summary>
    CheckpointVisible,

    /// <summary>Changes nothing observable and nothing in the resulting state.</summary>
    Invisible,

    /// <summary>
    /// Reproduces every observed checkpoint and still lands somewhere else. This is the only
    /// outcome that leaves the source mode genuinely open: consistent with the recording and
    /// inconsistent with the replay, so the footage cannot tell the two configurations apart.
    /// </summary>
    StateOnlyDivergence,
}

/// <summary>The comparable result of replaying one configuration.</summary>
public readonly record struct ModeParityInputs(
    bool CompletedHistory,
    bool AllCheckpointsPassed,
    string CheckpointSha256,
    string BehavioralStateSha256);

public static class ModeParity
{
    public const string CheckpointVisibleName = "checkpoint_visible";
    public const string InvisibleName = "invisible";
    public const string StateOnlyDivergenceName = "state_only_divergence";

    public static ModeParityClass Classify(ModeParityInputs baseline, ModeParityInputs candidate)
    {
        if (candidate.CompletedHistory != baseline.CompletedHistory ||
            candidate.AllCheckpointsPassed != baseline.AllCheckpointsPassed ||
            !string.Equals(candidate.CheckpointSha256, baseline.CheckpointSha256, StringComparison.Ordinal))
        {
            return ModeParityClass.CheckpointVisible;
        }
        return string.Equals(
            candidate.BehavioralStateSha256, baseline.BehavioralStateSha256, StringComparison.Ordinal)
            ? ModeParityClass.Invisible
            : ModeParityClass.StateOnlyDivergence;
    }

    /// <summary>Path-specific parity holds only while nothing in the space leaves the mode open.</summary>
    public static bool LeavesModeOpen(ModeParityClass classification) =>
        classification == ModeParityClass.StateOnlyDivergence;

    public static string WireName(ModeParityClass classification) => classification switch
    {
        ModeParityClass.CheckpointVisible => CheckpointVisibleName,
        ModeParityClass.Invisible => InvisibleName,
        ModeParityClass.StateOnlyDivergence => StateOnlyDivergenceName,
        _ => throw new ArgumentOutOfRangeException(nameof(classification)),
    };
}

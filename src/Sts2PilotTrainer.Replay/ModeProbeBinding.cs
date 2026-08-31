namespace Sts2PilotTrainer.Replay;

public sealed record ModeProbeBinding(
    string Source,
    string Schema,
    string RunId,
    string VideoId,
    string BuildVersion,
    string BuildCommit,
    string Seed,
    string ActionHistoryHash,
    IReadOnlyList<string> AvailableModifierTypes);

public sealed record ModeProbeBindingMismatch(
    string Field,
    string BaselineSource,
    string BaselineValue,
    string CandidateSource,
    string CandidateValue);

public sealed record ModeProbeBindingResult(
    IReadOnlyList<ModeProbeBindingMismatch> Mismatches)
{
    public bool MayClassify => Mismatches.Count == 0;
}

public static class ModeProbeBindingComparer
{
    public static ModeProbeBindingResult Compare(ModeProbeBinding baseline, ModeProbeBinding candidate)
    {
        var mismatches = new List<ModeProbeBindingMismatch>();
        Compare("schema", baseline.Schema, candidate.Schema);
        Compare("run_id", baseline.RunId, candidate.RunId);
        Compare("video_id", baseline.VideoId, candidate.VideoId);
        Compare("build_version", baseline.BuildVersion, candidate.BuildVersion);
        Compare("build_commit", baseline.BuildCommit, candidate.BuildCommit);
        Compare("seed", baseline.Seed, candidate.Seed);
        Compare("action_history_hash", baseline.ActionHistoryHash, candidate.ActionHistoryHash);
        Compare(
            "available_modifier_types",
            string.Join("\n", baseline.AvailableModifierTypes),
            string.Join("\n", candidate.AvailableModifierTypes));
        return new ModeProbeBindingResult(mismatches);

        void Compare(string field, string baselineValue, string candidateValue)
        {
            if (!string.Equals(baselineValue, candidateValue, StringComparison.Ordinal))
            {
                mismatches.Add(new ModeProbeBindingMismatch(
                    field,
                    baseline.Source,
                    baselineValue,
                    candidate.Source,
                    candidateValue));
            }
        }
    }
}

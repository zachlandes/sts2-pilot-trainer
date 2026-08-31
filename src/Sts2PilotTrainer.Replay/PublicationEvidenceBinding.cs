namespace Sts2PilotTrainer.Replay;

public sealed record PublicationEvidenceBinding(
    string Source,
    bool Passed,
    string RunId,
    string VideoId,
    string BuildVersion,
    string BuildCommit,
    string Seed,
    string ActionHistoryHash,
    string FinalStateSha256);

public sealed record PublicationEvidenceMismatch(
    string Field,
    string LeftSource,
    string LeftValue,
    string RightSource,
    string RightValue);

public sealed record PublicationEvidenceBindingResult(
    IReadOnlyList<PublicationEvidenceMismatch> Mismatches)
{
    public bool Passed => Mismatches.Count == 0;
}

public static class PublicationEvidenceBindingComparer
{
    public static PublicationEvidenceBindingResult Compare(
        PublicationEvidenceBinding left,
        PublicationEvidenceBinding right)
    {
        var mismatches = new List<PublicationEvidenceMismatch>();
        Compare("internal_pass", left.Passed.ToString(), right.Passed.ToString());
        Compare("run_id", left.RunId, right.RunId);
        Compare("video_id", left.VideoId, right.VideoId);
        Compare("build_version", left.BuildVersion, right.BuildVersion);
        Compare("build_commit", left.BuildCommit, right.BuildCommit);
        Compare("seed", left.Seed, right.Seed);
        Compare("action_history_hash", left.ActionHistoryHash, right.ActionHistoryHash);
        Compare("final_state_sha256", left.FinalStateSha256, right.FinalStateSha256);
        return new PublicationEvidenceBindingResult(mismatches);

        void Compare(string field, string leftValue, string rightValue)
        {
            if (!string.Equals(leftValue, rightValue, StringComparison.Ordinal))
            {
                mismatches.Add(new PublicationEvidenceMismatch(
                    field,
                    left.Source,
                    leftValue,
                    right.Source,
                    rightValue));
            }
        }
    }
}

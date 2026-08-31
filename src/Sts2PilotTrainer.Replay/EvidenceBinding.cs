namespace Sts2PilotTrainer.Replay;

/// <summary>
/// One piece of evidence reduced to the values that say which reconstruction it is
/// about.
///
/// The gate's conditions are computed by separate probes in separate processes, and
/// each writes its own report. Nothing about running them in sequence guarantees they
/// all describe the same build, the same seed and the same action history - a stale
/// report on disk looks exactly like a fresh one. Binding is what closes that: every
/// report reduces to the same named fields, and a verdict is only assembled from
/// reports whose fields agree.
///
/// Which fields those are is decided where the report is read, because that is where
/// their names live. This type owns the rule, not the list.
/// </summary>
public sealed record EvidenceBinding
{
    /// <summary>Names the report, so a mismatch says which two disagreed.</summary>
    public required string Source { get; init; }

    /// <summary>Field name to value. Compared exactly, in full: a field present on one
    /// side and absent on the other is a mismatch rather than something to skip, since
    /// a report that stopped emitting a field is precisely the drift worth catching.</summary>
    public required IReadOnlyDictionary<string, string> Fields { get; init; }

    public static EvidenceBinding Of(string source, IEnumerable<(string Field, string Value)> fields) =>
        new()
        {
            Source = source,
            Fields = new SortedDictionary<string, string>(
                fields.ToDictionary(entry => entry.Field, entry => entry.Value, StringComparer.Ordinal),
                StringComparer.Ordinal),
        };
}

public sealed record EvidenceMismatch(
    string Field,
    string LeftSource,
    string LeftValue,
    string RightSource,
    string RightValue);

public sealed record EvidenceBindingResult(IReadOnlyList<EvidenceMismatch> Mismatches)
{
    /// <summary>True only when the two reports describe the same reconstruction.</summary>
    public bool Bound => Mismatches.Count == 0;
}

public static class EvidenceBindingComparer
{
    private const string Absent = "<absent>";

    public static EvidenceBindingResult Compare(EvidenceBinding left, EvidenceBinding right)
    {
        var mismatches = left.Fields.Keys
            .Union(right.Fields.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(field => new
            {
                Field = field,
                Left = left.Fields.GetValueOrDefault(field, Absent),
                Right = right.Fields.GetValueOrDefault(field, Absent),
            })
            .Where(entry => !string.Equals(entry.Left, entry.Right, StringComparison.Ordinal))
            .Select(entry => new EvidenceMismatch(
                entry.Field, left.Source, entry.Left, right.Source, entry.Right))
            .ToList();

        return new EvidenceBindingResult(mismatches);
    }
}

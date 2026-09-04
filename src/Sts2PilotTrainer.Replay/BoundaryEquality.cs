namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Whether the moment a player is about to be given is the moment the recording
/// records.
///
/// The last gate before control changes hands, and the only one that can be asked
/// at all: everything upstream establishes that this environment could reproduce the
/// recording, and this establishes that it did. Two independent readings are
/// compared, and both have to agree.
///
/// The first is the recording's own observation of that moment, field by field -
/// the same <see cref="Checkpoint"/> the replay arbiter compares. The second is the
/// boundary's snapshot digest, which covers the whole canonical state including the
/// parts no video can show: every run-persistent RNG stream position and the order
/// of the draw pile. A hand that matches over a stream that does not is a fight that
/// will diverge on the next shuffle, and only the digest can see it.
///
/// The rule is one rule for every <see cref="ReplayBoundary.Kinds">kind</see> of
/// boundary; only the sentence a refusal is written in differs, because what a
/// player is being told they did not get differs. Pure, so every rule here has a
/// test on a machine with no game installed. It computes nothing about the fight and
/// says nothing about how it should be played.
/// </summary>
public sealed record BoundaryEquality
{
    /// <summary>Which kind of boundary was compared, from
    /// <see cref="ReplayBoundary.Kinds"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>Whether the live state is the recorded boundary on both readings.</summary>
    public required bool Matches { get; init; }

    /// <summary>Every observed field, compared. Present whether or not they all
    /// agreed: a reader that only sees the disagreements cannot tell a boundary that
    /// was checked thoroughly from one that was barely checked at all.</summary>
    public required IReadOnlyList<FieldComparison> Comparisons { get; init; }

    /// <summary>The digest the recording holds for this boundary.</summary>
    public required string ExpectedDigest { get; init; }

    /// <summary>The digest of the state the live run actually reached.</summary>
    public required string ActualDigest { get; init; }

    /// <summary>Why this is not the recorded boundary, written for the player who is
    /// about to not get it. Null when it is.</summary>
    public required string? Refusal { get; init; }

    /// <summary>
    /// Compares one live canonical state against the recording's boundary.
    /// </summary>
    /// <param name="kind">Which kind of boundary this is, from <see cref="ReplayBoundary.Kinds"/>.</param>
    /// <param name="boundary">What the recording observed at that moment.</param>
    /// <param name="live">The canonical state the live run reached, field by field.</param>
    /// <param name="liveDigest">Digest over the whole of that state.</param>
    /// <param name="expectedDigest">The recording's digest for this boundary.</param>
    public static BoundaryEquality Compare(
        string kind,
        Checkpoint boundary,
        IReadOnlyDictionary<string, string> live,
        string liveDigest,
        string expectedDigest)
    {
        if (!ReplayBoundary.Kinds.Contains(kind, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                $"'{kind}' is not a boundary kind this build knows. The kinds are a closed set: " +
                $"{string.Join(", ", ReplayBoundary.Kinds)}.",
                nameof(kind));
        }

        if (string.IsNullOrWhiteSpace(expectedDigest))
        {
            throw new ArgumentException(
                "A boundary cannot be verified without the recording's snapshot digest.",
                nameof(expectedDigest));
        }

        var comparisons = new List<FieldComparison>();
        foreach (var (field, expected) in boundary.Expect.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var present = live.TryGetValue(field, out var actual);
            comparisons.Add(new FieldComparison(
                field,
                expected.Value,
                present ? actual! : "<no such canonical field>",
                present && string.Equals(actual, expected.Value, StringComparison.Ordinal)));
        }

        var disagreements = comparisons.Where(comparison => !comparison.Matches).ToList();
        var digestDisagrees = !string.Equals(expectedDigest, liveDigest, StringComparison.Ordinal);

        return new BoundaryEquality
        {
            Kind = kind,
            Matches = disagreements.Count == 0 && !digestDisagrees,
            Comparisons = comparisons,
            ExpectedDigest = expectedDigest,
            ActualDigest = liveDigest,
            Refusal = Refuse(kind, boundary, disagreements, digestDisagrees, expectedDigest, liveDigest),
        };
    }

    private static string? Refuse(
        string kind,
        Checkpoint boundary,
        IReadOnlyList<FieldComparison> disagreements,
        bool digestDisagrees,
        string expectedDigest,
        string liveDigest)
    {
        if (disagreements.Count == 0 && !digestDisagrees) return null;

        if (disagreements.Count > 0)
        {
            var listed = string.Join("; ", disagreements.Select(comparison =>
                $"{comparison.Field}: the recording shows '{comparison.Expected}', this game produced " +
                $"'{comparison.Actual}'"));
            var opening = kind == ReplayBoundary.FloorEntryKind
                ? "This floor was not arrived at the way the recording's was, so it was not entered."
                : "This fight did not open the way the recording's did, so it was not entered.";
            var closing = kind == ReplayBoundary.FloorEntryKind
                ? "Something earlier in the run differed, and a floor arrived at from a different run is not " +
                  "the recording's floor."
                : "Something before the fight differed, and a fight that starts somewhere else cannot be " +
                  "compared against the recording's.";
            return $"{opening} At checkpoint '{boundary.Id}': {listed}. {closing}";
        }

        var visible = kind == ReplayBoundary.FloorEntryKind
            ? "This floor was arrived at with everything the recording shows and with different hidden state, " +
              "so it was not entered."
            : "This fight opened with everything the recording shows and with different hidden state, so it " +
              "was not entered.";
        return
            $"{visible} The recorded snapshot is {expectedDigest} and this game reached {liveDigest}. The " +
            "difference is in state no video can show - a run-persistent random stream, or the order of the " +
            "draw pile - and a run that continues from there diverges from the recording's at the next shuffle.";
    }
}

/// <summary>
/// The combat-start reading of <see cref="BoundaryEquality"/>, for callers written
/// before boundaries were a list.
///
/// A thin forward and nothing else. It exists so that the kind-aware rule could
/// arrive without every entry path being rewritten in the same change, and it goes
/// when the last caller does.
/// </summary>
public static class CombatStartEquality
{
    /// <inheritdoc cref="BoundaryEquality.Compare"/>
    public static BoundaryEquality Compare(
        Checkpoint boundary,
        IReadOnlyDictionary<string, string> live,
        string liveDigest,
        string expectedDigest) =>
        BoundaryEquality.Compare(ReplayBoundary.CombatStartKind, boundary, live, liveDigest, expectedDigest);
}

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Whether the fight a player is about to be given is the fight the recording
/// starts.
///
/// The last gate before control changes hands, and the only one that can be asked
/// at all: everything upstream establishes that this environment could reproduce the
/// recording, and this establishes that it did. Two independent readings are
/// compared, and both have to agree.
///
/// The first is the recording's own observation of that moment, field by field -
/// the same <see cref="Checkpoint"/> the replay arbiter compares, read from the
/// video at source resolution. The second is the combat-start snapshot digest,
/// which covers the whole canonical state including the parts no video can show:
/// every run-persistent RNG stream position and the order of the draw pile. A hand
/// that matches over a stream that does not is a fight that will diverge on the
/// next shuffle, and only the digest can see it.
///
/// Pure, so every rule here has a test on a machine with no game installed. It
/// computes nothing about the fight and says nothing about how it should be played.
/// </summary>
public sealed record CombatStartEquality
{
    /// <summary>Whether the live state is the recorded boundary on both readings.</summary>
    public required bool Matches { get; init; }

    /// <summary>Every observed field, compared. Present whether or not they all
    /// agreed: a reader that only sees the disagreements cannot tell a boundary that
    /// was checked thoroughly from one that was barely checked at all.</summary>
    public required IReadOnlyList<FieldComparison> Comparisons { get; init; }

    /// <summary>The digest the snapshot holds for this boundary, when one was
    /// supplied.</summary>
    public required string? ExpectedDigest { get; init; }

    /// <summary>The digest of the state the live run actually reached.</summary>
    public required string ActualDigest { get; init; }

    /// <summary>Why this is not the recorded boundary, written for the player who is
    /// about to not get the fight. Null when it is.</summary>
    public required string? Refusal { get; init; }

    /// <summary>
    /// Compares one live canonical state against the recording's boundary.
    /// </summary>
    /// <param name="boundary">What the recording observed when the fight opened.</param>
    /// <param name="live">The canonical state the live run reached, field by field.</param>
    /// <param name="liveDigest">Digest over the whole of that state.</param>
    /// <param name="expectedDigest">
    /// The combat-start snapshot's digest, when the caller has one. Null is a real
    /// case rather than a shortcut - a host entering a fight for the first time has
    /// no snapshot to compare against yet - and it is reported rather than treated
    /// as agreement.
    /// </param>
    public static CombatStartEquality Compare(
        Checkpoint boundary,
        IReadOnlyDictionary<string, string> live,
        string liveDigest,
        string? expectedDigest)
    {
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
        var digestDisagrees = expectedDigest is { Length: > 0 } &&
                              !string.Equals(expectedDigest, liveDigest, StringComparison.Ordinal);

        return new CombatStartEquality
        {
            Matches = disagreements.Count == 0 && !digestDisagrees,
            Comparisons = comparisons,
            ExpectedDigest = expectedDigest,
            ActualDigest = liveDigest,
            Refusal = Refuse(boundary, disagreements, digestDisagrees, expectedDigest, liveDigest),
        };
    }

    private static string? Refuse(
        Checkpoint boundary,
        IReadOnlyList<FieldComparison> disagreements,
        bool digestDisagrees,
        string? expectedDigest,
        string liveDigest)
    {
        if (disagreements.Count == 0 && !digestDisagrees) return null;

        if (disagreements.Count > 0)
        {
            var listed = string.Join("; ", disagreements.Select(comparison =>
                $"{comparison.Field}: the recording shows '{comparison.Expected}', this game produced " +
                $"'{comparison.Actual}'"));
            return
                $"This fight did not open the way the recording's did, so it was not entered. At checkpoint " +
                $"'{boundary.Id}': {listed}. Something before the fight differed, and a fight that starts " +
                "somewhere else cannot be compared against the recording's.";
        }

        return
            "This fight opened with everything the recording shows and with different hidden state, so it was " +
            $"not entered. The recorded combat-start snapshot is {expectedDigest} and this game reached " +
            $"{liveDigest}. The difference is in state no video can show - a run-persistent random stream, or " +
            "the order of the draw pile - and a fight that starts there diverges from the recording's at the " +
            "next shuffle.";
    }
}

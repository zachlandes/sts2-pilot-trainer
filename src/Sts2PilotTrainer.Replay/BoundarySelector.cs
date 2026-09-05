using System.Globalization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Which boundary of a recording somebody asked for, written the way a person would
/// say it.
///
/// A boundary's coordinate is the kind's own, not a position in a list. The third
/// combat start is fight 3 and the third floor arrival is not floor 3 - the run
/// begins on a floor it never arrived at - so a single ordinal counted across the
/// list would mean a different thing per kind and be wrong exactly where it was
/// least visible. What each kind is identified by is already written down on
/// <see cref="ReplayBoundary"/>, and this reads that back:
///
/// <code>
///   combat_start:2      the start of fight 2
///   floor_entry:5       arrival on floor 5
///   turn_start:2.3      turn 3 of fight 2
/// </code>
///
/// A turn takes two numbers because a turn's coordinate is two numbers. Asking for
/// one is refused rather than resolved against the first fight, which would be a
/// confident answer to a question nobody asked.
/// </summary>
public sealed record BoundarySelector
{
    /// <summary>The boundary a command means when nobody said which: the start of the
    /// recording's first fight, which is what these commands were for while a
    /// recording had one boundary.</summary>
    public static readonly BoundarySelector FirstFight = new()
    {
        Kind = ReplayBoundary.CombatStartKind,
        Fight = 1,
    };

    public required string Kind { get; init; }

    public int? Fight { get; init; }

    public int? Floor { get; init; }

    public int? Turn { get; init; }

    /// <summary>How this reads back to a person, in the same words it was written in.</summary>
    public override string ToString() => Kind switch
    {
        ReplayBoundary.TurnStartKind => $"{Kind}:{Number(Fight)}.{Number(Turn)}",
        ReplayBoundary.FloorEntryKind => $"{Kind}:{Number(Floor)}",
        _ => $"{Kind}:{Number(Fight)}",
    };

    /// <summary>
    /// Reads a selector, or refuses in words that say what the form is.
    ///
    /// Every refusal names the whole grammar rather than the part that was wrong,
    /// because somebody who got the coordinate wrong is somebody who does not yet know
    /// what coordinates a boundary has.
    /// </summary>
    public static BoundarySelector Parse(string text)
    {
        var parts = text.Split(':');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            throw new ManifestException(
                $"'{text}' does not name a boundary. {Grammar}");
        }

        var kind = parts[0];
        if (!ReplayBoundary.Kinds.Contains(kind, StringComparer.Ordinal))
        {
            throw new ManifestException(
                $"'{kind}' is not a boundary kind. The kinds are {string.Join(", ", ReplayBoundary.Kinds)}, and " +
                "the set is closed because a host dispatches on it. " + Grammar);
        }

        var coordinate = parts[1];
        if (kind == ReplayBoundary.TurnStartKind)
        {
            var numbers = coordinate.Split('.');
            if (numbers.Length != 2)
            {
                throw new ManifestException(
                    $"'{text}' names a turn with {numbers.Length.ToString(CultureInfo.InvariantCulture)} " +
                    "number(s). A turn belongs to a fight, so it takes both: which fight, then which turn of " +
                    "it. " + Grammar);
            }

            return new BoundarySelector
            {
                Kind = kind,
                Fight = Ordinal(numbers[0], text),
                Turn = Ordinal(numbers[1], text),
            };
        }

        var ordinal = Ordinal(coordinate, text);
        return kind == ReplayBoundary.FloorEntryKind
            ? new BoundarySelector { Kind = kind, Floor = ordinal }
            : new BoundarySelector { Kind = kind, Fight = ordinal };
    }

    /// <summary>
    /// The boundary this names in a list, or null when the list has none.
    ///
    /// Selection only. Whether a list that lacks it is a defect depends on whose list
    /// it is - a recording that declares no such boundary and a build that reaches no
    /// such boundary are different findings - so the refusal is the caller's to write.
    /// </summary>
    public ReplayBoundary? In(IEnumerable<ReplayBoundary> boundaries) =>
        boundaries.FirstOrDefault(boundary =>
            string.Equals(boundary.Kind, Kind, StringComparison.Ordinal) &&
            boundary.Fight == Fight &&
            boundary.Floor == Floor &&
            boundary.Turn == Turn);

    /// <summary>The plan a host walks to reach this boundary, or a refusal for a kind
    /// no host enters. A turn is carried by the format so a later rewind has somewhere
    /// to land, and nothing in these phases stands anybody in one.</summary>
    public IBoundaryPlan PlanFor(ReplayManifest manifest) => Kind switch
    {
        ReplayBoundary.CombatStartKind => RecordedFightPlan.For(manifest, Fight!.Value),
        ReplayBoundary.FloorEntryKind => FloorEntryPlan.For(manifest, Floor!.Value),
        _ => throw new ManifestException(
            $"{this} is a turn boundary, and nothing here enters one. A turn's state is reached by playing " +
            "the fight from its start, which is where a host is stood; see docs/comparison-direction.md."),
    };

    private const string Grammar =
        "A boundary is written <kind>:<coordinate> - combat_start:2 for the start of fight 2, floor_entry:5 " +
        "for arrival on floor 5, turn_start:2.3 for turn 3 of fight 2.";

    /// <summary>
    /// The positive whole number this text spells, or null when it spells anything
    /// else. Every coordinate here and every boundary option elsewhere counts from 1,
    /// and each caller names what was being counted in its own refusal.
    /// </summary>
    public static int? PositiveOrdinal(string value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;

    private static int Ordinal(string value, string text) =>
        PositiveOrdinal(value)
            ?? throw new ManifestException(
                $"'{text}' numbers a boundary '{value}'. Boundaries are counted from 1. " + Grammar);

    private static string Number(int? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? "?";
}

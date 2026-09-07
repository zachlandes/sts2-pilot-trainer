namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// How rare a relic is, as the strip orders by it. Ranked rather than named, because
/// the strip's rule is an ordering and nothing here prints a rarity.
/// </summary>
public enum RelicRarity
{
    /// <summary>An ancient Boon relic, which heads the strip.</summary>
    Boon,

    Rare,
    Uncommon,
    Common,

    /// <summary>Nothing said what this relic is. It sorts last rather than being given
    /// a rarity, because a strip that guessed would be ordering by a claim nobody
    /// made.</summary>
    Unknown,
}

/// <summary>
/// The relics on a browser row: the few that are shown, and how many are not.
///
/// <para><b>Ordered by rarity and then by when it was found.</b> The Boon relic first,
/// marked with the game's own gold rarity frame, then the rest by rarity, and within a
/// rarity in the order the run picked them up. The row is for scanning, so it shows a
/// handful and counts the rest; the pane carries every relic in the order found, which
/// is the run-history screen's own order, and each one there is the game's own relic
/// node with the game's own tooltip.</para>
///
/// <para><b>The rarity comes from outside.</b> A relic reaches this layer as a model
/// id and nothing else, and how rare it is is the game's own answer out of its model
/// database - so the caller supplies it and this owns the rule. A relic nothing could
/// answer for is <see cref="RelicRarity.Unknown"/> and sorts last, which keeps the
/// ordering honest on a build that has since dropped a relic.</para>
/// </summary>
/// <param name="Shown">The relics on the strip, in the strip's order.</param>
/// <param name="More">How many the strip did not have room for. Zero draws nothing.</param>
public sealed record RelicStrip(IReadOnlyList<RunRelic> Shown, int More)
{
    /// <summary>How many relics a list row shows before it starts counting.</summary>
    public const int OnARow = 4;

    /// <summary>The muted "+{n}" at the strip's end, or null when nothing was left
    /// out.</summary>
    public string? MoreLabel => More > 0 ? LibraryCopy.MoreRelics(More) : null;

    /// <summary>The strip for a list row: the first few by the ordering rule, and a
    /// count of the rest.</summary>
    public static RelicStrip ForRow(
        IReadOnlyList<RunRelic> relics, Func<string, RelicRarity> rarityOf, int shown = OnARow)
    {
        if (shown < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(shown), shown, "A strip that shows nothing is not a strip.");
        }

        var ordered = Ordered(relics, rarityOf);
        return new RelicStrip(
            [.. ordered.Take(shown)], Math.Max(0, ordered.Count - shown));
    }

    /// <summary>Every relic, in the order the run found them, which is the order the
    /// game's own run-history pane shows them in. Nothing is left out and nothing is
    /// re-ordered: the pane is where a relic is read.</summary>
    public static IReadOnlyList<RunRelic> ForPane(IReadOnlyList<RunRelic> relics) =>
        [.. relics.OrderBy(relic => relic.FoundAt)];

    private static IReadOnlyList<RunRelic> Ordered(
        IReadOnlyList<RunRelic> relics, Func<string, RelicRarity> rarityOf) =>
    [
        .. relics
            .OrderBy(relic => (int)rarityOf(relic.Id))
            .ThenBy(relic => relic.FoundAt),
    ];
}

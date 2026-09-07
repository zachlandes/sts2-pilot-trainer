namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// The relic strip's ordering rule: rarity first with the Boon relic at the head, then
/// the order the run found them.
///
/// The rarity itself comes from outside, because a recording carries a model id and
/// nothing else. What is pinned here is the rule and the direction it fails in: a relic
/// nothing could rank sorts last rather than being given a rarity somebody would then
/// read as established.
/// </summary>
public sealed class RelicStripTests
{
    private static RunRelic Relic(string id, int foundAt) => new(id, foundAt);

    private static RelicRarity Rank(string id) => id switch
    {
        "boon" => RelicRarity.Boon,
        "rare" => RelicRarity.Rare,
        "uncommon" => RelicRarity.Uncommon,
        "common-early" or "common-late" => RelicRarity.Common,
        _ => RelicRarity.Unknown,
    };

    [Fact]
    public void TheStripIsOrderedByRarityWithTheBoonRelicFirst()
    {
        IReadOnlyList<RunRelic> relics =
        [
            Relic("common-early", 0), Relic("rare", 1), Relic("boon", 2), Relic("uncommon", 3),
        ];

        var strip = RelicStrip.ForRow(relics, Rank);

        Assert.Equal(["boon", "rare", "uncommon", "common-early"], strip.Shown.Select(relic => relic.Id));
        Assert.Equal(0, strip.More);
        Assert.Null(strip.MoreLabel);
    }

    /// <summary>Within a rarity, the order the run found them. Two commons are not
    /// interchangeable to somebody reading a row for a run they watched.</summary>
    [Fact]
    public void WithinARarityTheOrderIsWhenTheRunFoundThem()
    {
        IReadOnlyList<RunRelic> relics = [Relic("common-late", 7), Relic("common-early", 1)];

        Assert.Equal(
            ["common-early", "common-late"],
            RelicStrip.ForRow(relics, Rank).Shown.Select(relic => relic.Id));
    }

    /// <summary>
    /// A relic nothing could rank sorts last rather than being given a rarity. That is
    /// the honest answer for a recording made on a build that had a relic this one has
    /// dropped; guessing would put it in front of relics whose rarity somebody
    /// established.
    /// </summary>
    [Fact]
    public void ARelicNobodyCouldRankSortsLast()
    {
        IReadOnlyList<RunRelic> relics = [Relic("gone", 0), Relic("common-late", 9)];

        Assert.Equal(
            ["common-late", "gone"],
            RelicStrip.ForRow(relics, Rank).Shown.Select(relic => relic.Id));
    }

    /// <summary>The row shows a handful and counts the rest: it is for scanning, and
    /// reading a relic is the pane's job.</summary>
    [Fact]
    public void TheRowShowsAHandfulAndCountsTheRest()
    {
        IReadOnlyList<RunRelic> relics =
            [.. Enumerable.Range(0, 7).Select(index => Relic($"common-{index}", index))];

        var strip = RelicStrip.ForRow(relics, Rank);

        Assert.Equal(RelicStrip.OnARow, strip.Shown.Count);
        Assert.Equal(3, strip.More);
        Assert.Equal("+3", strip.MoreLabel);
    }

    /// <summary>The pane carries every relic in the order the run found them, which is
    /// the run-history screen's own order. Nothing is left out and nothing is
    /// re-ordered.</summary>
    [Fact]
    public void ThePaneCarriesEveryRelicInTheOrderFound()
    {
        IReadOnlyList<RunRelic> relics = [Relic("boon", 3), Relic("common-early", 0), Relic("rare", 1)];

        Assert.Equal(
            ["common-early", "rare", "boon"],
            RelicStrip.ForPane(relics).Select(relic => relic.Id));
    }

    [Fact]
    public void AStripThatShowsNothingIsNotAStrip()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RelicStrip.ForRow([], Rank, shown: 0));
    }
}

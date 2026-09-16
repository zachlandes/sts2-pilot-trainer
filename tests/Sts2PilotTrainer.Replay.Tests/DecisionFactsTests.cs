namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// What a recording projects to, and how a corpus is counted against a denominator,
/// on inputs written by hand and with no game: the projection reads the format's own
/// arguments, and the count is arithmetic over two lists somebody else produced.
/// </summary>
public sealed class DecisionFactsTests
{
    [Fact]
    public void EveryActionProjectsToItsVerbAndTheDiscriminatedOnesToTheirPoint()
    {
        var manifest = Fixtures.ValidManifest() with
        {
            Actions =
            [
                Fixtures.Action(0, ActionVerb.ChooseNeowBlessing, ("option_index", "0")),
                Fixtures.Action(1, ActionVerb.MapMove, ("act", "0"), ("row", "1"), ("column", "3")),
                Fixtures.Action(2, ActionVerb.ClaimReward, ("reward_type", "gold")),
                Fixtures.Action(3, ActionVerb.ClaimReward, ("reward_type", "relic"), ("relic_id", "RELIC.ANCHOR")),
                Fixtures.Action(4, ActionVerb.TakeCard, ("card_id", "CARD.BASH"), ("option_index", "1")),
                Fixtures.Action(5, ActionVerb.TakeCardRewardAlternative, ("option_id", "Skip"), ("option_index", "3")),
                Fixtures.Action(6, ActionVerb.ShopPurchase, ("kind", "potion"), ("option_index", "0"), ("potion_id", "POTION.FIRE")),
                Fixtures.Action(7, ActionVerb.ShopPurchase, ("kind", "card_removal")),
                Fixtures.Action(8, ActionVerb.ChooseRestSiteOption, ("option_id", "SMITH"), ("option_index", "1")),
                Fixtures.Action(9, ActionVerb.ChooseEventOption, ("event_id", "EVENT.BRAIN_LEECH"), ("option_index", "0")),
                Fixtures.Action(10, ActionVerb.ChooseEventOption, ("event_id", "EVENT.BRAIN_LEECH"), ("option_index", "1")),
            ],
        };

        var points = DecisionFacts.Of(manifest);

        Assert.Equal(
            new[]
            {
                "verb  ChooseNeowBlessing",
                "verb  MapMove",
                "verb  ClaimReward",
                "reward-kind  gold",
                "reward-kind  relic",
                "verb  TakeCard",
                "reward-kind  card",
                "verb  TakeCardRewardAlternative",
                "card-reward-alternative  Skip",
                "verb  ShopPurchase",
                "shop-kind  potion",
                "shop-kind  card_removal",
                "verb  ChooseRestSiteOption",
                "rest-option  SMITH",
                "verb  ChooseEventOption",
                "event  EVENT.BRAIN_LEECH",
            }.Order(StringComparer.Ordinal),
            points.Select(point => point.ToString()).Order(StringComparer.Ordinal));
    }

    /// <summary>The projection validates nothing: an action short of its discriminating
    /// argument still counts for its verb and for nothing else.</summary>
    [Fact]
    public void AnActionMissingItsDiscriminatorProjectsToItsVerbAlone()
    {
        var manifest = Fixtures.ValidManifest() with
        {
            Actions = [Fixtures.Action(0, ActionVerb.ClaimReward), Fixtures.Action(1, ActionVerb.ShopPurchase, ("kind", ""))],
        };

        Assert.Equal(
            ["verb  ClaimReward", "verb  ShopPurchase"],
            DecisionFacts.Of(manifest).Select(point => point.ToString()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryProjectableKindIsAKindAndTheTwoOthersSayWhyTheyAreNot()
    {
        Assert.All(DecisionKinds.Projectable, kind => Assert.Contains(kind, DecisionKinds.All));
        Assert.Equal(
            [DecisionKinds.CardPrompt, DecisionKinds.NetAction],
            DecisionKinds.All.Where(kind => !DecisionKinds.IsProjectable(kind)));
        Assert.All(DecisionKinds.Projectable, kind => Assert.Null(DecisionKinds.NotProjectableBecause(kind)));
        Assert.NotNull(DecisionKinds.NotProjectableBecause(DecisionKinds.CardPrompt));
        Assert.NotNull(DecisionKinds.NotProjectableBecause(DecisionKinds.NetAction));
    }

    // ── Counting a corpus against a denominator ──────────────────────────────────

    private static readonly DecisionPoint Gold = new(DecisionKinds.RewardKind, "gold");
    private static readonly DecisionPoint Relic = new(DecisionKinds.RewardKind, "relic");
    private static readonly DecisionPoint Potion = new(DecisionKinds.RewardKind, "potion");
    private static readonly DecisionPoint Prompt = new(DecisionKinds.CardPrompt, "CardSelectCmd.FromHand(context)");

    [Fact]
    public void EveryPointIsCountedCoveredExcusedUncoveredOrNotProjectable()
    {
        var report = DecisionCoverage.Over(
            [Gold, Relic, Potion, Prompt],
            new Dictionary<DecisionPoint, string> { [Relic] = "nobody has found one yet" },
            [
                new CoveredRecording("a", new HashSet<DecisionPoint> { Gold }),
                new CoveredRecording("b", new HashSet<DecisionPoint> { Gold }),
            ]);

        Assert.Equal(
            [
                "reward-kind  gold  2 recording(s)",
                "reward-kind  relic  excused: nobody has found one yet",
                "reward-kind  potion  uncovered",
                $"card-prompt  CardSelectCmd.FromHand(context)  {DecisionKinds.NotProjectableBecause(DecisionKinds.CardPrompt)}",
            ],
            report.Rows.Select(row => row.Describe()));
        Assert.Equal(4, report.Points);
        Assert.Equal(1, report.Covered);
        Assert.Equal(1, report.Excused);
        Assert.Equal(1, report.Uncovered);
        Assert.Equal(1, report.NotProjectable);
        Assert.Equal(2, report.Recordings);
        Assert.False(report.Holds);
        Assert.Contains("points: 4  covered: 1  excused: 1  uncovered: 1  not projectable: 1  recordings: 2", report.Totals());
    }

    [Fact]
    public void TheBarHoldsWhenNothingIsUncovered()
    {
        var report = DecisionCoverage.Over(
            [Gold, Relic, Prompt],
            new Dictionary<DecisionPoint, string> { [Relic] = "nobody has found one yet" },
            [new CoveredRecording("a", new HashSet<DecisionPoint> { Gold })]);

        Assert.True(report.Holds);
        Assert.Empty(report.OutsideTheDenominator);
        Assert.Empty(report.StaleExcusals);
    }

    /// <summary>A point a recording reached that no walk produced is a finding about
    /// the walk or the format, and fails the bar rather than vanishing.</summary>
    [Fact]
    public void APointOutsideTheDenominatorIsNamedAndFailsTheBar()
    {
        var report = DecisionCoverage.Over(
            [Gold],
            new Dictionary<DecisionPoint, string>(),
            [new CoveredRecording("a", new HashSet<DecisionPoint> { Gold, Potion })]);

        Assert.False(report.Holds);
        var outside = Assert.Single(report.OutsideTheDenominator);
        Assert.Equal(CoverageState.OutsideTheDenominator, outside.State);
        Assert.Equal("reward-kind  potion  1 recording(s), and no walk of this build produced the point", outside.Describe());
        Assert.Contains("outside the denominator: 1", report.Totals());
    }

    /// <summary>An excusal a recording has since reached, or one naming a point no walk
    /// produces, is stale and comes out; the point itself is counted as what it is.</summary>
    [Fact]
    public void AStaleExcusalIsNamedAndFailsTheBar()
    {
        var report = DecisionCoverage.Over(
            [Gold, Relic],
            new Dictionary<DecisionPoint, string> { [Gold] = "stale", [Potion] = "names nothing the build offers" },
            [new CoveredRecording("a", new HashSet<DecisionPoint> { Gold, Relic })]);

        Assert.False(report.Holds);
        Assert.Equal([Gold, Potion], report.StaleExcusals);
        Assert.Equal(CoverageState.Covered, report.Rows[0].State);
        Assert.Contains("stale excusals: 2", report.Totals());
    }
}

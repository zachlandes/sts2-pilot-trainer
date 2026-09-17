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

    /// <summary>A decision the game's own rollback undid was still reached and chosen:
    /// the discarded branch projects beside the continued history.</summary>
    [Fact]
    public void ADiscardedBranchsDecisionsProjectBesideTheContinuedHistory()
    {
        var native = Fixtures.NativeManifest();
        var manifest = native with
        {
            Actions = [Fixtures.Action(0, ActionVerb.ClaimReward, ("reward_type", "gold"))],
            Source = native.Source with
            {
                Native = native.Source.Native! with
                {
                    Discarded =
                    [
                        new DiscardedBranch
                        {
                            RollbackToSeq = 0,
                            RollbackToDigest = "abc",
                            Trace = new ReplayTrace { Steps = [] },
                            Actions =
                            [
                                Fixtures.Action(1, ActionVerb.ClaimReward, ("reward_type", "relic"), ("relic_id", "RELIC.ANCHOR")),
                                Fixtures.Action(2, ActionVerb.ChooseRestSiteOption, ("option_id", "SMITH")),
                            ],
                        },
                    ],
                },
            },
        };

        Assert.Equal(
            [
                "rest-option  SMITH",
                "reward-kind  gold",
                "reward-kind  relic",
                "verb  ChooseRestSiteOption",
                "verb  ClaimReward",
            ],
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
    private static readonly RecordingStanding Holds = RecordingStanding.Of(Fixtures.NativeSourceBlock());
    private static readonly RecordingStanding Broken =
        RecordingStanding.Of(Fixtures.NativeSourceBlock(continuity: NativeSource.BrokenContinuity));
    private static readonly RecordingStanding Unmapped =
        RecordingStanding.Of(Fixtures.NativeSourceBlock(integrity: NativeSource.UnmappedIntegrity));

    /// <summary>The standing is the reading parity makes of the same file: a video
    /// reconstruction and a rewound recording hold, the two the recorder refused do
    /// not, and each refusal says why in the recorder's terms.</summary>
    [Fact]
    public void AStandingIsReadOffWhatTheRecorderSaidOfTheRun()
    {
        Assert.True(RecordingStanding.Of(null).Holds);
        Assert.True(Holds.Holds);
        Assert.True(RecordingStanding.Of(Fixtures.NativeSourceBlock(continuity: NativeSource.RewoundContinuity)).Holds);
        Assert.Equal(RecordingStandingKind.ContinuityBroken, Broken.Kind);
        Assert.StartsWith("continuity is 'broken'", Broken.Detail, StringComparison.Ordinal);
        Assert.Equal(RecordingStandingKind.IntegrityNotComplete, Unmapped.Kind);
        Assert.StartsWith("integrity is 'unmapped'", Unmapped.Detail, StringComparison.Ordinal);
        Assert.Equal(
            RecordingStandingKind.IntegrityNotComplete,
            RecordingStanding.Of(Fixtures.NativeSourceBlock(integrity: NativeSource.NonStandardIntegrity)).Kind);
    }

    /// <summary>A recording the recorder says holds nothing credits no point: what it
    /// reached is tallied apart and printed beside the row, the row's state is what
    /// the crediting recordings say, and an excusal it reached is not stale.</summary>
    [Fact]
    public void AnUnverifiedRecordingIsTalliedApartAndCreditsNothing()
    {
        var report = DecisionCoverage.Over(
            [Gold, Relic, Potion],
            new Dictionary<DecisionPoint, string> { [Relic] = "nobody has found one yet" },
            [
                new CoveredRecording("a", new HashSet<DecisionPoint> { Gold }, Holds),
                new CoveredRecording("b", new HashSet<DecisionPoint> { Gold, Relic, Potion }, Broken),
                new CoveredRecording("c", new HashSet<DecisionPoint> { Potion }, Unmapped),
            ]);

        Assert.Equal(
            [
                "reward-kind  gold  1 recording(s); reached by 1 unverified recording(s), not credited",
                "reward-kind  relic  excused: nobody has found one yet; reached by 1 unverified recording(s), not credited",
                "reward-kind  potion  uncovered; reached by 2 unverified recording(s), not credited",
            ],
            report.Rows.Select(row => row.Describe()));
        Assert.Equal(1, report.Covered);
        Assert.Equal(1, report.Excused);
        Assert.Equal(1, report.Uncovered);
        Assert.Empty(report.StaleExcusals);
        Assert.Equal(["b", "c"], report.Unverified.Select(recording => recording.RunId));
        Assert.Equal(3, report.Recordings);
        Assert.Equal(1, report.CreditedRecordings);
        Assert.Contains("recordings credited: 1  unverified: 2  unreadable: 0", report.Totals());
        Assert.False(report.Holds);
    }

    /// <summary>A manifest this build could not read is in the corpus and counts for
    /// nothing, named with the parser's words rather than dropped from the figure.</summary>
    [Fact]
    public void AnUnreadableManifestIsCountedInTheCorpusAndCreditsNothing()
    {
        var report = DecisionCoverage.Over(
            [Gold],
            new Dictionary<DecisionPoint, string>(),
            [new CoveredRecording("a", new HashSet<DecisionPoint> { Gold }, Holds)],
            [new UnreadableRecording("junk.replay.json", "this build cannot read the manifest: not JSON")]);

        Assert.True(report.Holds);
        Assert.Equal(2, report.Recordings);
        Assert.Equal(1, report.CreditedRecordings);
        Assert.Equal("junk.replay.json", Assert.Single(report.Unreadable).Manifest);
        Assert.Contains("recordings credited: 1  unverified: 0  unreadable: 1", report.Totals());
    }

    [Fact]
    public void EveryPointIsCountedCoveredExcusedUncoveredOrNotProjectable()
    {
        var report = DecisionCoverage.Over(
            [Gold, Relic, Potion, Prompt],
            new Dictionary<DecisionPoint, string> { [Relic] = "nobody has found one yet" },
            [
                new CoveredRecording("a", new HashSet<DecisionPoint> { Gold }, Holds),
                new CoveredRecording("b", new HashSet<DecisionPoint> { Gold }, Holds),
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
            [new CoveredRecording("a", new HashSet<DecisionPoint> { Gold }, Holds)]);

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
            [new CoveredRecording("a", new HashSet<DecisionPoint> { Gold, Potion }, Holds)]);

        Assert.False(report.Holds);
        var outside = Assert.Single(report.OutsideTheDenominator);
        Assert.Equal(CoverageState.OutsideTheDenominator, outside.State);
        Assert.Equal("reward-kind  potion  1 recording(s), and no walk of this build produced the point", outside.Describe());
        Assert.Contains("outside the denominator: 1", report.Totals());
    }

    /// <summary>An excusal naming a point no walk produces is stale on any corpus and
    /// fails the bar; one a crediting recording reached is a fact about this corpus,
    /// named and not failed, and the point itself is counted as what it is.</summary>
    [Fact]
    public void AStaleExcusalFailsTheBarAndAReachedOneIsNamed()
    {
        var report = DecisionCoverage.Over(
            [Gold, Relic],
            new Dictionary<DecisionPoint, string> { [Gold] = "reached here", [Potion] = "names nothing the build offers" },
            [new CoveredRecording("a", new HashSet<DecisionPoint> { Gold, Relic }, Holds)]);

        Assert.False(report.Holds);
        Assert.Equal([Potion], report.StaleExcusals);
        Assert.Equal([Gold], report.ExcusedAndReached);
        Assert.Equal(CoverageState.Covered, report.Rows[0].State);
        Assert.Contains("stale excusals: 1", report.Totals());
        Assert.Contains("excused and reached by this corpus: 1", report.Totals());

        var reachedOnly = DecisionCoverage.Over(
            [Gold, Relic],
            new Dictionary<DecisionPoint, string> { [Gold] = "reached here" },
            [new CoveredRecording("a", new HashSet<DecisionPoint> { Gold, Relic }, Holds)]);
        Assert.True(reachedOnly.Holds);
    }
}

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
                Fixtures.Action(10, ActionVerb.ChooseEventOption, ("event_id", "EVENT.BRAIN_LEECH"), ("option_index", "1"), ("option_key", "BRAIN_LEECH.pages.INITIAL.options.RIP")),
                Fixtures.Action(11, ActionVerb.ChooseNeowBlessing, ("option_index", "1"), ("option_key", "RELIC.WINGED_BOOTS")),
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
                "event-option  EVENT.BRAIN_LEECH BRAIN_LEECH.pages.INITIAL.options.RIP",
                "event-option  EVENT.NEOW RELIC.WINGED_BOOTS",
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
    public void EveryProjectableKindIsAKindTheSeamIsReachedByCoOccurrenceAndTheTwoOthersSayWhyTheyAreNot()
    {
        Assert.All(DecisionKinds.Projectable, kind => Assert.Contains(kind, DecisionKinds.All));
        Assert.Equal(
            [DecisionKinds.Seam, DecisionKinds.CardPrompt, DecisionKinds.NetAction],
            DecisionKinds.All.Where(kind => !DecisionKinds.IsProjectable(kind)));
        Assert.True(DecisionKinds.IsReachedByCoOccurrence(DecisionKinds.Seam));
        Assert.Null(DecisionKinds.NotProjectableBecause(DecisionKinds.Seam));
        Assert.All(DecisionKinds.Projectable, kind => Assert.Null(DecisionKinds.NotProjectableBecause(kind)));
        Assert.NotNull(DecisionKinds.NotProjectableBecause(DecisionKinds.CardPrompt));
        Assert.NotNull(DecisionKinds.NotProjectableBecause(DecisionKinds.NetAction));
    }

    // ── Counting a corpus against a denominator ──────────────────────────────────

    private static Dictionary<DecisionPoint, Excusal> Excuses(params (DecisionPoint Point, string Reason)[] excusals) =>
        excusals.ToDictionary(excusal => excusal.Point, excusal => new Excusal(ExcusalClass.NotOnTheRoute, excusal.Reason));

    private static readonly DecisionPoint Gold = new(DecisionKinds.RewardKind, "gold");
    private static readonly DecisionPoint Relic = new(DecisionKinds.RewardKind, "relic");
    private static readonly DecisionPoint Potion = new(DecisionKinds.RewardKind, "potion");
    private static readonly DecisionPoint Prompt = new(DecisionKinds.CardPrompt, "CardSelectCmd.FromHand(context)");
    /// <summary>The build the fixtures record, as the build under test: the standing
    /// of every recording below is asked against it.</summary>
    private static readonly EnvironmentIdentity Environment = Fixtures.NativeManifest().Environment;
    private static readonly LocalBuild ThisBuild = new(
        Environment.BuildVersion.Value, Environment.BuildDateUtc.Value, Environment.ContentHash.Value);
    private static readonly RecordingStanding Holds = Standing(Fixtures.NativeSourceBlock());
    private static readonly RecordingStanding Broken =
        Standing(Fixtures.NativeSourceBlock(continuity: NativeSource.BrokenContinuity));
    private static readonly RecordingStanding Unmapped =
        Standing(Fixtures.NativeSourceBlock(integrity: NativeSource.UnmappedIntegrity));
    private static readonly RecordingStanding OtherBuild =
        RecordingStanding.Of(Fixtures.NativeSourceBlock(), Environment, ThisBuild with { BuildVersion = "v0.112.0" }, ThisRecorder);

    /// <summary>The recorder the fixtures name themselves with, as the recorder under
    /// test: the standing of every recording below is asked against it.</summary>
    private const string ThisRecorder = "0.1.0";

    private static RecordingStanding Standing(NativeSource? native) =>
        RecordingStanding.Of(native, Environment, ThisBuild, ThisRecorder);

    private static RecordingStanding WrittenBy(string? recorderVersion) =>
        Standing(Fixtures.NativeSourceBlock() with { RecorderVersion = recorderVersion! });

    /// <summary>The standing is the reading parity makes of the same file: a video
    /// reconstruction and a rewound recording hold, the two the recorder refused do
    /// not, and each refusal says why in the recorder's terms.</summary>
    [Fact]
    public void AStandingIsReadOffWhatTheRecorderSaidOfTheRun()
    {
        Assert.True(Standing(null).Holds);
        Assert.True(Holds.Holds);
        Assert.True(Standing(Fixtures.NativeSourceBlock(continuity: NativeSource.RewoundContinuity)).Holds);
        Assert.Equal(RecordingStandingKind.ContinuityBroken, Broken.Kind);
        Assert.StartsWith("continuity is 'broken'", Broken.Detail, StringComparison.Ordinal);
        Assert.Equal(RecordingStandingKind.IntegrityNotComplete, Unmapped.Kind);
        Assert.StartsWith("integrity is 'unmapped'", Unmapped.Detail, StringComparison.Ordinal);
        Assert.Equal(
            RecordingStandingKind.IntegrityNotComplete,
            Standing(Fixtures.NativeSourceBlock(integrity: NativeSource.NonStandardIntegrity)).Kind);
        Assert.Equal(["build_version", "build_date_utc", "content_hash"], Holds.Build.Select(field => field.Field));
        Assert.All(Holds.Build, field => Assert.True(field.Matches));
    }

    /// <summary>
    /// A recording of another build holds nothing whatever the recorder said of the
    /// run, a reconstruction included, and is refused on the preflight's own three
    /// fields in the preflight's own words - the sentence <c>replay</c> refuses the
    /// same file with - so the two numbers and the arbiter cannot disagree about which
    /// build a recording is evidence about.
    /// </summary>
    [Fact]
    public void ARecordingOfAnotherBuildHoldsNothingInThePreflightsOwnWords()
    {
        Assert.Equal(RecordingStandingKind.AnotherBuild, OtherBuild.Kind);
        Assert.False(OtherBuild.Holds);
        var expected = EnvironmentPreflight.Build(Environment, ThisBuild with { BuildVersion = "v0.112.0" });
        Assert.Equal(expected, OtherBuild.Build);
        Assert.Equal(expected.Single(field => !field.Matches).Refusal, OtherBuild.Detail);
        Assert.StartsWith(
            "build_version: manifest says 'v0.111.0', this machine has 'v0.112.0'. Replaying on a different build",
            OtherBuild.Detail, StringComparison.Ordinal);

        // Each mismatching field is its own line, and the build outranks the recorder's
        // own account: a broken recording of another build is another build's
        var twoFields = RecordingStanding.Of(
            Fixtures.NativeSourceBlock(continuity: NativeSource.BrokenContinuity), Environment,
            ThisBuild with { BuildVersion = "v0.112.0", ContentHash = "999999999" }, ThisRecorder);
        Assert.Equal(RecordingStandingKind.AnotherBuild, twoFields.Kind);
        Assert.Equal(
            ["build_version: manifest says 'v0.111.0', this machine has 'v0.112.0'. ", "content_hash: manifest says '1568834832', this machine has '999999999'. "],
            twoFields.Detail.Split('\n').Select(line => line[..(line.IndexOf(". ", StringComparison.Ordinal) + 2)]));
        Assert.Equal(
            RecordingStandingKind.AnotherBuild,
            RecordingStanding.Of(null, Environment, ThisBuild with { BuildDateUtc = "2026.08.15" }, ThisRecorder).Kind);
        Assert.False(new CoveredRecording("other", new HashSet<DecisionPoint> { Gold }, OtherBuild).Credits);
    }

    /// <summary>
    /// A recording an older recorder wrote holds nothing for parity and still credits
    /// coverage: its journal is what that recorder got wrong, its manifest replays on
    /// this build all the same. Below is older; equal holds; a version the recording
    /// does not carry, one that does not parse, and the unstamped default a recorder
    /// built before the version was stamped named itself with are all older, never
    /// holding. The recorder is asked last, so a broken or unmapped recording of an
    /// older recorder is still the recorder's own refusal and credits nothing.
    /// </summary>
    [Fact]
    public void ARecordingOfAnOlderRecorderHoldsNothingForParityAndStillCreditsCoverage()
    {
        var older = WrittenBy("runmobile-recorder/0.0.9");
        Assert.Equal(RecordingStandingKind.OlderRecorder, older.Kind);
        Assert.False(older.Holds);
        Assert.True(older.CreditsCoverage);
        Assert.Equal(
            "journal written by recorder 'runmobile-recorder/0.0.9'; this build's recorder is " +
            "'runmobile-recorder/0.1.0', and what changed between them is why the journal is not held to a " +
            "replay. The manifest still replays on this build, so what it reached is credited to coverage",
            older.Detail);
        Assert.True(new CoveredRecording("older", new HashSet<DecisionPoint> { Gold }, older).Credits);

        Assert.True(WrittenBy("runmobile-recorder/0.1.0").Holds);
        Assert.True(WrittenBy("runmobile-recorder/0.1.1").Holds);
        Assert.True(Standing(Fixtures.NativeSourceBlock()).CreditsCoverage);

        foreach (var unreadable in new[] { "runmobile-recorder/1.0.0.0", "runmobile-recorder/fixture", "0.1.0", "", null })
        {
            var standing = WrittenBy(unreadable);
            Assert.Equal(RecordingStandingKind.OlderRecorder, standing.Kind);
            Assert.True(standing.CreditsCoverage);
        }

        Assert.Equal(
            RecordingStandingKind.ContinuityBroken,
            Standing(Fixtures.NativeSourceBlock(continuity: NativeSource.BrokenContinuity) with { RecorderVersion = "runmobile-recorder/0.0.9" }).Kind);
        Assert.Equal(
            RecordingStandingKind.IntegrityNotComplete,
            Standing(Fixtures.NativeSourceBlock(integrity: NativeSource.UnmappedIntegrity) with { RecorderVersion = "runmobile-recorder/0.0.9" }).Kind);
        Assert.False(Broken.CreditsCoverage);
        Assert.False(Unmapped.CreditsCoverage);
        Assert.False(OtherBuild.CreditsCoverage);
        Assert.True(Standing(null).CreditsCoverage);

        // The build under test names its own recorder the way Runmobile.json spells it,
        // and a string that is not one is a defect in the caller rather than a standing
        Assert.Throws<ArgumentException>(() => RecordingStanding.Of(null, Environment, ThisBuild, "fixture"));
    }

    /// <summary>A recording the recorder says holds nothing credits no point: what it
    /// reached is tallied apart and printed beside the row, the row's state is what
    /// the crediting recordings say, and an excusal it reached is not stale.</summary>
    [Fact]
    public void AnUnverifiedRecordingIsTalliedApartAndCreditsNothing()
    {
        var report = DecisionCoverage.Over(
            [Gold, Relic, Potion],
            Excuses((Relic, "nobody has found one yet")),
            [
                new CoveredRecording("a", new HashSet<DecisionPoint> { Gold }, Holds),
                new CoveredRecording("b", new HashSet<DecisionPoint> { Gold, Relic, Potion }, Broken),
                new CoveredRecording("c", new HashSet<DecisionPoint> { Potion }, Unmapped),
            ]);

        Assert.Equal(
            [
                "reward-kind  gold  1 recording(s); reached by 1 unverified recording(s), not credited",
                "reward-kind  relic  excused [not-on-the-route]: nobody has found one yet; reached by 1 unverified recording(s), not credited",
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
            Excuses(),
            [new CoveredRecording("a", new HashSet<DecisionPoint> { Gold }, Holds)],
            unreadable: [new UnreadableRecording("junk.replay.json", "this build cannot read the manifest: not JSON")]);

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
            Excuses((Relic, "nobody has found one yet")),
            [
                new CoveredRecording("a", new HashSet<DecisionPoint> { Gold }, Holds),
                new CoveredRecording("b", new HashSet<DecisionPoint> { Gold }, Holds),
            ]);

        Assert.Equal(
            [
                "reward-kind  gold  2 recording(s)",
                "reward-kind  relic  excused [not-on-the-route]: nobody has found one yet",
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
        Assert.Contains("points: 4  covered: 1  co-occurrence: 0  excused: 1  uncovered: 1  not projectable: 1  recordings: 2", report.Totals());
    }

    [Fact]
    public void TheBarHoldsWhenNothingIsUncovered()
    {
        var report = DecisionCoverage.Over(
            [Gold, Relic, Prompt],
            Excuses((Relic, "nobody has found one yet")),
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
            Excuses(),
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
            Excuses((Gold, "reached here"), (Potion, "names nothing the build offers")),
            [new CoveredRecording("a", new HashSet<DecisionPoint> { Gold, Relic }, Holds)]);

        Assert.False(report.Holds);
        Assert.Equal([Potion], report.StaleExcusals);
        Assert.Equal([Gold], report.ExcusedAndReached);
        Assert.Equal(CoverageState.Covered, report.Rows[0].State);
        Assert.Contains("stale excusals: 1", report.Totals());
        Assert.Contains("excused and reached by this corpus: 1", report.Totals());

        var reachedOnly = DecisionCoverage.Over(
            [Gold, Relic],
            Excuses((Gold, "reached here")),
            [new CoveredRecording("a", new HashSet<DecisionPoint> { Gold, Relic }, Holds)]);
        Assert.True(reachedOnly.Holds);
    }

    /// <summary>The models a recording met are read off every sampled value and every
    /// argument by their spelling: a card before its upgrade mark, a power before its
    /// amount, a relic an option dealt, the event itself.</summary>
    [Fact]
    public void TheModelsARecordingMetAreReadOffItsSamplesAndItsArguments()
    {
        var manifest = Fixtures.ValidManifest() with
        {
            Actions =
            [
                Fixtures.Action(0, ActionVerb.ChooseNeowBlessing, ("option_index", "1"), ("option_key", "RELIC.WINGED_BOOTS")),
                Fixtures.Action(1, ActionVerb.PlayCard, ("card_id", "CARD.TRUE_GRIT"), ("hand_index", "0")),
                Fixtures.Action(2, ActionVerb.ChooseEventOption, ("event_id", "EVENT.BRAIN_LEECH"), ("option_index", "0")),
            ],
            Checkpoints =
            [
                new Checkpoint
                {
                    Id = "fight-1-start",
                    AfterSeq = 1,
                    Kind = "combat_start",
                    Expect = new Dictionary<string, Fact<string>>
                    {
                        ["player.deck"] = Fact<string>.Engine("CARD.STRIKE_IRONCLAD+1|CARD.BASH"),
                        ["player.relics"] = Fact<string>.Engine("RELIC.BURNING_BLOOD|RELIC.ARCANE_SCROLL"),
                        ["player.potions"] = Fact<string>.Engine("empty|POTION.FIRE|empty"),
                        ["combat.player_powers"] = Fact<string>.Engine("POWER.FRAIL_POWER:2"),
                        ["combat.enemy.0.model"] = Fact<string>.Engine("MONSTER.FUZZY_WURM_CRAWLER"),
                        ["combat.player_hp"] = Fact<string>.Engine("72"),
                    },
                },
            ],
        };

        Assert.Equal(
            [
                "CARD.BASH", "CARD.STRIKE_IRONCLAD", "CARD.TRUE_GRIT", "EVENT.BRAIN_LEECH", "MONSTER.FUZZY_WURM_CRAWLER",
                "POTION.FIRE", "POWER.FRAIL_POWER", "RELIC.ARCANE_SCROLL", "RELIC.BURNING_BLOOD", "RELIC.WINGED_BOOTS",
            ],
            DecisionFacts.ModelsMet(manifest).Order(StringComparer.Ordinal));
    }

    /// <summary>A seam is reached by co-occurrence: a crediting recording that met one
    /// of its producers and answered one of the points it is answered at. Either alone
    /// reaches nothing, an unverified recording credits nothing, and the row says
    /// co-occurrence rather than covered.</summary>
    [Fact]
    public void ASeamIsReachedByCoOccurrenceAndNeverCalledCovered()
    {
        var seam = new ProducerSeam("reward-kind:gold", "AbstractModel.TryModifyRewards", ["RELIC.AMETHYST_AUBERGINE"], [Gold]);
        var report = DecisionCoverage.Over(
            [Gold, Relic, seam.Point],
            Excuses(),
            [
                new CoveredRecording("met and answered", new HashSet<DecisionPoint> { Gold }, Holds, new HashSet<string> { "RELIC.AMETHYST_AUBERGINE" }),
                new CoveredRecording("answered only", new HashSet<DecisionPoint> { Gold }, Holds, new HashSet<string> { "RELIC.ANCHOR" }),
                new CoveredRecording("met only", new HashSet<DecisionPoint> { Relic }, Holds, new HashSet<string> { "RELIC.AMETHYST_AUBERGINE" }),
                new CoveredRecording("broken", new HashSet<DecisionPoint> { Gold }, Broken, new HashSet<string> { "RELIC.AMETHYST_AUBERGINE" }),
            ],
            [seam]);

        Assert.Equal(
            [
                "reward-kind  gold  2 recording(s); reached by 1 unverified recording(s), not credited",
                "reward-kind  relic  1 recording(s)",
                "seam  reward-kind:gold @ AbstractModel.TryModifyRewards  co-occurrence in 1 recording(s); reached by 1 unverified recording(s), not credited",
            ],
            report.Rows.Select(row => row.Describe()));
        Assert.Equal(1, report.CoOccurrence);
        Assert.Equal(2, report.Covered);
        Assert.True(report.Holds);
        Assert.Contains("points: 3  covered: 2  co-occurrence: 1  excused: 0  uncovered: 0  not projectable: 0  recordings: 4", report.Totals());
    }

    /// <summary>An excusal is held to the classes the map admits for its point: a
    /// placeholder where the map derives a class, or a derived class the map does not
    /// derive, is named with what the map admits and fails the bar; one in an admitted
    /// class stands.</summary>
    [Fact]
    public void AnExcusalInAClassTheMapDoesNotAdmitIsNamedAndFailsTheBar()
    {
        var admissible = new Dictionary<DecisionPoint, IReadOnlySet<ExcusalClass>>
        {
            [Gold] = new HashSet<ExcusalClass> { ExcusalClass.NoProducerOnThisBuild, ExcusalClass.Generated },
            [Relic] = new HashSet<ExcusalClass> { ExcusalClass.Generated, ExcusalClass.NotOnTheRoute },
            [Potion] = new HashSet<ExcusalClass> { ExcusalClass.Generated, ExcusalClass.NotOnTheRoute },
        };
        var excusals = new Dictionary<DecisionPoint, Excusal>
        {
            [Gold] = new(ExcusalClass.NotOnTheRoute, "a placeholder where nothing produces the point"),
            [Relic] = new(ExcusalClass.MultiplayerOnly, "a derived class the map does not derive"),
            [Potion] = new(ExcusalClass.NotOnTheRoute, "admitted"),
        };

        var report = DecisionCoverage.Over([Gold, Relic, Potion], excusals, [], admissible: admissible);

        Assert.False(report.Holds);
        Assert.Equal(3, report.Excused);
        Assert.Equal(
            [
                "reward-kind  gold  excused as not-on-the-route, and the map admits no-producer-on-this-build, generated",
                "reward-kind  relic  excused as multiplayer-only, and the map admits generated, not-on-the-route",
            ],
            report.InadmissibleExcusals.Select(excusal => excusal.Describe()));
        Assert.Contains("inadmissible excusals: 2", report.Totals());
        Assert.Equal("excused [not-on-the-route]: admitted", report.Rows[2].Excuse!.Describe());

        var withoutTheMap = DecisionCoverage.Over([Gold, Relic, Potion], excusals, []);
        Assert.True(withoutTheMap.Holds);
    }

    /// <summary>An excusal is held to the producers the map lists for its point: a
    /// seam's own producers, or for any other point those of every seam answered at
    /// it. One naming a producer the map does not list there is named with what the
    /// map lists instead and fails the bar, because its class may still be admitted
    /// while the sentence about who produces the point has gone false; one naming a
    /// listed producer, or none, stands. Two excusals are equal by the producers they
    /// name as well as by class and reason.</summary>
    [Fact]
    public void AnExcusalNamingAProducerTheMapDoesNotListIsNamedAndFailsTheBar()
    {
        var goldSeam = new ProducerSeam("reward-kind:gold", "AbstractModel.TryModifyRewards", ["RELIC.AMETHYST_AUBERGINE"], [Gold]);
        var relicSeam = new ProducerSeam("reward-kind:relic", "AbstractModel.BeforeDeath", ["POWER.SWIPE_POWER"], [Relic]);
        var excusals = new Dictionary<DecisionPoint, Excusal>
        {
            [Gold] = new(ExcusalClass.NotOnTheRoute, "names the producer the map lists", ["RELIC.AMETHYST_AUBERGINE"]),
            [Relic] = new(ExcusalClass.NotOnTheRoute, "names another build's producer", ["RELIC.ANCHOR", "POWER.SWIPE_POWER"]),
            [relicSeam.Point] = new(ExcusalClass.Generated, "names a producer of another seam", ["RELIC.AMETHYST_AUBERGINE"]),
            [Potion] = new(ExcusalClass.NotOnTheRoute, "names a producer where the map lists none", ["POTION.FIRE"]),
            [goldSeam.Point] = new(ExcusalClass.NotOnTheRoute, "names none"),
        };

        var report = DecisionCoverage.Over([Gold, Relic, Potion, goldSeam.Point, relicSeam.Point], excusals, [], [goldSeam, relicSeam]);

        Assert.False(report.Holds);
        Assert.Equal(5, report.Excused);
        Assert.Equal(
            [
                "reward-kind  potion  excused naming POTION.FIRE, and the map lists no producer here",
                "reward-kind  relic  excused naming RELIC.ANCHOR, and the map lists POWER.SWIPE_POWER",
                "seam  reward-kind:relic @ AbstractModel.BeforeDeath  excused naming RELIC.AMETHYST_AUBERGINE, and the map lists POWER.SWIPE_POWER",
            ],
            report.MisnamedProducers.Select(producer => producer.Describe()));
        Assert.Contains("misnamed producers: 3", report.Totals());
        Assert.Equal(
            "excused [not-on-the-route; names RELIC.ANCHOR, POWER.SWIPE_POWER]: names another build's producer",
            report.Rows[1].Excuse!.Describe());
        Assert.Equal("excused [not-on-the-route]: names none", report.Rows[3].Excuse!.Describe());
        Assert.Equal(["RELIC.AMETHYST_AUBERGINE"], DecisionCoverage.ProducersListedAt(Gold, [goldSeam, relicSeam]));
        Assert.Equal(["POWER.SWIPE_POWER"], DecisionCoverage.ProducersListedAt(relicSeam.Point, [goldSeam, relicSeam]));
        Assert.Empty(DecisionCoverage.ProducersListedAt(Potion, [goldSeam, relicSeam]));

        var withoutTheMap = DecisionCoverage.Over([Gold, Relic, Potion, goldSeam.Point, relicSeam.Point], excusals, []);
        Assert.True(withoutTheMap.Holds);

        Assert.Equal(
            new Excusal(ExcusalClass.NotOnTheRoute, "same", ["RELIC.ANCHOR"]),
            new Excusal(ExcusalClass.NotOnTheRoute, "same", ["RELIC.ANCHOR"]));
        Assert.NotEqual(
            new Excusal(ExcusalClass.NotOnTheRoute, "same", ["RELIC.ANCHOR"]),
            new Excusal(ExcusalClass.NotOnTheRoute, "same"));
        Assert.Equal(new Excusal(ExcusalClass.NotOnTheRoute, "same"), new Excusal(ExcusalClass.NotOnTheRoute, "same", []));
    }

    [Fact]
    public void EveryExcusalClassHasANameAndIsDerivedOrUndeclared()
    {
        var classes = Enum.GetValues<ExcusalClass>();

        Assert.Equal(classes.Length, classes.Select(ExcusalClasses.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(classes.Order(), ExcusalClasses.Derived.Concat(ExcusalClasses.Undeclared).Order());
        Assert.All(ExcusalClasses.Derived, excusalClass => Assert.True(ExcusalClasses.IsDerived(excusalClass)));
        Assert.All(ExcusalClasses.Undeclared, excusalClass => Assert.False(ExcusalClasses.IsDerived(excusalClass)));
    }
}

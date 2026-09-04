using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The plan a host walks to reach the recording's fight, and the proof it is asked
/// for at the end of it. Both are pure, so both are tested on a machine with no
/// game installed.
/// </summary>
public sealed class RecordedFightPlanTests
{
    [Fact]
    public void TheDecisionsBeforeTheFightAreTheOnesTheRecordingMadeBeforeIt()
    {
        var plan = RecordedFightPlan.For(EntryFixtures.Recording());

        Assert.Equal([0, 1], plan.PrefightActions.Select(action => action.Seq));
        Assert.Equal(1, plan.CombatStartSeq);
        Assert.Equal("combat-start", plan.Boundary.Id);
    }

    /// <summary>
    /// The boundary is where the engine enters combat, and the manifest's own
    /// checkpoint has to be bound to the same place. A plan that put it anywhere else
    /// would prove the fight matched a moment nobody observed.
    /// </summary>
    [Fact]
    public void TheSnapshotKeyCoversExactlyTheDecisionsBeforeTheFight()
    {
        var recording = EntryFixtures.Recording();
        var plan = RecordedFightPlan.For(recording);

        Assert.Equal(
            SnapshotCacheKey.HashActions(recording.Actions.Where(action => action.Seq <= 1)),
            plan.SnapshotKey.ActionHistoryHash);
        Assert.Equal(1, plan.SnapshotKey.UpToSeq);
    }

    [Fact]
    public void RefusesAHistoryThatNeverReachesAFight()
    {
        var recording = EntryFixtures.Recording() with
        {
            Actions = EntryFixtures.Recording().Actions.Where(action => action.Seq <= 1).ToList(),
            Boundaries = [],
        };

        var refusal = Assert.Throws<ManifestException>(() => RecordedFightPlan.For(recording));
        Assert.Contains("never reaches a fight", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAHistoryThatStartsInsideAFight()
    {
        var recording = EntryFixtures.Recording() with
        {
            Actions = [EntryFixtures.Play(0)],
            Boundaries = [],
        };

        var refusal = Assert.Throws<ManifestException>(() => RecordedFightPlan.For(recording));
        Assert.Contains("already inside a fight", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole point of the boundary is that a live entry can be compared against
    /// it. A recording that reached a fight and wrote down nothing it saw there would
    /// put a player in an unfalsifiable one.
    /// </summary>
    [Fact]
    public void RefusesAFightTheRecordingObservedNothingAt()
    {
        var recording = EntryFixtures.Recording() with { Checkpoints = [] };

        var refusal = Assert.Throws<ManifestException>(() => RecordedFightPlan.For(recording));
        Assert.Contains("records nothing it observed there", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A checkpoint at the boundary that says nothing about a fight is not
    /// an observation of one starting.</summary>
    [Fact]
    public void RefusesABoundaryCheckpointThatNamesNoCombatField()
    {
        var recording = EntryFixtures.Recording() with
        {
            Checkpoints = [EntryFixtures.Boundary(new Dictionary<string, Fact<string>>
            {
                ["player.gold"] = Fact<string>.Observed("99", FactEvidence.AtVideoTime(75600, "coin counter")),
            })],
        };

        var refusal = Assert.Throws<ManifestException>(() => RecordedFightPlan.For(recording));
        Assert.Contains("records nothing it observed there", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A later fight is found by its declared boundary, not by the shape of the
    /// history. The first-combat-verb rule can only ever find the first fight, and a
    /// plan that guessed where the third began would stand a player somewhere nobody
    /// measured.
    /// </summary>
    [Theory]
    [InlineData(2, 3)]
    [InlineData(3, 6)]
    public void ReachesALaterFightByItsDeclaredBoundary(int fight, int combatStartSeq)
    {
        var plan = RecordedFightPlan.For(EntryFixtures.WholeRun(), fight);

        Assert.Equal(fight, plan.Fight);
        Assert.Equal(combatStartSeq, plan.CombatStartSeq);
        Assert.Equal(combatStartSeq + 1, plan.PrefightActions.Count);
        Assert.Equal(combatStartSeq, plan.PrefightActions[^1].Seq);
        Assert.Equal($"combat-start-{fight.ToString(System.Globalization.CultureInfo.InvariantCulture)}", plan.Boundary.Id);
    }

    [Fact]
    public void RefusesAFightTheRecordingDeclaresNoBoundaryFor()
    {
        var refusal = Assert.Throws<ManifestException>(
            () => RecordedFightPlan.For(EntryFixtures.WholeRun(), 4));

        Assert.Contains("declares no combat-start boundary for fight 4", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAFightOrdinalThatIsNotAFight()
    {
        var refusal = Assert.Throws<ManifestException>(
            () => RecordedFightPlan.For(EntryFixtures.WholeRun(), 0));

        Assert.Contains("Fights are numbered from 1", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTheStepAtThisPointInTheJourneyIsAuthorised()
    {
        var plan = RecordedFightPlan.For(EntryFixtures.Recording());

        Assert.True(plan.Authorises(0, plan.PrefightActions[0]));
        Assert.True(plan.Authorises(1, plan.PrefightActions[1]));

        // The recording's own second decision, offered first. Still refused: the
        // order is the run, not a preference.
        Assert.False(plan.Authorises(0, plan.PrefightActions[1]));
        Assert.False(plan.Authorises(2, plan.PrefightActions[1]));
        Assert.False(plan.Authorises(-1, plan.PrefightActions[0]));
    }
}

/// <summary>
/// The plan a host walks to reach the moment a recording arrived on a floor. The
/// same shape as a fight's plan and a different boundary: none of the combat fields
/// exist yet, so what proves the arrival is where the run stands.
/// </summary>
public sealed class FloorEntryPlanTests
{
    [Fact]
    public void TheDecisionsBeforeTheFloorAreTheOnesTheRecordingMadeBeforeIt()
    {
        var plan = FloorEntryPlan.For(EntryFixtures.WholeRun(), 2);

        Assert.Equal(2, plan.Floor);
        Assert.Equal(3, plan.FloorEntrySeq);
        Assert.Equal([0, 1, 2, 3], plan.PrefixActions.Select(action => action.Seq));
        Assert.Equal("floor-entry-2", plan.Boundary.Id);
        Assert.Equal(3, plan.SnapshotKey.UpToSeq);
    }

    [Fact]
    public void RefusesAFloorTheRecordingDeclaresNoBoundaryFor()
    {
        var refusal = Assert.Throws<ManifestException>(() => FloorEntryPlan.For(EntryFixtures.WholeRun(), 9));

        Assert.Contains("declares no floor-entry boundary for floor 9", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A floor is arrived on by moving on the map. A boundary pointing at any
    /// other action names a moment that is not the one it claims.</summary>
    [Fact]
    public void RefusesABoundaryThatDoesNotNameAMapMove()
    {
        var recording = EntryFixtures.WholeRun();
        var moved = recording with
        {
            Boundaries =
            [
                .. recording.Boundaries.Where(boundary => !boundary.IsFloorEntry),
                ReplayBoundary.FloorEntry(2, 2, Fact<string>.Engine(Fixtures.Digest)),
            ],
        };

        var refusal = Assert.Throws<ManifestException>(() => FloorEntryPlan.For(moved, 2));

        Assert.Contains("a floor is arrived on by moving on the map", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The combat fields the fight plan looks for do not exist on arrival, so a
    /// checkpoint carrying only those is not an observation of arriving anywhere.
    /// </summary>
    [Fact]
    public void RefusesAFloorTheRecordingObservedNothingPlacingAt()
    {
        var recording = EntryFixtures.WholeRun();
        var withoutPlace = recording with
        {
            Checkpoints = [.. recording.Checkpoints.Where(checkpoint => checkpoint.Id != "floor-entry-2")],
        };

        var refusal = Assert.Throws<ManifestException>(() => FloorEntryPlan.For(withoutPlace, 2));

        Assert.Contains("run.total_floor and run.map_coord", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesACheckpointWhoseFloorDisagreesWithTheBoundary()
    {
        var recording = EntryFixtures.WholeRun();
        var wrongFloor = recording with
        {
            Checkpoints =
            [
                .. recording.Checkpoints.Select(checkpoint => checkpoint.Id == "floor-entry-2"
                    ? checkpoint with
                    {
                        Expect = checkpoint.Expect.ToDictionary(
                            entry => entry.Key,
                            entry => entry.Key == "run.total_floor"
                                ? Fact<string>.Observed("8", FactEvidence.AtVideoTime(75600, "floor counter"))
                                : entry.Value,
                            StringComparer.Ordinal),
                    }
                    : checkpoint),
            ],
        };

        var refusal = Assert.Throws<ManifestException>(() => FloorEntryPlan.For(wrongFloor, 2));

        Assert.Contains("boundary for floor 2", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("run.total_floor is 8", refusal.Message, StringComparison.Ordinal);
    }
}

/// <summary>
/// The kind-aware boundary comparison. One rule for every kind, and the sentence a
/// refusal is written in chosen by kind, because what a player is being told they
/// did not get differs.
/// </summary>
public sealed class BoundaryEqualityTests
{
    private static readonly Dictionary<string, string> Live = new(StringComparer.Ordinal)
    {
        ["run.total_floor"] = "2",
        ["run.map_coord"] = "r1c3",
    };

    private static Checkpoint FloorBoundary(string floor = "2", string coord = "r1c3") => new()
    {
        Id = "floor-entry-2",
        AfterSeq = 3,
        Kind = "floor_entry",
        Expect = new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
        {
            ["run.total_floor"] = Fact<string>.Observed(floor, FactEvidence.AtVideoTime(90000, "floor counter")),
            ["run.map_coord"] = Fact<string>.Observed(coord, FactEvidence.AtVideoTime(90000, "ringed node")),
        },
    };

    [Fact]
    public void AgreesOnAFloorArrivalWhenEveryReadingAgrees()
    {
        var equality = BoundaryEquality.Compare(
            ReplayBoundary.FloorEntryKind, FloorBoundary(), Live, "sha256:abc", "sha256:abc");

        Assert.True(equality.Matches);
        Assert.Equal(ReplayBoundary.FloorEntryKind, equality.Kind);
        Assert.Null(equality.Refusal);
    }

    /// <summary>The refusal is written for the moment it is about: nobody arriving on
    /// a floor is told a fight opened differently.</summary>
    [Fact]
    public void RefusesAFloorArrivalInItsOwnWords()
    {
        var equality = BoundaryEquality.Compare(
            ReplayBoundary.FloorEntryKind, FloorBoundary(floor: "3"), Live, "sha256:abc", "sha256:abc");

        Assert.False(equality.Matches);
        Assert.Contains("was not arrived at the way the recording's was", equality.Refusal!, StringComparison.Ordinal);
        Assert.DoesNotContain("fight", equality.Refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAFloorArrivalWhoseHiddenStateDisagrees()
    {
        var equality = BoundaryEquality.Compare(
            ReplayBoundary.FloorEntryKind, FloorBoundary(), Live, "sha256:live", "sha256:recorded");

        Assert.False(equality.Matches);
        Assert.All(equality.Comparisons, comparison => Assert.True(comparison.Matches));
        Assert.Contains("This floor was arrived at with everything", equality.Refusal!, StringComparison.Ordinal);
    }

    /// <summary>The kinds are a closed set here too. A host that dispatched on an
    /// unrecognised one would be comparing a moment nothing knows how to reach.</summary>
    [Fact]
    public void RefusesToCompareAKindOutsideTheClosedSet()
    {
        var refusal = Assert.Throws<ArgumentException>(() => BoundaryEquality.Compare(
            "shop_entry", FloorBoundary(), Live, "sha256:abc", "sha256:abc"));

        Assert.Contains("closed set", refusal.Message, StringComparison.Ordinal);
    }
}

public sealed class CombatStartEqualityTests
{
    private static readonly Dictionary<string, string> Live = new(StringComparer.Ordinal)
    {
        ["combat.turn"] = "1",
        ["combat.hand"] = "CARD.STRIKE_IRONCLAD|CARD.BASH",
        ["combat.player_hp"] = "64",
    };

    [Fact]
    public void AgreesWhenEveryObservedValueAndTheSnapshotAgree()
    {
        var equality = CombatStartEquality.Compare(
            EntryFixtures.Boundary(), Live, "sha256:abc", "sha256:abc");

        Assert.True(equality.Matches);
        Assert.Null(equality.Refusal);
        Assert.All(equality.Comparisons, comparison => Assert.True(comparison.Matches));
    }

    /// <summary>Every observed value is reported, agreeing or not: a reader cannot
    /// tell a boundary that was checked thoroughly from one barely checked at all if
    /// only the disagreements are kept.</summary>
    [Fact]
    public void ReportsEveryObservedValueWhetherItAgreedOrNot()
    {
        var equality = CombatStartEquality.Compare(
            EntryFixtures.Boundary(), Live, "sha256:abc", "sha256:abc");

        Assert.Equal(
            ["combat.hand", "combat.player_hp", "combat.turn"],
            equality.Comparisons.Select(comparison => comparison.Field));
    }

    [Fact]
    public void RefusesWhenAnObservedValueDisagrees()
    {
        var live = new Dictionary<string, string>(Live, StringComparer.Ordinal)
        {
            ["combat.player_hp"] = "80",
        };

        var equality = CombatStartEquality.Compare(
            EntryFixtures.Boundary(), live, "sha256:abc", "sha256:abc");

        Assert.False(equality.Matches);
        Assert.Contains("did not open the way the recording's did", equality.Refusal!, StringComparison.Ordinal);
        Assert.Contains("the recording shows '64', this game produced '80'", equality.Refusal!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The reason the digest is compared at all. Everything a video can show agrees
    /// and the run has still diverged - in a random stream's position, or in the
    /// order of the draw pile - and the next shuffle would prove it.
    /// </summary>
    [Fact]
    public void RefusesWhenOnlyTheHiddenStateDisagrees()
    {
        var equality = CombatStartEquality.Compare(
            EntryFixtures.Boundary(), Live, "sha256:live", "sha256:recorded");

        Assert.False(equality.Matches);
        Assert.All(equality.Comparisons, comparison => Assert.True(comparison.Matches));
        Assert.Contains("state no video can show", equality.Refusal!, StringComparison.Ordinal);
        Assert.Contains("sha256:recorded", equality.Refusal!, StringComparison.Ordinal);
        Assert.Contains("sha256:live", equality.Refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesToCompareWithoutTheRecordedSnapshotDigest()
    {
        var refusal = Assert.Throws<ArgumentException>(() => CombatStartEquality.Compare(
            EntryFixtures.Boundary(), Live, "sha256:live", expectedDigest: null!));

        Assert.Contains("cannot be verified without", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFieldTheProjectionDoesNotProduceFailsRatherThanPassingQuietly()
    {
        var boundary = EntryFixtures.Boundary(new Dictionary<string, Fact<string>>
        {
            ["combat.turn"] = Fact<string>.Observed("1", FactEvidence.AtVideoTime(75600, "turn badge")),
            ["combat.invented"] = Fact<string>.Observed("7", FactEvidence.AtVideoTime(75600, "nowhere")),
        });

        var equality = CombatStartEquality.Compare(boundary, Live, "sha256:abc", "sha256:abc");

        Assert.False(equality.Matches);
        Assert.Contains(
            equality.Comparisons,
            comparison => comparison.Field == "combat.invented" &&
                          comparison.Actual == "<no such canonical field>");
    }
}

internal static class EntryFixtures
{
    internal static Checkpoint Boundary(IReadOnlyDictionary<string, Fact<string>>? expect = null) => new()
    {
        Id = "combat-start",
        AfterSeq = 1,
        Kind = "combat_start",
        Expect = expect ?? new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
        {
            ["combat.turn"] = Fact<string>.Observed("1", FactEvidence.AtVideoTime(75600, "turn badge")),
            ["combat.hand"] = Fact<string>.Observed(
                "CARD.STRIKE_IRONCLAD|CARD.BASH", FactEvidence.AtVideoTime(75600, "hand")),
            ["combat.player_hp"] = Fact<string>.Observed("64", FactEvidence.AtVideoTime(75600, "health bar")),
        },
    };

    internal static ActionRecord Play(int seq) => new()
    {
        Seq = seq,
        Verb = ActionVerb.PlayCard,
        Args = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["card_id"] = "CARD.STRIKE_IRONCLAD",
            ["hand_index"] = "0",
        },
        Source = FactSource.Observed,
        Evidence = FactEvidence.AtVideoTime(80000, "the card leaves the hand"),
    };

    /// <summary>
    /// A recording shaped like the shipped one: a blessing, a map move into a fight,
    /// then the fight. Built here rather than loaded so each test changes exactly one
    /// thing about it.
    /// </summary>
    internal static ReplayManifest Recording() => Fixtures.ValidManifest() with
    {
        Actions =
        [
            new ActionRecord
            {
                Seq = 0,
                Verb = ActionVerb.ChooseNeowBlessing,
                Args = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["option_index"] = "2" },
                Source = FactSource.Observed,
                Evidence = FactEvidence.AtVideoTime(26000, "read from its effect"),
            },
            new ActionRecord
            {
                Seq = 1,
                Verb = ActionVerb.MapMove,
                Args = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["act"] = "0",
                    ["row"] = "1",
                    ["column"] = "3",
                },
                Source = FactSource.Observed,
                Evidence = FactEvidence.AtVideoTime(73500, "the ringed node"),
            },
            Play(2),
        ],
        Checkpoints = [Boundary()],
    };

    /// <summary>
    /// A recording of a whole run: three fights and two floors after the first, with
    /// a declared boundary and an observation at each one. Built here so a plan can be
    /// asked for a fight the shape of the history could never point at.
    /// </summary>
    internal static ReplayManifest WholeRun() => Fixtures.ValidManifest() with
    {
        Actions =
        [
            Fixtures.Action(0, ActionVerb.ChooseNeowBlessing, ("option_index", "2")),
            Fixtures.Action(1, ActionVerb.MapMove, ("act", "0"), ("row", "1"), ("column", "3")),
            Fixtures.Action(2, ActionVerb.PlayCard, ("card_id", "CARD.STRIKE_IRONCLAD"), ("hand_index", "0")),
            Fixtures.Action(3, ActionVerb.MapMove, ("act", "0"), ("row", "2"), ("column", "3")),
            Fixtures.Action(4, ActionVerb.PlayCard, ("card_id", "CARD.BASH"), ("hand_index", "0")),
            Fixtures.Action(5, ActionVerb.MapMove, ("act", "0"), ("row", "3"), ("column", "2")),
            Fixtures.Action(6, ActionVerb.PlayCard, ("card_id", "CARD.BASH"), ("hand_index", "0")),
        ],
        Checkpoints =
        [
            CombatStart("combat-start-1", 1),
            FloorEntry("floor-entry-2", 3, floor: "2", coord: "r1c3"),
            CombatStart("combat-start-2", 3),
            FloorEntry("floor-entry-3", 5, floor: "3", coord: "r2c2"),
            CombatStart("combat-start-3", 6),
        ],
        Boundaries =
        [
            ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Fixtures.Digest)),
            ReplayBoundary.FloorEntry(2, 3, Fact<string>.Engine(Fixtures.Digest)),
            ReplayBoundary.CombatStart(2, 3, Fact<string>.Engine(Fixtures.Digest)),
            ReplayBoundary.FloorEntry(3, 5, Fact<string>.Engine(Fixtures.Digest)),
            ReplayBoundary.CombatStart(3, 6, Fact<string>.Engine(Fixtures.Digest)),
        ],
    };

    private static Checkpoint CombatStart(string id, int afterSeq) => new()
    {
        Id = id,
        AfterSeq = afterSeq,
        Kind = "combat_start",
        Expect = new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
        {
            ["combat.turn"] = Fact<string>.Observed("1", FactEvidence.AtVideoTime(75600, "turn badge")),
        },
    };

    private static Checkpoint FloorEntry(string id, int afterSeq, string floor, string coord) => new()
    {
        Id = id,
        AfterSeq = afterSeq,
        Kind = "floor_entry",
        Expect = new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
        {
            ["run.total_floor"] = Fact<string>.Observed(floor, FactEvidence.AtVideoTime(75600, "floor counter")),
            ["run.map_coord"] = Fact<string>.Observed(coord, FactEvidence.AtVideoTime(75600, "ringed node")),
        },
    };
}

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
}

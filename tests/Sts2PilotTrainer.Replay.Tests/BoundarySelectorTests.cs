using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// A boundary is asked for by the kind's own coordinate, and the point of these is
/// that a coordinate which does not identify one boundary is refused rather than
/// resolved to whichever came first.
/// </summary>
public sealed class BoundarySelectorTests
{
    [Theory]
    [InlineData("combat_start:1", "combat_start", 1, null, null)]
    [InlineData("combat_start:12", "combat_start", 12, null, null)]
    [InlineData("floor_entry:5", "floor_entry", null, 5, null)]
    public void ReadsAKindAndItsOwnCoordinate(
        string text, string kind, int? fight, int? floor, int? turn)
    {
        var selector = BoundarySelector.Parse(text);

        Assert.Equal(kind, selector.Kind);
        Assert.Equal(fight, selector.Fight);
        Assert.Equal(floor, selector.Floor);
        Assert.Equal(turn, selector.Turn);
        Assert.Equal(text, selector.ToString());
    }

    /// <summary>A turn belongs to a fight, so its coordinate is two numbers.</summary>
    [Fact]
    public void ReadsATurnAsAFightAndATurnOfIt()
    {
        var selector = BoundarySelector.Parse("turn_start:2.3");

        Assert.Equal(ReplayBoundary.TurnStartKind, selector.Kind);
        Assert.Equal(2, selector.Fight);
        Assert.Equal(3, selector.Turn);
        Assert.Equal("turn_start:2.3", selector.ToString());
    }

    [Theory]
    [InlineData("turn_start:3", "takes both")]
    [InlineData("turn_start:1.2.3", "takes both")]
    [InlineData("combat_start", "does not name a boundary")]
    [InlineData("combat_start:", "does not name a boundary")]
    [InlineData(":2", "does not name a boundary")]
    [InlineData("room_entry:2", "is not a boundary kind")]
    [InlineData("combat_start:0", "counted from 1")]
    [InlineData("combat_start:-1", "counted from 1")]
    [InlineData("floor_entry:third", "counted from 1")]
    public void RefusesACoordinateThatIdentifiesNoBoundary(string text, string expected)
    {
        var refusal = Assert.Throws<ManifestException>(() => BoundarySelector.Parse(text));

        Assert.Contains(expected, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every kind is named in the refusal for an unknown one. The set is closed and a
    /// person who guessed a kind is a person who has not seen the list.
    /// </summary>
    [Fact]
    public void NamesEveryKindWhenTheKindIsUnknown()
    {
        var refusal = Assert.Throws<ManifestException>(() => BoundarySelector.Parse("room_entry:1"));

        foreach (var kind in ReplayBoundary.Kinds)
        {
            Assert.Contains(kind, refusal.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The third floor arrival is not floor 3 - a run starts on a floor it never
    /// arrived at - which is the whole reason the coordinate is the kind's own rather
    /// than a position in the list.
    /// </summary>
    [Fact]
    public void FindsTheBoundaryWithThatCoordinateRatherThanThatPosition()
    {
        var boundaries = new[]
        {
            ReplayBoundary.FloorEntry(2, 3, Digest("floor-2")),
            ReplayBoundary.FloorEntry(3, 7, Digest("floor-3")),
            ReplayBoundary.FloorEntry(4, 11, Digest("floor-4")),
        };

        Assert.Equal("floor-3", BoundarySelector.Parse("floor_entry:3").In(boundaries)?.Digest.Value);
        Assert.Null(BoundarySelector.Parse("floor_entry:1").In(boundaries));
    }

    /// <summary>A turn of one fight is not the same turn of another.</summary>
    [Fact]
    public void KeepsTurnsOfDifferentFightsApart()
    {
        var boundaries = new[]
        {
            ReplayBoundary.TurnStart(1, 2, 5, Digest("f1t2")),
            ReplayBoundary.TurnStart(2, 2, 21, Digest("f2t2")),
        };

        Assert.Equal("f1t2", BoundarySelector.Parse("turn_start:1.2").In(boundaries)?.Digest.Value);
        Assert.Equal("f2t2", BoundarySelector.Parse("turn_start:2.2").In(boundaries)?.Digest.Value);
        Assert.Null(BoundarySelector.Parse("turn_start:3.2").In(boundaries));
    }

    /// <summary>A combat start is not a floor arrival that happens to share a number.</summary>
    [Fact]
    public void DoesNotMatchAcrossKinds()
    {
        var boundaries = new[] { ReplayBoundary.CombatStart(2, 4, Digest("fight-2")) };

        Assert.Null(BoundarySelector.Parse("floor_entry:2").In(boundaries));
        Assert.NotNull(BoundarySelector.Parse("combat_start:2").In(boundaries));
    }

    /// <summary>
    /// Nothing in these phases stands anybody at a turn, so asking for a plan to one
    /// is refused in words rather than answered with the fight's start.
    /// </summary>
    [Fact]
    public void RefusesAPlanToATurn()
    {
        var refusal = Assert.Throws<ManifestException>(
            () => BoundarySelector.Parse("turn_start:1.2").PlanFor(Fixtures.SyntheticManifest()));

        Assert.Contains("nothing here enters one", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A later fight of a real engine-generated history, selected and planned. The
    /// plan ends where that fight's own boundary says it does and walks every decision
    /// up to it, which is the whole of what a host is handed.
    /// </summary>
    [Fact]
    public void PlansTheJourneyToALaterFightOfAWholeAct()
    {
        var manifest = Fixtures.WholeActManifest();
        var declared = manifest.BoundaryAt(ReplayBoundary.CombatStartKind, fight: 3)!;

        var plan = BoundarySelector.Parse("combat_start:3").PlanFor(manifest);

        Assert.Equal(ReplayBoundary.CombatStartKind, plan.Kind);
        Assert.Equal(3, plan.Fight);
        Assert.Equal("the start of fight 3", plan.Describe());
        Assert.Equal(declared.AfterSeq, plan.BoundarySeq);
        Assert.Equal(declared.AfterSeq, plan.PrefixActions[^1].Seq);
        Assert.Equal(
            manifest.Actions.Count(action => action.Seq <= declared.AfterSeq), plan.PrefixActions.Count);
    }

    /// <summary>
    /// A floor arrival of the same history. Its checkpoint is the one that says where
    /// the run stands rather than whichever checkpoint shares the action - a fight
    /// starts on the same map move, and handing over that one would stand somebody at
    /// a moment proved by the wrong fields.
    /// </summary>
    [Fact]
    public void PlansTheJourneyToAFloorArrivalOfAWholeAct()
    {
        var manifest = Fixtures.WholeActManifest();
        var declared = manifest.BoundaryAt(ReplayBoundary.FloorEntryKind, floor: 4)!;

        var plan = BoundarySelector.Parse("floor_entry:4").PlanFor(manifest);

        Assert.Equal(ReplayBoundary.FloorEntryKind, plan.Kind);
        Assert.Equal(4, plan.Floor);
        Assert.Equal("arrival on floor 4", plan.Describe());
        Assert.Equal(declared.AfterSeq, plan.BoundarySeq);
        Assert.Equal("4", plan.Boundary.Expect["run.total_floor"].Value);
        Assert.Contains("run.map_coord", plan.Boundary.Expect.Keys);
    }

    /// <summary>
    /// The floor a run begins on is not arrived at, so nothing derived a boundary
    /// there and no plan may invent one.
    /// </summary>
    [Fact]
    public void RefusesTheFloorAWholeActNeverArrivesOn()
    {
        var refusal = Assert.Throws<ManifestException>(
            () => BoundarySelector.Parse("floor_entry:1").PlanFor(Fixtures.WholeActManifest()));

        Assert.Contains("declares no floor-entry boundary for floor 1", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A fight past the last one the history holds is refused rather than
    /// resolved to the nearest fight there is.</summary>
    [Fact]
    public void RefusesAFightAWholeActNeverReaches()
    {
        var manifest = Fixtures.WholeActManifest();
        var beyond = manifest.Boundaries.Where(boundary => boundary.IsCombatStart).Max(b => b.Fight!.Value) + 1;

        var refusal = Assert.Throws<ManifestException>(
            () => BoundarySelector.Parse($"combat_start:{beyond}").PlanFor(manifest));

        Assert.Contains(
            $"declares no combat-start boundary for fight {beyond}", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A turn of a later fight, selected out of a list that holds a turn 3 in several
    /// fights. This is where an ordinal counted across the list would quietly answer
    /// with another fight's turn.
    /// </summary>
    [Fact]
    public void SelectsATurnOfALaterFightOutOfAWholeAct()
    {
        var manifest = Fixtures.WholeActManifest();

        var selected = BoundarySelector.Parse("turn_start:2.3").In(manifest.Boundaries);

        Assert.NotNull(selected);
        Assert.Equal(2, selected.Fight);
        Assert.Equal(3, selected.Turn);
        Assert.Equal(
            manifest.BoundaryAt(ReplayBoundary.TurnStartKind, fight: 2, turn: 3)!.AfterSeq, selected.AfterSeq);
        Assert.NotEqual(
            manifest.BoundaryAt(ReplayBoundary.TurnStartKind, fight: 1, turn: 3)!.AfterSeq, selected.AfterSeq);
    }

    /// <summary>The boundary a command means when nobody said which.</summary>
    [Fact]
    public void DefaultsToTheFirstFight()
    {
        Assert.Equal("combat_start:1", BoundarySelector.FirstFight.ToString());
    }

    private static Fact<string> Digest(string value) => Fact<string>.Engine(value);
}

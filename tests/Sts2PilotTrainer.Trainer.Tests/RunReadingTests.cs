using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// What a recording says at one place in the run, and what a floor of it held.
///
/// Two derivations sit here and both are readings rather than computations. What is
/// pinned is the direction they fail in: a field a checkpoint does not carry is null,
/// never zero and never an empty deck, because "nothing was recorded here" and "there
/// was nothing here" are different facts. And a floor's kind is read off the decisions
/// the run made on it, because there is no room type in a recording to read.
/// </summary>
public sealed class RunReadingTests
{
    private static Fact<string> Engine(string value) => Fact<string>.Engine(value);

    private static ActionRecord Action(int seq, ActionVerb verb) => new()
    {
        Seq = seq,
        Verb = verb,
        Source = FactSource.Captured,
        Args = new Dictionary<string, string>(StringComparer.Ordinal),
    };

    private static ReplayManifest With(
        IReadOnlyList<Checkpoint> checkpoints, IReadOnlyList<ActionRecord>? actions = null) =>
        Fixtures.Recording() with { Checkpoints = checkpoints, Actions = actions ?? [] };

    private static Checkpoint At(int afterSeq, params (string Field, string Value)[] fields) => new()
    {
        Id = $"at-{afterSeq}",
        AfterSeq = afterSeq,
        Kind = ReplayBoundary.FloorEntryKind,
        Expect = fields.ToDictionary(field => field.Field, field => Engine(field.Value), StringComparer.Ordinal),
    };

    /// <summary>
    /// The deck as tiles: one per distinct card with its count, in the order the deck
    /// holds them rather than alphabetically, because the recording's order is what the
    /// run built.
    /// </summary>
    [Fact]
    public void TheDeckIsTiledByDistinctCardInTheOrderTheDeckHoldsThem()
    {
        var reading = RunReading.At(
            With([At(4, ("player.deck", "CARD.STRIKE|CARD.BASH|CARD.STRIKE|CARD.STRIKE"))]), 4);

        Assert.Equal([("CARD.STRIKE", 3), ("CARD.BASH", 1)], reading.Deck!.Select(t => (t.CardId, t.Count)));
        Assert.Equal(4, reading.DeckCount);
    }

    /// <summary>A field no checkpoint carries is a gap. A deck of null and a deck of
    /// nothing are different answers, and only one of them is a claim.</summary>
    [Fact]
    public void AFieldNoCheckpointCarriesIsNullRatherThanZero()
    {
        var nothing = RunReading.At(With([At(4, ("player.hp", "31"))]), 4);

        Assert.Null(nothing.Deck);
        Assert.Null(nothing.DeckCount);
        Assert.Equal(31, nothing.Hp);
        Assert.Null(nothing.MaxHp);
        Assert.Empty(nothing.Relics);
        Assert.Null(nothing.Encounter);
        Assert.Empty(nothing.Enemies);
    }

    /// <summary>A place with no checkpoint at all reads as nothing, and nothing is
    /// invented for it.</summary>
    [Fact]
    public void APlaceWithNoCheckpointReadsAsNothing()
    {
        Assert.Equal(RunReading.Nothing, RunReading.At(With([At(4, ("player.hp", "31"))]), 99));
    }

    /// <summary>An empty sequence field is an empty list, not one empty member: a run
    /// carrying no relics writes the field with nothing in it.</summary>
    [Fact]
    public void AnEmptySequenceFieldIsAnEmptyListAndNotOneEmptyMember()
    {
        Assert.Empty(RunReading.At(With([At(4, ("player.relics", ""))]), 4).Relics);
    }

    /// <summary>
    /// The enemies are numbered, so they are read until the numbering stops. A gap in
    /// the numbering ends the list rather than being stepped over, because a checkpoint
    /// numbers what it observed and there is no enemy 2 to find past a missing 1.
    /// </summary>
    [Fact]
    public void TheEnemiesAreReadInTheOrderTheCheckpointNumbersThem()
    {
        var reading = RunReading.At(
            With(
            [
                At(
                    4,
                    ("combat.encounter", "ENCOUNTER.PAIR"),
                    ("combat.enemy.0.model", "MONSTER.WURM"),
                    ("combat.enemy.0.hp", "56"),
                    ("combat.enemy.0.max_hp", "56"),
                    ("combat.enemy.1.model", "MONSTER.CRAWLER")),
            ]),
            4);

        Assert.Equal("ENCOUNTER.PAIR", reading.Encounter);
        Assert.Equal(["MONSTER.WURM", "MONSTER.CRAWLER"], reading.Enemies.Select(enemy => enemy.Model));
        Assert.Equal(56, reading.Enemies[0].Hp);
        Assert.Null(reading.Enemies[1].Hp);
    }

    /// <summary>
    /// A relic is found when it first appears in a checkpoint, which is what "the order
    /// found" means for a recording nobody watched being played. The position it carries
    /// is what the strip orders by within a rarity.
    /// </summary>
    [Fact]
    public void RelicsAreFoundInTheOrderTheCheckpointsFirstShowThem()
    {
        var recording = With(
        [
            At(2, ("player.relics", "RELIC.BURNING_BLOOD")),
            At(9, ("player.relics", "RELIC.BURNING_BLOOD|RELIC.SCROLL")),
            At(20, ("player.relics", "RELIC.BURNING_BLOOD|RELIC.SCROLL")),
        ]);

        var relics = RunReading.RelicsFound(recording);

        Assert.Equal(["RELIC.BURNING_BLOOD", "RELIC.SCROLL"], relics.Select(relic => relic.Id));
        Assert.Equal([0, 1], relics.Select(relic => relic.FoundAt));
    }

    /// <summary>Checkpoints are read in history order whatever order they are written
    /// in, so which relic was found first is the run's answer and not the file's.</summary>
    [Fact]
    public void RelicsAreReadInHistoryOrderAndNotInFileOrder()
    {
        var recording = With(
        [
            At(20, ("player.relics", "RELIC.A|RELIC.B")),
            At(2, ("player.relics", "RELIC.A")),
        ]);

        Assert.Equal(["RELIC.A", "RELIC.B"], RunReading.RelicsFound(recording).Select(relic => relic.Id));
    }

    /// <summary>A recording whose checkpoints say nothing about relics has none here,
    /// rather than an empty claim about a run with relics in it.</summary>
    [Fact]
    public void ARecordingThatSaysNothingAboutRelicsNamesNone()
    {
        Assert.Empty(RunReading.RelicsFound(With([At(4, ("player.hp", "31"))])));
    }

    /// <summary>
    /// A floor is named by the decision the run made on it. There is no room type in a
    /// manifest to read: the map move records the act and the coordinate, and the retail
    /// client rolls the room after the move.
    /// </summary>
    [Theory]
    [InlineData(ActionVerb.ShopPurchase, FloorKind.Shop)]
    [InlineData(ActionVerb.ChooseRestSiteOption, FloorKind.Rest)]
    [InlineData(ActionVerb.ChooseEventOption, FloorKind.Event)]
    [InlineData(ActionVerb.TakeChestRelic, FloorKind.Treasure)]
    [InlineData(ActionVerb.SkipChestRelic, FloorKind.Treasure)]
    [InlineData(ActionVerb.PlayCard, FloorKind.Combat)]
    [InlineData(ActionVerb.EndTurn, FloorKind.Combat)]
    public void AFloorIsNamedByTheDecisionTheRunMadeOnIt(ActionVerb verb, FloorKind kind)
    {
        var recording = With([], [Action(5, verb)]);

        Assert.Equal(kind, FloorKinds.Between(recording, 4, 9));
    }

    /// <summary>A floor whose window holds no decision that says what was there is
    /// unknown, and unknown is an answer rather than a missing one.</summary>
    [Fact]
    public void AFloorWithNoTellingDecisionIsUnknown()
    {
        var recording = With([], [Action(5, ActionVerb.MapMove)]);

        Assert.Equal(FloorKind.Unknown, FloorKinds.Between(recording, 4, 9));
        Assert.Null(LibraryCopy.KindWord(FloorKind.Unknown));
    }

    /// <summary>
    /// Combat overrules whatever else the floor's window holds, because a fight is
    /// followed by its reward screens and those are decisions too. A card taken after a
    /// fight says nothing about the room.
    /// </summary>
    [Fact]
    public void ACombatFloorStaysCombatWhateverFollowsTheFight()
    {
        var recording = With(
            [],
            [Action(5, ActionVerb.ChooseEventOption), Action(6, ActionVerb.PlayCard), Action(7, ActionVerb.TakeCard)]);

        Assert.Equal(FloorKind.Combat, FloorKinds.Between(recording, 4, 9));
    }

    /// <summary>The window is the floor's own and half-open at its far end, so a
    /// decision on the next floor is not this floor's.</summary>
    [Fact]
    public void TheWindowIsHalfOpenSoTheNextFloorsDecisionIsNotThisFloors()
    {
        var recording = With([], [Action(9, ActionVerb.ShopPurchase)]);

        Assert.Equal(FloorKind.Unknown, FloorKinds.Between(recording, 4, 9));
        Assert.Equal(FloorKind.Shop, FloorKinds.Between(recording, 9, 14));
    }
}

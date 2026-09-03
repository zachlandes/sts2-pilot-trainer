using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// What the player reads after their fight, produced from data rather than written
/// down. Every assertion is the approved sentence, character for character.
/// </summary>
public sealed class FightResultScreenTests
{
    private const string Digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void ReadsAsTheApprovedWordingOverAComparison()
    {
        var screen = FightResultScreen.For("NaveGreed", CombatComparison.Between(PlayersLine(), RecordingsLine()));

        Assert.True(screen.HasComparison);
        Assert.Equal("Your fight and NaveGreed's", screen.Title);
        Assert.Equal(["You", "NaveGreed"], screen.Columns);
        Assert.Equal(
        [
            new FightResultRow("Outcome", "Won", "Won", true),
            new FightResultRow("Turns", "3", "2", false),
            new FightResultRow("Health at the start", "64", "64", true),
            new FightResultRow("Health at the end", "50", "58", false),
            new FightResultRow("Net health change", "-14", "-6", false),
            new FightResultRow("Potions used", "Block Potion", "none", false),
            new FightResultRow("Cards removed", "none", "none", true),
        ], screen.Rows);
        Assert.Equal("Turn by turn", screen.TurnDetailHeading);
        Assert.Equal([1, 2, 3], screen.Turns.Select(turn => turn.Turn));
        Assert.Equal(["CARD.STRIKE_IRONCLAD"], screen.Turns[0].Yours!.CardModelIds);
        Assert.Equal(["CARD.HELLRAISER"], screen.Turns[0].Theirs!.CardModelIds);
        Assert.Equal((8, 6), (screen.Turns[0].Yours!.EnemyHealthLost, screen.Turns[0].Yours!.HealthLost));
        Assert.Equal(["POTION.BLOCK_POTION"], screen.Turns[1].Yours!.PotionModelIds);
        Assert.Empty(screen.Turns[1].Theirs!.PotionModelIds);
        Assert.Equal((10, 8), (screen.Turns[1].Yours!.EnemyHealthLost, screen.Turns[1].Yours!.HealthLost));
        Assert.Equal((34, 0), (screen.Turns[1].Theirs!.EnemyHealthLost, screen.Turns[1].Theirs!.HealthLost));
        Assert.Null(screen.Turns[2].Theirs);
        Assert.Equal(
        [
            "This states differences. It does not say which fight was better.",
            "Health lost counts only health that came off. Damage absorbed by block is not counted.",
        ], screen.Notes);
        Assert.Equal(string.Empty, screen.Notice);
        Assert.Equal("Done", screen.DoneButton);
    }

    [Fact]
    public void ATurnOnlySideReachedIsAbsentOnTheOtherRatherThanZero()
    {
        var screen = FightResultScreen.For("NaveGreed", CombatComparison.Between(RecordingsLine(), PlayersLine()));

        Assert.Null(screen.Turns[2].Yours);
        Assert.NotNull(screen.Turns[2].Theirs);
        Assert.Null(screen.Chart.Yours.Points[2].EnemyHealthLost);
        Assert.Null(screen.Chart.Yours.Points[2].HealthLost);
        Assert.False(screen.Chart.Yours.Points[2].Reached);
    }

    [Fact]
    public void ACardPlayedWithNoCardIdIsRefusedRatherThanDrawnBlank()
    {
        var capture = Live();
        capture.BeginStep("PlayCard", Args(), Sample("in_progress", 1, 64, 42));
        capture.CompleteStep(Sample("victory", 1, 64, 0, enemies: 0));

        var screen = FightResultScreen.Of("NaveGreed", capture, Recording());
        Assert.False(screen.HasComparison);
        Assert.Contains("carries no 'card_id'", screen.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void AnotherCreatorIsNamedBySameSentences()
    {
        var screen = FightResultScreen.For("Someone Else", CombatComparison.Between(PlayersLine(), RecordingsLine()));
        Assert.Equal("Your fight and Someone Else's", screen.Title);
        Assert.Equal(["You", "Someone Else"], screen.Columns);
        Assert.Equal("Someone Else", screen.Chart.Theirs.Label);
        Assert.Null(screen.Turns[2].Theirs);
    }

    [Fact]
    public void AGainIsSignedAndZeroIsNot()
    {
        var gain = PlayersLine() with { Summary = PlayersLine().Summary with { NetHealthChange = 6 } };
        var even = PlayersLine() with { Summary = PlayersLine().Summary with { NetHealthChange = 0 } };
        Assert.Equal("+6", FightResultScreen.For("N", CombatComparison.Between(gain, RecordingsLine())).Rows[4].Yours);
        Assert.Equal("0", FightResultScreen.For("N", CombatComparison.Between(even, RecordingsLine())).Rows[4].Yours);
    }

    [Fact]
    public void AFightLeftBeforeItEndedIsANotice()
    {
        var capture = Live();
        capture.Abandon();

        var screen = FightResultScreen.Of("NaveGreed", capture, Recording());
        Assert.False(screen.HasComparison);
        Assert.Equal("Combat Trainer", screen.Title);
        Assert.Equal("This fight was left before it ended, so there is nothing to compare.", screen.Notice);
        Assert.Equal("Done", screen.DoneButton);
    }

    [Fact]
    public void AFightThatCouldNotBeCapturedShowsTheCapturesOwnRefusal()
    {
        var capture = Live();
        capture.Finish(Sample("victory", 1, 64, 0, enemies: 0));
        Assert.Equal(FightCaptureState.Incomplete, capture.State);

        var screen = FightResultScreen.Of("NaveGreed", capture, Recording());
        Assert.Equal(capture.Refusal, screen.Notice);
        Assert.StartsWith("Your fight could not be captured completely, so it is not compared.", screen.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void AFightStillBeingFoughtIsNotCompared()
    {
        var screen = FightResultScreen.Of("NaveGreed", Live(), Recording());
        Assert.False(screen.HasComparison);
        Assert.Contains("has not ended", screen.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void ALostFightIsANotice()
    {
        var capture = Live();
        capture.BeginStep("EndTurn", Args(), Sample("in_progress", 1, 64, 42));
        capture.CompleteStep(Sample("defeat", 1, 0, 42));

        var screen = FightResultScreen.Of("NaveGreed", capture, Recording());
        Assert.Equal(
            "You did not win this fight, so there is no completed line to compare with NaveGreed's.", screen.Notice);
    }

    [Fact]
    public void AWonFightFromADifferentBoundaryShowsTheComparisonsOwnRefusal()
    {
        var capture = FightCapture.Begin("player", Sample("in_progress", 1, 64, 42), "sha256:" + new string('f', 64));
        capture.BeginStep("PlayCard", Card("CARD.BASH"), Sample("in_progress", 1, 64, 42));
        capture.CompleteStep(Sample("victory", 1, 64, 0, enemies: 0));

        var screen = FightResultScreen.Of("NaveGreed", capture, Recording());
        Assert.False(screen.HasComparison);
        Assert.Contains("different complete combat-start snapshot digests", screen.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public void AWonFightFromTheRecordedBoundaryIsCompared()
    {
        var capture = Live();
        capture.BeginStep("PlayCard", Card("CARD.HELLRAISER"), Sample("in_progress", 1, 64, 42));
        capture.CompleteStep(Sample("in_progress", 1, 64, 34));
        capture.BeginStep("EndTurn", Args(), Sample("in_progress", 1, 64, 34));
        capture.CompleteStep(Sample("in_progress", 2, 58, 34));
        capture.BeginStep("PlayCard", Card("CARD.BASH"), Sample("in_progress", 2, 58, 34));
        capture.CompleteStep(Sample("victory", 2, 58, 0, enemies: 0));

        var screen = FightResultScreen.Of("NaveGreed", capture, Recording());
        Assert.True(screen.HasComparison);
        Assert.All(screen.Rows, row => Assert.True(row.Matches));
        Assert.Equal(["CARD.BASH"], screen.Turns[1].Yours!.CardModelIds);
        Assert.Equal(screen.Turns[1].Yours!.CardModelIds, screen.Turns[1].Theirs!.CardModelIds);
        Assert.Equal(34, screen.Turns[1].Theirs!.EnemyHealthLost);
    }

    // ── The two lines ─────────────────────────────────────────────────────

    /// <summary>Three turns, a potion, a net loss of fourteen.</summary>
    private static CombatProjection PlayersLine()
    {
        var capture = Live();
        capture.BeginStep("PlayCard", Card("CARD.STRIKE_IRONCLAD"), Sample("in_progress", 1, 64, 42));
        capture.CompleteStep(Sample("in_progress", 1, 64, 34));
        capture.BeginStep("EndTurn", Args(), Sample("in_progress", 1, 64, 34));
        capture.CompleteStep(Sample("in_progress", 2, 58, 34));
        capture.BeginStep("UsePotion", Args(("potion_index", "0")), Sample("in_progress", 2, 58, 34));
        capture.CompleteStep(Sample("in_progress", 2, 58, 34, potions: "empty|empty"));
        capture.BeginStep("PlayCard", Card("CARD.BASH"), Sample("in_progress", 2, 58, 34, potions: "empty|empty"));
        capture.CompleteStep(Sample("in_progress", 2, 58, 24, potions: "empty|empty"));
        capture.BeginStep("EndTurn", Args(), Sample("in_progress", 2, 58, 24, potions: "empty|empty"));
        capture.CompleteStep(Sample("in_progress", 3, 50, 24, potions: "empty|empty"));
        capture.BeginStep("PlayCard", Card("CARD.HELLRAISER"), Sample("in_progress", 3, 50, 24, potions: "empty|empty"));
        capture.CompleteStep(Sample("victory", 3, 50, 0, enemies: 0, potions: "empty|empty"));
        return capture.Project();
    }

    /// <summary>Two turns, no potion, a net loss of six.</summary>
    private static CombatProjection RecordingsLine() => Recording().Projection();

    private static RecordedFight Recording()
    {
        var capture = FightCapture.Begin("test-run", Sample("in_progress", 1, 64, 42), Digest);
        capture.BeginStep("PlayCard", Card("CARD.HELLRAISER"), Sample("in_progress", 1, 64, 42));
        capture.CompleteStep(Sample("in_progress", 1, 64, 34));
        capture.BeginStep("EndTurn", Args(), Sample("in_progress", 1, 64, 34));
        capture.CompleteStep(Sample("in_progress", 2, 58, 34));
        capture.BeginStep("PlayCard", Card("CARD.BASH"), Sample("in_progress", 2, 58, 34));
        capture.CompleteStep(Sample("victory", 2, 58, 0, enemies: 0));
        return new RecordedFight
        {
            SchemaId = RecordedFight.Schema,
            RunId = "test-run",
            CoveredThroughSeq = 2,
            ActionHistoryHash = "sha256:unbound-in-this-test",
            CombatStartSnapshotDigest = Digest,
            Trace = capture.Trace,
        };
    }

    private static FightCapture Live() => FightCapture.Begin("player", Sample("in_progress", 1, 64, 42), Digest);

    private static Dictionary<string, string> Sample(
        string outcome, int turn, int hp, int enemyHp, int enemies = 1, string potions = "POTION.BLOCK_POTION|empty")
    {
        var sample = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["combat.in_progress"] = outcome == "in_progress" ? "true" : "false",
            ["combat.outcome"] = outcome,
            ["combat.turn"] = turn.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["combat.encounter"] = "ENCOUNTER.TEST",
            ["combat.enemy_count"] = enemies.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["combat.hand"] = "CARD.BASH|CARD.STRIKE_IRONCLAD",
            ["player.hp"] = hp.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["player.max_hp"] = "68",
            ["player.deck"] = "CARD.BASH|CARD.STRIKE_IRONCLAD",
            ["player.relics"] = "RELIC.BURNING_BLOOD",
            ["player.potions"] = potions,
        };
        for (var i = 0; i < enemies; i++)
        {
            sample[$"combat.enemy.{i}.model"] = "MONSTER.TEST";
            sample[$"combat.enemy.{i}.hp"] = enemyHp.ToString(System.Globalization.CultureInfo.InvariantCulture);
            sample[$"combat.enemy.{i}.max_hp"] = "42";
        }
        return sample;
    }

    private static IReadOnlyDictionary<string, string> Card(string modelId) => Args(("card_id", modelId));

    private static IReadOnlyDictionary<string, string> Args(params (string Key, string Value)[] args) =>
        args.ToDictionary(arg => arg.Key, arg => arg.Value, StringComparer.Ordinal);
}

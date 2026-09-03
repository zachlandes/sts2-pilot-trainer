using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// The post-fight chart, derived from a comparison and nothing else.
///
/// Every assertion here is about a number the comparison already carries or about
/// the absence of one. The cases that matter are the ones a chart is tempted to
/// invent: a turn only one line reached, a potion's turn, and the scale two lines
/// are drawn against.
/// </summary>
public sealed class FightResultChartTests
{
    private const string Digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void PlotsBothMeasuresForBothLinesAgainstTheTurn()
    {
        var chart = FightResultChart.From("NaveGreed", Comparison());

        Assert.Equal([1, 2, 3], chart.Turns);
        Assert.Equal([8, 10, 24], chart.Yours.Points.Select(point => point.EnemyHealthLost));
        Assert.Equal([6, 8, 0], chart.Yours.Points.Select(point => point.HealthLost));
        Assert.Equal([8, 34], chart.Theirs.Points.Take(2).Select(point => point.EnemyHealthLost));
        Assert.Equal([6, 0], chart.Theirs.Points.Take(2).Select(point => point.HealthLost));
        Assert.Equal([1, 2, 3], chart.Yours.Points.Select(point => point.Turn));
        Assert.Equal(chart.Turns.Count, chart.Theirs.Points.Count);
    }

    [Fact]
    public void KeepsThePlayersLineAndTheRecordingsApart()
    {
        var chart = FightResultChart.From("NaveGreed", Comparison());

        Assert.True(chart.Yours.IsPlayer);
        Assert.False(chart.Theirs.IsPlayer);
        Assert.Equal("You", chart.Yours.Label);
        Assert.Equal("NaveGreed", chart.Theirs.Label);
    }

    [Fact]
    public void MarksAPotionAtTheTurnItWasUsed()
    {
        var chart = FightResultChart.From("NaveGreed", Comparison());

        Assert.Empty(chart.Yours.Points[0].PotionModelIds);
        Assert.Equal(["POTION.BLOCK_POTION"], chart.Yours.Points[1].PotionModelIds);
        Assert.Empty(chart.Yours.Points[2].PotionModelIds);
        Assert.All(chart.Theirs.Points, point => Assert.Empty(point.PotionModelIds));
    }

    [Fact]
    public void ATurnALineNeverReachedHasNoValueRatherThanAZero()
    {
        var chart = FightResultChart.From("NaveGreed", Comparison());

        Assert.False(chart.Theirs.Points[2].Reached);
        Assert.Null(chart.Theirs.Points[2].EnemyHealthLost);
        Assert.Null(chart.Theirs.Points[2].HealthLost);
        Assert.True(chart.Yours.Points[2].Reached);
    }

    [Fact]
    public void ScalesBothMeasuresAndBothLinesAgainstOneCeiling()
    {
        // The largest single value anywhere is the recording's 34 off the enemy on
        // turn 2, and the player's line is drawn against it too.
        Assert.Equal(34, FightResultChart.From("NaveGreed", Comparison()).Ceiling);
    }

    [Fact]
    public void AFightThatCostNeitherSideAnythingStillHasItsTurns()
    {
        var chart = FightResultChart.From("NaveGreed", CombatComparison.Between(Untouched(), Untouched()));

        Assert.True(chart.HasTurns);
        Assert.Equal(0, chart.Ceiling);
        Assert.All(chart.Yours.Points, point => Assert.True(point.Reached));
    }

    [Fact]
    public void AScreenWithoutAComparisonHasNothingToDraw()
    {
        Assert.False(FightResultScreen.Left().Chart.HasTurns);
        Assert.Empty(FightResultScreen.Left().Chart.Yours.Points);
    }

    // ── The two lines ─────────────────────────────────────────────────────

    /// <summary>Three turns against two, with the player spending a potion on turn 2
    /// and the recording ending its fight on it.</summary>
    private static CombatComparison Comparison() => CombatComparison.Between(Yours(), Theirs());

    private static CombatProjection Yours()
    {
        var capture = Live("player");
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

    private static CombatProjection Theirs()
    {
        var capture = Live("recording");
        capture.BeginStep("PlayCard", Card("CARD.HELLRAISER"), Sample("in_progress", 1, 64, 42));
        capture.CompleteStep(Sample("in_progress", 1, 64, 34));
        capture.BeginStep("EndTurn", Args(), Sample("in_progress", 1, 64, 34));
        capture.CompleteStep(Sample("in_progress", 2, 58, 34));
        capture.BeginStep("PlayCard", Card("CARD.BASH"), Sample("in_progress", 2, 58, 34));
        capture.CompleteStep(Sample("victory", 2, 58, 0, enemies: 0));
        return capture.Project();
    }

    /// <summary>A fight that ended without either side losing anything.</summary>
    private static CombatProjection Untouched()
    {
        var capture = Live("untouched");
        capture.BeginStep("PlayCard", Card("CARD.BASH"), Sample("in_progress", 1, 64, 42));
        capture.CompleteStep(Sample("ended", 1, 64, 42));
        return capture.Project();
    }

    private static FightCapture Live(string sourceId) =>
        FightCapture.Begin(sourceId, Sample("in_progress", 1, 64, 42), Digest);

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

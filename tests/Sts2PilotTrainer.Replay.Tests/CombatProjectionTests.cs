namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The comparison contract, exercised without the game.
///
/// Every trace here is written by hand, which is the point: the contract reads only
/// the fields <see cref="ReplayTrace.SampledFields"/> names, so it can be shown to
/// do the right thing on inputs nobody had to own a game to produce - including the
/// inputs it must refuse, which no real replay would hand it.
/// </summary>
public class CombatProjectionTests
{
    [Fact]
    public void RefusesATraceRecordedBeforeCombatEndWasObservable()
    {
        // A trace from an older arbiter carries no outcome at all. Projecting it would
        // mean guessing whether its fight finished, and guessing wrong looks exactly
        // like a completed fight.
        var trace = new ReplayTrace
        {
            Steps =
            [
                new ReplayStep
                {
                    Seq = -1, Verb = "run_start",
                    Before = new Dictionary<string, string> { ["combat.in_progress"] = "false" },
                    After = new Dictionary<string, string> { ["combat.in_progress"] = "false" },
                },
            ],
        };

        var thrown = Assert.Throws<ManifestException>(() => Project("old", trace));
        Assert.Contains("combat.outcome", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAHistoryThatNeverEntersCombat()
    {
        var trace = Trace(Step(-1, "run_start", Outside(), Outside()));

        var thrown = Assert.Throws<ManifestException>(() => Project("no-fight", trace));
        Assert.Contains("never enters combat", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAFightThatIsStillBeingFought()
    {
        // Total turns, health lost and the final health are all defined at the end of a
        // fight. Reporting them for one still in progress is the confident wrong answer
        // the whole project exists to refuse.
        var trace = Trace(
            Step(-1, "run_start", Outside(), Outside()),
            Step(0, "MapMove", Outside(), InCombat(turn: 1, playerHp: 80, enemyHp: 40)),
            Step(1, "PlayCard", InCombat(1, 80, 40), InCombat(1, 80, 34)));

        var thrown = Assert.Throws<ManifestException>(() => Project("unfinished", trace));
        Assert.Contains("still in progress", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectsTheCombatSummaryWithNoChronology()
    {
        var projection = Project("fight", CompletedFight());

        Assert.Equal("victory", projection.Summary.Outcome);
        Assert.Equal(2, projection.Summary.TotalTurns);
        Assert.Equal(80, projection.Summary.StartingHealth);
        Assert.Equal(71, projection.Summary.FinalHealth);
        Assert.Equal(9, projection.Summary.HealthLost);
        Assert.Equal(["POTION.FIRE"], projection.Summary.ConsumablesUsed);

        // Which consumable, not when. The turn it was drunk on is the other
        // projection's answer, and a summary carrying both would make every consumer
        // decide which half to trust.
        Assert.DoesNotContain(
            projection.Summary.GetType().GetProperties(),
            property => property.Name.Contains("Turn", StringComparison.Ordinal) &&
                        property.Name != "TotalTurns");
    }

    [Fact]
    public void ProjectsTheTurnDetailWithTheChronologyTheSummaryOmits()
    {
        var projection = Project("fight", CompletedFight());

        Assert.Equal([1, 2], projection.Turns.Select(turn => turn.Turn));

        var first = projection.Turns[0];
        Assert.Equal(6, first.EnemyHealthLost);
        Assert.Equal(9, first.HealthLost);
        Assert.Empty(first.ConsumablesUsed);
        Assert.Equal(["PlayCard", "EndTurn"], first.Actions.Select(action => action.Verb));
        Assert.Equal("CARD.STRIKE", first.Actions[0].Args["card_id"]);

        // The exact turn the consumable was used, which is the whole reason this
        // projection exists alongside the summary.
        var second = projection.Turns[1];
        Assert.Equal(["POTION.FIRE"], second.ConsumablesUsed);
        Assert.Equal(34, second.EnemyHealthLost);

        var serialized = System.Text.Json.JsonSerializer.Serialize(second);
        Assert.Contains("\"enemy_health_lost\"", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("\"damage_dealt\"", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void CountsTheKillingBlowAgainstTheEnemyTheEngineRemoves()
    {
        // A dead enemy is taken out of the combat state rather than left at zero
        // health, so the final step's after-sample has no enemy at all. Its remaining
        // health is what that step lost; reading the absent field as zero would lose
        // the killing blow entirely.
        var projection = Project("fight", CompletedFight());

        Assert.Equal(40, projection.Turns.Sum(turn => turn.EnemyHealthLost));
    }

    [Fact]
    public void EnemyHealthLostExcludesDamageAbsorbedByBlock()
    {
        var start = InCombat(1, 80, 40);
        start["combat.enemy.0.block"] = "5";
        var after = InCombat(1, 80, 39);
        after["combat.enemy.0.block"] = "0";
        var victory = Outside();
        victory["combat.outcome"] = "victory";
        victory["player.hp"] = "80";

        var projection = Project("blocked", Trace(
            Step(-1, "run_start", Outside(), start),
            Step(0, "PlayCard", start, after),
            Step(1, "PlayCard", after, victory)));

        Assert.Equal(40, projection.Turns.Sum(turn => turn.EnemyHealthLost));
    }

    [Fact]
    public void RepresentsPermanentCardRemoval()
    {
        var start = InCombat(1, 80, 6);
        start["player.deck"] = "CARD.STRIKE|CARD.DEFEND|CARD.PAIN";
        var end = Outside();
        end["combat.outcome"] = "victory";
        end["player.hp"] = "80";
        end["player.deck"] = "CARD.STRIKE|CARD.DEFEND";

        var projection = Project("removal", Trace(
            Step(-1, "run_start", Outside(), start),
            Step(0, "PlayCard", start, end)));

        Assert.Equal(["CARD.PAIN"], projection.Summary.CardsRemoved);
    }

    [Fact]
    public void RefusesToAttributeDamageAcrossAReIndexedEnemyRoster()
    {
        // Two enemies, one dies, the survivor shifts down an index. A hit-point delta
        // taken by index across that step is a number about two different creatures,
        // and there is nothing in the sampled state that can tell them apart.
        var before = InCombat(1, 80, 10);
        before["combat.enemy_count"] = "2";
        before["combat.enemy.1.model"] = "MONSTER.OTHER";
        before["combat.enemy.1.hp"] = "20";

        var after = InCombat(1, 80, 20);
        after["combat.enemy.0.model"] = "MONSTER.OTHER";
        after["combat.outcome"] = "in_progress";

        var end = Outside();
        end["combat.outcome"] = "victory";
        end["player.hp"] = "80";

        var thrown = Assert.Throws<ManifestException>(() => Project("multi", Trace(
            Step(-1, "run_start", Outside(), before),
            Step(0, "PlayCard", before, after),
            Step(1, "PlayCard", after, end))));

        Assert.Contains("re-index", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ComparesTwoLinesOfTheSameFightWithoutRankingThem()
    {
        var left = Project("left", CompletedFight());
        var right = Project("right", ShorterFight());

        var comparison = CombatComparison.Between(left, right);

        var turns = comparison.Summary.Single(field => field.Field == "total_turns");
        Assert.False(turns.Matches);
        Assert.Equal("2", turns.Left);
        Assert.Equal("1", turns.Right);

        // A turn only one line reached is present with the other side absent, because
        // that absence is itself the difference.
        var secondTurn = comparison.Turns.Single(turn => turn.Turn == 2);
        Assert.NotNull(secondTurn.Left);
        Assert.Null(secondTurn.Right);

        // Differences, and nothing that ranks them.
        var rendered = System.Text.Json.JsonSerializer.Serialize(comparison);
        foreach (var forbidden in new[] { "\"score\"", "\"better\"", "\"rank\"", "\"winner\"" })
        {
            Assert.DoesNotContain(forbidden, rendered, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void RefusesToCompareTwoFightsThatDidNotStartFromTheSameBoundary()
    {
        // Two different fights produce a table of differences that looks perfectly
        // reasonable and means nothing, which is exactly why this is checked rather
        // than assumed.
        var left = Project("left", CompletedFight());
        var right = Project("right", CompletedFight(encounter: "ENCOUNTER.SOMETHING_ELSE"));

        var thrown = Assert.Throws<ManifestException>(() => CombatComparison.Between(left, right));

        Assert.Contains("not the same fight", thrown.Message, StringComparison.Ordinal);
        Assert.Contains("combat.encounter", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesMatchingVisibleBoundariesWithDifferentSnapshotDigests()
    {
        var left = Project("left", CompletedFight(), SnapshotDigest("hidden-state-a"));
        var right = Project("right", CompletedFight(), SnapshotDigest("hidden-state-b"));

        var thrown = Assert.Throws<ManifestException>(() => CombatComparison.Between(left, right));

        Assert.Contains("combat-start snapshot", thrown.Message, StringComparison.Ordinal);
    }

    // ── Hand-written traces ─────────────────────────────────────────────────

    /// <summary>
    /// Two turns, one potion drunk on the second, ending in a victory that removes the
    /// enemy from the combat state the way the engine does.
    /// </summary>
    private static ReplayTrace CompletedFight(string encounter = "ENCOUNTER.TEST")
    {
        var start = InCombat(1, 80, 40, encounter);
        var afterStrike = InCombat(1, 80, 34, encounter);
        var afterEndTurn = InCombat(2, 71, 34, encounter);
        var afterPotion = InCombat(2, 71, 34, encounter);
        afterPotion["player.potions"] = "empty|empty|empty";

        var victory = Outside();
        victory["combat.outcome"] = "victory";
        victory["player.hp"] = "71";
        victory["player.potions"] = "empty|empty|empty";

        return Trace(
            Step(-1, "run_start", Outside(), Outside()),
            Step(0, "MapMove", Outside(), start),
            Step(1, "PlayCard", start, afterStrike, ("card_id", "CARD.STRIKE")),
            Step(2, "EndTurn", afterStrike, afterEndTurn),
            Step(3, "UsePotion", afterEndTurn, afterPotion),
            Step(4, "PlayCard", afterPotion, victory, ("card_id", "CARD.BASH")));
    }

    /// <summary>The same fight from the same boundary, won a turn sooner.</summary>
    private static ReplayTrace ShorterFight()
    {
        var start = InCombat(1, 80, 40);
        var victory = Outside();
        victory["combat.outcome"] = "victory";
        victory["player.hp"] = "80";

        return Trace(
            Step(-1, "run_start", Outside(), Outside()),
            Step(0, "MapMove", Outside(), start),
            Step(1, "PlayCard", start, victory, ("card_id", "CARD.BASH")));
    }

    private static Dictionary<string, string> Outside() => new(StringComparer.Ordinal)
    {
        ["combat.in_progress"] = "false",
        ["combat.outcome"] = "none",
        ["player.hp"] = "80",
        ["player.max_hp"] = "80",
        ["player.deck"] = "CARD.STRIKE|CARD.DEFEND",
        ["player.relics"] = "RELIC.NONE",
        ["player.potions"] = "POTION.FIRE|empty|empty",
    };

    private static Dictionary<string, string> InCombat(
        int turn, int playerHp, int enemyHp, string encounter = "ENCOUNTER.TEST") =>
        new(StringComparer.Ordinal)
        {
            ["combat.in_progress"] = "true",
            ["combat.outcome"] = "in_progress",
            ["combat.turn"] = turn.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["combat.encounter"] = encounter,
            ["combat.enemy_count"] = "1",
            ["combat.enemy.0.model"] = "MONSTER.TEST",
            ["combat.enemy.0.hp"] = enemyHp.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["combat.enemy.0.max_hp"] = "40",
            ["player.hp"] = playerHp.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["player.max_hp"] = "80",
            ["player.deck"] = "CARD.STRIKE|CARD.DEFEND",
            ["player.relics"] = "RELIC.NONE",
            ["player.potions"] = "POTION.FIRE|empty|empty",
        };

    private static CombatProjection Project(
        string sourceId, ReplayTrace trace, string? snapshotDigest = null) =>
        CombatProjection.FromTrace(sourceId, trace, snapshotDigest ?? SnapshotDigest("same-combat-start"));

    private static string SnapshotDigest(string state) => CanonicalState.DigestRendering(state);

    private static ReplayTrace Trace(params ReplayStep[] steps) => new() { Steps = steps };

    private static ReplayStep Step(
        int seq, string verb,
        Dictionary<string, string> before, Dictionary<string, string> after,
        params (string Key, string Value)[] args) =>
        new()
        {
            Seq = seq,
            Verb = verb,
            Args = new SortedDictionary<string, string>(
                args.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                StringComparer.Ordinal),
            Before = new Dictionary<string, string>(before, StringComparer.Ordinal),
            After = new Dictionary<string, string>(after, StringComparer.Ordinal),
        };
}

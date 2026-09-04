namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The capture of a fight somebody is playing, exercised without the game.
///
/// Every sample here is written by hand, which is the point: the capture owns the
/// lifecycle and the continuity rule, and both have to hold on inputs nobody needs
/// a game to produce - including the inputs it must refuse, which a well-behaved
/// host would never hand it.
/// </summary>
public sealed class FightCaptureTests
{
    private const string Digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    /// <summary>
    /// The combat-start boundary is a step in the trace and is nobody's action, so
    /// "has anything been played" cannot be answered by counting steps.
    /// </summary>
    [Fact]
    public void ACaptureThatHasOnlyItsBoundaryHasNothingPlayedInIt()
    {
        var capture = FightCapture.Begin("player", Sample("in_progress", 1, 64, 42), Digest);

        Assert.Single(capture.Trace.Steps);
        Assert.False(capture.AnythingPlayed);

        capture.BeginStep("PlayCard", Args(("card_id", "CARD.BASH")), Sample("in_progress", 1, 64, 42));
        capture.CompleteStep(Sample("in_progress", 1, 64, 34));

        Assert.True(capture.AnythingPlayed);
    }

    [Fact]
    public void AnActionThatBeginsWhileAnotherIsOpenClosesItWhereThatOneHadFinished()
    {
        // Measured in the retail client: one click played a held card and ended the
        // turn, so the played card had not been sampled afterwards when the ended turn
        // began. Where the engine had nothing queued, the ended turn's before-sample is
        // exactly the card's after-sample, so the card is recorded rather than the
        // whole fight refused.
        var capture = FightCapture.Begin("player", Sample("in_progress", 1, 64, 42), Digest);
        capture.BeginStep("PlayCard", Args(("card_id", "CARD.DEFEND_IRONCLAD")), Sample("in_progress", 1, 64, 42));
        capture.BeginStep("EndTurn", Args(), Sample("in_progress", 1, 64, 42), previousActionFinished: true);
        capture.CompleteStep(Sample("in_progress", 2, 55, 42));

        Assert.Equal(FightCaptureState.Live, capture.State);
        Assert.Null(capture.Refusal);
        Assert.Equal(["combat_start", "PlayCard", "EndTurn"], capture.Trace.Steps.Select(step => step.Verb));
        var card = capture.Trace.Steps[1];
        Assert.Equal(card.Before, card.After);
    }

    [Fact]
    public void AnActionThatBeginsWhileTheOneBeforeItIsStillRunningIsStillRefused()
    {
        var capture = FightCapture.Begin("player", Sample("in_progress", 1, 64, 42), Digest);
        capture.BeginStep("PlayCard", Args(("card_id", "CARD.BASH")), Sample("in_progress", 1, 64, 42));
        capture.BeginStep("EndTurn", Args(), Sample("in_progress", 1, 64, 34));

        Assert.Equal(FightCaptureState.Incomplete, capture.State);
        Assert.Contains("had not been sampled afterwards", capture.Refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public void AnActionThatBeginsAfterAGapIsStillRefusedEvenSo()
    {
        // A finished previous action does not say the fight is unchanged. A change no
        // action accounts for is still a gap.
        var capture = FightCapture.Begin("player", Sample("in_progress", 1, 64, 42), Digest);
        capture.BeginStep("PlayCard", Args(("card_id", "CARD.BASH")), Sample("in_progress", 1, 64, 42));
        capture.CompleteStep(Sample("in_progress", 1, 64, 34));
        capture.BeginStep("EndTurn", Args(), Sample("in_progress", 1, 64, 20), previousActionFinished: true);

        Assert.Equal(FightCaptureState.Incomplete, capture.State);
        Assert.Contains("with no action in between", capture.Refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public void BeginsOnlyInsideALiveFight()
    {
        var thrown = Assert.Throws<ManifestException>(() =>
            FightCapture.Begin("player", Sample(outcome: "none", turn: 0, hp: 64, enemyHp: 42), Digest));
        Assert.Contains("only begin inside a live fight", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BeginsOnlyWithTheBoundaryDigestTheComparisonWillRequire()
    {
        var thrown = Assert.Throws<ManifestException>(() =>
            FightCapture.Begin("player", Sample("in_progress", 1, 64, 42), " "));
        Assert.Contains("combat-start snapshot digest", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SamplesEitherSideOfEveryActionAndKeepsOnlyTheTracesFields()
    {
        var capture = FightCapture.Begin("player", Sample("in_progress", 1, 64, 42), Digest);

        var before = Sample("in_progress", 1, 64, 42);
        before["combat.draw_pile"] = "CARD.STRIKE_IRONCLAD";
        capture.BeginStep("PlayCard", Args(("card_id", "CARD.BASH")), before);
        capture.CompleteStep(Sample("in_progress", 1, 64, 34));

        var trace = capture.Trace;
        Assert.Equal(2, trace.Steps.Count);
        Assert.Equal(-1, trace.Steps[0].Seq);
        Assert.Equal(FightCapture.CombatStartVerb, trace.Steps[0].Verb);
        Assert.Equal(0, trace.Steps[1].Seq);
        Assert.Equal("PlayCard", trace.Steps[1].Verb);
        Assert.Equal("CARD.BASH", trace.Steps[1].Args["card_id"]);
        Assert.Equal("42", trace.Steps[1].Before["combat.enemy.0.hp"]);
        Assert.Equal("34", trace.Steps[1].After["combat.enemy.0.hp"]);
        Assert.False(trace.Steps[1].Before.ContainsKey("combat.draw_pile"));
        Assert.Equal(FightCaptureState.Live, capture.State);
        Assert.False(capture.HasOpenStep);
    }

    [Fact]
    public void TheFightEndingInsideAnActionCompletesTheCaptureAndProjectsIt()
    {
        var capture = PlayedToVictory();

        Assert.Equal(FightCaptureState.Completed, capture.State);
        Assert.Null(capture.Refusal);

        var projection = capture.Project();
        Assert.Equal("player", projection.SourceId);
        Assert.Equal(Digest, projection.CombatStartSnapshotDigest);
        Assert.Equal("victory", projection.Summary.Outcome);
        Assert.Equal(2, projection.Summary.TotalTurns);
        Assert.Equal(64, projection.Summary.StartingHealth);
        Assert.Equal(58, projection.Summary.FinalHealth);
        Assert.Equal(-6, projection.Summary.NetHealthChange);
        Assert.Equal(["POTION.BLOCK_POTION"], projection.Summary.ConsumablesUsed);
        Assert.Equal([8, 34], projection.Turns.Select(turn => turn.EnemyHealthLost));
        Assert.Equal([6, 0], projection.Turns.Select(turn => turn.HealthLost));
        Assert.Equal(["POTION.BLOCK_POTION"], projection.Turns[1].ConsumablesUsed);
    }

    [Fact]
    public void ADefeatCompletesTheCaptureToo()
    {
        var capture = FightCapture.Begin("player", Sample("in_progress", 1, 10, 42), Digest);
        capture.BeginStep("EndTurn", Args(), Sample("in_progress", 1, 10, 42));
        capture.CompleteStep(Sample("defeat", 1, 0, 42));

        Assert.Equal(FightCaptureState.Completed, capture.State);
        Assert.Equal("defeat", capture.Project().Summary.Outcome);
    }

    [Fact]
    public void AGapBetweenTwoActionsIsRefusedRatherThanBridged()
    {
        // The enemy lost eight health between one sampled action and the next with no
        // action in between. Bridging it would attribute that damage to nothing and
        // the projection would quietly under-count.
        var capture = FightCapture.Begin("player", Sample("in_progress", 1, 64, 42), Digest);
        capture.BeginStep("PlayCard", Args(), Sample("in_progress", 1, 64, 42));
        capture.CompleteStep(Sample("in_progress", 1, 64, 42));
        capture.BeginStep("PlayCard", Args(), Sample("in_progress", 1, 64, 34));

        Assert.Equal(FightCaptureState.Incomplete, capture.State);
        Assert.Contains("could not be captured completely", capture.Refusal, StringComparison.Ordinal);
        Assert.Contains("combat.enemy.0.hp", capture.Refusal, StringComparison.Ordinal);
        Assert.False(capture.HasOpenStep);

        // Nothing further is recorded, and the fight cannot be projected.
        capture.CompleteStep(Sample("victory", 1, 64, 0));
        Assert.Equal(2, capture.Trace.Steps.Count);
        var thrown = Assert.Throws<ManifestException>(capture.Project);
        Assert.Equal(capture.Refusal, thrown.Message);
    }

    [Fact]
    public void AnActionThatBeginsOverAnOpenOneIsRefused()
    {
        var capture = FightCapture.Begin("player", Sample("in_progress", 1, 64, 42), Digest);
        capture.BeginStep("PlayCard", Args(), Sample("in_progress", 1, 64, 42));
        capture.BeginStep("PlayCard", Args(), Sample("in_progress", 1, 64, 42));

        Assert.Equal(FightCaptureState.Incomplete, capture.State);
        Assert.Contains("had not been sampled afterwards", capture.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEndedTurnTheGameTookBackIsForgotten()
    {
        var capture = FightCapture.Begin("player", Sample("in_progress", 1, 64, 42), Digest);
        capture.BeginStep("EndTurn", Args(), Sample("in_progress", 1, 64, 42));
        capture.DiscardOpenStep();
        Assert.False(capture.HasOpenStep);

        capture.BeginStep("PlayCard", Args(), Sample("in_progress", 1, 64, 42));
        capture.CompleteStep(Sample("victory", 1, 64, 0));

        Assert.Equal(FightCaptureState.Completed, capture.State);
        Assert.Equal(["PlayCard"], capture.Trace.Steps.Skip(1).Select(step => step.Verb));
    }

    [Fact]
    public void TheFightEndingClosesTheOpenActionWithTheFinalState()
    {
        // The killing blow: the game reports the fight over before the action's own
        // after-sample was taken. The final state closes it.
        var capture = FightCapture.Begin("player", Sample("in_progress", 1, 64, 42), Digest);
        capture.BeginStep("PlayCard", Args(), Sample("in_progress", 1, 64, 42));
        capture.Finish(Sample("victory", 1, 64, 0));

        Assert.Equal(FightCaptureState.Completed, capture.State);
        Assert.Equal("victory", capture.Trace.Steps[^1].After["combat.outcome"]);
    }

    [Fact]
    public void TheFightEndingWithNoActionOpenIsIncomplete()
    {
        var capture = FightCapture.Begin("player", Sample("in_progress", 1, 64, 42), Digest);
        capture.Finish(Sample("victory", 1, 64, 0));

        Assert.Equal(FightCaptureState.Incomplete, capture.State);
        Assert.Contains("no action being sampled", capture.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFightEndingWithAnOpenActionStillInProgressIsIncomplete()
    {
        var capture = FightCapture.Begin("player", Sample("in_progress", 1, 64, 42), Digest);
        capture.BeginStep("PlayCard", Args(), Sample("in_progress", 1, 64, 42));
        capture.Finish(Sample("in_progress", 1, 64, 30));

        Assert.Equal(FightCaptureState.Incomplete, capture.State);
        Assert.Contains("still reads as in progress", capture.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void AbandoningKeepsWhatWasSeenAndRefusesToProjectIt()
    {
        var capture = FightCapture.Begin("player", Sample("in_progress", 1, 64, 42), Digest);
        capture.BeginStep("PlayCard", Args(), Sample("in_progress", 1, 64, 42));
        capture.CompleteStep(Sample("in_progress", 1, 64, 34));
        capture.Abandon();

        Assert.Equal(FightCaptureState.Abandoned, capture.State);
        Assert.Equal(2, capture.Trace.Steps.Count);
        Assert.Throws<ManifestException>(capture.Project);

        // Abandoned is final: a fight that was left does not come back.
        capture.BeginStep("PlayCard", Args(), Sample("in_progress", 1, 64, 34));
        capture.CompleteStep(Sample("victory", 1, 64, 0));
        Assert.Equal(FightCaptureState.Abandoned, capture.State);
        Assert.Equal(2, capture.Trace.Steps.Count);
    }

    [Fact]
    public void RefusesToProjectAFightStillBeingFought()
    {
        var capture = FightCapture.Begin("player", Sample("in_progress", 1, 64, 42), Digest);
        var thrown = Assert.Throws<ManifestException>(capture.Project);
        Assert.Contains("has not ended", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTraceIsKeptAfterTheFightsStateIsGone()
    {
        // The engine clears its combat state when a fight ends; the capture's samples
        // are copies, so what the caller does to its dictionaries afterwards changes
        // nothing the projection reads.
        var boundary = Sample("in_progress", 1, 64, 42);
        var capture = FightCapture.Begin("player", boundary, Digest);
        var before = Sample("in_progress", 1, 64, 42);
        var after = Sample("victory", 1, 58, 0);
        capture.BeginStep("PlayCard", Args(), before);
        capture.CompleteStep(after);
        boundary.Clear();
        before.Clear();
        after.Clear();

        var projection = capture.Project();
        Assert.Equal(58, projection.Summary.FinalHealth);
        Assert.Equal(42, projection.Turns[0].EnemyHealthLost);
    }

    [Fact]
    public void TheProjectionCarriesTheDigestTheComparisonRequires()
    {
        var yours = PlayedToVictory().Project();
        var theirs = PlayedToVictory("sha256:ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff").Project();

        var thrown = Assert.Throws<ManifestException>(() => CombatComparison.Between(yours, theirs));
        Assert.Contains("different complete combat-start snapshot digests", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>Two turns: a card, an ended turn that costs six, a potion and the
    /// killing blow.</summary>
    private static FightCapture PlayedToVictory(string digest = Digest)
    {
        var capture = FightCapture.Begin("player", Sample("in_progress", 1, 64, 42), digest);
        capture.BeginStep("PlayCard", Args(("card_id", "CARD.BASH")), Sample("in_progress", 1, 64, 42));
        capture.CompleteStep(Sample("in_progress", 1, 64, 34));
        capture.BeginStep("EndTurn", Args(), Sample("in_progress", 1, 64, 34));
        capture.CompleteStep(Sample("in_progress", 2, 58, 34));
        capture.BeginStep("UsePotion", Args(("potion_index", "0")), Sample("in_progress", 2, 58, 34));
        capture.CompleteStep(Sample("in_progress", 2, 58, 34, potions: "empty|empty"));
        capture.BeginStep("PlayCard", Args(("card_id", "CARD.STRIKE_IRONCLAD")), Sample("in_progress", 2, 58, 34, potions: "empty|empty"));
        capture.CompleteStep(Sample("victory", 2, 58, 0, enemies: 0, potions: "empty|empty"));
        return capture;
    }

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

    private static IReadOnlyDictionary<string, string> Args(params (string Key, string Value)[] args) =>
        args.ToDictionary(arg => arg.Key, arg => arg.Value, StringComparer.Ordinal);
}

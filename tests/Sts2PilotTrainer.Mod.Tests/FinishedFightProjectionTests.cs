using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.Rooms;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;
using static Sts2PilotTrainer.Arbiter.Tests.HeadlessRuns;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What the canonical projection says of a fight at each moment of its life, measured
/// against the real engine.
///
/// Inside a live fight everything is projected: the turn, the hand, every pile, the
/// player's block and powers, every enemy. Once the fight is over nothing of it is,
/// however much the engine still holds - the finished <c>PlayerCombatState</c> stays
/// on the player until the next fight, and the projection reads none of it - because
/// the game's save carries none of it either, and a run continued from a save has to
/// read the same as the run that was never quit. What survives the fight is the
/// room's word on how it ended, which the save does carry and a restore does put back,
/// and which leaves the reading with the room.
/// </summary>
public sealed class FinishedFightProjectionTests
{
    /// <summary>Every field of a live fight, before and after a play, so a
    /// projection that dropped the fight while it was still being fought would be
    /// caught here and not in a comparison.</summary>
    [GameFact]
    public void ALiveFightIsProjectedWhole()
    {
        WithARun(session =>
        {
            using var driver = new RunDriver(session);
            driver.ImproviseUnrecordedCardSelections();
            EnterTheFirstFight(driver, session);

            var opened = CanonicalStateProjection.Project(session.RunState).Fields;
            AssertLiveFight(opened);

            PlayOneAttack(driver, session, seq: 2);
            var afterAPlay = CanonicalStateProjection.Project(session.RunState).Fields;
            AssertLiveFight(afterAPlay);
            Assert.NotEqual(opened["combat.hand"], afterAPlay["combat.hand"]);
            Assert.NotEqual(opened["combat.enemy.0.hp"], afterAPlay["combat.enemy.0.hp"]);
        });
    }

    /// <summary>
    /// The killing play's settled reading says the fight was won and nothing else of
    /// it, though the engine still carries the fight's own state; leaving the room
    /// takes even the outcome with it. Between the two, the loot screen reads exactly
    /// as the game's fight-won save restores it: the same room, marked finished, and
    /// no fight.
    /// </summary>
    [GameFact]
    public void AFinishedFightLeavesOnlyItsOutcomeAndOnlyWhileTheRunStandsInItsRoom()
    {
        WithARun(session =>
        {
            using var driver = new RunDriver(session);
            driver.ImproviseUnrecordedCardSelections();
            EnterTheFirstFight(driver, session);
            var seq = PlayToVictory(driver, session, firstSeq: 2);

            // The engine has not let go of the fight: the projection has.
            var player = session.RunState.Players[0];
            Assert.NotNull(player.PlayerCombatState);
            Assert.True(player.PlayerCombatState!.TurnNumber > 1);
            Assert.False(CombatManager.Instance!.IsInProgress);
            Assert.True(session.RunState.CurrentRoom is CombatRoom { IsPreFinished: true });

            var won = CanonicalStateProjection.Project(session.RunState).Fields;
            Assert.Equal("false", won["combat.in_progress"]);
            Assert.Equal("victory", won["combat.outcome"]);
            Assert.Equal(["combat.in_progress", "combat.outcome"], CombatFields(won));

            // The loot taken changes the player and nothing about the fight.
            if (driver.UnclaimedRewardKinds.Contains("gold", StringComparer.Ordinal))
            {
                driver.Apply(Record(seq++, ActionVerb.ClaimReward, ("reward_type", "gold")));
            }

            if (driver.UnclaimedRewardKinds.Count > 0) driver.Apply(Record(seq++, ActionVerb.SkipRewards));
            var lootTaken = CanonicalStateProjection.Project(session.RunState).Fields;
            Assert.Equal("victory", lootTaken["combat.outcome"]);
            Assert.Equal(["combat.in_progress", "combat.outcome"], CombatFields(lootTaken));

            // Leaving the room leaves the fight behind entirely, whatever the next
            // room is; a fight the next room deals is that fight, live.
            var (row, column) = NextNode(session);
            driver.Apply(Record(seq, ActionVerb.MapMove,
                ("act", Number(session.RunState.CurrentActIndex)), ("row", Number(row)), ("column", Number(column))));
            var arrived = CanonicalStateProjection.Project(session.RunState).Fields;
            Assert.NotNull(player.PlayerCombatState);
            if (arrived["combat.in_progress"] == "true")
            {
                AssertLiveFight(arrived);
            }
            else
            {
                Assert.Equal("none", arrived["combat.outcome"]);
                Assert.Equal(["combat.in_progress", "combat.outcome"], CombatFields(arrived));
            }
        });
    }

    /// <summary>A player who is dead reads as a defeat off the player, whatever the
    /// room says: no save carries a dead run and no restore produces one, so it is
    /// the run's own fact and outranks the room's finished mark.</summary>
    [GameFact]
    public void ADeadPlayerReadsAsADefeatOffThePlayer()
    {
        WithARun(session =>
        {
            using var driver = new RunDriver(session);
            driver.ImproviseUnrecordedCardSelections();
            EnterTheFirstFight(driver, session);
            PlayToVictory(driver, session, firstSeq: 2);
            Assert.Equal("victory", Field(session, "combat.outcome"));

            var creature = session.RunState.Players[0].Creature!;
            typeof(Creature).GetProperty(nameof(Creature.CurrentHp))!.SetValue(creature, 0);
            Assert.False(creature.IsAlive);

            var fields = CanonicalStateProjection.Project(session.RunState).Fields;
            Assert.Equal("false", fields["combat.in_progress"]);
            Assert.Equal("defeat", fields["combat.outcome"]);
            Assert.Equal(["combat.in_progress", "combat.outcome"], CombatFields(fields));
        });
    }

    /// <summary>
    /// The fight ends once no living enemy is primary, and an enemy is secondary
    /// while it carries a power whose <c>OwnerIsSecondaryEnemy</c> is set - so a
    /// minion can be standing at a win, unsampled with the rest of the finished
    /// fight. <see cref="CombatProjection.SecondaryEnemyPowers"/> is that set
    /// transcribed, and this is what holds the transcription to the engine's own
    /// power models: a build that adds one fails here by name.
    /// </summary>
    [GameFact]
    public void TheComparisonsSecondaryEnemyPowersAreTheEnginesOwn()
    {
        WithARun(_ =>
        {
            var engine = ModelDb.AllPowers
                .Where(power => power.OwnerIsSecondaryEnemy)
                .Select(power => power.Id.ToString())
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

            Assert.NotEmpty(engine);
            Assert.Equal(engine, CombatProjection.SecondaryEnemyPowers.OrderBy(id => id, StringComparer.Ordinal));
        });
    }

    /// <summary>
    /// An enemy can leave a fight alive only during the enemy side of the turn, which
    /// is the whole of what lets the comparison credit a fight the player's own action
    /// ended as a kill and leave one that ended inside an end of turn without a number.
    /// Held to the engine: every call site of <see cref="CreatureCmd.Escape"/> in this
    /// build, read off the game's own IL, is either a monster's move - a method the
    /// monster registers as a <see cref="MoveState"/>'s action - or a model's override
    /// of one of the turn hooks. A build that lets a card or a potion send an enemy
    /// fleeing fails here by name rather than crediting its health as damage.
    /// </summary>
    [GameFact]
    public void AnEnemyLeavesAFightAliveOnlyDuringTheEnemySideOfTheTurn()
    {
        var escape = typeof(CreatureCmd).GetMethod(nameof(CreatureCmd.Escape))
            ?? throw new InvalidOperationException("CreatureCmd.Escape is not on this build.");
        var sites = ChoiceEntryPoints.MethodsNaming(escape)
            .Select(ChoiceEntryPoints.DeclaredMember)
            .Distinct()
            .ToList();

        Assert.NotEmpty(sites);
        Assert.All(sites, site => Assert.True(
            IsAMonsterMove(site) || IsATurnHook(site),
            $"{site.DeclaringType?.FullName}.{site.Name} sends a creature out of the fight and is neither a " +
            "monster's move nor a turn hook."));
    }

    private static bool IsAMonsterMove(MethodBase site) =>
        site.DeclaringType is { } monster &&
        monster.IsSubclassOf(typeof(MonsterModel)) &&
        ChoiceEntryPoints.MethodsNaming(site).Any(registrar =>
            registrar.DeclaringType == monster &&
            ChoiceEntryPoints.Callees(registrar).Any(callee =>
                callee is ConstructorInfo && callee.DeclaringType == typeof(MoveState)));

    private static bool IsATurnHook(MethodBase site) =>
        site is MethodInfo method &&
        method.GetBaseDefinition().DeclaringType == typeof(AbstractModel) &&
        method.Name.Contains("Turn", StringComparison.Ordinal);

    private static void AssertLiveFight(IReadOnlyDictionary<string, string> fields)
    {
        Assert.Equal("true", fields["combat.in_progress"]);
        Assert.Equal("in_progress", fields["combat.outcome"]);
        foreach (var field in new[]
                 {
                     "combat.turn", "combat.phase", "combat.energy", "combat.max_energy", "combat.hand",
                     "combat.draw_pile", "combat.discard_pile", "combat.exhaust_pile", "combat.play_pile",
                     "combat.hand_count", "combat.draw_pile_count", "combat.discard_pile_count", "combat.block",
                     "combat.player_hp", "combat.player_powers", "combat.round", "combat.encounter",
                     "combat.enemy_count", "combat.enemy.0.model", "combat.enemy.0.hp", "combat.enemy.0.max_hp",
                     "combat.enemy.0.intent",
                 })
        {
            Assert.True(fields.ContainsKey(field), $"a live fight is projected without {field}");
        }
    }

    private static List<string> CombatFields(IReadOnlyDictionary<string, string> fields) =>
        fields.Keys.Where(key => key.StartsWith("combat.", StringComparison.Ordinal)).Order(StringComparer.Ordinal).ToList();

    private static void PlayOneAttack(RunDriver driver, GameSession session, int seq)
    {
        var hand = session.RunState.Players[0].PlayerCombatState!.Hand.Cards;
        var index = Enumerable.Range(0, hand.Count)
            .First(i => hand[i].Type == CardType.Attack && hand[i].CanPlay(out _, out _));
        driver.Apply(Record(seq, ActionVerb.PlayCard,
            ("card_id", hand[index].Id.ToString()), ("hand_index", Number(index))));
    }

    /// <summary>Attacks first and ends the turn when nothing can be played, the way
    /// every headless walk here wins the Ironclad's first fight; returns the next
    /// free sequence number.</summary>
    private static int PlayToVictory(RunDriver driver, GameSession session, int firstSeq)
    {
        var seq = firstSeq;
        for (var turn = 0; turn < 40 && Field(session, "combat.outcome") == "in_progress"; turn++)
        {
            while (Field(session, "combat.outcome") == "in_progress")
            {
                var hand = session.RunState.Players[0].PlayerCombatState!.Hand.Cards;
                var playable = Enumerable.Range(0, hand.Count).Where(i => hand[i].CanPlay(out _, out _)).ToList();
                var index = playable.FirstOrDefault(i => hand[i].Type == CardType.Attack, playable.Count > 0 ? playable[0] : -1);
                if (index < 0) break;
                driver.Apply(Record(seq++, ActionVerb.PlayCard,
                    ("card_id", hand[index].Id.ToString()), ("hand_index", Number(index))));
            }

            if (Field(session, "combat.outcome") != "in_progress") break;
            driver.Apply(Record(seq++, ActionVerb.EndTurn));
        }

        Assert.Equal("victory", Field(session, "combat.outcome"));
        return seq;
    }

    private static (int Row, int Column) NextNode(GameSession session)
    {
        var current = Field(session, "run.map_coord");
        var separator = current.IndexOf('c');
        var row = int.Parse(current.AsSpan(1, separator - 1), System.Globalization.CultureInfo.InvariantCulture);
        var column = int.Parse(current.AsSpan(separator + 1), System.Globalization.CultureInfo.InvariantCulture);
        var edge = session.CurrentMapTopology().Edges
            .Where(candidate => candidate.FromRow == row && candidate.FromColumn == column)
            .OrderBy(candidate => candidate.ToColumn)
            .First();
        return (edge.ToRow, edge.ToColumn);
    }
}

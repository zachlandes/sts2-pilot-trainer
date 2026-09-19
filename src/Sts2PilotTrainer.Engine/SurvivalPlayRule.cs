using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The mechanical play rule a driven fight uses, under either <see cref="SurvivalRule"/>:
/// the measured one, R3 as the survival scout measured it over 300 hunted seeds per
/// character, or the attack-first rule before it.
///
/// A rule over the hand the engine dealt, the enemies' displayed intents and the
/// player's own state, read through the engine's own members, and never a judgement
/// about how to play: under <see cref="SurvivalRule.BlockWhenThreatened"/>, block
/// while the number drawn over the enemies exceeds the player's block, else the first
/// playable attack aimed at the living enemy with the least health, else the first
/// playable card; under <see cref="SurvivalRule.AttackFirst"/>, the first playable
/// attack aimed at the first living enemy, else the first playable card. Nothing
/// playable ends the turn. It is here rather than in either driver that plays it
/// because two hosts play it - the retail soak inside the client, through the actions
/// a click issues, and the whole-act walk through the headless driver - and a rule
/// copied into each is two rules the moment one is tuned; the walk's own
/// <c>SurvivingIndex</c> reads this and derives nothing.
/// </summary>
public static class SurvivalPlayRule
{
    /// <summary>One play: which card of the hand, at which enemy, or null for a card
    /// that takes no target.</summary>
    public readonly record struct Play(int HandIndex, Creature? Target);

    /// <summary>
    /// The next play from this hand, or null where nothing is playable and the turn
    /// ends.
    /// </summary>
    /// <param name="rule">Which of the two lines to play.</param>
    /// <param name="player">Whose hand, block and health.</param>
    /// <param name="enemies">The fight's enemies in the engine's own order, dead ones
    /// included; only the living ones intend anything or take a target.</param>
    public static Play? Next(SurvivalRule rule, Player player, IEnumerable<Creature?> enemies)
    {
        if (player.PlayerCombatState is not { } state) return null;

        var hand = state.Hand.Cards;
        var playable = Enumerable.Range(0, hand.Count).Where(index => hand[index].CanPlay(out _, out _)).ToList();
        if (playable.Count == 0) return null;

        var living = Living(enemies);
        if (rule == SurvivalRule.BlockWhenThreatened && IncomingDamage(player.Creature, living) > player.Creature.Block)
        {
            var block = playable.FirstOrDefault(index => hand[index].GainsBlock, -1);
            if (block >= 0) return new Play(block, TargetFor(rule, hand[block].TargetType, living));
        }

        var attack = playable.FirstOrDefault(index => hand[index].Type == CardType.Attack, -1);
        var chosen = attack >= 0 ? attack : playable[0];
        return new Play(chosen, TargetFor(rule, hand[chosen].TargetType, living));
    }

    /// <summary>The enemies of the roster still alive, in the engine's own order, which
    /// is the order the driver resolves a <c>target_index</c> against.</summary>
    public static IReadOnlyList<Creature> Living(IEnumerable<Creature?> enemies) =>
        enemies.Where(enemy => enemy is { IsAlive: true }).Select(enemy => enemy!).ToList();

    /// <summary>The number the game draws over the enemies: every attack intent of
    /// every living enemy, summed, aimed at this player.</summary>
    public static int IncomingDamage(Creature target, IReadOnlyList<Creature> living)
    {
        var targets = new[] { target };
        return living.Sum(enemy =>
            enemy.Monster?.NextMove?.Intents?.OfType<AttackIntent>().Sum(intent => intent.GetTotalDamage(targets, enemy)) ?? 0);
    }

    /// <summary>The target anything that aims the way a card does takes under this
    /// rule - a potion thrown at an enemy takes the same one: the living enemy at
    /// <see cref="TargetIndex"/>, or null for something that aims at nothing or where
    /// nobody is alive.</summary>
    public static Creature? TargetFor(SurvivalRule rule, TargetType aim, IEnumerable<Creature?> enemies)
    {
        if (aim != TargetType.AnyEnemy) return null;
        var living = Living(enemies);
        var index = TargetIndex(rule, living);
        return index < 0 ? null : living[index];
    }

    /// <summary>Which living enemy, by position among the living, a card aimed at one
    /// takes: under <see cref="SurvivalRule.BlockWhenThreatened"/> the one with the
    /// least health, earliest on ties; under <see cref="SurvivalRule.AttackFirst"/> the
    /// first; -1 where nobody is alive. A rule over the roster and not a choice about
    /// which to hit.</summary>
    public static int TargetIndex(SurvivalRule rule, IReadOnlyList<Creature> living)
    {
        if (living.Count == 0) return -1;
        if (rule == SurvivalRule.AttackFirst) return 0;

        var weakest = 0;
        for (var index = 1; index < living.Count; index++)
        {
            if (living[index].CurrentHp < living[weakest].CurrentHp) weakest = index;
        }

        return weakest;
    }
}

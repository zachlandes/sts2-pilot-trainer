using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The mechanical play rule a driven fight uses: the survival line's rule R3, as the
/// survival scout measured it over 300 hunted seeds per character.
///
/// A rule over the hand the engine dealt, the enemies' displayed intents and the
/// player's own state, read through the engine's own members, and never a judgement
/// about how to play: block while the number drawn over the enemies exceeds the
/// player's block, else attack the enemy with the least health, else the first
/// playable card, and end the turn when nothing is playable. It is here rather than in
/// the driver that plays it because two hosts play it - the retail soak inside the
/// client, through the actions a click issues, and a headless walk through the driver -
/// and a rule copied into each is two rules the moment one is tuned.
///
/// The whole-act walk's own <c>SurvivingIndex</c> is the older rule R0 (first playable
/// attack, else first playable card) until the survival work adopts this one; the two
/// differ in what they play and not in what they are, a function of the engine's state.
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
    /// <param name="player">Whose hand, block and health.</param>
    /// <param name="enemies">The fight's enemies in the engine's own order, dead ones
    /// included; only the living ones intend anything or take a target.</param>
    public static Play? Next(Player player, IReadOnlyList<Creature> enemies)
    {
        if (player.PlayerCombatState is not { } state) return null;

        var hand = state.Hand.Cards;
        var playable = Enumerable.Range(0, hand.Count).Where(index => hand[index].CanPlay(out _, out _)).ToList();
        if (playable.Count == 0) return null;

        var living = enemies.Where(enemy => enemy is { IsAlive: true }).ToList();
        if (IncomingDamage(player.Creature, living) > player.Creature.Block)
        {
            var block = playable.FirstOrDefault(index => hand[index].GainsBlock, -1);
            if (block >= 0) return new Play(block, TargetFor(hand[block], living));
        }

        var attack = playable.FirstOrDefault(index => hand[index].Type == CardType.Attack, -1);
        var chosen = attack >= 0 ? attack : playable[0];
        return new Play(chosen, TargetFor(hand[chosen], living));
    }

    /// <summary>The number the game draws over the enemies: every attack intent of
    /// every living enemy, summed, aimed at this player.</summary>
    public static int IncomingDamage(Creature target, IReadOnlyList<Creature> living)
    {
        var targets = new[] { target };
        return living.Sum(enemy =>
            enemy.Monster?.NextMove?.Intents?.OfType<AttackIntent>().Sum(intent => intent.GetTotalDamage(targets, enemy)) ?? 0);
    }

    /// <summary>The target a card aimed at an enemy takes under this rule: the living,
    /// hittable enemy with the least health, earliest on ties; null for a card that
    /// aims at nothing or where no enemy can be hit.</summary>
    public static Creature? TargetFor(CardModel card, IReadOnlyList<Creature> living) =>
        TargetFor(card.TargetType, living);

    /// <summary>The same, for anything that aims the way a card does - a potion thrown
    /// at an enemy takes the same target.</summary>
    public static Creature? TargetFor(TargetType aim, IReadOnlyList<Creature> living)
    {
        if (aim != TargetType.AnyEnemy) return null;

        Creature? weakest = null;
        foreach (var enemy in living.Where(enemy => enemy.IsHittable))
        {
            if (weakest is null || enemy.CurrentHp < weakest.CurrentHp) weakest = enemy;
        }

        return weakest;
    }
}

using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// A headless fight of one named encounter, entered the way the game's own console
/// jumps to one (<c>FightConsoleCmd</c>, through <c>RunManager.EnterRoomDebug</c>),
/// on a run of any character, with the pieces a regression about one card, one power
/// or one enemy needs staged into it through the engine's own commands.
///
/// A test helper and not a product path: no recording is made of a fight entered this
/// way, and the recorder would mark a run the console was used in non-standard. It
/// exists so a regression can name the encounter and the card it is about instead of
/// hunting a seed whose route happens to deal them.
/// </summary>
internal static class HeadlessFights
{
    /// <summary>A seed the runs here are of; nothing about it is load-bearing.</summary>
    private const string Seed = "P1L0TTRA1NER";

    /// <summary>A run of the character, stood in the named encounter with its first
    /// turn open, taken away again afterwards.</summary>
    internal static void InAFight(string character, string encounterId, Action<GameSession, RunDriver> body)
    {
        HeadlessRuns.EndAnyRun();
        var session = new GameSession();
        try
        {
            session.StartRun(Seed, character, 0, "standard", ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"]);
            using var driver = new RunDriver(session);
            driver.EnterFirstRoom();
            driver.Apply(HeadlessRuns.Record(0, ActionVerb.ChooseNeowBlessing, ("option_index", "0")));

            var encounter = ModelDb.GetById<EncounterModel>(new ModelId(ModelId.SlugifyCategory<EncounterModel>(), encounterId["ENCOUNTER.".Length..])).ToMutable();
            RunManager.Instance.EnterRoomDebug(RoomType.Monster, MapPointType.Monster, encounter, showTransition: false)
                .GetAwaiter().GetResult();
            Pump.Drain();
            Assert.Equal("true", HeadlessRuns.Field(session, "combat.in_progress"));
            Assert.Equal(encounterId, HeadlessRuns.Field(session, "combat.encounter"));

            body(session, driver);
        }
        finally
        {
            HeadlessRuns.EndAnyRun();
            HeadlessEngine.Forget();
        }
    }

    /// <summary>Puts one generated card in the hand the way a card that creates one
    /// does: created into the combat state, then added through the engine's command,
    /// at the end of the hand or at the position named.</summary>
    internal static CardModel Deal(CardModel canonical, Player player, CardPilePosition position = CardPilePosition.Bottom)
    {
        var card = CombatManager.Instance!.DebugOnlyGetState()!.CreateCard(canonical, player);
        CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player, position).GetAwaiter().GetResult();
        Pump.Drain();
        return card;
    }

    /// <summary>Plays a card in hand through the driver, as a recording would.</summary>
    internal static void Play(RunDriver driver, GameSession session, CardModel card, int seq)
    {
        var hand = session.RunState.Players[0].PlayerCombatState!.Hand.Cards;
        var handIndex = hand.ToList().IndexOf(card);
        Assert.True(handIndex >= 0, $"{card.Id} is not in hand: {string.Join(", ", hand.Select(held => held.Id))}");
        var args = new List<(string, string)> { ("card_id", card.Id.ToString()), ("hand_index", HeadlessRuns.Number(handIndex)) };
        if (card.TargetType == TargetType.AnyEnemy && LivingEnemies().Count > 1) args.Add(("target_index", "0"));
        driver.Apply(HeadlessRuns.Record(seq, ActionVerb.PlayCard, [.. args]));
    }

    internal static IReadOnlyList<Creature> LivingEnemies() =>
        CombatManager.Instance!.DebugOnlyGetState()!.Enemies.Where(enemy => enemy is { IsAlive: true }).ToList();

    /// <summary>Wounds an enemy down to the health named, through the engine's own
    /// damage command, the way the console's damage command does.</summary>
    internal static void WoundTo(Creature enemy, int health, Creature dealer)
    {
        var amount = enemy.CurrentHp - health;
        Assert.True(amount > 0, $"{enemy.Monster?.Id} has {enemy.CurrentHp} health, not more than {health}");
        CreatureCmd.Damage(new BlockingPlayerChoiceContext(), [enemy], amount, ValueProp.Unpowered, dealer)
            .GetAwaiter().GetResult();
        Pump.Drain();
        Assert.Equal(health, enemy.CurrentHp);
    }

    /// <summary>Every line the game logged at error level while the body ran.</summary>
    internal static IReadOnlyList<string> ErrorsLoggedDuring(Action body)
    {
        var errors = new List<string>();
        void Capture(LogLevel level, string text, int skipFrames)
        {
            if (level == LogLevel.Error) errors.Add(text);
        }

        Log.LogCallback += Capture;
        try
        {
            body();
        }
        finally
        {
            Log.LogCallback -= Capture;
        }

        return errors;
    }
}

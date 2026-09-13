using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.TestSupport;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The list the recorder derives for a card prompt is the list the engine hands its
/// own seam, element for element, at every entry point that reaches a screen.
///
/// <see cref="CardPromptOffers"/> transcribes each entry point's selector branch, and a
/// transcription is safe only while something holds it to its source. So each of these
/// asks the real engine through one entry point, inside a real fight, with a selector
/// on the stack that records exactly what it was handed - and asserts that the prompt
/// <see cref="CardPrompts"/> announced offers the same cards in the same order, and
/// that what came back is what the selector answered. A build that changes one entry
/// point's expression fails here by name.
///
/// The selector is what makes the same patch fire headlessly as in the client: the
/// entry point takes its selector branch, which reads at the prefix rather than at the
/// pause, and hands the list to the seam instead of to a screen. What the recording
/// writes a position against is that list.
/// </summary>
public sealed class CardPromptOfferTests
{
    private const int TakesOne = 1;

    public CardPromptOfferTests()
    {
        // The prompts are asked with the game's own types in each test's signature, so
        // the game assembly has to be resolvable before a test body is compiled. It
        // reaches the default context only once something has started the host.
        _ = EngineHost.StartupPhase();
    }

    [GameFact]
    public void FromCombatPileOverTheDiscardOffersThePileFiltered() => InTheFirstFight((player, ask) =>
    {
        var combat = player.PlayerCombatState!;
        Discard(combat.Hand.Cards[0]);
        Discard(combat.Hand.Cards[0]);
        Discard(combat.Hand.Cards[0]);
        Assert.True(combat.DiscardPile.Cards.Count >= 3);

        var asked = ask(nameof(CardSelectCmd.FromCombatPile), () => CardSelectCmd.FromCombatPile(
            new BlockingPlayerChoiceContext(), combat.DiscardPile, player, Prefs(TakesOne), filter: null));

        asked.OffersWhatTheEngineHandedItsSeam();
        Assert.Equal(nameof(MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCombatPileCardSelectScreen), asked.Prompt.Screen);
    });

    /// <summary>The draw pile is the one list the seam re-orders, by rarity then id,
    /// and the pile's own order is what the sort is stable over.</summary>
    [GameFact]
    public void FromCombatPileOverTheDrawPileOffersItSortedByRarityThenId() => InTheFirstFight((player, ask) =>
    {
        var combat = player.PlayerCombatState!;
        Assert.True(combat.DrawPile.Cards.Count >= 3);

        var asked = ask(nameof(CardSelectCmd.FromCombatPile), () => CardSelectCmd.FromCombatPile(
            new BlockingPlayerChoiceContext(), combat.DrawPile, player, Prefs(TakesOne),
            card => card.Type == CardType.Attack));

        asked.OffersWhatTheEngineHandedItsSeam();
        Assert.All(asked.Offered, card => Assert.Equal(CardType.Attack, card.Type));
        Assert.Equal(
            asked.Offered.OrderBy(card => card.Rarity).ThenBy(card => card.Id).ToList(),
            asked.Offered);
    });

    /// <summary>The four-argument overload forwards to the five-argument one, so the
    /// prompt is observed once, at the entry point that reaches the screen.</summary>
    [GameFact]
    public void FromCombatPileWithoutAFilterIsObservedOnceAtTheOverloadItForwardsTo() => InTheFirstFight((player, ask) =>
    {
        var combat = player.PlayerCombatState!;
        Discard(combat.Hand.Cards[0]);
        Discard(combat.Hand.Cards[0]);

        var asked = ask(nameof(CardSelectCmd.FromCombatPile), () => CardSelectCmd.FromCombatPile(
            new BlockingPlayerChoiceContext(), combat.DiscardPile, player, Prefs(TakesOne)));

        asked.OffersWhatTheEngineHandedItsSeam();
        Assert.Equal(1, asked.Announced);
    });

    [GameFact]
    public void FromHandOffersTheHandFiltered() => InTheFirstFight((player, ask) =>
    {
        var combat = player.PlayerCombatState!;
        Assert.True(combat.Hand.Cards.Count >= 3);

        var asked = ask(nameof(CardSelectCmd.FromHand), () => CardSelectCmd.FromHand(
            new BlockingPlayerChoiceContext(), player, Prefs(TakesOne), filter: null, source: combat.Hand.Cards[0]));

        asked.OffersWhatTheEngineHandedItsSeam();
        Assert.Equal(combat.Hand.Cards, asked.Offered);
    });

    /// <summary>The discard prompt sets its own glow rule on the prefs and forwards to
    /// <c>FromHand</c>; a filter reaches the seam as a filtered hand.</summary>
    [GameFact]
    public void FromHandForDiscardIsObservedAtFromHandWithItsFilter() => InTheFirstFight((player, ask) =>
    {
        var combat = player.PlayerCombatState!;

        var asked = ask(nameof(CardSelectCmd.FromHand), () => CardSelectCmd.FromHandForDiscard(
            new BlockingPlayerChoiceContext(), player, Prefs(TakesOne),
            card => card.Type != CardType.Attack, source: combat.Hand.Cards[0]));

        asked.OffersWhatTheEngineHandedItsSeam();
        Assert.Equal(1, asked.Announced);
        Assert.All(asked.Offered, card => Assert.NotEqual(CardType.Attack, card.Type));
    });

    [GameFact]
    public void FromHandForUpgradeOffersTheUpgradableHandAndAsksForOne() => InTheFirstFight((player, ask) =>
    {
        var combat = player.PlayerCombatState!;
        Assert.True(combat.Hand.Cards.Count(card => card.IsUpgradable) >= 2);

        var asked = ask(nameof(CardSelectCmd.FromHandForUpgrade), () => Single(CardSelectCmd.FromHandForUpgrade(
            new BlockingPlayerChoiceContext(), player, source: combat.Hand.Cards[0])));

        asked.OffersWhatTheEngineHandedItsSeam();
        Assert.Equal(1, asked.Prompt.MinSelect);
        Assert.Equal(1, asked.Prompt.MaxSelect);
    });

    [GameFact]
    public void FromChooseACardScreenOffersTheCardsAsGiven() => InTheFirstFight((player, ask) =>
    {
        var cards = ModelDb.AllCards.Where(card => card.Type == CardType.Attack).Take(3)
            .Select(card => card.ToMutable()).ToList();

        var asked = ask(nameof(CardSelectCmd.FromChooseACardScreen), () => Single(
            CardSelectCmd.FromChooseACardScreen(new BlockingPlayerChoiceContext(), cards, player, canSkip: true)));

        asked.OffersWhatTheEngineHandedItsSeam();
        Assert.Equal(0, asked.Prompt.MinSelect);
        Assert.Equal(1, asked.Prompt.MaxSelect);
    });

    [GameFact]
    public void FromSimpleGridOffersTheCardsAsGiven() => InTheFirstFight((player, ask) =>
    {
        var cards = player.Deck.Cards.Take(4).ToList();

        var asked = ask(nameof(CardSelectCmd.FromSimpleGrid), () => CardSelectCmd.FromSimpleGrid(
            new BlockingPlayerChoiceContext(), cards, player, Prefs(TakesOne)));

        asked.OffersWhatTheEngineHandedItsSeam();
    });

    [GameFact]
    public void FromSimpleGridForRewardsOffersTheCreatedCardsInTheOrderGiven() => InTheFirstFight((player, ask) =>
    {
        var created = ModelDb.AllCards.Where(card => card.Type == CardType.Skill).Take(4)
            .Select(card => new CardCreationResult(card.ToMutable())).ToList();

        var asked = ask(nameof(CardSelectCmd.FromSimpleGridForRewards), () => CardSelectCmd.FromSimpleGridForRewards(
            new BlockingPlayerChoiceContext(), created, player, Prefs(TakesOne)));

        asked.OffersWhatTheEngineHandedItsSeam();
        Assert.Equal(created.Select(result => result.Card).ToList(), asked.Offered);
    });

    [GameFact]
    public void FromDeckForUpgradeOffersTheUpgradableDeckInDeckOrder() => InTheFirstFight((player, ask) =>
    {
        var asked = ask(nameof(CardSelectCmd.FromDeckForUpgrade), () =>
            CardSelectCmd.FromDeckForUpgrade(player, Prefs(TakesOne)));

        asked.OffersWhatTheEngineHandedItsSeam();
        Assert.Equal(player.Deck.Cards.Where(card => card.IsUpgradable).ToList(), asked.Offered);
    });

    [GameFact]
    public void FromDeckForTransformationOffersTheTransformableDeckInDeckOrder() => InTheFirstFight((player, ask) =>
    {
        var asked = ask(nameof(CardSelectCmd.FromDeckForTransformation), () =>
            CardSelectCmd.FromDeckForTransformation(player, Prefs(TakesOne)));

        asked.OffersWhatTheEngineHandedItsSeam();
    });

    /// <summary>The enchantment prompt is asked with cards already filtered; the seam
    /// gets them back in deck order, whatever order they were given in.</summary>
    [GameFact]
    public void FromDeckForEnchantmentOffersTheGivenCardsInDeckOrder() => InTheFirstFight((player, ask) =>
    {
        var enchantment = ModelDb.Enchantment<Adroit>();
        var enchantable = player.Deck.Cards.Where(enchantment.CanEnchant).ToList();
        Assert.True(enchantable.Count >= 3);
        var given = enchantable.AsEnumerable().Reverse().ToList();

        var asked = ask(nameof(CardSelectCmd.FromDeckForEnchantment), () =>
            CardSelectCmd.FromDeckForEnchantment(given, enchantment, 1, Prefs(TakesOne)));

        asked.OffersWhatTheEngineHandedItsSeam();
        Assert.Equal(enchantable, asked.Offered);
    });

    /// <summary>Both player overloads forward to the one that takes the cards, so the
    /// prompt is observed once.</summary>
    [GameFact]
    public void FromDeckForEnchantmentByPlayerIsObservedOnceAtTheOverloadItForwardsTo() => InTheFirstFight((player, ask) =>
    {
        var enchantment = ModelDb.Enchantment<Adroit>();

        var asked = ask(nameof(CardSelectCmd.FromDeckForEnchantment), () =>
            CardSelectCmd.FromDeckForEnchantment(player, enchantment, 1, Prefs(TakesOne)));

        asked.OffersWhatTheEngineHandedItsSeam();
        Assert.Equal(1, asked.Announced);
    });

    [GameFact]
    public void FromDeckGenericOffersTheDeckFilteredAndStablySorted() => InTheFirstFight((player, ask) =>
    {
        var asked = ask(nameof(CardSelectCmd.FromDeckGeneric), () => CardSelectCmd.FromDeckGeneric(
            player, Prefs(TakesOne), card => card.Type != CardType.Power, card => card.Type == CardType.Attack ? 0 : 1));

        asked.OffersWhatTheEngineHandedItsSeam();
        var attacks = asked.Offered.TakeWhile(card => card.Type == CardType.Attack).Count();
        Assert.All(asked.Offered.Skip(attacks), card => Assert.NotEqual(CardType.Attack, card.Type));
    });

    /// <summary>A removal forwards to the generic deck prompt with curses first and
    /// the rest in deck order.</summary>
    [GameFact]
    public void FromDeckForRemovalIsObservedAtFromDeckGenericWithCursesFirst() => InTheFirstFight((player, ask) =>
    {
        var asked = ask(nameof(CardSelectCmd.FromDeckGeneric), () =>
            CardSelectCmd.FromDeckForRemoval(player, Prefs(TakesOne)));

        asked.OffersWhatTheEngineHandedItsSeam();
        Assert.Equal(1, asked.Announced);
        Assert.Equal(player.Deck.Cards.Where(card => card.IsRemovable).ToList(), asked.Offered);
    });

    /// <summary>
    /// A prompt the engine answers for itself is opened and derived to nothing: the
    /// selector is never asked, and the prompt says the engine answered. Here the
    /// discard holds one card and the prompt takes one without confirmation.
    /// </summary>
    [GameFact]
    public void APromptTheEngineAutoPicksIsDerivedToNoDecision() => InTheFirstFight((player, ask) =>
    {
        var combat = player.PlayerCombatState!;
        Discard(combat.Hand.Cards[0]);
        Assert.Single(combat.DiscardPile.Cards);

        var asked = ask(nameof(CardSelectCmd.FromCombatPile), () => CardSelectCmd.FromCombatPile(
            new BlockingPlayerChoiceContext(), combat.DiscardPile, player, Prefs(TakesOne), filter: null));

        Assert.Null(asked.Selector.Options);
        Assert.Equal(CardPrompts.PromptState.EngineAnswered, asked.Prompt.State);
        Assert.Null(asked.Prompt.Offered);
        Assert.Single(asked.Answer);
    });

    /// <summary>
    /// A selector of the game's own is the engine answering itself, in both hosts, so
    /// no prompt is opened for it at all. Stood in for here by the game's own type
    /// pushed above the recording one.
    /// </summary>
    [GameFact]
    public void APromptTheGamesOwnSelectorAnswersOpensNothing() => InTheFirstFight((player, ask) =>
    {
        var combat = player.PlayerCombatState!;
        Discard(combat.Hand.Cards[0]);
        Discard(combat.Hand.Cards[0]);

        Assert.True(CardPrompts.IsTheGamesOwn(new VakuuCardSelector()));
        Assert.False(CardPrompts.IsTheGamesOwn(new Recording()));

        var announced = 0;
        var previous = CardPrompts.Answered;
        CardPrompts.Answered = (_, _) => announced++;
        try
        {
            using (CardSelectCmd.PushSelector(new VakuuCardSelector()))
            {
                var chosen = CardSelectCmd.FromCombatPile(
                        new BlockingPlayerChoiceContext(), combat.DiscardPile, player, Prefs(TakesOne), filter: null)
                    .GetAwaiter().GetResult().ToList();
                Assert.Single(chosen);
            }
        }
        finally
        {
            CardPrompts.Answered = previous;
        }

        Assert.Equal(0, announced);
        Assert.Null(CardPrompts.Open);
    });

    /// <summary>
    /// Two prompts open at once is a state nothing can order, so both carry the
    /// other's name and a subscriber refuses each. Reached here by a selector that
    /// holds the first prompt's answer back while a second entry point is asked.
    /// </summary>
    [GameFact]
    public void APromptAskedWhileAnotherIsOpenMarksBothAsAConflict() => InTheFirstFight((player, ask) =>
    {
        var held = new TaskCompletionSource<IEnumerable<CardModel>>();
        var holding = new Holding(held.Task);
        var announced = new List<CardPrompts.Prompt>();
        var previous = CardPrompts.Answered;
        CardPrompts.Answered = (prompt, _) => announced.Add(prompt);
        try
        {
            using (CardSelectCmd.PushSelector(holding))
            {
                var first = CardSelectCmd.FromDeckForUpgrade(player, Prefs(TakesOne));
                Assert.False(first.IsCompleted);
                var opened = CardPrompts.Open;
                Assert.NotNull(opened);
                Assert.Null(opened!.Conflict);

                var second = CardSelectCmd.FromDeckForTransformation(player, Prefs(TakesOne));
                Assert.Equal(nameof(CardSelectCmd.FromDeckForTransformation), opened.Conflict);
                Assert.Equal(nameof(CardSelectCmd.FromDeckForUpgrade), CardPrompts.Open!.Conflict);

                held.SetResult([holding.Options![0]]);
                first.GetAwaiter().GetResult();
                second.GetAwaiter().GetResult();
                Pump.Drain();
            }
        }
        finally
        {
            CardPrompts.Answered = previous;
        }

        Assert.Equal(2, announced.Count);
        Assert.All(announced, prompt => Assert.NotNull(prompt.Conflict));
        Assert.Null(CardPrompts.Open);
    });

    /// <summary>
    /// A prompt asked and never offered is never counted, and holds nothing open past
    /// the fight. A hook's context asks the engine to pause an action that is never
    /// run - here, never even handed the task it would run - so the entry point's task
    /// never settles and the prompt stays asked. The count reads nothing for it, the
    /// fight's end drops it, and the next prompt is asked with no conflict. Asked with
    /// nothing on the engine's selector stack, because a selector there is the branch
    /// that reads at the call.
    /// </summary>
    [GameFact]
    public void APromptAskedAndNeverOfferedIsNotCountedAndDoesNotOutliveTheFight() => HeadlessRuns.WithARun(session =>
    {
        var player = session.RunState.Players[0];
        using (var driver = new RunDriver(session))
        {
            HeadlessRuns.EnterTheFirstFight(driver, session);
        }

        var combat = player.PlayerCombatState!;
        Discard(combat.Hand.Cards[0]);
        Discard(combat.Hand.Cards[0]);
        Assert.Null(CardSelectCmd.Selector);
        Assert.Equal(0, CardScreensUp.Count);

        var harmony = new Harmony($"sts2-pilot-trainer.card-prompt-test.{Guid.NewGuid():N}");
        var previous = CardPrompts.Answered;
        var announced = new List<CardPrompts.Prompt>();
        try
        {
            foreach (var patchClass in CardPrompts.PatchClasses) harmony.CreateClassProcessor(patchClass).Patch();
            CardPrompts.Forget();
            CardPrompts.Answered = (prompt, _) => announced.Add(prompt);

            var context = new HookPlayerChoiceContext(player, LocalContext.NetId!.Value, GameActionType.Combat);
            var asking = CardSelectCmd.FromCombatPile(context, combat.DiscardPile, player, Prefs(TakesOne), filter: null);
            Pump.Drain();

            Assert.False(asking.IsCompleted);
            var stranded = CardPrompts.Open;
            Assert.NotNull(stranded);
            Assert.Equal(CardPrompts.PromptState.Asked, stranded!.State);
            Assert.Equal(0, CardScreensUp.Count);

            var enemies = CombatManager.Instance.DebugOnlyGetState()!.Enemies.Where(enemy => enemy.IsAlive).ToList();
            CreatureCmd.Kill(enemies, force: true).GetAwaiter().GetResult();
            CombatManager.Instance.CheckWinCondition().GetAwaiter().GetResult();
            Pump.Drain();

            Assert.False(CombatManager.Instance.IsInProgress);
            Assert.Null(CardPrompts.Open);
            Assert.False(asking.IsCompleted);
            Assert.Equal(0, CardScreensUp.Count);

            var selector = new Recording();
            using (CardSelectCmd.PushSelector(selector))
            {
                _ = CardSelectCmd.FromDeckForUpgrade(player, Prefs(TakesOne)).GetAwaiter().GetResult();
                Pump.Drain();
            }

            var next = Assert.Single(announced);
            Assert.Equal(nameof(CardSelectCmd.FromDeckForUpgrade), next.EntryPoint);
            Assert.Null(next.Conflict);
            Assert.Equal(selector.Options, next.Offered);
            Assert.Equal(0, CardScreensUp.Count);
        }
        finally
        {
            CardPrompts.Answered = previous;
            CardPrompts.Forget();
            harmony.UnpatchAll(harmony.Id);
        }
    });

    /// <summary>
    /// A prompt counted in a run is given back when that run is torn down, whether or
    /// not its task ever settles, and the next run counts from zero. The hand prompt
    /// is the shape that needs it - its own teardown completes the prompt instead of
    /// cancelling it and the entry point then waits for ever on a reset queue - and it
    /// is stood in for here by a selector that never answers, which never settles
    /// either. The task is left outstanding across the teardown and the next run.
    /// </summary>
    [GameFact]
    public void APromptCountedInARunIsGivenBackWhenTheRunIsTornDown()
    {
        var harmony = new Harmony($"sts2-pilot-trainer.card-prompt-test.{Guid.NewGuid():N}");
        var previous = CardPrompts.Answered;
        try
        {
            foreach (var patchClass in CardPrompts.PatchClasses.Concat(CardScreensUp.PatchClasses))
            {
                harmony.CreateClassProcessor(patchClass).Patch();
            }

            CardPrompts.Forget();
            CardPrompts.Answered = null;

            var held = new Holding(new TaskCompletionSource<IEnumerable<CardModel>>().Task);
            Task<IEnumerable<CardModel>>? stranded = null;
            HeadlessRuns.WithARun(session =>
            {
                using var driver = new RunDriver(session);
                HeadlessRuns.EnterTheFirstFight(driver, session);
                var player = session.RunState.Players[0];
                Assert.Equal(0, CardScreensUp.Count);

                using (CardSelectCmd.PushSelector(held))
                {
                    stranded = CardSelectCmd.FromHand(
                        new BlockingPlayerChoiceContext(), player, Prefs(TakesOne), filter: null,
                        source: player.PlayerCombatState!.Hand.Cards[0]);
                }

                Assert.False(stranded.IsCompleted);
                Assert.Equal(CardPrompts.PromptState.Offered, CardPrompts.Open!.State);
                Assert.Equal(1, CardScreensUp.Count);

                HeadlessRuns.EndAnyRun();

                Assert.Equal(0, CardScreensUp.Count);
                Assert.Null(CardPrompts.Open);
                Assert.False(stranded.IsCompleted);
            });

            HeadlessRuns.WithARun(session =>
            {
                using var driver = new RunDriver(session);
                HeadlessRuns.EnterTheFirstFight(driver, session);
                var player = session.RunState.Players[0];
                Assert.Equal(0, CardScreensUp.Count);

                var answer = new TaskCompletionSource<IEnumerable<CardModel>>();
                var holding = new Holding(answer.Task);
                using (CardSelectCmd.PushSelector(holding))
                {
                    var asking = CardSelectCmd.FromDeckForUpgrade(player, Prefs(TakesOne));
                    Assert.Equal(1, CardScreensUp.Count);

                    answer.SetResult([holding.Options![0]]);
                    asking.GetAwaiter().GetResult();
                    Pump.Drain();
                }

                Assert.Equal(0, CardScreensUp.Count);
                Assert.False(stranded!.IsCompleted);
            });
        }
        finally
        {
            CardPrompts.Answered = previous;
            CardPrompts.Forget();
            harmony.UnpatchAll(harmony.Id);
        }
    }

    /// <summary>
    /// The recorded-fight journey lights the recording's card off the open prompt's
    /// list, and a prompt it cannot light is refused by name before any wait on a
    /// screen: here a hand prompt, held open by a selector that does not answer.
    /// </summary>
    [GameFact]
    public void TheJourneyRefusesAPromptItCannotLightByName() => InTheFirstFight((player, ask) =>
    {
        var combat = player.PlayerCombatState!;
        var held = new TaskCompletionSource<IEnumerable<CardModel>>();
        var holding = new Holding(held.Task);

        using (CardSelectCmd.PushSelector(holding))
        {
            var asking = CardSelectCmd.FromHand(
                new BlockingPlayerChoiceContext(), player, Prefs(TakesOne), filter: null, source: combat.Hand.Cards[0]);
            Assert.False(asking.IsCompleted);

            var refusal = Assert.Throws<RevealRefusedException>(
                () => RecordedCardScreen.Find(combat.Hand.Cards[0].Id.ToString(), 0));
            Assert.Contains("CardSelectCmd.FromHand", refusal.Message, StringComparison.Ordinal);
            Assert.Contains("draws no screen", refusal.Message, StringComparison.Ordinal);

            held.SetResult([holding.Options![0]]);
            asking.GetAwaiter().GetResult();
            Pump.Drain();
        }

        // And with no prompt open there is nothing to refuse yet, only something to
        // wait for.
        Assert.Throws<RevealNotReadyException>(() => RecordedCardScreen.Find("CARD.STRIKE_IRONCLAD", 0));
    });

    // ── The harness ──────────────────────────────────────────────────────────────

    /// <summary>One prompt asked through one entry point: what the engine handed its
    /// seam, and what <see cref="CardPrompts"/> announced.</summary>
    private sealed record Asked(
        Recording Selector, CardPrompts.Prompt Prompt, IReadOnlyList<CardModel> Answer, int Announced)
    {
        internal IReadOnlyList<CardModel> Offered => Prompt.Offered ?? throw new Xunit.Sdk.XunitException(
            $"{Prompt.EntryPoint} was derived to no decision ({Prompt.State}).");

        /// <summary>The whole claim: same cards, same order, same bounds, and the
        /// answer announced is the answer the selector gave.</summary>
        internal void OffersWhatTheEngineHandedItsSeam()
        {
            Assert.NotNull(Selector.Options);
            Assert.Equal(CardPrompts.PromptState.Offered, Prompt.State);
            Assert.Equal(Selector.Options!.Count, Offered.Count);
            for (var index = 0; index < Offered.Count; index++)
            {
                Assert.True(
                    ReferenceEquals(Selector.Options[index], Offered[index]),
                    $"{Prompt.EntryPoint} offers {Offered[index].Id} at {index} and the engine handed " +
                    $"{Selector.Options[index].Id} there.");
            }

            Assert.Equal(Selector.MinSelect, Prompt.MinSelect);
            Assert.Equal(Selector.MaxSelect, Prompt.MaxSelect);
            Assert.Equal(Selector.Chose.Count, Answer.Count);
            Assert.All(Selector.Chose.Zip(Answer), pair => Assert.Same(pair.First, pair.Second));
            Assert.Null(Prompt.Conflict);
            Assert.Null(CardPrompts.Open);
        }
    }

    private delegate Asked Ask(string entryPoint, Func<Task<IEnumerable<CardModel>>> call);

    /// <summary>
    /// A run of the probe seed in its first fight, with the shell's prompt patches
    /// installed for the block and a way to ask the engine one prompt.
    ///
    /// The driver's own selector is on the stack for the walk in; each ask pushes a
    /// recording selector above it for the length of the call, so the engine takes its
    /// selector branch and hands the list to something that keeps it.
    /// </summary>
    private static void InTheFirstFight(Action<Player, Ask> body) => HeadlessRuns.WithARun(session =>
    {
        using var driver = new RunDriver(session);
        HeadlessRuns.EnterTheFirstFight(driver, session);
        var player = session.RunState.Players[0];

        var harmony = new Harmony($"sts2-pilot-trainer.card-prompt-test.{Guid.NewGuid():N}");
        var previous = CardPrompts.Answered;
        try
        {
            foreach (var patchClass in CardPrompts.PatchClasses) harmony.CreateClassProcessor(patchClass).Patch();
            CardPrompts.Forget();

            body(player, (entryPoint, call) =>
            {
                var selector = new Recording();
                CardPrompts.Prompt? prompt = null;
                IReadOnlyList<CardModel>? answer = null;
                var announced = 0;
                CardPrompts.Answered = (asked, chosen) =>
                {
                    announced++;
                    prompt = asked;
                    answer = chosen;
                };

                using (CardSelectCmd.PushSelector(selector))
                {
                    _ = call().GetAwaiter().GetResult();
                    Pump.Drain();
                }

                Assert.NotNull(prompt);
                Assert.Equal(entryPoint, prompt!.EntryPoint);
                return new Asked(selector, prompt, answer!, announced);
            });
        }
        finally
        {
            CardPrompts.Answered = previous;
            CardPrompts.Forget();
            harmony.UnpatchAll(harmony.Id);
        }
    });

    /// <summary>A selector that keeps what it was handed and picks from the front.</summary>
    private sealed class Recording : ICardSelector
    {
        internal List<CardModel>? Options { get; private set; }

        internal int MinSelect { get; private set; }

        internal int MaxSelect { get; private set; }

        internal List<CardModel> Chose { get; private set; } = [];

        public Task<IEnumerable<CardModel>> GetSelectedCards(
            IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            Options = options.ToList();
            MinSelect = minSelect;
            MaxSelect = maxSelect;
            Chose = Options.Take(Math.Max(maxSelect, 1)).ToList();
            return Task.FromResult<IEnumerable<CardModel>>(Chose);
        }

        public CardRewardSelection GetSelectedCardReward(
            IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives) =>
            throw new NotSupportedException("No card reward is asked here.");
    }

    /// <summary>A selector that answers every prompt with one task it does not
    /// complete, so a prompt stays open for as long as the test wants.</summary>
    private sealed class Holding(Task<IEnumerable<CardModel>> answer) : ICardSelector
    {
        internal List<CardModel>? Options { get; private set; }

        public Task<IEnumerable<CardModel>> GetSelectedCards(
            IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            Options ??= options.ToList();
            return answer;
        }

        public CardRewardSelection GetSelectedCardReward(
            IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives) =>
            throw new NotSupportedException("No card reward is asked here.");
    }

    private static CardSelectorPrefs Prefs(int count) => new(CardSelectorPrefs.ExhaustSelectionPrompt, count);

    /// <summary>The two single-card entry points, as the many-card shape the harness asks.</summary>
    private static async Task<IEnumerable<CardModel>> Single(Task<CardModel?> one) =>
        await one is { } card ? [card] : [];

    /// <summary>Moves a card to the discard through the engine's own command.</summary>
    private static void Discard(CardModel card)
    {
        CardPileCmd.Add(card, PileType.Discard).GetAwaiter().GetResult();
        Pump.Drain();
    }
}

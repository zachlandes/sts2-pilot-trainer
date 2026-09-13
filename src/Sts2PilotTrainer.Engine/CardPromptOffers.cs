using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The list a card prompt hands whoever answers it, derived from what the prompt was
/// asked, exactly as each <see cref="CardSelectCmd"/> entry point derives it on
/// v0.111.0.
///
/// A recording's <c>option_index</c> is a position in the list the engine hands its
/// own answering seam - <c>ICardSelector.GetSelectedCards</c>, which
/// <see cref="ManifestCardSelector"/> fills headlessly - and no screen the retail client
/// draws holds that list: the combat-pile screen keeps the live pile and an empty list of
/// its own, the hand prompt draws no screen at all, and the draw pile is re-ordered by
/// rarity and id before the seam sees it so an answerer cannot learn draw order. So the
/// recorder cannot read the list off anything; it has to produce it, from the same
/// arguments the entry point was given, by the same expression.
///
/// This is duplicated game logic, on purpose and said so. Each method here is the
/// <c>Selector</c> branch of the entry point it is named after, transcribed, and what
/// makes the duplication safe is <c>CardPromptOfferTests</c>: every method is driven
/// through the headless engine against a selector that records what the engine handed
/// it, element for element. A build that changes one expression fails there by name.
///
/// Null means the engine answers itself and nobody decides anything: the fight is
/// ending, there is nothing to offer, or the candidates fit inside <c>MinSelect</c>
/// without manual confirmation. Those are the entry point's own early returns, matched
/// by the same predicates, so a recording and a replay agree about which prompts were
/// decisions.
/// </summary>
public static class CardPromptOffers
{
    /// <summary><see cref="CardSelectCmd.FromChooseACardScreen"/>: the cards as given,
    /// zero or one of them. An empty prompt is a softlock the engine returns null from.</summary>
    public static IReadOnlyList<CardModel>? FromChooseACardScreen(IReadOnlyList<CardModel> cards) =>
        cards.Count == 0 ? null : cards;

    /// <summary><see cref="CardSelectCmd.FromSimpleGridForRewards"/>: the created cards,
    /// in the order given. The screen sorts them by <c>prefs.Comparison</c> for display
    /// and maps its answer back into this list.</summary>
    public static IReadOnlyList<CardModel>? FromSimpleGridForRewards(
        IReadOnlyList<CardCreationResult> cards, CardSelectorPrefs prefs, bool combatIsEnding)
    {
        if (combatIsEnding || cards.Count == 0) return null;
        if (AutoPicked(cards.Count, prefs)) return null;
        return cards.Select(created => created.Card).ToList();
    }

    /// <summary><see cref="CardSelectCmd.FromSimpleGrid"/>: the cards as given.</summary>
    public static IReadOnlyList<CardModel>? FromSimpleGrid(
        IReadOnlyList<CardModel> cardsIn, CardSelectorPrefs prefs, bool combatIsEnding)
    {
        if (combatIsEnding) return null;
        var cards = cardsIn.ToList();
        if (cards.Count == 0 || AutoPicked(cards.Count, prefs)) return null;
        return cards;
    }

    /// <summary>
    /// <see cref="CardSelectCmd.FromCombatPile(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext, CardPile, Player, CardSelectorPrefs, Func{CardModel, bool})"/>:
    /// the pile filtered, and for the draw pile ordered by rarity then id.
    ///
    /// The ordering is the seam's and not the screen's: the retail screen draws the
    /// pile in its own display order and answers with the card itself, so a position is
    /// only ever taken against this list. The sort is stable, so two cards that share a
    /// rarity and an id keep the pile's own order between them - which a replay
    /// reproduces, because the pile's order is the hidden state it replays.
    /// </summary>
    public static IReadOnlyList<CardModel>? FromCombatPile(
        CardPile pile, Func<CardModel, bool>? filter, CardSelectorPrefs prefs, bool combatIsEnding)
    {
        if (combatIsEnding || !pile.IsCombatPile) return null;

        IReadOnlyList<CardModel> candidates = filter is null ? pile.Cards : pile.Cards.Where(filter).ToList();
        if (candidates.Count == 0 || AutoPicked(candidates.Count, prefs)) return null;

        return pile.Type == PileType.Draw
            ? candidates.OrderBy(card => card.Rarity).ThenBy(card => card.Id).ToList()
            : candidates.ToList();
    }

    /// <summary><see cref="CardSelectCmd.FromDeckForUpgrade"/>: the deck's upgradable
    /// cards in deck order.</summary>
    public static IReadOnlyList<CardModel>? FromDeckForUpgrade(Player player, CardSelectorPrefs prefs)
    {
        var cards = PileType.Deck.GetPile(player).Cards.Where(card => card.IsUpgradable).ToList();
        if (cards.Count == 0 || AutoPicked(cards.Count, prefs)) return null;
        return cards;
    }

    /// <summary><see cref="CardSelectCmd.FromDeckForTransformation"/>: the deck's
    /// transformable non-quest cards in deck order.</summary>
    public static IReadOnlyList<CardModel>? FromDeckForTransformation(Player player, CardSelectorPrefs prefs)
    {
        var cards = PileType.Deck.GetPile(player).Cards
            .Where(card => card.Type != CardType.Quest && card.IsTransformable)
            .ToList();
        if (cards.Count == 0 || AutoPicked(cards.Count, prefs)) return null;
        return cards;
    }

    /// <summary>
    /// <see cref="CardSelectCmd.FromDeckForEnchantment(IReadOnlyList{CardModel}, EnchantmentModel, int, CardSelectorPrefs)"/>:
    /// the given cards in their owner's deck order.
    ///
    /// The one deck prompt whose early return ignores manual confirmation: candidates
    /// that fit inside <c>MinSelect</c> are taken as given, however the prefs read.
    /// A dead owner is refused by the engine before any screen; the prompt is then
    /// nobody's decision.
    /// </summary>
    public static IReadOnlyList<CardModel>? FromDeckForEnchantment(
        IReadOnlyList<CardModel> cards, EnchantmentModel enchantment, CardSelectorPrefs prefs)
    {
        if (cards.Any(card => card.Pile?.Type != PileType.Deck || !enchantment.CanEnchant(card))) return null;
        if (cards.Count <= prefs.MinSelect) return null;

        var owner = cards[0].Owner;
        if (owner.Creature.IsDead) return null;

        var deckIndex = PileType.Deck.GetPile(owner).Cards
            .Select((card, index) => (card, index))
            .ToDictionary(entry => entry.card, entry => entry.index);
        return cards.OrderBy(card => deckIndex[card]).ToList();
    }

    /// <summary><see cref="CardSelectCmd.FromDeckGeneric"/>: the deck filtered, then
    /// stably ordered by the caller's key where one is given. A removal's key puts
    /// curses first and everything else in deck order.</summary>
    public static IReadOnlyList<CardModel>? FromDeckGeneric(
        Player player, CardSelectorPrefs prefs, Func<CardModel, bool>? filter, Func<CardModel, int>? sortingOrder)
    {
        var deck = PileType.Deck.GetPile(player).Cards.ToList();
        var cards = filter is null ? deck : deck.Where(filter).ToList();
        if (player.Creature.IsDead) return null;
        if (sortingOrder is not null) cards = cards.OrderBy(sortingOrder).ToList();
        if (cards.Count == 0 || AutoPicked(cards.Count, prefs)) return null;
        return cards;
    }

    /// <summary><see cref="CardSelectCmd.FromHand"/>: the hand filtered, in hand order.
    /// <see cref="CardSelectCmd.FromHandForDiscard"/> reaches this with its own glow
    /// rule set on the prefs and nothing else changed.</summary>
    public static IReadOnlyList<CardModel>? FromHand(
        Player player, CardSelectorPrefs prefs, Func<CardModel, bool>? filter, bool combatIsOverOrEnding)
    {
        if (combatIsOverOrEnding) return null;
        var cards = PileType.Hand.GetPile(player).Cards.Where(filter ?? (_ => true)).ToList();
        if (cards.Count == 0 || AutoPicked(cards.Count, prefs)) return null;
        return cards;
    }

    /// <summary><see cref="CardSelectCmd.FromHandForUpgrade"/>: the hand's upgradable
    /// cards, asked for exactly one where there are two or more.</summary>
    public static IReadOnlyList<CardModel>? FromHandForUpgrade(Player player, bool combatIsOverOrEnding)
    {
        if (combatIsOverOrEnding) return null;
        var cards = PileType.Hand.GetPile(player).Cards.Where(card => card.IsUpgradable).ToList();
        return cards.Count <= 1 ? null : cards;
    }

    /// <summary>How many the hand-upgrade prompt asks for: one, whatever the caller's
    /// prefs, because the entry point builds its own.</summary>
    public const int HandForUpgradeSelects = 1;

    /// <summary>What the choose-a-card prompt asks for: at most one, and none is a
    /// skip.</summary>
    public const int ChooseACardMinSelects = 0;

    /// <summary>See <see cref="ChooseACardMinSelects"/>.</summary>
    public const int ChooseACardMaxSelects = 1;

    /// <summary>The engine's own rule for taking the candidates without asking: they
    /// fit inside the minimum and nobody asked for a confirmation.</summary>
    private static bool AutoPicked(int count, CardSelectorPrefs prefs) =>
        !prefs.RequireManualConfirmation && count <= prefs.MinSelect;
}

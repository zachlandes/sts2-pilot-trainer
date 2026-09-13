using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// Every card prompt the engine puts to a player, observed at the command that asks
/// it and never at the screen that draws it.
///
/// One funnel: on v0.111.0 every prompt over a hand, a pile, a deck or a handful of
/// created cards is opened by a public entry point of <see cref="CardSelectCmd"/>, and
/// nothing else creates or shows a card-selection screen. Each entry point has the
/// same shape - the engine's own early returns, then a selector standing in for the
/// player, then the retail screen - so one mechanism observes all of them: a prefix
/// reads what the prompt was asked, the list it offers is derived by
/// <see cref="CardPromptOffers"/> from those arguments by the entry point's own
/// expression, and a postfix looks at the task the entry point hands back and announces
/// what came back once it has. The same patch fires headlessly, where the engine takes
/// its selector branch, which is what lets <c>CardPromptOfferTests</c> hold every
/// derived list to the list the engine itself handed over.
///
/// Watching the screens was how three of the five prompt shapes went unrecorded or
/// misrecorded: the combat-pile screen keeps an empty list and the live pile, the hand
/// prompt draws no screen at all, and the choose-a-card screen shares no base with the
/// grid. The recording's <c>option_index</c> is a position in the list the engine hands
/// its seam, which no screen holds.
///
/// <em>When</em> the list is derived is the one subtlety, and it is the engine's
/// timing rather than a choice made here. An entry point with a
/// <see cref="PlayerChoiceContext"/> reads its pile or hand only after
/// <c>SignalPlayerChoiceBegun</c> has paused the action asking - and a hook's context
/// pauses on a later frame, after actions the hook was queued behind have run, so a
/// list read at the call would be a list the engine never offered. So a prompt that
/// will pause is derived by <see cref="Paused"/>, at the pause, and everything else -
/// a deck prompt, which has no context; a selector standing in, which skips the pause;
/// a blocking context, which returns without one - is derived in the prefix, which is
/// when those read. A prompt that settles without ever having been derived is refused
/// by its subscriber, not guessed at.
///
/// A prompt the engine answers for itself is not a decision and is not opened at all:
/// a selector of the game's own - Whispering Earring pushes one for the length of its
/// effect - answers in both hosts, so no recording may state a decision there and no
/// replay will ask for one. A prompt whose engine-side early return fires - the fight is
/// ending, nothing to offer, the candidates fit inside the minimum - is opened and
/// derived to nothing, so its answer is told to nobody.
///
/// A prompt is counted in <see cref="CardScreensUp"/> only from the moment it is
/// offered, never from the moment it was asked. A hook's prompt whose action the fight
/// ended before it ran is asked and never offered, and its task never settles: counted
/// from the call it would hold the count up for the rest of the process, and every
/// settle after it would wait for ever. The same prompt is dropped as <see cref="Open"/>
/// when the fight ends, so it is not the conflict the next fight's first prompt meets.
///
/// This is the shell's rather than the recorder's, for the reason
/// <see cref="CardScreensUp"/> is: a prompt being up is a fact about the game that both
/// settles read, and the list it offers is the list the recorded-fight journey finds
/// the recording's card on. What a prompt's answer means is a subscriber's business;
/// this says only that one happened and hands over the prompt it answered.
/// </summary>
internal static class CardPrompts
{
    /// <summary>Where a prompt is in its life: asked and not yet read by the engine,
    /// read and offered to somebody, or read and answered by the engine itself.</summary>
    internal enum PromptState
    {
        Asked,
        Offered,
        EngineAnswered,
    }

    /// <summary>
    /// One prompt the engine is asking, from the moment its entry point was called to
    /// the moment the task it handed back settled.
    ///
    /// Every field is a framework or game type: this type is laid out when the game
    /// enumerates the mod's types, before a sibling assembly can be resolved, and a
    /// sibling-typed field there is a mod that fails to load. See docs/in-game-host.md.
    /// </summary>
    internal sealed class Prompt
    {
        private readonly Func<IReadOnlyList<CardModel>?> _derive;
        private readonly TaskCompletionSource _offered = new();

        internal Prompt(
            string entryPoint, string screen, int minSelect, int maxSelect, Func<IReadOnlyList<CardModel>?> derive)
        {
            EntryPoint = entryPoint;
            Screen = screen;
            MinSelect = minSelect;
            MaxSelect = maxSelect;
            _derive = derive;
        }

        /// <summary>The <see cref="CardSelectCmd"/> member that asked, by its own name.</summary>
        internal string EntryPoint { get; }

        /// <summary>What the retail client draws for it: the screen's type, or the
        /// hand, by the game's own name for the node.</summary>
        internal string Screen { get; }

        internal int MinSelect { get; }

        internal int MaxSelect { get; }

        internal PromptState State { get; private set; } = PromptState.Asked;

        /// <summary>The list the engine offers, in the order its seam receives it, once
        /// <see cref="State"/> is <see cref="PromptState.Offered"/>; null before and
        /// where the engine answered itself.</summary>
        internal IReadOnlyList<CardModel>? Offered { get; private set; }

        /// <summary>Completes the moment <see cref="State"/> becomes
        /// <see cref="PromptState.Offered"/>, and never otherwise.</summary>
        internal Task WhenOffered => _offered.Task;

        /// <summary>The entry point of another prompt that was still open when this one
        /// was asked, or that this one was still open for. Two prompts open at once is
        /// a state nothing here can order, so a subscriber refuses both.</summary>
        internal string? Conflict { get; set; }

        /// <summary>Reads what the engine is about to read, now.</summary>
        internal void Derive()
        {
            if (State != PromptState.Asked) return;
            Offered = _derive();
            State = Offered is null ? PromptState.EngineAnswered : PromptState.Offered;
            if (State == PromptState.Offered) _offered.SetResult();
        }
    }

    /// <summary>The prompt the engine is asking right now, or null.</summary>
    internal static Prompt? Open { get; private set; }

    /// <summary>A prompt's task has settled with an answer: the prompt, and the cards
    /// that came back, in the order the engine returned them.</summary>
    internal static Action<Prompt, IReadOnlyList<CardModel>>? Answered { get; set; }

    /// <summary>Every patch class this owns, for the shell to install and for
    /// <c>RunmobileModuleTests</c> to hold to one owner. One per entry point that
    /// reaches a screen - the entry points that forward to one of these in one call
    /// are not patched, because patching a forwarder as well would announce one prompt
    /// twice - plus the pause, the fight's end and the teardown.</summary>
    internal static IReadOnlyList<Type> PatchClasses { get; } =
    [
        typeof(ChooseACard), typeof(SimpleGridForRewards), typeof(SimpleGrid), typeof(CombatPile),
        typeof(DeckForUpgrade), typeof(DeckForTransformation), typeof(DeckForEnchantment), typeof(DeckGeneric),
        typeof(Hand), typeof(HandForUpgrade),
        typeof(Paused), typeof(FightEnded), typeof(RunTornDown),
    ];

    /// <summary>
    /// Opens a prompt for what an entry point was just asked.
    ///
    /// Derived now where the engine reads now - no context to pause with, a selector
    /// standing in for the player, or a context that returns without pausing - and left
    /// for <see cref="Paused"/> otherwise. Not opened at all where the game's own
    /// selector answers, because nobody decides anything there.
    /// </summary>
    private static Prompt? Ask(
        string entryPoint, string screen, int minSelect, int maxSelect, PlayerChoiceContext? context,
        Func<IReadOnlyList<CardModel>?> derive)
    {
        var selector = CardSelectCmd.Selector;
        if (selector is not null && IsTheGamesOwn(selector)) return null;

        var prompt = new Prompt(entryPoint, screen, minSelect, maxSelect, derive);
        if (context is null || selector is not null || context is BlockingPlayerChoiceContext) prompt.Derive();

        // Open is cleared the moment a prompt's task settles, and at the end of the
        // fight and the run, so one still here is one the engine has not answered
        if (Open is { } other)
        {
            other.Conflict = prompt.EntryPoint;
            prompt.Conflict = other.EntryPoint;
        }

        Open = prompt;
        return prompt;
    }

    /// <summary>Whether a selector on the engine's stack is the game's own - which
    /// answers a prompt in both hosts - rather than one standing in for a player.</summary>
    internal static bool IsTheGamesOwn(object selector) =>
        selector.GetType().Assembly == typeof(CardSelectCmd).Assembly;

    /// <summary>Whether the fight is ending, read the way the entry points read it.</summary>
    private static bool CombatIsEnding => CombatManager.Instance is { IsEnding: true };

    private static bool CombatIsOverOrEnding => CombatManager.Instance is { IsOverOrEnding: true };

    /// <summary>
    /// Watches the task an entry point handed back, counting the prompt as up from the
    /// moment it is offered for as long as the task is outstanding, and announces what
    /// came back. The task the caller gets completes after the announcement, so a
    /// subscriber reads the answer before the caller moves the cards it names.
    /// </summary>
    private static async Task<IEnumerable<CardModel>> Observe(Prompt prompt, Task<IEnumerable<CardModel>> inner)
    {
        IEnumerable<CardModel> chosen;
        try
        {
            chosen = await CardScreensUp.WhileOneIsUp(inner, prompt.WhenOffered);
        }
        finally
        {
            Close(prompt);
        }

        Announce(prompt, chosen.ToList());
        return chosen;
    }

    /// <summary>The same for the two entry points that answer with one card or none.</summary>
    private static async Task<CardModel?> ObserveOne(Prompt prompt, Task<CardModel?> inner)
    {
        CardModel? chosen;
        try
        {
            chosen = await CardScreensUp.WhileOneIsUp(inner, prompt.WhenOffered);
        }
        finally
        {
            Close(prompt);
        }

        Announce(prompt, chosen is null ? [] : [chosen]);
        return chosen;
    }

    private static void Close(Prompt prompt)
    {
        if (ReferenceEquals(Open, prompt)) Open = null;
    }

    private static void Announce(Prompt prompt, IReadOnlyList<CardModel> chosen) =>
        CardScreensUp.Announce(
            $"a card prompt's answer ({prompt.EntryPoint})", () => Answered?.Invoke(prompt, chosen));

    /// <summary>Forgets an open prompt, for a test that opened one by hand.</summary>
    internal static void Forget() => Open = null;

    // ── The entry points ─────────────────────────────────────────────────────────

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseACardScreen))]
    internal static class ChooseACard
    {
        [HarmonyPrefix]
        internal static void Before(PlayerChoiceContext context, IReadOnlyList<CardModel> cards, out Prompt? __state) =>
            __state = Ask(nameof(CardSelectCmd.FromChooseACardScreen), nameof(NChooseACardSelectionScreen),
                CardPromptOffers.ChooseACardMinSelects, CardPromptOffers.ChooseACardMaxSelects, context,
                () => CardPromptOffers.FromChooseACardScreen(cards));

        [HarmonyPostfix]
        internal static void After(Prompt? __state, ref Task<CardModel?> __result)
        {
            if (__state is not null) __result = ObserveOne(__state, __result);
        }
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGridForRewards))]
    internal static class SimpleGridForRewards
    {
        [HarmonyPrefix]
        internal static void Before(
            PlayerChoiceContext context, List<CardCreationResult> cards, CardSelectorPrefs prefs, out Prompt? __state)
        {
            var ending = CombatIsEnding;
            __state = Ask(nameof(CardSelectCmd.FromSimpleGridForRewards), nameof(NSimpleCardSelectScreen),
                prefs.MinSelect, prefs.MaxSelect, context,
                () => CardPromptOffers.FromSimpleGridForRewards(cards, prefs, ending));
        }

        [HarmonyPostfix]
        internal static void After(Prompt? __state, ref Task<IEnumerable<CardModel>> __result)
        {
            if (__state is not null) __result = Observe(__state, __result);
        }
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGrid))]
    internal static class SimpleGrid
    {
        [HarmonyPrefix]
        internal static void Before(
            PlayerChoiceContext context, IReadOnlyList<CardModel> cardsIn, CardSelectorPrefs prefs, out Prompt? __state)
        {
            var ending = CombatIsEnding;
            __state = Ask(nameof(CardSelectCmd.FromSimpleGrid), nameof(NSimpleCardSelectScreen),
                prefs.MinSelect, prefs.MaxSelect, context,
                () => CardPromptOffers.FromSimpleGrid(cardsIn, prefs, ending));
        }

        [HarmonyPostfix]
        internal static void After(Prompt? __state, ref Task<IEnumerable<CardModel>> __result)
        {
            if (__state is not null) __result = Observe(__state, __result);
        }
    }

    /// <summary>The five-argument overload, which the four-argument one forwards to
    /// with a filter that admits everything.</summary>
    [HarmonyPatch(
        typeof(CardSelectCmd), nameof(CardSelectCmd.FromCombatPile),
        [typeof(PlayerChoiceContext), typeof(CardPile), typeof(Player), typeof(CardSelectorPrefs), typeof(Func<CardModel, bool>)])]
    internal static class CombatPile
    {
        [HarmonyPrefix]
        internal static void Before(
            PlayerChoiceContext context, CardPile pile, CardSelectorPrefs prefs, Func<CardModel, bool>? filter, out Prompt? __state)
        {
            var ending = CombatIsEnding;
            __state = Ask(nameof(CardSelectCmd.FromCombatPile), nameof(NCombatPileCardSelectScreen),
                prefs.MinSelect, prefs.MaxSelect, context,
                () => CardPromptOffers.FromCombatPile(pile, filter, prefs, ending));
        }

        [HarmonyPostfix]
        internal static void After(Prompt? __state, ref Task<IEnumerable<CardModel>> __result)
        {
            if (__state is not null) __result = Observe(__state, __result);
        }
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckForUpgrade))]
    internal static class DeckForUpgrade
    {
        [HarmonyPrefix]
        internal static void Before(Player player, CardSelectorPrefs prefs, out Prompt? __state) =>
            __state = Ask(nameof(CardSelectCmd.FromDeckForUpgrade), nameof(NDeckUpgradeSelectScreen),
                prefs.MinSelect, prefs.MaxSelect, null,
                () => CardPromptOffers.FromDeckForUpgrade(player, prefs));

        [HarmonyPostfix]
        internal static void After(Prompt? __state, ref Task<IEnumerable<CardModel>> __result)
        {
            if (__state is not null) __result = Observe(__state, __result);
        }
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckForTransformation))]
    internal static class DeckForTransformation
    {
        [HarmonyPrefix]
        internal static void Before(Player player, CardSelectorPrefs prefs, out Prompt? __state) =>
            __state = Ask(nameof(CardSelectCmd.FromDeckForTransformation), nameof(NDeckTransformSelectScreen),
                prefs.MinSelect, prefs.MaxSelect, null,
                () => CardPromptOffers.FromDeckForTransformation(player, prefs));

        [HarmonyPostfix]
        internal static void After(Prompt? __state, ref Task<IEnumerable<CardModel>> __result)
        {
            if (__state is not null) __result = Observe(__state, __result);
        }
    }

    /// <summary>The overload that takes the cards, which both player overloads
    /// forward to once they have filtered the deck.</summary>
    [HarmonyPatch(
        typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckForEnchantment),
        [typeof(IReadOnlyList<CardModel>), typeof(EnchantmentModel), typeof(int), typeof(CardSelectorPrefs)])]
    internal static class DeckForEnchantment
    {
        [HarmonyPrefix]
        internal static void Before(
            IReadOnlyList<CardModel> cards, EnchantmentModel enchantment, CardSelectorPrefs prefs, out Prompt? __state) =>
            __state = Ask(nameof(CardSelectCmd.FromDeckForEnchantment), nameof(NDeckEnchantSelectScreen),
                prefs.MinSelect, prefs.MaxSelect, null,
                () => CardPromptOffers.FromDeckForEnchantment(cards, enchantment, prefs));

        [HarmonyPostfix]
        internal static void After(Prompt? __state, ref Task<IEnumerable<CardModel>> __result)
        {
            if (__state is not null) __result = Observe(__state, __result);
        }
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckGeneric))]
    internal static class DeckGeneric
    {
        [HarmonyPrefix]
        internal static void Before(
            Player player, CardSelectorPrefs prefs, Func<CardModel, bool>? filter, Func<CardModel, int>? sortingOrder, out Prompt? __state) =>
            __state = Ask(nameof(CardSelectCmd.FromDeckGeneric), nameof(NDeckCardSelectScreen),
                prefs.MinSelect, prefs.MaxSelect, null,
                () => CardPromptOffers.FromDeckGeneric(player, prefs, filter, sortingOrder));

        [HarmonyPostfix]
        internal static void After(Prompt? __state, ref Task<IEnumerable<CardModel>> __result)
        {
            if (__state is not null) __result = Observe(__state, __result);
        }
    }

    /// <summary>The hand prompt, which <c>FromHandForDiscard</c> forwards to. It draws
    /// no screen: the hand itself goes into its selection mode.</summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHand))]
    internal static class Hand
    {
        [HarmonyPrefix]
        internal static void Before(
            PlayerChoiceContext context, Player player, CardSelectorPrefs prefs, Func<CardModel, bool>? filter, out Prompt? __state)
        {
            var ending = CombatIsOverOrEnding;
            __state = Ask(nameof(CardSelectCmd.FromHand), nameof(NPlayerHand), prefs.MinSelect, prefs.MaxSelect, context,
                () => CardPromptOffers.FromHand(player, prefs, filter, ending));
        }

        [HarmonyPostfix]
        internal static void After(Prompt? __state, ref Task<IEnumerable<CardModel>> __result)
        {
            if (__state is not null) __result = Observe(__state, __result);
        }
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHandForUpgrade))]
    internal static class HandForUpgrade
    {
        [HarmonyPrefix]
        internal static void Before(PlayerChoiceContext context, Player player, out Prompt? __state)
        {
            var ending = CombatIsOverOrEnding;
            __state = Ask(nameof(CardSelectCmd.FromHandForUpgrade), nameof(NPlayerHand),
                CardPromptOffers.HandForUpgradeSelects, CardPromptOffers.HandForUpgradeSelects, context,
                () => CardPromptOffers.FromHandForUpgrade(player, ending));
        }

        [HarmonyPostfix]
        internal static void After(Prompt? __state, ref Task<CardModel?> __result)
        {
            if (__state is not null) __result = ObserveOne(__state, __result);
        }
    }

    // ── The engine's read moment, and the teardown ──────────────────────────────

    /// <summary>
    /// The moment an entry point with a context reads its pile or hand: every context
    /// that pauses at all pauses here, synchronously, as the last thing it does before
    /// the entry point continues. A prompt still waiting to be read is read now.
    /// </summary>
    [HarmonyPatch(typeof(ActionQueueSet), nameof(ActionQueueSet.PauseActionForPlayerChoice))]
    internal static class Paused
    {
        [HarmonyPostfix]
        internal static void After() => Open?.Derive();
    }

    /// <summary>
    /// A prompt still open when the fight ends is a prompt nobody will answer - a
    /// hook's, asked in the fight and waiting on an action the end of the fight
    /// cancelled - and it must not be the conflict the next prompt meets. The game ends
    /// a fight along two paths, a win and a processed loss, and this is the first
    /// thing either does.
    /// </summary>
    [HarmonyPatch]
    internal static class FightEnded
    {
        [HarmonyTargetMethods]
        internal static IEnumerable<MethodBase> Targets()
        {
            yield return typeof(CombatManager)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(method => method.Name == "EndCombatInternal" && method.GetParameters().Length == 1);
            yield return AccessTools.DeclaredMethod(typeof(CombatManager), "ProcessPendingLoss");
        }

        [HarmonyPrefix]
        internal static void Before() => Open = null;
    }

    /// <summary>A prompt still open when the run is torn down is a prompt nobody will
    /// answer, and it must not be the conflict the next run's first prompt meets.</summary>
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
    internal static class RunTornDown
    {
        [HarmonyPostfix]
        internal static void After() => Open = null;
    }
}

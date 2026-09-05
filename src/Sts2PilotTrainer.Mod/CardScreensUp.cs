using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// How many card screens are up in front of the player, and who answered them.
///
/// A fact about the game rather than about either feature, which is why it is the
/// shell's. Both settles read it - the recorder's, to keep a reading off a decision
/// somebody has not finished making, and the Combat Trainer's, so a prompt a played
/// card opens does not spend the engine's budget - and neither of them owns it. Left
/// behind the recorder's patches it would stop counting on a build the recorder
/// declines to watch, which is exactly the build where the trainer is meant to carry
/// on, and the trainer would quietly go back to charging a player's thinking against
/// the engine.
///
/// The count is taken and given back by <see cref="WhileOneIsUp{T}"/> alone, in one
/// try/finally around the screen's own task, so every increment has its decrement and
/// there is no bare decrement for a caller to reach - it cannot drift and it cannot go
/// below zero. Both of the game's card screens complete their own completion source in
/// <c>_ExitTree</c>, the grid by cancelling and the reward screen by faulting, so a
/// screen torn down with its run still ends the task this waits on.
///
/// What a screen offered and what came back is announced rather than interpreted:
/// which card came off which list is the recorder's business, and this says only that
/// an answer happened. So is what a failure to read one means: a subscriber handles its
/// own, and <see cref="Announce"/> is only there so that one cannot take another down.
/// </summary>
internal static class CardScreensUp
{
    private static int _open;

    /// <summary>How many card screens are open right now.</summary>
    internal static int Count => Volatile.Read(ref _open);

    /// <summary>A screen over a pile, a deck or a hand has been answered, with the
    /// screen itself and the cards that came back.</summary>
    internal static Action<NCardGridSelectionScreen, IEnumerable<CardModel>>? GridAnswered { get; set; }

    /// <summary>A card reward's screen has been answered, by the position it reports.</summary>
    internal static Action<int?>? RewardAnswered { get; set; }

    /// <summary>Every patch class this owns, for the shell to install and for
    /// <c>RunmobileModuleTests</c> to hold to one owner.</summary>
    internal static IReadOnlyList<Type> PatchClasses { get; } = [typeof(Grid), typeof(Reward)];

    /// <summary>Counts one card screen for as long as the game's own task for it is
    /// outstanding.</summary>
    internal static async Task<T> WhileOneIsUp<T>(Task<T> screen)
    {
        Interlocked.Increment(ref _open);
        try
        {
            return await screen;
        }
        finally
        {
            Interlocked.Decrement(ref _open);
        }
    }

    /// <summary>
    /// Tells the subscribers a screen was answered, and keeps one of them from taking
    /// anything else down with it.
    ///
    /// Not error handling on a subscriber's behalf: what a failure means is the
    /// subscriber's to say, and each of them says it - the recorder by marking the
    /// recording broken and writing the reason into its own journal. This catch is for
    /// the two things a subscriber must not be able to do, which are to break the game's
    /// own card-screen path and to stop another subscriber running. Nothing should reach
    /// it, and something that does is a subscriber that did not handle its own failure.
    /// </summary>
    private static void Announce(string what, Action announce)
    {
        try
        {
            announce();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] {what} threw out of its own handler, which should have dealt with " +
                $"it: {ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>Every card screen the game opens over a pile, a deck or a hand. One
    /// patch for all of them, because they share the base that holds both halves of the
    /// answer. The returned task is handed on unchanged, having been looked at.</summary>
    [HarmonyPatch(typeof(NCardGridSelectionScreen), nameof(NCardGridSelectionScreen.CardsSelected))]
    internal static class Grid
    {
        [HarmonyPostfix]
        internal static void After(
            NCardGridSelectionScreen __instance, ref Task<IEnumerable<CardModel>> __result) =>
            __result = Observe(__instance, __result);

        internal static async Task<IEnumerable<CardModel>> Observe(
            NCardGridSelectionScreen screen, Task<IEnumerable<CardModel>> inner)
        {
            var chosen = await WhileOneIsUp(inner);
            Announce("a card screen's answer", () => GridAnswered?.Invoke(screen, chosen));
            return chosen;
        }
    }

    /// <summary>The screen a card reward puts up, which answers with a position rather
    /// than a card.</summary>
    [HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.OptionSelected))]
    internal static class Reward
    {
        [HarmonyPostfix]
        internal static void After(ref Task<int?> __result) => __result = Observe(__result);

        internal static async Task<int?> Observe(Task<int?> inner)
        {
            var option = await WhileOneIsUp(inner);
            Announce("a card reward's answer", () => RewardAnswered?.Invoke(option));
            return option;
        }
    }
}

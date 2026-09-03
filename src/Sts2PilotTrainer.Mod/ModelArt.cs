using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The game's own artwork for a model id.
///
/// The result panel draws cards and potions as pictures, and the pictures are the
/// game's: a card's portrait and a potion's bottle, loaded from the model database
/// this process already has. That is what keeps the packaging contract in
/// <c>docs/distribution.md</c> - this mod ships no resource pack and contributes no
/// art of its own.
///
/// It answers null rather than guessing. A model id the database does not know, or
/// art a build no longer has, is a picture this host cannot draw; the panel then
/// writes the card's name where its portrait would have been, which is the honest
/// answer and never the wrong picture. Nothing here is version-independent, which is
/// why it lives with the rest of the code that knows how this build is put together.
/// </summary>
internal static class ModelArt
{
    /// <summary>The database is walked once per id and remembered. Every fight
    /// redraws the same handful of cards, and <c>ModelDb.AllCards</c> is a scan.</summary>
    private static readonly Dictionary<string, Texture2D?> Known = new(StringComparer.Ordinal);

    /// <summary>The prefix of a potion's model id. Cards and potions live in
    /// different collections, and the id says which.</summary>
    private const string PotionPrefix = "POTION.";

    /// <summary>The artwork for a card or potion model id, or null where this build
    /// has none.</summary>
    internal static Texture2D? Of(string modelId)
    {
        if (Known.TryGetValue(modelId, out var known)) return known;

        Texture2D? art = null;
        try
        {
            art = modelId.StartsWith(PotionPrefix, StringComparison.Ordinal)
                ? ModelDb.AllPotions.FirstOrDefault(potion => potion.Id.ToString() == modelId)?.Image
                : ModelDb.AllCards.FirstOrDefault(card => card.Id.ToString() == modelId)?.Portrait;
        }
        catch (Exception ex)
        {
            // Loading art is the game's own resource loader, and a missing atlas entry
            // is not a reason to lose the result. Said once per id, because it is
            // remembered either way.
            Log.Warn(
                $"[{CombatTrainerMod.ModId}] has no artwork for '{modelId}': " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }

        Known[modelId] = art;
        return art;
    }
}

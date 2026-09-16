using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The game's own artwork for a model id.
///
/// The result panel draws cards and potions as pictures, and the pictures are the
/// game's: a card's portrait and a potion's bottle, loaded from the model database
/// this process already has. Runmobile's one-resource pack contains only the wagon
/// shared by its mod-list and Compendium entries, so this panel contributes no model
/// artwork of its own.
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

    /// <summary>The prefixes that say which collection an id lives in. Cards, potions
    /// and relics are three collections, and the id is what says which to walk.</summary>
    private const string PotionPrefix = "POTION.";

    /// <inheritdoc cref="PotionPrefix"/>
    private const string RelicPrefix = "RELIC.";

    /// <summary>The character-select portrait for a character model id.</summary>
    internal static Texture2D? CharacterPortrait(string modelId)
    {
        try
        {
            return ModelDb.AllCharacters
                .FirstOrDefault(character => character.Id.ToString() == modelId)
                ?.CharacterSelectIcon;
        }
        catch (Exception ex)
        {
            Log.Warn(
                $"[{RunmobileMod.ModId}] has no character portrait for '{modelId}': " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
            return null;
        }
    }

    /// <summary>
    /// The model whose art an id wears: an upgraded card is written <c>CARD.X+1</c>,
    /// the way <c>CanonicalStateProjection</c> and the recorder write it, and the
    /// database knows the card as <c>CARD.X</c>. The portrait is the same card's at
    /// any upgrade level, so the suffix is dropped before the database is asked;
    /// before it was, every upgraded card in a deck drew as its name in place of its
    /// picture.
    /// </summary>
    internal static string ArtId(string modelId)
    {
        var upgrade = modelId.IndexOf('+');
        return upgrade < 0 ? modelId : modelId[..upgrade];
    }

    /// <summary>The artwork for a card, potion or relic model id, or null where this
    /// build has none.</summary>
    internal static Texture2D? Of(string modelId)
    {
        if (Known.TryGetValue(modelId, out var known)) return known;

        var id = ArtId(modelId);
        Texture2D? art = null;
        try
        {
            art = id.StartsWith(PotionPrefix, StringComparison.Ordinal)
                ? ModelDb.AllPotions.FirstOrDefault(potion => potion.Id.ToString() == id)?.Image
                : id.StartsWith(RelicPrefix, StringComparison.Ordinal)
                    ? ModelDb.AllRelics.FirstOrDefault(relic => relic.Id.ToString() == id)?.Icon
                    : ModelDb.AllCards.FirstOrDefault(card => card.Id.ToString() == id)?.Portrait;
        }
        catch (Exception ex)
        {
            // Loading art is the game's own resource loader, and a missing atlas entry
            // is not a reason to lose the result. Said once per id, because it is
            // remembered either way.
            Log.Warn(
                $"[{RunmobileMod.ModId}] has no artwork for '{modelId}': " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }

        Known[modelId] = art;
        return art;
    }
}

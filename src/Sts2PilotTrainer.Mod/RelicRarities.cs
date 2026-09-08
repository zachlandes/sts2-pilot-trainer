using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// How rare a relic is, read out of this build's own model database.
///
/// The strip's ordering rule is <c>RelicStrip</c>'s and this is the one input it
/// cannot answer for itself: a recording carries a relic's model id and nothing else,
/// and how rare that relic is is the game's answer. Keeping the reading here and the
/// rule there is what lets the rule be asserted with no game at all.
///
/// <para>An id this build does not know is <see cref="RelicRarity.Unknown"/> rather
/// than a rarity, and the strip then sorts it last. That is the honest answer for a
/// recording made on a build that had a relic this one has dropped; guessing would put
/// it in front of relics whose rarity somebody actually established.</para>
///
/// <para>The game's own eight rarities map onto the design's four. Ancient is the Boon
/// relic the strip heads with; Starter, Shop and Event are not rarities in the sense
/// the strip orders by - a starter relic is as common as the character it comes with -
/// so they sit with Common, which is where they sit in the run-history pane.</para>
/// </summary>
internal static class RelicRarities
{
    /// <summary>Walked once per id and remembered. A browser row asks for four relics
    /// per run over a list of fifty, and <c>ModelDb.AllRelics</c> is a scan.</summary>
    private static readonly Dictionary<string, Trainer.RelicRarity> Known = new(StringComparer.Ordinal);

    /// <summary>How rare one relic is, as the strip orders by it.</summary>
    internal static Trainer.RelicRarity Of(string modelId)
    {
        if (Known.TryGetValue(modelId, out var known)) return known;

        var rarity = Trainer.RelicRarity.Unknown;
        try
        {
            var relic = ModelDb.AllRelics.FirstOrDefault(candidate => candidate.Id.ToString() == modelId);
            if (relic is not null) rarity = Map(relic.Rarity);
        }
        catch (Exception ex)
        {
            // Said once per id, because it is remembered either way. A relic nobody
            // could rank still shows on the strip; it shows last.
            Log.Warn(
                $"[{RunmobileMod.ModId}] could not read the rarity of '{modelId}': " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }

        Known[modelId] = rarity;
        return rarity;
    }

    /// <summary>
    /// The game's rarity as the strip's.
    ///
    /// Named one by one rather than defaulted, so a ninth rarity added to the game
    /// answers Unknown here - which sorts last - rather than being silently ranked as
    /// one of these.
    /// </summary>
    private static Trainer.RelicRarity Map(MegaCrit.Sts2.Core.Entities.Relics.RelicRarity rarity) => rarity switch
    {
        MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Ancient => Trainer.RelicRarity.Boon,
        MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Rare => Trainer.RelicRarity.Rare,
        MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Uncommon => Trainer.RelicRarity.Uncommon,
        MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Common => Trainer.RelicRarity.Common,
        MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Starter => Trainer.RelicRarity.Common,
        MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Shop => Trainer.RelicRarity.Common,
        MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Event => Trainer.RelicRarity.Common,
        _ => Trainer.RelicRarity.Unknown,
    };
}

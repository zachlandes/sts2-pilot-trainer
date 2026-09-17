using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.TestSupport;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The card reward's screen, stood in for where a headless process answers it.
///
/// The recorder reads a card reward's answer off the client's own
/// <c>NCardRewardSelectionScreen</c>, which this process never draws; the driver
/// answers the same reward through its <see cref="ManifestCardSelector"/>, the
/// engine's own seam, from inside the same call. This hands the recorder what the
/// screen would have: the cards and alternatives offered, in the order the screen
/// lists them, and the position that came back.
/// </summary>
[HarmonyPatch(typeof(ManifestCardSelector), nameof(ManifestCardSelector.GetSelectedCardReward))]
internal static class HeadlessCardRewardScreen
{
    [HarmonyPostfix]
    internal static void After(
        IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives,
        CardRewardSelection __result)
    {
        var offered = options.Select(option => option.Card).ToList();
        int? position = __result.card is { } card
            ? offered.IndexOf(card)
            : __result.alternative is { } alternative
                ? offered.Count + alternatives.ToList().IndexOf(alternative)
                : null;
        RunRecorder.CardRewardAnswered(offered, alternatives, position);
    }
}

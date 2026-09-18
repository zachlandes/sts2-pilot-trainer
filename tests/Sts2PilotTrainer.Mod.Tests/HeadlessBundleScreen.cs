using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The bundle screen Scroll Boxes opens, stood in for where a headless process
/// answers it.
///
/// The recorder reads a bundle's answer off the choice the client's own
/// <c>NChooseABundleSelectionScreen</c> syncs, which this process never draws; the
/// driver answers the same prompt through its <see cref="ManifestCardSelector"/> from
/// inside the stand-in at the prompt's entry point, and syncs nothing. This hands the
/// recorder what the sync would have: the bundles offered and the position that came
/// back, the way <see cref="HeadlessCardRewardScreen"/> hands it a card reward's. A
/// refusal answers with no bundle and tells the recorder nothing, since the driver
/// raises it.
/// </summary>
[HarmonyPatch(typeof(ManifestCardSelector), nameof(ManifestCardSelector.GetSelectedBundle))]
internal static class HeadlessBundleScreen
{
    [HarmonyPostfix]
    internal static void After(IReadOnlyList<IReadOnlyList<CardModel>> bundles, IReadOnlyList<CardModel> __result)
    {
        var position = bundles.ToList().FindIndex(bundle => ReferenceEquals(bundle, __result));
        if (position < 0) return;
        RunRecorder.BundleScreenAnswered(bundles, position);
    }
}

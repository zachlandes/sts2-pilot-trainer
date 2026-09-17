using System.Reflection;
using HarmonyLib;
using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// Every method a Harmony patch class in the shell or the recorder attaches to,
/// resolved on this build. A class whose targets are computed rather than declared
/// contributes nothing, which is correct for the question asked here: none of those
/// are entry points.
///
/// Beside the tests rather than in the engine's <see cref="Engine.ChoiceEntryPoints"/>,
/// because which members the mod patches is the mod's knowledge and the engine
/// cannot reference it.
/// </summary>
internal static class RecorderPatches
{
    internal static IReadOnlySet<MethodBase> Patched() =>
        RunRecorder.PatchClasses
            .Concat(CardScreensUp.PatchClasses)
            .Concat(CardPrompts.PatchClasses)
            .SelectMany(patchClass => patchClass
                .GetCustomAttributes(typeof(HarmonyPatch), inherit: false)
                .OfType<HarmonyPatch>()
                .Select(attribute => Resolve(attribute.info)))
            .OfType<MethodBase>()
            .ToHashSet();

    private static MethodBase? Resolve(HarmonyMethod patch)
    {
        if (patch.declaringType is null) return null;
        if (patch.methodType == MethodType.Constructor || patch.methodName is null)
        {
            return AccessTools.Constructor(patch.declaringType, patch.argumentTypes);
        }

        return AccessTools.Method(patch.declaringType, patch.methodName, patch.argumentTypes);
    }
}

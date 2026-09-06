using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The screens the headless host stands in for at the prompt itself, because no seam
/// answers them.
///
/// Three prompts on v0.111.0 have no <c>ICardSelector</c>-style seam. The bundle
/// screen Scroll Boxes opens takes its first bundle without asking when the engine's
/// test-mode flag is on; the relic screen has no test branch at all and would show a
/// scene; the Crystal Sphere's minigame pushes its own screen and waits on it. Each of
/// the three is patched here, at the game's own entry point, to hand the question to
/// the driver's selector - which answers from the manifest or records a refusal - and
/// to return without a scene tree. None of them decides anything.
///
/// Installed with the rest of <see cref="HeadlessPatches"/>, headlessly only: inside
/// the retail client the player is the one looking at these screens. Which driver
/// answers is <see cref="Current"/>, set by the driver for its lifetime; with no
/// driver at all the game's own path runs, which is what a process that started a run
/// without a manifest asked for.
/// </summary>
internal static class ScreenStandIns
{
    /// <summary>The driver answering prompts right now, or null.</summary>
    internal static IStandInAnswerer? Current { get; set; }

    /// <summary>The Crystal Sphere minigame the engine is waiting on, captured where
    /// the retail client would show its screen, or null when none is open.</summary>
    internal static CrystalSphereMinigame? OpenMinigame { get; private set; }

    /// <summary>What a driver answers a stood-in prompt with.</summary>
    internal interface IStandInAnswerer
    {
        IReadOnlyList<CardModel> AnswerBundle(IReadOnlyList<IReadOnlyList<CardModel>> bundles);

        RelicModel? AnswerRelic(IReadOnlyList<RelicModel> relics);
    }

    internal static void Install(Harmony harmony, List<string> failures)
    {
        Patch(harmony, failures,
            typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseABundleScreen), nameof(BeforeBundleScreen));
        Patch(harmony, failures,
            typeof(RelicSelectCmd), nameof(RelicSelectCmd.FromChooseARelicScreen), nameof(BeforeRelicScreen));
        Patch(harmony, failures,
            typeof(NCrystalSphereScreen), nameof(NCrystalSphereScreen.ShowScreen), nameof(BeforeCrystalSphereScreen));
    }

    /// <summary>The minigame is over, or the run left it; nothing is open. The
    /// subscription goes with it, so a forgotten grid holds nothing here.</summary>
    internal static void ForgetMinigame()
    {
        if (OpenMinigame is { } open) open.Finished -= ForgetMinigame;
        OpenMinigame = null;
    }

    private static void Patch(Harmony harmony, List<string> failures, Type type, string method, string prefix)
    {
        var target = type.GetMethod(method, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        if (target is null)
        {
            failures.Add(
                $"screen stand-in: {type.Name}.{method} not found in this build, so the host cannot answer " +
                "that screen from a manifest and a replay reaching it would take the engine's own answer.");
            return;
        }

        try
        {
            harmony.Patch(
                target,
                prefix: new HarmonyMethod(
                    typeof(ScreenStandIns).GetMethod(prefix, BindingFlags.NonPublic | BindingFlags.Static)!));
        }
        catch (Exception ex)
        {
            failures.Add($"screen stand-in {type.Name}.{method}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Harmony prefix: answer the bundle screen from the driver, or let the
    /// engine's own path run when no driver is answering.</summary>
    private static bool BeforeBundleScreen(
        Player player,
        IReadOnlyList<IReadOnlyList<CardModel>> bundles,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (Current is not { } answerer) return true;

        // The engine's own early returns, kept: a fight that is ending and an empty
        // prompt are not questions, and the game answers them itself.
        if (MegaCrit.Sts2.Core.Combat.CombatManager.Instance is { IsEnding: true } || bundles.Count == 0)
        {
            return true;
        }

        __result = Task.FromResult<IEnumerable<CardModel>>(answerer.AnswerBundle(bundles));
        return false;
    }

    /// <summary>Harmony prefix: the same, for the relic screen.</summary>
    private static bool BeforeRelicScreen(
        Player player, IReadOnlyList<RelicModel> relics, ref Task<RelicModel?> __result)
    {
        if (Current is not { } answerer) return true;

        __result = Task.FromResult(answerer.AnswerRelic(relics));
        return false;
    }

    /// <summary>
    /// Harmony prefix: capture the minigame the screen would have shown, and show
    /// nothing.
    ///
    /// The minigame waits on its own completion source, which the retail screen's
    /// clicks drive through <c>CellClicked</c>; here the driver drives the same member
    /// from the manifest's reveals. The screen instance the game would return is used
    /// by nothing on the engine's path, so null is what a scene tree that does not
    /// exist hands back.
    /// </summary>
    private static bool BeforeCrystalSphereScreen(CrystalSphereMinigame grid, ref NCrystalSphereScreen __result)
    {
        if (Current is null) return true;

        ForgetMinigame();
        OpenMinigame = grid;
        grid.Finished += ForgetMinigame;
        __result = null!;
        return false;
    }
}

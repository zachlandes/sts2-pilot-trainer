using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// Applies the player's recording-retention policy when the singleplayer menu opens.
///
/// This is a shell duty rather than a feature surface.
/// It runs even when every module declines, and at the first moment the game has a selected save profile but no recorder journal is open.
/// </summary>
[HarmonyPatch(typeof(NSingleplayerSubmenu))]
internal static class SingleplayerMenuRetention
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(NSingleplayerSubmenu._Ready))]
    internal static void Apply()
    {
        try
        {
            RecordingRetention.ApplyOnce();
        }
        catch (Exception ex)
        {
            // The player's main menu is not ours to break
            Log.Error(
                $"[{RunmobileMod.ModId}] could not apply recording retention: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }
}

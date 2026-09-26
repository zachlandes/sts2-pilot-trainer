using MegaCrit.Sts2.Core.Logging;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The other mods this game has active, as the play-from surfaces read them.
///
/// One reader for the run view, the run-history plate and the journey, so the three
/// warn about one list. The reading is <see cref="Preflight.ActiveMods"/>, which is
/// not a gate: a mod being loaded is a fact to say, and whether it changed the fight is
/// what the combat-boundary verification refuses on before anybody is handed the
/// controls. A read that fails is logged and warns about nothing, because a warning is
/// not something a screen is refused over.
///
/// No field here is a sibling assembly's type; see docs/in-game-host.md on load order.
/// </summary>
internal static class OtherActiveMods
{
    /// <summary>The names the warning sentence names, or none.</summary>
    internal static IReadOnlyList<string> Names() => Read()?.Names ?? [];

    /// <summary>
    /// The longer account, written to the game's log as a recorded fight is started
    /// and nowhere the player reads. The sentence on screen names the mods; this names
    /// what each declares and what the content hash cannot settle about it.
    /// </summary>
    internal static void LogAtPlayFrom()
    {
        if (Read() is { } advisory)
        {
            Log.Warn($"[{RunmobileMod.ModId}] playing from a recording with other mods active. {advisory.Diagnostic}", 2);
        }
    }

    private static ActiveModsAdvisory? Read()
    {
        try
        {
            return Preflight.ActiveMods();
        }
        catch (Exception ex)
        {
            Log.Warn(
                $"[{RunmobileMod.ModId}] could not read which mods this game has active, so none is warned " +
                $"about: {ex.GetType().Name}: {ex.Message}", 2);
            return null;
        }
    }
}

using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The retail soak: the mod plays standard singleplayer runs inside the retail client
/// with the recorder attached, so the recorder can be held to parity over a night of
/// real-time play.
///
/// An instrument rather than a feature, and the one module that draws nothing. It is
/// off unless the profile's <c>settings.json</c> carries a <c>retail_soak</c> plan,
/// which no control writes and only the soak script does, into an isolated profile;
/// a player's client never runs it. It refuses rather than degrades: recording off, a
/// plan it cannot run, a multiplayer session, another Runmobile run live, or a game
/// the shell could not adopt each stop it before a run is started, in the log.
///
/// What it presses is what a click presses, so what the recorder sees is what it sees
/// of a person: the actions a card, a potion and the end-turn button enqueue, the
/// game's own autoplayer handlers for every screen and room they run cleanly for, and
/// the mod's own pressers for the fight, the event room and the two prompts the
/// game's autoplayer answered engine-side. <c>RetailSoakGuardTests</c> reads this
/// module's IL and refuses every member that would make a run no replay reproduces -
/// a power applied, an enemy killed, a selector pushed, a card auto-played, a seed
/// override, a progress or preference write. See docs/in-game-host.md, "The retail
/// soak".
/// </summary>
internal sealed class RetailSoakModule : IRunmobileModule
{
    internal static RetailSoakModule Instance { get; } = new();

    private readonly Lock _gate = new();

    private string? _refusal;
    private bool _examined;

    private RetailSoakModule()
    {
    }

    /// <summary>The patch classes this module installs: the one arming point.</summary>
    internal static IReadOnlyList<Type> PatchClasses { get; } = [typeof(MainMenuArming)];

    public string Name => "Retail soak";

    public bool Enabled
    {
        get
        {
            Examine();
            return _refusal is null;
        }
    }

    public string? Refusal
    {
        get
        {
            Examine();
            return _refusal;
        }
    }

    public void Install(Harmony harmony)
    {
        foreach (var patchClass in PatchClasses)
        {
            harmony.CreateClassProcessor(patchClass).Patch();
        }
    }

    /// <summary>
    /// Whether this build still has the members the soak presses: the arming point,
    /// the run start and the handlers it reuses. A build that renamed one would leave
    /// the soak armed and unable to start, and the night would end with nothing
    /// measured; refused here it ends with a line saying why.
    /// </summary>
    private void Examine()
    {
        lock (_gate)
        {
            if (_examined) return;
            _examined = true;

            var problems = RetailSoak.MissingGameMembers().ToList();
            if (AccessTools.Method(typeof(NMainMenu), nameof(NMainMenu._Ready)) is null)
            {
                problems.Add($"{nameof(NMainMenu)}.{nameof(NMainMenu._Ready)} is absent from this build, so the soak has nowhere to arm.");
            }

            if (problems.Count > 0) _refusal = string.Join(" ", problems);
        }
    }

    /// <summary>
    /// The arming point: the game's main menu, built.
    ///
    /// Not the mod initializer, which reads nothing because there is no game yet, and
    /// not the singleplayer submenu the retention duty hangs on: that submenu is
    /// created the first time somebody presses Singleplayer, and in a headless client
    /// nobody does. The main menu is built one startup phase before the game has a
    /// model database, so nothing is read here; the night's task waits for the game's
    /// own startup phase to say it is finished before it reads a setting, and adopts
    /// the game only once it has a plan to run.
    /// </summary>
    [HarmonyPatch(typeof(NMainMenu))]
    internal static class MainMenuArming
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(NMainMenu._Ready))]
        internal static void After()
        {
            try
            {
                RetailSoak.MainMenuReady();
            }
            catch (Exception ex)
            {
                // The player's main menu is not ours to break
                Log.Error($"[{RunmobileMod.ModId}] the retail soak could not arm: {ex.GetType().Name}: {ex.Message}", 2);
            }
        }
    }
}

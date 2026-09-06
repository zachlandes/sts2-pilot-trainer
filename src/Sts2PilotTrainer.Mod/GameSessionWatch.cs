using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// What kind of game this client is playing, and therefore whether Runmobile may say
/// or write anything at all.
///
/// The shell's, not a module's, and installed however the modules answer. It decides
/// what this mod may do to somebody else's session, which is the same class of
/// question as <see cref="ProfileWriteBarrier"/> and no feature gets to decide it for
/// itself. A module that refused still must not draw in a multiplayer game, and a
/// module that could not establish what it needs is not what makes a multiplayer game
/// quiet.
///
/// A multiplayer game gets nothing: no menu card, no screen, no transport, and no
/// indicator that a run is or is not being recorded. That last one is why this is a
/// suppression of surfaces rather than of recording - a chip saying "not recording"
/// is still this mod drawing in a game two people are playing, and one of them never
/// installed it.
///
/// It is observed rather than assumed, twice over. <see cref="LiveRun.ReadSession"/>
/// reads the game's own networking and player list, which is the reading; the two
/// patches below latch the moment the game is asked to set a multiplayer session up,
/// which closes the window before there is anything to read - continuing a saved
/// multiplayer run is asynchronous, so the method that starts it returns long before
/// the run exists. Neither stands in for the other: the latch says a multiplayer
/// session was set up in this process and the reading says what the run in progress
/// is, and a session is refused if either says multiplayer.
///
/// The recorder asks the same question for its own purpose and gets the same reading;
/// <see cref="RunSession"/> owns both rules, because "may this be recorded" and "may
/// anything be drawn" are two permissions rather than one.
/// </summary>
internal static class GameSessionWatch
{
    private static readonly Lock Gate = new();

    private static bool _multiplayerSessionSetUp;

    /// <summary>The patch classes the shell installs for this. Two on the game's own
    /// multiplayer run setup, one on the refusal the first of those can answer with,
    /// and one on the teardown every run reaches.</summary>
    internal static IReadOnlyList<Type> PatchClasses { get; } =
    [
        typeof(NewMultiplayerRun), typeof(SavedMultiplayerRun), typeof(MultiplayerSetupRefusedPatch),
        typeof(RunTornDown),
    ];

    /// <summary>
    /// What this session is, as observed.
    ///
    /// The latch wins over the reading rather than being merged with it: while a
    /// multiplayer session is being set up there is nothing to read, and a reading
    /// taken then answers <see cref="RunSessionKind.NoRunInProgress"/> - which is a
    /// state this mod may draw in. So a session the game said was multiplayer stays
    /// multiplayer until the run is torn down, whatever a later reading says.
    ///
    /// What it answers while the latch is set is
    /// <see cref="RunSessionKind.MultiplayerKindUnread"/>, which is what the latch
    /// establishes: the game set a multiplayer session up. Which of the two shapes it
    /// is would be a reading, and no reading was taken.
    /// </summary>
    internal static RunSessionKind Observed
    {
        get
        {
            bool multiplayer;
            lock (Gate) multiplayer = _multiplayerSessionSetUp;
            return multiplayer ? RunSessionKind.MultiplayerKindUnread : LiveRun.ReadSession();
        }
    }

    /// <summary>Whether this mod may put anything in front of the player right
    /// now.</summary>
    internal static bool MaySpeak => RunSession.MaySpeakIn(Observed);

    /// <summary>
    /// The game has set a multiplayer run up in this process.
    ///
    /// Latched rather than read, because this is the one instant where the reading is
    /// not yet available and the fact is certain: the game called its own multiplayer
    /// setup member.
    ///
    /// This suppresses surfaces and nothing else, and it is the whole of what a
    /// multiplayer game gets from this mod: a latch set here refuses every later
    /// reading, so no recording is begun and no artifact is created. What it cannot do
    /// is reach a recording that is already live - see docs/in-game-host.md for that
    /// limit.
    ///
    /// It is called from a prefix, so what it knows is that the game was asked to set
    /// a multiplayer session up rather than that the setup took effect. Going quiet on
    /// a request the game then refuses is the safe direction and it is not free: with
    /// a run in progress the next teardown clears the latch, but a setup attempted
    /// with no run leaves no teardown to reach, which is what
    /// <see cref="MultiplayerSetupRefused"/> is for.
    /// </summary>
    internal static void MultiplayerSessionSetUp()
    {
        lock (Gate)
        {
            if (_multiplayerSessionSetUp) return;
            _multiplayerSessionSetUp = true;
        }

        Log.Info(
            $"[{RunmobileMod.ModId}] this is a multiplayer game; Runmobile will show nothing and record " +
            "nothing until it ends", 2);
    }

    /// <summary>
    /// The setup this process latched for threw, so there is no multiplayer session
    /// and there may be no run to clear the latch either.
    ///
    /// The one way back. A latch nothing lifts is a client that draws no card and
    /// records no run for the rest of the process, saying so in a single log line -
    /// silent and safe, and still a mod that stopped working.
    /// </summary>
    internal static void MultiplayerSetupRefused()
    {
        bool latched;
        lock (Gate)
        {
            latched = _multiplayerSessionSetUp;
            _multiplayerSessionSetUp = false;
        }

        if (!latched) return;

        Log.Info(
            $"[{RunmobileMod.ModId}] that multiplayer game did not start, so Runmobile is watching this " +
            "client again", 2);
    }

    /// <summary>The run is gone. The next session is read again from scratch, because
    /// a client that played a multiplayer game and then started a singleplayer one is
    /// in a singleplayer game.</summary>
    internal static void SessionTornDown()
    {
        lock (Gate) _multiplayerSessionSetUp = false;
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewMultiplayer))]
    internal static class NewMultiplayerRun
    {
        [HarmonyPrefix]
        internal static void Before() => MultiplayerSessionSetUp();
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedMultiplayer))]
    internal static class SavedMultiplayerRun
    {
        // A prefix rather than a postfix: this member is asynchronous, so a postfix
        // fires when its state machine starts rather than when the run exists, and
        // the whole point of the latch is to cover that stretch.
        [HarmonyPrefix]
        internal static void Before() => MultiplayerSessionSetUp();
    }

    /// <summary>
    /// The synchronous setup refused the call, so the latch it set comes off.
    ///
    /// A finalizer rather than a postfix because a postfix does not run when the body
    /// throws, and the throw is the whole signal. Only this member has one: the saved
    /// setup is asynchronous and carries its refusal in the task it returns, where no
    /// patch here sees it, so a failed continue is cleared by the next teardown or not
    /// at all.
    /// </summary>
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpNewMultiplayer))]
    internal static class MultiplayerSetupRefusedPatch
    {
        [HarmonyFinalizer]
        internal static void Finally(Exception? __exception)
        {
            if (__exception is not null) MultiplayerSetupRefused();
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
    internal static class RunTornDown
    {
        [HarmonyPostfix]
        internal static void After() => SessionTornDown();
    }
}

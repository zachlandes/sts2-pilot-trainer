namespace Sts2PilotTrainer.Replay;

/// <summary>
/// What kind of game a run is being played in, as somebody watching the client read
/// it.
///
/// A closed set, because everything downstream dispatches on it and the answers are
/// not interchangeable: this project records, replays and stands a player in
/// singleplayer runs and nothing else. Every other value here is a reason to say
/// nothing and write nothing, and they are kept apart rather than collapsed into one
/// "no" so that the log line a player reads names what was actually seen.
///
/// <see cref="Unreadable"/> is a value rather than an exception because the question
/// is asked from places that must still answer - a menu being built, a run starting -
/// and it is refused like any multiplayer answer. A reading that could not be taken
/// is not a singleplayer run; it is a run nothing established anything about.
/// </summary>
public enum RunSessionKind
{
    /// <summary>Nothing could be read: the client has a run whose networking or
    /// player list this build could not ask about. Refused, like every value below
    /// <see cref="Singleplayer"/>.</summary>
    Unreadable,

    /// <summary>There is no run. The main menu, and the only state besides a
    /// singleplayer run in which this mod may put anything in front of anybody.</summary>
    NoRunInProgress,

    /// <summary>One player, no network game. The only run this project records,
    /// replays or stands anybody in.</summary>
    Singleplayer,

    /// <summary>More than one player sharing this client, with no network game. The
    /// game's own code calls this shape "fake multiplayer"; from the recorder's side
    /// it is a multiplayer run, because the history holds decisions more than one
    /// person made.</summary>
    LocalMultiplayer,

    /// <summary>A network game, hosting or joined.</summary>
    NetworkedMultiplayer,

    /// <summary>
    /// A multiplayer session the game itself set up, of a kind nothing here read.
    ///
    /// The one value that comes from a latch rather than a reading: the game called
    /// its own multiplayer setup member, which settles that this is a multiplayer
    /// session and settles nothing about which of the two shapes above it is - the
    /// run does not exist yet, so there is no player list or network game to read.
    /// It is a value of its own rather than either of them, because a recording and a
    /// log line that named a network game here would be asserting what nobody looked
    /// at.
    /// </summary>
    MultiplayerKindUnread,

    /// <summary>A recorded network game being played back by the game's own replay
    /// path. Not a run anybody is playing, and not one this project may describe.</summary>
    Spectated,
}

/// <summary>
/// What a <see cref="RunSessionKind"/> permits, in the one place that decides it.
///
/// Both questions are asked from the mod, where the reading is taken, and both are
/// answered here, where they can be tested on a machine that does not own the game.
/// They are deliberately separate questions with the same shape: recording a run and
/// drawing anything at all are different permissions, and a later release that shows
/// a player something in a game it does not record would change one of these and not
/// the other.
/// </summary>
public static class RunSession
{
    /// <summary>
    /// Whether a run of this kind may be recorded.
    ///
    /// One value passes. The MVP records, replays and enters singleplayer runs only,
    /// and a recording of anything else is one nothing here could reproduce: a
    /// multiplayer history holds decisions this client never made, and the headless
    /// driver replays one player's run.
    /// </summary>
    public static bool MayBeRecorded(RunSessionKind kind) => kind == RunSessionKind.Singleplayer;

    /// <summary>
    /// Whether this mod may put anything in front of the player - a menu card, a
    /// screen, a transport, an indicator that a run is being recorded.
    ///
    /// A multiplayer game gets nothing at all, which is a stronger rule than "records
    /// nothing": an indicator saying a run is not being recorded is still this mod
    /// drawing in somebody's multiplayer session. The menu passes because there is no
    /// run to be wrong about yet, and it is where the mod's own card lives.
    /// </summary>
    public static bool MaySpeakIn(RunSessionKind kind) =>
        kind is RunSessionKind.Singleplayer or RunSessionKind.NoRunInProgress;

    /// <summary>
    /// The reading in the words a log line uses. Interpolated from the kind rather
    /// than written at each caller, so the two refusals cannot come to describe the
    /// same reading differently.
    /// </summary>
    public static string Describe(RunSessionKind kind) => kind switch
    {
        RunSessionKind.Singleplayer => "a singleplayer run",
        RunSessionKind.NoRunInProgress => "no run in progress",
        RunSessionKind.LocalMultiplayer => "a multiplayer run shared with another player on this client",
        RunSessionKind.NetworkedMultiplayer => "a multiplayer run over the network",
        RunSessionKind.MultiplayerKindUnread => "a multiplayer run this client set up",
        RunSessionKind.Spectated => "a recorded network game being played back",
        _ => "a run this build could not read the kind of",
    };
}

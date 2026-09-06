using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// Which colour the recorder's row is drawn in.
///
/// Two, because the row makes two kinds of claim. The overlay's own colour says the row
/// is one more line of the game's version information, and a recording under way is
/// exactly that: a fact about this session, stated where the build and the seed are.
/// The warning hue is the eligibility screen's, and it is used for the one row that
/// asks something of the player - a recording that stopped is a run they will not be
/// able to play from, and the overlay is where they will notice.
/// </summary>
public enum RecorderRowTone
{
    /// <summary>Whatever the overlay's own rows are drawn in. The row inherits it and
    /// carries no colour of its own.</summary>
    Overlay,

    /// <summary>The eligibility screen's warning hue.</summary>
    Warning,
}

/// <summary>
/// What the row can read, and nothing else.
///
/// The recorder is either attached to the run being played or it is not, and where it
/// is, its capture is at one of three states. A recorder that is not attached has no
/// capture, so <paramref name="Capture"/> is null exactly there; the derivation treats
/// an active recorder with no state as nothing established, rather than as a recording.
/// </summary>
/// <param name="RecorderActive">Whether a recorder is attached to the run being played.
/// False when the player turned recording off, when the run is a trainer run this mod
/// constructed, and when the recorder module refused this build.</param>
/// <param name="Capture">Where that recorder's capture has got to.</param>
public sealed record RecorderFacts(bool RecorderActive, RunCaptureState? Capture);

/// <summary>
/// The recorder's presence on screen: one more row of the game's own version overlay.
///
/// A player who is just playing a run does not want a control on their screen for a
/// feature they expect to simply work, and the overlay is already there - build, date,
/// seed and MODDED at the corner, in a small dim capital the game puts up itself and the
/// player hides from the same place. So the recorder says what it is doing as one more
/// row of that column, in the overlay's own font, size and colour, and says nothing
/// anywhere else. A player who hides the overlay hides this with it; that is the point
/// rather than a gap.
///
/// The row is text. It is not pressed, it carries no tooltip and it sits on no plate;
/// <see cref="ElementSurface.Pressable"/> is false in every state, and
/// <see cref="ElementSurface.Absent"/> is how the row is not there at all. A chip a
/// player could choose to have instead is left possible as a later setting and is not
/// drawn here.
///
/// <see cref="For"/> is total and pure over <see cref="RecorderFacts"/>, the way
/// <see cref="PlaybackTransport.For"/> is over its phase and facts, and for the same
/// reason: what is on screen is read off the facts every time they can have changed,
/// never built once and patched.
/// </summary>
/// <param name="Row">Whether the row is on the surface.</param>
/// <param name="Text">What it reads. Empty where the row is absent.</param>
/// <param name="Tone">What colour it is drawn in.</param>
public sealed record RecorderPresence(ElementSurface Row, string Text, RecorderRowTone Tone)
{
    /// <summary>No row at all.</summary>
    public static readonly RecorderPresence Nothing =
        new(ElementSurface.Absent, string.Empty, RecorderRowTone.Overlay);

    /// <summary>
    /// The row for these facts.
    ///
    /// Three rows the design names and two it does not. A recorder attached and
    /// recording is RECORDING in the overlay's colour; one attached whose watch has a
    /// hole in it is RECORDING STOPPED in the warning hue; none attached is no row. A
    /// capture that finished is a run that is over, so nothing is being recorded and the
    /// row says nothing rather than something stale; an active recorder with no capture
    /// state is a contradiction in the facts, and a contradiction draws nothing rather
    /// than guessing which half is right.
    /// </summary>
    public static RecorderPresence For(RecorderFacts facts)
    {
        if (!facts.RecorderActive) return Nothing;

        return facts.Capture switch
        {
            RunCaptureState.Recording =>
                new RecorderPresence(ElementSurface.Shown(), RecorderCopy.Recording, RecorderRowTone.Overlay),
            RunCaptureState.Broken =>
                new RecorderPresence(ElementSurface.Shown(), RecorderCopy.RecordingStopped, RecorderRowTone.Warning),
            _ => Nothing,
        };
    }
}

using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// Which colour the recorder's row is drawn in.
///
/// Two, because the row makes two kinds of claim. The overlay's own colour says the row
/// is one more line of the game's version information, and a recording under way is
/// exactly that: a fact about this session, stated where the build and the seed are.
/// The warning hue is the eligibility screen's, and it is used for the rows that ask
/// something of the player - a recording that stopped is a run they will not be able to
/// play from, and one that can no longer be shared is a run they may want to start
/// again, and the overlay is where they will notice either.
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
///
/// Whether the capture is still watching and whether it can be shared are two facts and
/// both are read, because a reload that rewound the run behind what was recorded costs
/// the recording its continuity and costs the watch nothing. A row derived from the
/// state alone would say a run was still being recorded and say nothing about the one
/// thing that changed about it.
/// </summary>
/// <param name="RecorderActive">Whether a recorder is attached to the run being played.
/// False when the player turned recording off, when the run is a trainer run this mod
/// constructed, and when the recorder module refused this build.</param>
/// <param name="Capture">Where that recorder's capture has got to.</param>
/// <param name="Continuous">Whether that capture can still account for the run from its
/// start, which is what decides whether the recording may ever be shared. Stated rather
/// than defaulted: a caller that left it out would be claiming continuity it had not
/// read.</param>
public sealed record RecorderFacts(bool RecorderActive, RunCaptureState? Capture, bool Continuous);

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
    /// Four rows the design names and two it does not. A recorder attached and
    /// recording a run it can account for is RECORDING in the overlay's colour; one
    /// still recording a run it cannot is RECORDING · NOT SHAREABLE in the warning hue,
    /// because the recording goes on and the one thing that changed about it is the
    /// player's to know; one whose watch stopped is RECORDING STOPPED in the same hue;
    /// none attached is no row. Neither warning row states a cause - what made the hole
    /// is in the journal, and the overlay is a column of short capitals. A capture that finished is a run that is over, so nothing is
    /// being recorded and the row says nothing rather than something stale; an active
    /// recorder with no capture state is a contradiction in the facts, and a
    /// contradiction draws nothing rather than guessing which half is right.
    /// </summary>
    public static RecorderPresence For(RecorderFacts facts)
    {
        if (!facts.RecorderActive) return Nothing;

        return facts.Capture switch
        {
            RunCaptureState.Recording when facts.Continuous =>
                new RecorderPresence(ElementSurface.Shown(), RecorderCopy.Recording, RecorderRowTone.Overlay),
            RunCaptureState.Recording =>
                new RecorderPresence(
                    ElementSurface.Shown(), RecorderCopy.RecordingNotShareable, RecorderRowTone.Warning),
            RunCaptureState.Broken =>
                new RecorderPresence(ElementSurface.Shown(), RecorderCopy.RecordingStopped, RecorderRowTone.Warning),
            _ => Nothing,
        };
    }
}

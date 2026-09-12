using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// What the bookmark tag can read, and nothing else.
///
/// Six facts, all the recorder's. Whether it is attached to the run being played;
/// where its capture has got to; which fight has just ended with nothing happening
/// since, or null; whether the run has moved on from that fight's floor; whether it
/// is in the middle of doing so; and whether that fight is bookmarked. The fourth and
/// fifth are separate from the third rather than folded into it because the recorder
/// reads them apart - a fight's end from its own capture closing, the move from the
/// map - and a derivation over all of them is one that cannot draw a tag for a fight
/// the run has already left.
///
/// The fifth exists because the fourth is read off the recording, and a map move is
/// in the recording only once the engine has settled after it: the room is built,
/// the fight it dealt has started and its first turn has begun. Until then the last
/// fight the recording holds is still the one that just ended and no floor has been
/// entered since, so a tag derived over the fourth alone drew the previous fight's
/// bookmark over the opening frames of the next one. The recorder knows the moment
/// the node was pressed - the move is announced to it before it settles - and that
/// is the moment the player left the floor.
/// </summary>
/// <param name="RecorderActive">Whether a recorder is attached to the run being played.
/// False when the player turned recording off, when the run is a trainer run this mod
/// constructed, when the recorder module refused this build, and in a multiplayer
/// session.</param>
/// <param name="Capture">Where that recorder's capture has got to, the same reading
/// <see cref="RecorderFacts"/> takes. Null exactly where no recorder is attached.</param>
/// <param name="FightJustEnded">The ordinal of the fight that has just ended, or null
/// when no fight has, or when something has happened since.</param>
/// <param name="MovedOn">Whether the run has left the floor that fight was on, as the
/// recording reads it: a floor entered after the fight ended.</param>
/// <param name="LeavingTheFloor">Whether a map move has been announced to the recorder
/// and not yet recorded - the player has pressed the node, and the engine is still
/// building the room at the other end of it.</param>
/// <param name="Bookmarked">Whether that fight is bookmarked right now.</param>
public sealed record FightMarkFacts(
    bool RecorderActive,
    RunCaptureState? Capture,
    int? FightJustEnded,
    bool MovedOn,
    bool LeavingTheFloor,
    bool Bookmarked);

/// <summary>
/// The bookmark tag: one control, hung under the top bar for exactly the stretch
/// between a fight ending and the run moving on.
///
/// That stretch is the loot screen and the card screen behind it on a win, and the
/// game's ending on a loss, where the run never moves on and the tag stays until the
/// run is torn down. It never appears after an event or a chest, because those end no
/// fight; and it never appears in a trainer fight, because nothing is recorded there
/// and the fight is somebody else's run.
///
/// Absent rather than greyed wherever nothing could be saved: a control that could not
/// save anything would only be a control saying why, and the shell's rule for a "no"
/// is that nothing is drawn. Pressing toggles; pressing again is the undo, which is the
/// same one-toggle-on-one-fact rule the whole-run save has. A brief fill on press is
/// the whole feedback - no toast, no caption.
///
/// <see cref="For"/> is total and pure over <see cref="FightMarkFacts"/>, the way
/// <see cref="PlaybackTransport.For"/> and <see cref="RecorderPresence.For"/> are, and
/// for the same reason: what is on screen is read off the facts every time they can
/// have changed, never built once and patched. The mod projects it and never decides.
/// </summary>
/// <param name="Control">The one control. Present, drawn and pressable are answered
/// separately, because a Godot control that is hidden takes no input.</param>
/// <param name="Fight">The fight the control is about, or null where there is none.</param>
/// <param name="Bookmarked">Whether the control is drawn filled - the mark is on - or
/// hollow.</param>
public sealed record FightMark(ElementSurface Control, int? Fight, bool Bookmarked)
{
    /// <summary>No tag at all.</summary>
    public static readonly FightMark Nothing = new(ElementSurface.Absent, null, false);

    /// <summary>
    /// The tag for these facts.
    ///
    /// One state draws it: a recorder attached and still recording the run - or one
    /// that has recorded it to its end, which is where a lost fight is bookmarked on
    /// the game's death screen - a fight just ended, and the run still on its floor,
    /// neither gone from it nor on its way. Everything else is nothing. A capture whose
    /// watch has a hole in it draws no tag, because the row on the overlay has just told
    /// the player the recording stopped and a control offering to save into it would say
    /// the opposite. The control is always
    /// pressable when it is drawn - there is no refused form of this tag, because a tag
    /// that could not save is absent.
    /// </summary>
    public static FightMark For(FightMarkFacts facts)
    {
        if (!facts.RecorderActive || facts.Capture is not (RunCaptureState.Recording or RunCaptureState.Finished))
        {
            return Nothing;
        }

        if (facts.FightJustEnded is not { } fight || facts.MovedOn || facts.LeavingTheFloor) return Nothing;

        return new FightMark(
            new ElementSurface(
                Presence.Drawn,
                Pressable: true,
                Press.None,
                TooltipTitle: facts.Bookmarked ? RecorderCopy.Bookmarked : RecorderCopy.BookmarkThisFight,
                TooltipBody: facts.Bookmarked ? RecorderCopy.PressToRemoveBookmark : string.Empty),
            fight,
            facts.Bookmarked);
    }
}

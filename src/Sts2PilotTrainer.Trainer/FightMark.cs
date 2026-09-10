namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// What the bookmark tag can read, and nothing else.
///
/// Four facts, all the recorder's. Whether it is attached to the run being played;
/// which fight has just ended with nothing happening since, or null; whether the run
/// has moved on from that fight's floor; and whether that fight is bookmarked. The
/// third is separate from the second rather than folded into it because the recorder
/// reads them apart - a fight's end from its own capture closing, the move from the
/// map - and a derivation over both is one that cannot draw a tag for a fight the run
/// has already left.
/// </summary>
/// <param name="RecorderActive">Whether a recorder is attached to the run being played.
/// False when the player turned recording off, when the run is a trainer run this mod
/// constructed, when the recorder module refused this build, and in a multiplayer
/// session.</param>
/// <param name="FightJustEnded">The ordinal of the fight that has just ended, or null
/// when no fight has, or when something has happened since.</param>
/// <param name="MovedOn">Whether the run has left the floor that fight was on.</param>
/// <param name="Bookmarked">Whether that fight is bookmarked right now.</param>
public sealed record FightMarkFacts(bool RecorderActive, int? FightJustEnded, bool MovedOn, bool Bookmarked);

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
    /// One state draws it: a recorder attached, a fight just ended, and the run still
    /// on its floor. Everything else is nothing. The control is always pressable when
    /// it is drawn - there is no refused form of this tag, because a tag that could not
    /// save is absent.
    /// </summary>
    public static FightMark For(FightMarkFacts facts)
    {
        if (!facts.RecorderActive || facts.FightJustEnded is not { } fight || facts.MovedOn) return Nothing;

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

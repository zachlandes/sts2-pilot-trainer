namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// Whether an element is on the surface at all, and if it is, whether it carries a
/// face of its own.
///
/// The distinction is the whole of why this type exists. In Godot a control that is
/// not visible receives no input, so "hidden" and "not pressable" were one decision
/// while a single boolean drove both - and the chip, which is meant to say nothing
/// until it is pressed, ended up with nothing that could be pressed. Silent is the
/// answer: on the surface, taking input over its whole box, drawing nothing but the
/// hover and focus rim the game uses for "this is the thing you are about to press".
/// </summary>
public enum Presence
{
    /// <summary>Not part of the surface in this mode. The only state in which the
    /// node is invisible, and so the only one in which it is never hit-tested, never
    /// tooltipped and its handler unreachable.</summary>
    Absent,

    /// <summary>On the surface with its own face: a plate, an edge, a glyph.</summary>
    Drawn,

    /// <summary>On the surface and silent. The chip's press target.</summary>
    Silent,
}

/// <summary>
/// What pressing an element does.
///
/// Named rather than wired, so the surface says which of the two menus the one press
/// target opens and no caller has to re-derive the mode to find out.
/// </summary>
public enum Press
{
    None,
    Back,
    PlayOrPause,
    Step,
    OpenSpeedMenu,
    OpenChipMenu,
    OpenVideo,
}

/// <summary>Which menu the surface offers under it, if any.</summary>
public enum MenuKind
{
    None,
    Speed,
    Chip,
}

/// <summary>
/// One element of the transport, answering the three questions that used to be one.
///
/// Present, drawn, pressable - in that order, because each only means anything given
/// the one before it. "Present but not drawn" is the chip's press target; "present,
/// drawn, not pressable" is a refused control, which the design says stays where it
/// is and, where a reason has been written for it, says why; "absent" is the only
/// state that hides the node.
/// </summary>
/// <param name="Pressable">Whether input reaches it. Meaningless while
/// <paramref name="Presence"/> is <see cref="Presence.Absent"/>, and always false
/// there, so nothing has to check both.</param>
public sealed record ElementSurface(
    Presence Presence,
    bool Pressable,
    Press Press,
    TransportGlyph? Glyph = null,
    string TooltipTitle = "",
    string TooltipBody = "")
{
    /// <summary>Nothing here at all: the state that hides the node.</summary>
    public static readonly ElementSurface Absent = new(Presence.Absent, false, Press.None);

    /// <summary>On the surface and carrying nothing that can be acted on - a label,
    /// the mark, the counter.</summary>
    public static ElementSurface Shown(TransportGlyph? glyph = null) =>
        new(Presence.Drawn, false, Press.None, glyph);

    /// <summary>Present when there is something to show, absent when there is
    /// not.</summary>
    public static ElementSurface ShownIf(bool present) => present ? Shown() : Absent;
}

/// <summary>
/// The whole transport as a table: for the mode it is in, what each element is.
///
/// One derivation fills this and the strip projects it, which is the rule that
/// replaced a single boolean deciding what exists, what is visible and what can be
/// pressed across every mode. Four defects came out of that boolean in one branch -
/// a menu left hanging under a chip, a chip with no press target, an opening window
/// stating a speed that was not in force, and a chip state applied once and never
/// re-derived - and all four are unstateable here, because the three questions are
/// answered separately and answered in one place.
///
/// The strip reads this and never the mode. Geometry is the single exception:
/// <paramref name="ChipPlate"/> says which of the two shapes to lay out, because a
/// chip and a tag put their contents in different places.
/// </summary>
/// <param name="HoldLine">Whether the tag's one moving part may run here. It shows a
/// hold draining under Play, and it travels while the game is between screens - the
/// same claim either way, that the transport is waiting on the game rather than
/// stopped.</param>
public sealed record TransportSurface(
    bool ChipPlate,
    ElementSurface Mark,
    ElementSurface Identity,
    ElementSurface Title,
    ElementSurface Counter,
    ElementSurface Speed,
    ElementSurface Back,
    ElementSurface Play,
    ElementSurface Step,
    bool HoldLine,
    bool Note,
    bool Ledger,
    MenuKind Menu);

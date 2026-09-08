using Godot;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The library's ink, in one place.
///
/// The surface is drawn on the game's own parchment, so every colour here is one that
/// reads on it. They are the same values <c>ScreenMarkup</c> and <c>LibraryMarkup</c>
/// already use, named once so a control the mod positions and a sentence the mod
/// writes cannot drift apart - a teal numeral in markup and a teal ring drawn beside
/// it have to be the same teal.
///
/// Nothing here is a new palette. The green and the red are the eligibility screen's,
/// the muted is the supporting colour every surface in this mod dims with, and the
/// gold is the game's own rarity frame read back as a colour for the strip's Boon
/// mark.
/// </summary>
internal static class LibraryPalette
{
    /// <summary>Supporting text and anything under a row. Dimmer than a row, so the
    /// rows read first.</summary>
    internal static readonly Color Muted = new(0.714f, 0.659f, 0.573f);

    /// <summary>The parchment's own ink, for a label the mod positions beside the
    /// game's own.</summary>
    internal static readonly Color Ink = new(0.184f, 0.157f, 0.129f);

    /// <summary>What this player has done: a played fight's tick on the strip, the
    /// Last floor replayed column. The one colour on this surface that is about the
    /// person rather than the run.</summary>
    internal static readonly Color Teal = new(0.278f, 0.678f, 0.643f);

    /// <summary>Requirement met, and the pane's verdict line. The eligibility
    /// screen's own affirmative green.</summary>
    internal static readonly Color Green = new(0.561f, 0.788f, 0.447f);

    /// <summary>A recording this game is not the one for. The eligibility screen's own
    /// red, and the one place the plate raises its voice.</summary>
    internal static readonly Color Red = new(0.878f, 0.459f, 0.353f);

    /// <summary>The game's own gold rarity frame, as the strip's Boon mark.</summary>
    internal static readonly Color Gold = new(0.847f, 0.706f, 0.353f);

    /// <summary>A cell of the strip nobody has stood in and nothing is wrong with:
    /// the parchment's own line weight.</summary>
    internal static readonly Color Line = new(0.404f, 0.353f, 0.290f);
}

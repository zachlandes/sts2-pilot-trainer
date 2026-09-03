using Godot;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The playback transport, drawn: one strip docked in the band under the game's own
/// top bar, built once and kept for the whole watched journey.
///
/// Built once is the point of it. The popup this replaces was created and torn down
/// around every decision, which is why it could not carry a position across the
/// map-to-combat transition and why it had to cover the screen a player is here to
/// look at. This strip is a child of the run's own persistent UI, so the room
/// underneath can change without it noticing; <see cref="Apply"/> is how it changes
/// what it says.
///
/// Three facts about the retail client decide its shape and each one is written into
/// the code rather than assumed. It sits under the top bar because that band is empty
/// on every screen this journey walks past. Its root and its background ignore the
/// mouse, so the map, the event and the fight underneath keep every click that is not
/// on a button. Its buttons take focus, so a controller can reach them.
///
/// It computes nothing: every word comes from <see cref="PlaybackTransport"/>. Built
/// from stock Godot nodes for the same reason the result panel is - this assembly has
/// no Godot source generators, so a <c>Control</c> subclass of ours would never have
/// its overrides called, and stock nodes are also what lets the whole strip be
/// assembled and asserted on in a process with no game.
/// </summary>
internal sealed class PlaybackTransportStrip
{
    internal const string RootName = "CombatTrainerTransport";

    // ── The palette ────────────────────────────────────────────────────────
    //
    // Provisional, and deliberately marked so. Every colour, size and position below
    // was chosen by the engineer who made the transport work, to be legible in the
    // client and to keep off the relic inventory; none of it is a design anybody
    // decided. docs/mod-ui-direction.md carries the captain's goal for these
    // surfaces - native to the game, plainly not part of it, iconography where text
    // is doing an icon's job - and the constraints a redesign has to hold.
    // What is not provisional is everything outside this file: one long-lived node,
    // parented to the run's persistent interface, letting clicks through, collapsing
    // during the player's fight.

    private static readonly Color StripFill = Rgb(0x10, 0x12, 0x14, 0.88f);
    private static readonly Color StripEdge = Rgb(0x5c, 0x63, 0x6b, 0.9f);
    private static readonly Color ChipText = Rgb(0xf1, 0xdf, 0xae);
    private static readonly Color CaptionText = Rgb(0xe8, 0xe4, 0xda);
    private static readonly Color CounterText = Rgb(0xa9, 0xb3, 0xbd);
    private static readonly Color NoteText = Rgb(0x7d, 0x85, 0x90);
    private static readonly Color ButtonFill = Rgb(0xd9, 0xb2, 0x5f);
    private static readonly Color ButtonText = Rgb(0x10, 0x12, 0x14);

    /// <summary>A control the transport is not offering right now. Drawn rather than
    /// removed: a strip whose buttons move about as the journey goes on is a strip
    /// nobody can aim at.</summary>
    private static readonly Color DisabledFill = Rgb(0x3a, 0x3d, 0x42);

    private static readonly Color DisabledText = Rgb(0x7d, 0x85, 0x90);

    // ── The layout ─────────────────────────────────────────────────────────

    private const float MaxStripWidth = 1180f;

    /// <summary>The bar itself: the chip, the counter, the caption and the three
    /// controls.</summary>
    private const float BarHeight = 84f;

    /// <summary>The extra band a once-only sentence gets. Its own full-width line
    /// rather than a share of the caption's, because it is a rule about how to read
    /// the whole journey and the first thing tried - fitting it beside the caption -
    /// ran it under the buttons and cut it off in the client.</summary>
    private const float NoteHeight = 34f;
    private const float SideMargin = 48f;
    private const float Pad = 20f;
    private const float ButtonWidth = 132f;
    private const float ButtonHeight = 44f;
    private const float ButtonGap = 10f;
    private const float ChipWidth = 210f;

    /// <summary>The chip the strip shrinks to during the player's own fight. Wide
    /// enough for the trainer's name and no wider: the whole point of it is that it
    /// is not in the way.</summary>
    private const float ChipOnlyWidth = 220f;

    private const float ChipOnlyHeight = 46f;

    private const int ChipFontSize = 18;
    private const int CaptionFontSize = 20;
    private const int CounterFontSize = 15;
    private const int NoteFontSize = 13;

    private readonly Control _root;
    private readonly ColorRect _edge;
    private readonly ColorRect _fill;
    private readonly Label _chip;
    private readonly Label _counter;
    private readonly Label _caption;
    private readonly Label _note;
    private readonly Button _back;
    private readonly Button _forward;
    private readonly Button _play;
    private readonly Vector2 _viewport;
    private readonly float _dockTop;

    private PlaybackTransportStrip(
        Control root, ColorRect edge, ColorRect fill, Label chip, Label counter, Label caption, Label note,
        Button back, Button forward, Button play, Vector2 viewport, float dockTop)
    {
        _root = root;
        _edge = edge;
        _fill = fill;
        _chip = chip;
        _counter = counter;
        _caption = caption;
        _note = note;
        _back = back;
        _forward = forward;
        _play = play;
        _viewport = viewport;
        _dockTop = dockTop;
    }

    internal Control Root => _root;

    internal Button Back => _back;

    internal Button Forward => _forward;

    internal Button Play => _play;

    /// <summary>
    /// Assembles the strip.
    /// </summary>
    /// <param name="state">What it says to begin with.</param>
    /// <param name="viewport">The surface it is docked on.</param>
    /// <param name="dockTop">How far down the band under the game's top bar starts.
    /// Passed in rather than measured here, because the top bar is the game's node
    /// and this class draws in a process that may not have one.</param>
    /// <param name="font">The font the game's own labels use, or null to leave the
    /// theme's default in place.</param>
    internal static PlaybackTransportStrip Build(
        PlaybackTransport state, Vector2 viewport, float dockTop, Font? font,
        Action back, Action forward, Action play)
    {
        var root = new Control
        {
            Name = RootName,
            Position = Vector2.Zero,
            Size = viewport,
            // The screen underneath is the thing the player is watching. Everything
            // that is not a button lets its clicks through, which is what keeps the
            // map, the event and the fight working while the strip is up.
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        var width = Math.Min(MaxStripWidth, viewport.X - (2 * SideMargin));
        var left = (viewport.X - width) / 2;

        var edge = Box(StripEdge, left - 2, dockTop - 2, width + 4, BarHeight + 4);
        edge.Name = "StripEdge";
        root.AddChild(edge);

        var fill = Box(StripFill, left, dockTop, width, BarHeight);
        fill.Name = "Strip";
        root.AddChild(fill);

        var chip = Styled("Chip", state.Chip, ChipFontSize, ChipText, font);
        var counter = Styled("Counter", state.Counter, CounterFontSize, CounterText, font);
        var caption = Styled("Caption", state.Caption, CaptionFontSize, CaptionText, font);
        var note = Styled("Note", state.Note, NoteFontSize, NoteText, font);
        foreach (var label in new[] { chip, counter, caption, note }) root.AddChild(label);

        var backButton = MakeButton("Back", state.Back, font, back);
        var forwardButton = MakeButton("Forward", state.Forward, font, forward);
        var playButton = MakeButton("Play", state.Play, font, play);
        foreach (var button in new[] { backButton, forwardButton, playButton }) root.AddChild(button);

        var strip = new PlaybackTransportStrip(
            root, edge, fill, chip, counter, caption, note,
            backButton, forwardButton, playButton, viewport, dockTop);
        strip.Apply(state);
        return strip;
    }

    /// <summary>
    /// Changes what the strip says, without rebuilding it.
    ///
    /// The whole reason this class holds its nodes. A strip rebuilt between decisions
    /// would be a popup with a different shape: it would lose focus, lose its place in
    /// the tree, and be gone across exactly the transition this design exists to
    /// survive.
    /// </summary>
    internal void Apply(PlaybackTransport state)
    {
        _chip.Text = state.Chip;
        _counter.Text = state.Counter;
        _caption.Text = state.Caption;
        _note.Text = state.Note;

        var hasNote = state.Note.Length > 0;
        _note.Visible = hasNote;

        Dress(_back, state.Back);
        Dress(_forward, state.Forward);
        Dress(_play, state.Play);

        var width = state.HasControls
            ? Math.Min(MaxStripWidth, _viewport.X - (2 * SideMargin))
            : ChipOnlyWidth;
        var bar = state.HasControls ? BarHeight : ChipOnlyHeight;
        var height = bar + (hasNote ? NoteHeight : 0);
        // The chip collapses toward the strip's own right end rather than to the
        // left margin, and that is about the game rather than about symmetry: the
        // band under the top bar carries the run's relic inventory along its left,
        // and a chip parked there covers relics the player is fighting with.
        var band = Math.Min(MaxStripWidth, _viewport.X - (2 * SideMargin));
        var bandLeft = (_viewport.X - band) / 2;
        var left = state.HasControls ? bandLeft : bandLeft + band - ChipOnlyWidth;

        Place(_edge, left - 2, _dockTop - 2, width + 4, height + 4);
        Place(_fill, left, _dockTop, width, height);

        foreach (var button in new[] { _back, _forward, _play }) button.Visible = state.HasControls;
        _counter.Visible = state.HasControls;
        _caption.Visible = state.HasControls;

        if (!state.HasControls)
        {
            Place(_chip, left + Pad, _dockTop, ChipOnlyWidth - (2 * Pad), ChipOnlyHeight);
            return;
        }

        var buttonsRight = left + width - Pad;
        var playX = buttonsRight - ButtonWidth;
        var forwardX = playX - ButtonGap - ButtonWidth;
        var backX = forwardX - ButtonGap - ButtonWidth;
        var buttonsTop = _dockTop + ((bar - ButtonHeight) / 2);
        Place(_play, playX, buttonsTop, ButtonWidth, ButtonHeight);
        Place(_forward, forwardX, buttonsTop, ButtonWidth, ButtonHeight);
        Place(_back, backX, buttonsTop, ButtonWidth, ButtonHeight);

        Place(_chip, left + Pad, _dockTop + 12, ChipWidth, 26);
        Place(_counter, left + Pad, _dockTop + bar - 38, ChipWidth, 26);
        Place(
            _caption, left + Pad + ChipWidth, _dockTop + ((bar - 32) / 2),
            backX - ButtonGap - (left + Pad + ChipWidth), 32);

        // The whole inner width, under everything. A sentence squeezed between the
        // caption and the buttons is the one that came back cut off.
        Place(_note, left + Pad, _dockTop + bar - 4, width - (2 * Pad), NoteHeight - 6);
    }

    private static void Dress(Button button, TransportControl control)
    {
        button.Text = control.Label;
        button.Disabled = !control.Enabled;
        var fill = control.Enabled ? ButtonFill : DisabledFill;
        var style = new StyleBoxFlat { BgColor = fill, BorderColor = fill };
        style.SetCornerRadiusAll(8);
        style.SetBorderWidthAll(2);
        foreach (var state in new[] { "normal", "hover", "pressed", "focus", "disabled" })
        {
            button.AddThemeStyleboxOverride(state, style);
        }

        var text = control.Enabled ? ButtonText : DisabledText;
        foreach (var entry in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_disabled_color" })
        {
            button.AddThemeColorOverride(entry, text);
        }
    }

    private static Button MakeButton(string name, TransportControl control, Font? font, Action pressed)
    {
        var button = new Button
        {
            Name = name,
            Text = control.Label,
            Size = new Vector2(ButtonWidth, ButtonHeight),
            // Takes focus on purpose: a control a keyboard or a controller cannot
            // reach is a control half the players do not have.
            FocusMode = Control.FocusModeEnum.All,
        };

        button.AddThemeFontSizeOverride("font_size", CounterFontSize + 2);
        if (font is not null) button.AddThemeFontOverride("font", font);
        button.Pressed += () => pressed();
        return button;
    }

    private static Label Styled(string name, string text, int size, Color color, Font? font)
    {
        var label = new Label
        {
            Name = name,
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
        };

        if (font is not null) label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    /// <summary>
    /// Puts a control in its box.
    ///
    /// The minimum size is set as well as the size, and that ordering is the same one
    /// the result panel learned on a screen: a Control is clamped up to its minimum,
    /// and a label's minimum width is its whole unwrapped line, so a caption given a
    /// width alone widens itself straight back off the strip.
    /// </summary>
    private static void Place(Control control, float x, float y, float width, float height)
    {
        control.Position = new Vector2(x, y);
        control.CustomMinimumSize = new Vector2(width, 0);
        control.Size = new Vector2(width, height);
    }

    private static ColorRect Box(Color color, float x, float y, float width, float height)
    {
        var box = new ColorRect
        {
            Color = color,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        Place(box, x, y, width, height);
        return box;
    }

    private static Color Rgb(int red, int green, int blue, float alpha = 1f) =>
        new(red / 255f, green / 255f, blue / 255f, alpha);
}

using Godot;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The playback transport, drawn: a tag hanging from the game's own top bar, built
/// once and kept for the whole watched journey.
///
/// Built once is the point of it. The popup this began as was created and torn down
/// around every decision, so it could not carry a position across the map-to-combat
/// transition and it covered the screens a player is there to look at. This tag is a
/// child of the run's own persistent interface, so the room underneath can change
/// without it noticing; <see cref="Apply"/> is how it changes what it says.
///
/// What it looks like is the accepted design in docs/mod-ui-direction.md and the
/// design report behind it, and its one idea is a seam made of material rather than
/// of loudness: the same palette as the game - cream ink, gold rim - on a flat
/// charcoal plate where everything the game draws is textured stone or parchment.
/// A player sees the bar, sees a thing hanging under it, and reads "the game, then
/// the mod" without a caption saying so. It hangs beneath the game's own meta
/// cluster - map, deck, settings - which is the right neighbourhood for controls that
/// act on the recording rather than on the run, and it covers no relic, no intent, no
/// row and no card.
///
/// Three facts about the retail client decide its behaviour and each is written into
/// the code rather than assumed. Its root and everything on it except the buttons
/// ignore the mouse, so the map, the event and the fight underneath keep every click
/// that is not on a control. Its buttons take focus, so a controller can reach them.
/// And it is anchored to measured furniture rather than positioned at a constant, so
/// it stays out of the way as the relic row grows.
///
/// It computes nothing: every word and every state comes from
/// <see cref="PlaybackTransport"/>. Built from stock Godot nodes for the same reason
/// the result panel is - this assembly has no Godot source generators, so a
/// <c>Control</c> subclass of ours would never have its overrides called, and stock
/// nodes are also what let the whole tag be assembled and asserted on with no game.
/// </summary>
internal sealed class PlaybackTransportStrip
{
    internal const string RootName = "CombatTrainerTransport";

    // ── The palette ────────────────────────────────────────────────────────
    //
    // The game's colours on a material the game does not use. Every value is the
    // accepted design's; changing one is a design decision, not a refactor.

    private static readonly Color PlateFace = Rgb(0x1c, 0x1a, 0x20, 0.94f);
    private static readonly Color PlateEdge = Rgb(0xc9, 0xa8, 0x5c);
    private static readonly Color PlateInner = Rgb(0x3a, 0x33, 0x30);
    private static readonly Color Cream = Rgb(0xf1, 0xe4, 0xc0);
    private static readonly Color Muted = Rgb(0xa8, 0x9f, 0x8c);
    private static readonly Color Dim = Rgb(0x6a, 0x62, 0x59);
    private static readonly Color Gold = Rgb(0xd9, 0xb2, 0x5f);
    private static readonly Color Teal = Rgb(0x7f, 0xe3, 0xea);
    private static readonly Color Red = Rgb(0xc8, 0x46, 0x3a);
    private static readonly Color ButtonFace = Rgb(0x2a, 0x26, 0x2c);
    private static readonly Color ButtonEdge = Rgb(0x4a, 0x43, 0x40);
    private static readonly Color DisabledFace = Rgb(0x22, 0x1f, 0x24);
    private static readonly Color DisabledEdge = Rgb(0x2e, 0x2a, 0x2c);
    private static readonly Color DisabledGlyph = Rgb(0x57, 0x51, 0x4b);
    private static readonly Color HoldTrack = Rgb(0x3b, 0x3a, 0x3e);
    private static readonly Color TipFace = Rgb(0x14, 0x12, 0x0f, 0.96f);
    private static readonly Color TipEdge = Rgb(0x6b, 0x5a, 0x32);
    private static readonly Color TipBody = Rgb(0xcf, 0xc6, 0xb0);

    // ── The layout, in the design's own reference units ─────────────────────
    //
    // The design was measured on captures at the game's 1512 by 916 logical
    // reference, and the client reports its viewport in engine units that vary with
    // the window. Everything below is therefore in reference units and scaled once,
    // rather than being a set of constants that are right on one monitor.

    private const float ReferenceHeight = 916f;

    private const float TagWidth = 378f;
    private const float TagHeight = 56f;
    private const float ChipWidth = 150f;
    private const float ChipHeight = 44f;

    /// <summary>The chamfer on the tag's bottom corners, which is most of what makes
    /// it read as hanging rather than as a panel.</summary>
    private const float Chamfer = 12f;

    private const float MarkSize = 22f;
    private const float ButtonSize = 30f;
    private const float ButtonGap = 6f;
    private const float SpeedWidth = 26f;
    private const float PipPitch = 11f;
    private const float IdentityWidth = 128f;

    private const int CreatorFontSize = 15;
    private const int TitleFontSize = 11;
    private const int CounterFontSize = 12;
    private const int SpeedFontSize = 12;
    private const int MenuFontSize = 14;
    private const int TipTitleFontSize = 13;
    private const int TipBodyFontSize = 12;
    private const int NoteFontSize = 12;

    /// <summary>How tall a line of text is as a multiple of its font size. Used only
    /// where there is no font to measure with, which is every test here.</summary>
    private const float LineHeight = 1.35f;

    private readonly Control _root;
    private readonly Polygon2D _plateFill;
    private readonly Line2D _plateEdge;
    private readonly Line2D _plateInner;
    private readonly Polygon2D _pinLeft;
    private readonly Polygon2D _pinRight;
    private readonly Control _mark;
    private readonly Label _creator;
    private readonly Label _title;
    private readonly Label _numerals;
    private readonly Control _pips;
    private readonly Button _speed;
    private readonly Label _speedLabel;
    private readonly Button _back;
    private readonly Button _play;
    private readonly Button _step;
    private readonly Line2D _holdTrack;
    private readonly Line2D _holdFill;
    private readonly Control _note;
    private readonly Control _notePlate;
    private readonly Label _noteText;
    private readonly Control _ledger;
    private readonly Control _menu;
    private readonly Control _tip;
    private readonly Control _tipPlate;
    private readonly Label _tipTitle;
    private readonly Label _tipBody;
    private readonly Font? _font;

    private Vector2 _viewport;
    private Vector2 _anchor;
    private float _unit;
    private PlaybackTransport _state;

    /// <summary>
    /// What the tag is, element by element - and the only thing this class reads to
    /// decide anything.
    ///
    /// A reference-typed field on purpose, like every other cross-assembly field here:
    /// the game enumerates this assembly's types before the mod initializer runs, and
    /// a field whose layout needs a sibling assembly's value type resolved takes the
    /// whole mod down one phase before it knows where its siblings are. A pointer is
    /// always a pointer. See docs/in-game-host.md.
    /// </summary>
    private TransportSurface _surface;

    /// <summary>Whether a hold is running. The surface says whether this mode draws
    /// one at all; this says whether there is one to draw.</summary>
    private bool _holding;

    /// <summary>
    /// The control a visible tooltip belongs to, so it can be put back where it
    /// belongs when what hangs under the tag changes.
    ///
    /// Pressing a control focuses it, and focus raises the tooltip - which happens
    /// before the press has changed anything, so the sentence is placed against
    /// whatever was hanging a moment ago. Look back is the case that shows it: the
    /// tooltip went up, then the ledger appeared underneath it, and the sentence sat
    /// on top of the rows it had been placed above.
    /// </summary>
    private (Control Anchor, Func<ElementSurface> Element)? _tipSource;

    private PlaybackTransportStrip(Nodes nodes, Vector2 viewport, Vector2 anchor, Font? font)
    {
        _root = nodes.Root;
        _plateFill = nodes.PlateFill;
        _plateEdge = nodes.PlateEdge;
        _plateInner = nodes.PlateInner;
        _pinLeft = nodes.PinLeft;
        _pinRight = nodes.PinRight;
        _mark = nodes.Mark;
        _creator = nodes.Creator;
        _title = nodes.Title;
        _numerals = nodes.Numerals;
        _pips = nodes.Pips;
        _speed = nodes.Speed;
        _speedLabel = nodes.SpeedLabel;
        _back = nodes.Back;
        _play = nodes.Play;
        _step = nodes.Step;
        _holdTrack = nodes.HoldTrack;
        _holdFill = nodes.HoldFill;
        _note = nodes.Note;
        _notePlate = nodes.NotePlate;
        _noteText = nodes.NoteText;
        _ledger = nodes.Ledger;
        _menu = nodes.Menu;
        _tip = nodes.Tip;
        _tipPlate = nodes.TipPlate;
        _tipTitle = nodes.TipTitle;
        _tipBody = nodes.TipBody;
        _font = font;
        _viewport = viewport;
        _anchor = anchor;
        _unit = viewport.Y / ReferenceHeight;
        _state = nodes.State;
        _surface = nodes.State.Surface;
    }

    internal Control Root => _root;

    internal Button Back => _back;

    internal Button Play => _play;

    internal Button Step => _step;

    /// <summary>The speed control, and the chip's press target during the player's
    /// fight: one button, because the tag and the chip are one node.</summary>
    internal Button Speed => _speed;

    internal Control Tooltip => _tip;

    internal Control Ledger => _ledger;

    internal Control Menu => _menu;

    /// <summary>What the tag is currently saying, for a host that has to ask.</summary>
    internal PlaybackTransport State => _state;

    /// <summary>What the tag currently is. The host asks this rather than re-deriving
    /// the mode when it needs to know what a press means.</summary>
    internal TransportSurface Surface => _surface;

    /// <summary>
    /// Assembles the tag.
    /// </summary>
    /// <param name="state">What it says to begin with.</param>
    /// <param name="viewport">The surface it hangs on.</param>
    /// <param name="anchor">The top-right corner it hangs from, in engine units:
    /// the bottom of the top bar's own widgets, and the right edge of the game's meta
    /// cluster. Passed in rather than measured here, because that furniture is the
    /// game's and this class draws in a process that may have none.</param>
    /// <param name="font">The font the game's own labels use, or null to leave the
    /// theme's default in place.</param>
    internal static PlaybackTransportStrip Build(
        PlaybackTransport state, Vector2 viewport, Vector2 anchor, Font? font,
        Action back, Action play, Action step, Action speed, Action identity)
    {
        var root = new Control
        {
            Name = RootName,
            Position = Vector2.Zero,
            Size = viewport,
            // The screen underneath is the thing the player is watching. Everything
            // that is not a control lets its clicks through, which is what keeps the
            // map, the event and the fight working while the tag is up.
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        var unit = viewport.Y / ReferenceHeight;
        var nodes = new Nodes
        {
            Root = root,
            State = state,
            PlateFill = Add(root, new Polygon2D { Name = "Plate", Color = PlateFace }),
            PlateEdge = Add(root, Stroke("PlateEdge", PlateEdge, 1.6f * unit)),
            PlateInner = Add(root, Stroke("PlateInner", PlateInner, 1f * unit)),
            PinLeft = Add(root, new Polygon2D { Name = "PinLeft", Color = Gold }),
            PinRight = Add(root, new Polygon2D { Name = "PinRight", Color = Gold }),
            Mark = Add(root, new Control { Name = "Mark", MouseFilter = Control.MouseFilterEnum.Ignore }),
            Creator = Add(root, Text("Creator", CreatorFontSize, Cream, font)),
            Title = Add(root, Text("VideoTitle", TitleFontSize, Muted, font)),
            Numerals = Add(root, Text("Counter", CounterFontSize, Muted, font)),
            Pips = Add(root, new Control { Name = "Pips", MouseFilter = Control.MouseFilterEnum.Ignore }),
            HoldTrack = Add(root, Stroke("HoldTrack", HoldTrack, 2.4f * unit)),
            HoldFill = Add(root, Stroke("Hold", Teal, 2.4f * unit)),
            Note = Add(root, Plated("Note")),
            Ledger = Add(root, new Control { Name = "Ledger", MouseFilter = Control.MouseFilterEnum.Ignore }),
            Menu = Add(root, new Control { Name = "Menu", MouseFilter = Control.MouseFilterEnum.Ignore }),
        };

        nodes.NotePlate = Add(nodes.Note, new Control
        {
            Name = "NotePlate",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        nodes.NoteText = Add(nodes.Note, Wrapping(Text("NoteText", NoteFontSize, Muted, font)));

        nodes.Speed = Add(root, Pressable("Speed", font, speed));
        nodes.SpeedLabel = Add(nodes.Speed, Text("SpeedLabel", SpeedFontSize, Muted, font));
        nodes.Back = Add(root, Pressable("Back", font, back));
        nodes.Play = Add(root, Pressable("Play", font, play));
        nodes.Step = Add(root, Pressable("Step", font, step));

        nodes.Tip = Add(root, Plated("Tooltip"));

        // The plate is its own child, added before the words, so redrawing it never
        // has to reorder the tooltip's children to keep the text on top.
        nodes.TipPlate = Add(nodes.Tip, new Control
        {
            Name = "TooltipPlate",
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        nodes.TipTitle = Add(nodes.Tip, Text("TooltipTitle", TipTitleFontSize, Cream, font));
        nodes.TipBody = Add(nodes.Tip, Wrapping(Text("TooltipBody", TipBodyFontSize, TipBody, font)));

        var strip = new PlaybackTransportStrip(nodes, viewport, anchor, font);

        // The identity block is a control too, because pressing it opens the video at
        // the moment being shown. Its hit area is the two lines of text, so it is a
        // transparent button laid over them rather than a styled one.
        strip._creator.MouseFilter = Control.MouseFilterEnum.Ignore;
        strip._title.MouseFilter = Control.MouseFilterEnum.Ignore;
        var identityButton = Add(root, new Button
        {
            Name = "Identity",
            Flat = true,
            FocusMode = Control.FocusModeEnum.All,
        });
        identityButton.Pressed += () => identity();
        strip._identity = identityButton;
        // Every control's words come off its own element, the speed control included -
        // which is what makes the chip silent without a special case: its press target
        // is an element whose tooltip is empty, rather than a mode this class checks.
        strip.Wire(identityButton, () => strip._surface.Identity);
        strip.Wire(strip._back, () => strip._surface.Back);
        strip.Wire(strip._play, () => strip._surface.Play);
        strip.Wire(strip._step, () => strip._surface.Step);
        strip.Wire(strip._speed, () => strip._surface.Speed);

        strip.Apply(state);
        return strip;
    }

    private Button _identity = null!;

    internal Button Identity => _identity;

    /// <summary>
    /// Changes what the tag says, without rebuilding it.
    ///
    /// The whole reason this class holds its nodes. A tag rebuilt between decisions
    /// would be a popup with a different shape: it would lose focus, lose its place in
    /// the tree, and be gone across exactly the transition this design exists to
    /// survive.
    /// </summary>
    internal void Apply(PlaybackTransport state)
    {
        _state = state;
        var surface = state.Surface;

        // A menu belongs to the surface that offered it. Left hanging when the surface
        // starts offering a different one it would sit under a chip that is meant to
        // say nothing until it is pressed, and swallow that first press closing itself.
        // Asked of the surface rather than of the mode, so a mode that keeps the same
        // menu keeps it open and one that changes it never can.
        if (_openMenu != None && _openMenu != Code(surface.Menu))
        {
            _openMenu = None;
            _onChoose = null;
        }

        _surface = surface;
        var width = (surface.ChipPlate ? ChipWidth : TagWidth) * _unit;
        var height = (surface.ChipPlate ? ChipHeight : TagHeight) * _unit;
        var left = _anchor.X - width;
        var top = _anchor.Y;

        Plate(left, top, width, height);

        var mark = MarkSize * _unit;
        Show(_mark, surface.Mark);
        SetGlyph(_mark, state.Mark, mark, mark, surface.Mark.Glyph == TransportGlyph.Warn ? Red : Gold);
        Place(_mark, left + (12 * _unit), top + ((height - mark) / 2), mark, mark);

        // The creator is on every surface there is - it is the whole of what a chip
        // says - so it is the one label with nothing to decide.
        _creator.Text = state.Identity.Creator;
        _title.Text = state.Identity.VideoTitle ?? string.Empty;
        Show(_title, surface.Title);

        // Two lines when there is a video title, one centred line when there is not.
        // The fallback is the design's: a recording whose manifest has no title says
        // the creator alone rather than showing an empty second line.
        var identityLeft = left + (42 * _unit);
        if (_title.Visible)
        {
            Place(_creator, identityLeft, top + (10 * _unit), IdentityWidth * _unit, 20 * _unit);
            Place(_title, identityLeft, top + (30 * _unit), IdentityWidth * _unit, 16 * _unit);
        }
        else
        {
            Place(_creator, identityLeft, top + ((height - (20 * _unit)) / 2), IdentityWidth * _unit, 20 * _unit);
        }

        Place(
            _identity, identityLeft, top + (6 * _unit), IdentityWidth * _unit, height - (12 * _unit));
        Project(_identity, surface.Identity);

        // Everything that hangs under the tag hangs off this rather than off the tag,
        // so two of them are never stacked on the same band. Reset before the things
        // that move it, and read by the things that hang below them.
        _hangingBottom = top + height;

        ApplyControls(state, surface, left, top, width, height);
        ApplyNote(state, surface, left, top, height, width);
        ApplyLedger(state, surface, left, top, height, width);
        ApplyMenu(state, left, width);

        // Last, once everything that hangs has been laid out and the measure is final.
        // A tooltip that is already up was placed against the measure of a moment ago,
        // and this is the only point at which the right answer is known.
        if (_tip.Visible && _tipSource is { } tip)
        {
            ShowTooltip(tip.Anchor, tip.Element);
        }
    }

    /// <summary>
    /// One element onto one button: what it is, whether it can be pressed, and what it
    /// looks like - in that order and separately.
    ///
    /// The whole of the refactor is here. A Godot control that is not visible receives
    /// no input, so while one boolean decided both, "present but silent" could not be
    /// said and the chip had no press target at all. Presence decides visibility,
    /// pressability decides input, and the face is a third answer that neither of them
    /// implies.
    /// </summary>
    private void Project(Button button, ElementSurface element)
    {
        button.Visible = element.Presence != Presence.Absent;
        button.Disabled = !element.Pressable;

        if (element.Presence == Presence.Silent) Bare(button);
        else if (element.Presence == Presence.Drawn) Face(button, element.Pressable);
    }

    /// <summary>The same for something that is only ever looked at.</summary>
    private static void Show(Control control, ElementSurface element) =>
        control.Visible = element.Presence != Presence.Absent;

    /// <summary>
    /// The bottom of the lowest thing hanging under the tag.
    ///
    /// The plates are translucent - the game is meant to show through them - so two
    /// surfaces at the same height are not one covering the other, they are both
    /// legible at once and neither readable. The retail client drew the speed menu
    /// straight over the look-back ledger and the ledger's words came through it.
    /// </summary>
    private float _hangingBottom;

    private void ApplyControls(
        PlaybackTransport state, TransportSurface surface, float left, float top, float width, float height)
    {
        Show(_numerals, surface.Counter);
        Project(_speed, surface.Speed);
        Project(_back, surface.Back);
        Project(_play, surface.Play);
        Project(_step, surface.Step);

        // The label belongs to the speed control's face rather than to the node, so a
        // press target that is present and silent carries no words. That is what lets
        // the chip be pressed and still say nothing.
        _speedLabel.Visible = surface.Speed.Presence == Presence.Drawn;
        _speedLabel.Text = state.SpeedLabel;

        // Drawn while a hold is actually running, rather than cleared on every pass:
        // the surface is re-derived on every fact that changes, and a hold that went
        // out each time would be the stall the drained line exists to rule out.
        _holdTrack.Visible = _holdFill.Visible = surface.HoldLine && _holding;

        ApplyPips(state.Counter, surface, left + (182 * _unit), top + (36 * _unit));

        // The chip is the one place geometry differs, and it differs completely: one
        // press target over the whole plate, so a mouse hits it anywhere on the chip
        // and a controller reaches it by focus.
        if (surface.ChipPlate)
        {
            _tip.Visible = false;
            Place(_speed, left, top, width, height);
            return;
        }

        _numerals.Text = state.Counter.Count == 0 ? string.Empty : state.Counter.Numerals;
        _numerals.AddThemeColorOverride(FontColour, state.Counter.LookingAt is null ? Muted : Cream);
        Place(_numerals, left + (178 * _unit), top + (12 * _unit), 48 * _unit, 18 * _unit);

        Place(_speed, left + (232 * _unit), top + (13 * _unit), SpeedWidth * _unit, ButtonSize * _unit);
        Place(_speedLabel, 0, 0, SpeedWidth * _unit, ButtonSize * _unit);
        _speedLabel.HorizontalAlignment = HorizontalAlignment.Center;

        var buttonsLeft = left + (266 * _unit);
        var buttonTop = top + (13 * _unit);
        var pitch = (ButtonSize + ButtonGap) * _unit;
        PlaceButton(_back, surface.Back, buttonsLeft, buttonTop);
        PlaceButton(_play, surface.Play, buttonsLeft + pitch, buttonTop);
        PlaceButton(_step, surface.Step, buttonsLeft + (2 * pitch), buttonTop);
    }

    private void PlaceButton(Button button, ElementSurface element, float x, float y)
    {
        if (element.Presence == Presence.Absent) return;

        if (element.Glyph is { } glyph)
        {
            SetGlyph(
                button,
                glyph,
                ButtonSize * _unit,
                (ButtonSize - 10) * _unit,
                element.Pressable ? Cream : DisabledGlyph);
        }

        Place(button, x, y, ButtonSize * _unit, ButtonSize * _unit);
    }

    /// <summary>
    /// The step pips.
    ///
    /// Drawn only while there are few enough to be read at a glance; the numerals are
    /// always there, so a whole run loses the picture and keeps the fact. Done is a
    /// filled grey dot, the current one is teal, the ones ahead are hollow, and the
    /// one being looked at is ringed.
    /// </summary>
    private void ApplyPips(TransportCounter counter, TransportSurface surface, float x, float y)
    {
        Clear(_pips);
        _pips.Visible = surface.Counter.Presence != Presence.Absent && counter.ShowPips && counter.Count > 0;
        if (!_pips.Visible) return;

        Place(_pips, 0, 0, _viewport.X, _viewport.Y);
        for (var step = 1; step <= counter.Count; step++)
        {
            var centre = new Vector2(x + ((step - 1) * PipPitch * _unit), y);
            var (radius, colour, filled) = step == counter.Current
                ? (2.9f, Teal, true)
                : step < counter.Current ? (2.9f, Muted, true) : (2.6f, Dim, false);

            _pips.AddChild(filled
                ? new Polygon2D
                {
                    Name = $"Pip{step}",
                    Polygon = Ring(centre, radius * _unit),
                    Color = colour,
                }
                : Stroke($"Pip{step}", colour, 1.2f * _unit, Ring(centre, radius * _unit), closed: true));

            if (counter.LookingAt == step)
            {
                _pips.AddChild(
                    Stroke($"Pip{step}.Looking", Cream, 1.3f * _unit, Ring(centre, 5.4f * _unit), closed: true));
            }
        }
    }

    /// <summary>
    /// The once-per-run sentence, hung under the tag in the ledger's shape.
    ///
    /// A rule about how to read these screens rather than a caption, which is why it
    /// is said once, before the first decision anybody watches, and never again.
    /// </summary>
    private void ApplyNote(
        PlaybackTransport state, TransportSurface surface, float left, float top, float height, float width)
    {
        _note.Visible = surface.Note;
        if (!_note.Visible) return;

        _noteText.Text = state.Note;

        // The sentence is longer than the tag is wide, so it wraps, and the panel is
        // sized to the wrapped text rather than to one line. Sizing it to a line is
        // what cut the sentence off after "what was cho" in the client.
        var inset = 12 * _unit;
        var textWidth = width - (2 * inset);
        var noteHeight = WrappedHeight(state.Note, NoteFontSize, textWidth, fallbackLines: 2) + (16 * _unit);

        var noteTop = top + height + (6 * _unit);
        _hangingBottom = noteTop + noteHeight;

        Place(_note, left, noteTop, width, noteHeight);
        Clear(_notePlate);
        Place(_notePlate, 0, 0, width, noteHeight);
        PlatePolygon(_notePlate, width, noteHeight);
        Place(_noteText, inset, 0, textWidth, noteHeight);
    }

    /// <summary>
    /// The ledger of decisions already made, hung under the tag while looking back.
    ///
    /// It exists because a decision made two screens ago happened somewhere that is
    /// gone: the run cannot be asked about it again and must never be rewound to
    /// answer, so what was read at the time is listed instead. The artwork is the
    /// game's own, asked for by model id.
    /// </summary>
    private void ApplyLedger(
        PlaybackTransport state, TransportSurface surface, float left, float top, float height, float width)
    {
        Clear(_ledger);
        _ledger.Visible = surface.Ledger && state.Ledger.Count > 0;
        if (!_ledger.Visible) return;

        var rowHeight = 32 * _unit;
        var ledgerHeight = (10 * _unit) + (rowHeight * state.Ledger.Count);
        var ledgerTop = top + height + (6 * _unit);
        _hangingBottom = ledgerTop + ledgerHeight;

        Place(_ledger, left, ledgerTop, width, ledgerHeight);
        PlatePolygon(_ledger, width, ledgerHeight);

        for (var index = 0; index < state.Ledger.Count; index++)
        {
            var row = state.Ledger[index];
            var rowTop = (6 * _unit) + (index * rowHeight);
            var colour = row.IsLookedAt ? Cream : Muted;

            if (row.IsLookedAt)
            {
                var glyph = TransportGlyphArt.Of(TransportGlyph.Back, $"Ledger{row.Number}.Marker", 18 * _unit, Cream);
                Place(glyph, 10 * _unit, rowTop + (7 * _unit));
                _ledger.AddChild(glyph);
            }

            var art = _art?.Invoke(row.ArtModelId);
            if (art is not null)
            {
                var picture = new TextureRect
                {
                    Name = $"Ledger{row.Number}.Art",
                    Texture = art,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                Place(picture, 32 * _unit, rowTop + (4 * _unit), 24 * _unit, 24 * _unit);
                _ledger.AddChild(picture);
            }

            var label = Text($"Ledger{row.Number}", MenuFontSize, colour, _font);
            label.Text = row.Label;
            Place(label, 62 * _unit, rowTop, width - (86 * _unit), rowHeight);
            _ledger.AddChild(label);

            if (row.IsCurrent)
            {
                _ledger.AddChild(new Polygon2D
                {
                    Name = $"Ledger{row.Number}.Current",
                    Polygon = Ring(new Vector2(width - (16 * _unit), rowTop + (rowHeight / 2)), 3 * _unit),
                    Color = Teal,
                });
            }
        }
    }

    /// <summary>The speed menu, or the chip's two directions, hung in the same shape
    /// as the ledger so they read as one family.</summary>
    private void ApplyMenu(PlaybackTransport state, float left, float width)
    {
        Clear(_menu);
        var rows = OpenRows;
        _menu.Visible = rows.Count > 0;
        if (!_menu.Visible) return;

        var rowHeight = 32 * _unit;
        var chip = _openMenu == Code(MenuKind.Chip);
        var menuWidth = (chip ? 260 : 96) * _unit;
        var menuHeight = (10 * _unit) + (rowHeight * rows.Count);
        var menuLeft = chip ? left + width - menuWidth : left + (192 * _unit);
        var menuTop = _hangingBottom + (6 * _unit);
        Place(_menu, menuLeft, menuTop, menuWidth, menuHeight);
        PlatePolygon(_menu, menuWidth, menuHeight);

        // The menu hangs under the tag like the note and the ledger do, so it moves
        // the measure whatever hangs next sits below. It did not, and the client drew
        // the speed control's own tooltip straight over the menu that control had just
        // opened - the same both-legible-neither-readable failure the one-measure rule
        // exists to prevent.
        _hangingBottom = menuTop + menuHeight;

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];

            // Copied per row, and that is the whole of it. A `for` loop has one
            // variable, so a closure written over `index` directly reads whatever it
            // holds when the closure runs - which is one past the last row, always.
            // Every menu row on this surface therefore asked for a row that does not
            // exist, and the menu closed having done nothing. Measured in the retail
            // client: neither the speed rows nor the chip's two directions had ever
            // worked, because the chip could not be pressed and a chosen speed looks
            // much like a speed nobody chose.
            var chosen = index;
            var rowTop = (6 * _unit) + (index * rowHeight);
            var colour = !row.Enabled ? DisabledGlyph : row.IsCurrent ? Cream : Muted;

            if (row.Glyph is { } glyph)
            {
                var art = TransportGlyphArt.Of(glyph, $"MenuRow{index}.Glyph", 20 * _unit, colour);
                Place(art, 12 * _unit, rowTop + (6 * _unit));
                _menu.AddChild(art);
            }

            var button = Pressable($"MenuRow{index}", _font, () => Choose(chosen));
            button.Flat = true;
            button.Disabled = !row.Enabled;
            Place(button, 0, rowTop, menuWidth, rowHeight);
            _menu.AddChild(button);

            var label = Text($"MenuRow{index}.Label", MenuFontSize, colour, _font);
            label.Text = row.Label;
            Place(label, 40 * _unit, rowTop, menuWidth - (56 * _unit), rowHeight);
            _menu.AddChild(label);

            if (row.IsCurrent)
            {
                _menu.AddChild(new Polygon2D
                {
                    Name = $"MenuRow{index}.Current",
                    Polygon = Ring(new Vector2(menuWidth - (16 * _unit), rowTop + (rowHeight / 2)), 3 * _unit),
                    Color = Teal,
                });
            }
        }
    }

    /// <summary>
    /// Which menu is open, if any - as a number rather than the enum, and deliberately
    /// not the rows themselves.
    ///
    /// An <c>int</c> for the same reason <c>RecordedFightRun._speedIndex</c> is one: a
    /// <c>MenuKind?</c> field is a generic instantiation over a sibling assembly's
    /// value type, so computing this class's layout would resolve that sibling one
    /// phase before the runtime has been taught where the siblings are.
    ///
    /// The indirection is not style, and it cost a startup to learn. The game
    /// enumerates this assembly's types before it calls the mod initializer, and a
    /// field whose type is a generic instantiation over a sibling assembly's type -
    /// <c>IReadOnlyList&lt;MenuRow&gt;</c> was the one - makes the runtime resolve
    /// that sibling to build the instantiation, one phase before
    /// <see cref="SiblingAssemblies"/> has taught it where the siblings are. The whole
    /// mod then fails to load. A plain reference-typed field is fine, because its
    /// layout is a pointer; the rows are read off the state instead.
    /// See docs/in-game-host.md.
    /// </summary>
    private int _openMenu;

    /// <summary>No menu open. <see cref="_openMenu"/> holds one more than the kind, so
    /// zero is the empty answer and no separate flag is needed.</summary>
    private const int None = 0;

    private static int Code(MenuKind kind) => (int)kind + 1;

    private Action<int>? _onChoose;

    private Func<string, Texture2D?>? _art;

    /// <summary>The rows the open menu is showing, read from the state rather than
    /// held.</summary>
    private IReadOnlyList<MenuRow> OpenRows =>
        _openMenu == Code(MenuKind.Chip) ? _state.ChipMenu
        : _openMenu == Code(MenuKind.Speed) ? _state.SpeedMenu
        : [];

    /// <summary>How the ledger gets the game's own artwork for a model id. Injected so
    /// the tag assembles in a process with no model database.</summary>
    internal void DrawArtWith(Func<string, Texture2D?> art) => _art = art;

    /// <summary>
    /// Opens whichever menu this surface offers, which is not the caller's to choose.
    ///
    /// A caller that named the menu would be re-deriving the mode to name it, and that
    /// was one of the four places the mode was decided over again. A surface offering
    /// none opens none.
    /// </summary>
    internal void OpenMenu(Action<int>? chosen)
    {
        if (_surface.Menu == MenuKind.None) return;

        // The tooltip goes with the press that opened the menu. It was raised by the
        // focus that press gave the control, which happens before the menu exists, so
        // it was placed against a measure the menu had not moved yet and was drawn
        // over the rows it had just produced. It is also redundant there: the menu is
        // a better answer to "what does this do" than the sentence about it, and a
        // controller that focuses the control again gets the sentence back, below the
        // menu, because the measure is right by then.
        HideTooltip();

        _openMenu = Code(_surface.Menu);
        _onChoose = chosen;
        Apply(_state);
    }

    internal void CloseMenu()
    {
        _openMenu = None;
        _onChoose = null;
        Apply(_state);
    }

    internal bool MenuIsOpen => _openMenu != None && OpenRows.Count > 0;

    private void Choose(int index)
    {
        var chosen = _onChoose;
        var rows = OpenRows;
        var enabled = index >= 0 && index < rows.Count && rows[index].Enabled;
        CloseMenu();
        if (enabled) chosen?.Invoke(index);
    }

    /// <summary>
    /// How far through the hold Play is, drawn as a line draining along the tag's
    /// foot.
    ///
    /// The captain's rule that reveal, hold and commit should be visible rather than
    /// implied: without this the tag simply pauses, and a watcher cannot tell a hold
    /// from a stall. Nothing else on the tag animates.
    /// </summary>
    internal void ShowHold(double fraction)
    {
        if (!BeginLine(out var from, out var to, out var y)) return;

        _holdFill.Points =
            [new Vector2(from, y), new Vector2(from + ((to - from) * (float)Math.Clamp(fraction, 0, 1)), y)];
    }

    /// <summary>
    /// The same line, travelling, while the game is between screens.
    ///
    /// The two windows where the run cannot be moved - the game putting the next
    /// screen up after a decision, and the fight opening after the last one - refuse
    /// every control that would move it, and by the captain's ruling say nothing about
    /// why. A row of controls that all go dead with nothing else changing reads as
    /// broken rather than as busy, so the state is shown instead of explained, in the
    /// vocabulary the tag already has: this is the same line making the same claim it
    /// always makes, that the transport is waiting on the game.
    ///
    /// It carries no fraction, and that is the honest part. Neither window has a known
    /// length - one is a screen transition, the other is however long the fight takes
    /// to open - and a line draining toward a deadline would be claiming one.
    /// </summary>
    /// <param name="phase">Where in one pass the travelling segment is, from zero to
    /// one, wrapping.</param>
    internal void ShowMoving(double phase)
    {
        if (!BeginLine(out var from, out var to, out var y)) return;

        var segment = (to - from) * SweepWidth;
        var head = from - segment + ((float)Math.Clamp(phase, 0, 1) * ((to - from) + segment));
        _holdFill.Points =
        [
            new Vector2(Math.Max(from, head), y),
            new Vector2(Math.Min(to, head + segment), y),
        ];
    }

    /// <summary>How much of the tag's foot the travelling segment covers.</summary>
    private const float SweepWidth = 0.26f;

    /// <summary>Puts the track up and answers where it runs, or says this surface has
    /// no line to draw on.</summary>
    private bool BeginLine(out float from, out float to, out float y)
    {
        _holding = true;
        var draw = _surface.HoldLine;
        _holdTrack.Visible = draw;
        _holdFill.Visible = draw;

        var width = TagWidth * _unit;
        var left = _anchor.X - width;
        y = _anchor.Y + (TagHeight * _unit) - (3 * _unit);
        from = left + (12 * _unit);
        to = left + width - (12 * _unit);
        if (!draw) return false;

        _holdTrack.Points = [new Vector2(from, y), new Vector2(to, y)];
        return true;
    }

    internal void HideHold()
    {
        _holding = false;
        _holdTrack.Visible = false;
        _holdFill.Visible = false;
    }

    /// <summary>Redraws the tag against a viewport and an anchor measured again. The
    /// anchor is measured once, in <c>PlaybackTransportDock.Attach</c>, and nothing in
    /// the mod remeasures it, so a relic row that grows past the measured band or a
    /// window resized mid-journey leaves the tag where it was.</summary>
    internal void Reanchor(Vector2 viewport, Vector2 anchor)
    {
        _viewport = viewport;
        _anchor = anchor;
        _unit = viewport.Y / ReferenceHeight;
        _root.Size = viewport;
        Apply(_state);
    }

    // ── The plate ──────────────────────────────────────────────────────────

    /// <summary>
    /// The tag itself: a flat charcoal plate with an inked gold edge, two pins and a
    /// chamfered foot.
    ///
    /// Flat is the decision. The game's own furniture is torn stone and parchment, so
    /// a panel in the game's material would read as the game's own and a panel in a
    /// louder palette would read as a debug overlay. Same colours, different
    /// material: that is the seam the player reads without being told.
    /// </summary>
    private void Plate(float left, float top, float width, float height)
    {
        var chamfer = Chamfer * _unit;
        var outline = Chamfered(new Vector2(left, top), width, height, chamfer);
        _plateFill.Polygon = outline;
        _plateEdge.Points = [.. outline, outline[0]];

        var inset = 3 * _unit;
        var inner = Chamfered(
            new Vector2(left + inset, top + inset), width - (2 * inset), height - (2 * inset), chamfer);
        _plateInner.Points = [.. inner, inner[0]];

        var pin = 2.4f * _unit;
        _pinLeft.Polygon = Ring(new Vector2(left + (11 * _unit), top + (8 * _unit)), pin);
        _pinRight.Polygon = Ring(new Vector2(left + width - (11 * _unit), top + (8 * _unit)), pin);
    }

    /// <summary>The plate's outline, with the bottom corners cut.</summary>
    private static Vector2[] Chamfered(Vector2 origin, float width, float height, float chamfer) =>
    [
        origin,
        origin + new Vector2(width, 0),
        origin + new Vector2(width, height - chamfer),
        origin + new Vector2(width - (chamfer * 0.85f), height),
        origin + new Vector2(chamfer * 0.85f, height),
        origin + new Vector2(0, height - chamfer),
    ];

    /// <summary>Gives a hung panel - the note, the ledger, a menu - the tag's own
    /// material, so they read as one family rather than as three surfaces.</summary>
    private void PlatePolygon(Control host, float width, float height)
    {
        var chamfer = 10 * _unit;
        var outline = Chamfered(Vector2.Zero, width, height, chamfer);
        host.AddChild(new Polygon2D { Name = "Plate", Polygon = outline, Color = PlateFace });
        host.AddChild(Stroke("PlateEdge", PlateEdge, 1.4f * _unit, [.. outline, outline[0]]));
    }

    private static Vector2[] Ring(Vector2 centre, float radius)
    {
        const int segments = 16;
        var points = new Vector2[segments];
        for (var i = 0; i < segments; i++)
        {
            var angle = Mathf.Tau * i / segments;
            points[i] = centre + new Vector2(radius * Mathf.Cos(angle), radius * Mathf.Sin(angle));
        }

        return points;
    }

    // ── Controls, tooltips and focus ───────────────────────────────────────

    private static readonly StringName FontColour = "font_color";

    /// <summary>
    /// Takes a control's face off, leaving the plate it sits on to be what is seen.
    ///
    /// The chip says nothing until it is pressed, so its press target cannot carry a
    /// button of its own; it keeps the hover and focus rim, which is the game's own
    /// language for "this is the thing you are about to press" and the only way a
    /// player is told the chip does anything at all.
    /// </summary>
    private void Bare(Button button)
    {
        var none = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = new Color(0, 0, 0, 0) };
        none.SetCornerRadiusAll(5);
        none.SetBorderWidthAll(0);
        foreach (var state in new[] { "normal", "pressed", "disabled" })
        {
            button.AddThemeStyleboxOverride(state, none);
        }

        var lit = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = Gold };
        lit.SetCornerRadiusAll(5);
        lit.SetBorderWidthAll(2);
        button.AddThemeStyleboxOverride("hover", lit);
        button.AddThemeStyleboxOverride("focus", lit);
    }

    private void Face(Button button, bool enabled)
    {
        var fill = enabled ? ButtonFace : DisabledFace;
        var edge = enabled ? ButtonEdge : DisabledEdge;
        var style = new StyleBoxFlat { BgColor = fill, BorderColor = edge };
        style.SetCornerRadiusAll(5);
        style.SetBorderWidthAll(1);
        foreach (var state in new[] { "normal", "pressed", "disabled" })
        {
            button.AddThemeStyleboxOverride(state, style);
        }

        // Hover and focus take the gold rim, which is the game's own language for
        // "this is the thing you are about to press".
        var lit = new StyleBoxFlat { BgColor = Rgb(0x3a, 0x33, 0x38), BorderColor = Gold };
        lit.SetCornerRadiusAll(5);
        lit.SetBorderWidthAll(2);
        button.AddThemeStyleboxOverride("hover", lit);
        button.AddThemeStyleboxOverride("focus", lit);
    }

    /// <summary>
    /// Gives a control its tooltip, and shows it the way the game shows its own.
    ///
    /// The words live here because the controls are icon only: the captain's ruling
    /// is that progressive disclosure is the game's own principle, so the glyph
    /// carries the meaning and the sentence is one hover away. A refused control still
    /// has one, and where a reason has been written it says why it is refused rather
    /// than repeating what it would do; the between-screens windows have none by
    /// design, and an element with nothing to say carries no tooltip at all.
    /// </summary>
    private void Wire(Button button, Func<ElementSurface> element)
    {
        button.MouseEntered += () => ShowTooltip(button, element);
        button.MouseExited += HideTooltip;
        button.FocusEntered += () => ShowTooltip(button, element);
        button.FocusExited += HideTooltip;
    }

    /// <summary>
    /// Puts a tooltip up, or re-places one that is already up.
    ///
    /// The element is asked for rather than handed over, because this is also the way
    /// a tooltip that survives a state change is re-placed: the sentence has to be the
    /// one the current surface says, not the one the labels are still holding.
    /// </summary>
    private void ShowTooltip(Control anchor, Func<ElementSurface> element)
    {
        var surface = element();
        var title = surface.TooltipTitle;
        var body = surface.TooltipBody;
        if (title.Length == 0 && body.Length == 0)
        {
            HideTooltip();
            return;
        }

        _tipTitle.Text = title;
        _tipBody.Text = body;
        _tip.Visible = true;
        _tipSource = (anchor, element);

        // Sized to the body once it has wrapped, not to the newlines in it: at this
        // width "Shows an earlier choice again. Nothing is undone." is two lines and
        // carries no newline of its own, and counting them lost the last word.
        var width = 250 * _unit;
        var inset = 12 * _unit;
        var bodyTop = 24 * _unit;
        var bodyHeight = WrappedHeight(body, TipBodyFontSize, width - (2 * inset), fallbackLines: 2);
        var height = bodyTop + bodyHeight + (8 * _unit);

        // Below the control and pulled back on screen, never over the tag itself:
        // a tooltip that covers the counter it is explaining is worse than none. And
        // below whatever else is already hanging there, for the same reason.
        var x = Math.Clamp(
            anchor.Position.X + (anchor.Size.X / 2) - (width / 2), 8 * _unit, _viewport.X - width - (8 * _unit));
        var y = Math.Max(anchor.Position.Y + anchor.Size.Y, _hangingBottom) + (10 * _unit);
        Place(_tip, x, y, width, height);
        Clear(_tipPlate);
        Place(_tipPlate, 0, 0, width, height);
        PlatePolygon(_tipPlate, width, height);
        Place(_tipTitle, inset, 6 * _unit, width - (2 * inset), 18 * _unit);
        Place(_tipBody, inset, bodyTop, width - (2 * inset), bodyHeight);
    }

    private void HideTooltip()
    {
        _tip.Visible = false;
        _tipSource = null;
    }

    // ── Node plumbing ──────────────────────────────────────────────────────

    /// <summary>
    /// Puts a glyph in the middle of its host.
    ///
    /// The box is passed in rather than read off the host, because the host is sized
    /// by the same pass that sets its glyph and a <c>Control</c> asked for its size
    /// before that pass has run answers with the size it had last frame - nothing at
    /// all, the first time. That is what drew every control's glyph half a glyph up
    /// and to the left of its plate on the first paint, and left the mark straddling
    /// the tag's own edge for the whole run, since the mark is never given a size.
    /// </summary>
    private void SetGlyph(Control host, TransportGlyph glyph, float box, float size, Color colour)
    {
        foreach (var child in host.GetChildren().ToList())
        {
            if (child.Name.ToString().StartsWith("Glyph", StringComparison.Ordinal))
            {
                host.RemoveChild(child);
                child.QueueFree();
            }
        }

        var art = TransportGlyphArt.Of(glyph, "Glyph", size, colour);
        Place(art, (box - size) / 2, (box - size) / 2);
        host.AddChild(art);
    }

    private static void Clear(Node host, IReadOnlyList<Node>? keep = null)
    {
        foreach (var child in host.GetChildren().ToList())
        {
            if (keep is not null && keep.Contains(child)) continue;
            host.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static T Add<T>(Node parent, T child) where T : Node
    {
        parent.AddChild(child);
        return child;
    }

    private static Button Pressable(string name, Font? font, Action pressed)
    {
        var button = new Button
        {
            Name = name,
            // Takes focus on purpose: a control a keyboard or a controller cannot
            // reach is a control half the players do not have.
            FocusMode = Control.FocusModeEnum.All,
        };

        if (font is not null) button.AddThemeFontOverride("font", font);
        button.Pressed += () => pressed();
        return button;
    }

    private static Label Text(string name, int size, Color colour, Font? font)
    {
        var label = new Label
        {
            Name = name,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
        };

        if (font is not null) label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride(FontColour, colour);
        return label;
    }

    /// <summary>Lets a label run onto as many lines as its box allows, and stops it
    /// clipping. A sentence the tag says is always sized by <see cref="WrappedHeight"/>,
    /// so the box is the right height and there is nothing left to clip.</summary>
    private static Label Wrapping(Label label)
    {
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        label.ClipText = false;
        return label;
    }

    private static Control Plated(string name) => new()
    {
        Name = name,
        MouseFilter = Control.MouseFilterEnum.Ignore,
        Visible = false,
    };

    private static Line2D Stroke(string name, Color colour, float width, Vector2[]? points = null, bool closed = false)
    {
        var line = new Line2D { Name = name, DefaultColor = colour, Width = width };
        if (points is not null) line.Points = closed ? [.. points, points[0]] : points;
        return line;
    }

    /// <summary>
    /// Puts a control in its box.
    ///
    /// The minimum size is set as well as the size, and that ordering is the one the
    /// result panel learned on a screen: a Control is clamped up to its minimum, and a
    /// label's minimum width is its whole unwrapped line, so a caption given a width
    /// alone widens itself straight back off the tag.
    /// </summary>
    private static void Place(Control control, float x, float y, float width, float height)
    {
        control.Position = new Vector2(x, y);
        control.CustomMinimumSize = new Vector2(width, 0);
        control.Size = new Vector2(width, height);
    }

    private static void Place(Control control, float x, float y) => control.Position = new Vector2(x, y);

    /// <summary>
    /// How tall a sentence is once it has been wrapped to the width it is given.
    ///
    /// Counting the newlines in the text is not enough. Every sentence this tag says
    /// is wrapped by width, so a panel sized from the text alone runs short and cuts
    /// the sentence off mid-word - which is what the once-only note and the look-back
    /// tooltip both did in the client. Measured with the font that will draw it;
    /// with no font, which is every test here and nothing in the client, the caller's
    /// own line count stands.
    /// </summary>
    private float WrappedHeight(string text, int fontSize, float width, int fallbackLines)
    {
        if (_font is null) return fallbackLines * LineHeight * fontSize;

        return _font.GetMultilineStringSize(
            text,
            HorizontalAlignment.Left,
            width,
            fontSize,
            maxLines: -1,
            brkFlags: TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound).Y;
    }

    private static Color Rgb(int red, int green, int blue, float alpha = 1f) =>
        new(red / 255f, green / 255f, blue / 255f, alpha);

    /// <summary>Every node the tag keeps, gathered so the constructor is a list of
    /// assignments rather than twenty parameters.</summary>
    private sealed class Nodes
    {
        internal required Control Root { get; init; }

        internal required PlaybackTransport State { get; init; }

        internal required Polygon2D PlateFill { get; init; }

        internal required Line2D PlateEdge { get; init; }

        internal required Line2D PlateInner { get; init; }

        internal required Polygon2D PinLeft { get; init; }

        internal required Polygon2D PinRight { get; init; }

        internal required Control Mark { get; init; }

        internal required Label Creator { get; init; }

        internal required Label Title { get; init; }

        internal required Label Numerals { get; init; }

        internal required Control Pips { get; init; }

        internal required Line2D HoldTrack { get; init; }

        internal required Line2D HoldFill { get; init; }

        internal required Control Note { get; init; }

        internal required Control Ledger { get; init; }

        internal required Control Menu { get; init; }

        internal Control NotePlate { get; set; } = null!;

        internal Label NoteText { get; set; } = null!;

        internal Button Speed { get; set; } = null!;

        internal Label SpeedLabel { get; set; } = null!;

        internal Button Back { get; set; } = null!;

        internal Button Play { get; set; } = null!;

        internal Button Step { get; set; } = null!;

        internal Control Tip { get; set; } = null!;

        internal Control TipPlate { get; set; } = null!;

        internal Label TipTitle { get; set; } = null!;

        internal Label TipBody { get; set; } = null!;
    }
}

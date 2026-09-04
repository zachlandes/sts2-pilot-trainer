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
        nodes.NoteText = Add(nodes.Note, Text("NoteText", NoteFontSize, Muted, font));

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
        nodes.TipBody = Add(nodes.Tip, Text("TooltipBody", TipBodyFontSize, TipBody, font));

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
        strip.Wire(identityButton, () => strip._state.Identity.TooltipTitle, () => strip._state.Identity.TooltipBody);

        strip.Wire(strip._back, () => strip._state.Back.TooltipTitle, () => strip.Body(strip._state.Back));
        strip.Wire(strip._play, () => strip._state.Play.TooltipTitle, () => strip.Body(strip._state.Play));
        strip.Wire(strip._step, () => strip._state.Step.TooltipTitle, () => strip.Body(strip._state.Step));
        strip.Wire(strip._speed, () => TrainerCopy.SpeedTooltipTitle, () => TrainerCopy.SpeedTooltipBody);

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
        var tag = state.HasControls || state.Mode == TransportMode.Refused;
        var width = (tag ? TagWidth : ChipWidth) * _unit;
        var height = (tag ? TagHeight : ChipHeight) * _unit;
        var left = _anchor.X - width;
        var top = _anchor.Y;

        Plate(left, top, width, height);

        SetGlyph(_mark, state.Mark, MarkSize * _unit, state.Mode == TransportMode.Refused ? Red : Gold);
        Place(_mark, left + (12 * _unit), top + ((height - (MarkSize * _unit)) / 2));

        _creator.Text = state.Identity.Creator;
        _title.Text = state.Identity.VideoTitle ?? string.Empty;
        _title.Visible = state.Identity.VideoTitle is not null;

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
        _identity.Visible = tag;
        _identity.Disabled = !state.Identity.IsLink;

        ApplyControls(state, left, top, width, height, tag);
        ApplyNote(state, left, top, height, width);
        ApplyLedger(state, left, top, height, width);
        ApplyMenu(state, left, top, height, width);
    }

    private void ApplyControls(
        PlaybackTransport state, float left, float top, float width, float height, bool tag)
    {
        foreach (var control in new Control[] { _numerals, _pips, _speed, _back, _play, _step })
        {
            control.Visible = tag;
        }

        _holdTrack.Visible = false;
        _holdFill.Visible = false;

        if (!tag)
        {
            _tip.Visible = false;
            return;
        }

        _numerals.Text = state.Counter.Count == 0 ? string.Empty : state.Counter.Numerals;
        _numerals.AddThemeColorOverride(FontColour, state.Counter.LookingAt is null ? Muted : Cream);
        Place(_numerals, left + (178 * _unit), top + (12 * _unit), 48 * _unit, 18 * _unit);

        ApplyPips(state.Counter, left + (182 * _unit), top + (36 * _unit));

        _speedLabel.Text = state.SpeedLabel;
        Face(_speed, enabled: true);
        Place(_speed, left + (232 * _unit), top + (13 * _unit), SpeedWidth * _unit, ButtonSize * _unit);
        Place(_speedLabel, 0, 0, SpeedWidth * _unit, ButtonSize * _unit);
        _speedLabel.HorizontalAlignment = HorizontalAlignment.Center;

        var buttonsLeft = left + (266 * _unit);
        var buttonTop = top + (13 * _unit);
        var pitch = (ButtonSize + ButtonGap) * _unit;
        ApplyButton(_back, state.Back, buttonsLeft, buttonTop);
        ApplyButton(_play, state.Play, buttonsLeft + pitch, buttonTop);
        ApplyButton(_step, state.Step, buttonsLeft + (2 * pitch), buttonTop);
    }

    private void ApplyButton(Button button, TransportControl control, float x, float y)
    {
        button.Disabled = !control.Enabled;
        Face(button, control.Enabled);
        SetGlyph(button, control.Glyph, (ButtonSize - 10) * _unit, control.Enabled ? Cream : DisabledGlyph);
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
    private void ApplyPips(TransportCounter counter, float x, float y)
    {
        Clear(_pips);
        _pips.Visible = counter.ShowPips && counter.Count > 0;
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
    private void ApplyNote(PlaybackTransport state, float left, float top, float height, float width)
    {
        _note.Visible = state.Note.Length > 0;
        if (!_note.Visible) return;

        _noteText.Text = state.Note;
        var noteHeight = 34 * _unit;
        Place(_note, left, top + height + (6 * _unit), width, noteHeight);
        Clear(_notePlate);
        Place(_notePlate, 0, 0, width, noteHeight);
        PlatePolygon(_notePlate, width, noteHeight);
        Place(_noteText, 12 * _unit, 0, width - (24 * _unit), noteHeight);
    }

    /// <summary>
    /// The ledger of decisions already made, hung under the tag while looking back.
    ///
    /// It exists because a decision made two screens ago happened somewhere that is
    /// gone: the run cannot be asked about it again and must never be rewound to
    /// answer, so what was read at the time is listed instead. The artwork is the
    /// game's own, asked for by model id.
    /// </summary>
    private void ApplyLedger(PlaybackTransport state, float left, float top, float height, float width)
    {
        Clear(_ledger);
        _ledger.Visible = state.Ledger.Count > 0;
        if (!_ledger.Visible) return;

        var rowHeight = 32 * _unit;
        var ledgerHeight = (10 * _unit) + (rowHeight * state.Ledger.Count);
        var ledgerTop = top + height + (6 * _unit);
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
    private void ApplyMenu(PlaybackTransport state, float left, float top, float height, float width)
    {
        Clear(_menu);
        var rows = OpenRows;
        _menu.Visible = rows.Count > 0;
        if (!_menu.Visible) return;

        var rowHeight = 32 * _unit;
        var menuWidth = (_menuIsChip ? 260 : 96) * _unit;
        var menuHeight = (10 * _unit) + (rowHeight * rows.Count);
        var menuLeft = _menuIsChip ? left + width - menuWidth : left + (192 * _unit);
        Place(_menu, menuLeft, top + height + (6 * _unit), menuWidth, menuHeight);
        PlatePolygon(_menu, menuWidth, menuHeight);

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var rowTop = (6 * _unit) + (index * rowHeight);
            var colour = !row.Enabled ? DisabledGlyph : row.IsCurrent ? Cream : Muted;

            if (row.Glyph is { } glyph)
            {
                var art = TransportGlyphArt.Of(glyph, $"MenuRow{index}.Glyph", 20 * _unit, colour);
                Place(art, 12 * _unit, rowTop + (6 * _unit));
                _menu.AddChild(art);
            }

            var button = Pressable($"MenuRow{index}", _font, () => Choose(index));
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
    /// Which menu is open, if any - and deliberately not the rows themselves.
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
    private bool _menuOpen;

    private bool _menuIsChip;

    private Action<int>? _onChoose;

    private Func<string, Texture2D?>? _art;

    /// <summary>The rows the open menu is showing, read from the state rather than
    /// held.</summary>
    private IReadOnlyList<MenuRow> OpenRows =>
        !_menuOpen ? [] : _menuIsChip ? _state.ChipMenu : _state.SpeedMenu;

    /// <summary>How the ledger gets the game's own artwork for a model id. Injected so
    /// the tag assembles in a process with no model database.</summary>
    internal void DrawArtWith(Func<string, Texture2D?> art) => _art = art;

    /// <summary>Opens one of the two menus. <paramref name="chip"/> chooses which;
    /// closing is <see cref="CloseMenu"/>.</summary>
    internal void OpenMenu(bool chip, Action<int>? chosen)
    {
        _menuOpen = true;
        _menuIsChip = chip;
        _onChoose = chosen;
        Apply(_state);
    }

    internal void CloseMenu()
    {
        _menuOpen = false;
        _onChoose = null;
        Apply(_state);
    }

    internal bool MenuIsOpen => _menuOpen && OpenRows.Count > 0;

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
        var tag = _state.HasControls;
        _holdTrack.Visible = tag;
        _holdFill.Visible = tag;
        if (!tag) return;

        var width = TagWidth * _unit;
        var left = _anchor.X - width;
        var y = _anchor.Y + (TagHeight * _unit) - (3 * _unit);
        var from = left + (12 * _unit);
        var to = left + width - (12 * _unit);
        _holdTrack.Points = [new Vector2(from, y), new Vector2(to, y)];
        _holdFill.Points =
            [new Vector2(from, y), new Vector2(from + ((to - from) * (float)Math.Clamp(fraction, 0, 1)), y)];
    }

    internal void HideHold()
    {
        _holdTrack.Visible = false;
        _holdFill.Visible = false;
    }

    /// <summary>Moves the tag when the game's own furniture moves under it - the relic
    /// row grows, the window changes.</summary>
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
    /// has one, and it says why it is refused rather than repeating what it would do.
    /// </summary>
    private void Wire(Button button, Func<string> title, Func<string> body)
    {
        button.MouseEntered += () => ShowTooltip(button, title(), body());
        button.MouseExited += HideTooltip;
        button.FocusEntered += () => ShowTooltip(button, title(), body());
        button.FocusExited += HideTooltip;
    }

    private string Body(TransportControl control) =>
        control.Enabled ? control.TooltipBody : control.DisabledReason ?? control.TooltipBody;

    private void ShowTooltip(Control anchor, string title, string body)
    {
        _tipTitle.Text = title;
        _tipBody.Text = body;
        _tip.Visible = true;

        var width = 250 * _unit;
        var lines = body.Split('\n').Length;
        var height = (30 + (15 * lines)) * _unit;

        // Below the control and pulled back on screen, never over the tag itself:
        // a tooltip that covers the counter it is explaining is worse than none.
        var x = Math.Clamp(
            anchor.Position.X + (anchor.Size.X / 2) - (width / 2), 8 * _unit, _viewport.X - width - (8 * _unit));
        Place(_tip, x, anchor.Position.Y + anchor.Size.Y + (10 * _unit), width, height);
        Clear(_tipPlate);
        Place(_tipPlate, 0, 0, width, height);
        PlatePolygon(_tipPlate, width, height);
        Place(_tipTitle, 12 * _unit, 6 * _unit, width - (24 * _unit), 18 * _unit);
        Place(_tipBody, 12 * _unit, 24 * _unit, width - (24 * _unit), height - (30 * _unit));
        _tipBody.AutowrapMode = TextServer.AutowrapMode.WordSmart;
    }

    private void HideTooltip() => _tip.Visible = false;

    // ── Node plumbing ──────────────────────────────────────────────────────

    private void SetGlyph(Control host, TransportGlyph glyph, float size, Color colour)
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
        Place(art, (host.Size.X - size) / 2, (host.Size.Y - size) / 2);
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

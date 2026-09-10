using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The bookmark tag: the recorder's one control, hung under the top bar for the
/// stretch between a fight ending and the run moving on.
///
/// The same material as the transport's tag - flat charcoal at 94%, an inked gold edge,
/// two pins, a chamfered foot - hung from the same anchor
/// <see cref="PlaybackTransportDock"/> measures, right-aligned to the deck button. A
/// fifth of the transport's width: the mod's mark at the left and one icon-only
/// control at the right, with its words a hover away. The two tags never coexist,
/// because a trainer run is not recorded and a recorded run has no transport.
///
/// Every state is <see cref="FightMark.For"/>'s; this draws, and it re-derives every
/// frame the way the recorder's overlay row does, because the facts under it change
/// while the run stands - a fight ends inside a sampled action, a map move leaves the
/// floor, a press flips the mark - and none of those is an event the tag is told about.
/// The press goes to the active recorder, which is the only thing that writes.
///
/// <para>Stock Godot nodes, for the reason the transport is: nodes this assembly
/// subclassed would never have their overrides called in the client, and stock nodes
/// are what let the tag be built and asserted on with no game. Nothing here holds a
/// value of a sibling assembly's type in a field, and the closures capture only
/// Godot's own types, because the game enumerates this assembly's types before the
/// siblings can be resolved.</para>
/// </summary>
internal sealed class FightMarkTag
{
    internal const string RootName = "RunmobileFightMark";

    // The transport's palette, on the transport's material.
    private static readonly Color PlateFace = Rgb(0x1c, 0x1a, 0x20, 0.94f);
    private static readonly Color PlateEdge = Rgb(0xc9, 0xa8, 0x5c);
    private static readonly Color PlateInner = Rgb(0x3a, 0x33, 0x30);
    private static readonly Color Cream = Rgb(0xf1, 0xe4, 0xc0);
    private static readonly Color Gold = Rgb(0xd9, 0xb2, 0x5f);
    private static readonly Color ButtonFace = Rgb(0x2a, 0x26, 0x2c);
    private static readonly Color ButtonEdge = Rgb(0x4a, 0x43, 0x40);
    private static readonly Color Ink = Rgb(0x1b, 0x16, 0x11);

    // In the design's reference units, scaled once, like the transport's own.
    private const float ReferenceHeight = 916f;
    private const float TagWidth = 120f;
    private const float TagHeight = 56f;
    private const float Chamfer = 12f;
    private const float MarkSize = 22f;
    private const float ButtonSize = 30f;
    private const float GlyphSize = 22f;

    private readonly Control _root;
    private readonly Polygon2D _plateFill;
    private readonly Line2D _plateEdge;
    private readonly Line2D _plateInner;
    private readonly Polygon2D _pinLeft;
    private readonly Polygon2D _pinRight;
    private readonly Control _mark;
    private readonly Button _button;
    private readonly float _unit;
    private readonly Vector2 _anchor;

    private bool _bookmarked;
    private bool _drawnOnce;

    private FightMarkTag(Control root, Polygon2D plateFill, Line2D plateEdge, Line2D plateInner,
        Polygon2D pinLeft, Polygon2D pinRight, Control mark, Button button, float unit, Vector2 anchor)
    {
        _root = root;
        _plateFill = plateFill;
        _plateEdge = plateEdge;
        _plateInner = plateInner;
        _pinLeft = pinLeft;
        _pinRight = pinRight;
        _mark = mark;
        _button = button;
        _unit = unit;
        _anchor = anchor;
    }

    internal Control Root => _root;

    /// <summary>The one control, for a test and for focus.</summary>
    internal Button Control => _button;

    /// <summary>
    /// Builds the tag, hidden, at the anchor.
    /// </summary>
    /// <param name="viewport">The size of the interface the tag is parented to.</param>
    /// <param name="anchor">The top-right corner the tag is pinned to.</param>
    /// <param name="pressed">What a press does. The recorder's toggle, in the client.</param>
    internal static FightMarkTag Build(Vector2 viewport, Vector2 anchor, Action pressed)
    {
        var root = new Control
        {
            Name = RootName,
            Position = Vector2.Zero,
            Size = viewport,
            Visible = false,
            // Everything that is not the control lets its clicks through: the loot
            // screen underneath is the thing the player is on.
            MouseFilter = Godot.Control.MouseFilterEnum.Ignore,
        };

        var unit = viewport.Y / ReferenceHeight;
        var plateFill = Add(root, new Polygon2D { Name = "Plate", Color = PlateFace });
        var plateEdge = Add(root, Stroke("PlateEdge", PlateEdge, 1.6f * unit));
        var plateInner = Add(root, Stroke("PlateInner", PlateInner, 1f * unit));
        var pinLeft = Add(root, new Polygon2D { Name = "PinLeft", Color = Gold });
        var pinRight = Add(root, new Polygon2D { Name = "PinRight", Color = Gold });
        var mark = Add(root, new Control { Name = "Mark", MouseFilter = Godot.Control.MouseFilterEnum.Ignore });
        var button = Add(root, new Button
        {
            Name = "Bookmark",
            // Takes focus on purpose: a control a keyboard or a controller cannot
            // reach is a control half the players do not have. Handed back on press,
            // which is the transport's own cost and its own fix.
            FocusMode = Godot.Control.FocusModeEnum.All,
        });
        Face(button);
        button.Pressed += () =>
        {
            pressed();
            button.ReleaseFocus();
        };

        var tag = new FightMarkTag(
            root, plateFill, plateEdge, plateInner, pinLeft, pinRight, mark, button, unit, anchor);
        tag.Layout();
        return tag;
    }

    /// <summary>
    /// Puts one derived mark on the tag.
    ///
    /// Visible only where the derivation drew it and the shell allows it. The glyph is
    /// rebuilt only when the mark changed, because a glyph is a small tree of nodes and
    /// this runs every frame.
    /// </summary>
    internal void Apply(FightMark mark, bool mayDraw)
    {
        var drawn = mayDraw && mark.Control.Presence == Presence.Drawn;
        _root.Visible = drawn;
        _button.Disabled = !mark.Control.Pressable;
        _button.TooltipText = mark.Control.TooltipBody.Length == 0
            ? mark.Control.TooltipTitle
            : $"{mark.Control.TooltipTitle}\n{mark.Control.TooltipBody}";

        if (_drawnOnce && _bookmarked == mark.Bookmarked) return;
        _drawnOnce = true;
        _bookmarked = mark.Bookmarked;
        SetGlyph(mark.Bookmarked);
    }

    /// <summary>Whether the tag is drawn filled: the mark is on.</summary>
    internal bool Bookmarked => _bookmarked;

    private void Layout()
    {
        var width = TagWidth * _unit;
        var height = TagHeight * _unit;
        var left = _anchor.X - width;
        var top = _anchor.Y;

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

        var markSize = MarkSize * _unit;
        Place(_mark, left + (12 * _unit), top + ((height - markSize) / 2), markSize, markSize);
        _mark.AddChild(TransportGlyphArt.Of(TransportGlyph.Mark, "Glyph", markSize, Gold));

        var button = ButtonSize * _unit;
        Place(_button, left + width - (12 * _unit) - button, top + ((height - button) / 2), button, button);
    }

    /// <summary>The control's glyph: hollow cream at rest, filled gold with an ink
    /// hairline once the mark is on.</summary>
    private void SetGlyph(bool bookmarked)
    {
        foreach (var child in _button.GetChildren().ToList())
        {
            if (child.Name.ToString().StartsWith("Glyph", StringComparison.Ordinal))
            {
                _button.RemoveChild(child);
                child.QueueFree();
            }
        }

        var box = ButtonSize * _unit;
        var size = GlyphSize * _unit;
        var at = (box - size) / 2;
        if (bookmarked)
        {
            var fill = LibraryGlyphArt.Of(LibraryGlyph.Bookmark, "GlyphFill", size, Gold);
            Place(fill, at, at);
            _button.AddChild(fill);
            var hairline = LibraryGlyphArt.Of(LibraryGlyph.BookmarkOutline, "GlyphEdge", size, Ink);
            Place(hairline, at, at);
            _button.AddChild(hairline);
        }
        else
        {
            var hollow = LibraryGlyphArt.Of(LibraryGlyph.BookmarkOutline, "Glyph", size, Cream);
            Place(hollow, at, at);
            _button.AddChild(hollow);
        }
    }

    private static void Face(Button button)
    {
        var style = new StyleBoxFlat { BgColor = ButtonFace, BorderColor = ButtonEdge };
        style.SetCornerRadiusAll(5);
        style.SetBorderWidthAll(1);
        foreach (var state in new[] { "normal", "pressed", "disabled" })
        {
            button.AddThemeStyleboxOverride(state, style);
        }

        // Hover and focus take the gold rim, the game's own language for "this is the
        // thing you are about to press".
        var lit = new StyleBoxFlat { BgColor = Rgb(0x3a, 0x33, 0x38), BorderColor = Gold };
        lit.SetCornerRadiusAll(5);
        lit.SetBorderWidthAll(2);
        button.AddThemeStyleboxOverride("hover", lit);
        button.AddThemeStyleboxOverride("focus", lit);
    }

    private static Vector2[] Chamfered(Vector2 origin, float width, float height, float chamfer) =>
    [
        origin,
        origin + new Vector2(width, 0),
        origin + new Vector2(width, height - chamfer),
        origin + new Vector2(width - (chamfer * 0.85f), height),
        origin + new Vector2(chamfer * 0.85f, height),
        origin + new Vector2(0, height - chamfer),
    ];

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

    private static Line2D Stroke(string name, Color colour, float width) =>
        new() { Name = name, DefaultColor = colour, Width = width };

    private static void Place(Control control, float x, float y, float width, float height)
    {
        control.Position = new Vector2(x, y);
        control.CustomMinimumSize = new Vector2(width, height);
        control.Size = new Vector2(width, height);
    }

    private static void Place(Control control, float x, float y) => control.Position = new Vector2(x, y);

    private static T Add<T>(Node parent, T child) where T : Node
    {
        parent.AddChild(child);
        return child;
    }

    private static Color Rgb(int r, int g, int b, float a = 1f) => new(r / 255f, g / 255f, b / 255f, a);

    // ── Lifecycle in the client ──────────────────────────────────────────────

    /// <summary>
    /// The two patches that keep the tag derived while a run stands, installed apart
    /// from the recorder's watch the way the overlay row is: a patch that will not
    /// attach here costs the tag and nothing else.
    /// </summary>
    internal static readonly IReadOnlyList<Type> PatchClasses = [typeof(RunBegins), typeof(RunEnds)];

    private static Action? _tick;

    /// <summary>
    /// A run is being set up. Starts re-deriving the tag every frame, once; the run's
    /// interface does not exist yet, so the tag is docked from the tick the first
    /// frame it can be.
    /// </summary>
    [HarmonyPatch(typeof(RunManager))]
    internal static class RunBegins
    {
        [HarmonyPostfix]
        [HarmonyPatch(nameof(RunManager.SetUpNewSingleplayer))]
        internal static void AfterNew() => Watch();

        [HarmonyPostfix]
        [HarmonyPatch(nameof(RunManager.SetUpSavedSingleplayer))]
        internal static void AfterSaved() => Watch();
    }

    /// <summary>The run is being torn down. The tag goes with the interface it hung
    /// in; this stops asking.</summary>
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
    internal static class RunEnds
    {
        [HarmonyPostfix]
        internal static void After() => Stop();
    }

    private static void Watch()
    {
        try
        {
            if (_tick is not null) return;
            if (Godot.Engine.GetMainLoop() is not SceneTree tree) return;

            // A static method group and not a closure: nothing of a sibling
            // assembly's is captured, and the same delegate is what is removed.
            Action tick = Tick;
            _tick = tick;
            tree.ProcessFrame += tick;
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] the bookmark tag could not start watching the run: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    private static void Stop()
    {
        try
        {
            if (_tick is { } tick && Godot.Engine.GetMainLoop() is SceneTree tree) tree.ProcessFrame -= tick;
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] the bookmark tag could not stop watching the run: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
        finally
        {
            _tick = null;
            PlaybackTransportDock.DetachFightMark();
        }
    }

    /// <summary>
    /// Reads the facts and puts what they derive to on the tag, docking it the first
    /// frame the run has an interface to dock in.
    /// </summary>
    private static void Tick()
    {
        try
        {
            var mark = FightMark.For(RunRecorder.FightMarkFacts());
            var current = PlaybackTransportDock.FightMark;
            if (current is null)
            {
                // Nothing to draw and nothing built: the common case, every frame of
                // every fight, and it costs one derivation.
                if (mark.Control.Presence != Presence.Drawn) return;
                current = PlaybackTransportDock.AttachFightMark(RunRecorder.ToggleBookmark);
                if (current is null) return;
            }

            current.Apply(mark, RunmobileMod.MayDraw);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not derive the bookmark tag: {ex.GetType().Name}: {ex.Message}", 2);
            Stop();
        }
    }
}

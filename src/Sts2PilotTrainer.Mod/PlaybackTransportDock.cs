using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// Where the transport lives inside the running client, and for how long.
///
/// The strip itself is stock Godot nodes and knows nothing about the game; this puts
/// it somewhere that outlives a room. <c>NRun.GlobalUi</c> is the run's own
/// persistent interface - the top bar, the relic inventory, the map screen - and the
/// room is a sibling of it that the game swaps underneath. A strip parented here
/// therefore crosses the map-to-combat transition without being rebuilt, which is the
/// fact this whole design was waiting on.
///
/// It docks in the band under the game's own top bar, measured from the top bar
/// rather than written down, because that band is the one part of every screen this
/// journey walks past that the game leaves empty.
/// </summary>
internal static class PlaybackTransportDock
{
    /// <summary>
    /// How far under the top bar's own widgets the tag hangs, in the design's
    /// reference units.
    ///
    /// Measured rather than chosen: the bar's torn edge runs to about 76 and the HP
    /// widget's box ends at 50, so a smaller gap puts the tag's head inside the tear.
    /// </summary>
    private const float BandGap = 22f;

    /// <summary>The design's own reference height, which every measurement in it is
    /// expressed against.</summary>
    private const float ReferenceHeight = 916f;

    /// <summary>Where the tag hangs on a client whose top bar cannot be measured, in
    /// reference units. A fallback rather than a layout: the tag is better slightly
    /// misplaced than absent, and the log says which happened.</summary>
    private const float FallbackTop = 72f;

    private const float FallbackRight = 1358f;

    private static PlaybackTransportStrip? _strip;

    internal static PlaybackTransportStrip? Current => _strip;

    /// <summary>
    /// Puts the strip on screen, once.
    ///
    /// Refuses rather than drawing somewhere arbitrary: with no run node there is no
    /// persistent interface to live in, and a strip parented to the room instead
    /// would vanish at the first transition - the exact failure this replaces.
    /// </summary>
    internal static PlaybackTransportStrip Attach(
        PlaybackTransport state, Action back, Action play, Action step, Action speed, Action identity,
        Func<string, Texture2D?> art)
    {
        Detach();

        var run = NRun.Instance
            ?? throw new InvalidOperationException(
                "This game has no run node, so there is nothing that outlives a room for the transport to " +
                "live in.");

        var globalUi = run.GlobalUi
            ?? throw new InvalidOperationException(
                "This run has no persistent interface, so the transport has nowhere to dock.");

        var strip = PlaybackTransportStrip.Build(
            state,
            globalUi.GetViewportRect().Size,
            Anchor(globalUi),
            GameFont.Of(globalUi.GetTree()?.Root),
            back,
            play,
            step,
            speed,
            identity);
        strip.DrawArtWith(art);

        // Added last so it draws over the run's own interface rather than under it.
        globalUi.AddChild(strip.Root);
        _strip = strip;
        Log.Info(
            $"[{RunmobileMod.ModId}] docked the transport under {globalUi.Name} " +
            $"(viewport {globalUi.GetViewportRect().Size}, anchor {Anchor(globalUi)}, " +
            $"strip {strip.Root.GetGlobalRect()}, visible {strip.Root.IsVisibleInTree()})", 2);
        return strip;
    }

    /// <summary>Changes what the strip says. Does nothing when there is none, which
    /// is the case on every path that tore the run down first.</summary>
    internal static void Apply(PlaybackTransport state)
    {
        if (_strip is not { } strip || !GodotObject.IsInstanceValid(strip.Root)) return;
        strip.Apply(state);
    }

    internal static void Detach()
    {
        var strip = _strip;
        _strip = null;
        if (strip is null || !GodotObject.IsInstanceValid(strip.Root)) return;

        try
        {
            strip.Root.GetParent()?.RemoveChild(strip.Root);
            strip.Root.QueueFree();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not remove the transport: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Where the tag hangs from: the top-right corner it is pinned to.
    ///
    /// Anchored to the game's own furniture rather than positioned at a constant, and
    /// both halves of that are load-bearing. The top comes from the bar's own widgets
    /// because <c>NTopBar</c> is a full-screen control whose rect ends at the bottom
    /// of the viewport - measuring the node itself put the first tag off the screen
    /// entirely. The right comes from the deck button, which is the right edge of the
    /// game's own meta cluster, so the tag hangs under map, deck and settings where
    /// controls that act on the recording belong. Neither is a number that is right on
    /// one monitor.
    ///
    /// It does not clear everything drawn out there, and the client says so: the build
    /// and seed text <c>NDebugInfoLabelManager</c> draws starts further right than the
    /// deck button ends, so the tag covers the first characters of the seed. That is
    /// the game's own version overlay, which a player toggles off from the menu that
    /// put it up, and dodging it would trade a measured anchor for a debug artefact.
    /// The gameplay furniture is what the anchor is for.
    /// </summary>
    private static Vector2 Anchor(NGlobalUi globalUi)
    {
        var unit = globalUi.GetViewportRect().Size.Y / ReferenceHeight;
        var top = FallbackTop * unit;
        var right = FallbackRight * unit;

        try
        {
            var topBar = globalUi.TopBar;

            // Health and gold are the leftmost things the bar draws on every screen
            // this journey walks past, and the lower of them is where its widgets end.
            var bottom = 0f;
            foreach (var widget in new Control?[] { topBar?.Hp, topBar?.Gold })
            {
                if (widget is { Visible: true } && widget.Size.Y > 0)
                {
                    bottom = Math.Max(bottom, widget.GetGlobalRect().End.Y);
                }
            }

            if (bottom > 0) top = bottom + (BandGap * unit);

            // The deck button, with the settings button as the fallback the design
            // names: both sit in the same cluster and either one puts the tag in the
            // right neighbourhood.
            var edge = topBar?.Deck as Control ?? topBar?.Pause;
            if (edge is { Visible: true } && edge.Size.X > 0) right = edge.GetGlobalRect().End.X;
        }
        catch (Exception ex)
        {
            Log.Info(
                $"[{RunmobileMod.ModId}] could not measure the top bar ({ex.GetType().Name}); hanging " +
                "the transport at the default anchor", 2);
        }

        return new Vector2(right, top);
    }
}

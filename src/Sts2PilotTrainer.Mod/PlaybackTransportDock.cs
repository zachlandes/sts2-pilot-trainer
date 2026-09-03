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
    /// <summary>The gap between the game's top bar and the strip.</summary>
    private const float BandGap = 12f;

    /// <summary>Where the band starts on a client whose top bar cannot be measured.
    /// A fallback rather than a layout: the strip is better slightly misplaced than
    /// absent, and the log says which happened.</summary>
    private const float FallbackDockTop = 108f;

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
        PlaybackTransport state, Action back, Action forward, Action play)
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
            DockTop(globalUi),
            GameFont.Of(globalUi.GetTree()?.Root),
            back,
            forward,
            play);

        // Added last so it draws over the run's own interface rather than under it.
        globalUi.AddChild(strip.Root);
        _strip = strip;
        Log.Info(
            $"[{RunmobileMod.ModId}] docked the transport under {globalUi.Name} " +
            $"(viewport {globalUi.GetViewportRect().Size}, dock top {DockTop(globalUi)}, " +
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
    /// How far down the empty band under the game's top bar starts.
    ///
    /// Measured off the widgets the bar actually draws rather than off the bar node,
    /// which was the first thing tried and is wrong: <c>NTopBar</c> is a full-screen
    /// control whose rect ends at the bottom of the viewport, so docking under it put
    /// the strip off the screen. Health and gold are the leftmost things in the bar
    /// on every screen this journey walks past, and the bottom of the lower of them is
    /// where the band the game leaves empty begins.
    /// </summary>
    private static float DockTop(NGlobalUi globalUi)
    {
        try
        {
            var topBar = globalUi.TopBar;
            var bottom = 0f;
            foreach (var widget in new Control?[] { topBar?.Hp, topBar?.Gold })
            {
                if (widget is { Visible: true } && widget.Size.Y > 0)
                {
                    bottom = Math.Max(bottom, widget.GetGlobalRect().End.Y);
                }
            }

            if (bottom > 0) return bottom + BandGap;
        }
        catch (Exception ex)
        {
            Log.Info(
                $"[{RunmobileMod.ModId}] could not measure the top bar ({ex.GetType().Name}); docking " +
                "the transport at the default height", 2);
        }

        return FallbackDockTop;
    }
}

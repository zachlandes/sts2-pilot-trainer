using System.Globalization;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using Sts2PilotTrainer.Engine;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// Lights the thing the recording is about to choose, using the game's own selected
/// state and never its click path.
///
/// The reveal half of reveal, hold, commit. The captain's rule for it is that the
/// trainer should piggy-back on the highlighting the game already has rather than
/// draw a ring of its own, and that a watcher gets to see what was chosen before the
/// screen moves on - so this applies the state a hovered or controller-focused
/// control would be in, holds it there, and leaves the choosing to
/// <see cref="RecordedFightEntry.AdvanceOneStep"/>.
///
/// Two mechanisms, both the game's. Focus is what a control's own <c>OnFocus</c>
/// runs off, so grabbing it is what plays the game's hover tween on an event option
/// and scales a map node. On the map there is also <see cref="NSelectionReticle"/>,
/// the ring the game puts round a controller-focused node, and that one is lit
/// directly so it stays lit when the player moves focus to the transport.
///
/// It refuses rather than approximating. A screen that is not up, a coordinate this
/// act's map does not draw, an option row that grants a different relic from the one
/// the recording took: each of those is a reveal that would be pointing at the wrong
/// thing, and pointing at the wrong thing before committing a decision is worse than
/// not pointing at all.
/// </summary>
internal static class RecordedFightReveal
{
    /// <summary>The unique name the map point's own scene gives its reticle. Read the
    /// way the game reads it, in <c>NMapPoint.ConnectSignals</c>.</summary>
    private const string ReticlePath = "%SelectionReticle";

    /// <summary>What is currently lit, so it can be put back. Only ever a node this
    /// class lit itself.</summary>
    private static NSelectionReticle? _lit;

    /// <summary>
    /// Applies the game's own selected state to the recording's next decision.
    /// </summary>
    /// <returns>What was lit, for the log.</returns>
    /// <exception cref="RevealNotReadyException">When the target is there but the
    /// screen has not finished putting it up, which is a moment to wait out rather
    /// than a reason to end the run.</exception>
    /// <exception cref="InvalidOperationException">When the screen the decision
    /// happens on cannot be driven, or does not hold the thing the recording chose.</exception>
    internal static string Reveal(PrefightTarget target)
    {
        Clear();
        switch (target)
        {
            case PrefightTarget.MapNode node:
                RevealMapNode(node.Coord);
                break;
            case PrefightTarget.EventOption option:
                RevealEventOption(option.Index, option.RelicModelId);
                break;
            default:
                throw new InvalidOperationException(
                    $"Action {target.Seq} is a kind of decision this trainer cannot point at on the game's " +
                    "own screen, so it will not be committed unseen.");
        }

        return target.Description;
    }

    /// <summary>Puts back whatever this class lit. Safe to call when nothing is.</summary>
    internal static void Clear()
    {
        var lit = _lit;
        _lit = null;
        if (lit is null || !GodotObject.IsInstanceValid(lit)) return;
        lit.OnDeselect();
    }

    private static void RevealMapNode(MapCoord coord)
    {
        var map = NMapScreen.Instance
            ?? throw new InvalidOperationException(
                "The recording moves on the map, and this game has no map screen to show the move on.");

        var points = MapPointsOf(map);
        if (!points.TryGetValue(coord, out var point) || !GodotObject.IsInstanceValid(point))
        {
            throw new InvalidOperationException(
                $"The recording moves to (row {coord.row.ToString(CultureInfo.InvariantCulture)}, column " +
                $"{coord.col.ToString(CultureInfo.InvariantCulture)}), which this act's map screen does not " +
                "draw. Refusing to move somewhere nobody was shown.");
        }

        // Focus first, because the node's own OnFocus is what scales it and tints it.
        Focus(point, "the map node the recording moves to");

        // Then the ring, directly. The game lights it from OnFocus only when the
        // player is on a controller, and it is the clearest mark the map has - so it
        // is lit here whatever they are holding, and it survives the player moving
        // focus to the transport's own buttons.
        var reticle = point.GetNodeOrNull<NSelectionReticle>(ReticlePath);
        if (reticle is not null)
        {
            reticle.OnSelect();
            _lit = reticle;
        }
    }

    private static void RevealEventOption(int index, string relicModelId)
    {
        var room = NEventRoom.Instance
            ?? throw new InvalidOperationException(
                "The recording chooses an event option, and this game has no event screen to show it on.");

        var layout = room.Layout
            ?? throw new InvalidOperationException(
                "The event screen has no layout, so the option the recording took cannot be pointed at.");

        var buttons = layout.OptionButtons.ToList();
        if (index < 0 || index >= buttons.Count)
        {
            throw new InvalidOperationException(
                $"The recording takes option {index.ToString(CultureInfo.InvariantCulture)} and this screen " +
                $"is showing {buttons.Count.ToString(CultureInfo.InvariantCulture)}. Refusing to point at a " +
                "row that is not there.");
        }

        var button = buttons[index];

        // Checked rather than assumed. The buttons are the event's options in the
        // order it listed them, and that is exactly the kind of thing that is true
        // until a build changes it - so the row is confirmed to be the one granting
        // the relic the recording took before it is lit and then committed.
        var offered = button.Option?.Relic?.Id.ToString();
        if (offered != relicModelId)
        {
            throw new InvalidOperationException(
                $"The recording takes the option granting '{relicModelId}', and option " +
                $"{index.ToString(CultureInfo.InvariantCulture)} on this screen grants " +
                $"'{offered ?? "nothing"}'. The screen is not showing what the recording chose.");
        }

        Focus(button, $"option {index.ToString(CultureInfo.InvariantCulture)} on the event screen");
    }

    /// <summary>
    /// Applies the game's own focus, and says so when the screen is not ready for it
    /// yet.
    ///
    /// Measured in the client: an event's option rows fly in and are enabled at the
    /// end of that animation, so a reveal issued the moment the run reaches the screen
    /// finds a control that cannot take focus, and Godot's own answer to that is a
    /// warning rather than a failure. A reveal that silently did not happen is a
    /// decision about to be committed unseen, so this refuses instead - as
    /// <see cref="RevealNotReadyException"/>, because the honest response to a screen
    /// still settling is to wait for it rather than to end the run.
    /// </summary>
    private static void Focus(Control control, string what)
    {
        if (control.FocusMode == Control.FocusModeEnum.None || !control.IsVisibleInTree())
        {
            throw new RevealNotReadyException($"This screen is still putting up {what}.");
        }

        control.GrabFocus();
        if (!control.HasFocus())
        {
            throw new RevealNotReadyException($"This screen would not let {what} be selected yet.");
        }
    }

    /// <summary>
    /// The map screen's own node for each coordinate.
    ///
    /// Private on the screen and reached by name, which is this project's standing
    /// answer for a reading the game does not expose: refuse loudly on a build that
    /// no longer has it rather than fall back to something that looks like it worked.
    /// </summary>
    private static IReadOnlyDictionary<MapCoord, NMapPoint> MapPointsOf(NMapScreen map)
    {
        var field = typeof(NMapScreen).GetField(
            "_mapPointDictionary", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new InvalidOperationException(
                "NMapScreen has no _mapPointDictionary on this build, so the node the recording moves to " +
                "cannot be found on screen.");

        return field.GetValue(map) as IReadOnlyDictionary<MapCoord, NMapPoint>
            ?? throw new InvalidOperationException(
                "NMapScreen._mapPointDictionary is not a map of coordinates to nodes on this build.");
    }
}

/// <summary>
/// The screen holds what the recording chose, and is not ready to show it selected.
///
/// Its own type because the two failures want opposite answers: a screen that cannot
/// be driven at all ends the attempt with the reason, and a screen that is still
/// animating wants another moment. Conflating them either ends runs over a tween or
/// waits forever for a screen that will never arrive.
/// </summary>
internal sealed class RevealNotReadyException(string message) : InvalidOperationException(message);

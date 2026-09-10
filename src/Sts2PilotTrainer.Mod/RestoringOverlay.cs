using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// What the player looks at while the save their run is restored from is being
/// materialised.
///
/// The one surface this mod draws that is not parented to a run. Everything else the
/// journey shows hangs under <c>NRun.GlobalUi</c>, and in
/// <see cref="JourneyPhase.Preparing"/> there is no run yet - the packaged arbiter is
/// replaying the recording's history in its own process, which took a little under a
/// minute on the retail proof. So this hangs under <c>NGame</c>, which outlives the
/// menu it is put up over and the run scene that replaces it.
///
/// It is the game's own loading idiom rather than a plate of this mod's design:
/// <c>res://scenes/screens/main_menu/loading_overlay.tscn</c> is what the client puts
/// up while it loads, and instantiating it is what makes this read as the game
/// waiting rather than as a mod overlay. Only its label is this mod's, which is the
/// same rule every other surface here follows - the game's own furniture, at the
/// sizes the game draws it.
///
/// Indeterminate on purpose. The arbiter prints the floors it arrived on only once
/// its replay has finished, so a floor-by-floor bar would be a number this mod does
/// not have; the animated ellipsis says the wait is alive and claims nothing else.
///
/// Nothing here is allowed to end a journey: this is what a player looks at while the
/// work happens, not the work, so a client that cannot put it up is logged and the
/// restore goes on without it.
/// </summary>
internal static class RestoringOverlay
{
    private const string ScenePath = "res://scenes/screens/main_menu/loading_overlay.tscn";

    /// <summary>How long each step of the ellipsis is held. The game's own loading
    /// text is static, so this is the one thing here that is not borrowed; slow
    /// enough to read as breathing rather than as flicker.</summary>
    private const double EllipsisSeconds = 0.45;

    private const int EllipsisSteps = 4;

    private static Control? _overlay;
    private static MegaLabel? _label;
    private static string _headline = string.Empty;
    private static int _step;

    /// <summary>Puts the notice up, changes it, or takes it away - whichever the
    /// derivation says. Idempotent, because every phase change asks.</summary>
    internal static void Apply(RestoringNotice? notice)
    {
        if (notice is null)
        {
            Remove();
            return;
        }

        _headline = notice.Headline;
        if (_overlay is { } up && GodotObject.IsInstanceValid(up))
        {
            Draw();
            return;
        }

        Show();
    }

    internal static void Remove()
    {
        var overlay = _overlay;
        _overlay = null;
        _label = null;
        _step = 0;
        if (overlay is null || !GodotObject.IsInstanceValid(overlay)) return;

        try
        {
            overlay.GetParent()?.RemoveChild(overlay);
            overlay.QueueFree();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not remove the restoring notice: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    private static void Show()
    {
        try
        {
            var game = NGame.Instance
                ?? throw new InvalidOperationException(
                    "This process has no game node, so there is nothing that outlives the menu for the " +
                    "restoring notice to live in.");

            var scene = ResourceLoader.Load<PackedScene>(ScenePath)
                ?? throw new InvalidOperationException($"This build has no '{ScenePath}' to borrow.");

            var overlay = scene.Instantiate<Control>();

            // Added before the label is asked for: the scene's own script puts the
            // game's "Loading..." on it when it enters the tree, so a headline set
            // before that would be the one thing overwritten.
            game.AddChild(overlay);
            _overlay = overlay;
            _step = 0;
            _label = overlay.GetNodeOrNull<MegaLabel>("%Label")
                ?? throw new InvalidOperationException(
                    $"This build's '{ScenePath}' has no %Label, so the restoring notice has nothing to say.");

            Draw();

            var ellipsis = new Godot.Timer { WaitTime = EllipsisSeconds, Autostart = true, OneShot = false };
            overlay.AddChild(ellipsis);
            ellipsis.Timeout += Advance;
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not put up the restoring notice: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
            Remove();
        }
    }

    private static void Advance()
    {
        _step = (_step + 1) % EllipsisSteps;
        Draw();
    }

    private static void Draw()
    {
        if (_label is not { } label || !GodotObject.IsInstanceValid(label)) return;
        label.SetTextAutoSize(_headline + new string('.', _step));
    }
}

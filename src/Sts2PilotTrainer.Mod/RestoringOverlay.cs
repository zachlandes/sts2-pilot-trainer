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
/// It prefers the game's own loading idiom rather than a plate of this mod's design:
/// <c>res://scenes/screens/main_menu/loading_overlay.tscn</c> is what the client puts
/// up while it loads, and instantiating it is what makes this read as the game
/// waiting rather than as a mod overlay. That path is confirmed against the package
/// index for v0.111.0, and the scene's script - <c>NLoadingOverlay</c> - is what reads
/// its <c>%Label</c>. Only the label's words are this mod's.
///
/// The borrowed scene is preferred and never required. Where it cannot be loaded, or
/// is loaded and has no <c>%Label</c> to say anything through, this draws its own
/// plate instead: the whole point of the surface is that a minute of waiting is not
/// spent looking at nothing, so a client that has moved the scene costs the native
/// look and never the notice. The two drawings say the same sentence, which is
/// <see cref="RestoringNotice"/>'s.
///
/// The one thing it substitutes a default for is the font, and only where the native
/// reading itself fails: elsewhere in this mod missing native furniture refuses the
/// surface, and here refusing would mean the blank screen this exists to remove.
///
/// Indeterminate on purpose. The arbiter prints the floors it arrived on only once
/// its replay has finished, so a floor-by-floor bar would be a number this mod does
/// not have; the animated ellipsis says the wait is alive and claims nothing else.
///
/// Nothing here is allowed to end a journey: this is what a player looks at while the
/// work happens, not the work.
/// </summary>
internal static class RestoringOverlay
{
    /// <summary>Confirmed against the game's own package index for v0.111.0. The
    /// label inside it is what the fallback covers, because a scene that loads and
    /// has been rearranged says nothing without one.</summary>
    private const string ScenePath = "res://scenes/screens/main_menu/loading_overlay.tscn";

    private const string LabelPath = "%Label";

    internal const string RootName = "RunmobileRestoringNotice";

    /// <summary>How long each step of the ellipsis is held. The game's own loading
    /// text is static, so this is the one thing here that is not borrowed; slow
    /// enough to read as breathing rather than as flicker.</summary>
    private const double EllipsisSeconds = 0.45;

    private static readonly Color Scrim = new(0.06f, 0.07f, 0.08f, 0.94f);
    private static readonly Color HeadlineText = new(0.945f, 0.874f, 0.682f);

    private static Control? _overlay;
    private static MegaLabel? _borrowedLabel;
    private static Label? _ownLabel;
    private static RestoringNotice? _notice;
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

        _notice = notice;
        if (_overlay is { } up && GodotObject.IsInstanceValid(up))
        {
            Draw();
            return;
        }

        Show(notice);
    }

    internal static void Remove()
    {
        var overlay = _overlay;
        Forget();
        Free(overlay);
    }

    private static void Show(RestoringNotice notice)
    {
        var game = NGame.Instance;
        if (game is null)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] this process has no game node, so the restoring notice has nowhere " +
                "to live", 2);
            return;
        }

        _step = 0;
        if (!BorrowTheGamesOverlay(game))
        {
            var partial = _overlay;
            Forget();
            Free(partial);
            if (!DrawItHere(game)) return;
        }

        _notice = notice;
        Draw();

        var ellipsis = new Godot.Timer { WaitTime = EllipsisSeconds, Autostart = true, OneShot = false };
        _overlay!.AddChild(ellipsis);
        ellipsis.Timeout += Advance;
    }

    /// <summary>
    /// The client's own loading overlay, put up and read for its label.
    ///
    /// False for either way it can fail to be the surface a player reads - no such
    /// scene on this build, or a scene with nothing to say through - and the caller
    /// draws this mod's own plate instead.
    /// </summary>
    private static bool BorrowTheGamesOverlay(Node game)
    {
        try
        {
            var scene = ResourceLoader.Load<PackedScene>(ScenePath);
            if (scene is null)
            {
                Log.Error(
                    $"[{RunmobileMod.ModId}] this build has no '{ScenePath}' to borrow; drawing the restoring " +
                    "notice here instead", 2);
                return false;
            }

            var overlay = scene.Instantiate<Control>();

            // Added before the label is asked for: the scene's own script puts the
            // game's "Loading..." on it when it enters the tree, so a headline set
            // before that would be the one thing overwritten.
            game.AddChild(overlay);
            _overlay = overlay;
            _borrowedLabel = overlay.GetNodeOrNull<MegaLabel>(LabelPath);
            if (_borrowedLabel is not null) return true;

            Log.Error(
                $"[{RunmobileMod.ModId}] this build's '{ScenePath}' has no {LabelPath}; drawing the restoring " +
                "notice here instead", 2);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not borrow '{ScenePath}' for the restoring notice " +
                $"({ex.GetType().Name}: {ex.Message}); drawing it here instead", 2);
            return false;
        }
    }

    /// <summary>This mod's own plate: a scrim over whatever is behind, and one line
    /// centred on it.</summary>
    private static bool DrawItHere(Node game)
    {
        try
        {
            var root = new Control
            {
                Name = RootName,
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            game.AddChild(root);

            var viewport = root.GetViewportRect().Size;
            root.Position = Vector2.Zero;
            root.Size = viewport;

            root.AddChild(new ColorRect
            {
                Name = "Scrim",
                Color = Scrim,
                Position = Vector2.Zero,
                Size = viewport,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });

            var label = new Label
            {
                Name = "Headline",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Position = Vector2.Zero,
                Size = viewport,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            NativeHeadingStyle()?.ApplyTo(label);
            label.AddThemeColorOverride("font_color", HeadlineText);
            root.AddChild(label);

            _overlay = root;
            _ownLabel = label;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not draw the restoring notice: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
            var partial = _overlay;
            Forget();
            Free(partial);
            return false;
        }
    }

    private static GameTextStyle? NativeHeadingStyle()
    {
        try
        {
            return GameText.Scene(NativeTextRole.PopupHeading);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not read this build's native heading style for the restoring " +
                $"notice ({ex.GetType().Name}: {ex.Message}); saying it at Godot's own size rather than not " +
                "at all", 2);
            return null;
        }
    }

    private static void Advance()
    {
        _step++;
        Draw();
    }

    private static void Draw()
    {
        if (_notice is not { } notice) return;
        var line = notice.Line(_step);

        if (_borrowedLabel is { } borrowed && GodotObject.IsInstanceValid(borrowed))
        {
            borrowed.SetTextAutoSize(line);
            return;
        }

        if (_ownLabel is { } own && GodotObject.IsInstanceValid(own)) own.Text = line;
    }

    private static void Forget()
    {
        _overlay = null;
        _borrowedLabel = null;
        _ownLabel = null;
        _notice = null;
        _step = 0;
    }

    private static void Free(Control? overlay)
    {
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
}

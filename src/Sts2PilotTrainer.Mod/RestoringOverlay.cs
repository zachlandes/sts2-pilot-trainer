using Godot;
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
/// One drawing, and it is this mod's own: a scrim over whatever is behind, and the
/// notice's line centred on it at the native heading role. The client's own loading
/// overlay was borrowed here for a while and is not any more. That scene ships hidden,
/// so borrowing it put a present-but-invisible surface over the whole wait - the
/// blank screen this exists to remove, drawn by the half of the surface no test
/// process can execute. The native backdrop is what was dropped; a follow-up may
/// borrow it again with its visibility handled and an in-client capture behind it.
///
/// The one thing it substitutes a default for is the font, and only where the native
/// reading itself fails: elsewhere in this mod missing native furniture refuses the
/// surface, and here refusing would mean that blank screen again.
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
    internal const string RootName = "RunmobileRestoringNotice";

    /// <summary>How long each step of the ellipsis is held. Slow enough to read as
    /// breathing rather than as flicker.</summary>
    private const double EllipsisSeconds = 0.45;

    private static readonly Color Scrim = new(0.06f, 0.07f, 0.08f, 0.94f);
    private static readonly Color HeadlineText = new(0.945f, 0.874f, 0.682f);

    private static Control? _overlay;
    private static Label? _label;
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

        ShowUnder(game, notice);
    }

    /// <summary>The same, under a named parent, which is what lets the surface be put
    /// up and read in a process with no game.</summary>
    internal static void ShowUnder(Node parent, RestoringNotice notice)
    {
        _step = 0;
        if (!DrawItHere(parent)) return;

        _notice = notice;
        Draw();

        var ellipsis = new Godot.Timer { WaitTime = EllipsisSeconds, Autostart = true, OneShot = false };
        _overlay!.AddChild(ellipsis);
        ellipsis.Timeout += Advance;
    }

    /// <summary>The plate: a scrim over whatever is behind, and one line centred on
    /// it.</summary>
    private static bool DrawItHere(Node parent)
    {
        try
        {
            var root = new Control
            {
                Name = RootName,
                MouseFilter = Control.MouseFilterEnum.Stop,
                Visible = true,
            };
            parent.AddChild(root);

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
            _label = label;
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
        if (_label is { } label && GodotObject.IsInstanceValid(label)) label.Text = notice.Line(_step);
    }

    private static void Forget()
    {
        _overlay = null;
        _label = null;
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

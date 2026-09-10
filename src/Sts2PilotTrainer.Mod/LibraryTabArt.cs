using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The library's tabs: the game's own settings tab, instantiated from its scene.
///
/// <c>NSettingsTab</c> is what the Statistics and Achievements tabs on the game's own
/// stats screen are, and the settings screen's General, Audio and the rest: a squared
/// plate with a stroked outline drawn only on the selected one, a label that goes from
/// half-cream to cream on selection and to gold on hover, and a hover scale on the
/// whole tab. A ribbon duplicated from the popup's cancel button read as a button and
/// not a tab, which is what this replaces.
///
/// <para><b>The lock is the game's own too.</b> The stats screen lays
/// <c>submenu_lock.png</c> over its Achievements tab while that tab is disabled; the
/// same image over the Community tab says the same thing in the same place, with a
/// tooltip naming what is short. The tab stays pressable under it because what is
/// behind it - the runs included with Runmobile and a run looked up by code - is
/// still there, and a locked tab a player cannot open would hide them.</para>
///
/// <para>The tab's hover materials are declared local to its scene, so every instance
/// animates its own; nothing here shares a material with a row or a ribbon.</para>
/// </summary>
internal static class LibraryTabArt
{
    internal const string Scene = "res://scenes/screens/settings_tab.tscn";

    /// <summary>The stats screen's own lock over its disabled Achievements tab: the
    /// image <c>stats_screen.tscn</c> binds to that tab's <c>Lock</c> node. Two files in
    /// this build share the basename, and <c>images/ui/main_menu/submenu_lock.png</c> is
    /// the other one - the chained lock a locked submenu card wears, with its plate and
    /// frame in the picture - which is what this named until a scene fact held it to
    /// the stats scene.</summary>
    internal const string LockImage = "res://images/packed/main_menu/submenu_lock.png";

    /// <summary>The scene's own proportions: 256 by 90. A tab at any height keeps
    /// them, so the plate is never squashed.</summary>
    internal const float Aspect = 256f / 90f;

    /// <summary>
    /// Where the lock sits, from the stats scene: a square a little taller than the
    /// tab, centred on it, so it overhangs the plate's top and bottom edges the way the
    /// game's does (offsets 73..182 by -5.5..103.5 on a 256 by 90 tab).
    /// </summary>
    internal static Rect2 LockBounds(Vector2 tab)
    {
        var side = tab.Y * (109f / 90f);
        return new Rect2((tab.X - side) / 2f, (tab.Y - side) / 2f, side, side);
    }

    /// <summary>
    /// Adds one tab, sized to the box it was given.
    ///
    /// Selected and deselected through the tab's own <c>Select</c> and
    /// <c>Deselect</c>, which is what the stats screen's tab manager calls; nothing here
    /// tints a label or toggles an outline by hand.
    /// </summary>
    internal static NSettingsTab Add(
        Control parent, string name, string label, bool current, string? lockTooltip,
        Rect2 at, Action? press)
    {
        var scene = ResourceLoader.Load<PackedScene>(Scene)
            ?? throw new InvalidOperationException($"This build has no '{Scene}' tab scene.");
        var tab = scene.Instantiate<NSettingsTab>();
        tab.Name = name;
        // The scene anchors its root to its parent's centre; an offset written before
        // there is a parent lands the tab at that centre plus the offset once it enters
        // the tree. Anchor it top-left first, so a position means a position
        tab.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        tab.Position = at.Position;
        // The scene declares a 256 by 90 minimum, and Godot clamps a size up to the
        // minimum standing at the moment it is written - lowering it afterwards does not
        // shrink what was already clamped, which drew the two tabs 256 wide at 170
        // apart, one over the other. Lower the constraint before writing the value, the
        // way the strip's textures ignore their intrinsic size before taking art
        tab.CustomMinimumSize = at.Size;
        tab.Size = at.Size;
        // The hover scale grows from the tab's centre, as the game's does
        tab.PivotOffset = at.Size / 2f;
        tab.FocusMode = FocusModeFor(current);
        parent.AddChild(tab);
        // The scene sorts its own children on entering the tree and may clamp again
        tab.Size = at.Size;

        tab.SetLabel(label);
        if (current) tab.Select();
        else tab.Deselect();

        if (press is { } pressed)
        {
            tab.Connect(
                NClickableControl.SignalName.Released,
                Callable.From<NClickableControl>(_ => pressed()));
        }

        if (lockTooltip is { Length: > 0 } tooltip) AddLock(tab, tooltip);
        return tab;
    }

    /// <summary>
    /// Whether a tab is a controller focus stop.
    ///
    /// The game's own <c>settings_tab.tscn</c> leaves <c>focus_mode</c> unset, so
    /// <c>NClickableControl</c> reads as not controller-navigable and a native tab is
    /// never stopped on; its manager switches tabs on the shoulder hotkeys instead.
    /// This band has no hotkeys, so the tab a press would actually cross to stays a
    /// stop - it is the only controller route to the other tab - and the one you are
    /// on, whose press does nothing, is not.
    /// </summary>
    internal static Control.FocusModeEnum FocusModeFor(bool current) =>
        current ? Control.FocusModeEnum.None : Control.FocusModeEnum.All;

    private static void AddLock(NSettingsTab tab, string tooltip)
    {
        tab.TooltipText = tooltip;
        var bounds = LockBounds(tab.Size);
        var lockArt = ResourceLoader.Load<Texture2D>(LockImage);
        if (lockArt is null)
        {
            // Said once, and the tooltip still carries the reason
            MegaCrit.Sts2.Core.Logging.Log.Warn(
                $"[{RunmobileMod.ModId}] this build has no '{LockImage}' lock image; the Community " +
                "tab says why it is locked in its tooltip only.", 2);
            return;
        }

        // Ignore intrinsic size before assigning art or Godot keeps the texture's minimum
        tab.AddChild(new TextureRect
        {
            Name = "Lock",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = lockArt,
            Position = bounds.Position,
            Size = bounds.Size,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
    }
}

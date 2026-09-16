using System.Text.RegularExpressions;
using Godot;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What the library borrows from the game's own scenes, pinned without the game: the
/// lock's place on a tab, which is the stats screen's, and which run-history icon each
/// floor kind wears.
/// </summary>
public sealed class LibraryNativeFurnitureTests
{
    /// <summary>
    /// The lock was the stats screen's 109-square centred on the tab, which drew it
    /// across the middle of "Community". It is now a small badge - half the
    /// run-history entry's marker - hung off the tab's top-right corner, above the band
    /// the word is drawn in, and the word's box is left whole: narrowing it for the
    /// lock made the tab's own auto-size draw "Community" smaller than "Mine".
    /// </summary>
    [Theory]
    [InlineData(256f, 90f)]
    [InlineData(205f, 72f)]
    [InlineData(170f, 60f)]
    public void TheLockHangsOffTheTabsCornerClearOfTheWord(float width, float height)
    {
        var tab = new Vector2(width, height);
        var lockBounds = LibraryTabArt.LockBounds(tab);
        var maxFontSize = NativeScenes.Here.State is NativeScenes.AvailabilityState.Run
            ? NativeScenes.DesignSize(NativeScenes.Read(LibraryTabArt.Scene)!, "Label")
            : 32;

        Assert.Equal(FloorMarkerArt.EntryIconSide / 2f, lockBounds.Size.X, 3);
        Assert.Equal(lockBounds.Size.X, lockBounds.Size.Y);
        Assert.True(lockBounds.Position.Y < 0f && lockBounds.End.Y > 0f, "the lock hangs off the top edge");
        Assert.True(lockBounds.Position.X < width && lockBounds.End.X > width, "the lock hangs off the right edge");
        Assert.True(
            lockBounds.End.Y <= (height - maxFontSize) / 2f,
            $"the lock ends at {lockBounds.End.Y}, over the band a {maxFontSize} word is centred in");
    }

    [Fact]
    public void TheTabKeepsItsScenesProportions()
    {
        Assert.Equal(256f / 90f, LibraryTabArt.Aspect);
        Assert.Equal("res://scenes/screens/settings_tab.tscn", LibraryTabArt.Scene);
    }

    /// <summary>
    /// The image the stats scene itself binds to its Achievements tab's <c>Lock</c>
    /// node, read out of this build's own pack. Existence is not the check - both
    /// files named <c>submenu_lock.png</c> exist - so the scene is asked which one it
    /// draws, and a lock that is any other file fails here by name.
    /// </summary>
    [NativeSceneFact]
    public void TheLockIsTheImageTheStatsScreenBindsToItsAchievementsTab()
    {
        const string Scene = "res://scenes/screens/stats_screen/stats_screen.tscn";
        var scene = NativeScenes.Read(Scene);
        Assert.NotNull(scene);

        var lockNode = NativeScenes.Properties(scene, "Tabs/TabContainer/Achievements/Lock");
        Assert.NotNull(lockNode);
        var texture = Regex.Match(lockNode["texture"], "^ExtResource\\(\"([^\"]+)\"\\)$");
        Assert.True(texture.Success, $"the stats scene's lock is bound to '{lockNode["texture"]}', not an ext_resource");

        var bound = NativeScenes.ExternalResources(scene)[texture.Groups[1].Value];
        Assert.True(bound == LibraryTabArt.LockImage, $"the stats scene draws '{bound}' on its Achievements tab; the Community tab names '{LibraryTabArt.LockImage}'");
    }

    /// <summary>The game's own tabs are no controller stop, and the one you are on
    /// would be a stop on a control whose press does nothing. The other one stays a
    /// stop because this band has no shoulder hotkeys to reach it with.</summary>
    [Fact]
    public void OnlyTheTabAPressCrossesToTakesFocus()
    {
        Assert.Equal(Control.FocusModeEnum.None, LibraryTabArt.FocusModeFor(current: true));
        Assert.Equal(Control.FocusModeEnum.All, LibraryTabArt.FocusModeFor(current: false));
    }

    /// <summary>The names <c>ImageHelper.GetRoomIconPath</c> produces for the run-history
    /// screen, and no icon at all for a kind nothing established - the game draws an
    /// unknown room and an event as the same question mark, and the strip must not say
    /// "event" about a floor nothing was read from.</summary>
    [Theory]
    [InlineData(FloorKind.Combat, "monster")]
    [InlineData(FloorKind.Shop, "shop")]
    [InlineData(FloorKind.Rest, "rest_site")]
    [InlineData(FloorKind.Event, "event")]
    [InlineData(FloorKind.Treasure, "treasure")]
    [InlineData(FloorKind.Unknown, null)]
    public void EachFloorKindWearsTheRunHistoryScreensOwnIcon(FloorKind kind, string? icon)
    {
        Assert.Equal(icon, FloorMarkerArt.IconName(kind));
        if (icon is null) return;

        Assert.Equal($"res://images/ui/run_history/{icon}.png", FloorMarkerArt.IconPath(icon));
        Assert.Equal($"res://images/ui/run_history/{icon}_outline.png", FloorMarkerArt.OutlinePath(icon));
    }

    /// <summary>
    /// The three numbers the strip and the tab lock size themselves by, read off the
    /// run-history entry's own scene: a 60 box, an icon 4 units over it, drawn at 0.7.
    /// A build that redraws its history entry moves the marker here by name.
    /// </summary>
    [NativeSceneFact]
    public void TheMarkerSizeIsTheRunHistoryEntrysOwn()
    {
        var scene = NativeScenes.Read(FloorMarkerArt.EntryScene);
        Assert.NotNull(scene);

        var root = NativeScenes.RootProperties(scene);
        Assert.NotNull(root);
        Assert.Equal($"Vector2({FloorMarkerArt.EntryBox:0}, {FloorMarkerArt.EntryBox:0})", root["custom_minimum_size"]);

        var icon = NativeScenes.Properties(scene, "Icon");
        Assert.NotNull(icon);
        Assert.Equal($"{FloorMarkerArt.EntryIconOverhang:0.0}", icon["offset_right"]);
        Assert.Equal($"Vector2({FloorMarkerArt.EntryIconScale:0.0}, {FloorMarkerArt.EntryIconScale:0.0})", icon["scale"]);
    }

    /// <summary>
    /// The relic holder the run-history screen flows its relics with, read off its own
    /// scene: a box, and an icon inset from it by its own offsets. The rows' pitch is
    /// that box because the flow puts nothing between holders, and that is held here
    /// too - a build that separates them moves the rows by name.
    /// </summary>
    [NativeSceneFact]
    public void TheRelicRowsAreTheRunHistoryHoldersOwnSizeAndFlow()
    {
        var (box, icon) = RelicHolderOnThisBuild();
        Assert.Equal(LibraryPaneArtTests.MinePane().RelicBox, box);
        Assert.Equal(LibraryPaneArtTests.MinePane().RelicIcon, icon);

        var history = NativeScenes.Read(GameText.Declarations.Single(d => d.Role == NativeTextRole.Fact).Scene);
        Assert.NotNull(history);
        var flow = NativeScenes.Properties(
            history, "ScreenContents/Content/Container/RelicHistory/MarginContainer/RelicsContainer");
        Assert.NotNull(flow);
        Assert.Equal("0", flow["theme_override_constants/h_separation"]);
        Assert.Equal("0", flow["theme_override_constants/v_separation"]);
    }

    /// <summary>
    /// The Mine pane holds a run of dozens of relics on this build, from the sizes the
    /// shipped scenes actually set: each role the pane draws with, the relic holder,
    /// the panel's ribbon and the popup's body. The arithmetic is
    /// <see cref="LibraryPaneArtTests.AssertMinePaneFits"/>'s; this is what holds it to
    /// a build rather than to numbers copied into a test.
    /// </summary>
    [NativeSceneFact]
    public void TheMinePaneFitsOnThisBuildWithDozensOfRelics()
    {
        var (box, icon) = RelicHolderOnThisBuild();
        var sizes = new LibraryPaneArtTests.NativePaneSizes(
            RowTitle: NativeScenes.DesignSize(RoleScene(NativeTextRole.RowTitle), RoleNode(NativeTextRole.RowTitle)),
            Secondary: NativeScenes.DesignSize(RoleScene(NativeTextRole.Secondary), RoleNode(NativeTextRole.Secondary)),
            Fact: NativeScenes.DesignSize(RoleScene(NativeTextRole.Fact), RoleNode(NativeTextRole.Fact)),
            CardCaption: NativeScenes.DesignSize(RoleScene(NativeTextRole.CardCaption), RoleNode(NativeTextRole.CardCaption)),
            Numeral: NativeScenes.DesignSize(RoleScene(NativeTextRole.FloorNumeral), RoleNode(NativeTextRole.FloorNumeral)),
            Body: NativeScenes.DesignSize(RoleScene(NativeTextRole.PopupBody), RoleNode(NativeTextRole.PopupBody)),
            Ribbon: Ribbon(NativeScenes.Vector2(NativeScenes.RootProperties(RoleScene(NativeTextRole.ButtonCaption))!["custom_minimum_size"])),
            BodyTop: float.Parse(
                NativeScenes.Properties(RoleScene(NativeTextRole.PopupBody), RoleNode(NativeTextRole.PopupBody))!["offset_top"],
                System.Globalization.CultureInfo.InvariantCulture),
            RelicBox: box,
            RelicIcon: icon,
            Arrow: ArrowWidthOnThisBuild());

        Assert.Equal(LibraryPaneArtTests.MinePane(), sizes);
        LibraryPaneArtTests.AssertMinePaneFits(sizes);
    }

    private static Vector2 Ribbon((float X, float Y) size) => new(size.X, size.Y);

    /// <summary>The paginator's arrow column, read the way <see cref="NativePaginatorArt.ArrowWidth"/>
    /// reads the live scene: the control's right offset less its left.</summary>
    private static float ArrowWidthOnThisBuild()
    {
        var paginator = NativeScenes.Read(NativePaginatorArt.PaginatorScene);
        Assert.NotNull(paginator);
        var arrow = NativeScenes.Properties(paginator, NativePaginatorArt.ArrowNode);
        Assert.NotNull(arrow);
        return float.Parse(arrow["offset_right"], System.Globalization.CultureInfo.InvariantCulture)
            - float.Parse(arrow["offset_left"], System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The strip's page arrows stand in the column the game's own paginator gives its
    /// arrows, which is narrower than a marker's column, and the strip's marker between
    /// them is the run-history entry's own; on this build's 456-wide pane that is four
    /// floors a page, and a fifth from 475. A build that redraws its paginator moves the
    /// arrows here by name.
    /// </summary>
    [NativeSceneFact]
    public void TheStripsPageArrowsTakeThePaginatorsOwnColumn()
    {
        var sizes = LibraryPaneArtTests.MinePane() with { Arrow = ArrowWidthOnThisBuild() };
        var layout = LibraryPaneArt.LayoutStrip(50, sizes.Width, sizes.Arrow, anchor: 0, sizes.Numeral);

        Assert.Equal(LibraryPaneArtTests.MinePane().Arrow, sizes.Arrow);
        Assert.Equal(sizes.Arrow, layout.Arrow);
        Assert.Equal(LibraryPaneArt.NativeCell, layout.Cell, 3);
        Assert.Equal(4, layout.Count);
        Assert.Equal(5, LibraryPaneArt.LayoutStrip(50, 475f, sizes.Arrow, anchor: 0, sizes.Numeral).Count);
    }

    /// <summary>The holder's box and its icon's side, read the way
    /// <see cref="RelicHolderArt"/> reads the live scene: the root's minimum size, less
    /// the icon's left offset in and its right offset back.</summary>
    private static (float Box, float Icon) RelicHolderOnThisBuild()
    {
        var holder = NativeScenes.Read(RelicHolderArt.HolderScene);
        Assert.NotNull(holder);
        var box = NativeScenes.Vector2(NativeScenes.RootProperties(holder)!["custom_minimum_size"]).X;

        var relicScene = NativeScenes.InstancedScene(holder, "Relic");
        Assert.NotNull(relicScene);
        var relicText = NativeScenes.Read(relicScene);
        Assert.NotNull(relicText);
        var iconNode = NativeScenes.Properties(relicText, "Icon");
        Assert.NotNull(iconNode);
        var left = float.Parse(iconNode["offset_left"], System.Globalization.CultureInfo.InvariantCulture);
        var right = float.Parse(iconNode["offset_right"], System.Globalization.CultureInfo.InvariantCulture);
        return (box, box - left + right);
    }

    private static string RoleScene(NativeTextRole role) =>
        NativeScenes.Read(GameText.Declarations.Single(d => d.Role == role).Scene)!;

    private static string RoleNode(NativeTextRole role) =>
        GameText.Declarations.Single(d => d.Role == role).Node.TrimStart('%');

    /// <summary>A process with no game has none of the icons, and the strip then draws
    /// the mod's own glyph rather than nothing.</summary>
    [Fact]
    public void WithoutTheGameTheMarkerIsNullRatherThanAWrongPicture()
    {
        Assert.Null(FloorMarkerArt.Of(FloorKind.Combat));
        Assert.Null(FloorMarkerArt.Of(FloorKind.Unknown));
    }
}

public sealed class GlyphFillTests
{
    /// <summary>A filled circle is built from a closed arc, whose last point repeats
    /// its first; a polygon with that repeat fails to triangulate in the client and
    /// draws nothing. The fill drops it.</summary>
    [Fact]
    public void AFilledCircleDoesNotRepeatItsFirstVertex()
    {
        var disc = LibraryGlyphArt.Of(LibraryGlyph.Disc, "Disc", 32f, Colors.White);
        var polygon = disc.GetChildren().OfType<Polygon2D>().Single();

        var first = polygon.Polygon[0];
        var last = polygon.Polygon[^1];
        var gap = MathF.Sqrt(((first.X - last.X) * (first.X - last.X)) + ((first.Y - last.Y) * (first.Y - last.Y)));
        Assert.True(gap > 1f, $"the last vertex is {gap} from the first; a repeat within rounding draws nothing");
        Assert.True(polygon.Polygon.Length >= 20);
    }
}

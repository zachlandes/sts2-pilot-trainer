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
    /// <summary>The stats scene puts a 109-square lock centred on a 256 by 90
    /// Achievements tab, overhanging its top and bottom. The same proportions at any
    /// tab size.</summary>
    [Theory]
    [InlineData(256f, 90f)]
    [InlineData(205f, 72f)]
    public void TheLockSitsWhereTheStatsScreenPutsIt(float width, float height)
    {
        var lockBounds = LibraryTabArt.LockBounds(new Vector2(width, height));

        Assert.Equal(lockBounds.Size.X, lockBounds.Size.Y);
        Assert.Equal(height * 109f / 90f, lockBounds.Size.Y, 3);
        Assert.Equal(width / 2f, lockBounds.Position.X + (lockBounds.Size.X / 2f), 3);
        Assert.Equal(height / 2f, lockBounds.Position.Y + (lockBounds.Size.Y / 2f), 3);
        Assert.True(lockBounds.Position.Y < 0f);
    }

    [Fact]
    public void TheTabKeepsItsScenesProportions()
    {
        Assert.Equal(256f / 90f, LibraryTabArt.Aspect);
        Assert.Equal("res://scenes/screens/settings_tab.tscn", LibraryTabArt.Scene);
    }

    /// <summary>The packed image, not the one under <c>images/ui</c> that shares its
    /// basename: that one is the chained lock a submenu card wears, and it shipped on
    /// the Community tab once because a path can be right in every part but one.</summary>
    [Fact]
    public void TheLockIsThePackedStatsScreenImage()
    {
        Assert.Equal("res://images/packed/main_menu/submenu_lock.png", LibraryTabArt.LockImage);
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

    /// <summary>Every scene and image the library borrows is one this build ships. A
    /// path that is not there is a tab that refuses or a marker that silently draws the
    /// mod's own glyph, and neither is a test failure without this.</summary>
    [NativeSceneFact]
    public void EverySceneAndImageTheLibraryBorrowsIsOneThisBuildShips()
    {
        var borrowed = new List<string> { LibraryTabArt.Scene, LibraryTabArt.LockImage };
        foreach (var kind in Enum.GetValues<FloorKind>())
        {
            if (FloorMarkerArt.IconName(kind) is not { } icon) continue;
            borrowed.Add(FloorMarkerArt.IconPath(icon));
            borrowed.Add(FloorMarkerArt.OutlinePath(icon));
        }

        var missing = borrowed.Where(path => !NativeScenes.Ships(path)).ToList();
        Assert.True(missing.Count == 0, "this build does not ship: " + string.Join(", ", missing));
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

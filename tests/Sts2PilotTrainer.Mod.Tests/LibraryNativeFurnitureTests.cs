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

public sealed class LibraryNoticeTests
{
    /// <summary>The lock's plate is sized by its sentences wrapped to the column, not
    /// by counting newlines: a three-line notice in a narrow column stands taller
    /// than three lines.</summary>
    [Fact]
    public void ThePlateGrowsWithTheWrappedSentences()
    {
        var style = new GameTextStyle(null, 20);
        var wide = LibraryScreen.WrappedHeight(LibraryCopy.CommunityOffNotice, 4000f, style);
        var narrow = LibraryScreen.WrappedHeight(LibraryCopy.CommunityOffNotice, 400f, style);

        Assert.Equal(3 * 1.45f * 20, wide, 2);
        Assert.True(narrow > wide, "a narrow column wraps the sentences onto more lines");
    }

    /// <summary>A short window is a window the library still opens on. The plate is
    /// drawn to what is left over the rows' own floor, so <c>ScreenPage.For</c> never
    /// receives fewer places than a page holds and refuses the browser.</summary>
    [Fact]
    public void AShortColumnKeepsTheRowsAPageAndClipsThePlate()
    {
        const float step = 40f;
        const float wanted = 200f;
        var available = (3 * step) + (wanted / 2f);

        var notice = LibraryScreen.NoticeBudget(available, step, wanted, pinned: 0);
        var places = (int)Math.Floor((available - notice) / step);

        Assert.True(notice > 0f, "a short window still gets guidance, clipped");
        Assert.True(notice < wanted, "the plate gave way to the rows");
        Assert.True(places >= ScreenPage.MinimumPerPage, $"{places} places is under a page");
        Assert.True(ScreenPage.For(rows: 12, places, page: 0).Pages > 1);
    }

    /// <summary>Room enough and the plate stands at the height its sentences want.</summary>
    [Fact]
    public void ATallColumnGivesThePlateWhatItAskedFor()
    {
        Assert.Equal(200f, LibraryScreen.NoticeBudget(2000f, 40f, 200f, pinned: 0));
    }

    /// <summary>A pinned row is on every page, so it is counted into the floor the
    /// notice may not eat: <c>ScreenPage.For</c> takes it off the places first.</summary>
    [Fact]
    public void APinnedRowIsCountedIntoTheFloor()
    {
        const float step = 40f;
        var available = (4 * step) + 200f;

        var notice = LibraryScreen.NoticeBudget(available, step, wanted: 200f, pinned: 1);
        var places = (int)Math.Floor((available - notice) / step);

        Assert.Equal(4, places);
        var page = ScreenPage.For(rows: 12, places, page: 0, pinned: 1);
        Assert.True(page.Pages > 1);
    }

    /// <summary>Too short even for a page of rows and there is no notice at all: the
    /// plate takes nothing the rows were already short of.</summary>
    [Fact]
    public void AColumnUnderAPageGetsNoPlate()
    {
        Assert.Equal(0f, LibraryScreen.NoticeBudget(2 * 40f, 40f, wanted: 200f, pinned: 0));
    }

    /// <summary>A plate with no room for a line of its own text does not go up, and the
    /// body says the absence instead: the player reads one explanation, never none.
    /// </summary>
    [Fact]
    public void AColumnTooShortForThePlatePutsTheReasonOverTheTabs()
    {
        const float textSize = 20f;
        var allotted = LibraryScreen.NoticeBudget(2 * 40f, 40f, wanted: 200f, pinned: 0);
        var drawn = LibraryScreen.NoticeDraws(allotted, textSize);
        var page = Page(body: null, fallback: "no sharing service");

        Assert.False(drawn);
        Assert.Equal("no sharing service", page.BodyWith(drawn));
    }

    /// <summary>And where it does go up the body says nothing, so the absence is not
    /// explained twice.</summary>
    [Fact]
    public void APlateThatGoesUpSilencesTheBody()
    {
        const float textSize = 20f;
        var allotted = LibraryScreen.NoticeBudget(2000f, 40f, wanted: 200f, pinned: 0);
        var drawn = LibraryScreen.NoticeDraws(allotted, textSize);

        Assert.True(drawn);
        Assert.Null(Page(body: null, fallback: "no sharing service").BodyWith(drawn));
    }

    /// <summary>The fallback joins what the body was already saying rather than
    /// replacing it: a failed fetch and a missing service are two facts.</summary>
    [Fact]
    public void TheFallbackJoinsAnExistingBody()
    {
        var page = Page(body: "the index could not be fetched", fallback: "no sharing service");

        Assert.Equal("the index could not be fetched\nno sharing service", page.BodyWith(false));
        Assert.Equal("the index could not be fetched", page.BodyWith(true));
    }

    /// <summary>A plate with exactly one line of room is a plate: the boundary is where
    /// its own text fits inside the padding and the gap under it.</summary>
    [Theory]
    [InlineData(20f * 3.2f, true)]
    [InlineData((20f * 3.2f) - 0.1f, false)]
    public void OneLineOfTextIsTheLeastAPlateSays(float allotted, bool drawn)
    {
        Assert.Equal(drawn, LibraryScreen.NoticeDraws(allotted, 20f));
    }

    private static LibraryPage Page(string? body, string? fallback) =>
        new(
            "Runmobile", Tabs: [], ListHeader: null, Rows: [], Pane: null, BackLabel: "Back",
            Body: body, ListNotice: "the plate", ListNoticeFallback: fallback);
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

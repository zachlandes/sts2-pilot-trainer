namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// What one page of a row column holds.
///
/// The rule being pinned is that nothing focusable is ever placed past the panel: the
/// library's rows are absolutely positioned siblings, so a column drawn whole would put
/// most of a fifty-run list off the screen and let a controller walk down into rows
/// nobody can see. Every case here asserts <see cref="ScreenPage.Drawn"/> against the
/// room that was measured, because that count is exactly what reaches the screen.
/// </summary>
public sealed class ScreenPageTests
{
    [Fact]
    public void AColumnThatFitsIsOnePageAndSpendsNothingOnGettingAround()
    {
        var page = ScreenPage.For(rows: 5, perPage: 5, page: 0);

        Assert.Equal(0, page.First);
        Assert.Equal(5, page.Count);
        Assert.False(page.HasPrevious);
        Assert.False(page.HasNext);
        Assert.Equal(1, page.Pages);
        Assert.Equal(5, page.Drawn);
    }

    /// <summary>The defect this exists for: a column longer than the panel draws that
    /// page's rows and no others.</summary>
    [Fact]
    public void AColumnLongerThanThePanelDrawsOnlyItsFirstPage()
    {
        var page = ScreenPage.For(rows: 50, perPage: 8, page: 0);

        Assert.Equal(0, page.First);
        Assert.Equal(6, page.Count);
        Assert.False(page.HasPrevious);
        Assert.True(page.HasNext);
        Assert.True(page.Drawn <= 8);
    }

    /// <summary>Next lands on the rows that follow, and back again on the ones before,
    /// so walking the pages walks the column once.</summary>
    [Fact]
    public void TheNextPageHoldsTheRowsAfterThisOne()
    {
        var first = ScreenPage.For(rows: 50, perPage: 8, page: 0);
        var second = ScreenPage.For(rows: 50, perPage: 8, page: 1);

        Assert.Equal(first.First + first.Count, second.First);
        Assert.True(second.HasPrevious);
        Assert.True(second.HasNext);
        Assert.Equal(first.First, ScreenPage.For(rows: 50, perPage: 8, page: 0).First);
    }

    /// <summary>Every page of every shape stays inside what was measured, which is the
    /// property that keeps a focusable row from being drawn past the panel.</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(20)]
    public void NoPageEverDrawsMoreThanFits(int perPage)
    {
        for (var rows = 0; rows <= 60; rows++)
        {
            var pages = ScreenPage.For(rows, perPage, page: 0).Pages;
            for (var index = 0; index < pages; index++)
            {
                var page = ScreenPage.For(rows, perPage, index);
                Assert.True(
                    page.Drawn <= perPage,
                    $"{rows} rows at {perPage} a page drew {page.Drawn} on page {index}.");
            }
        }
    }

    /// <summary>Every row of the column is on exactly one page, so paging hides nothing
    /// and repeats nothing.</summary>
    [Theory]
    [InlineData(3)]
    [InlineData(7)]
    public void EveryRowIsOnExactlyOnePage(int perPage)
    {
        const int rows = 41;
        var seen = new List<int>();
        var pages = ScreenPage.For(rows, perPage, page: 0).Pages;
        for (var index = 0; index < pages; index++)
        {
            var page = ScreenPage.For(rows, perPage, index);
            seen.AddRange(Enumerable.Range(page.First, page.Count));
        }

        Assert.Equal(Enumerable.Range(0, rows), seen);
    }

    /// <summary>
    /// A column that shortened under a player - a recording removed while the browser
    /// was open - shows them the last page rather than nothing.
    /// </summary>
    [Fact]
    public void APageBeyondTheEndIsTheLastPage()
    {
        var page = ScreenPage.For(rows: 20, perPage: 8, page: 99);

        Assert.Equal(page.Pages - 1, page.Index);
        Assert.True(page.HasPrevious);
        Assert.False(page.HasNext);
    }

    /// <summary>A panel measured to hold less than a paged page needs is refused rather
    /// than drawn over, the way an unmeasurable row height already is. The measurement
    /// is the only thing that decides: nothing raises it to the minimum, because a page
    /// of three in room for two and a half is a row over the panel's own ribbons.</summary>
    [Fact]
    public void APanelTooSmallToPageIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenPage.For(rows: 10, perPage: 2, page: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenPage.For(rows: 10, perPage: 0, page: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenPage.For(rows: -1, perPage: 8, page: 0));
    }

    /// <summary>
    /// The browser's way to its other tab is a row, and a column longer than one page
    /// keeps it on every page rather than dropping it after the first - which is what
    /// pinning is for.
    /// </summary>
    [Fact]
    public void APinnedRowIsOnEveryPageOfALongColumn()
    {
        const int perPage = 8;
        var pages = ScreenPage.For(rows: 50, perPage, page: 0, pinned: 1).Pages;

        Assert.True(pages > 1);
        for (var index = 0; index < pages; index++)
        {
            var page = ScreenPage.For(rows: 50, perPage, index, pinned: 1);
            Assert.Equal(1, page.Pinned);
            Assert.True(page.Drawn <= perPage, $"page {index} drew {page.Drawn}.");
        }
    }

    /// <summary>Pinning shrinks the page rather than pushing its last row past the
    /// panel: the pinned row, the page's own rows and Previous and Next together are
    /// what was measured to fit.</summary>
    [Theory]
    [InlineData(4, 1)]
    [InlineData(8, 1)]
    [InlineData(8, 2)]
    [InlineData(20, 3)]
    public void NoPageEverDrawsMoreThanFitsBesideItsPinnedRows(int perPage, int pinned)
    {
        for (var rows = 0; rows <= 60; rows++)
        {
            var pages = ScreenPage.For(rows, perPage, page: 0, pinned).Pages;
            for (var index = 0; index < pages; index++)
            {
                var page = ScreenPage.For(rows, perPage, index, pinned);
                Assert.True(
                    page.Drawn <= perPage,
                    $"{rows} rows at {perPage} a page with {pinned} pinned drew {page.Drawn} " +
                    $"on page {index}.");
            }
        }
    }

    /// <summary>Paging still walks the column once when a row is pinned to every
    /// page.</summary>
    [Fact]
    public void EveryRowIsOnExactlyOnePageBesideAPinnedRow()
    {
        const int rows = 41;
        var seen = new List<int>();
        var pages = ScreenPage.For(rows, perPage: 8, page: 0, pinned: 1).Pages;
        for (var index = 0; index < pages; index++)
        {
            var page = ScreenPage.For(rows, perPage: 8, index, pinned: 1);
            seen.AddRange(Enumerable.Range(page.First, page.Count));
        }

        Assert.Equal(Enumerable.Range(0, rows), seen);
    }

    [Fact]
    public void ASelectedLaterRowOpensOnThePageThatContainsIt()
    {
        var page = ScreenPage.Containing(rows: 50, perPage: 8, row: 37, pinned: 2);

        Assert.True(page.Index > 0);
        Assert.InRange(37, page.First, page.First + page.Count - 1);
        Assert.True(page.Drawn <= 8);
    }

    /// <summary>A panel with room for a page, but not once a row is pinned to it, is
    /// refused rather than drawing the pinned row over the ribbons.</summary>
    [Fact]
    public void APanelWithNoRoomLeftBesideAPinnedRowIsRefused()
    {
        Assert.True(ScreenPage.For(rows: 10, perPage: 3, page: 0).Drawn <= 3);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ScreenPage.For(rows: 10, perPage: 3, page: 0, pinned: 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ScreenPage.For(rows: 10, perPage: 8, page: 0, pinned: -1));
    }
}

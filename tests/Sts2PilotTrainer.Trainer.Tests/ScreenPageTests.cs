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
    /// than drawn over, the way an unmeasurable row height already is.</summary>
    [Fact]
    public void APanelTooSmallToPageIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenPage.For(rows: 10, perPage: 2, page: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScreenPage.For(rows: -1, perPage: 8, page: 0));
    }
}

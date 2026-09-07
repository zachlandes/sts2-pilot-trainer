namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// Which of a column's rows one page of it holds.
///
/// The library's screens are the game's own popup with a column of rows laid out in it,
/// and the popup is a fixed size: a run of a real length proves dozens of floors, and a
/// player may keep fifty runs. Drawn as one column those rows go past the panel and off
/// the screen, and a controller walks down into rows nobody can see. This is where that
/// stops.
///
/// <para>It is arithmetic over counts and nothing else. How many rows fit is measured
/// from the game's own nodes by the drawing, the list itself is whatever the browser and
/// the run view returned - neither of them knows a page exists - and this says which
/// slice of it is on screen. Keeping it here rather than inside the drawing is what lets
/// the rule be asserted without a scene tree.</para>
///
/// <para><b>Two of the page's places belong to the way out of it.</b> A paged column
/// spends one on Previous and one on Next, and they are rows like any other, so the
/// count a page draws - its own rows and its navigation - never exceeds what was
/// measured to fit. That is the whole invariant: <see cref="Drawn"/> is at most
/// <c>perPage</c>, so nothing focusable is ever placed past the panel.</para>
/// </summary>
/// <param name="First">The index in the whole column of this page's first row.</param>
/// <param name="Count">How many of the column's own rows this page holds.</param>
/// <param name="HasPrevious">Whether there is a page before this one.</param>
/// <param name="HasNext">Whether there is a page after it.</param>
/// <param name="Index">Which page this is, from zero.</param>
/// <param name="Pages">How many pages the column takes.</param>
/// <param name="Pinned">How many of the page's places are spent on rows that are not
/// paged at all - drawn above the slice on every page, the way Previous and Next are
/// drawn below it.</param>
public sealed record ScreenPage(
    int First, int Count, bool HasPrevious, bool HasNext, int Index, int Pages, int Pinned = 0)
{
    /// <summary>The fewest rows a page may hold. Three, because a paged page spends two
    /// places on Previous and Next and a page that could hold nothing else would be a
    /// column a player cannot walk down.</summary>
    public const int MinimumPerPage = 3;

    /// <summary>How many controls this page puts on screen: the rows pinned to every
    /// page, its own rows, and whichever of Previous and Next it offers.</summary>
    public int Drawn => Pinned + Count + (HasPrevious ? 1 : 0) + (HasNext ? 1 : 0);

    /// <summary>The page containing <paramref name="row"/> after the column has been
    /// divided under the same rules as <see cref="For"/>.</summary>
    public static ScreenPage Containing(int rows, int perPage, int row, int pinned = 0)
    {
        if (row < 0 || row >= rows)
        {
            throw new ArgumentOutOfRangeException(nameof(row), row, "The selected row must be in the column.");
        }

        var first = For(rows, perPage, page: 0, pinned);
        if (first.Pages == 1) return first;
        var size = perPage - pinned - 2;
        return For(rows, perPage, row / size, pinned);
    }

    /// <summary>
    /// The page of a column of <paramref name="rows"/> rows in a panel that fits
    /// <paramref name="perPage"/> of them.
    ///
    /// A column that fits is one page and spends nothing on navigation. A column that
    /// does not is cut into equal pages with the two places reserved, so a page's rows
    /// are always the ones following the page before it. <paramref name="page"/> is
    /// clamped rather than refused: a column that shortened under a player - a recording
    /// removed while the browser was open - should show them the last page rather than
    /// nothing.
    ///
    /// <paramref name="pinned"/> rows are on every page and are counted against
    /// <paramref name="perPage"/> before anything else, so pinning one shrinks the page
    /// rather than pushing its last row past what was measured. A panel with no room
    /// left for a page is refused here, which is the only place that answer is given.
    /// </summary>
    public static ScreenPage For(int rows, int perPage, int page, int pinned = 0)
    {
        if (rows < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rows), rows, "A column has no negative length.");
        }

        if (pinned < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pinned), pinned, "A page pins no negative number of rows.");
        }

        var places = perPage - pinned;
        if (places < MinimumPerPage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(perPage),
                perPage,
                $"A page of {perPage} places with {pinned} pinned to it leaves fewer than the " +
                $"{MinimumPerPage} rows a page holds.");
        }

        if (rows <= places) return new ScreenPage(0, rows, false, false, 0, 1, pinned);

        var size = places - 2;
        var pages = ((rows - 1) / size) + 1;
        var index = Math.Clamp(page, 0, pages - 1);
        var first = index * size;
        return new ScreenPage(
            first, Math.Min(size, rows - first), index > 0, index < pages - 1, index, pages, pinned);
    }
}

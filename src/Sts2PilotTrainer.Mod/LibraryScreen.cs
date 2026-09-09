using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// One row on a library screen: what it says, whether it can be pressed, and what
/// pressing it does.
/// </summary>
/// <param name="Note">The design's second line: what the row does, said under the
/// label. A note and a reason are different sentences and a row can carry both - the
/// note says where pressing it goes and the reason says why it cannot be pressed.</param>
/// <param name="Reason">Said under the row when it is refused. The design's rule is
/// that a refused row on this surface states its reason, unlike the transport's
/// refused menu rows, because here the reason is a fact about the recording rather
/// than a state that clears itself in seconds.</param>
/// <param name="Pinned">A row that is not paged: it is drawn above the page's own rows
/// on every page. A column's own navigation belongs to the column, so a screen whose
/// way to somewhere else is a row would otherwise lose it on page one.</param>
/// <param name="MarkTooltip">The sentence behind the hollow-eye mark at the row's end,
/// or null for a row with no mark. The mark says the recording's line for this row's
/// fight has been shown this sitting, and its tooltip is the whole explanation.</param>
/// <param name="Glyph">A glyph drawn at the row's end - the disclosure chevron on a
/// row that drills in, the bin on one that removes. Null draws none.</param>
/// <param name="Selected">Whether this row is the one the pane is showing. Drawn, so a
/// player can see which of a list of fifty the pane belongs to.</param>
/// <param name="ActReached">The recording-derived value under the Act reached column.</param>
/// <param name="Trailing">What the row says at its right-hand end, in the teal that
/// means "something you did": the Last floor replayed column.</param>
internal sealed record ScreenRow(
    string Label, bool Enabled, Action Press, string? Note = null, string? Reason = null,
    bool Pinned = false, string? MarkTooltip = null, LibraryGlyph? Glyph = null,
    bool Selected = false, string? ActReached = null, string? Trailing = null,
    string? Character = null, bool Heading = false);

/// <summary>One parchment tab across the top band.</summary>
internal sealed record ScreenTab(string Label, bool Current, Action Press);

internal sealed record ScreenFilter(string Label, bool Checked, Action Toggle);

/// <summary>
/// The right-hand pane: the selected run, in the run-history screen's own language.
/// </summary>
/// <param name="Relics">Relic model ids, in the order the run found them. Drawn with
/// the game's own icons where this build has them.</param>
/// <param name="Deck">The deck as card tiles, or null where the recording says nothing
/// about it - which draws no tiles rather than an empty deck.</param>
/// <param name="Strip">The run strip, full width under the identity.</param>
/// <param name="Verdict">The eligibility green line. Null draws none.</param>
/// <param name="Facts">Lines under the strip, in the supporting colour: what a fight
/// is against, the health it starts at, the floor pane's one sentence.</param>
/// <param name="Plate">The flat plate under the pane. Rows, drawn as ribbons.</param>
/// <param name="Ribbon">The pane's own way forward - "Open the run" - or null.</param>
/// <param name="VerdictPassed">Whether the verdict is affirmative.</param>
internal sealed record ScreenPane(
    string Heading,
    string? Subtitle,
    IReadOnlyList<string> Relics,
    IReadOnlyList<DeckTile>? Deck,
    int? DeckCount,
    IReadOnlyList<RunStripCell> Strip,
    Action<int>? SelectFloor,
    int? StripPage,
    Action<int>? SelectStripPage,
    string? Verdict,
    IReadOnlyList<string> Facts,
    IReadOnlyList<ScreenRow> Plate,
    ScreenRow? Ribbon,
    bool VerdictPassed = true);

/// <summary>
/// What one library screen is: the band, the list, and the pane beside it.
///
/// Handed whole to <see cref="LibraryScreen.Show"/> rather than as eleven arguments,
/// because a screen is one thing and the drawing has to lay its parts out against each
/// other.
/// </summary>
/// <param name="Back">Where the ribbon goes. Null closes the library, which is what the
/// screen a player entered on does; a screen opened from another one hands in the way
/// back to it, so the surface is one place a player moves around in rather than a
/// sequence they fall out of the bottom of.</param>
/// <param name="Page">Which page of a list too long for the panel to draw. Zero is the
/// first, and a caller never passes anything else - the Previous and Next rows re-show
/// this same screen at the page either side.</param>
internal sealed record LibraryPage(
    string Title,
    IReadOnlyList<ScreenTab> Tabs,
    string? ListHeader,
    IReadOnlyList<ScreenRow> Rows,
    ScreenPane? Pane,
    string BackLabel,
    ScreenFilter? ListFilter = null,
    Action? Back = null,
    Action<string>? CodeSubmitted = null,
    string CodePlaceholder = "",
    string? Body = null,
    string? ListFooter = null,
    string? ListFooterTooltip = null,
    int Page = 0,
    int? SelectedRow = null,
    Action<long, string, string, string, bool>? ShareSubmitted = null);

/// <summary>
/// The library's parchment furniture: the game's own panel, with the design's screen
/// laid out inside it.
///
/// Built out of the game's materials for the reason <see cref="TrainerScreen"/> gives
/// for the eligibility screen - the panel, the parchment, the fonts, the ribbons,
/// their hotkeys and their controller focus are MegaCrit's, and a hand-built lookalike
/// is a worse copy that also drifts. The tabs and the rows are duplicates of the
/// panel's own second ribbon, so a tab is a real game button with the game's hover,
/// focus and press behaviour rather than a rectangle this mod drew.
///
/// <para><b>What is the mod's own is what the game has not got.</b> The run strip's
/// cells, the ticks and rings on them, the crowns and the chevron are
/// <see cref="LibraryGlyphArt"/>'s, drawn in the same family as the transport's for the
/// reason that family exists: the game has no free-standing glyph at a strip cell's
/// size to borrow. Where the game does have the art - a relic's icon, a card's
/// portrait - this draws the game's through <see cref="ModelArt"/>.</para>
///
/// <para><b>The duplicated rows give up their hotkeys.</b> The panel's ribbons bind
/// confirm and cancel through <c>NHotkeyManager</c>, which is a stack: five rows all
/// binding confirm would mean the key pressing whichever was pushed last rather than
/// the one a player is looking at. Each duplicate is disconnected the moment it is
/// added, so the keys stay with the two ribbons and the rows are reached the way every
/// other list in this game is reached - by focus.</para>
///
/// <para>The library expands the native parchment inside the game's logical canvas.
/// Its content area lies between the body label and the bottom ribbons; the panes
/// split that area and pagination measures the space remaining after the header
/// and footer. The paper and its controls share the same expanded bounds.</para>
///
/// <para><b>A list longer than the panel is paged, never drawn past it.</b> The rows
/// are absolutely positioned siblings rather than a scrolling list, so a column of
/// fifty would put most of itself off the screen and leave a controller walking down
/// into rows nobody can see. <see cref="ScreenPage"/> decides which slice is on screen
/// and the last two places of a paged page go to Previous and Next; focus is joined
/// across what is drawn and nothing else, so it cannot reach a row that is not there.
/// Paging is presentation: <c>RunBrowser</c> and <c>RunView</c> return every row they
/// always did, and this decides what a player is looking at.</para>
///
/// It composes nothing and decides nothing. What the rows say is
/// <c>Sts2PilotTrainer.Trainer</c>'s answer; this puts them on screen.
/// </summary>
internal static class LibraryScreen
{
    private static long surface;
    private static NGenericPopup? currentPopup;

    /// <summary>The popup scene's own name for its content, resolved by the game's
    /// code the same way.</summary>
    private const string VerticalPopupPath = "VerticalPopup";

    /// <summary>Labels the ribbons carry until this mod replaces them. Never shown:
    /// the game's initialisers take a localized string and a DLL-only mod contributes
    /// no localization table, so the game's own confirm and cancel keys stand in and
    /// the text is then set directly.</summary>
    private static LocString PlaceholderConfirm => new("main_menu_ui", "GENERIC_POPUP.confirm");

    /// <summary>The smallest the popup's own body is allowed to shrink itself to, as a
    /// share of the size it stands at when the screen opens - which is the size its own
    /// scene was designed to, because the floor is set before any text is put in it. A
    /// body nobody can read is not a list, and the floor is now the game's own size
    /// rather than a number of ours.</summary>

    /// <summary>How far apart rows sit, as a multiple of a row's own measured
    /// height.</summary>
    private const float RowStep = 1.12f;

    /// <summary>The same, on a screen whose rows carry a second line: the note is drawn
    /// in the gap, so the gap has to hold it.</summary>
    private const float NotedRowStep = 1.55f;

    /// <summary>How far under a row its second line sits, as a multiple of the row's own
    /// height. A row occupies its whole height, so anything under one clears it.</summary>
    private const float NoteDrop = 1.12f;

    /// <summary>How far a line of Runmobile's own text stands off the one before it, as
    /// a multiple of its own size. The header over the list, the filter under it and the
    /// footer beneath are each one line and a breath.</summary>
    private const float LineStep = 1.6f;

    /// <summary>The same for a control rather than a line: a checkbox is a box round a
    /// word and stands taller than the word does.</summary>
    private const float ControlStep = 2.3f;

    /// <summary>How wide the list is, as a share of the content area. The pane takes
    /// the rest less the seam, so the two are one measurement rather than two.</summary>
    private const float ListShare = 0.54f;

    /// <summary>The gap between the panes, where the divider runs.</summary>
    private const float SeamShare = 0.03f;

    /// <summary>
    /// Shows one screen, replacing whatever this module had up.
    ///
    /// One ribbon at the foot. There is no affirmative ribbon there because everything
    /// on this surface is pressed where it is: the pane has its own "Open the run"
    /// ribbon, and a panel-level ribbon meaning "the one you highlighted" would be a
    /// second way to press the thing already under the cursor.
    /// </summary>
    internal static long Show(LibraryPage page)
    {
        var shownSurface = Interlocked.Increment(ref surface);
        NGenericPopup? popup = null;
        NModalContainer? container = null;
        var added = false;
        try
        {
            popup = NGenericPopup.Create()
                ?? throw new InvalidOperationException("This process has no popup surface.");
            container = NModalContainer.Instance
                ?? throw new InvalidOperationException("This process has no modal container.");
            container.Add(popup, showBackstop: true);
            added = true;

            var content = popup.GetNode<NVerticalPopup>(VerticalPopupPath);
            ExpandForLibrary(content);
            var label = content.BodyLabel();
            label.BbcodeEnabled = true;
            label.ScrollActive = true;
            content.SetText(page.Title, page.Body ?? string.Empty);
            ReservePageRoom(content, page);

            ShareFields? share = null;
            content.InitYesButton(
                PlaceholderConfirm,
                _ =>
                {
                    if (share is not null)
                    {
                        page.ShareSubmitted!(
                            shownSurface, share.Name.Text, share.Description.Text,
                            share.DisplayName.Text, share.Consent.ButtonPressed);
                    }
                    else if (page.Back is { } back)
                    {
                        Callable.From(() => Reopen(back)).CallDeferred();
                    }
                    else
                    {
                        Dismiss();
                    }
                });
            content.YesButton.SetText(page.ShareSubmitted is null ? page.BackLabel : LibraryCopy.ShareSubmit);
            if (page.ShareSubmitted is null)
            {
                content.HideNoButton();
            }
            else
            {
                content.InitNoButton(
                    PlaceholderConfirm,
                    _ =>
                    {
                        if (page.Back is { } back) Callable.From(() => Reopen(back)).CallDeferred();
                        else Dismiss();
                    });
                content.NoButton.SetText(page.BackLabel);
            }

            var area = AreaOf(content);
            if (page.ShareSubmitted is not null) share = AddShareFields(content, area);
            var bandControls = new List<Control>();
            var band = AddBand(content, page, area, bandControls);

            var listWidth = page.Pane is null ? area.Size.X : area.Size.X * ListShare;
            var first = AddRows(
                content, page, new Rect2(area.Position.X, band, listWidth, area.End.Y - band));

            if (page.Pane is { } pane)
            {
                var seam = area.Size.X * SeamShare;
                var paneAt = new Rect2(
                    area.Position.X + listWidth + seam,
                    band,
                    area.Size.X - listWidth - seam,
                    area.End.Y - band);
                AddDivider(content, area.Position.X + listWidth + (seam / 2f), band, area.End.Y);

                // The list keeps focus where it has any: it is the way in, and a screen
                // that opened on the pane's own ribbon would put the way forward ahead
                // of the choice it acts on. The pane is reached rightward from the list,
                // which is where it is.
                var paneFocus = LibraryPaneArt.Add(content, pane, paneAt);
                if (first is not null && paneFocus is not null)
                {
                    first.FocusNeighborRight = paneFocus.GetPath();
                    paneFocus.FocusNeighborLeft = first.GetPath();
                }

                first ??= paneFocus;
            }

            var bodyFocus = first ?? share?.Name ?? (Control)content.YesButton;
            JoinBand(bandControls, bodyFocus, content);
            if (share is not null) JoinForm(share, content);

            // Deferred: adding the modal updates the game's active screen context,
            // which decides what is focused. Grabbing focus before that has finished
            // loses it, and a screen nothing can be reached on from a controller is a
            // screen half the players cannot use.
            var focus = first ?? share?.Name ?? bandControls.FirstOrDefault() ?? (Control)content.YesButton;
            Callable.From(() => focus.GrabFocus()).CallDeferred();
            currentPopup = popup;
        }
        catch (Exception ex)
        {
            try
            {
                if (added) container!.Clear();
                else popup?.QueueFree();
            }
            catch (Exception cleanup)
            {
                Log.Error(
                    $"[{RunmobileMod.ModId}] could not clear a failed library modal: " +
                    $"{cleanup.GetType().Name}: {cleanup.Message}", 2);
            }

            Log.Error(
                $"[{RunmobileMod.ModId}] could not show a library screen: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }

        return shownSurface;
    }

    /// <summary>
    /// The game's own confirm, for the one thing on this surface that cannot be undone.
    ///
    /// Two ribbons and no rows: it asks one question and the panel's own affirmative and
    /// cancel keys answer it, which is what every confirm in this game is. Removing a
    /// run comes through here, so the mod never takes a player's recording on a single
    /// press.
    ///
    /// <paramref name="onCancel"/> is where Keep them goes, and it is never null in
    /// practice: a confirm reached from a screen returns to that screen, so cancelling
    /// leaves a player where they were rather than on an empty modal.
    /// </summary>
    internal static void Confirm(
        string title, string body, string confirmLabel, string cancelLabel,
        Action onConfirm, Action onCancel)
    {
        NGenericPopup? popup = null;
        NModalContainer? container = null;
        var added = false;
        try
        {
            popup = NGenericPopup.Create()
                ?? throw new InvalidOperationException("This process has no popup surface.");
            container = NModalContainer.Instance
                ?? throw new InvalidOperationException("This process has no modal container.");
            container.Add(popup, showBackstop: true);
            added = true;

            var content = popup.GetNode<NVerticalPopup>(VerticalPopupPath);
            content.BodyLabel().BbcodeEnabled = true;
            content.SetText(title, body);

            // Deferred for the reason every other ribbon here is: the panel takes itself
            // down when a ribbon is pressed, and a screen shown inside the handler would
            // be the one it took down.
            content.InitYesButton(
                PlaceholderConfirm, _ => Callable.From(() => Reopen(onConfirm)).CallDeferred());
            content.InitNoButton(
                PlaceholderConfirm, _ => Callable.From(() => Reopen(onCancel)).CallDeferred());
            content.YesButton.SetText(confirmLabel);
            content.NoButton.SetText(cancelLabel);

            // The way out is what a confirm opens on. This is the one screen in the
            // library where the safe answer is the one under the cursor.
            Callable.From(() => content.NoButton.GrabFocus()).CallDeferred();
        }
        catch (Exception ex)
        {
            try
            {
                if (added) container!.Clear();
                else popup?.QueueFree();
            }
            catch (Exception cleanup)
            {
                Log.Error(
                    $"[{RunmobileMod.ModId}] could not clear a failed confirm: " +
                    $"{cleanup.GetType().Name}: {cleanup.Message}", 2);
            }

            Log.Error(
                $"[{RunmobileMod.ModId}] could not ask about '{title}': " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>Expands the retail popup to hold the library's two-pane furniture.</summary>
    private static void ExpandForLibrary(NVerticalPopup content)
    {
        // Keep the one-column popup centred while making room for both panes
        var oldSize = content.Size;
        var newSize = new Vector2(1200f, 850f);
        // The native root is a TextureRect; keep-aspect leaves the paper behind its controls
        content.Set("expand_mode", (int)TextureRect.ExpandModeEnum.IgnoreSize);
        content.Set("stretch_mode", (int)TextureRect.StretchModeEnum.Scale);
        content.Position -= (newSize - oldSize) / 2f;
        content.Size = newSize;

        var header = content.GetNode<Control>("Header");
        header.Size = new Vector2(newSize.X, header.Size.Y);

        var description = content.BodyLabel();
        description.Position = new Vector2(70f, description.Position.Y);
        description.Size = new Vector2(
            newSize.X - 140f,
            newSize.Y - description.Position.Y - content.YesButton.Size.Y - 12f);

        var buttonY = newSize.Y - content.YesButton.Size.Y;
        content.NoButton.Position = new Vector2(70f, buttonY);
        content.YesButton.Position = new Vector2(newSize.X - content.YesButton.Size.X - 70f, buttonY);
    }

    /// <summary>Bounds the popup's scrolling body above the library furniture.</summary>
    private static void ReservePageRoom(NVerticalPopup content, LibraryPage page)
    {
        if (page.Rows.Count == 0 && page.Pane is null && page.Tabs.Count == 0) return;

        var label = content.BodyLabel();
        label.FitContent = false;
        label.CustomMinimumSize = new Vector2(label.CustomMinimumSize.X, 0f);
        label.Size = new Vector2(
            label.Size.X,
            string.IsNullOrEmpty(page.Body) ? 0f : Math.Min(label.Size.Y, content.NoButton.Size.Y));
    }

    /// <summary>
    /// The content area, measured from the panel's own nodes: what lies between the
    /// body label and the ribbons, as wide as the label.
    ///
    /// Everything this screen positions is placed inside it, so a build that moves the
    /// panel's label or its ribbons moves the whole screen with them.
    /// </summary>
    private static Rect2 AreaOf(NVerticalPopup content)
    {
        var label = content.BodyLabel();
        var top = label.Position.Y + label.Size.Y;
        var bottom = content.NoButton.Position.Y;
        if (bottom <= top)
        {
            throw new InvalidOperationException(
                "This build's panel leaves no room between its body and its ribbons, so a library screen " +
                "cannot be laid out in it.");
        }

        return new Rect2(label.Position.X, top, label.Size.X, bottom - top);
    }

    /// <summary>
    /// The top band: the tabs, and the run-code field beside them.
    ///
    /// Returns where the band ends, which is where the panes begin. A screen with
    /// neither draws nothing and the panes start at the top of the area.
    /// </summary>
    private static float AddBand(
        NVerticalPopup content, LibraryPage page, Rect2 area, List<Control> focusable)
    {
        if (page.Tabs.Count == 0 && page.CodeSubmitted is null) return area.Position.Y;

        var prototype = content.NoButton;
        var height = prototype.Size.Y;
        var tabWidth = Math.Min(prototype.Size.X, area.Size.X * 0.17f);
        var at = area.Position.X;
        foreach (var tab in page.Tabs)
        {
            var button = Duplicate(content, prototype, $"RunmobileTab{tab.Label}");
            if (button is null) continue;

            button.SetText(tab.Label);
            button.Position = new Vector2(at, area.Position.Y);
            button.Size = new Vector2(tabWidth, button.Size.Y);
            button.CustomMinimumSize = button.Size;
            button.Visible = true;

            // The current tab is not pressable: pressing the tab you are on would
            // rebuild the screen you are looking at, and a control that does nothing
            // visible is a control a player presses twice. It is drawn at full weight
            // and the others are dimmed, which is what says which one you are on.
            if (tab.Current)
            {
                button.MouseFilter = Control.MouseFilterEnum.Ignore;
                button.FocusMode = Control.FocusModeEnum.None;
            }
            else
            {
                button.Modulate = new Color(1f, 1f, 1f, 0.6f);
                var press = tab.Press;
                button.Connect(
                    NClickableControl.SignalName.Released,
                    Callable.From<NButton>(_ => Reopen(press)));
                focusable.Add(button);
            }

            at += tabWidth * 1.04f;
        }

        if (page.CodeSubmitted is { } submitted)
        {
            at += 90f;
            focusable.Add(AddCodeField(
                content,
                page.CodePlaceholder,
                submitted,
                new Rect2(at, area.Position.Y, Math.Max(tabWidth, area.End.X - at), height)));
        }

        return area.Position.Y + (height * 1.65f);
    }

    /// <summary>The line between the panes. It runs the whole height of the content
    /// area, up through the list's own header, because the header sits over the list
    /// rather than over the screen.</summary>
    private static void AddDivider(NVerticalPopup content, float x, float top, float bottom)
    {
        var line = new Line2D
        {
            Name = "RunmobilePaneDivider",
            Width = 1.5f,
            DefaultColor = LibraryPalette.Line with { A = 0.5f },
            Points = [new Vector2(x, top), new Vector2(x, bottom)],
        };
        content.AddChild(line);
    }

    /// <summary>
    /// Lays the list out in the left pane and joins it into one focus column.
    ///
    /// Returns the first row a player can press, which is where focus goes: a screen
    /// that opened with the Back ribbon highlighted would put the way out ahead of the
    /// way in. Null when nothing is pressable, and the caller then focuses the pane's
    /// own ribbon or the panel's.
    ///
    /// How many rows fit is the space the pane leaves, in steps - measured, like
    /// everything else positioned here. The measurement is the only thing that decides
    /// how many rows are drawn: room for fewer than a page is refused by
    /// <see cref="ScreenPage.For"/> rather than drawn over.
    /// </summary>
    private static Control? AddRows(NVerticalPopup content, LibraryPage page, Rect2 at)
    {
        var rows = page.Rows;
        var prototype = content.NoButton;
        var noted = rows.Any(row => SupportingText(row) is not null);
        var step = prototype.Size.Y * (noted ? NotedRowStep : RowStep);
        if (step <= 0f)
        {
            throw new InvalidOperationException(
                "This build's popup ribbon has no measurable height, so a row column cannot be laid out.");
        }

        var note = content.BodyText();
        var top = at.Position.Y;
        if (page.ListHeader is { Length: > 0 } header)
        {
            AddLine(content, header, new Vector2(at.Position.X, top), at.Size.X, LibraryPalette.Muted, note);
            top += note.Size * LineStep;
        }

        var placed = new List<Control>();
        if (page.ListFilter is { } filter)
        {
            var checkbox = AddFilter(content, filter, new Vector2(at.Position.X, top), at.Size.X, note);
            placed.Add(checkbox);
            top += note.Size * ControlStep;
        }

        var bottom = at.End.Y;
        if (page.ListFooter is { Length: > 0 } footer)
        {
            // Under the list, where the design puts it: it is about what the list is
            // not showing, so it sits with the list rather than in the band. The whole
            // reason is the tooltip, because a numeral is what a player scans and a
            // sentence is what they ask for.
            bottom -= note.Size * LineStep;
            AddLine(
                content, footer, new Vector2(at.Position.X, bottom), at.Size.X,
                LibraryPalette.Muted, note, tooltip: page.ListFooterTooltip);
        }

        var fits = (int)Math.Floor((bottom - top) / step);
        var pinned = rows.Where(row => row.Pinned).ToList();
        var paged = rows.Where(row => !row.Pinned).ToList();
        var requestedPage = page.Page;
        if (page.SelectedRow is { } selectedRow &&
            selectedRow >= 0 && selectedRow < rows.Count && !rows[selectedRow].Pinned)
        {
            var pagedRow = rows.Take(selectedRow).Count(row => !row.Pinned);
            requestedPage = ScreenPage.Containing(
                paged.Count, fits, pagedRow, pinned.Count).Index;
        }

        var slice = ScreenPage.For(paged.Count, fits, requestedPage, pinned.Count);
        var drawn = new List<ScreenRow>(pinned);
        drawn.AddRange(paged.Skip(slice.First).Take(slice.Count));
        if (slice.HasPrevious)
        {
            var previous = slice.Index - 1;
            drawn.Add(new ScreenRow(
                LibraryCopy.PreviousPage, Enabled: true,
                () => Show(page with { Page = previous, SelectedRow = null })));
        }

        if (slice.HasNext)
        {
            var next = slice.Index + 1;
            drawn.Add(new ScreenRow(
                LibraryCopy.NextPage, Enabled: true,
                () => Show(page with { Page = next, SelectedRow = null })));
        }

        var rowTop = top;
        for (var index = 0; index < drawn.Count; index++)
        {
            var button = AddRow(
                content, drawn[index], $"RunmobileRow{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                new Vector2(at.Position.X, rowTop), at.Size.X);
            rowTop += drawn[index].Heading ? note.Size * ControlStep : step;
            if (button is not null) placed.Add(button);
        }

        JoinColumn(placed, content);
        return placed.FirstOrDefault(control => control.FocusMode != Control.FocusModeEnum.None);
    }

    private static CheckBox AddFilter(
        NVerticalPopup content, ScreenFilter filter, Vector2 at, float width, GameTextStyle text)
    {
        var height = text.Size * ControlStep;
        var checkbox = new CheckBox
        {
            Name = "RunmobileListFilter",
            Text = filter.Label,
            ButtonPressed = filter.Checked,
            Position = at,
            Size = new Vector2(width, height),
            CustomMinimumSize = new Vector2(width, height),
            FocusMode = Control.FocusModeEnum.All,
        };
        text.ApplyTo(checkbox);
        checkbox.Pressed += () => Navigate(filter.Label, filter.Toggle);
        content.AddChild(checkbox);
        return checkbox;
    }

    /// <summary>
    /// One row, as a duplicate of the panel's own second ribbon.
    ///
    /// Widened to the pane it is in rather than left at the ribbon's own width, because
    /// a list row carries a run's identity and a ribbon is sized for a word.
    /// </summary>
    internal static Control? AddRow(
        NVerticalPopup content, ScreenRow row, string name, Vector2 at, float width)
    {
        if (row.Heading)
        {
            AddLine(content, row.Label, at, width, LibraryPalette.Muted, content.HeaderText());
            return null;
        }

        var button = Duplicate(content, content.NoButton, name, width);
        if (button is null) return null;

        button.Position = at;
        button.Size = new Vector2(width, button.Size.Y);
        button.CustomMinimumSize = new Vector2(width, button.Size.Y);
        foreach (var part in new[] { "%Visuals", "%Image", "%Outline" })
        {
            var visual = button.GetNode<Control>(part);
            visual.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            if (visual is TextureRect texture)
            {
                texture.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
                texture.StretchMode = TextureRect.StretchModeEnum.Scale;
            }
        }
        var rowLabel = button.GetNode<Control>("%Label");
        rowLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopLeft);
        rowLabel.Position = new Vector2(row.Character is null ? 12f : 64f, 0f);
        rowLabel.Size = new Vector2(
            row.Character is null ? width - 24f : width * 0.42f - 64f, button.Size.Y);
        var text = row.Label;
        button.SetText(text);

        // Refit after Godot propagates the widened ribbon into its child label
        // SetTextAutoSize otherwise measures the narrow prototype and shrinks actions
        Callable.From(() => button.SetText(text)).CallDeferred();
        button.Visible = true;

        if (row.Enabled)
        {
            var pressed = row;
            button.Connect(
                NClickableControl.SignalName.Released,
                Callable.From<NButton>(_ => Press(pressed)));
        }
        else
        {
            // Refused rows keep their place and their reason and take no input.
            // Absent is the only state that hides a row here, and a row that is
            // there is a row a player has been told about.
            button.Modulate = new Color(1f, 1f, 1f, 0.45f);
            button.MouseFilter = Control.MouseFilterEnum.Ignore;
            button.FocusMode = Control.FocusModeEnum.None;
        }

        if (row.Character is { Length: > 0 } character) AddCharacterPortrait(button, character);
        if (row.Selected) AddSelectionRing(button);
        if (SupportingText(row) is { } supporting) AddNote(content, button, supporting);
        if (row.ActReached is { Length: > 0 } actReached) AddActReached(content, button, actReached);
        if (row.Trailing is { Length: > 0 } trailing) AddTrailing(content, button, trailing);
        if (row.Glyph is { } glyph) AddGlyph(button, glyph, LibraryPalette.Muted);
        if (row.MarkTooltip is { Length: > 0 } tooltip) AddMark(button, tooltip);
        return button;
    }

    /// <summary>Keeps a refusal's reason under the row rather than shrinking its action.</summary>
    private static string? SupportingText(ScreenRow row) => (row.Note, row.Reason) switch
    {
        ({ Length: > 0 } note, { Length: > 0 } reason) => $"{note} · {reason}",
        ({ Length: > 0 } note, _) => note,
        (_, { Length: > 0 } reason) => reason,
        _ => null,
    };

    /// <summary>Draws the retail character-select portrait beside a run row.</summary>
    private static void AddCharacterPortrait(Control row, string character)
    {
        if (ModelArt.CharacterPortrait(character) is not { } texture) return;

        var size = row.Size.Y * 0.72f;
        var portrait = new TextureRect
        {
            Name = $"{row.Name}Character",
            Texture = texture,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(size * 0.24f, (row.Size.Y - size) / 2f),
            Size = new Vector2(size, size),
        };
        row.AddChild(portrait);
    }

    /// <summary>
    /// The ring round the list row the pane is showing.
    ///
    /// Drawn rather than written, and drawn rather than coloured: a row tinted to say
    /// "selected" would compete with the teal that means "something you did", and the
    /// two would then be one visual language saying two things.
    /// </summary>
    private static void AddSelectionRing(Control row)
    {
        var ring = new Line2D
        {
            Name = $"{row.Name}Selected",
            Width = 1.8f,
            DefaultColor = LibraryPalette.Ink with { A = 0.75f },
            Points =
            [
                new Vector2(0, 0), new Vector2(row.Size.X, 0),
                new Vector2(row.Size.X, row.Size.Y), new Vector2(0, row.Size.Y), new Vector2(0, 0),
            ],
        };
        row.AddChild(ring);
    }

    /// <summary>
    /// The hollow-eye mark at a row's end: shown this sitting.
    ///
    /// The transport's own glyph, so the run view and the post-fight choice say
    /// "shown" in one shape. It is parented to the row so it moves and dims with it,
    /// and it passes the mouse through rather than stopping it, so the row keeps the
    /// press and the engine's own tooltip still reads the sentence on hover.
    /// </summary>
    private static void AddMark(Control row, string tooltip)
    {
        var size = row.Size.Y * 0.5f;
        var mark = TransportGlyphArt.Of(TransportGlyph.Reveal, "ShownThisSitting", size, LibraryPalette.Muted);
        mark.MouseFilter = Control.MouseFilterEnum.Pass;
        mark.TooltipText = tooltip;
        mark.Position = new Vector2(row.Size.X - size - (size * 0.6f), (row.Size.Y - size) / 2f);
        row.AddChild(mark);
    }

    /// <summary>A glyph at the row's end, inboard of where the shown-this-sitting mark
    /// goes, so a row that carries both keeps them apart.</summary>
    private static void AddGlyph(Control row, LibraryGlyph glyph, Color colour)
    {
        var size = row.Size.Y * 0.5f;
        var art = LibraryGlyphArt.Of(glyph, $"{row.Name}Glyph", size, colour);
        art.Position = new Vector2(row.Size.X - (size * 2.4f), (row.Size.Y - size) / 2f);
        row.AddChild(art);
    }

    /// <summary>
    /// What a row says at its right-hand end, in the teal that means "something you
    /// did".
    ///
    /// The Last floor replayed column. Teal because it is the one fact on a row that is
    /// about the person rather than the run, and blank rather than zero where they have
    /// loaded nothing - the caller passes null and no label is drawn.
    /// </summary>
    private static void AddActReached(NVerticalPopup content, Control row, string text)
    {
        var style = content.BodyText();
        var label = new Label
        {
            Name = $"{row.Name}ActReached",
            Text = text,
            Position = new Vector2(row.Size.X * 0.42f, (row.Size.Y - style.Size) / 2f),
            CustomMinimumSize = new Vector2(row.Size.X * 0.18f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        style.ApplyTo(label);
        label.AddThemeColorOverride("font_color", LibraryPalette.Muted);
        row.AddChild(label);
    }

    private static void AddTrailing(NVerticalPopup content, Control row, string text)
    {
        var style = content.BodyText();
        var label = new Label
        {
            Name = $"{row.Name}Trailing",
            Text = text,
            Position = new Vector2(row.Size.X * 0.6f, (row.Size.Y - style.Size) / 2f),
            CustomMinimumSize = new Vector2(row.Size.X * 0.3f, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        style.ApplyTo(label);
        label.AddThemeColorOverride("font_color", LibraryPalette.Teal);
        row.AddChild(label);
    }

    /// <summary>
    /// Draws a row's second line under it.
    ///
    /// Under rather than appended, because the design's rows are a label and a line
    /// beneath it: a note run into the label with a dash would read as part of what the
    /// row is called. It is a plain label wearing the game's font and takes no input, so
    /// the row above it keeps the whole press and the whole focus.
    ///
    /// Parented to the row rather than to the panel, so the dimming a refused row
    /// carries reaches it. A note at full brightness under a row at 45% would read
    /// louder than the refusal it belongs to, and a refused row would stop looking
    /// refused at note level.
    /// </summary>
    private static void AddNote(NVerticalPopup content, Control row, string note)
    {
        var style = content.BodyText();
        var label = new Label
        {
            Name = $"{row.Name}Note",
            Text = note,
            Position = new Vector2(0f, row.Size.Y * NoteDrop),
            CustomMinimumSize = new Vector2(row.Size.X, 0f),
            Size = new Vector2(row.Size.X, style.Size * 1.3f),
            ClipText = true,
            TooltipText = note,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        style.ApplyTo(label);
        label.AddThemeColorOverride("font_color", LibraryPalette.Muted);
        row.AddChild(label);
    }

    /// <summary>
    /// One line of the mod's own text on the parchment, wearing the game's font.
    /// Returns where the next line goes.
    /// </summary>
    /// <param name="tooltip">The sentence behind the line, or null. A line normally
    /// ignores the mouse so whatever is under it keeps the press; one that carries a
    /// tooltip takes the mouse back, because a tooltip needs it. The hidden-run numeral
    /// is the only line on this surface that does - a numeral is what a player scans and
    /// the sentence is what they ask for.</param>
    internal static float AddLine(
        NVerticalPopup content, string text, Vector2 at, float width, Color colour, GameTextStyle style,
        HorizontalAlignment alignment = HorizontalAlignment.Left, string? tooltip = null)
    {
        var lineCount = text.Count(character => character == '\n') + 1;
        var height = style.Size * 1.3f * lineCount;
        var label = new Label
        {
            Name = "RunmobileLine",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Text = text,
            Position = at,
            CustomMinimumSize = new Vector2(width, 0f),
            Size = new Vector2(width, height),
            MouseFilter = tooltip is { Length: > 0 }
                ? Control.MouseFilterEnum.Stop
                : Control.MouseFilterEnum.Ignore,
            TooltipText = tooltip ?? string.Empty,
            HorizontalAlignment = alignment,
        };

        style.ApplyTo(label);
        label.AddThemeColorOverride("font_color", colour);
        content.AddChild(label);
        return at.Y + (style.Size * 1.45f * lineCount);
    }

    /// <summary>
    /// A duplicate of the panel's own ribbon, with its hotkeys given up.
    ///
    /// Before anything else it may do: a duplicate registers the ribbon's own confirm
    /// or cancel key on the manager's stack, and the last one pushed would answer for
    /// every row on the screen.
    /// </summary>
    internal static NPopupYesNoButton? Duplicate(
        NVerticalPopup content, NPopupYesNoButton prototype, string name, float? ribbonWidth = null)
    {
        const int duplicateFlags =
            (int)(Node.DuplicateFlags.Groups | Node.DuplicateFlags.Scripts |
                  Node.DuplicateFlags.UseInstantiation);
        if (prototype.Duplicate(duplicateFlags) is not NPopupYesNoButton button) return null;

        button.Name = name;
        // The retail button caches its visual nodes and materials in _Ready
        if (ribbonWidth is { } width) LibraryRibbonArt.ReplaceTextures(button, width);
        content.AddChild(button);
        button.DisconnectHotkeys();
        button.FocusMode = Control.FocusModeEnum.All;
        button.IsYes = false;
        return button;
    }

    /// <summary>
    /// Adds the run-code field: a stock Godot line edit wearing the game's own font.
    ///
    /// Stock rather than the game's <c>NSearchBar</c>, which is a scene this mod has no
    /// path to instantiate outside the screens that already hold one. The font and size
    /// are the popup's own body copy, the way every other line on this parchment is, so
    /// the field reads as part of the game rather than as Godot's default sans.
    /// </summary>
    private sealed record ShareFields(
        LineEdit Name, LineEdit Description, LineEdit DisplayName, CheckBox Consent)
    {
        internal IReadOnlyList<Control> Controls => [Name, Description, DisplayName, Consent];
    }

    private static ShareFields AddShareFields(NVerticalPopup content, Rect2 at)
    {
        var height = content.NoButton.Size.Y;
        var text = content.BodyText();

        LineEdit Field(string name, string placeholder, int? limit, int step)
        {
            var field = new LineEdit
            {
                Name = name,
                PlaceholderText = placeholder,
                Position = new Vector2(at.Position.X, at.Position.Y + (height * step)),
                CustomMinimumSize = new Vector2(at.Size.X, height),
                FocusMode = Control.FocusModeEnum.All,
            };
            if (limit is { } maximum) field.MaxLength = maximum;
            text.ApplyTo(field);
            content.AddChild(field);
            return field;
        }

        var name = Field(
            "RunmobileShareName", LibraryCopy.ShareNameField,
            ShareSubmission.NameCharacterLimit, 0);
        var description = Field(
            "RunmobileShareDescription", LibraryCopy.ShareDescriptionField,
            ShareSubmission.DescriptionCharacterLimit, 1);
        var displayName = Field(
            "RunmobileShareDisplayName", LibraryCopy.ShareDisplayNameField, null, 2);
        var consent = new CheckBox
        {
            Name = "RunmobileShareConsent",
            Text = LibraryCopy.ShareConsent,
            Position = new Vector2(at.Position.X, at.Position.Y + (height * 3)),
            CustomMinimumSize = new Vector2(at.Size.X, height),
            FocusMode = Control.FocusModeEnum.All,
        };
        text.ApplyTo(consent);
        content.AddChild(consent);
        return new ShareFields(name, description, displayName, consent);
    }

    private static LineEdit AddCodeField(
        NVerticalPopup content, string placeholder, Action<string> submitted, Rect2 at)
    {
        var field = new LineEdit
        {
            Name = "RunmobileCodeField",
            PlaceholderText = placeholder,
            Position = at.Position,
            CustomMinimumSize = at.Size,
            FocusMode = Control.FocusModeEnum.All,
        };

        content.BodyText().ApplyTo(field);
        content.AddChild(field);
        field.Connect(
            LineEdit.SignalName.TextSubmitted,
            Callable.From<string>(text => submitted(text)));
        return field;
    }

    /// <summary>
    /// Takes down whatever this module has up.
    ///
    /// The container holds one screen: opening the next while this one is up would
    /// leave the two stacked with the old one taking the input. Every way in calls
    /// this first, rows and the run-code field alike, and this is the only place that
    /// knows which container it is.
    /// </summary>
    internal static bool IsCurrent(long shownSurface) =>
        Interlocked.Read(ref surface) == shownSurface &&
        currentPopup is { } popup &&
        GodotObject.IsInstanceValid(popup) &&
        popup.IsInsideTree();

    internal static void Invalidate(long shownSurface)
    {
        if (Interlocked.Read(ref surface) != shownSurface) return;
        Interlocked.Increment(ref surface);
        currentPopup = null;
    }

    internal static void Dismiss()
    {
        Interlocked.Increment(ref surface);
        currentPopup = null;
        NModalContainer.Instance?.Clear();
    }

    /// <summary>Shows the screen a ribbon returns to, with whatever is up taken down
    /// first, and says so rather than leaving a player on a screen that did not
    /// change.</summary>
    private static void Reopen(Action back) => Navigate("go back", back);

    internal static void Navigate(string label, Action action)
    {
        Dismiss();
        Callable.From(() => Act(label, action)).CallDeferred();
    }

    /// <summary>Runs a row's action with the modal taken down first.</summary>
    internal static void Press(ScreenRow row) => Navigate(row.Label, row.Press);

    /// <summary>
    /// Runs one of this screen's actions and says what failed rather than letting it
    /// reach Godot's own dispatch.
    ///
    /// Every press here is reached from a signal, so a throw would leave the dispatch
    /// holding it with the screen already taken down and nothing on screen saying what
    /// happened.
    /// </summary>
    private static void Act(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not act on '{what}': " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Wires the rows into a column a controller can walk, with the ribbons below it.
    ///
    /// Refused rows are skipped rather than stepped over: a control that takes no focus
    /// is not a stop on the way down, and pointing a neighbour at one would strand a
    /// player mid-column.
    /// </summary>
    private static void JoinForm(ShareFields share, NVerticalPopup content)
    {
        var controls = share.Controls;
        for (var index = 0; index < controls.Count; index++)
        {
            controls[index].FocusNeighborTop =
                (index > 0 ? controls[index - 1] : controls[index]).GetPath();
            controls[index].FocusNeighborBottom = index + 1 < controls.Count
                ? controls[index + 1].GetPath()
                : content.YesButton.GetPath();
        }

        content.YesButton.FocusNeighborTop = controls[^1].GetPath();
        content.NoButton.FocusNeighborTop = controls[^1].GetPath();
    }

    private static void JoinBand(
        IReadOnlyList<Control> controls, Control body, NVerticalPopup content)
    {
        if (controls.Count == 0) return;

        for (var index = 0; index < controls.Count; index++)
        {
            controls[index].FocusNeighborLeft =
                controls[index > 0 ? index - 1 : 0].GetPath();
            controls[index].FocusNeighborRight =
                controls[index + 1 < controls.Count ? index + 1 : index].GetPath();
            controls[index].FocusNeighborBottom = body.GetPath();
        }

        body.FocusNeighborTop = controls[0].GetPath();
        if (ReferenceEquals(body, content.YesButton))
        {
            content.YesButton.FocusNeighborTop = controls[^1].GetPath();
        }
    }

    private static void JoinColumn(IReadOnlyList<Control> rows, NVerticalPopup content)
    {
        var focusable = rows.Where(row => row.FocusMode != Control.FocusModeEnum.None).ToList();
        for (var index = 0; index < focusable.Count; index++)
        {
            focusable[index].FocusNeighborTop =
                (index > 0 ? focusable[index - 1] : focusable[index]).GetPath();
            focusable[index].FocusNeighborBottom = index + 1 < focusable.Count
                ? focusable[index + 1].GetPath()
                : content.YesButton.GetPath();
        }

        if (focusable.Count > 0) content.YesButton.FocusNeighborTop = focusable[^1].GetPath();
    }
}

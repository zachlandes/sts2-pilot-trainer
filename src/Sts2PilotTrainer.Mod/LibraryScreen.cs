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
internal sealed record ScreenRow(
    string Label, bool Enabled, Action Press, string? Note = null, string? Reason = null,
    bool Pinned = false, string? MarkTooltip = null);

/// <summary>
/// The one way this module puts anything on screen: the game's own modal popup, with
/// a body of text and a column of rows under it.
///
/// Built out of the game's furniture for the reason <see cref="TrainerScreen"/> gives
/// for the eligibility screen - the panel, the fonts, the ribbons, their hotkeys and
/// their controller focus are MegaCrit's, and a hand-built lookalike is a worse copy
/// that also drifts. The rows are duplicates of the popup's own second ribbon, so a
/// row is a real game button with the game's hover, focus and press behaviour rather
/// than a rectangle this mod drew.
///
/// <para><b>The duplicated rows give up their hotkeys.</b> The popup's ribbons bind
/// confirm and cancel through <c>NHotkeyManager</c>, which is a stack: five rows all
/// binding confirm would mean the key pressing whichever was pushed last rather than
/// the one a player is looking at. Each duplicate is disconnected the moment it is
/// added, so the keys stay with the two ribbons and the rows are reached the way every
/// other list in this game is reached - by focus.</para>
///
/// <para>Every position here is measured from the game's own nodes rather than
/// written down: the first row starts under the popup's own body label, the step
/// between rows is the row's own height, and how many rows a page holds is the space
/// between the two divided by that step. A build that changes the popup's layout
/// changes all three with it.</para>
///
/// <para><b>A column longer than the panel is paged, never drawn past it.</b> The rows
/// are absolutely positioned siblings rather than a scrolling list, so a column of fifty
/// would put most of itself off the screen and leave a controller walking down into rows
/// nobody can see. <see cref="ScreenPage"/> decides which slice is on screen and the last
/// two places of a paged page go to Previous and Next; focus is joined across what is
/// drawn and nothing else, so it cannot reach a row that is not there. A pinned row is
/// on every page and spends one of the page's own places, so pinning shrinks the page
/// rather than pushing its last row past the panel. Paging is presentation:
/// <c>RunBrowser</c> and <c>RunView</c> return every row they always did, and this
/// decides what a player is looking at.</para>
///
/// It composes nothing and decides nothing. What the rows are is
/// <c>Sts2PilotTrainer.Trainer</c>'s answer; this puts them on screen.
/// </summary>
internal static class LibraryScreen
{
    /// <summary>The popup scene's own name for its content, resolved by the game's
    /// code the same way.</summary>
    private const string VerticalPopupPath = "VerticalPopup";

    /// <summary>Labels the ribbons carry until this mod replaces them. Never shown:
    /// the game's initialisers take a localized string and a DLL-only mod contributes
    /// no localization table, so the game's own confirm and cancel keys stand in and
    /// the text is then set directly.</summary>
    private static LocString PlaceholderConfirm => new("main_menu_ui", "GENERIC_POPUP.confirm");

    /// <summary>The smallest the body is allowed to become. A body nobody can read is
    /// not a list.</summary>
    private const int MinimumBodyFontSize = 17;

    /// <summary>How far apart rows sit, as a multiple of a row's own measured
    /// height.</summary>
    private const float RowStep = 1.12f;

    /// <summary>The same, on a screen whose rows carry a second line: the note is drawn
    /// in the gap, so the gap has to hold it.</summary>
    private const float NotedRowStep = 1.55f;

    /// <summary>How far under a row its second line sits, as a multiple of the row's own
    /// height. A row occupies its whole height, so anything under one clears it.</summary>
    private const float NoteDrop = 1.02f;

    /// <summary>The second line is supporting text and reads after the row, so it is
    /// smaller and dimmer than the label the game drew.</summary>
    private const int NoteFontSize = 15;

    /// <summary>The one colour this surface says anything quiet in, the same one
    /// <c>LibraryMarkup</c> dims with.</summary>
    private static readonly Color NoteColour = new(0.714f, 0.659f, 0.573f);

    /// <summary>
    /// Shows one screen, replacing whatever this module had up.
    ///
    /// One ribbon and a column of rows. There is no affirmative ribbon here because
    /// there is nothing on this surface to affirm: every offer is a row, and a screen
    /// with a second ribbon meaning "the one you highlighted" would be a second way to
    /// press the thing already under the cursor.
    /// </summary>
    /// <param name="back">Where the ribbon goes. Null closes the library, which is
    /// what the screen a player entered on does; a screen opened from another one hands
    /// in the way back to it, so the surface is one place a player moves around in
    /// rather than a sequence they fall out of the bottom of.</param>
    /// <param name="page">Which page of a column too long for the panel to draw. Zero is
    /// the first, and a caller never passes anything else - the Previous and Next rows
    /// re-show this same screen at the page either side.</param>
    internal static void Show(
        string title,
        string body,
        IReadOnlyList<ScreenRow> rows,
        string backLabel,
        Action? back = null,
        Action<string>? codeSubmitted = null,
        string codePlaceholder = "",
        int page = 0,
        Action<string, string, string, bool>? shareSubmitted = null)
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
            var label = content.BodyLabel();
            label.BbcodeEnabled = true;
            label.MinFontSize = MinimumBodyFontSize;
            label.ScrollActive = true;
            content.SetText(title, body);

            // Deferred for the reason Press clears first: the popup takes itself down
            // when the ribbon is pressed, and a screen shown inside the handler would be
            // the one it took down. Deferring puts the next screen after that.
            var share = shareSubmitted is null ? null : AddShareFields(content);
            content.InitYesButton(
                PlaceholderConfirm,
                _ =>
                {
                    if (share is not null)
                    {
                        shareSubmitted!(share.Name.Text, share.Description.Text,
                            share.DisplayName.Text, share.Consent.ButtonPressed);
                    }
                    else if (back is not null)
                    {
                        Callable.From(() => Reopen(back)).CallDeferred();
                    }
                });
            content.YesButton.SetText(share is null ? backLabel : LibraryCopy.ShareSubmit);
            if (share is null)
            {
                content.HideNoButton();
            }
            else
            {
                content.NoButton.Visible = true;
                content.NoButton.SetText(backLabel);
                content.NoButton.Connect(
                    NClickableControl.SignalName.Released,
                    Callable.From<NButton>(_ =>
                    {
                        if (back is not null) Callable.From(() => Reopen(back)).CallDeferred();
                    }));
            }

            var field = codeSubmitted is null ? null : AddCodeField(content, codePlaceholder, codeSubmitted);
            var first = AddRows(
                content,
                rows,
                share is not null ? 4f : field is null ? 0f : 1f,
                page,
                turned => Show(
                    title, body, rows, backLabel, back, codeSubmitted, codePlaceholder, turned,
                    shareSubmitted));

            // Deferred: adding the modal updates the game's active screen context,
            // which decides what is focused. Grabbing focus before that has finished
            // loses it, and a screen nothing can be reached on from a controller is a
            // screen half the players cannot use.
            var focus = first ?? (Control)content.YesButton;
            Callable.From(() => focus.GrabFocus()).CallDeferred();
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
    }

    /// <summary>
    /// Lays the rows out under the body and joins them into one focus column.
    ///
    /// Returns the first row a player can press, which is where focus goes: a screen
    /// that opened with the Back ribbon highlighted would put the way out ahead of the
    /// way in. Null when nothing is pressable, and the caller then focuses a ribbon.
    ///
    /// How many rows fit is the space between the top of the column and the popup's own
    /// ribbons, in steps - measured, like everything else positioned here. The
    /// measurement is the only thing that decides how many rows are drawn: a panel with
    /// no room for a page is refused by <see cref="ScreenPage.For"/> rather than drawn
    /// over, the way a ribbon with no measurable height already is.
    /// </summary>
    private static Control? AddRows(
        NVerticalPopup content,
        IReadOnlyList<ScreenRow> rows,
        float offsetSteps,
        int page,
        Action<int> turnTo)
    {
        if (rows.Count == 0) return null;

        var prototype = content.NoButton;
        var label = content.BodyLabel();
        var noted = rows.Any(row => row.Note is { Length: > 0 });
        var top = label.Position.Y + label.Size.Y + (prototype.Size.Y * RowStep * offsetSteps);
        var step = prototype.Size.Y * (noted ? NotedRowStep : RowStep);
        if (step <= 0f)
        {
            throw new InvalidOperationException(
                "This build's popup ribbon has no measurable height, so a row column cannot be laid out.");
        }

        var room = prototype.Position.Y - top;
        var fits = (int)Math.Floor(room / step);
        var pinned = rows.Where(row => row.Pinned).ToList();
        var paged = rows.Where(row => !row.Pinned).ToList();
        var slice = ScreenPage.For(paged.Count, fits, page, pinned.Count);
        var drawn = new List<ScreenRow>(pinned);
        drawn.AddRange(paged.Skip(slice.First).Take(slice.Count));
        if (slice.HasPrevious)
        {
            var previous = slice.Index - 1;
            drawn.Add(new ScreenRow(LibraryCopy.PreviousPage, Enabled: true, () => turnTo(previous)));
        }

        if (slice.HasNext)
        {
            var next = slice.Index + 1;
            drawn.Add(new ScreenRow(LibraryCopy.NextPage, Enabled: true, () => turnTo(next)));
        }

        var placed = new List<Control>();
        for (var index = 0; index < drawn.Count; index++)
        {
            var row = drawn[index];
            const int duplicateFlags =
                (int)(Node.DuplicateFlags.Groups | Node.DuplicateFlags.Scripts |
                      Node.DuplicateFlags.UseInstantiation);
            if (prototype.Duplicate(duplicateFlags) is not NPopupYesNoButton button) continue;

            button.Name = $"RunmobileRow{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            content.AddChild(button);

            // Before anything else it may do: a duplicate registers the ribbon's own
            // confirm or cancel key on the manager's stack, and the last one pushed
            // would answer for every row on the screen.
            button.DisconnectHotkeys();

            button.IsYes = false;
            button.SetText(row.Reason is { Length: > 0 } reason ? $"{row.Label} — {reason}" : row.Label);
            button.Position = new Vector2(prototype.Position.X, top + (step * index));
            button.Visible = true;

            if (row.Enabled)
            {
                button.Connect(
                    NClickableControl.SignalName.Released,
                    Callable.From<NButton>(_ => Press(row)));
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

            if (row.Note is { Length: > 0 } note) AddNote(content, button, note);
            if (row.MarkTooltip is { Length: > 0 } tooltip) AddMark(button, tooltip);

            placed.Add(button);
        }

        JoinColumn(placed, content);
        return placed.FirstOrDefault(control => control.FocusMode != Control.FocusModeEnum.None);
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
        var mark = TransportGlyphArt.Of(TransportGlyph.Reveal, "ShownThisSitting", size, MarkColour);
        mark.MouseFilter = Control.MouseFilterEnum.Pass;
        mark.TooltipText = tooltip;
        mark.Position = new Vector2(row.Size.X - size - (size * 0.6f), (row.Size.Y - size) / 2f);
        row.AddChild(mark);
    }

    /// <summary>The mark's ink: the note's own colour, so it reads as supporting the
    /// row rather than competing with its label.</summary>
    private static readonly Color MarkColour = new(0.714f, 0.659f, 0.573f);

    /// <summary>
    /// Draws a row's second line under it.
    ///
    /// Under rather than appended, because the design's rows are a label and a line
    /// beneath it: a note run into the label with a dash would read as part of what the
    /// row is called. It is a plain label wearing the game's font and takes no input, so
    /// the row above it keeps the whole press and the whole focus.
    ///
    /// Parented to the row rather than to the popup, so the dimming a refused row
    /// carries reaches it. A note at full brightness under a row at 45% would read
    /// louder than the refusal it belongs to, and a refused row would stop looking
    /// refused at note level.
    /// </summary>
    private static void AddNote(NVerticalPopup content, Control row, string note)
    {
        var label = new Label
        {
            Name = $"{row.Name}Note",
            Text = note,
            Position = new Vector2(0f, row.Size.Y * NoteDrop),
            CustomMinimumSize = new Vector2(row.Size.X, 0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        if (GameFont.Of(content.GetTree()?.Root) is { } font) label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", NoteFontSize);
        label.AddThemeColorOverride("font_color", NoteColour);
        row.AddChild(label);
    }

    /// <summary>
    /// Adds the run-code field: a stock Godot line edit wearing the game's own font.
    ///
    /// Stock rather than the game's <c>NSearchBar</c>, which is a scene this mod has no
    /// path to instantiate outside the screens that already hold one. The font is
    /// borrowed from a label already on screen, the way the result panel borrows it,
    /// so the field reads as part of the game rather than as Godot's default sans.
    /// </summary>
    private sealed record ShareFields(
        LineEdit Name, LineEdit Description, LineEdit DisplayName, CheckBox Consent);

    private static ShareFields AddShareFields(NVerticalPopup content)
    {
        var label = content.BodyLabel();
        var x = content.NoButton.Position.X;
        var y = label.Position.Y + label.Size.Y;
        var width = label.Size.X;
        var height = content.NoButton.Size.Y;
        var font = GameFont.Of(content.GetTree()?.Root);

        LineEdit Field(string name, string placeholder, int limit, int step)
        {
            var field = new LineEdit
            {
                Name = name,
                PlaceholderText = placeholder,
                MaxLength = limit,
                Position = new Vector2(x, y + (height * step)),
                CustomMinimumSize = new Vector2(width, height),
            };
            if (font is not null) field.AddThemeFontOverride("font", font);
            content.AddChild(field);
            return field;
        }

        var name = Field("RunmobileShareName", LibraryCopy.ShareNameField, 40, 0);
        var description = Field(
            "RunmobileShareDescription", LibraryCopy.ShareDescriptionField, 200, 1);
        var displayName = Field(
            "RunmobileShareDisplayName", LibraryCopy.ShareDisplayNameField, 40, 2);
        var consent = new CheckBox
        {
            Name = "RunmobileShareConsent",
            Text = LibraryCopy.ShareConsent,
            Position = new Vector2(x, y + (height * 3)),
            CustomMinimumSize = new Vector2(width, height),
        };
        if (font is not null) consent.AddThemeFontOverride("font", font);
        content.AddChild(consent);
        return new ShareFields(name, description, displayName, consent);
    }

    private static Control AddCodeField(
        NVerticalPopup content, string placeholder, Action<string> submitted)
    {
        var label = content.BodyLabel();
        var field = new LineEdit
        {
            Name = "RunmobileCodeField",
            PlaceholderText = placeholder,
            Position = new Vector2(content.NoButton.Position.X, label.Position.Y + label.Size.Y),
            CustomMinimumSize = new Vector2(label.Size.X, content.NoButton.Size.Y),
        };

        if (GameFont.Of(content.GetTree()?.Root) is { } font) field.AddThemeFontOverride("font", font);
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
    internal static void Dismiss() => NModalContainer.Instance?.Clear();

    /// <summary>Shows the screen a ribbon returns to, with whatever is up taken down
    /// first, and says so rather than leaving a player on a screen that did not
    /// change.</summary>
    private static void Reopen(Action back)
    {
        Dismiss();
        try
        {
            back();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not go back: {ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>Runs a row's action with the modal taken down first.</summary>
    private static void Press(ScreenRow row)
    {
        Dismiss();
        try
        {
            row.Press();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not act on '{row.Label}': " +
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

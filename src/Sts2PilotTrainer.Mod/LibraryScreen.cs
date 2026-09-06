using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// One row on a library screen: what it says, whether it can be pressed, and what
/// pressing it does.
/// </summary>
/// <param name="Reason">Said under the row when it is refused. The design's rule is
/// that a refused row on this surface states its reason, unlike the transport's
/// refused menu rows, because here the reason is a fact about the recording rather
/// than a state that clears itself in seconds.</param>
internal sealed record ScreenRow(string Label, bool Enabled, Action Press, string? Reason = null);

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
/// written down: the first row starts under the popup's own body label and the step
/// between rows is the row's own height. A build that changes the popup's layout
/// changes this with it.</para>
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

    /// <inheritdoc cref="PlaceholderConfirm"/>
    private static LocString PlaceholderCancel => new("main_menu_ui", "GENERIC_POPUP.cancel");

    /// <summary>The smallest the body is allowed to become. A body nobody can read is
    /// not a list.</summary>
    private const int MinimumBodyFontSize = 17;

    /// <summary>How far apart rows sit, as a multiple of a row's own measured
    /// height.</summary>
    private const float RowStep = 1.12f;

    /// <summary>
    /// Shows one screen, replacing whatever this module had up.
    ///
    /// <paramref name="confirm"/> is the screen's affirmative ribbon, which the game's
    /// own popup puts on the right and closes on. A screen with nothing to affirm
    /// passes null and gets one ribbon, which is what the eligibility screen does when
    /// it has no fight to offer.
    /// </summary>
    internal static void Show(
        string title,
        string body,
        IReadOnlyList<ScreenRow> rows,
        string backLabel,
        (string Label, Action Press)? confirm = null,
        Action<string>? codeSubmitted = null,
        string codePlaceholder = "")
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

            if (confirm is { } affirmative)
            {
                content.InitYesButton(PlaceholderConfirm, _ => affirmative.Press());
                content.YesButton.SetText(affirmative.Label);
                content.InitNoButton(PlaceholderCancel, _ => NModalContainer.Instance?.Clear());
                content.NoButton.SetText(backLabel);
            }
            else
            {
                content.InitYesButton(PlaceholderConfirm, _ => { });
                content.HideNoButton();
                content.YesButton.SetText(backLabel);
            }

            var field = codeSubmitted is null ? null : AddCodeField(content, codePlaceholder, codeSubmitted);
            var first = AddRows(content, rows, field is null ? 0f : 1f);

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
    /// </summary>
    private static Control? AddRows(NVerticalPopup content, IReadOnlyList<ScreenRow> rows, float offsetSteps)
    {
        if (rows.Count == 0) return null;

        var prototype = content.NoButton;
        var label = content.BodyLabel();
        var top = label.Position.Y + label.Size.Y + (prototype.Size.Y * RowStep * offsetSteps);
        var step = prototype.Size.Y * RowStep;
        if (step <= 0f)
        {
            throw new InvalidOperationException(
                "This build's popup ribbon has no measurable height, so a row column cannot be laid out.");
        }

        var placed = new List<Control>();
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
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

            placed.Add(button);
        }

        JoinColumn(placed, content);
        return placed.FirstOrDefault(control => control.FocusMode != Control.FocusModeEnum.None);
    }

    /// <summary>
    /// Adds the run-code field: a stock Godot line edit wearing the game's own font.
    ///
    /// Stock rather than the game's <c>NSearchBar</c>, which is a scene this mod has no
    /// path to instantiate outside the screens that already hold one. The font is
    /// borrowed from a label already on screen, the way the result panel borrows it,
    /// so the field reads as part of the game rather than as Godot's default sans.
    /// </summary>
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
    /// Runs a row's action with the modal taken down first.
    ///
    /// The screen a row opens is another modal, and the container holds one: opening
    /// the next while this one is up would leave the two stacked with the old one
    /// taking the input.
    /// </summary>
    private static void Press(ScreenRow row)
    {
        NModalContainer.Instance?.Clear();
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

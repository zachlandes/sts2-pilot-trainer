using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The two things the trainer still says in a panel rather than on the transport: a
/// refusal, and the result of the player's fight.
///
/// It used to carry the recording's decisions too, one popup per step. That is gone:
/// <see cref="PlaybackTransportStrip"/> is the one owner of the watched journey now,
/// because a popup is created and torn down around each decision and so cannot carry
/// a position across the map-to-combat transition, and because it covers the screens
/// the player is here to look at. What is left here is what a popup is actually for -
/// something that has to be acknowledged before anything else happens.
///
/// Both survivors draw over the game's own modal container, which is what dims the
/// screen behind them and takes them away on every path that already clears a popup.
/// </summary>
internal static class PrefightScreen
{
    private const string VerticalPopupPath = "VerticalPopup";

    /// <summary>Labels the popup's buttons carry until this mod replaces them. Never
    /// shown; the game's own initialisers take localized strings and a DLL-only mod
    /// contributes no localization table, so its own keys stand in and the text is
    /// then set directly.</summary>
    private static LocString PlaceholderConfirm => new("main_menu_ui", "GENERIC_POPUP.confirm");

    private static LocString PlaceholderCancel => new("main_menu_ui", "GENERIC_POPUP.cancel");

    private static NGenericPopup? _open;

    /// <summary>The result panel, while it is up. Not a popup: the result is the one
    /// surface this mod draws itself.</summary>
    private static Control? _openResult;

    /// <summary>
    /// Says why the fight was not entered.
    ///
    /// The sentence is the engine's or the comparison's, shown word for word: a
    /// drifted boundary already has an owner that explains itself, and rewriting it
    /// here would put a second account of the same failure on screen.
    /// </summary>
    /// <summary>
    /// Says why the fight was not entered, in a player's words, without losing the
    /// engine's.
    ///
    /// The first version of this showed the engine's diagnostic verbatim, and the
    /// captain's reading of it was that it looked like debugging information - which
    /// it was: a sentence about rows and columns shown to somebody who has never seen
    /// either. So the popup now says which screen stopped, that the trainer stopped
    /// rather than guess, and that the game was not changed. The exact reason is one
    /// press away and always in the log. The refusal is not softened: it still stops,
    /// and it still carries the diagnostic for whoever wants it.
    /// </summary>
    /// <param name="screen">The screen in the player's own word, or null on a refusal
    /// that is not about one - in which case the engine's own sentence is all there
    /// is, and it is shown rather than a noun being invented for it.</param>
    internal static void ShowRefusal(string creator, string? screen, string reason) =>
        ShowRefusal(creator, screen, reason, details: false);

    private static void ShowRefusal(string creator, string? screen, string reason, bool details)
    {
        if (screen is null)
        {
            Open(TrainerCopy.Name, reason, TrainerCopy.BackButton, Close, null, null, backstop: false);
            return;
        }

        var body = $"{TrainerCopy.RefusalHeadline(creator, screen)}\n\n{TrainerCopy.RefusalNoHarm}";
        if (details) body += $"\n\n{reason}";

        Open(
            TrainerCopy.Name,
            body,
            TrainerCopy.BackButton,
            Close,
            details ? TrainerCopy.RefusalHideDetails : TrainerCopy.RefusalShowDetails,
            () => ShowRefusal(creator, screen, reason, !details),
            backstop: false);
    }

    /// <summary>
    /// Asks before the transport does the one destructive thing it offers.
    ///
    /// The game's own popup, with its own two ribbons, because this is exactly what a
    /// modal is for: the player is about to discard an attempt, and a surface that
    /// does not have to be acknowledged would be the wrong shape for it. The
    /// affirmative sits on the right where the game puts it.
    /// </summary>
    internal static void Confirm(
        string title, string body, string confirmLabel, string cancelLabel, Action confirmed) =>
        Open(title, body, confirmLabel, () => { Close(); confirmed(); }, cancelLabel, Close, backstop: true);

    /// <summary>
    /// Shows the player's fight beside the recording's, or the one sentence that
    /// says why there is no comparison. One button, Done, which leaves the fight.
    ///
    /// Not the game's popup. The result is a panel of this mod's own, added into the
    /// game's modal container so that the container's own backstop dims and blocks
    /// the screen underneath and its Clear takes the panel away on every path that
    /// already clears a popup. The font is read from the theme the container sits
    /// under, so the words are in the game's own type rather than in Godot's default.
    /// </summary>
    internal static void ShowResult(FightResultScreen screen, Action done)
    {
        Close();

        try
        {
            var container = NModalContainer.Instance
                ?? throw new InvalidOperationException("This process has no modal container.");

            var panel = FightResultPanel.Build(
                screen,
                container.GetViewportRect().Size,
                ModelArt.Of,
                GameFont.Of(container.GetTree()?.Root) ?? container.GetThemeFont(GameLabelFont, GameLabelThemeType),
                done);

            container.AddChild(panel.Root);
            container.ShowBackstop();
            _openResult = panel.Root;

            // Deferred for the reason every focus grab in this mod is: adding to the
            // container changes what the game considers the active screen, and a grab
            // before that has settled is a panel a keyboard cannot reach.
            Callable.From(() => panel.Done.GrabFocus()).CallDeferred();
        }
        catch (Exception ex)
        {
            _openResult = null;
            throw new InvalidOperationException(
                $"The result panel could not be shown: {ex.GetType().Name}: {ex.Message}", ex);
        }
    }

    /// <summary>The theme entry the game's own labels take their font from.</summary>
    private static readonly StringName GameLabelFont = "font";

    private static readonly StringName GameLabelThemeType = "Label";

    internal static void Close()
    {
        try
        {
            if (_open is not null || _openResult is not null) NModalContainer.Instance?.Clear();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not close the trainer popup: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
        finally
        {
            _open = null;
            _openResult = null;
        }
    }

    private static void Open(
        string title, string body, string confirmLabel, Action onConfirm, string? cancelLabel, Action? onCancel,
        bool backstop)
    {
        Close();

        NGenericPopup? popup = null;
        var added = false;
        try
        {
            popup = NGenericPopup.Create()
                ?? throw new InvalidOperationException("This process has no popup surface.");
            var container = NModalContainer.Instance
                ?? throw new InvalidOperationException("This process has no modal container.");

            // A refusal names the run it is refusing and the screen it was refused on
            // is the evidence for the sentence, so that one keeps the screen lit. A
            // confirmation is about to discard an attempt and dims it, because the
            // question is the only thing on screen that matters at that moment.
            container.Add(popup, showBackstop: backstop);
            added = true;

            var content = popup.GetNode<NVerticalPopup>(VerticalPopupPath);
            content.SetText(title, body);
            content.InitYesButton(PlaceholderConfirm, _ => onConfirm());
            content.YesButton.SetText(confirmLabel);

            if (cancelLabel is null || onCancel is null)
            {
                content.HideNoButton();
            }
            else
            {
                content.InitNoButton(PlaceholderCancel, _ => onCancel());
                content.NoButton.SetText(cancelLabel);
            }

            // Deferred for the same reason the eligibility screen defers it: adding the
            // modal changes what the game considers the active screen, and grabbing
            // focus before that has settled loses it - which leaves a popup a keyboard
            // or a controller cannot reach.
            Callable.From(() => content.YesButton.GrabFocus()).CallDeferred();
            _open = popup;
        }
        catch (Exception ex)
        {
            try
            {
                if (added) NModalContainer.Instance?.Clear();
                else popup?.QueueFree();
            }
            catch (Exception cleanup)
            {
                Log.Error(
                    $"[{RunmobileMod.ModId}] could not clear a failed trainer popup: " +
                    $"{cleanup.GetType().Name}: {cleanup.Message}", 2);
            }

            _open = null;
            throw new InvalidOperationException(
                $"The trainer popup could not be shown: {ex.GetType().Name}: {ex.Message}", ex);
        }
    }
}

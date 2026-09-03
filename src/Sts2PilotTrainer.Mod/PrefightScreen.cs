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
    internal static void ShowRefusal(string reason) =>
        Open(TrainerCopy.Name, reason, TrainerCopy.BackButton, Close, null, null);

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
        string title, string body, string confirmLabel, Action onConfirm, string? cancelLabel, Action? onCancel)
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

            // No backstop. A refusal names the run it is refusing, and the screen it
            // was refused on is the evidence for the sentence; dimming it would hide
            // the thing the player needs in order to make sense of the words.
            container.Add(popup, showBackstop: false);
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

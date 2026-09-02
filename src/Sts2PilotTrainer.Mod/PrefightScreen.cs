using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// What sits over the game's own screens while the recording makes its decisions.
///
/// The screens underneath are the game's - Neow's event, then the map with the run
/// standing on it - and they are the whole point: the options the recording did not
/// take carry the strategic information, which is why a player watches the screens
/// rather than reading a summary of them. So this draws no screen of its own. It
/// puts the game's own popup over the one already there, with no backstop so the
/// screen behind stays lit, and it carries three things: whose recording this is,
/// which decision of theirs this is, and what they did.
///
/// Two controls, and they are the game's popup buttons: one makes the next recorded
/// decision, one makes all of them. There is no third, because there is no other
/// decision available here - the recording owns every one of them, and
/// <see cref="RecordedFightRun.DeviationLock"/> is what makes that true of the
/// commands underneath rather than only of these buttons.
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

    /// <summary>
    /// Shows the decision the recording makes next.
    ///
    /// The caption is read from the run the decision is about to act on, through
    /// <see cref="RecordedFightEntry.DescribeNextStep"/>, and worded by the trainer's
    /// own copy. Nothing about this recording is written down here.
    /// </summary>
    internal static void Show(string creator, RecordedFightEntry entry, Action next, Action skip)
    {
        var journey = PrefightJourney.For(
            creator, [entry.DescribeNextStep()], entry.Plan.PrefightActions.Count);
        var step = journey.Steps[0];

        var body = string.Join("\n\n",
            journey.Chip,
            $"{step.Counter}   {step.Caption}",
            journey.ChoicesShownAsRecorded);

        // Shown with the step it belongs to, and numbered from one, so that a step
        // whose number is 1 carries the note and the rest do not.
        var withNote = step.Number == 1
            ? body
            : string.Join("\n\n", journey.Chip, $"{step.Counter}   {step.Caption}");

        Open(journey.Chip, withNote, journey.NextButton, next, journey.SkipButton, skip);
    }

    /// <summary>
    /// Says why the fight was not entered.
    ///
    /// The sentence is the engine's or the comparison's, shown word for word: a
    /// drifted boundary already has an owner that explains itself, and rewriting it
    /// here would put a second account of the same failure on screen.
    /// </summary>
    internal static void ShowRefusal(string reason) =>
        Open(TrainerCopy.Name, reason, TrainerCopy.BackButton, Close, null, null);

    internal static void Close()
    {
        try
        {
            if (_open is not null) NModalContainer.Instance?.Clear();
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{CombatTrainerMod.ModId}] could not close the watching popup: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
        }
        finally
        {
            _open = null;
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

            // No backstop. The screen underneath is the recording's own decision being
            // shown, and dimming it would hide the thing the player is here to see.
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
                    $"[{CombatTrainerMod.ModId}] could not clear a failed watching popup: " +
                    $"{cleanup.GetType().Name}: {cleanup.Message}", 2);
            }

            _open = null;
            throw new InvalidOperationException(
                $"The watching popup could not be shown: {ex.GetType().Name}: {ex.Message}", ex);
        }
    }
}

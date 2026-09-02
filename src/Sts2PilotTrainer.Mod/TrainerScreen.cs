using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The one screen this mod owns: whether the player's game can play the recorded
/// fight, and what to do about it when it cannot.
///
/// Built out of the game's own modal popup rather than assembled from Godot
/// controls, for the same reason the mode card is a duplicate: the panel, the
/// fonts, the button, its hotkeys and its controller focus are the game's, and a
/// hand-built lookalike would be a worse copy of them that also drifts.
///
/// It computes nothing. <see cref="Preflight.EvaluateLiveHost"/> produces the
/// verdict, <see cref="EligibilityScreen"/> turns it into rows and sentences, and
/// this puts those on screen.
/// </summary>
internal static class TrainerScreen
{
    /// <summary>The popup scene's own name for its content, resolved by the game's
    /// code the same way.</summary>
    private const string VerticalPopupPath = "VerticalPopup";

    /// <summary>Label the Back button carries until this mod replaces it. Never
    /// shown; <see cref="NVerticalPopup.InitYesButton"/> takes a localized string and
    /// a DLL-only mod contributes no localization table, so the game's own confirm
    /// key stands in and the text is then set directly.</summary>
    private static LocString PlaceholderButtonLabel => new("main_menu_ui", "GENERIC_POPUP.confirm");

    /// <summary>The smallest the evidence rows are allowed to become.</summary>
    private const int MinimumBodyFontSize = 17;

    internal static void Open()
    {
        try
        {
            Show(Compose());
        }
        catch (Exception ex)
        {
            // Everything a reading can refuse - a profile that went away, a build
            // that cannot identify itself - arrives here. Saying so is the whole
            // point of the screen; claiming eligibility would be the failure.
            Log.Error(
                $"[{CombatTrainerMod.ModId}] could not read this game's eligibility: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
            Show(EligibilityScreenRefusal.For(ex));
        }
    }

    private static EligibilityScreen Compose()
    {
        var recording = CombatTrainerMod.Recording
            ?? throw new InvalidOperationException("This build carries no recording to check against.");
        var expected = recording.Environment;
        return EligibilityScreen.For(expected, Preflight.EvaluateLiveHost(expected));
    }

    private static void Show(EligibilityScreen screen)
    {
        var popup = NGenericPopup.Create();
        var container = NModalContainer.Instance;
        if (popup is null || container is null)
        {
            Log.Error($"[{CombatTrainerMod.ModId}] this process has no modal surface; nothing shown.", 2);
            return;
        }

        container.Add(popup, showBackstop: true);

        var content = popup.GetNode<NVerticalPopup>(VerticalPopupPath);
        var body = content.BodyLabel();

        // Enabled explicitly rather than assumed: if the scene ever shipped with it
        // off, the row colours would render as literal tags in the middle of the copy.
        body.BbcodeEnabled = true;

        // The popup's label shrinks its font until the text fits, which for a body
        // this long lands somewhere unreadable. A floor is the honest trade: a row
        // nobody can read is not evidence, and the label scrolls what does not fit.
        body.MinFontSize = MinimumBodyFontSize;
        body.ScrollActive = true;

        content.SetText(screen.Title, ScreenMarkup.Body(screen));
        content.InitYesButton(PlaceholderButtonLabel, _ => { });
        content.HideNoButton();
        content.YesButton.SetText(screen.BackButton);

        // Deferred: adding the modal updates the game's active screen context, which
        // decides what is focused. Grabbing focus before that has finished loses it,
        // and a screen whose only control cannot be reached from a keyboard or a
        // controller is a screen half the players cannot leave.
        Callable.From(() => content.YesButton.GrabFocus()).CallDeferred();
    }

    /// <summary>
    /// The popup's body label. Public to read on the popup's own screens and not to
    /// this mod, so it is reached the way every other private reading in this
    /// project is - by name, refusing loudly when a build no longer has it.
    /// </summary>
    private static MegaRichTextLabel BodyLabel(this NVerticalPopup popup)
    {
        var property = typeof(NVerticalPopup).GetProperty(
            "BodyLabel",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("NVerticalPopup has no BodyLabel on this build.");
        return property.GetValue(popup) as MegaRichTextLabel
            ?? throw new InvalidOperationException("NVerticalPopup.BodyLabel was not a rich text label.");
    }
}

/// <summary>
/// What the screen says when it could not read the game at all.
///
/// A refusal, never a verdict: the failing headline plus the reason, so that an
/// environment the trainer cannot measure is never reported as one it measured and
/// approved.
/// </summary>
internal static class EligibilityScreenRefusal
{
    internal static EligibilityScreen For(Exception failure) => new(
        Title: TrainerCopy.Name,
        Subtitle: TrainerCopy.Subtitle,
        RecordingLine: string.Empty,
        Headline: TrainerCopy.FailHeadline,
        Eligible: false,
        Rows: [],
        Refusals: [failure.Message],
        ProfileNote: TrainerCopy.ProfileNote,
        BackButton: TrainerCopy.BackButton);
}

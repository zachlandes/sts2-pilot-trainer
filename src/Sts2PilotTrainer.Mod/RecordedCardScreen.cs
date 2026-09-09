using System.Globalization;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The game's own card-selection screen, read and pressed for the recording.
///
/// One owner because the reveal and the commit are two halves of the same reading:
/// the reveal lights a holder and the commit presses that same holder, and two readers
/// could light one card and press another. <see cref="RecordedFightReveal"/> asks this
/// for the control to light; <see cref="RecordedFightRun"/> asks it to press. Nothing
/// here decides anything - which card is the recording's, and the plan's.
///
/// It is written to <see cref="NCardGridSelectionScreen"/> rather than to one screen,
/// because which screen a blessing opens is the relic's business: a removal opens
/// <c>NDeckCardSelectScreen</c> and a transform opens <c>NDeckTransformSelectScreen</c>,
/// through a different command with a different confirm button. They share the base
/// that owns the grid, the offered list and the click, which is the whole of what this
/// needs - and naming one of them is how the first attempt at this refused a screen that
/// was open in front of the player.
///
/// The screen is reached where the engine put it, on the overlay stack, and its parts
/// by the unique names the screens' own <c>_Ready</c> uses. Which card is which is read
/// off the list the engine handed the screen rather than off the grid, because the grid
/// sorts what it was given before it draws it: the recording's <c>option_index</c> is a
/// position in the engine's list and a position on screen is a layout detail. That is
/// the same list, in the same order, that <c>ManifestCardSelector</c> is offered
/// headlessly, so the two hosts pick the same card out of a deck holding four Strikes.
///
/// It refuses rather than approximating, in the two ways
/// <see cref="RecordedFightReveal"/> does: a screen that has not arrived yet is a
/// moment to wait out, and a screen holding something other than what the recording
/// took is a reveal that would point at the wrong card.
/// </summary>
internal static class RecordedCardScreen
{
    /// <summary>The unique names the screen's own <c>_Ready</c> reads its parts by.
    /// Read the way the game reads them; a build that renames one costs a loud refusal
    /// rather than a press on the wrong control.</summary>
    private const string GridPath = "%CardGrid";

    private const string PreviewPath = "%PreviewContainer";

    // The confirm inside that preview is found by type rather than by name: the deck
    // screen calls it %PreviewConfirm and the transform screen calls it Confirm, and
    // what matters is only that it is the preview's own confirm.

    /// <summary>The recording's card on the screen that is up: the screen, and the
    /// holder drawing it.</summary>
    internal sealed record Found(NCardGridSelectionScreen Screen, NGridCardHolder Holder, int Offered);

    /// <summary>
    /// Finds the holder drawing the card the recording took, and establishes that the
    /// screen is showing what the recording was shown.
    /// </summary>
    /// <exception cref="RevealNotReadyException">When the screen the engine is opening
    /// has not arrived yet, or has not drawn this card yet.</exception>
    /// <exception cref="RevealRefusedException">When the screen offers something other
    /// than what the recording took.</exception>
    internal static Found Find(string cardModelId, int optionIndex)
    {
        // Not "a screen of this type somewhere in the tree": the one the engine last
        // pushed. A run that had left an older one up would otherwise be answered on
        // the wrong screen, and the engine is waiting on this one.
        if (NOverlayStack.Instance?.Peek() is not NCardGridSelectionScreen screen ||
            !GodotObject.IsInstanceValid(screen))
        {
            throw new RevealNotReadyException(
                "The card screen for this recording's last decision hasn't opened yet.");
        }

        var offered = OfferedTo(screen);
        if (optionIndex < 0 || optionIndex >= offered.Count)
        {
            throw new RevealRefusedException(
                $"The recording takes screen option {optionIndex.ToString(CultureInfo.InvariantCulture)} and " +
                $"this screen offers {offered.Count.ToString(CultureInfo.InvariantCulture)}. Refusing to " +
                "point at a card that is not there.",
                TrainerCopy.CardScreenName);
        }

        var card = offered[optionIndex];
        if (card.Id.ToString() != cardModelId)
        {
            throw new RevealRefusedException(
                $"The recording takes {cardModelId} at screen option " +
                $"{optionIndex.ToString(CultureInfo.InvariantCulture)}, and this screen offers {card.Id} " +
                "there. The screen is not showing what the recording chose.",
                TrainerCopy.CardScreenName);
        }

        var grid = screen.GetNodeOrNull<NCardGrid>(GridPath)
            ?? throw new InvalidOperationException(
                $"The card screen has no {GridPath} on this build, so the card the recording took cannot be " +
                "found on screen.");

        // Not ready rather than refused, because the commonest cause is a moment: the
        // grid animates its rows in and a card it has not laid out yet has no holder
        // for a frame or two after the screen arrives.
        //
        // The other cause is not a moment. The grid keeps holders for a sliding window
        // of rows and reassigns them from the list as it scrolls, so a deck taller than
        // the window has cards with no holder at all until somebody scrolls to them -
        // and nothing here scrolls. See docs/in-game-host.md for what is not built.
        //
        // Which of the two this is cannot be told from one reading: a grid mid-animation
        // is also drawing fewer cards than it was given. So both are stated and neither
        // is claimed. A reader who sees this at all has watched the retry give up, which
        // is the evidence that decides it, and inventing a verdict here would put a
        // confident wrong sentence in front of whoever is debugging.
        var holder = grid.GetCardHolder(card);
        if (holder is null || !GodotObject.IsInstanceValid(holder))
        {
            var drawn = grid.CurrentlyDisplayedCards.Count();
            throw new RevealNotReadyException(
                $"This screen is drawing {drawn.ToString(CultureInfo.InvariantCulture)} of the " +
                $"{offered.Count.ToString(CultureInfo.InvariantCulture)} cards it was given and not the one " +
                "the recording took: it is either still laying its rows in, or that card is outside the " +
                "grid's scrolling window and nothing here scrolls.");
        }

        return new Found(screen, holder, offered.Count);
    }

    /// <summary>
    /// Presses the recording's card, the way a click on it does.
    ///
    /// The holder's own <c>Pressed</c> signal, which is what the holder emits for a
    /// click or a controller press and what the grid is connected to. Emitted at the
    /// holder rather than at the grid so the grid's own handler runs: it records which
    /// holder was last pressed, which is where the screen puts focus back if the
    /// selection is cancelled.
    /// </summary>
    internal static void Press(Found found) =>
        found.Holder.EmitSignal(NCardHolder.SignalName.Pressed, found.Holder);

    /// <summary>
    /// Presses the confirm the screen puts up once it has all the cards it asked for,
    /// and says whether there was one to press.
    ///
    /// A screen transition rather than a decision, for the reason
    /// <c>RecordedFightRun.CarryOnPastTheGamesOwnProceed</c> gives: the recording holds
    /// the picks and not the presses between them. Where the screen wants more cards
    /// than have been picked so far there is no preview up and nothing to press, which
    /// is the ordinary case for every pick but the last.
    /// </summary>
    internal static bool ConfirmIfThePreviewIsUp(NCardGridSelectionScreen screen)
    {
        if (Preview(screen) is not { } preview) return false;

        var confirm = ConfirmButtonIn(preview)
            ?? throw new InvalidOperationException(
                "The card screen's preview has no confirm button on this build, so the selection the " +
                "recording made cannot be confirmed.");

        if (!confirm.IsEnabled) return false;

        confirm.ForceClick();
        return true;
    }

    /// <summary>
    /// The preview's own confirm, found by type.
    ///
    /// By type because the two screens name it differently and neither name is the
    /// point. The preview holds one confirm and one back button, so the first confirm
    /// under it is unambiguous; a build that put two there would be a build this should
    /// stop on rather than guess at, and the caller refuses on null.
    /// </summary>
    private static NConfirmButton? ConfirmButtonIn(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is NConfirmButton confirm) return confirm;
            if (ConfirmButtonIn(child) is { } deeper) return deeper;
        }

        return null;
    }

    /// <summary>Whether the screen has taken its answer and gone.</summary>
    internal static bool HasClosed(NCardGridSelectionScreen screen) =>
        !GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree();

    /// <summary>Whether the screen is showing its own preview of what was picked.
    /// Asked of the screen rather than remembered, because whether there is one at all
    /// depends on how many cards it was told to ask for.</summary>
    internal static bool PreviewIsUp(NCardGridSelectionScreen screen) => Preview(screen) is not null;

    /// <summary>The screen's preview of the picked cards while it is up, or null.</summary>
    private static Control? Preview(NCardGridSelectionScreen screen) =>
        HasClosed(screen) ? null : screen.GetNodeOrNull<Control>(PreviewPath) is { Visible: true } preview
            ? preview
            : null;

    /// <summary>
    /// The cards the engine handed this screen, in the order it handed them, which is
    /// what the recording's <c>option_index</c> indexes.
    ///
    /// <see cref="CardScreensUp"/>'s reading, because a screen's contents are a fact
    /// about the game that the recorder reads for the same thing. What a build without
    /// it means is this caller's own to say, and it says refusal: falling back on the
    /// grid's order would silently mean a different card, because the grid sorts what
    /// it was given before it draws it.
    /// </summary>
    private static IReadOnlyList<CardModel> OfferedTo(NCardGridSelectionScreen screen) =>
        CardScreensUp.OfferedTo(screen)
        ?? throw new InvalidOperationException(
            "This build does not expose what the card screen was offered, so which card the recording took " +
            "cannot be told from the cards drawn beside it.");
}

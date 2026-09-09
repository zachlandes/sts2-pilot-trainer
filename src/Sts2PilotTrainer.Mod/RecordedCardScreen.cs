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
/// The screen is reached where the engine put it, on the overlay stack, and its parts
/// by the unique names the screen's own <c>_Ready</c> uses. Which card is which is read
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

    private const string PreviewConfirmPath = "%PreviewConfirm";

    /// <summary>The recording's card on the screen that is up: the screen, and the
    /// holder drawing it.</summary>
    internal sealed record Found(NDeckCardSelectScreen Screen, NGridCardHolder Holder, int Offered);

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
        if (NOverlayStack.Instance?.Peek() is not NDeckCardSelectScreen screen ||
            !GodotObject.IsInstanceValid(screen))
        {
            throw new RevealNotReadyException(
                "The card screen the recording's last decision opens is not up yet.");
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

        // Not ready rather than refused: the grid animates its rows in, and a card the
        // screen has not laid out yet has no holder for a moment after the screen
        // arrives.
        var holder = grid.GetCardHolder(card);
        if (holder is null || !GodotObject.IsInstanceValid(holder))
        {
            throw new RevealNotReadyException("This screen is still laying out the card the recording took.");
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
    internal static bool ConfirmIfThePreviewIsUp(NDeckCardSelectScreen screen)
    {
        if (Preview(screen) is not { } preview) return false;

        var confirm = preview.GetNodeOrNull<NConfirmButton>(PreviewConfirmPath)
            ?? throw new InvalidOperationException(
                $"The card screen's preview has no {PreviewConfirmPath} on this build, so the selection the " +
                "recording made cannot be confirmed.");

        if (!confirm.IsEnabled) return false;

        confirm.ForceClick();
        return true;
    }

    /// <summary>Whether the screen has taken its answer and gone.</summary>
    internal static bool HasClosed(NDeckCardSelectScreen screen) =>
        !GodotObject.IsInstanceValid(screen) || !screen.IsInsideTree();

    /// <summary>Whether the screen is showing its own preview of what was picked.
    /// Asked of the screen rather than remembered, because whether there is one at all
    /// depends on how many cards it was told to ask for.</summary>
    internal static bool PreviewIsUp(NDeckCardSelectScreen screen) => Preview(screen) is not null;

    /// <summary>The screen's preview of the picked cards while it is up, or null.</summary>
    private static Control? Preview(NDeckCardSelectScreen screen) =>
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
    private static IReadOnlyList<CardModel> OfferedTo(NDeckCardSelectScreen screen) =>
        CardScreensUp.OfferedTo(screen)
        ?? throw new InvalidOperationException(
            "This build does not expose what the card screen was offered, so which card the recording took " +
            "cannot be told from the cards drawn beside it.");
}

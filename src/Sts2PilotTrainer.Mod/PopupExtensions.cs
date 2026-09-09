using Godot;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace Sts2PilotTrainer.Mod;

internal static class PopupExtensions
{
    /// <summary>Reads the popup's native heading label.</summary>
    internal static MegaLabel HeaderLabel(this NVerticalPopup popup) => popup.Header;

    /// <summary>Reads the popup's native body label.</summary>
    internal static MegaRichTextLabel BodyLabel(this NVerticalPopup popup) => popup.Description;

    /// <summary>What the game draws this popup's heading in.</summary>
    internal static GameTextStyle HeaderText(this NVerticalPopup popup) =>
        GameText.Require(popup.HeaderLabel(), "popup heading");

    /// <summary>What the game draws this popup's body copy in.</summary>
    internal static GameTextStyle BodyText(this NVerticalPopup popup) =>
        GameText.Require(popup.BodyLabel(), "popup description");

    /// <summary>What the game draws this popup's ribbon labels in.</summary>
    internal static GameTextStyle ButtonText(this NVerticalPopup popup) =>
        GameText.RequireUnder(popup.NoButton.GetNodeOrNull<Control>("%Label"), "popup button label");
}

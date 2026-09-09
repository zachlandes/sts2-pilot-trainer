using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace Sts2PilotTrainer.Mod;

internal static class PopupExtensions
{
    /// <summary>Reads the popup's native heading label.</summary>
    internal static MegaLabel HeaderLabel(this NVerticalPopup popup) =>
        popup.GetNode<MegaLabel>("Header");

    /// <summary>Reads the popup's native body label.</summary>
    internal static MegaRichTextLabel BodyLabel(this NVerticalPopup popup) =>
        popup.GetNode<MegaRichTextLabel>("Description");

}

using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Nodes.CommonUi;

namespace Sts2PilotTrainer.Mod;

internal static class PopupExtensions
{
    /// <summary>
    /// Reads the popup's body label, which the game does not expose consistently across builds.
    /// </summary>
    internal static MegaRichTextLabel BodyLabel(this NVerticalPopup popup)
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

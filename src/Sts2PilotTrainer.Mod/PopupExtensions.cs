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

    /// <summary>
    /// What the game draws this popup's body copy in.
    ///
    /// The popup's own description is the native element every line Runmobile writes on
    /// a parchment stands in for, so it is the one that is measured. A build whose body
    /// cannot be read falls back to what the rest of the screen is drawn at.
    /// </summary>
    internal static GameTextStyle BodyText(this NVerticalPopup popup)
    {
        try
        {
            return GameText.Of(popup.BodyLabel()) ?? GameText.On(popup);
        }
        catch (InvalidOperationException)
        {
            return GameText.On(popup);
        }
    }
}

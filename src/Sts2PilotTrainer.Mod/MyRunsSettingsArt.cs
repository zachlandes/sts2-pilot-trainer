using Godot;

namespace Sts2PilotTrainer.Mod;

/// <summary>The settings screen's action silhouette and checkbox images, never redrawn</summary>
internal sealed record MyRunsSettingsArt(
    Texture2D Action,
    ShaderMaterial ActionMaterial,
    Vector2 ActionSize,
    Texture2D Ticked,
    Texture2D Unticked)
{
    internal Vector2 Insets { get; init; }
    internal GameTextOutline? CaptionOutline { get; init; }

    /// <summary>The native button's own hue, the one its caption outline was drawn for</summary>
    internal float ActionHue { get; init; } = DestructiveHue;

    internal const string TickedPath = "res://images/atlases/ui_atlas.sprites/checkbox_ticked.tres";
    internal const string UntickedPath = "res://images/atlases/ui_atlas.sprites/checkbox_unticked.tres";

    // Between the native gold (.61) and maroon (.45): burnt orange, not another Reset
    internal const float DestructiveHue = 0.53f;

    internal static MyRunsSettingsArt From(Control nativeButton)
    {
        var image = nativeButton.GetNodeOrNull<TextureRect>("Image")
            ?? throw new InvalidOperationException("The native settings button has no Image.");
        var material = image.Material as ShaderMaterial
            ?? throw new InvalidOperationException("The native settings button has no HSV material.");
        return new MyRunsSettingsArt(
            image.Texture ?? throw new InvalidOperationException("The native settings button has no texture."),
            material,
            nativeButton.Size,
            ResourceLoader.Load<Texture2D>(TickedPath)
                ?? throw new InvalidOperationException("The native checked tickbox image is unavailable."),
            ResourceLoader.Load<Texture2D>(UntickedPath)
                ?? throw new InvalidOperationException("The native unchecked tickbox image is unavailable."))
        {
            CaptionOutline = GameTextOutline.From(nativeButton.GetNodeOrNull<Control>("Label")
                ?? throw new InvalidOperationException("The native settings button has no caption.")),
            ActionHue = (float)material.GetShaderParameter("h"),
            Insets = nativeButton.GetParent() is Control entry
                ? new Vector2(entry.GetThemeConstant("margin_left", "MarginContainer"),
                    entry.GetThemeConstant("margin_right", "MarginContainer"))
                : Vector2.Zero,
        };
    }

    internal ShaderMaterial DestructiveMaterial()
    {
        // The same HSV shader as Credits and Reset, on our own copy so neither changes
        var material = (ShaderMaterial)ActionMaterial.Duplicate();
        material.SetShaderParameter("h", DestructiveHue);
        material.SetShaderParameter("s", 1.5f);
        material.SetShaderParameter("v", 1.1f);
        return material;
    }

    /// <summary>
    /// The native caption outline, turned to the destructive face's hue.
    ///
    /// Every settings button's outline is a dark shade of its own face - green under
    /// Mod Settings, brown under Credits, maroon under Reset - and the anchor's, copied
    /// as it is, would be dark green on burnt orange. Turning it through the same
    /// rotation the face's shader applies puts Reset's outline within a few hundredths
    /// of the one the game drew for it, so it is the game's own rule rather than a
    /// colour picked here.
    /// </summary>
    internal GameTextOutline? DestructiveOutline() => CaptionOutline is { } outline
        ? outline with { Colour = ShiftHue(outline.Colour, ActionHue, DestructiveHue) }
        : null;

    /// <summary>
    /// <c>res://shaders/hsv.gdshader</c>'s hue shift, in the YIQ space it rotates:
    /// the colour a face tinted to <paramref name="from"/> becomes when the same
    /// material is set to <paramref name="to"/>.
    /// </summary>
    internal static Color ShiftHue(Color colour, float from, float to)
    {
        // A face left at its own hue keeps the outline the game drew, to the bit
        if (from == to) return colour;
        // The shader turns by 2π(1 - h) and multiplies on the right, which is this way round
        var angle = 2.0 * Math.PI * (to - from);
        var (cos, sin) = (Math.Cos(angle), Math.Sin(angle));
        double y = 0.2989 * colour.R + 0.5870 * colour.G + 0.1140 * colour.B;
        double i = 0.5959 * colour.R - 0.2774 * colour.G - 0.3216 * colour.B;
        double q = 0.2115 * colour.R - 0.5229 * colour.G + 0.3114 * colour.B;
        var (ti, tq) = (cos * i + sin * q, cos * q - sin * i);
        return new Color(
            Clamp(y + 0.9563 * ti + 0.6210 * tq),
            Clamp(y - 0.2721 * ti - 0.6474 * tq),
            Clamp(y - 1.1070 * ti + 1.7046 * tq),
            colour.A);
    }

    private static float Clamp(double channel) => (float)Math.Clamp(channel, 0.0, 1.0);
}

using Godot;

namespace Sts2PilotTrainer.Mod;

/// <summary>Widens the retail ribbon without widening its painted ends.</summary>
internal static class LibraryRibbonArt
{
    internal static void ReplaceTextures(Control button, float width)
    {
        foreach (var path in new[] { "%Image", "%Outline" })
        {
            if (button.GetNode<Control>(path) is not TextureRect image || image.Texture is null)
                throw new InvalidOperationException($"The library ribbon has no texture at {path}");

            var owner = image.Owner;
            var unique = image.UniqueNameInOwner;
            var patch = Create(image, image.Texture.GetSize().X, width);
            image.UniqueNameInOwner = false;
            image.ReplaceBy(patch);
            patch.Name = image.Name;
            patch.Owner = owner;
            patch.UniqueNameInOwner = unique;
            image.Free();
        }
    }

    internal static NinePatchRect Create(TextureRect image, float sourceWidth, float width)
    {
        // Keep the outer quarters at native resolution; only the middle widens
        // Narrow rows cap the margins so the patch cannot enlarge their hit area
        var cap = (int)MathF.Floor(MathF.Min(sourceWidth, width) / 4f);
        return new NinePatchRect
        {
            Name = image.Name,
            Texture = image.Texture,
            Material = image.Material,
            UseParentMaterial = image.UseParentMaterial,
            Modulate = image.Modulate,
            SelfModulate = image.SelfModulate,
            Visible = image.Visible,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = image.Position,
            Size = new Vector2(width, image.Size.Y),
            PatchMarginLeft = cap,
            PatchMarginRight = cap,
            PatchMarginTop = 0,
            PatchMarginBottom = 0,
            AxisStretchHorizontal = NinePatchRect.AxisStretchMode.Stretch,
            AxisStretchVertical = NinePatchRect.AxisStretchMode.Stretch,
        };
    }
}

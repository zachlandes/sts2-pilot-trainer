using Godot;
using System.Runtime.CompilerServices;

namespace Sts2PilotTrainer.Mod;

/// <summary>Widens the retail ribbon without widening its painted ends.</summary>
internal static class LibraryRibbonArt
{
    private static readonly ConditionalWeakTable<AtlasTexture, ImageTexture> PaddedTextures = new();

    private static Texture2D? ForNinePatch(Texture2D? texture) => texture is AtlasTexture atlas
        ? PaddedTextures.GetValue(atlas, Rasterize)
        : texture;

    private static ImageTexture Rasterize(AtlasTexture atlas)
    {
        // NinePatchRect omits atlas padding, misaligning the image and its outline
        // Reconstruct the logical texture in memory without exporting game assets
        using var source = atlas.Atlas.GetImage();
        if (source.IsCompressed() && source.Decompress() != Error.Ok)
            throw new InvalidOperationException("The library ribbon atlas could not be decompressed");
        source.Convert(Image.Format.Rgba8);
        var size = atlas.GetSize();
        using var padded = Image.CreateEmpty((int)size.X, (int)size.Y, false, Image.Format.Rgba8);
        padded.Fill(Colors.Transparent);
        padded.BlitRect(source,
            new Rect2I((int)atlas.Region.Position.X, (int)atlas.Region.Position.Y,
                (int)atlas.Region.Size.X, (int)atlas.Region.Size.Y),
            new Vector2I((int)atlas.Margin.Position.X, (int)atlas.Margin.Position.Y));
        return ImageTexture.CreateFromImage(padded);
    }

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
            Texture = ForNinePatch(image.Texture),
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

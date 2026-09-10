using Godot;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

public sealed class MyRunsSettingsArtTests
{
    internal static MyRunsSettingsArt Art() => new(
        new Texture2D(), new ShaderMaterial(), new Vector2(320, 64), new Texture2D(), new Texture2D());

    [Fact]
    public void MissingNativeArtRefusesInsteadOfDrawingAFallback()
    {
        var style = new GameTextStyle(null, 28);
        var text = new MyRunsSettingsText(style, style, style, style, style);
        Assert.Throws<InvalidOperationException>(() => MyRunsSettingsRow.Build(
            MyRunsRow.For(new MyRunsFacts(Runs: 3, Bytes: 100, Keep: 20)),
            20, true, 1000, text, _ => { }, () => { }, _ => { }, _ => { }));
    }

    [Fact]
    public void NativeArtAndHeadingSeparateActionsFromSettings()
    {
        var nativeCaption = new Label();
        nativeCaption.AddThemeColorOverride("font_outline_color", new Color(0.2f, 0.1f, 0.05f));
        nativeCaption.AddThemeConstantOverride("outline_size", 7);
        var art = Art() with { Insets = new Vector2(12, 12), CaptionOutline = GameTextOutline.From(nativeCaption) };
        var style = new GameTextStyle(null, 28);
        var text = new MyRunsSettingsText(style, style, style, style, style) { Art = art };
        var row = MyRunsSettingsRow.Build(
            MyRunsRow.For(new MyRunsFacts(Runs: 3, Bytes: 100, Keep: 20, MainMenuRowShown: false)),
            20, true, 1000, text, _ => { }, () => { }, _ => { }, _ => { });

        var heading = Child<Label>(row.Root, "Heading");
        Assert.Equal("Runmobile", heading.Text);
        Assert.Equal(HorizontalAlignment.Left, heading.HorizontalAlignment);
        Assert.Equal(12, heading.Position.X);
        Assert.Equal(988, row.Remove.Position.X + row.Remove.Size.X);
        Assert.Equal(7, row.Remove.GetThemeConstant("outline_size", "Button"));
        Assert.Equal(nativeCaption.GetThemeColor("font_outline_color", "Label"),
            row.Remove.GetThemeColor("font_outline_color", "Button"));
        Assert.True(heading.Position.Y + heading.Size.Y < row.Fewer.Position.Y);
        Assert.Equal("Show community runs", Child<Label>(row.Root, "FetchLabel").Text);
        Assert.Equal("Show on the main menu", Child<Label>(row.Root, "MainMenuLabel").Text);
        Assert.Equal("Remove", row.Remove.Text);
        var removalLabel = Child<Label>(row.Root, "RemoveLabel");
        Assert.Equal("Remove all my runs", removalLabel.Text);
        Assert.True(removalLabel.Position.X + removalLabel.Size.X < row.Remove.Position.X);
        Assert.Equal(art.ActionSize.X, row.Remove.Size.X);
        Assert.Equal(art.ActionSize.Y, row.Remove.Size.Y);
        var action = Child<TextureRect>(row.Remove, "NativeArt");
        Assert.Same(art.Action, action.Texture);
        Assert.NotSame(art.ActionMaterial, action.Material);
        Assert.True(action.ShowBehindParent);
        Assert.IsType<StyleBoxEmpty>(row.Remove.ThemeStylebox("normal"));
        Assert.Same(art.Ticked, Child<TextureRect>(row.Fetch, "NativeArt").Texture);
        Assert.Same(art.Unticked, Child<TextureRect>(row.MainMenu, "NativeArt").Texture);

        row.Apply(MyRunsRow.For(new MyRunsFacts(Runs: 3, Bytes: 100, Keep: 20, MainMenuRowShown: true)), 20, false);
        Assert.Same(art.Unticked, Child<TextureRect>(row.Fetch, "NativeArt").Texture);
        Assert.Same(art.Ticked, Child<TextureRect>(row.MainMenu, "NativeArt").Texture);
    }

    [Fact]
    public void OutlineFollowsTheFaceThroughTheGamesOwnHueShift()
    {
        // v0.111.0's settings scene: Mod Settings is tinted h=.82 under a dark green
        // outline, Reset h=.45 under (0.29, 0.147, 0.1421)
        var modSettings = new Color(0.1274f, 0.26f, 0.14066f);
        var reset = MyRunsSettingsArt.ShiftHue(modSettings, 0.82f, 0.45f);
        Assert.Equal(0.29, reset.R, 0.03);
        Assert.Equal(0.147, reset.G, 0.03);
        Assert.Equal(0.142, reset.B, 0.03);
        Assert.Equal(1f, reset.A);

        var unchanged = MyRunsSettingsArt.ShiftHue(modSettings, 0.82f, 0.82f);
        Assert.Equal(modSettings.R, unchanged.R, 0.001);
        Assert.Equal(modSettings.G, unchanged.G, 0.001);
        Assert.Equal(modSettings.B, unchanged.B, 0.001);
    }

    [Fact]
    public void RemoveWearsTheOutlineTurnedToItsOwnHue()
    {
        var nativeCaption = new Label();
        nativeCaption.AddThemeColorOverride("font_outline_color", new Color(0.1274f, 0.26f, 0.14066f));
        nativeCaption.AddThemeConstantOverride("outline_size", 12);
        var art = Art() with { ActionHue = 0.82f, CaptionOutline = GameTextOutline.From(nativeCaption) };
        var style = new GameTextStyle(null, 28);
        var text = new MyRunsSettingsText(style, style, style, style, style) { Art = art };
        var row = MyRunsSettingsRow.Build(
            MyRunsRow.For(new MyRunsFacts(Runs: 3, Bytes: 100, Keep: 20)),
            20, true, 1000, text, _ => { }, () => { }, _ => { }, _ => { });

        var expected = MyRunsSettingsArt.ShiftHue(
            new Color(0.1274f, 0.26f, 0.14066f), 0.82f, MyRunsSettingsArt.DestructiveHue);
        var outline = row.Remove.GetThemeColor("font_outline_color", "Button");
        Assert.Equal(expected.R, outline.R, 0.001);
        Assert.Equal(expected.G, outline.G, 0.001);
        Assert.Equal(expected.B, outline.B, 0.001);
        // A dark shade of burnt orange, not the green the anchor wears
        Assert.True(outline.R > outline.G && outline.G > outline.B);
        Assert.Equal(12, row.Remove.GetThemeConstant("outline_size", "Button"));
    }

    private static T Child<T>(Node parent, string name) where T : Node =>
        parent.GetChildren().OfType<T>().Single(child => child.Name.ToString() == name);

    [NativeSceneFact]
    public void CheckboxImagesAreTheSettingsScenesOwnImages()
    {
        var scene = NativeScenes.Read("res://scenes/ui/tickbox.tscn")!;
        var resources = NativeScenes.ExternalResources(scene).Values;
        Assert.Contains(MyRunsSettingsArt.TickedPath, resources);
        Assert.Contains(MyRunsSettingsArt.UntickedPath, resources);
    }
}

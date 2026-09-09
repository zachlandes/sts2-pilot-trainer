using Godot;
using MegaCrit.Sts2.Core.Localization.Fonts;

namespace Sts2PilotTrainer.Mod;

internal readonly record struct GameTextStyle(
    Font? Font,
    int Size,
    Font? BoldFont = null,
    bool? LocaleBold = null)
{
    internal T ApplyTo<T>(T control)
        where T : Control
    {
        if (control is RichTextLabel rich)
        {
            if (Font is { } font) rich.AddThemeFontOverride(GameText.RichFontEntry, font);
            if (BoldFont is { } boldFace) rich.AddThemeFontOverride(GameText.RichBoldFontEntry, boldFace);
            rich.AddThemeFontSizeOverride(GameText.RichFontSizeEntry, Size);
            rich.AddThemeFontSizeOverride(GameText.RichBoldFontSizeEntry, Size);
            return control;
        }

        if (Font is { } face) control.AddThemeFontOverride(GameText.FontEntry, face);
        control.AddThemeFontSizeOverride(GameText.FontSizeEntry, Size);
        if (LocaleBold is { } bold) GameText.ApplyLocale(control, bold);
        return control;
    }
}

internal static class GameText
{
    internal static readonly StringName FontEntry = "font";
    internal static readonly StringName FontSizeEntry = "font_size";
    internal static readonly StringName RichFontEntry = "normal_font";
    internal static readonly StringName RichFontSizeEntry = "normal_font_size";
    internal static readonly StringName RichBoldFontEntry = "bold_font";
    internal static readonly StringName RichBoldFontSizeEntry = "bold_font_size";

    private static readonly StringName LabelType = "Label";
    private static readonly StringName RichTextLabelType = "RichTextLabel";
    private static readonly Dictionary<string, PackedScene> Scenes = [];
    private static readonly Dictionary<NativeTextRole, GameTextStyle> SceneStyles = [];

    internal static GameTextStyle? Of(Node? node)
    {
        if (node is not Control control) return null;

        if (control.HasThemeFontOverride(FontEntry))
        {
            return new GameTextStyle(
                control.GetThemeFont(FontEntry, LabelType),
                DesignSize(control, FontSizeEntry, LabelType));
        }

        if (control.HasThemeFontOverride(RichFontEntry))
        {
            var bold = control.HasThemeFontOverride(RichBoldFontEntry)
                ? control.GetThemeFont(RichBoldFontEntry, RichTextLabelType)
                : null;
            return new GameTextStyle(
                control.GetThemeFont(RichFontEntry, RichTextLabelType),
                DesignSize(control, RichFontSizeEntry, RichTextLabelType),
                bold);
        }

        return null;
    }

    internal static GameTextStyle Require(Node? node, string role) =>
        Of(node) ?? throw new InvalidOperationException($"This build's {role} has no native text style.");

    internal static GameTextStyle Scene(NativeTextRole role)
    {
        if (SceneStyles.TryGetValue(role, out var style)) return style;

        var spec = Specs[role];
        var root = Load(spec.Scene).Instantiate();
        try
        {
            var control = root.GetNodeOrNull<Control>(spec.Node)
                ?? throw new InvalidOperationException(
                    $"This build's '{spec.Scene}' has no '{spec.Node}' text role.");
            style = Require(control, spec.Name) with { LocaleBold = spec.Bold };
            SceneStyles.Add(role, style);
            return style;
        }
        finally
        {
            root.Free();
        }
    }

    private static PackedScene Load(string path)
    {
        if (Scenes.TryGetValue(path, out var scene)) return scene;
        scene = ResourceLoader.Load<PackedScene>(path)
            ?? throw new InvalidOperationException($"This build has no '{path}' text resource.");
        Scenes.Add(path, scene);
        return scene;
    }

    private static int DesignSize(Control control, StringName sizeEntry, StringName type)
    {
        return control.Get("AutoSizeEnabled").AsBool()
            ? control.Get("MaxFontSize").AsInt32()
            : control.GetThemeFontSize(sizeEntry, type);
    }

    internal static void ApplyLocale(Control control, bool bold) =>
        FontControlUtils.ApplyLocaleFontSubstitution(
            control,
            bold ? FontType.Bold : FontType.Regular,
            FontEntry);

    private sealed record Spec(string Scene, string Node, string Name, bool Bold);

    private static readonly IReadOnlyDictionary<NativeTextRole, Spec> Specs =
        new Dictionary<NativeTextRole, Spec>
        {
            [NativeTextRole.PopupHeading] = new("res://scenes/ui/vertical_popup.tscn", "Header", "popup heading", true),
            [NativeTextRole.PopupBody] = new("res://scenes/ui/vertical_popup.tscn", "%Description", "popup body", false),
            [NativeTextRole.ButtonCaption] = new("res://scenes/ui/abandon_run_no_button.tscn", "%Label", "button caption", true),
            [NativeTextRole.ListNumeral] = new("res://scenes/screens/daily_run/daily_run_leaderboard_row.tscn", "FloorSection/Floor", "list numeral", true),
            [NativeTextRole.ListHeading] = new("res://scenes/screens/daily_run/daily_run_leaderboard_header.tscn", "Name", "list heading", true),
            [NativeTextRole.Secondary] = new("res://scenes/screens/main_menu/submenu_button_short.tscn", "Description", "secondary line", false),
            [NativeTextRole.RowTitle] = new("res://scenes/screens/main_menu/submenu_button_short.tscn", "Title", "row title", true),
            [NativeTextRole.Fact] = new("res://scenes/screens/run_history_screen/run_history.tscn", "%BuildLabel", "screen fact", false),
            [NativeTextRole.SectionHeading] = new("res://scenes/screens/game_over_screen.tscn", "%DiscoveryLabel", "section heading", true),
            [NativeTextRole.GroupHeading] = new("res://scenes/screens/run_history_screen/act_history_entry.tscn", "Title", "group heading", false),
            [NativeTextRole.FigureLabel] = new("res://scenes/screens/game_over_screen/score_line.tscn", "%Label", "figure label", true),
            [NativeTextRole.FigureValue] = new("res://scenes/screens/game_over_screen/score_line.tscn", "%Score", "figure value", true),
            [NativeTextRole.ChartNumeral] = new("res://scenes/screens/game_over_screen.tscn", "%ScoreProgress", "result numeral", true),
            [NativeTextRole.SettingsValue] = new("res://scenes/screens/settings_slider.tscn", "SliderValue", "settings value", false),
            [NativeTextRole.StepperNumeral] = new("res://scenes/screens/paginator.tscn", "LabelContainer/Mask/Label", "stepper numeral", true),
            [NativeTextRole.TooltipTitle] = new("res://scenes/ui/hover_tip.tscn", "%Title", "tooltip title", true),
            [NativeTextRole.TooltipBody] = new("res://scenes/ui/hover_tip.tscn", "%Description", "tooltip body", false),
            [NativeTextRole.TagNumeral] = new("res://scenes/ui/top_bar.tscn", "%AscensionLabel", "tag numeral", false),
            [NativeTextRole.Identity] = new("res://scenes/screens/main_menu/change_profile_button.tscn", "HBoxContainer/MarginContainer/VBoxContainer/Title", "identity name", true),
            [NativeTextRole.IdentityDescription] = new("res://scenes/screens/main_menu/change_profile_button.tscn", "HBoxContainer/MarginContainer/VBoxContainer/Description", "identity description", false),
            [NativeTextRole.DropdownValue] = new("res://scenes/screens/settings_dropdown.tscn", "Container/CurrentOption/Label", "dropdown value", true),
            [NativeTextRole.DropdownItem] = new("res://scenes/ui/dropdown_item.tscn", "Label", "dropdown item", true),
            [NativeTextRole.LedgerRow] = new("res://scenes/ui/map_point_history_hover_tip.tscn", "TextContainer/RewardStats/RewardRows/ObtainedRow1", "ledger row", false),
            [NativeTextRole.FloorNumeral] = new("res://scenes/ui/map_point_history_hover_tip.tscn", "TextContainer/TopContainer/Title", "floor numeral", true),
            [NativeTextRole.CardCaption] = new("res://scenes/screens/run_history_screen/deck_history_entry.tscn", "MarginContainer/Label", "card caption", false),
            [NativeTextRole.Input] = new("res://scenes/screens/card_library/card_library.tscn", "Sidebar/MarginContainer/TopVBox/SearchBar/TextArea", "input field", false),
            [NativeTextRole.Tickbox] = new("res://scenes/screens/card_library/rarity_tickbox.tscn", "Label", "tickbox label", true),
            [NativeTextRole.Footer] = new("res://scenes/screens/card_library/card_library.tscn", "%CardCountLabel", "list footer", false),
        };
}

internal enum NativeTextRole
{
    PopupHeading,
    PopupBody,
    ButtonCaption,
    ListNumeral,
    ListHeading,
    Secondary,
    RowTitle,
    Fact,
    SectionHeading,
    GroupHeading,
    FigureLabel,
    FigureValue,
    ChartNumeral,
    SettingsValue,
    StepperNumeral,
    TooltipTitle,
    TooltipBody,
    TagNumeral,
    Identity,
    IdentityDescription,
    DropdownValue,
    DropdownItem,
    LedgerRow,
    FloorNumeral,
    CardCaption,
    Input,
    Tickbox,
    Footer,
}

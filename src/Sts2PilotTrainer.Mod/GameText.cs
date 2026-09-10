using Godot;
using MegaCrit.Sts2.Core.Localization.Fonts;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2PilotTrainer.Mod;

internal readonly record struct GameTextStyle(
    Font? Font,
    int Size,
    Font? BoldFont = null,
    bool? LocaleBold = null)
{
    /// <summary>
    /// How tall a sentence stands once wrapped to a width, in the font that will draw
    /// it.
    ///
    /// Counting the newlines in the text is not enough: every sentence the mod draws
    /// wraps by width, so a panel sized from the text alone runs short and cuts the
    /// sentence off mid-word. Measured here in one place so a break flag changed for
    /// one surface reaches the others. With no font - which is a test, and nothing in
    /// the client - the caller's own estimate stands, because what a surface guesses
    /// there is its own business.
    /// </summary>
    internal float WrappedHeight(string text, float width, float fallbackHeight) =>
        Font is { } font
            ? font.GetMultilineStringSize(
                text,
                HorizontalAlignment.Left,
                width,
                Size,
                maxLines: -1,
                brkFlags: TextServer.LineBreakFlag.Mandatory | TextServer.LineBreakFlag.WordBound).Y
            : fallbackHeight;

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

    /// <summary>
    /// Resolves every role against this build, once, and says in the game's log which
    /// ones it could not answer.
    ///
    /// A role names a node in one of the game's own scenes, and a build that renamed
    /// or moved that node answers nothing. Before this ran, the first surface to ask
    /// for such a role threw where it asked - which was inside the recorded-fight
    /// journey, so one wrong path in this table said so in a message about text as a
    /// player was entering the fight. The refusal is unchanged; what this adds is that
    /// it is named here, once, ahead of the surface that asks.
    ///
    /// It is not called from the mod initializer, because the mod reads nothing at
    /// initialization: that runs one startup phase before the game exists and reading
    /// the game there ends the process. <see cref="RunmobileMod.EnsureAdopted"/> is the
    /// mod's first moment with a running game and is where this runs.
    /// See docs/in-game-host.md.
    ///
    /// Returns the roles this build could not answer, which is what a test reads.
    /// </summary>
    internal static IReadOnlyList<NativeTextRole> Verify() => Verify(Read);

    /// <inheritdoc cref="Verify()"/>
    /// <param name="read">Where a role's style comes from. The game's own scene files
    /// in the client; a test supplies its own, because there is no Godot to load a
    /// scene from without one.</param>
    internal static IReadOnlyList<NativeTextRole> Verify(Func<NativeTextRole, GameTextStyle> read)
    {
        List<NativeTextRole> refused = [];
        foreach (var role in Specs.Keys)
        {
            try
            {
                Resolve(role, read);
            }
            catch (Exception ex)
            {
                // Per role, so one path this build has lost does not end the sweep and
                // leave the rest of the table unread.
                refused.Add(role);
                Log.Error(
                    $"[{RunmobileMod.ModId}] this build has no {Specs[role].Name} " +
                    $"('{Specs[role].Node}' in '{Specs[role].Scene}'): {ex.Message}", 2);
            }
        }

        return refused;
    }

    /// <summary>
    /// The style a role draws at on this build.
    ///
    /// A role this build cannot answer refuses here, and the surface that asked refuses
    /// with it: missing native furniture is never substituted for. <see cref="Verify"/>
    /// has normally already asked, so this is a dictionary lookup by the time a surface
    /// is drawn.
    /// </summary>
    internal static GameTextStyle Scene(NativeTextRole role) => Resolve(role, Read);

    /// <summary>Forgets what was read off this build. For a test that stands in for
    /// the game's scene files and must not leave its answers behind.</summary>
    internal static void Forget()
    {
        SceneStyles.Clear();
        Scenes.Clear();
    }

    private static GameTextStyle Resolve(NativeTextRole role, Func<NativeTextRole, GameTextStyle> read)
    {
        if (SceneStyles.TryGetValue(role, out var style)) return style;

        style = read(role);
        SceneStyles.Add(role, style);
        return style;
    }

    /// <summary>
    /// One role, off the game's own scene. Nothing outlives the read: a role is a font
    /// and a size, and the tree it was copied from is the game's, not this mod's.
    /// </summary>
    private static GameTextStyle Read(NativeTextRole role)
    {
        var spec = Specs[role];
        var root = Load(spec.Scene).Instantiate();
        try
        {
            var control = root.GetNodeOrNull<Control>(spec.Node)
                ?? throw new InvalidOperationException(
                    $"This build's '{spec.Scene}' has no '{spec.Node}' text role.");
            return Require(control, spec.Name) with { LocaleBold = spec.Bold };
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

    /// <summary>
    /// Every role and the node it names, for a test that reads the game's own scene
    /// files off this build. A wrong path here is invisible to everything else until
    /// the surface that asks for it is drawn, and one of them cost a player the
    /// recorded fight they were entering.
    /// </summary>
    internal static IEnumerable<(NativeTextRole Role, string Scene, string Node, string Name)> Declarations =>
        Specs.Select(pair => (pair.Key, pair.Value.Scene, pair.Value.Node, pair.Value.Name));

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
            [NativeTextRole.TooltipTitle] = new("res://scenes/ui/hover_tip.tscn", "%Title", "tooltip title", true),
            [NativeTextRole.TooltipBody] = new("res://scenes/ui/hover_tip.tscn", "%Description", "tooltip body", false),
            [NativeTextRole.TagNumeral] = new("res://scenes/ui/top_bar.tscn", "%AscensionLabel", "tag numeral", false),
            [NativeTextRole.Identity] = new("res://scenes/screens/main_menu/change_profile_button.tscn", "HBoxContainer/MarginContainer/VBoxContainer/Title", "identity name", true),
            [NativeTextRole.IdentityDescription] = new("res://scenes/screens/main_menu/change_profile_button.tscn", "HBoxContainer/MarginContainer/VBoxContainer/Description", "identity description", false),
            [NativeTextRole.DropdownValue] = new("res://scenes/screens/settings_dropdown.tscn", "Container/CurrentOption/Label", "dropdown value", true),
            [NativeTextRole.DropdownItem] = new("res://scenes/ui/dropdown_item.tscn", "Label", "dropdown item", true),
            [NativeTextRole.LedgerRow] = new("res://scenes/ui/map_point_history_hover_tip.tscn",
                "TextContainer/TopContainer/RewardStats/RewardRows/ObtainedRow1", "ledger row", false),
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

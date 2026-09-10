using Godot;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

internal readonly record struct MyRunsSettingsText(
    GameTextStyle Row,
    GameTextStyle Numeral,
    GameTextStyle Reading,
    GameTextStyle Detail,
    GameTextStyle Button)
{
    internal MyRunsSettingsArt? Art { get; init; }
}

/// <summary>
/// Runmobile's settings row, drawn: what the player keeps, what it takes on this
/// computer, the way to take it back, whether community runs are shown, and whether
/// Runmobile puts a row on the game's main menu.
///
/// It is a row rather than a section. Runmobile's settings section belongs to the run
/// library, which owns where it hangs off the game's own modding entry point and what
/// else sits in it; this is one thing that section puts in itself, built whole so the
/// section places it and nothing more. Keeping it apart is also what lets it be
/// assembled and asserted on in a process with no game.
///
/// It computes nothing. <see cref="MyRunsRow"/> says what every line reads and whether
/// each control may be pressed, <see cref="Apply"/> is the only way anything
/// here changes, and every control reports what a player did rather than acting on
/// it. That is the rule the transport answers to and it is here for the same reason: a
/// reading of the disk taken before an act and a receipt taken after it are two facts,
/// and a surface that let each control set its own label would eventually show one
/// beside the other.
///
/// <para><b>The keep control is a stepper and the design says slider.</b> A deliberate
/// substitution, not a shortfall: Godot draws a slider's grabber from a theme
/// <em>icon</em>, so a slider here would either wear the engine's default grey on a
/// screen made of torn stone, or need a piece of art this mod does not ship - and the
/// game's own <c>NSettingsSlider</c> cannot be had outside the settings scene it lives
/// in. What the design settles is the label and the numeral, and both are exactly as
/// written. How the number moves is presentation, which
/// docs/mod-ui-direction.md puts on this side of the line.</para>
///
/// The heading scopes the group without repeating the mod on every setting.
/// Native button and tickbox images distinguish acts from persistent settings.
/// Input stays on stock controls: duplicating Credits or Reset would also duplicate
/// their retail commands, while this row must only report to its existing owner.
///
/// Built from stock Godot nodes for the reason the transport and the result panel are:
/// this assembly has no Godot source generators, so a <c>Control</c> subclass of ours
/// would never have its overrides called.
/// </summary>
internal sealed class MyRunsSettingsRow
{
    internal const string RootName = "RunmobileMyRuns";

    // ── The palette ────────────────────────────────────────────────────────
    //
    // The game's own colours, the same values the transport's tag is drawn in. Held
    // here rather than shared with it because the tag's palette is the tag's material
    // - docs/mod-ui-direction.md owns that - and a settings row that followed a
    // redesign of the transport would be a row nobody asked to redesign.

    private static readonly Color Cream = Rgb(0xf1, 0xe4, 0xc0);
    private static readonly Color Muted = Rgb(0xa8, 0x9f, 0x8c);
    private static readonly Color Gold = Rgb(0xd9, 0xb2, 0x5f);
    private static readonly Color RuleLine = new(0.909804f, 0.862745f, 0.745098f, 0.25098f);
    private static readonly Color ButtonFace = Rgb(0x2a, 0x26, 0x2c);
    private static readonly Color ButtonEdge = Rgb(0x4a, 0x43, 0x40);
    private static readonly Color DisabledFace = Rgb(0x22, 0x1f, 0x24);
    private static readonly Color DisabledEdge = Rgb(0x2e, 0x2a, 0x2c);
    private static readonly Color Dim = Rgb(0x6a, 0x62, 0x59);

    // ── The layout, as multiples of the text in it ──────────────────────────
    //
    // Every box here is a multiple of the settings screen's own text size rather than a
    // number, so a native-role or game-build change moves the words and their boxes
    // together. A row written in constants can keep the game's words while leaving the
    // boxes around them at an obsolete size.

    private const float LabelHeightRatio = 1.5f;

    /// <summary>Clear space separates the heading, stepper and disk reading</summary>
    private const float StepperPadRatio = 0.55f;
    private const float NoteHeightRatio = 1.5f;
    private const float RuleGapRatio = 0.95f;
    private const float StepSizeRatio = 1.6f;
    private const float StepGapRatio = 0.4f;
    private const float NumeralWidthRatio = 2.3f;
    private const float RemoveHeightRatio = 2f;
    private const float ControlGapRatio = 0.7f;
    private const float FetchGapRatio = 0.7f;
    private const float FetchHeightRatio = 2f;
    private const float MainMenuGapRatio = 0.4f;
    private const float MainMenuHeightRatio = 2f;

    private static readonly StringName FontColour = "font_color";

    /// <summary>The native settings row and button styles this row copies.</summary>
    private readonly MyRunsSettingsText _text;

    private readonly Control _root;
    private readonly Label _heading;
    private readonly Label _removeLabel;
    private readonly Label _fetchLabel;
    private readonly Label _mainMenuLabel;
    private readonly TextureRect _removeArt;
    private readonly TextureRect _fetchArt;
    private readonly TextureRect _mainMenuArt;
    private readonly Label _keepLabel;
    private readonly Label _keepNumeral;
    private readonly Button _fewer;
    private readonly Button _more;
    private readonly Line2D _rule;
    private readonly Label _reading;
    private readonly Label _detail;
    private readonly Button _remove;
    private readonly Button _fetch;
    private readonly Button _mainMenu;

    /// <summary>
    /// What the row currently says.
    ///
    /// A reference-typed field on purpose, like every other cross-assembly field in
    /// this assembly: the game enumerates these types before the mod initializer runs,
    /// and a field whose layout needs a sibling assembly's value type resolved takes
    /// the whole mod down one phase before it knows where its siblings are. See
    /// docs/in-game-host.md, and run <c>ModAssemblyLoadOrderTests</c> rather than
    /// judging a new field's shape.
    /// </summary>
    private MyRunsRow _row;

    /// <summary>
    /// The policy the stepper is standing on, as a plain int so nothing here holds a
    /// sibling type it does not have to.
    ///
    /// Written only by <see cref="Apply"/>, which is what keeps it and the numeral
    /// beside it the same number: a stepper that advanced itself on the press would be
    /// a run ahead of the file whenever the write failed, and the row would refuse an
    /// end the policy had not reached.
    /// </summary>
    private int _keep;
    private bool _fetchIndex;
    private float _layoutWidth;

    private MyRunsSettingsRow(Nodes nodes)
    {
        _root = nodes.Root;
        _heading = nodes.Heading;
        _removeLabel = nodes.RemoveLabel;
        _fetchLabel = nodes.FetchLabel;
        _mainMenuLabel = nodes.MainMenuLabel;
        _removeArt = nodes.RemoveArt;
        _fetchArt = nodes.FetchArt;
        _mainMenuArt = nodes.MainMenuArt;
        _keepLabel = nodes.KeepLabel;
        _keepNumeral = nodes.KeepNumeral;
        _fewer = nodes.Fewer;
        _more = nodes.More;
        _rule = nodes.Rule;
        _reading = nodes.Reading;
        _detail = nodes.Detail;
        _remove = nodes.Remove;
        _fetch = nodes.Fetch;
        _mainMenu = nodes.MainMenu;
        _row = nodes.Row;
        _keep = nodes.Keep;
        _fetchIndex = nodes.FetchRunIndex;
        _text = nodes.Text;
        _layoutWidth = _root.Size.X;
        _root.Resized += OnRootResized;
    }

    internal Control Root => _root;

    /// <summary>The destructive control. Named so a host can put focus somewhere
    /// else.</summary>
    internal Button Remove => _remove;

    internal Button Fewer => _fewer;

    internal Button More => _more;

    internal Button Fetch => _fetch;

    /// <summary>The switch that puts Runmobile on the game's main menu, or takes it
    /// off. Named so a host can assert on it without reaching into the tree.</summary>
    internal Button MainMenu => _mainMenu;

    /// <summary>What the row is saying, for a host that has to ask rather than
    /// re-derive.</summary>
    internal MyRunsRow Row => _row;

    /// <summary>
    /// Lays the row out again at a width that is known this time.
    ///
    /// Wanted because a settings screen has not been laid out when its <c>_Ready</c>
    /// runs: every control still carries the size its scene was saved at, and the
    /// column's real width arrives a frame later when the container sorts its children.
    /// A row built from the first reading is built against a number that was never the
    /// answer - which is how a row whose words are the game's own size came to clip them
    /// mid-word, measured in the retail client.
    ///
    /// Nothing here re-derives what the row says. It is the same nodes, placed again.
    /// </summary>
    internal void Relayout(float width)
    {
        if (width <= 0f) return;

        _layoutWidth = width;
        // The height is a minimum and the width never is. A container gives a child at
        // least its minimum, so a row that asked for a width would widen the game's own
        // settings list to match - which it did, in the retail client, dragging every one
        // of the game's rows out to the edge of the screen. The row asks for the room it
        // needs downward and takes its width from what it was given.
        _root.Size = new Vector2(width, HeightFor(_text));
        _root.CustomMinimumSize = new Vector2(0f, HeightFor(_text));
        Layout(width);
    }

    private void OnRootResized()
    {
        var width = _root.Size.X;
        if (width <= 0f || width == _layoutWidth) return;
        Relayout(width);
    }

    /// <summary>
    /// How tall the row is at the width it was built for. A section stacks what it
    /// hosts, so it has to be told.
    ///
    /// The readings take the whole column, and each control gets its own row below
    /// them so native-sized text never takes the readings' room.
    /// </summary>
    internal float Height => HeightFor(_text);

    /// <inheritdoc cref="Height"/>
    internal static float HeightFor(MyRunsSettingsText text)
    {
        var label = Math.Max(text.Row.Size, text.Numeral.Size) * LabelHeightRatio;
        var reading = text.Reading.Size * LabelHeightRatio;
        var note = text.Detail.Size * NoteHeightRatio;
        var remove = RemoveHeight(text);
        return (text.Button.Size * LabelHeightRatio) + (text.Row.Size * StepperPadRatio * 2f) +
            (text.Row.Size * StepperPadRatio * 2f) + label +
            (text.Row.Size * RuleGapRatio) + reading + note +
            (text.Row.Size * ControlGapRatio) + remove +
            (text.Row.Size * FetchGapRatio) + (text.Button.Size * FetchHeightRatio) +
            (text.Row.Size * MainMenuGapRatio) + (text.Button.Size * MainMenuHeightRatio);
    }

    /// <summary>
    /// Assembles the row.
    /// </summary>
    /// <param name="row">What it says to begin with.</param>
    /// <param name="keep">The policy the stepper starts on - the player's own number,
    /// which may be larger than anything a press would reach it at.</param>
    /// <param name="width">How wide the section is laying it out, in engine
    /// units.</param>
    /// <param name="text">The native row and button styles from this settings screen.</param>
    /// <param name="keepChanged">What to do when the player moves the policy. The row
    /// reports and does not act: what a new policy means for the disk is the retention
    /// owner's, and the host re-derives and calls <see cref="Apply"/>.</param>
    /// <param name="removePressed">What to do when the player asks for every run to
    /// go. Same rule: this raises it, and does not remove anything itself.</param>
    internal static MyRunsSettingsRow Build(
        MyRunsRow row, int keep, bool fetchRunIndex, float width, MyRunsSettingsText text,
        Action<int> keepChanged, Action removePressed, Action<bool> fetchChanged,
        Action<bool> mainMenuChanged)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(keepChanged);
        ArgumentNullException.ThrowIfNull(removePressed);
        ArgumentNullException.ThrowIfNull(fetchChanged);
        ArgumentNullException.ThrowIfNull(mainMenuChanged);

        var art = text.Art ?? throw new InvalidOperationException("Native settings art was not supplied.");
        var height = HeightFor(text);
        var root = new Control
        {
            Name = RootName,
            Position = Vector2.Zero,
            Size = new Vector2(width, height),
            CustomMinimumSize = new Vector2(0f, height),
            // The section underneath owns everything this row is not standing on.
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        var nodes = new Nodes
        {
            Root = root,
            Row = row,
            Keep = keep,
            FetchRunIndex = fetchRunIndex,
            Text = text,
            Heading = Add(root, Text("Heading", text.Button, Cream)),
            RemoveLabel = Add(root, Text("RemoveLabel", text.Row, Cream)),
            FetchLabel = Add(root, Text("FetchLabel", text.Row, Cream)),
            MainMenuLabel = Add(root, Text("MainMenuLabel", text.Row, Cream)),
            KeepLabel = Add(root, Text("KeepLabel", text.Row, Cream)),
            KeepNumeral = Add(root, Text("KeepNumeral", text.Numeral, Cream)),
            Rule = Add(root, new Line2D { Name = "Rule", DefaultColor = RuleLine, Width = 2f }),
            Reading = Add(root, Text("Reading", text.Reading, Cream)),
            Detail = Add(root, Text("Detail", text.Detail, Muted)),
        };

        nodes.Fewer = Add(root, Pressable("Fewer", "−", text.Numeral));
        nodes.More = Add(root, Pressable("More", "+", text.Numeral));
        nodes.Remove = Add(root, Pressable("Remove", string.Empty, text.Button));
        nodes.Fetch = Add(root, Pressable("FetchRunIndex", string.Empty, text.Button));
        nodes.MainMenu = Add(root, Pressable("MainMenuRow", string.Empty, text.Button));

        nodes.Heading.Text = LibraryCopy.SettingsHeading;
        nodes.FetchLabel.Text = LibraryCopy.ShowCommunityRuns;
        nodes.MainMenuLabel.Text = LibraryCopy.ShowOnMainMenu;
        nodes.RemoveArt = Image(nodes.Remove, art.Action);
        nodes.RemoveArt.Material = art.DestructiveMaterial();
        nodes.RemoveArt.ShowBehindParent = true;
        nodes.FetchArt = Image(nodes.Fetch, art.Ticked);
        nodes.MainMenuArt = Image(nodes.MainMenu, art.Unticked);

        var built = new MyRunsSettingsRow(nodes);

        // Wired after construction so each handler reads the row's own current policy
        // rather than the number it was built with. A stepper that closed over its
        // starting value is the same defect the transport's one derivation exists to
        // prevent, one control down.
        built._fewer.Pressed += () => built.Step(-1, keepChanged);
        built._more.Pressed += () => built.Step(1, keepChanged);
        built._remove.Pressed += removePressed;
        built._fetch.Pressed += () => fetchChanged(!built._fetchIndex);

        // Reads the row's own current answer at press time rather than the one it was
        // built with, for the reason the stepper does: what is on the menu can change
        // under this screen, and a handler that closed over the starting value would ask
        // for a state the row had already left.
        built._mainMenu.Pressed += () => mainMenuChanged(!built._row.MainMenu.Shown);
        built.Apply(row, keep, fetchRunIndex);
        return built;
    }

    /// <summary>
    /// Says what the row now says.
    ///
    /// The one way anything here changes, and it re-reads every element rather than the
    /// ones a caller thinks moved: the reading, the receipt and the policy can all
    /// change from one act.
    ///
    /// <paramref name="keep"/> is the number <paramref name="row"/> was derived from,
    /// passed rather than parsed back out of the numeral so the stepper's ends and the
    /// numeral can never be two different answers.
    /// </summary>
    internal void Apply(MyRunsRow row, int keep) => Apply(row, keep, _fetchIndex);

    internal void Apply(MyRunsRow row, int keep, bool fetchRunIndex)
    {
        ArgumentNullException.ThrowIfNull(row);
        _row = row;
        _keep = keep;
        _fetchIndex = fetchRunIndex;

        _keepLabel.Text = row.KeepLabel;
        _keepNumeral.Text = row.KeepNumeral;
        _reading.Text = row.Reading;
        _detail.Text = row.Detail;
        _removeLabel.Text = row.RemoveLabel;
        _remove.Text = LibraryCopy.Remove;
        // No tooltips: Godot draws its own in the engine's default theme, which is the
        // one thing on this screen that is not the game's, and the first open of the
        // screen left one standing wherever the mouse was before the column sorted.
        // Each control's label sits beside it and each switch shows its own state
        _fetchArt.Texture = fetchRunIndex ? _text.Art!.Ticked : _text.Art!.Unticked;
        _mainMenuArt.Texture = row.MainMenu.Shown ? _text.Art!.Ticked : _text.Art!.Unticked;

        // The stepper refuses at its bottom rather than disappearing there, so the two
        // controls never move about under the player's aim. There is no top: a policy
        // has a minimum and the player's own number above it is theirs.
        Refuse(_fewer, !row.KeepPressable || _keep <= MyRunsRow.MinimumKeep);
        Refuse(_more, !row.KeepPressable);
        Refuse(_remove, !row.RemovePressable);
        Refuse(_fetch, !row.KeepPressable);
        Refuse(_mainMenu, !row.MainMenuPressable);

        Face(_fewer, !_fewer.Disabled);
        Face(_more, !_more.Disabled);
        NativeFace(_remove, _removeArt);
        _text.Art!.DestructiveOutline()?.ApplyTo(_remove);
        NativeFace(_fetch, _fetchArt);
        NativeFace(_mainMenu, _mainMenuArt);

        Layout(_root.Size.X);
    }

    /// <summary>
    /// Reports the number one press would move the policy to, and moves nothing
    /// itself.
    ///
    /// One run, from wherever the file already stands: a file that keeps five hundred
    /// steps to four hundred and ninety-nine, because nothing here may quietly rewrite a
    /// larger policy into a smaller one. Only the bottom is held, and a policy this
    /// build could not read is not stepped at all.
    /// </summary>
    private void Step(int by, Action<int> keepChanged)
    {
        if (!_row.KeepPressable) return;

        var next = Math.Max(MyRunsRow.MinimumKeep, _keep + by);
        if (next == _keep) return;

        keepChanged(next);
    }

    /// <summary>
    /// Lays the row out at the width the section gave it.
    ///
    /// The policy and disk readings take the whole column, with their controls aligned
    /// to the right edge like the native rows around them.
    /// </summary>
    private void Layout(float width)
    {
        var fullWidth = width;
        var insets = _text.Art!.Insets;
        width = Math.Max(0f, width - insets.X - insets.Y);
        var unit = _text.Row.Size;
        var labelHeight = Math.Max(_text.Row.Size, _text.Numeral.Size) * LabelHeightRatio;
        var readingHeight = _text.Reading.Size * LabelHeightRatio;
        var noteHeight = _text.Detail.Size * NoteHeightRatio;
        var stepSize = unit * StepSizeRatio;
        var stepGap = unit * StepGapRatio;
        var numeralWidth = unit * NumeralWidthRatio;
        var ruleGap = unit * RuleGapRatio;
        var removeHeight = RemoveHeight(_text);

        var stepper = (stepSize * 2) + numeralWidth + (stepGap * 2);
        var removeWidth = Math.Min(width, removeHeight * ActionAspect(_text));

        var headingHeight = _text.Button.Size * LabelHeightRatio;
        Place(_heading, 0f, unit * StepperPadRatio, width, headingHeight);
        var headingSpace = headingHeight + unit * StepperPadRatio * 2f;
        var pad = unit * StepperPadRatio;
        Place(_keepLabel, 0f, headingSpace + pad, width - stepper - stepGap, labelHeight);
        Place(_fewer, width - stepper, headingSpace + pad + ((labelHeight - stepSize) / 2f), stepSize, stepSize);
        Place(_keepNumeral, width - stepper + stepSize + stepGap, headingSpace + pad, numeralWidth, labelHeight);
        _keepNumeral.HorizontalAlignment = HorizontalAlignment.Center;
        Place(_more, width - stepSize, headingSpace + pad + ((labelHeight - stepSize) / 2f), stepSize, stepSize);

        _rule.Points = [Vector2.Zero, new Vector2(fullWidth, 0f)];

        var lower = headingSpace + (pad * 2f) + labelHeight + ruleGap;
        Place(_reading, 0f, lower, width, readingHeight);
        Place(_detail, 0f, lower + readingHeight, width, noteHeight);

        var removeY = lower + readingHeight + noteHeight + (unit * ControlGapRatio);
        Place(_remove, width - removeWidth, removeY, removeWidth, removeHeight);
        Place(_removeLabel, 0f, removeY, Math.Max(0f, width - removeWidth - stepGap), removeHeight);

        var fetchHeight = _text.Button.Size * FetchHeightRatio;
        var fetchWidth = fetchHeight;
        var fetchY = removeY + removeHeight + (unit * FetchGapRatio);
        var controlCentre = width - Math.Min(width, _text.Art!.ActionSize.X) / 2f;
        Place(_fetch, controlCentre - fetchWidth / 2f, fetchY, fetchWidth, fetchHeight);

        Place(_fetchLabel, 0f, fetchY, _fetch.Position.X - stepGap, fetchHeight);
        var mainMenuWidth = _text.Button.Size * MainMenuHeightRatio;
        Place(
            _mainMenu,
            controlCentre - mainMenuWidth / 2f,
            fetchY + fetchHeight + (unit * MainMenuGapRatio),
            mainMenuWidth,
            _text.Button.Size * MainMenuHeightRatio);
        Place(_mainMenuLabel, 0f, _mainMenu.Position.Y, _mainMenu.Position.X - stepGap, _mainMenu.Size.Y);
        Place(_removeArt, 0f, 0f, _remove.Size.X, _remove.Size.Y);
        Place(_fetchArt, 0f, 0f, _fetch.Size.X, _fetch.Size.Y);
        Place(_mainMenuArt, 0f, 0f, _mainMenu.Size.X, _mainMenu.Size.Y);
        // The native entry's margins align every row, not just its button
        foreach (var child in _root.GetChildren().OfType<Control>())
            child.Position += new Vector2(insets.X, 0f);
    }

    private static void Refuse(Button button, bool refused)
    {
        button.Disabled = refused;
        // Godot stops hit-testing a disabled control, so a refused button is still on
        // the surface and still holds its place; it simply cannot be reached.
        button.FocusMode = refused ? Control.FocusModeEnum.None : Control.FocusModeEnum.All;
    }

    private static void Face(Button button, bool enabled)
    {
        var style = new StyleBoxFlat
        {
            BgColor = enabled ? ButtonFace : DisabledFace,
            BorderColor = enabled ? ButtonEdge : DisabledEdge,
        };
        style.SetCornerRadiusAll(4);
        style.SetBorderWidthAll(1);
        foreach (var state in new[] { "normal", "pressed", "disabled" })
        {
            button.AddThemeStyleboxOverride(state, style);
        }

        Lit(button, Gold);
        button.AddThemeColorOverride(FontColour, enabled ? Cream : Dim);
    }

    private static float ActionAspect(MyRunsSettingsText text) =>
        text.Art!.ActionSize.X / text.Art.ActionSize.Y;

    private static float RemoveHeight(MyRunsSettingsText text) => Math.Max(
        text.Button.Size * RemoveHeightRatio,
        text.Art!.ActionSize.Y);

    private static void NativeFace(Button button, TextureRect image)
    {
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "focus" })
            button.AddThemeStyleboxOverride(state, new StyleBoxEmpty());
        image.Modulate = button.Disabled ? Dim : Colors.White;
        foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
            button.AddThemeColorOverride(state, Cream);
        button.AddThemeColorOverride("font_disabled_color", Dim);
    }

    private static TextureRect Image(Button button, Texture2D texture)
    {
        var image = Add(button, new TextureRect
        {
            Name = "NativeArt",
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = texture,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        // Focus and hover light only this control's borrowed art
        button.MouseEntered += () => { if (!button.Disabled) image.Modulate = new Color(1.2f, 1.2f, 1.2f); };
        button.MouseExited += () => image.Modulate = button.Disabled ? Dim : Colors.White;
        button.FocusEntered += () => { if (!button.Disabled) image.Modulate = new Color(1.2f, 1.2f, 1.2f); };
        button.FocusExited += () => image.Modulate = button.Disabled ? Dim : Colors.White;
        return image;
    }

    /// <summary>Hover and focus take a rim, which is the game's own language for "this
    /// is the thing you are about to press".</summary>
    private static void Lit(Button button, Color colour)
    {
        var lit = new StyleBoxFlat { BgColor = Rgb(0x3a, 0x33, 0x38), BorderColor = colour };
        lit.SetCornerRadiusAll(4);
        lit.SetBorderWidthAll(2);
        button.AddThemeStyleboxOverride("hover", lit);
        button.AddThemeStyleboxOverride("focus", lit);
    }

    private static T Add<T>(Node parent, T child) where T : Node
    {
        parent.AddChild(child);
        return child;
    }

    private static Button Pressable(string name, string label, GameTextStyle text)
    {
        var button = new Button
        {
            Name = name,
            Text = label,
            // Clipped for the reason the labels are: a Button's own minimum width is
            // its unwrapped label, so one whose word outgrows its box widens itself
            // back out of the row rather than being cut off inside it.
            ClipText = true,
            // Takes focus on purpose: a control a keyboard or a controller cannot
            // reach is a control half the players do not have.
            FocusMode = Control.FocusModeEnum.All,
        };

        text.ApplyTo(button);
        return button;
    }

    private static Label Text(string name, GameTextStyle style, Color colour)
    {
        var label = new Label
        {
            Name = name,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
        };

        style.ApplyTo(label);
        label.AddThemeColorOverride(FontColour, colour);
        return label;
    }

    /// <summary>
    /// Puts a control in its box.
    ///
    /// The minimum size is set as well as the size, in that order, for the reason the
    /// transport's own <c>Place</c> records: a Control is clamped up to its minimum, and
    /// a label's minimum width is its whole unwrapped line, so a label given a width
    /// alone widens itself straight back out of the row.
    /// </summary>
    private static void Place(Control control, float x, float y, float width, float height)
    {
        control.Position = new Vector2(x, y);
        control.CustomMinimumSize = new Vector2(width, 0);
        control.Size = new Vector2(width, height);
    }

    private static Color Rgb(int red, int green, int blue, float alpha = 1f) =>
        new(red / 255f, green / 255f, blue / 255f, alpha);

    /// <summary>Every node the row keeps, gathered so the constructor is a list of
    /// assignments rather than a dozen parameters.</summary>
    private sealed class Nodes
    {
        internal required Control Root { get; init; }

        internal required MyRunsRow Row { get; init; }

        internal required int Keep { get; init; }

        internal required bool FetchRunIndex { get; init; }

        internal required MyRunsSettingsText Text { get; init; }

        internal required Label Heading { get; init; }
        internal required Label RemoveLabel { get; init; }
        internal required Label FetchLabel { get; init; }
        internal required Label MainMenuLabel { get; init; }
        internal TextureRect RemoveArt { get; set; } = null!;
        internal TextureRect FetchArt { get; set; } = null!;
        internal TextureRect MainMenuArt { get; set; } = null!;

        internal required Label KeepLabel { get; init; }

        internal required Label KeepNumeral { get; init; }

        internal required Line2D Rule { get; init; }

        internal required Label Reading { get; init; }

        internal required Label Detail { get; init; }

        internal Button Fewer { get; set; } = null!;

        internal Button More { get; set; } = null!;

        internal Button Remove { get; set; } = null!;

        internal Button Fetch { get; set; } = null!;

        internal Button MainMenu { get; set; } = null!;
    }
}

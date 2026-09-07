using Godot;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The settings row about the player's own runs, drawn: what they keep, what it takes
/// on this computer, and the way to take it back.
///
/// It is a row rather than a section. Runmobile's settings section belongs to the run
/// library, which owns where it hangs off the game's own modding entry point and what
/// else sits in it; this is one thing that section puts in itself, built whole so the
/// section places it and nothing more. Keeping it apart is also what lets it be
/// assembled and asserted on in a process with no game.
///
/// It computes nothing. <see cref="MyRunsRow"/> says what every line reads and whether
/// the destructive control may be pressed, <see cref="Apply"/> is the only way anything
/// here changes, and the two controls report what a player did rather than acting on
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
/// <para><b>The removal is a stock button and the design says the game's red
/// ribbon.</b> The same case as the stepper. The game assembly ships no ribbon node -
/// <em>ribbon</em> is the design's word for the button bar the game's own popup draws -
/// and the game's settings furniture, <c>NSettingsButton</c> and its siblings, is
/// scene-resident and cannot be had standalone, so a hand-rolled row has nothing to
/// duplicate. The red is carried instead, by <see cref="Destructive"/>; the design's
/// ribbon material is where it actually exists, on the game's own popup behind the
/// press.</para>
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
    private static readonly Color Red = Rgb(0xc8, 0x46, 0x3a);
    private static readonly Color RedFace = Rgb(0x3a, 0x1d, 0x1a);
    private static readonly Color RuleLine = Rgb(0x3a, 0x33, 0x30);
    private static readonly Color ButtonFace = Rgb(0x2a, 0x26, 0x2c);
    private static readonly Color ButtonEdge = Rgb(0x4a, 0x43, 0x40);
    private static readonly Color DisabledFace = Rgb(0x22, 0x1f, 0x24);
    private static readonly Color DisabledEdge = Rgb(0x2e, 0x2a, 0x2c);
    private static readonly Color Dim = Rgb(0x6a, 0x62, 0x59);

    // ── The layout, in the design's own reference units ─────────────────────

    private const float LabelHeight = 22f;
    private const float NoteHeight = 18f;
    private const float RuleGap = 14f;
    private const float StepSize = 24f;
    private const float StepGap = 6f;
    private const float NumeralWidth = 34f;
    private const float RemoveWidth = 168f;
    private const float RemovePad = 14f;
    private const float RemoveHeight = 30f;
    private const float FetchGap = 10f;
    private const float FetchHeight = 30f;

    private const int LabelFontSize = 15;
    private const int NoteFontSize = 12;
    private const int NumeralFontSize = 15;
    private const int RemoveFontSize = 13;

    private static readonly StringName FontColour = "font_color";

    private readonly Control _root;
    private readonly Label _keepLabel;
    private readonly Label _keepNumeral;
    private readonly Button _fewer;
    private readonly Button _more;
    private readonly Line2D _rule;
    private readonly Label _reading;
    private readonly Label _detail;
    private readonly Button _remove;
    private readonly Button _fetch;

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

    private MyRunsSettingsRow(Nodes nodes)
    {
        _root = nodes.Root;
        _keepLabel = nodes.KeepLabel;
        _keepNumeral = nodes.KeepNumeral;
        _fewer = nodes.Fewer;
        _more = nodes.More;
        _rule = nodes.Rule;
        _reading = nodes.Reading;
        _detail = nodes.Detail;
        _remove = nodes.Remove;
        _fetch = nodes.Fetch;
        _row = nodes.Row;
        _keep = nodes.Keep;
        _fetchIndex = nodes.FetchRunIndex;
    }

    internal Control Root => _root;

    /// <summary>The destructive control. Named so a host can put focus somewhere
    /// else.</summary>
    internal Button Remove => _remove;

    internal Button Fewer => _fewer;

    internal Button More => _more;

    internal Button Fetch => _fetch;

    /// <summary>What the row is saying, for a host that has to ask rather than
    /// re-derive.</summary>
    internal MyRunsRow Row => _row;

    /// <summary>
    /// How tall the row is at the width it was built for. A section stacks what it
    /// hosts, so it has to be told.
    ///
    /// The lower line is as tall as the taller of its two things rather than as tall as
    /// both: the reading and its note are stacked, and the destructive control sits
    /// beside them rather than under them.
    /// </summary>
    internal static float Height =>
        LabelHeight + RuleGap + Math.Max(LabelHeight + NoteHeight, RemoveHeight) + FetchGap + FetchHeight;

    /// <summary>
    /// Assembles the row.
    /// </summary>
    /// <param name="row">What it says to begin with.</param>
    /// <param name="keep">The policy the stepper starts on - the player's own number,
    /// which may be larger than anything a press would reach it at.</param>
    /// <param name="width">How wide the section is laying it out, in engine
    /// units.</param>
    /// <param name="font">The font the game's own labels use, or null to leave the
    /// theme's default in place.</param>
    /// <param name="keepChanged">What to do when the player moves the policy. The row
    /// reports and does not act: what a new policy means for the disk is the retention
    /// owner's, and the host re-derives and calls <see cref="Apply"/>.</param>
    /// <param name="removePressed">What to do when the player asks for every run to
    /// go. Same rule: this raises it, and does not remove anything itself.</param>
    internal static MyRunsSettingsRow Build(
        MyRunsRow row, int keep, bool fetchRunIndex, float width, Font? font,
        Action<int> keepChanged, Action removePressed, Action<bool> fetchChanged)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(keepChanged);
        ArgumentNullException.ThrowIfNull(removePressed);
        ArgumentNullException.ThrowIfNull(fetchChanged);

        var root = new Control
        {
            Name = RootName,
            Position = Vector2.Zero,
            Size = new Vector2(width, Height),
            CustomMinimumSize = new Vector2(width, Height),
            // The section underneath owns everything this row is not standing on.
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };

        var nodes = new Nodes
        {
            Root = root,
            Row = row,
            Keep = keep,
            FetchRunIndex = fetchRunIndex,
            KeepLabel = Add(root, Text("KeepLabel", LabelFontSize, Cream, font)),
            KeepNumeral = Add(root, Text("KeepNumeral", NumeralFontSize, Cream, font)),
            Rule = Add(root, new Line2D { Name = "Rule", DefaultColor = RuleLine, Width = 1f }),
            Reading = Add(root, Text("Reading", LabelFontSize, Cream, font)),
            Detail = Add(root, Text("Detail", NoteFontSize, Muted, font)),
        };

        nodes.Fewer = Add(root, Pressable("Fewer", "−", font));
        nodes.More = Add(root, Pressable("More", "+", font));
        nodes.Remove = Add(root, Pressable("Remove", string.Empty, font));
        nodes.Fetch = Add(root, Pressable("FetchRunIndex", string.Empty, font));

        var built = new MyRunsSettingsRow(nodes);

        // Wired after construction so each handler reads the row's own current policy
        // rather than the number it was built with. A stepper that closed over its
        // starting value is the same defect the transport's one derivation exists to
        // prevent, one control down.
        built._fewer.Pressed += () => built.Step(-1, keepChanged);
        built._more.Pressed += () => built.Step(1, keepChanged);
        built._remove.Pressed += removePressed;
        built._fetch.Pressed += () => fetchChanged(!built._fetchIndex);
        built.Layout(width, font);
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
        _remove.Text = row.RemoveLabel;
        _fetch.Text = $"{LibraryCopy.FetchRunIndex}: {(fetchRunIndex ? "on" : "off")}";

        // The stepper refuses at its bottom rather than disappearing there, so the two
        // controls never move about under the player's aim. There is no top: a policy
        // has a minimum and the player's own number above it is theirs.
        Refuse(_fewer, !row.KeepPressable || _keep <= MyRunsRow.MinimumKeep);
        Refuse(_more, !row.KeepPressable);
        Refuse(_remove, !row.RemovePressable);
        Refuse(_fetch, !row.KeepPressable);

        Face(_fewer, !_fewer.Disabled);
        Face(_more, !_more.Disabled);
        Destructive(_remove, row.RemovePressable);
        Face(_fetch, !_fetch.Disabled);
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
    /// Two lines with a control on the right of each, and a hairline between them,
    /// because they are two different sentences: a standing policy above, and what is
    /// on the disk right now below.
    /// </summary>
    private void Layout(float width, Font? font)
    {
        var stepper = (StepSize * 2) + NumeralWidth + (StepGap * 2);
        var removeWidth = Math.Min(width, RemoveBoxWidth(_row.RemoveLabel, font));

        Place(_keepLabel, 0f, 0f, width - stepper - StepGap, LabelHeight);
        Place(_fewer, width - stepper, (LabelHeight - StepSize) / 2f, StepSize, StepSize);
        Place(_keepNumeral, width - stepper + StepSize + StepGap, 0f, NumeralWidth, LabelHeight);
        _keepNumeral.HorizontalAlignment = HorizontalAlignment.Center;
        Place(_more, width - StepSize, (LabelHeight - StepSize) / 2f, StepSize, StepSize);

        var ruleY = LabelHeight + (RuleGap / 2f);
        _rule.Points = [new Vector2(0f, ruleY), new Vector2(width, ruleY)];

        var lower = LabelHeight + RuleGap;
        Place(_reading, 0f, lower, width - removeWidth - StepGap, LabelHeight);
        Place(_remove, width - removeWidth, lower + ((LabelHeight - RemoveHeight) / 2f), removeWidth, RemoveHeight);
        Place(_detail, 0f, lower + LabelHeight, width - removeWidth - StepGap, NoteHeight);

        var fetchY = lower + Math.Max(LabelHeight + NoteHeight, RemoveHeight) + FetchGap;
        Place(_fetch, 0f, fetchY, width, FetchHeight);
    }

    /// <summary>
    /// How wide the destructive control's box has to be for its own word to fit in it.
    ///
    /// Measured rather than assumed, because a Button's own minimum width is its
    /// unwrapped label and a box narrower than that is one the engine widens straight
    /// back out of the row. The constant stands in where there is no font to measure
    /// with, which is the fallback the transport's own measuring uses: a process with no
    /// game draws nothing, so an estimate there costs nothing.
    /// </summary>
    private static float RemoveBoxWidth(string label, Font? font) =>
        font is null
            ? RemoveWidth
            : font.GetStringSize(
                label,
                HorizontalAlignment.Left,
                width: -1f,
                RemoveFontSize,
                TextServer.JustificationFlag.None,
                TextServer.Direction.Auto,
                TextServer.Orientation.Horizontal).X + (RemovePad * 2);

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

    /// <summary>
    /// The one control here that cannot be undone, in the game's own red.
    ///
    /// The red is what the design's "red ribbon" lands as here, because there is no
    /// ribbon node to reach for: the class docstring above says why, and the popup
    /// behind this press is where the game's own ribbons do the asking.
    ///
    /// Red on its edge and its word rather than a red slab, because a filled red
    /// rectangle on a settings screen reads as an error the player has already made.
    /// Refused, it is drawn in the same grey every other refused control is: a player
    /// with nothing to remove is not being warned about anything.
    /// </summary>
    private static void Destructive(Button button, bool enabled)
    {
        var style = new StyleBoxFlat
        {
            BgColor = enabled ? RedFace : DisabledFace,
            BorderColor = enabled ? Red : DisabledEdge,
        };
        style.SetCornerRadiusAll(4);
        style.SetBorderWidthAll(1);
        foreach (var state in new[] { "normal", "pressed", "disabled" })
        {
            button.AddThemeStyleboxOverride(state, style);
        }

        Lit(button, enabled ? Red : DisabledEdge);
        button.AddThemeColorOverride(FontColour, enabled ? Cream : Dim);
        button.AddThemeFontSizeOverride("font_size", RemoveFontSize);
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

    private static Button Pressable(string name, string text, Font? font)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
            // Clipped for the reason the labels are: a Button's own minimum width is
            // its unwrapped label, so one whose word outgrows its box widens itself
            // back out of the row rather than being cut off inside it.
            ClipText = true,
            // Takes focus on purpose: a control a keyboard or a controller cannot
            // reach is a control half the players do not have.
            FocusMode = Control.FocusModeEnum.All,
        };

        if (font is not null) button.AddThemeFontOverride("font", font);
        return button;
    }

    private static Label Text(string name, int size, Color colour, Font? font)
    {
        var label = new Label
        {
            Name = name,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
            ClipText = true,
        };

        if (font is not null) label.AddThemeFontOverride("font", font);
        label.AddThemeFontSizeOverride("font_size", size);
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

        internal required Label KeepLabel { get; init; }

        internal required Label KeepNumeral { get; init; }

        internal required Line2D Rule { get; init; }

        internal required Label Reading { get; init; }

        internal required Label Detail { get; init; }

        internal Button Fewer { get; set; } = null!;

        internal Button More { get; set; } = null!;

        internal Button Remove { get; set; } = null!;

        internal Button Fetch { get; set; } = null!;
    }
}

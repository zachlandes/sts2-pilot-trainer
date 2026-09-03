using System.Reflection;
using Godot;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The drawn result, assembled node by node in a process with no game.
///
/// Every node the panel puts up is a stock Godot node, so the whole panel can be
/// built here and asked what it drew: a card for every card played, a potion where a
/// potion was spent, a point on the chart for every turn a line reached and none
/// where it did not, and the two lines told apart by more than their colour.
///
/// It asserts on what is drawn rather than on where. The layout is arithmetic over
/// the surface it is given, and pinning coordinates would make every future spacing
/// change a test failure about nothing.
/// </summary>
public sealed class FightResultPanelTests
{
    private const string Digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly Vector2 Surface = new(1920, 1080);

    [Fact]
    public void DrawsACardForEveryCardPlayedAndAPotionWhereOneWasSpent()
    {
        var panel = Build(Panel(Comparison()));

        Assert.NotNull(Find(panel, "Turn.1.Yours.Card.CARD.STRIKE_IRONCLAD"));
        Assert.NotNull(Find(panel, "Turn.2.Yours.Card.CARD.BASH"));
        Assert.NotNull(Find(panel, "Turn.2.Yours.Potion.POTION.BLOCK_POTION"));
        Assert.NotNull(Find(panel, "Turn.1.Theirs.Card.CARD.HELLRAISER"));
        Assert.Null(Find(panel, "Turn.1.Theirs.Potion.POTION.BLOCK_POTION"));
    }

    [Fact]
    public void DrawsTheGamesArtworkWhereThereIsSomeAndTheNameWhereThereIsNot()
    {
        var known = new Texture2D();
        var panel = Build(
            Panel(Comparison()),
            art: modelId => modelId == "CARD.BASH" ? known : null);

        Assert.Same(known, Find<TextureRect>(panel, "Turn.2.Yours.Card.CARD.BASH.Art").Texture);
        Assert.Null(Find(panel, "Turn.1.Yours.Card.CARD.STRIKE_IRONCLAD.Art"));
        Assert.Equal("Strike Ironclad", Find<Label>(panel, "Turn.1.Yours.Card.CARD.STRIKE_IRONCLAD.Name").Text);
    }

    [Fact]
    public void ArtworkIsDrawnAtTheSizeOfItsChipRatherThanOfItsTexture()
    {
        // Card art is hundreds of pixels tall. In the client it was drawn at its own
        // size, over half the panel, because a texture rect's minimum size is its
        // texture until it is told to ignore it.
        var panel = Build(Panel(Comparison()), art: _ => new Texture2D());

        var chip = Find<ColorRect>(panel, "Turn.2.Yours.Card.CARD.BASH");
        var picture = Find<TextureRect>(panel, "Turn.2.Yours.Card.CARD.BASH.Art");
        Assert.Equal(TextureRect.ExpandModeEnum.IgnoreSize, picture.ExpandMode);
        Assert.Equal(chip.Size.X - 2, picture.Size.X);
        Assert.Equal(chip.Size.Y - 2, picture.Size.Y);
        Assert.True(picture.Size.Y <= 34);
    }

    [Fact]
    public void PlotsAPointForEveryTurnALineReachedAndNoneWhereItDidNot()
    {
        var panel = Build(Panel(Comparison()));

        // The player fought three turns; the recording's fight ended on its second.
        Assert.NotNull(Find(panel, "Chart.Enemy.Line.You.Point.3"));
        Assert.NotNull(Find(panel, "Chart.Player.Line.You.Point.3"));
        Assert.Null(Find(panel, "Chart.Enemy.Line.Them.Point.3"));
        Assert.Null(Find(panel, "Chart.Player.Line.Them.Point.3"));
        Assert.Equal(3, Points(panel, "Chart.Enemy.Line.You"));
        Assert.Equal(2, Points(panel, "Chart.Enemy.Line.Them"));
    }

    [Fact]
    public void DrawsEachPointsOwnNumeral()
    {
        var panel = Build(Panel(Comparison()));

        Assert.Equal("10", Assert.IsType<Label>(Find(panel, "Chart.Enemy.Line.You.Value.2")).Text);
        Assert.Equal("34", Assert.IsType<Label>(Find(panel, "Chart.Enemy.Line.Them.Value.2")).Text);
        Assert.Equal("8", Assert.IsType<Label>(Find(panel, "Chart.Player.Line.You.Value.2")).Text);
        Assert.Equal("-8", Assert.IsType<Label>(Find(panel, "Turn.2.Yours.HealthLost")).Text);
        Assert.Equal("0", Assert.IsType<Label>(Find(panel, "Turn.2.Theirs.HealthLost")).Text);
    }

    [Fact]
    public void MarksAPotionOnTheChartAtTheTurnItWasSpent()
    {
        var panel = Build(Panel(Comparison()));

        Assert.NotNull(Find(panel, "Chart.Potion.2.You.POTION.BLOCK_POTION"));
        Assert.Null(Find(panel, "Chart.Potion.1.You.POTION.BLOCK_POTION"));
        Assert.Null(Find(panel, "Chart.Potion.2.NaveGreed.POTION.BLOCK_POTION"));
    }

    [Fact]
    public void KeepsTheTwoLinesApartByColourAndByShape()
    {
        var panel = Build(Panel(Comparison()));

        var yours = Assert.IsType<Line2D>(Find(panel, "Chart.Enemy.Line.You"));
        var theirs = Assert.IsType<Line2D>(Find(panel, "Chart.Enemy.Line.Them"));
        Assert.NotEqual(yours.DefaultColor, theirs.DefaultColor);

        var yourPoint = Assert.IsType<ColorRect>(Find(panel, "Chart.Enemy.Line.You.Point.1"));
        var theirPoint = Assert.IsType<ColorRect>(Find(panel, "Chart.Enemy.Line.Them.Point.1"));
        Assert.Equal(yours.DefaultColor, yourPoint.Color);
        Assert.Equal(theirs.DefaultColor, theirPoint.Color);
        Assert.Equal(0, yourPoint.Rotation);
        Assert.NotEqual(0, theirPoint.Rotation);

        // The same two colours name the columns and border the cards, so a column,
        // an icon and a line read as one fighter.
        Assert.Equal(yours.DefaultColor, Assert.IsType<ColorRect>(Find(panel, "Legend.You.Swatch")).Color);
        Assert.Equal(theirs.DefaultColor, Assert.IsType<ColorRect>(Find(panel, "Legend.Them.Swatch")).Color);
        Assert.Equal(
            yours.DefaultColor, Assert.IsType<ColorRect>(Find(panel, "Turn.1.Yours.Card.CARD.STRIKE_IRONCLAD")).Color);
        Assert.Equal(
            theirs.DefaultColor, Assert.IsType<ColorRect>(Find(panel, "Turn.1.Theirs.Card.CARD.HELLRAISER")).Color);
    }

    [Fact]
    public void SaysSoWhereALineHadAlreadyFinished()
    {
        var panel = Build(Panel(Comparison()));

        Assert.Equal("fight over", Assert.IsType<Label>(Find(panel, "Turn.3.Theirs.FightOver")).Text);
        Assert.Null(Find(panel, "Turn.3.Theirs.HealthLost"));
        Assert.Null(Find(panel, "Turn.3.Yours.FightOver"));
    }

    [Fact]
    public void CarriesTheApprovedWordingAndNothingElse()
    {
        var screen = Panel(Comparison());
        var panel = Build(screen);

        Assert.Equal("Your fight and NaveGreed's", Assert.IsType<Label>(Find(panel, "Title")).Text);
        Assert.Equal("Both fights started from the same position.",
            Assert.IsType<Label>(Find(panel, "SameBoundaryNote")).Text);
        Assert.Equal("Turn by turn", Assert.IsType<Label>(Find(panel, "Chronology")).Text);
        Assert.Equal("Health lost each turn", Assert.IsType<Label>(Find(panel, "Chart")).Text);
        Assert.Equal("Enemy health lost", Assert.IsType<Label>(Find(panel, "Chart.Enemy")).Text);
        Assert.Equal("Health lost", Assert.IsType<Label>(Find(panel, "Chart.Player")).Text);
        Assert.Equal("Turn", Assert.IsType<Label>(Find(panel, "Chart.TurnAxis")).Text);
        Assert.Equal("You", Assert.IsType<Label>(Find(panel, "Legend.You")).Text);
        Assert.Equal("NaveGreed", Assert.IsType<Label>(Find(panel, "Legend.Them")).Text);
        Assert.Equal("Health at the end", Assert.IsType<Label>(Find(panel, "Figure.Health at the end")).Text);
        Assert.Equal("50", Assert.IsType<Label>(Find(panel, "Figure.Health at the end.Yours")).Text);
        Assert.Equal("58", Assert.IsType<Label>(Find(panel, "Figure.Health at the end.Theirs")).Text);
        Assert.Equal("Done", Assert.IsType<Button>(Find(panel, "Done")).Text);

        // Every word drawn is a word the screen carries. Nothing is written here.
        var approved = new HashSet<string>(
            new[] { screen.Title, screen.SameBoundaryNote, screen.TurnDetailHeading, screen.FightOverLabel,
                    screen.DoneButton, screen.Chart.Heading, screen.Chart.TurnLabel, screen.Chart.EnemyMeasureLabel,
                    screen.Chart.PlayerMeasureLabel }
                .Concat(screen.Columns)
                .Concat(screen.Notes)
                .Concat(screen.Rows.SelectMany(row => new[] { row.Label, row.Yours, row.Theirs })),
            StringComparer.Ordinal);
        foreach (var label in Descendants(panel).OfType<Label>())
        {
            if (approved.Contains(label.Text)) continue;
            // What is left is a numeral, or a model id as a player reads it, and both
            // are values rather than wording.
            Assert.True(
                int.TryParse(label.Text.TrimStart('-'), out _) ||
                label.Text == ModelIdNames.Display(label.Text.Replace(" ", "_").ToUpperInvariant()),
                $"'{label.Text}' is on the panel and is not the screen's own wording.");
        }
    }

    [Fact]
    public void AFightWithNoComparisonIsTheNoticeAndTheButton()
    {
        var panel = Build(FightResultScreen.Left());

        Assert.Equal(
            "This fight was left before it ended, so there is nothing to compare.",
            Assert.IsType<Label>(Find(panel, "Notice")).Text);
        Assert.Equal("Combat Trainer", Assert.IsType<Label>(Find(panel, "Title")).Text);
        Assert.NotNull(Find(panel, "Done"));
        Assert.Null(Find(panel, "Chart"));
        Assert.Null(Find(panel, "Legend.You"));
    }

    [Fact]
    public void ANoticeIsWrappedInsideAPanelTheSizeOfASentence()
    {
        // Both were wrong in the client before they were tested: the engine's longest
        // refusal was laid out on one line that ran off the panel, inside a panel
        // sized for a comparison that was not there.
        var notice = FightResultScreen.Refused(
            "Your fight could not be captured completely, so it is not compared. A 'EndTurn' began while the " +
            "'PlayCard' before it had not been sampled afterwards, so the capture cannot say what each of them did.");
        var compared = Build(Panel(Comparison()));
        var panel = Build(notice);

        var label = Find<Label>(panel, "Notice");
        var box = Find<ColorRect>(panel, "Panel");
        Assert.Equal(TextServer.AutowrapMode.WordSmart, label.AutowrapMode);
        Assert.True(label.Position.X + label.Size.X <= box.Size.X);
        Assert.True(box.Size.X < Find<ColorRect>(compared, "Panel").Size.X);
        Assert.True(box.Size.Y < Find<ColorRect>(compared, "Panel").Size.Y);
    }

    [Fact]
    public void TheOneControlIsTheButtonAndItIsWhatLeavesTheFight()
    {
        var left = 0;
        var nodes = FightResultPanel.Build(
            Panel(Comparison()), Surface, _ => null, font: null, done: () => left++);

        Assert.Single(Descendants(nodes.Root).OfType<Button>());
        Assert.Same(nodes.Done, Descendants(nodes.Root).OfType<Button>().Single());

        // The handler itself, because a button whose press goes nowhere leaves a
        // player in a finished fight. Reached through the field the C# event compiles
        // to, since nothing outside Godot can raise the signal here.
        var handler = typeof(BaseButton)
            .GetField("Pressed", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(nodes.Done) as Action;
        Assert.NotNull(handler);
        handler!.Invoke();
        Assert.Equal(1, left);
    }

    [Fact]
    public void FitsInsideTheSurfaceItIsGiven()
    {
        foreach (var surface in new[] { new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(2560, 1440) })
        {
            var root = FightResultPanel.Build(Panel(Comparison()), surface, _ => null, null, () => { }).Root;
            var panel = Find<ColorRect>(root, "Panel");

            Assert.Equal(surface, root.Size);
            Assert.True(panel.Position.X >= 0 && panel.Position.Y >= 0);
            Assert.True(panel.Position.X + panel.Size.X <= surface.X);
            Assert.True(panel.Position.Y + panel.Size.Y <= surface.Y);
            Assert.All(
                Descendants(panel).OfType<Control>(),
                node => Assert.True(
                    node.Position.Y + node.Size.Y <= panel.Size.Y + 1,
                    $"'{node.Name}' hangs below the panel on a {surface.X}x{surface.Y} screen"));
        }
    }

    // ── The panel, and the fight it is about ───────────────────────────────

    private static Control Build(FightResultScreen screen, Func<string, Texture2D?>? art = null) =>
        FightResultPanel.Build(screen, Surface, art ?? (_ => null), font: null, done: () => { }).Root;

    private static FightResultScreen Panel(CombatComparison comparison) =>
        FightResultScreen.For("NaveGreed", comparison);

    /// <summary>
    /// A node by the name the panel gave it. Written with dots here and matched
    /// against the panel's own rewriting of them, because Godot will not keep a dot
    /// in a node name and a model id is mostly dots.
    /// </summary>
    private static Node? Find(Node node, string name) =>
        Descendants(node).FirstOrDefault(child => child.Name.ToString() == name.Replace('.', '_'));

    private static T Find<T>(Node node, string name) where T : Node => Assert.IsType<T>(Find(node, name));

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static int Points(Node panel, string name) =>
        Assert.IsType<Line2D>(Find(panel, name)).Points.Length;

    /// <summary>Three turns against two, with a potion on the player's second.</summary>
    private static CombatComparison Comparison() => CombatComparison.Between(Yours(), Theirs());

    private static CombatProjection Yours()
    {
        var capture = Live("player");
        capture.BeginStep("PlayCard", Card("CARD.STRIKE_IRONCLAD"), Sample("in_progress", 1, 64, 42));
        capture.CompleteStep(Sample("in_progress", 1, 64, 34));
        capture.BeginStep("EndTurn", Args(), Sample("in_progress", 1, 64, 34));
        capture.CompleteStep(Sample("in_progress", 2, 58, 34));
        capture.BeginStep("UsePotion", Args(("potion_index", "0")), Sample("in_progress", 2, 58, 34));
        capture.CompleteStep(Sample("in_progress", 2, 58, 34, potions: "empty|empty"));
        capture.BeginStep("PlayCard", Card("CARD.BASH"), Sample("in_progress", 2, 58, 34, potions: "empty|empty"));
        capture.CompleteStep(Sample("in_progress", 2, 58, 24, potions: "empty|empty"));
        capture.BeginStep("EndTurn", Args(), Sample("in_progress", 2, 58, 24, potions: "empty|empty"));
        capture.CompleteStep(Sample("in_progress", 3, 50, 24, potions: "empty|empty"));
        capture.BeginStep("PlayCard", Card("CARD.HELLRAISER"), Sample("in_progress", 3, 50, 24, potions: "empty|empty"));
        capture.CompleteStep(Sample("victory", 3, 50, 0, enemies: 0, potions: "empty|empty"));
        return capture.Project();
    }

    private static CombatProjection Theirs()
    {
        var capture = Live("recording");
        capture.BeginStep("PlayCard", Card("CARD.HELLRAISER"), Sample("in_progress", 1, 64, 42));
        capture.CompleteStep(Sample("in_progress", 1, 64, 34));
        capture.BeginStep("EndTurn", Args(), Sample("in_progress", 1, 64, 34));
        capture.CompleteStep(Sample("in_progress", 2, 58, 34));
        capture.BeginStep("PlayCard", Card("CARD.BASH"), Sample("in_progress", 2, 58, 34));
        capture.CompleteStep(Sample("victory", 2, 58, 0, enemies: 0));
        return capture.Project();
    }

    private static FightCapture Live(string sourceId) =>
        FightCapture.Begin(sourceId, Sample("in_progress", 1, 64, 42), Digest);

    private static Dictionary<string, string> Sample(
        string outcome, int turn, int hp, int enemyHp, int enemies = 1, string potions = "POTION.BLOCK_POTION|empty")
    {
        var sample = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["combat.in_progress"] = outcome == "in_progress" ? "true" : "false",
            ["combat.outcome"] = outcome,
            ["combat.turn"] = turn.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["combat.encounter"] = "ENCOUNTER.TEST",
            ["combat.enemy_count"] = enemies.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["combat.hand"] = "CARD.BASH|CARD.STRIKE_IRONCLAD",
            ["player.hp"] = hp.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["player.max_hp"] = "68",
            ["player.deck"] = "CARD.BASH|CARD.STRIKE_IRONCLAD",
            ["player.relics"] = "RELIC.BURNING_BLOOD",
            ["player.potions"] = potions,
        };
        for (var i = 0; i < enemies; i++)
        {
            sample[$"combat.enemy.{i}.model"] = "MONSTER.TEST";
            sample[$"combat.enemy.{i}.hp"] = enemyHp.ToString(System.Globalization.CultureInfo.InvariantCulture);
            sample[$"combat.enemy.{i}.max_hp"] = "42";
        }
        return sample;
    }

    private static IReadOnlyDictionary<string, string> Card(string modelId) => Args(("card_id", modelId));

    private static IReadOnlyDictionary<string, string> Args(params (string Key, string Value)[] args) =>
        args.ToDictionary(arg => arg.Key, arg => arg.Value, StringComparer.Ordinal);
}

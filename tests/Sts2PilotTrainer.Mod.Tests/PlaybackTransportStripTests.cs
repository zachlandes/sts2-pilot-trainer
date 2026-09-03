using Godot;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The transport strip, assembled node by node in a process with no game.
///
/// Every node it puts up is a stock Godot node, so the whole strip can be built here
/// and asked what it drew: the recording's own words, three controls, a control that
/// is drawn but refused, and the collapse to a chip when the fight becomes the
/// player's.
///
/// Two of its assertions are about the retail client rather than about drawing, and
/// they are here because they are the facts the design was waiting on. The strip
/// lets a click through everywhere except on a button, which is what keeps the map,
/// the event and the fight underneath working. And its buttons take focus, which is
/// what a controller needs to reach them.
/// </summary>
public sealed class PlaybackTransportStripTests
{
    private static readonly Vector2 Surface = new(1920, 1080);

    private static readonly PrefightChoice Blessing = new PrefightChoice.Blessing(0, "RELIC.LEAFY_POULTICE");

    private static readonly PrefightChoice MapMove = new PrefightChoice.MapMove(1, "Monster", 3, 7);

    [Fact]
    public void CarriesTheRecordingsOwnWordsAndNothingElse()
    {
        var state = Revealing(Blessing, 1, noteShown: false);
        var strip = Build(state);

        Assert.Equal(state.Chip, Label(strip, "Chip").Text);
        Assert.Equal("1 of 2", Label(strip, "Counter").Text);
        Assert.Equal("NaveGreed took Leafy Poultice", Label(strip, "Caption").Text);
        Assert.Equal(state.Note, Label(strip, "Note").Text);
        Assert.Equal("Back", strip.Back.Text);
        Assert.Equal("Forward", strip.Forward.Text);
        Assert.Equal("Play", strip.Play.Text);
    }

    /// <summary>
    /// The whole reason this is a strip and not a popup: the same nodes carry the
    /// next decision. A rebuilt surface is one that has lost its place in the tree,
    /// which is what happens across the transition this design exists to survive.
    /// </summary>
    [Fact]
    public void TheSameNodesCarryTheNextDecision()
    {
        var strip = Build(Revealing(Blessing, 1, noteShown: false));
        var caption = Label(strip, "Caption");
        var root = strip.Root;

        strip.Apply(Revealing(MapMove, 2, noteShown: true));

        Assert.Same(root, strip.Root);
        Assert.Same(caption, Label(strip, "Caption"));
        Assert.Equal("NaveGreed moved to the Monster node, centre column", caption.Text);
        Assert.Equal("2 of 2", Label(strip, "Counter").Text);
    }

    /// <summary>The sentence said once is drawn once, and its line is not left
    /// standing empty underneath the caption afterwards.</summary>
    [Fact]
    public void TheOnceOnlySentenceIsDrawnOnlyWhileItIsSaid()
    {
        var strip = Build(Revealing(Blessing, 1, noteShown: false));
        Assert.True(Label(strip, "Note").Visible);

        strip.Apply(Revealing(MapMove, 2, noteShown: true));
        Assert.False(Label(strip, "Note").Visible);
    }

    /// <summary>A control the transport is not offering is drawn and refused, rather
    /// than removed. Buttons that move about under the player's aim between one
    /// decision and the next are worse than a button that is plainly off.</summary>
    [Fact]
    public void AControlThatIsNotOfferedIsDrawnAndRefused()
    {
        var strip = Build(Revealing(Blessing, 1, noteShown: false));

        Assert.True(strip.Back.Visible);
        Assert.True(strip.Back.Disabled);

        strip.Apply(Revealing(MapMove, 2, noteShown: true));
        Assert.False(strip.Back.Disabled);
    }

    /// <summary>
    /// Everything but a button lets a click through. The screens underneath are the
    /// game's map, the game's event and the player's own fight, and a strip that ate
    /// their input would break every one of them.
    /// </summary>
    [Fact]
    public void EverythingExceptAButtonLetsAClickThrough()
    {
        var strip = Build(Revealing(Blessing, 1, noteShown: false));

        Assert.Equal(Control.MouseFilterEnum.Ignore, strip.Root.MouseFilter);
        foreach (var control in Descendants(strip.Root).OfType<Control>())
        {
            if (control is Button) continue;
            Assert.Equal(Control.MouseFilterEnum.Ignore, control.MouseFilter);
        }
    }

    /// <summary>A control a keyboard or a controller cannot reach is a control half
    /// the players do not have.</summary>
    [Fact]
    public void EveryControlCanTakeFocus()
    {
        var strip = Build(Revealing(Blessing, 1, noteShown: false));

        foreach (var button in new[] { strip.Back, strip.Forward, strip.Play })
        {
            Assert.Equal(Control.FocusModeEnum.All, button.FocusMode);
        }
    }

    /// <summary>
    /// The player's own fight. The strip keeps its nodes and shows one of them: no
    /// counter, no caption, no controls, and a chip that says only whose surface this
    /// is.
    /// </summary>
    [Fact]
    public void CollapsesToASilentChipForThePlayersOwnFight()
    {
        var strip = Build(Revealing(MapMove, 2, noteShown: true));

        strip.Apply(PlaybackTransport.DuringYourFight());

        Assert.Equal("Combat Trainer", Label(strip, "Chip").Text);
        Assert.True(Label(strip, "Chip").Visible);
        Assert.False(Label(strip, "Counter").Visible);
        Assert.False(Label(strip, "Caption").Visible);
        Assert.False(Label(strip, "Note").Visible);
        foreach (var button in new[] { strip.Back, strip.Forward, strip.Play })
        {
            Assert.False(button.Visible);
        }
    }

    /// <summary>
    /// The chip is out of the way twice over: a band the width of its own words
    /// rather than of the screen, and one that ends where the strip ended rather than
    /// where it began.
    ///
    /// The right end is the load-bearing half. The band under the game's top bar
    /// carries the run's relic inventory along its left, and the first chip drawn in
    /// the client sat on top of it.
    /// </summary>
    [Fact]
    public void TheChipIsSmallAndKeepsOffTheLeftOfTheBand()
    {
        var strip = Build(Revealing(MapMove, 2, noteShown: true));
        var band = Find<ColorRect>(strip.Root, "Strip");
        var watchingLeft = band.Position.X;
        var watchingWidth = band.Size.X;
        var watchingRight = watchingLeft + watchingWidth;

        strip.Apply(PlaybackTransport.DuringYourFight());

        var chip = Find<ColorRect>(strip.Root, "Strip");
        Assert.True(chip.Size.X < watchingWidth / 2);
        Assert.True(chip.Position.X > watchingLeft);
        Assert.Equal(watchingRight, chip.Position.X + chip.Size.X, 1);
    }

    private static PlaybackTransport Revealing(PrefightChoice choice, int number, bool noteShown) =>
        PlaybackTransport.Revealing("NaveGreed", choice, number, count: 2, playing: false, noteShown: noteShown);

    private static PlaybackTransportStrip Build(PlaybackTransport state) =>
        PlaybackTransportStrip.Build(
            state, Surface, dockTop: 120, font: null, back: () => { }, forward: () => { }, play: () => { });

    private static Label Label(PlaybackTransportStrip strip, string name) => Find<Label>(strip.Root, name);

    private static T Find<T>(Node root, string name) where T : Node =>
        Descendants(root).OfType<T>().Single(node => node.Name.ToString() == name);

    private static IEnumerable<Node> Descendants(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}

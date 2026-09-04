using Godot;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The hanging tag, assembled node by node in a process with no game.
///
/// Every node it puts up is a stock Godot node, so the whole tag can be built here
/// and asked what it drew: the recording's own words, three glyph controls, a control
/// that is drawn but refused, the ledger of decisions already made, and the collapse
/// to a chip when the fight becomes the player's.
///
/// Several of its assertions are about the retail client rather than about drawing,
/// and they are here because they are the facts the design turns on. The tag lets a
/// click through everywhere except on a control, which is what keeps the map, the
/// event and the fight working underneath it. Its controls take focus, which is what
/// a controller needs. It hangs from a measured anchor rather than a constant, so it
/// stays clear of the relic row as the run grows. And its glyphs are filled or hollow
/// by the rule that says whether a control moves the run.
/// </summary>
public sealed class PlaybackTransportStripTests
{
    private static readonly Vector2 Surface = new(1782, 1080);

    /// <summary>The anchor the client measures: the right edge of the game's own meta
    /// cluster, under the top bar's widgets.</summary>
    private static readonly Vector2 Anchor = new(1600, 85);

    private static readonly PrefightChoice Blessing = new PrefightChoice.Blessing(0, "RELIC.LEAFY_POULTICE");

    private static readonly PrefightChoice MapMove = new PrefightChoice.MapMove(1, "Monster", 3, 7);

    private static readonly TransportIdentity NaveGreed = new(
        "NaveGreed", "Ironclad A10, Underdocks", "https://www.youtube.com/watch?v=OJ-6QXhNgdg&t=26s", "0:26");

    [Fact]
    public void CarriesTheRecordingsOwnWordsAndNothingElse()
    {
        var strip = Build(Revealing(Blessing, 1, noteShown: false));

        Assert.Equal("NaveGreed", Label(strip, "Creator").Text);
        Assert.Equal("Ironclad A10, Underdocks", Label(strip, "VideoTitle").Text);
        Assert.Equal("1 of 2", Label(strip, "Counter").Text);
        Assert.Equal(
            "NaveGreed's choices are shown as recorded. This shows what was chosen, not why.",
            Label(strip, "NoteText").Text);
    }

    /// <summary>
    /// The controls are icon only. Nothing on a control carries text, because the
    /// words are in the tooltip - which is the captain's ruling that progressive
    /// disclosure is the game's own principle.
    /// </summary>
    [Fact]
    public void TheControlsCarryGlyphsAndNoText()
    {
        var strip = Build(Revealing(MapMove, 2, noteShown: true));

        foreach (var button in new[] { strip.Back, strip.Play, strip.Step })
        {
            Assert.Equal(string.Empty, button.Text);
            Assert.NotNull(Descendants(button).OfType<Control>().FirstOrDefault(
                node => node.Name.ToString() == "Glyph"));
        }
    }

    /// <summary>
    /// The rule the glyph family carries: a filled shape moves the run, a hollow one
    /// only looks. Step's triangle is a filled polygon; look back's is a stroked line
    /// with no fill behind it.
    /// </summary>
    [Fact]
    public void FilledGlyphsMoveTheRunAndHollowOnesOnlyLook()
    {
        var strip = Build(Revealing(MapMove, 2, noteShown: true));

        Assert.Contains(Descendants(strip.Step), node =>
            node is Polygon2D polygon && polygon.Name.ToString().EndsWith("Triangle", StringComparison.Ordinal));
        Assert.Contains(Descendants(strip.Back), node =>
            node is Line2D line && line.Name.ToString().EndsWith("Triangle", StringComparison.Ordinal));
        Assert.DoesNotContain(Descendants(strip.Back), node =>
            node is Polygon2D polygon && polygon.Name.ToString().EndsWith("Triangle", StringComparison.Ordinal));
    }

    /// <summary>
    /// The whole reason this is a tag and not a popup: the same nodes carry the next
    /// decision. A rebuilt surface is one that has lost its place in the tree, which
    /// is what happens across the transition this design exists to survive.
    /// </summary>
    [Fact]
    public void TheSameNodesCarryTheNextDecision()
    {
        var strip = Build(Revealing(Blessing, 1, noteShown: false));
        var counter = Label(strip, "Counter");
        var root = strip.Root;

        strip.Apply(Revealing(MapMove, 2, noteShown: true));

        Assert.Same(root, strip.Root);
        Assert.Same(counter, Label(strip, "Counter"));
        Assert.Equal("2 of 2", counter.Text);
    }

    /// <summary>The sentence said once is drawn once, and its panel is not left
    /// hanging empty under the tag afterwards.</summary>
    [Fact]
    public void TheOnceOnlySentenceIsDrawnOnlyWhileItIsSaid()
    {
        var strip = Build(Revealing(Blessing, 1, noteShown: false));
        Assert.True(Find<Control>(strip.Root, "Note").Visible);

        strip.Apply(Revealing(MapMove, 2, noteShown: true));
        Assert.False(Find<Control>(strip.Root, "Note").Visible);
    }

    /// <summary>A control the transport is not offering is drawn and refused, rather
    /// than removed. Controls that move about under the player's aim between one
    /// decision and the next are worse than a control that is plainly off.</summary>
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
    /// Everything but a control lets a click through. The screens underneath are the
    /// game's map, the game's event and the player's own fight, and a tag that ate
    /// their input would break every one of them.
    /// </summary>
    [Fact]
    public void EverythingExceptAControlLetsAClickThrough()
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

        foreach (var button in new[] { strip.Back, strip.Play, strip.Step, strip.Speed, strip.Identity })
        {
            Assert.Equal(Control.FocusModeEnum.All, button.FocusMode);
        }
    }

    /// <summary>
    /// The tooltip is how an icon-only control says what it does, and a controller
    /// reaching it has to get the same words a pointer does.
    /// </summary>
    [Fact]
    public void HoverAndFocusBothShowTheTooltip()
    {
        var strip = Build(Revealing(MapMove, 2, noteShown: true));
        Assert.False(strip.Tooltip.Visible);

        strip.Step.EmitHover(entered: true);
        Assert.True(strip.Tooltip.Visible);
        Assert.Equal("Step", Label(strip.Tooltip, "TooltipTitle").Text);
        Assert.Contains("NaveGreed moved to the Monster node", Label(strip.Tooltip, "TooltipBody").Text,
            StringComparison.Ordinal);

        strip.Step.EmitHover(entered: false);
        Assert.False(strip.Tooltip.Visible);

        strip.Back.EmitFocus(entered: true);
        Assert.True(strip.Tooltip.Visible);
        Assert.Equal("Look back", Label(strip.Tooltip, "TooltipTitle").Text);
    }

    /// <summary>A refused control's tooltip says why it is refused, rather than
    /// repeating what it would have done.</summary>
    [Fact]
    public void ARefusedControlsTooltipSaysWhy()
    {
        var strip = Build(Revealing(Blessing, 1, noteShown: false));

        strip.Back.EmitHover(entered: true);

        Assert.Equal("This is the first choice.", Label(strip.Tooltip, "TooltipBody").Text);
    }

    /// <summary>
    /// The hold, made visible. Without it the tag simply pauses under Play and a
    /// watcher cannot tell a hold from a stall.
    /// </summary>
    [Fact]
    public void TheHoldIsDrawnAsALineThatDrains()
    {
        var strip = Build(Revealing(Blessing, 1, noteShown: false));
        var fill = Find<Line2D>(strip.Root, "Hold");
        Assert.False(fill.Visible);

        strip.ShowHold(0.5);
        Assert.True(fill.Visible);
        var half = fill.Points[1].X - fill.Points[0].X;

        strip.ShowHold(1.0);
        Assert.True(fill.Points[1].X - fill.Points[0].X > half);

        strip.HideHold();
        Assert.False(fill.Visible);
    }

    /// <summary>
    /// Looking back hangs a ledger of what has been chosen so far, with the game's own
    /// artwork for each. It exists because those choices were made on screens that are
    /// gone, and the run must never be rewound to answer for them.
    /// </summary>
    [Fact]
    public void LookingBackHangsALedgerWithTheGamesOwnArt()
    {
        var art = new Texture2D();
        var strip = Build(Revealing(Blessing, 1, noteShown: true));
        strip.DrawArtWith(modelId => modelId == "RELIC.LEAFY_POULTICE" ? art : null);

        strip.Apply(PlaybackTransport.LookingBackAt(
            NaveGreed, [Blessing], shown: 1, current: 2, count: 2, next: MapMove));

        Assert.True(strip.Ledger.Visible);
        Assert.Equal("Leafy Poultice", Label(strip.Ledger, "Ledger1").Text);
        Assert.Same(art, Find<TextureRect>(strip.Ledger, "Ledger1.Art").Texture);
        Assert.Equal("Monster node, centre column", Label(strip.Ledger, "Ledger2").Text);

        strip.Apply(Revealing(MapMove, 2, noteShown: true));
        Assert.False(strip.Ledger.Visible);
    }

    /// <summary>
    /// The player's own fight. The tag keeps its nodes and shows two of them: the mark
    /// and the name, and nothing that offers anything.
    /// </summary>
    [Fact]
    public void CollapsesToASilentChipForThePlayersOwnFight()
    {
        var strip = Build(Revealing(MapMove, 2, noteShown: true));

        strip.Apply(PlaybackTransport.DuringYourFight(NaveGreed, anythingPlayed: true));

        Assert.Equal("NaveGreed", Label(strip, "Creator").Text);
        Assert.False(Label(strip, "Counter").Visible);
        Assert.False(Find<Control>(strip.Root, "Pips").Visible);
        foreach (var button in new[] { strip.Back, strip.Play, strip.Step })
        {
            Assert.False(button.Visible);
        }
    }

    /// <summary>
    /// The chip says nothing until it is pressed, which means it can be pressed. Its
    /// press target is the whole plate and carries no words of its own; without one
    /// the two directions it offers - back to the beginning, and to the end - cannot
    /// be reached in the client at all.
    /// </summary>
    [Fact]
    public void TheChipIsPressableOverItsWholePlate()
    {
        var strip = Build(Revealing(MapMove, 2, noteShown: true));

        strip.Apply(PlaybackTransport.DuringYourFight(NaveGreed, anythingPlayed: true));

        var plate = Find<Polygon2D>(strip.Root, "Plate").Polygon;
        var press = strip.Speed;

        Assert.True(press.Visible);
        Assert.False(press.Disabled);
        Assert.Equal(plate.Min(point => point.X), press.Position.X, 1);
        Assert.Equal(plate.Min(point => point.Y), press.Position.Y, 1);
        Assert.Equal(plate.Max(point => point.X) - plate.Min(point => point.X), press.Size.X, 1);
        Assert.Equal(plate.Max(point => point.Y) - plate.Min(point => point.Y), press.Size.Y, 1);
        Assert.False(Label(strip, "SpeedLabel").Visible);

        press.EmitHover(entered: true);
        Assert.False(strip.Tooltip.Visible);
    }

    /// <summary>
    /// The chip is out of the way twice over: a plate the width of its own words, and
    /// one hung from the same right-hand anchor as the tag. The band under the game's
    /// top bar carries the run's relic inventory along its left, and a chip parked
    /// there covers relics the player is fighting with.
    /// </summary>
    [Fact]
    public void TheChipIsSmallAndHangsFromTheSameRightHandAnchor()
    {
        var strip = Build(Revealing(MapMove, 2, noteShown: true));
        var tag = Find<Polygon2D>(strip.Root, "Plate").Polygon;
        var tagLeft = tag.Min(point => point.X);
        var tagRight = tag.Max(point => point.X);

        strip.Apply(PlaybackTransport.DuringYourFight(NaveGreed, anythingPlayed: true));

        var chip = Find<Polygon2D>(strip.Root, "Plate").Polygon;
        Assert.True(chip.Max(point => point.X) - chip.Min(point => point.X) < (tagRight - tagLeft) / 2);
        Assert.True(chip.Min(point => point.X) > tagLeft);
        Assert.Equal(tagRight, chip.Max(point => point.X), 1);
    }

    /// <summary>
    /// The tag hangs from measured furniture, so it moves when the game's own does.
    /// A constant is right on one monitor and on one relic count.
    /// </summary>
    [Fact]
    public void TheTagHangsFromTheAnchorItIsGiven()
    {
        var strip = Build(Revealing(MapMove, 2, noteShown: true));

        strip.Reanchor(Surface, new Vector2(1200, 200));

        var plate = Find<Polygon2D>(strip.Root, "Plate").Polygon;
        Assert.Equal(1200, plate.Max(point => point.X), 1);
        Assert.Equal(200, plate.Min(point => point.Y), 1);
    }

    /// <summary>The pips are a picture of the journey, and they stop being drawn when
    /// there are too many to read at a glance.</summary>
    [Fact]
    public void ThePipsAreDrawnOnlyWhileTheyCanBeRead()
    {
        var strip = Build(Revealing(MapMove, 2, noteShown: true));
        Assert.Equal(2, Descendants(Find<Control>(strip.Root, "Pips"))
            .Count(node => node.Name.ToString().StartsWith("Pip", StringComparison.Ordinal)));

        strip.Apply(PlaybackTransport.Revealing(NaveGreed, MapMove, 2, 40, false, true));
        Assert.False(Find<Control>(strip.Root, "Pips").Visible);
        Assert.Equal("2 of 40", Label(strip, "Counter").Text);
    }

    /// <summary>
    /// The speed menu and the chip's menu are the same shape and the same node. A row
    /// that is refused cannot be chosen, and choosing closes the menu either way.
    /// </summary>
    [Fact]
    public void AMenuOffersItsRowsAndARefusedRowCannotBeChosen()
    {
        var chosen = new List<int>();
        var strip = Build(PlaybackTransport.DuringYourFight(NaveGreed, anythingPlayed: false));

        strip.OpenMenu(chip: true, chosen.Add);

        Assert.True(strip.Menu.Visible);
        Assert.Equal("Jump to the beginning", Label(strip.Menu, "MenuRow0.Label").Text);
        Assert.Equal("Jump to the end", Label(strip.Menu, "MenuRow1.Label").Text);
        Assert.True(Find<Button>(strip.Menu, "MenuRow1").Disabled);
        Assert.False(Find<Button>(strip.Menu, "MenuRow0").Disabled);

        strip.CloseMenu();
        Assert.False(strip.Menu.Visible);
    }

    /// <summary>
    /// A menu belongs to the tag it was opened on. Left open across the collapse to
    /// the chip it would hang under a chip that says nothing until it is pressed, and
    /// the chip's first press would close it instead of opening its own two
    /// directions.
    /// </summary>
    [Fact]
    public void AMenuLeftOpenIsClosedWhenTheTagChangesWhatItIs()
    {
        var strip = Build(Revealing(MapMove, 2, noteShown: true));

        strip.OpenMenu(chip: false, _ => { });
        Assert.True(strip.MenuIsOpen);

        strip.Apply(PlaybackTransport.DuringYourFight(NaveGreed, anythingPlayed: true));

        Assert.False(strip.MenuIsOpen);
        Assert.False(strip.Menu.Visible);
    }

    /// <summary>The identity block is a control because pressing it opens the video,
    /// and it is refused on a recording with nowhere to open.</summary>
    [Fact]
    public void TheIdentityBlockIsPressableOnlyWhenThereIsAVideo()
    {
        var strip = Build(Revealing(MapMove, 2, noteShown: true));
        Assert.False(strip.Identity.Disabled);

        strip.Apply(PlaybackTransport.Revealing(
            new TransportIdentity("NaveGreed", null, null, null), MapMove, 2, 2, false, true));

        Assert.True(strip.Identity.Disabled);
        Assert.False(Label(strip, "VideoTitle").Visible);
    }

    /// <summary>
    /// A glyph sits inside the control it belongs to, on the first paint as well as on
    /// every one after it.
    ///
    /// The tag draws each control's glyph in the same pass that sizes the control, so
    /// a glyph centred against the size the host reports is centred against the size
    /// it had last frame - nothing at all, the first time. In the client that put
    /// every control's glyph half a glyph up and to the left of its plate on the
    /// screen it first appeared on, and left the mark straddling the tag's own edge
    /// for the whole run, because the mark is never resized after that.
    /// </summary>
    [Fact]
    public void EveryGlyphIsDrawnInsideItsControlOnTheFirstPaint()
    {
        var strip = Build(Revealing(Blessing, 1, noteShown: false));

        var hosts = new Control[] { strip.Back, strip.Play, strip.Step, Find<Control>(strip.Root, "Mark") };
        foreach (var host in hosts)
        {
            var glyph = Descendants(host).OfType<Control>().Single(node => node.Name.ToString() == "Glyph");

            Assert.True(glyph.Position.X >= 0, $"{host.Name} drew its glyph left of itself");
            Assert.True(glyph.Position.Y >= 0, $"{host.Name} drew its glyph above itself");
            Assert.True(
                glyph.Position.X + glyph.Size.X <= host.Size.X,
                $"{host.Name} drew its glyph past its right edge");
            Assert.True(
                glyph.Position.Y + glyph.Size.Y <= host.Size.Y,
                $"{host.Name} drew its glyph past its bottom edge");

            // Centred, not merely contained.
            Assert.Equal(glyph.Position.X, (host.Size.X - glyph.Size.X) / 2, 3);
            Assert.Equal(glyph.Position.Y, (host.Size.Y - glyph.Size.Y) / 2, 3);
        }
    }

    /// <summary>
    /// Every sentence the tag says is allowed to wrap, and none of them is clipped.
    ///
    /// Both panels that carry a sentence were sized from the text rather than from
    /// the text once wrapped, so the once-only note stopped at "what was cho" and
    /// look back's tooltip lost the word "undone." A panel is sized by measuring the
    /// wrapped text, which leaves nothing to clip.
    /// </summary>
    [Fact]
    public void TheSentencesTheTagSaysWrapRatherThanBeingCutOff()
    {
        var strip = Build(Revealing(Blessing, 1, noteShown: false));
        strip.Back.EmitHover(entered: true);

        foreach (var sentence in new[] { Label(strip, "NoteText"), Label(strip.Tooltip, "TooltipBody") })
        {
            Assert.False(sentence.ClipText, $"{sentence.Name} clips its sentence");
            Assert.Equal(TextServer.AutowrapMode.WordSmart, sentence.AutowrapMode);
        }
    }

    /// <summary>
    /// Nothing that hangs under the tag is hung on top of something else that is
    /// already hanging there.
    ///
    /// The plates are translucent on purpose - the game shows through them - so two
    /// surfaces on the same band are not one covering the other, they are both legible
    /// at once and neither readable. The retail client drew the speed menu straight
    /// over the look-back ledger and the ledger's words came up through it.
    /// </summary>
    [Fact]
    public void WhatHangsUnderTheTagHangsBelowWhateverIsAlreadyThere()
    {
        var strip = Build(PlaybackTransport.LookingBackAt(
            NaveGreed, [Blessing], shown: 1, current: 2, count: 2, next: MapMove));
        strip.OpenMenu(chip: false, _ => { });

        var ledger = strip.Ledger;
        Assert.True(ledger.Visible);
        Assert.True(strip.Menu.Visible);
        Assert.True(
            strip.Menu.Position.Y >= ledger.Position.Y + ledger.Size.Y,
            $"the menu starts at {strip.Menu.Position.Y} and the ledger runs to " +
            $"{ledger.Position.Y + ledger.Size.Y}");

        // The tooltip hangs off the same measure, so hovering a control while looking
        // back does not put the sentence over the ledger either.
        strip.Back.EmitFocus(entered: true);
        Assert.True(strip.Tooltip.Visible);
        Assert.True(
            strip.Tooltip.Position.Y >= ledger.Position.Y + ledger.Size.Y,
            $"the tooltip starts at {strip.Tooltip.Position.Y} and the ledger runs to " +
            $"{ledger.Position.Y + ledger.Size.Y}");
    }

    private static PlaybackTransport Revealing(PrefightChoice choice, int number, bool noteShown) =>
        PlaybackTransport.Revealing(NaveGreed, choice, number, count: 2, playing: false, noteShown: noteShown);

    private static PlaybackTransportStrip Build(PlaybackTransport state) =>
        PlaybackTransportStrip.Build(
            state, Surface, Anchor, font: null,
            back: () => { }, play: () => { }, step: () => { }, speed: () => { }, identity: () => { });

    private static Label Label(PlaybackTransportStrip strip, string name) => Find<Label>(strip.Root, name);

    private static Label Label(Node root, string name) => Find<Label>(root, name);

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

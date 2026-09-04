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

        strip.Apply(LookingBack());

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

        strip.Apply(Chip(anythingPlayed: true));

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

        strip.Apply(Chip(anythingPlayed: true));

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

        // Present and silent: it takes input over the whole plate and draws nothing
        // but the rim the game uses for "this is the thing you are about to press".
        Assert.Equal(0f, Stylebox(press, "normal").BgColor.A, 3);
        Assert.Equal(0, Stylebox(press, "normal").BorderWidth);
        Assert.True(Stylebox(press, "hover").BorderWidth > 0);
    }

    /// <summary>
    /// Present, drawn and pressable are three answers, and every mode answers all
    /// three for every element.
    ///
    /// The refactor this asserts replaced one boolean that decided all three at once,
    /// which is what made "silent but pressable" unsayable and left the chip with no
    /// control at all.
    /// </summary>
    [Fact]
    public void EachModeSaysSeparatelyWhatIsThereWhatIsDrawnAndWhatCanBePressed()
    {
        var strip = Build(Revealing(MapMove, 2, noteShown: true));

        // Watching: the tag, everything drawn, everything on offer.
        Assert.True(strip.Step.Visible);
        Assert.False(strip.Step.Disabled);
        Assert.True(Stylebox(strip.Step, "normal").BgColor.A > 0);

        // Opening: the same tag, the same controls, none of them on offer - and the
        // speed control still is, because it does not move the run.
        strip.Apply(Opening(PlaybackSpeed.Double));
        foreach (var button in new[] { strip.Back, strip.Play, strip.Step })
        {
            Assert.True(button.Visible);
            Assert.True(button.Disabled);
            Assert.True(Stylebox(button, "normal").BgColor.A > 0);
        }

        Assert.True(strip.Speed.Visible);
        Assert.False(strip.Speed.Disabled);
        Assert.Equal("2×", Label(strip, "SpeedLabel").Text);

        // The chip: those three gone entirely, one silent press target left.
        strip.Apply(Chip(anythingPlayed: false));
        foreach (var button in new[] { strip.Back, strip.Play, strip.Step, strip.Identity })
        {
            Assert.False(button.Visible);
        }

        Assert.True(strip.Speed.Visible);
        Assert.False(strip.Speed.Disabled);

        // Refused: the tag again, and this time nothing works at all, the speed
        // included.
        strip.Apply(RefusedTag());
        foreach (var button in new[] { strip.Back, strip.Play, strip.Step, strip.Speed, strip.Identity })
        {
            Assert.True(button.Visible);
            Assert.True(button.Disabled);
        }

        Assert.False(Label(strip, "Counter").Visible);
    }

    /// <summary>
    /// The chip carries the mark and the name. A recording whose manifest has a title
    /// does not put it on a plate a third the width of the tag.
    /// </summary>
    [Fact]
    public void TheChipCarriesTheMarkAndTheNameAndNothingElse()
    {
        var strip = Build(Revealing(MapMove, 2, noteShown: true));
        Assert.True(Label(strip, "VideoTitle").Visible);

        strip.Apply(Chip(anythingPlayed: true));

        Assert.True(Label(strip, "Creator").Visible);
        Assert.False(Label(strip, "VideoTitle").Visible);
        Assert.False(Label(strip, "SpeedLabel").Visible);
    }

    /// <summary>
    /// A menu survives the decisions its own surface keeps offering it, and only
    /// closes when the surface starts offering a different one.
    ///
    /// Closing on every change would shut the speed menu under the player's hand
    /// between one decision and the next; not closing at all is what left it hanging
    /// under the chip.
    /// </summary>
    [Fact]
    public void AMenuSurvivesADecisionAndNotAChangeOfWhatIsOffered()
    {
        var strip = Build(Revealing(Blessing, 1, noteShown: false));
        strip.OpenMenu(_ => { });
        Assert.True(strip.MenuIsOpen);

        strip.Apply(Revealing(MapMove, 2, noteShown: true));
        Assert.True(strip.MenuIsOpen);

        strip.Apply(Opening(PlaybackSpeed.Normal));
        Assert.True(strip.MenuIsOpen);

        strip.Apply(Chip(anythingPlayed: true));
        Assert.False(strip.MenuIsOpen);
    }

    /// <summary>
    /// The chip's press target opens the chip's own two directions, and the strip is
    /// not told which menu that is - it is the one the surface offers.
    /// </summary>
    [Fact]
    public void ThePressTargetOpensWhicheverMenuTheSurfaceOffers()
    {
        var strip = Build(Revealing(MapMove, 2, noteShown: true));

        strip.OpenMenu(_ => { });
        Assert.Equal("1×", Label(strip.Menu, "MenuRow1.Label").Text);
        strip.CloseMenu();

        strip.Apply(Chip(anythingPlayed: true));
        strip.OpenMenu(_ => { });

        Assert.True(strip.Menu.Visible);
        Assert.Equal("Jump to the beginning", Label(strip.Menu, "MenuRow0.Label").Text);
    }

    /// <summary>
    /// A refused tag offers nothing, so there is no menu to open on it either.
    /// </summary>
    [Fact]
    public void ARefusedTagHasNoMenuToOpen()
    {
        var strip = Build(RefusedTag());

        strip.OpenMenu(_ => { });

        Assert.False(strip.MenuIsOpen);
        Assert.False(strip.Menu.Visible);
    }

    /// <summary>
    /// Step is drawn and refused between committing one choice and revealing the next,
    /// which is a window a fast second press used to get inside.
    /// </summary>
    [Fact]
    public void StepIsDrawnAndRefusedWhileNothingIsRevealed()
    {
        var strip = Build(For(JourneyPhase.Watching, next: MapMove, stepsTaken: 1, revealed: false));

        Assert.True(strip.Step.Visible);
        Assert.True(strip.Step.Disabled);

        strip.Apply(For(JourneyPhase.Watching, next: MapMove, stepsTaken: 1, revealed: true));
        Assert.False(strip.Step.Disabled);
    }

    /// <summary>
    /// Play is refused in that same window, for the same reason: starting the sequence
    /// there would make the next decision without anybody having been shown it.
    /// </summary>
    [Fact]
    public void PlayIsDrawnAndRefusedWhileNothingIsRevealed()
    {
        var strip = Build(For(JourneyPhase.Watching, next: MapMove, stepsTaken: 1, revealed: false));

        Assert.True(strip.Play.Visible);
        Assert.True(strip.Play.Disabled);

        strip.Apply(For(JourneyPhase.Watching, next: MapMove, stepsTaken: 1, revealed: true));
        Assert.False(strip.Play.Disabled);
    }

    /// <summary>
    /// Pause is not refused there. It stops the run rather than moving it, and a
    /// sequence that cannot be stopped mid-transition is the reason somebody reaches
    /// for it.
    /// </summary>
    [Fact]
    public void PauseStaysOnOfferWhileNothingIsRevealed()
    {
        var strip = Build(For(
            JourneyPhase.Watching, next: MapMove, stepsTaken: 1, revealed: false, playing: true));

        Assert.True(strip.Play.Visible);
        Assert.False(strip.Play.Disabled);
    }

    /// <summary>
    /// A refused tag still states the speed the run was being watched at. Every
    /// control on it is dead, and a label that quietly reverts to 1x is a wrong
    /// reading rather than a missing one.
    /// </summary>
    [Fact]
    public void ARefusedTagStatesTheSpeedThatWasInForce()
    {
        var strip = Build(RefusedTag(PlaybackSpeed.Double));

        Assert.Equal("2×", Label(strip, "SpeedLabel").Text);
        Assert.True(strip.Speed.Disabled);
    }

    /// <summary>
    /// While the game is between screens the same line travels instead of draining.
    ///
    /// Those two windows refuse everything that moves the run and say nothing about
    /// why, so the tag has to show that it is waiting rather than stopped. It carries
    /// no fraction: neither window has a known length, and a line draining toward a
    /// deadline would be claiming one.
    /// </summary>
    [Fact]
    public void BetweenScreensTheLineTravelsRatherThanDraining()
    {
        var strip = Build(Opening(PlaybackSpeed.Normal));
        var track = Find<Line2D>(strip.Root, "HoldTrack");
        var fill = Find<Line2D>(strip.Root, "Hold");

        strip.ShowMoving(0.4);
        Assert.True(track.Visible);
        Assert.True(fill.Visible);
        var at40 = fill.Points[0].X;
        var width = fill.Points[1].X - fill.Points[0].X;

        strip.ShowMoving(0.6);
        Assert.True(fill.Points[0].X > at40);

        // A segment of the track rather than a measure of it: the same size wherever
        // it is, which is what says it is not counting anything down.
        Assert.Equal(width, fill.Points[1].X - fill.Points[0].X, 1);
        Assert.True(width < (track.Points[1].X - track.Points[0].X) / 2);

        // It enters and leaves at the ends rather than jumping, and never runs past
        // the track it travels on.
        strip.ShowMoving(0.0);
        Assert.Equal(track.Points[0].X, fill.Points[0].X, 1);
        Assert.True(fill.Points[1].X - fill.Points[0].X < width);

        strip.ShowMoving(1.0);
        Assert.Equal(track.Points[1].X, fill.Points[1].X, 1);
        Assert.True(fill.Points[1].X - fill.Points[0].X < width);

        // And there is nothing to show on a surface with no line.
        strip.Apply(Chip(anythingPlayed: false));
        strip.ShowMoving(0.5);
        Assert.False(fill.Visible);
    }

    /// <summary>
    /// The hold survives the surface being re-derived under it.
    ///
    /// Every fact that changes re-derives the tag, and Play re-derives on each hold
    /// tick, so a pass that cleared the line each time would leave it flickering or
    /// blank - which is the stall the drained line exists to rule out.
    /// </summary>
    [Fact]
    public void AHoldInFlightSurvivesTheTagBeingRedrawn()
    {
        var strip = Build(Revealing(Blessing, 1, noteShown: false));
        var fill = Find<Line2D>(strip.Root, "Hold");

        strip.ShowHold(0.5);
        Assert.True(fill.Visible);

        strip.Apply(Revealing(Blessing, 1, noteShown: true));
        Assert.True(fill.Visible);

        // And it goes with a mode that has no hold to draw.
        strip.Apply(Chip(anythingPlayed: false));
        Assert.False(fill.Visible);
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

        strip.Apply(Chip(anythingPlayed: true));

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

        strip.Apply(For(JourneyPhase.Watching, next: MapMove, stepsTaken: 1, count: 40));
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
        var strip = Build(Chip(anythingPlayed: false));

        strip.OpenMenu(chosen.Add);

        Assert.True(strip.Menu.Visible);
        Assert.Equal("Jump to the beginning", Label(strip.Menu, "MenuRow0.Label").Text);
        Assert.Equal("Jump to the end", Label(strip.Menu, "MenuRow1.Label").Text);
        Assert.True(Find<Button>(strip.Menu, "MenuRow1").Disabled);
        Assert.False(Find<Button>(strip.Menu, "MenuRow0").Disabled);

        strip.CloseMenu();
        Assert.False(strip.Menu.Visible);
    }

    /// <summary>
    /// Pressing a menu row runs that row's action.
    ///
    /// It did not, for either menu, in every build this surface has ever had. The rows
    /// were built in a `for` loop whose closures were written over the loop variable
    /// itself, so every row asked for row number `rows.Count` - one past the last -
    /// and the menu closed having done nothing. Nothing caught it: no exception is
    /// thrown, the menu still closes, and neither menu had been chosen from in the
    /// retail client, because the chip could not be pressed at all and a speed that
    /// did not take looks much like a speed nobody set.
    /// </summary>
    [Fact]
    public void PressingAMenuRowRunsThatRowsAction()
    {
        var chosen = new List<int>();
        var strip = Build(Revealing(MapMove, 2, noteShown: true));

        strip.OpenMenu(chosen.Add);

        Find<Button>(strip.Menu, "MenuRow2").EmitPressed();
        Assert.Equal([2], chosen);
        Assert.False(strip.MenuIsOpen);

        // And the same for the other menu, which is the same code and the same rows.
        strip.Apply(Chip(anythingPlayed: true));
        strip.OpenMenu(chosen.Add);
        Find<Button>(strip.Menu, "MenuRow1").EmitPressed();

        Assert.Equal([2, 1], chosen);
    }

    /// <summary>A refused row cannot be chosen, and pressing it neither runs an action
    /// nor leaves the menu open.</summary>
    [Fact]
    public void ARefusedRowCannotBeChosen()
    {
        var chosen = new List<int>();
        var strip = Build(Chip(anythingPlayed: false));

        strip.OpenMenu(chosen.Add);
        var refused = Find<Button>(strip.Menu, "MenuRow1");

        Assert.True(refused.Disabled);
        refused.EmitPressed();
        Assert.Empty(chosen);
    }

    /// <summary>
    /// An open menu moves the measure everything below it hangs from.
    ///
    /// The plates are translucent, so a surface drawn on the same band as another is
    /// not one covering the other - both are legible and neither readable. The menu
    /// did not move that measure, and the client drew the speed control's own tooltip
    /// straight over the menu that control had just opened.
    /// </summary>
    [Fact]
    public void AnOpenMenuPushesWhateverHangsNextBelowIt()
    {
        var strip = Build(Revealing(Blessing, 1, noteShown: false));
        strip.OpenMenu(_ => { });

        var note = Find<Control>(strip.Root, "Note");
        var menu = strip.Menu;
        Assert.True(note.Visible);
        Assert.True(menu.Visible);
        Assert.True(
            menu.Position.Y >= note.Position.Y + note.Size.Y,
            $"the menu starts at {menu.Position.Y} and the note runs to {note.Position.Y + note.Size.Y}");

        strip.Speed.EmitFocus(entered: true);
        Assert.True(strip.Tooltip.Visible);
        Assert.True(
            strip.Tooltip.Position.Y >= menu.Position.Y + menu.Size.Y,
            $"the tooltip starts at {strip.Tooltip.Position.Y} and the menu runs to " +
            $"{menu.Position.Y + menu.Size.Y}");
    }

    /// <summary>
    /// A tooltip already on screen moves when what hangs under the tag changes.
    ///
    /// Pressing a control focuses it and focus raises its tooltip, which happens
    /// before the press has changed anything - so the sentence is placed against the
    /// measure of a moment ago. Look back is the case that shows it in the client: the
    /// tooltip went up against the tag's own foot, the ledger then appeared beneath
    /// it, and the sentence sat over the rows it was meant to hang below.
    /// </summary>
    [Fact]
    public void ATooltipAlreadyUpMovesBelowWhateverAppearsUnderIt()
    {
        var strip = Build(Revealing(MapMove, 2, noteShown: true));

        strip.Back.EmitFocus(entered: true);
        Assert.True(strip.Tooltip.Visible);
        var before = strip.Tooltip.Position.Y;

        strip.Apply(LookingBack());

        var ledger = strip.Ledger;
        Assert.True(ledger.Visible);
        Assert.True(strip.Tooltip.Visible);
        Assert.True(strip.Tooltip.Position.Y > before, "the tooltip did not move at all");
        Assert.True(
            strip.Tooltip.Position.Y >= ledger.Position.Y + ledger.Size.Y,
            $"the tooltip starts at {strip.Tooltip.Position.Y} and the ledger runs to " +
            $"{ledger.Position.Y + ledger.Size.Y}");
    }

    /// <summary>
    /// Opening a menu takes down the tooltip that the same press raised.
    ///
    /// Pressing a control focuses it, and focus raises its tooltip - which happens
    /// before the menu that press opens exists, so the sentence was placed against a
    /// measure the menu had not moved yet and was drawn straight over the rows. In the
    /// client that made the first two speed rows unreadable.
    /// </summary>
    [Fact]
    public void OpeningAMenuTakesDownTheTooltipThatOpenedIt()
    {
        var strip = Build(Revealing(Blessing, 1, noteShown: false));

        strip.Speed.EmitFocus(entered: true);
        Assert.True(strip.Tooltip.Visible);

        strip.OpenMenu(_ => { });

        Assert.True(strip.Menu.Visible);
        Assert.False(strip.Tooltip.Visible);
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

        strip.OpenMenu(_ => { });
        Assert.True(strip.MenuIsOpen);

        strip.Apply(Chip(anythingPlayed: true));

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

        strip.Apply(For(
            JourneyPhase.Watching, new TransportIdentity("NaveGreed", null, null, null),
            next: MapMove, stepsTaken: 1));

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
        var strip = Build(LookingBack());
        strip.OpenMenu(_ => { });

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
        For(JourneyPhase.Watching, next: choice, stepsTaken: number - 1, noteShown: noteShown);

    /// <summary>
    /// The one way to get a state, here as everywhere: the strip draws what the phase
    /// and the run's facts say, and there is no hand-built state to draw instead.
    /// </summary>
    private static PlaybackTransport For(
        JourneyPhase phase,
        TransportIdentity? identity = null,
        IReadOnlyList<PrefightChoice>? made = null,
        PrefightChoice? next = null,
        int stepsTaken = 0,
        int count = 2,
        bool atCombatStart = false,
        bool revealed = true,
        int? lookingBackAt = null,
        bool playing = false,
        bool noteShown = true,
        PlaybackSpeed speed = PlaybackSpeed.Normal,
        bool anythingPlayed = false) =>
        PlaybackTransport.For(phase, new TransportFacts(
            identity ?? NaveGreed, made ?? [], next, stepsTaken, count, atCombatStart, revealed,
            lookingBackAt, playing, noteShown, speed, anythingPlayed))
        ?? throw new InvalidOperationException($"{phase} puts nothing on screen.");

    private static PlaybackTransport LookingBack() =>
        For(JourneyPhase.Watching, made: [Blessing], next: MapMove, stepsTaken: 1, lookingBackAt: 1);

    private static PlaybackTransport Chip(bool anythingPlayed) =>
        For(JourneyPhase.InFight, anythingPlayed: anythingPlayed);

    private static PlaybackTransport Opening(PlaybackSpeed speed) =>
        For(JourneyPhase.Watching, stepsTaken: 2, atCombatStart: true, speed: speed);

    private static PlaybackTransport RefusedTag(PlaybackSpeed speed = PlaybackSpeed.Normal) =>
        For(JourneyPhase.Refused, speed: speed);

    private static StyleBoxFlat Stylebox(Control control, string state) =>
        Assert.IsType<StyleBoxFlat>(control.ThemeStylebox(state));

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

using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// The table: for each of the five things the transport can be, what every element
/// of it is.
///
/// This is the design written as an assertion rather than as prose, and it is the
/// test the refactor exists for. Four defects on this surface came from one boolean
/// deciding what exists, what is drawn and what can be pressed across four modes, and
/// each of them was a cell nobody could see: a menu still offered under a chip, a
/// press target that was hidden, a speed reset on the way through, a chip state
/// applied once. Answered per element and asserted per mode, a wrong cell is one
/// failing row here rather than a bug found in the retail client.
///
/// The derivation is total and pure, so every row below is reachable with no game.
/// </summary>
public sealed class TransportSurfaceTests
{
    private static readonly PrefightChoice Blessing = new PrefightChoice.Blessing(0, "RELIC.LEAFY_POULTICE");

    private static readonly PrefightChoice MapMove = new PrefightChoice.MapMove(1, "Monster", 3, 7);

    private static readonly TransportIdentity NaveGreed = new(
        "NaveGreed", "Ironclad A10, Underdocks", "https://www.youtube.com/watch?v=OJ-6QXhNgdg&t=26s", "0:26");

    /// <summary>The five modes, named the way the table names them.</summary>
    public enum Column
    {
        Watching,
        LookingBack,
        Opening,
        Chip,
        Refused,
    }

    /// <summary>
    /// A journey that is not on screen has no surface at all, which is what makes the
    /// derivation total: there is no phase whose answer is "whatever was there
    /// before".
    /// </summary>
    [Theory]
    [InlineData(JourneyPhase.None)]
    [InlineData(JourneyPhase.Starting)]
    public void APhaseThatDrawsNothingAnswersWithNothing(JourneyPhase phase) =>
        Assert.Null(PlaybackTransport.For(phase, Facts()));

    [Theory]
    [InlineData(Column.Watching, TransportMode.Watching)]
    [InlineData(Column.LookingBack, TransportMode.LookingBack)]
    [InlineData(Column.Opening, TransportMode.Opening)]
    [InlineData(Column.Chip, TransportMode.Chip)]
    [InlineData(Column.Refused, TransportMode.Refused)]
    public void EachPhaseAndItsFactsProduceOneMode(Column column, TransportMode mode) =>
        Assert.Equal(mode, Surface(column).Mode);

    /// <summary>
    /// The plate. A chip is the only surface that is not the tag, and it is the only
    /// one that changes shape.
    /// </summary>
    [Theory]
    [InlineData(Column.Watching, false)]
    [InlineData(Column.LookingBack, false)]
    [InlineData(Column.Opening, false)]
    [InlineData(Column.Chip, true)]
    [InlineData(Column.Refused, false)]
    public void ThePlate(Column column, bool chip) => Assert.Equal(chip, Surface(column).Surface.ChipPlate);

    /// <summary>The mark is on every surface, and becomes the warning only under a
    /// refusal.</summary>
    [Theory]
    [InlineData(Column.Watching, TransportGlyph.Mark)]
    [InlineData(Column.LookingBack, TransportGlyph.Mark)]
    [InlineData(Column.Opening, TransportGlyph.Mark)]
    [InlineData(Column.Chip, TransportGlyph.Mark)]
    [InlineData(Column.Refused, TransportGlyph.Warn)]
    public void TheMark(Column column, TransportGlyph glyph)
    {
        var mark = Surface(column).Surface.Mark;

        Assert.Equal(Presence.Drawn, mark.Presence);
        Assert.False(mark.Pressable);
        Assert.Equal(glyph, mark.Glyph);
    }

    /// <summary>
    /// The identity block, which is a control because pressing it opens the video.
    /// The chip is the mark and the name and nothing else, so there is nothing to
    /// press on it; a refused tag has one but it does not work.
    /// </summary>
    [Theory]
    [InlineData(Column.Watching, Presence.Drawn, true)]
    [InlineData(Column.LookingBack, Presence.Drawn, true)]
    [InlineData(Column.Opening, Presence.Drawn, true)]
    [InlineData(Column.Chip, Presence.Absent, false)]
    [InlineData(Column.Refused, Presence.Drawn, false)]
    public void TheIdentityPressTarget(Column column, Presence presence, bool pressable)
    {
        var identity = Surface(column).Surface.Identity;

        Assert.Equal(presence, identity.Presence);
        Assert.Equal(pressable, identity.Pressable);
    }

    /// <summary>
    /// The video's title. Absent on the chip: the design says the chip carries the
    /// mark and the name, and a title on a plate that narrow is neither.
    /// </summary>
    [Theory]
    [InlineData(Column.Watching, Presence.Drawn)]
    [InlineData(Column.LookingBack, Presence.Drawn)]
    [InlineData(Column.Opening, Presence.Drawn)]
    [InlineData(Column.Chip, Presence.Absent)]
    [InlineData(Column.Refused, Presence.Drawn)]
    public void TheVideoTitle(Column column, Presence presence) =>
        Assert.Equal(presence, Surface(column).Surface.Title.Presence);

    /// <summary>A recording with no title in its manifest has no title element, on
    /// any surface that would otherwise draw one.</summary>
    [Fact]
    public void TheVideoTitleIsAbsentOnARecordingThatHasNone()
    {
        var untitled = new TransportIdentity("NaveGreed", null, null, null);

        Assert.Equal(
            Presence.Absent,
            PlaybackTransport.For(JourneyPhase.Watching, Facts(identity: untitled, next: Blessing))!
                .Surface.Title.Presence);
    }

    /// <summary>
    /// A recording with no video carries no tooltip on its identity block.
    ///
    /// A disabled Godot button still raises its tooltip on hover, so a block left
    /// holding the linked wording would promise to open a video the manifest does not
    /// have - which a recording made inside the player's own game never does.
    /// </summary>
    [Fact]
    public void AnIdentityWithNoVideoSaysNothingOnHover()
    {
        var noVideo = new TransportIdentity("NaveGreed", null, null, null);
        var identity = PlaybackTransport.For(JourneyPhase.Watching, Facts(identity: noVideo, next: Blessing))!
            .Surface.Identity;

        Assert.Equal(Presence.Drawn, identity.Presence);
        Assert.False(identity.Pressable);
        Assert.Equal(string.Empty, identity.TooltipTitle);
        Assert.Equal(string.Empty, identity.TooltipBody);

        // A recording that does have one still says what pressing it does.
        var linked = PlaybackTransport.For(JourneyPhase.Watching, Facts(next: Blessing))!.Surface.Identity;
        Assert.True(linked.Pressable);
        Assert.NotEqual(string.Empty, linked.TooltipBody);
    }

    /// <summary>Where in the recording's decisions this is. Gone once the decisions
    /// are behind the run and the fight is the player's.</summary>
    [Theory]
    [InlineData(Column.Watching, Presence.Drawn)]
    [InlineData(Column.LookingBack, Presence.Drawn)]
    [InlineData(Column.Opening, Presence.Drawn)]
    [InlineData(Column.Chip, Presence.Absent)]
    [InlineData(Column.Refused, Presence.Absent)]
    public void TheCounter(Column column, Presence presence) =>
        Assert.Equal(presence, Surface(column).Surface.Counter.Presence);

    /// <summary>
    /// The one press target, and the cell the whole model exists for.
    ///
    /// It is the speed control on a tag and the chip's press target on a chip, because
    /// the tag and the chip are one node. Silent is what lets it take input while
    /// showing nothing: a Godot control that is not visible receives no input at all,
    /// so a chip drawn by hiding everything had nothing left that could be pressed and
    /// both directions it offers were unreachable in the client.
    /// </summary>
    [Theory]
    [InlineData(Column.Watching, Presence.Drawn, true, Press.OpenSpeedMenu)]
    [InlineData(Column.LookingBack, Presence.Drawn, true, Press.OpenSpeedMenu)]
    [InlineData(Column.Opening, Presence.Drawn, true, Press.OpenSpeedMenu)]
    [InlineData(Column.Chip, Presence.Silent, true, Press.OpenChipMenu)]
    [InlineData(Column.Refused, Presence.Drawn, false, Press.OpenSpeedMenu)]
    public void TheSpeedAndChipPressTarget(Column column, Presence presence, bool pressable, Press press)
    {
        var speed = Surface(column).Surface.Speed;

        Assert.Equal(presence, speed.Presence);
        Assert.Equal(pressable, speed.Pressable);
        Assert.Equal(press, speed.Press);
    }

    /// <summary>The chip's press target says nothing, which is what "silent until it
    /// is pressed" means for a control whose words would otherwise be a tooltip.</summary>
    [Fact]
    public void TheChipsPressTargetCarriesNoWords()
    {
        var speed = Surface(Column.Chip).Surface.Speed;

        Assert.Equal(string.Empty, speed.TooltipTitle);
        Assert.Equal(string.Empty, speed.TooltipBody);
    }

    /// <summary>
    /// The three controls that move the run, or refuse to.
    ///
    /// Drawn and refused rather than removed everywhere they are not on offer, except
    /// on the chip, where the whole point is that nothing is offered unbidden.
    /// </summary>
    [Theory]
    [InlineData(Column.Watching, Presence.Drawn, true, true)]
    [InlineData(Column.LookingBack, Presence.Drawn, true, true)]
    [InlineData(Column.Opening, Presence.Drawn, false, false)]
    [InlineData(Column.Chip, Presence.Absent, false, false)]
    [InlineData(Column.Refused, Presence.Drawn, false, false)]
    public void TheControlsThatMoveTheRun(Column column, Presence presence, bool play, bool step)
    {
        var surface = Surface(column).Surface;

        Assert.Equal(presence, surface.Back.Presence);
        Assert.Equal(presence, surface.Play.Presence);
        Assert.Equal(presence, surface.Step.Presence);

        Assert.Equal(play, surface.Play.Pressable);
        Assert.Equal(step, surface.Step.Pressable);

        // An absent element is never pressed: its handler is unreachable, so it does
        // not carry one. That is what makes "absent" a real answer rather than a
        // control that is merely invisible and still wired up.
        if (presence == Presence.Absent)
        {
            Assert.Equal(Press.None, surface.Back.Press);
            Assert.Equal(Press.None, surface.Play.Press);
            Assert.Equal(Press.None, surface.Step.Press);
            Assert.False(surface.Back.Pressable);
            return;
        }

        Assert.Equal(Press.Back, surface.Back.Press);
        Assert.Equal(Press.PlayOrPause, surface.Play.Press);
        Assert.Equal(Press.Step, surface.Step.Press);
    }

    /// <summary>
    /// Look back is on offer whenever there is something behind, and that is a fact
    /// about the journey rather than about the mode: it means the decision before this
    /// one while watching, and the decision before the one being looked at while
    /// looking back.
    /// </summary>
    [Fact]
    public void LookBackIsOfferedWhereverThereIsSomethingBehind()
    {
        Assert.False(PlaybackTransport.For(
            JourneyPhase.Watching, Facts(next: Blessing))!.Surface.Back.Pressable);
        Assert.True(PlaybackTransport.For(
            JourneyPhase.Watching, Facts(next: MapMove, stepsTaken: 1))!.Surface.Back.Pressable);

        Assert.False(PlaybackTransport.For(
            JourneyPhase.Watching,
            Facts(made: [Blessing], next: MapMove, stepsTaken: 1, lookingBackAt: 1))!.Surface.Back.Pressable);
        Assert.True(PlaybackTransport.For(
            JourneyPhase.Watching,
            Facts(made: [Blessing, MapMove], next: MapMove, stepsTaken: 2, count: 3, lookingBackAt: 2))!
                .Surface.Back.Pressable);
    }

    /// <summary>
    /// Step is on offer only while the decision it would make is on the game's own
    /// screen.
    ///
    /// The window between committing one decision and revealing the next is a screen
    /// transition long, and a second press inside it made the next decision without
    /// anybody having been shown it. Refused there rather than hidden: a control that
    /// disappears for half a second and comes back cannot be aimed at.
    /// </summary>
    [Fact]
    public void StepIsRefusedBetweenCommittingOneChoiceAndRevealingTheNext()
    {
        var revealed = PlaybackTransport.For(
            JourneyPhase.Watching, Facts(next: MapMove, stepsTaken: 1, revealed: true))!;
        var committing = PlaybackTransport.For(
            JourneyPhase.Watching, Facts(next: MapMove, stepsTaken: 1, revealed: false))!;

        Assert.True(revealed.Surface.Step.Pressable);
        Assert.False(committing.Surface.Step.Pressable);

        // Play is refused there for the same reason: starting the sequence would make
        // the next decision without anybody having been shown it.
        Assert.False(committing.Surface.Play.Pressable);

        // The rest of the tag is unchanged: nothing moves, nothing vanishes, and the
        // counter still says which decision the run is on.
        Assert.Equal(Presence.Drawn, committing.Surface.Step.Presence);
        Assert.Equal(Presence.Drawn, committing.Surface.Play.Presence);
        Assert.Equal("2 of 2", committing.Counter.Numerals);
    }

    /// <summary>
    /// Back is refused in that same window, and it was not.
    ///
    /// The client would take the press: the ledger opened, and the reveal that
    /// followed cleared what was being looked at with no input from the player. So the
    /// control was pressable, appeared to work, and was undone a frame later - the same
    /// family as a menu row that did nothing. Play and Step were already gated on
    /// revealed for exactly this reason; Back was gated only on there being something
    /// behind it.
    ///
    /// The two refusals stay distinguishable, because "nothing behind yet" and "not
    /// yet" are different answers to a player who presses and is told no.
    /// </summary>
    [Fact]
    public void BackIsRefusedBetweenCommittingOneChoiceAndRevealingTheNext()
    {
        var revealed = PlaybackTransport.For(
            JourneyPhase.Watching, Facts(next: MapMove, stepsTaken: 1, revealed: true))!;
        var committing = PlaybackTransport.For(
            JourneyPhase.Watching, Facts(next: MapMove, stepsTaken: 1, revealed: false))!;
        var firstChoice = PlaybackTransport.For(
            JourneyPhase.Watching, Facts(next: MapMove, stepsTaken: 0, revealed: false))!;

        Assert.True(revealed.Surface.Back.Pressable);
        Assert.False(committing.Surface.Back.Pressable);

        // Refused, not gone: a control that vanishes for half a second cannot be aimed at.
        Assert.Equal(Presence.Drawn, committing.Surface.Back.Presence);

        // And it says which no it is. There is something behind here, so the reason is
        // the window rather than the absence.
        Assert.Equal(TrainerCopy.BetweenScreensDisabledReason, committing.Back.DisabledReason);
        Assert.Equal(TrainerCopy.NothingBehindYet, firstChoice.Back.DisabledReason);
    }

    /// <summary>
    /// The tag's line, the once-only sentence and the ledger.
    ///
    /// The line is on the two surfaces where the transport is waiting on the game - a
    /// hold draining under Play, and travelling while the game is between screens.
    /// The opening window is the second of those, and it is the whole reason the line
    /// is allowed there: every control is refused and, by the captain's ruling, says
    /// nothing about why, so a tag with nothing moving on it would read as broken.
    /// </summary>
    [Theory]
    [InlineData(Column.Watching, true, false, false)]
    [InlineData(Column.LookingBack, false, false, true)]
    [InlineData(Column.Opening, true, false, false)]
    [InlineData(Column.Chip, false, false, false)]
    [InlineData(Column.Refused, false, false, false)]
    public void WhatHangsUnderTheTag(Column column, bool hold, bool note, bool ledger)
    {
        var surface = Surface(column).Surface;

        Assert.Equal(hold, surface.HoldLine);
        Assert.Equal(note, surface.Note);
        Assert.Equal(ledger, surface.Ledger);
    }

    /// <summary>The once-only sentence is on the surface only while it is being said,
    /// which is the first decision of a journey that has not said it.</summary>
    [Fact]
    public void TheOnceOnlySentenceIsOnTheSurfaceOnlyWhileItIsSaid()
    {
        Assert.True(PlaybackTransport.For(
            JourneyPhase.Watching, Facts(next: Blessing, noteShown: false))!.Surface.Note);
        Assert.False(PlaybackTransport.For(
            JourneyPhase.Watching, Facts(next: Blessing, noteShown: true))!.Surface.Note);
    }

    /// <summary>
    /// Which menu the one press target offers.
    ///
    /// The strip closes any menu whose kind is not the one the surface offers, so a
    /// menu opened on a tag cannot survive the collapse to a chip - which is what left
    /// the speed menu hanging under the chip and swallowing its first press.
    /// </summary>
    [Theory]
    [InlineData(Column.Watching, MenuKind.Speed)]
    [InlineData(Column.LookingBack, MenuKind.Speed)]
    [InlineData(Column.Opening, MenuKind.Speed)]
    [InlineData(Column.Chip, MenuKind.Chip)]
    [InlineData(Column.Refused, MenuKind.None)]
    public void WhichMenuIsOffered(Column column, MenuKind menu) =>
        Assert.Equal(menu, Surface(column).Surface.Menu);

    /// <summary>
    /// The speed in force survives every mode it passes through.
    ///
    /// It did not: the chip reset it to Normal, so a player who had set 2× saw 1× on
    /// the tag afterwards and a chosen speed appeared not to have taken. A fact
    /// carried by the derivation cannot be dropped by a state nobody remembered to
    /// pass it to.
    /// </summary>
    [Theory]
    [InlineData(Column.Watching)]
    [InlineData(Column.LookingBack)]
    [InlineData(Column.Opening)]
    [InlineData(Column.Chip)]
    public void TheSpeedInForceIsCarriedThroughEveryMode(Column column) =>
        Assert.Equal("2×", Surface(column, PlaybackSpeed.Double).SpeedLabel);

    /// <summary>
    /// Whether the player has played a turn is read at the moment the chip is
    /// derived, not at the moment the fight was handed over.
    ///
    /// The hand-over built the chip by hand with "nothing played", and nothing
    /// re-derived it, so jumping to the end was refused for the whole fight and said
    /// a reason that had stopped being true at the first card.
    /// </summary>
    [Fact]
    public void TheChipsSecondDirectionFollowsWhetherAnythingHasBeenPlayed()
    {
        Assert.False(PlaybackTransport.For(
            JourneyPhase.InFight, Facts(anythingPlayed: false))!.ChipMenu[1].Enabled);
        Assert.True(PlaybackTransport.For(
            JourneyPhase.InFight, Facts(anythingPlayed: true))!.ChipMenu[1].Enabled);
    }

    /// <summary>The result screen's surface is the chip's: the run is still there
    /// underneath, and nothing new is offered over the result.</summary>
    [Fact]
    public void TheResultKeepsTheChip() =>
        Assert.Equal(TransportMode.Chip, PlaybackTransport.For(JourneyPhase.Result, Facts())!.Mode);

    /// <summary>
    /// A watched journey with no decision left to show refuses rather than inventing
    /// one, which is the same refusal the rest of this project makes.
    /// </summary>
    [Fact]
    public void RefusesToShowADecisionThatIsNotThere()
    {
        var refusal = Assert.Throws<ManifestException>(
            () => PlaybackTransport.For(JourneyPhase.Watching, Facts(next: null)));

        Assert.Contains("no decision left to show", refusal.Message, StringComparison.Ordinal);
    }

    private static PlaybackTransport Surface(Column column, PlaybackSpeed speed = PlaybackSpeed.Normal) =>
        column switch
        {
            Column.Watching => PlaybackTransport.For(
                JourneyPhase.Watching, Facts(next: MapMove, stepsTaken: 1, speed: speed))!,
            Column.LookingBack => PlaybackTransport.For(
                JourneyPhase.Watching,
                Facts(made: [Blessing], next: MapMove, stepsTaken: 1, lookingBackAt: 1, speed: speed))!,
            Column.Opening => PlaybackTransport.For(
                JourneyPhase.Watching, Facts(stepsTaken: 2, atCombatStart: true, speed: speed))!,
            Column.Chip => PlaybackTransport.For(JourneyPhase.InFight, Facts(speed: speed))!,
            Column.Refused => PlaybackTransport.For(JourneyPhase.Refused, Facts(speed: speed))!,
            _ => throw new InvalidOperationException($"No column {column}."),
        };

    private static TransportFacts Facts(
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
        new(
            identity ?? NaveGreed, made ?? [], next, stepsTaken, count, atCombatStart, revealed,
            lookingBackAt, playing, noteShown, speed, anythingPlayed);
}

using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// What the transport says while the recording makes the decisions that lead to its
/// fight.
///
/// Every assertion here is the approved sentence, character for character, produced
/// from data rather than written down. That is the whole claim being tested: a
/// second recording, by somebody else, past a different node, says the right thing
/// without any of these sentences changing.
///
/// The controls are icon only, so what is asserted about them is the glyph and the
/// tooltip rather than a label. The glyph carries the rule that matters - filled
/// shapes move the run, hollow shapes only look - and the tooltip carries the words.
/// </summary>
public sealed class PlaybackTransportTests
{
    private static readonly PrefightChoice Blessing = new PrefightChoice.Blessing(0, "RELIC.LEAFY_POULTICE");

    /// <summary>The shipped recording's map move: column 3 of the act's seven.</summary>
    private static readonly PrefightChoice MapMove = new PrefightChoice.MapMove(1, "Monster", 3, 7);

    private static readonly TransportIdentity NaveGreed = new(
        "NaveGreed", "Ironclad A10, Underdocks", "https://www.youtube.com/watch?v=OJ-6QXhNgdg&t=26s", "0:26");

    [Fact]
    public void TheFirstRevealReadsAsTheApprovedWordingForTheShippedRecording()
    {
        var transport = Revealing(Blessing, 1, noteShown: false);

        Assert.Equal(TransportMode.Watching, transport.Mode);
        Assert.Equal("NaveGreed", transport.Identity.Creator);
        Assert.Equal("Ironclad A10, Underdocks", transport.Identity.VideoTitle);
        Assert.Equal("1 of 2", transport.Counter.Numerals);
        Assert.Equal(
            "NaveGreed's choices are shown as recorded. This shows what was chosen, not why.",
            transport.Note);
    }

    /// <summary>
    /// The controls carry drawn shapes, not words, and the shapes mean something: a
    /// filled triangle commits a decision, a hollow one only re-shows it.
    /// </summary>
    [Fact]
    public void TheControlsAreGlyphsAndTheirFillSaysWhetherTheyMoveTheRun()
    {
        var transport = Revealing(MapMove, 2, noteShown: true);

        Assert.Equal(TransportGlyph.Back, transport.Back.Glyph);
        Assert.Equal(TransportGlyph.Play, transport.Play.Glyph);
        Assert.Equal(TransportGlyph.Step, transport.Step.Glyph);
    }

    /// <summary>
    /// The words the wide bar drew always live in the tooltips now, which is the
    /// captain's tooltips-only ruling. Step's names the decision it is about to make.
    /// </summary>
    [Fact]
    public void TheWordsLiveInTheTooltipsAndStepNamesTheDecision()
    {
        var transport = Revealing(Blessing, 1, noteShown: true);

        Assert.Equal("Look back", transport.Back.TooltipTitle);
        Assert.Equal("Shows an earlier choice again. Nothing is undone.", transport.Back.TooltipBody);
        Assert.Equal("Play", transport.Play.TooltipTitle);
        Assert.Equal("Step", transport.Step.TooltipTitle);
        Assert.Contains("Makes this choice, then shows the next.", transport.Step.TooltipBody,
            StringComparison.Ordinal);
        Assert.Contains("1 of 2 · NaveGreed took Leafy Poultice", transport.Step.TooltipBody,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheSecondRevealIsTheMapMoveAndSaysNothingAboutHowToReadIt()
    {
        var transport = Revealing(MapMove, 2, noteShown: true);

        Assert.Equal("2 of 2", transport.Counter.Numerals);
        Assert.Contains("NaveGreed moved to the Monster node, centre column", transport.Step.TooltipBody,
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, transport.Note);
    }

    /// <summary>
    /// The sentence about how to read these screens is said once. It is a rule, and a
    /// rule repeated above every decision is a rule nobody reads.
    /// </summary>
    [Fact]
    public void TheOnceOnlySentenceIsSaidOnceAndOnTheFirstDecision()
    {
        Assert.NotEqual(string.Empty, Revealing(Blessing, 1, noteShown: false).Note);
        Assert.Equal(string.Empty, Revealing(Blessing, 1, noteShown: true).Note);
        Assert.Equal(string.Empty, Revealing(MapMove, 2, noteShown: false).Note);
    }

    /// <summary>
    /// Look back is offered from the second decision on, and says why when it is not.
    /// A control that does nothing is worse than one that is plainly off and explains
    /// itself on hover.
    /// </summary>
    [Fact]
    public void LookingBackIsOfferedOnlyOnceThereIsSomethingBehind()
    {
        var first = Revealing(Blessing, 1, noteShown: false);
        Assert.False(first.Back.Enabled);
        Assert.Equal("This is the first choice.", first.Back.DisabledReason);
        Assert.True(Revealing(MapMove, 2, noteShown: true).Back.Enabled);
    }

    /// <summary>Play is the only control that changes what it carries, and it carries
    /// what pressing it does next.</summary>
    [Fact]
    public void PlayBecomesPauseWhileItIsRunning()
    {
        Assert.Equal(TransportGlyph.Play, Revealing(Blessing, 1, noteShown: true).Play.Glyph);
        Assert.Equal(
            TransportGlyph.Pause,
            For(JourneyPhase.Watching, next: Blessing, playing: true).Play.Glyph);
    }

    /// <summary>
    /// The pips are a picture of where the journey is, and they stop being one when
    /// there are too many to read. The numerals never stop.
    /// </summary>
    [Fact]
    public void ThePipsAreDrawnOnlyWhileTheyCanBeRead()
    {
        Assert.True(Revealing(Blessing, 1, noteShown: true).Counter.ShowPips);
        Assert.False(For(JourneyPhase.Watching, next: Blessing, count: 40).Counter.ShowPips);
        Assert.Equal("1 of 40", For(JourneyPhase.Watching, next: Blessing, count: 40).Counter.Numerals);
    }

    /// <summary>
    /// Looking back lists what has been chosen so far, because the screens those
    /// choices were made on are gone. The rows do not repeat the creator's name; the
    /// tag hanging above them carries it once.
    /// </summary>
    [Fact]
    public void LookingBackListsTheDecisionsAlreadyMadeWithoutRepeatingTheName()
    {
        var transport = For(
            JourneyPhase.Watching, made: [Blessing], next: MapMove, stepsTaken: 1, lookingBackAt: 1);

        Assert.Equal(TransportMode.LookingBack, transport.Mode);
        Assert.Equal("1 of 2", transport.Counter.Numerals);
        Assert.Equal(2, transport.Counter.Current);
        Assert.Equal(1, transport.Counter.LookingAt);

        Assert.Equal(2, transport.Ledger.Count);
        Assert.Equal("Leafy Poultice", transport.Ledger[0].Label);
        Assert.Equal("RELIC.LEAFY_POULTICE", transport.Ledger[0].ArtModelId);
        Assert.True(transport.Ledger[0].IsLookedAt);
        Assert.Equal("Monster node, centre column", transport.Ledger[1].Label);
        Assert.True(transport.Ledger[1].IsCurrent);
        Assert.DoesNotContain("NaveGreed", transport.Ledger[0].Label, StringComparison.Ordinal);
    }

    /// <summary>
    /// Step does not promise a commit it will not make.
    ///
    /// While looking back, pressing Step walks the view forward through decisions the
    /// recording already made; it commits nothing. The tooltip said "Makes this choice,
    /// then shows the next" there anyway, which is a control naming an action it does
    /// not perform - the same family as a menu row that did nothing.
    ///
    /// The false sentence is removed rather than replaced. Deleting a statement that
    /// has become untrue is a correction; writing a new true one would be a wording
    /// decision, and the counter and the caption already say which decision is on
    /// screen. Watching keeps the sentence, because there it is true.
    /// </summary>
    [Fact]
    public void StepPromisesACommitOnlyWhereItMakesOne()
    {
        var lookingBack = For(
            JourneyPhase.Watching, made: [Blessing], next: MapMove, stepsTaken: 1, lookingBackAt: 1);
        var watching = For(
            JourneyPhase.Watching, made: [Blessing], next: MapMove, stepsTaken: 1);

        Assert.DoesNotContain(
            TrainerCopy.StepTooltipBody, lookingBack.Step.TooltipBody, StringComparison.Ordinal);
        Assert.Contains(
            TrainerCopy.StepTooltipBody, watching.Step.TooltipBody, StringComparison.Ordinal);

        // What is left still says which decision is being looked at, so removing the
        // sentence took nothing the player needed.
        Assert.Contains("Monster node", lookingBack.Step.TooltipBody, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesToLookBackAtADecisionThatWasNeverMade()
    {
        Assert.Throws<ManifestException>(() => For(
            JourneyPhase.Watching, made: [Blessing], next: MapMove, stepsTaken: 1, lookingBackAt: 2));
    }

    /// <summary>
    /// The player's own fight. The tag collapses to a chip that says only whose
    /// surface this is, because the captain's ruling is that comparing inside a fight
    /// is second-order: a player diverges from the recorded line almost at once.
    /// </summary>
    [Fact]
    public void DuringThePlayersFightTheTagIsASilentChip()
    {
        var transport = For(JourneyPhase.InFight, anythingPlayed: true);

        Assert.Equal(TransportMode.Chip, transport.Mode);
        Assert.Equal("NaveGreed", transport.Identity.Creator);
        Assert.Equal(string.Empty, transport.Note);
        foreach (var element in new[]
                 {
                     transport.Surface.Back, transport.Surface.Play, transport.Surface.Step,
                     transport.Surface.Counter,
                 })
        {
            Assert.Equal(Presence.Absent, element.Presence);
        }
    }

    /// <summary>
    /// Pressed, the chip offers two directions and no third. There is no watch row and
    /// no comparison inside the fight; both were removed by the round-three ruling.
    /// </summary>
    [Fact]
    public void TheChipOffersTwoDirectionsAndNoThird()
    {
        var menu = For(JourneyPhase.InFight, anythingPlayed: true).ChipMenu;

        Assert.Equal(2, menu.Count);
        Assert.Equal("Jump to the beginning", menu[0].Label);
        Assert.Equal(TransportGlyph.Again, menu[0].Glyph);
        Assert.Equal("Jump to the end", menu[1].Label);
        Assert.Equal(TransportGlyph.Jump, menu[1].Glyph);
        Assert.True(menu[1].Enabled);
    }

    /// <summary>
    /// Between the last recorded choice and the fight it leads to there is nothing
    /// left to commit, and the fight takes as long as it takes to open. Every control
    /// that would move the run is refused there and says why, rather than staying
    /// offered over a run that has run out of decisions.
    /// </summary>
    [Fact]
    public void NothingMovesTheRunWhileTheFightIsOpening()
    {
        var transport = For(JourneyPhase.Watching, atCombatStart: true, speed: PlaybackSpeed.Double);

        Assert.Equal(TransportMode.Opening, transport.Mode);
        Assert.Equal("2×", transport.SpeedLabel);
        Assert.Equal("2 of 2", transport.Counter.Numerals);
        foreach (var control in new[] { transport.Back, transport.Play, transport.Step })
        {
            Assert.False(control.Enabled);

            // No reason, because none has been approved for this window. A refused
            // control with no reason says what it does, which is what the surface
            // falls back to rather than inventing a sentence.
            Assert.Null(control.DisabledReason);
        }

        Assert.Equal(
            "Makes the rest of the choices, pausing on each one.", transport.Surface.Play.TooltipBody);
    }

    /// <summary>With nothing played there is no attempt to finish, so the end is
    /// refused, silently, rather than producing an empty result. One action clears
    /// it.</summary>
    [Fact]
    public void JumpingToTheEndIsRefusedBeforeAnythingHasBeenPlayed()
    {
        var menu = For(JourneyPhase.InFight, anythingPlayed: false).ChipMenu;

        Assert.False(menu[1].Enabled);
        Assert.True(menu[0].Enabled);
    }

    /// <summary>
    /// A refusal is the tag's business only in so far as it stops offering things. The
    /// sentence a player reads is the popup's.
    /// </summary>
    [Fact]
    public void ARefusalTakesTheMarkAndEveryControl()
    {
        var transport = For(JourneyPhase.Refused);

        Assert.Equal(TransportGlyph.Warn, transport.Mark);
        Assert.False(transport.Back.Enabled);
        Assert.False(transport.Play.Enabled);
        Assert.False(transport.Step.Enabled);
        Assert.Equal("Combat Trainer stopped; dismiss the message first.", transport.Step.DisabledReason);
    }

    /// <summary>The mark is the reticle the reveal lights, which is the mod marked by
    /// the thing it does.</summary>
    [Fact]
    public void TheMarkIsTheReticleExceptWhileARefusalIsUp()
    {
        Assert.Equal(TransportGlyph.Mark, Revealing(Blessing, 1, noteShown: true).Mark);
        Assert.Equal(TransportGlyph.Mark, For(JourneyPhase.InFight, anythingPlayed: true).Mark);
    }

    /// <summary>
    /// The identity block names the video and opens it where the move is made. A
    /// recording whose manifest has no title falls back to the creator alone rather
    /// than inventing one.
    /// </summary>
    [Fact]
    public void TheIdentityBlockNamesTheVideoAndOpensItAtTheMoment()
    {
        var transport = Revealing(Blessing, 1, noteShown: true);

        Assert.True(transport.Identity.IsLink);
        Assert.Equal("NaveGreed · Ironclad A10, Underdocks", transport.Identity.TooltipTitle);
        Assert.Equal("Opens the video at 0:26, where this move is made.", transport.Identity.TooltipBody);

        var untitled = new TransportIdentity("NaveGreed", null, null, null);
        Assert.Equal("NaveGreed", untitled.TooltipTitle);
        Assert.False(untitled.IsLink);
    }

    /// <summary>
    /// Speed divides the hold and never crosses the screen's own floor: a hold shorter
    /// than the game's own animation would commit while the last decision was still
    /// being shown.
    /// </summary>
    [Fact]
    public void SpeedDividesTheHoldButNeverBelowTheScreensFloor()
    {
        Assert.Equal(0.8, PlaybackSpeed.Double.Divide(1.6, floor: 0.2), 3);
        Assert.Equal(3.2, PlaybackSpeed.Half.Divide(1.6, floor: 0.2), 3);
        Assert.Equal(1.6, PlaybackSpeed.Normal.Divide(1.6, floor: 0.2), 3);

        // The map's floor is the game's own one-second select effect.
        Assert.Equal(1.0, PlaybackSpeed.Double.Divide(1.2, floor: 1.0), 3);
    }

    [Fact]
    public void TheSpeedMenuMarksTheSpeedInUse()
    {
        var transport = For(JourneyPhase.Watching, next: Blessing, speed: PlaybackSpeed.OneAndAHalf);

        Assert.Equal("1.5×", transport.SpeedLabel);
        Assert.Equal(["0.5×", "1×", "1.5×", "2×"], transport.SpeedMenu.Select(row => row.Label));
        Assert.True(transport.SpeedMenu[2].IsCurrent);
        Assert.False(transport.SpeedMenu[1].IsCurrent);
    }

    /// <summary>
    /// The same sentences about a different recording. Nothing about NaveGreed, the
    /// Underdocks or a Sludge Spinner is in the wording, so a second manifest reaches
    /// the screens without any of it being edited.
    /// </summary>
    [Fact]
    public void AnotherRecordingIsDescribedByTheSameSentences()
    {
        var other = new TransportIdentity("Someone Else", null, null, null);
        var blessing = For(
            JourneyPhase.Watching, other, next: new PrefightChoice.Blessing(0, "RELIC.ARCANE_SCROLL"));
        var move = For(
            JourneyPhase.Watching, other, next: new PrefightChoice.MapMove(1, "Event", 0, 7), stepsTaken: 1);

        Assert.Contains("Someone Else took Arcane Scroll", blessing.Step.TooltipBody, StringComparison.Ordinal);
        Assert.Contains(
            "Someone Else moved to the Event node, left column", move.Step.TooltipBody, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAStepNumberTheJourneyDoesNotHave()
    {
        Assert.Throws<ManifestException>(() => For(JourneyPhase.Watching, next: Blessing, stepsTaken: -1));
        Assert.Throws<ManifestException>(() => For(JourneyPhase.Watching, next: Blessing, stepsTaken: 2));
    }

    /// <summary>
    /// A decision with no approved caption refuses rather than getting a generic one.
    /// Only two kinds of screen are walked past in this proof, and a third described
    /// in words nobody approved would be the host inventing copy.
    /// </summary>
    [Fact]
    public void RefusesADecisionItHasNoApprovedCaptionFor()
    {
        var refusal = Assert.Throws<ManifestException>(() =>
            For(JourneyPhase.Watching, next: new UnknownChoice(4), count: 1));

        Assert.Contains("no way to describe", refusal.Message, StringComparison.Ordinal);
    }

    private static PlaybackTransport Revealing(PrefightChoice choice, int number, bool noteShown) =>
        For(JourneyPhase.Watching, next: choice, stepsTaken: number - 1, noteShown: noteShown);

    /// <summary>
    /// The one way in, which is the point of the model: a state is what the phase and
    /// the facts say it is, and there is no other constructor to reach around it with.
    /// </summary>
    internal static PlaybackTransport For(
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

    private sealed record UnknownChoice(int Seq) : PrefightChoice(Seq);
}

/// <summary>
/// Where a column sits, in the words the caption uses. Thirds of the act's own
/// width, because the same column number is a different place on a wider map.
/// </summary>
public sealed class MapColumnTests
{
    [Theory]
    [InlineData(0, 7, "left")]
    [InlineData(1, 7, "left")]
    [InlineData(2, 7, "left")]
    [InlineData(3, 7, "centre")]
    [InlineData(4, 7, "centre")]
    [InlineData(5, 7, "right")]
    [InlineData(6, 7, "right")]
    [InlineData(0, 3, "left")]
    [InlineData(1, 3, "centre")]
    [InlineData(2, 3, "right")]
    public void ThirdsOfTheActsOwnWidth(int column, int columnCount, string expected) =>
        Assert.Equal(expected, MapColumns.Position(column, columnCount));

    [Fact]
    public void RefusesAColumnThisActDoesNotHave()
    {
        Assert.Throws<ManifestException>(() => MapColumns.Position(7, 7));
        Assert.Throws<ManifestException>(() => MapColumns.Position(-1, 7));
        Assert.Throws<ManifestException>(() => MapColumns.Position(0, 0));
    }
}

/// <summary>
/// Who a recording is by, and what the screens that name them say.
/// </summary>
public sealed class RecordingIdentityTests
{
    [Fact]
    public void TheCreatorAndTheSubtitleComeFromTheManifest()
    {
        var recording = Fixtures.Recording();

        Assert.Equal("NaveGreed", RecordingIdentity.Creator(recording));
        Assert.Equal(
            "NaveGreed · Ironclad · Ascension 10 · Floor 2 · Sludge Spinner",
            RecordingIdentity.Subtitle(recording));
        Assert.Equal(
            "Fight NaveGreed's Floor 2 Sludge Spinner exactly as recorded, then compare your fight with " +
            "the recording. Reads your game; never writes to it.",
            RecordingIdentity.Description(recording));
    }

    /// <summary>A recording that does not say whose run it is cannot be attributed,
    /// and a channel id on screen in place of a name would be an attribution this
    /// host invented.</summary>
    [Fact]
    public void RefusesARecordingThatDoesNotSayWhoseRunItIs()
    {
        var refusal = Assert.Throws<ManifestException>(() =>
            RecordingIdentity.Creator(Fixtures.Recording(creator: null)));

        Assert.Contains("does not say whose run it is", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnotherRecordingsSubtitleNamesItsOwnCharacterAndAscension()
    {
        var recording = Fixtures.Recording(
            Fixtures.Identity(),
            creator: "Someone Else") with
        {
            Environment = Fixtures.Identity() with
            {
                Character = Fact<string>.Observed(
                    "CHARACTER.SILENT", FactEvidence.AtVideoTime(9000, "sprite")),
                Ascension = Fact<int>.Observed(4, FactEvidence.AtVideoTime(9000, "badge")),
            },
        };

        Assert.StartsWith("Someone Else · Silent · Ascension 4 · ",
            RecordingIdentity.Subtitle(recording), StringComparison.Ordinal);
    }
}

/// <summary>
/// The offer of the fight, on the eligibility screen.
/// </summary>
public sealed class FightOfferTests
{
    [Fact]
    public void TheFightIsNotOfferedUnlessTheHostSaysTheRunCanBeConstructed()
    {
        Assert.False(Fixtures.Screen().FightOffered);
        Assert.True(Fixtures.Screen(fightOffered: true).FightOffered);
    }

    [Fact]
    public void TheOfferCarriesTheApprovedWording()
    {
        var screen = Fixtures.Screen(fightOffered: true);

        Assert.Equal("Enter the fight", screen.EnterButton);
        Assert.Equal(
            "This fight is not saved and does not count toward your run history.",
            screen.NotSavedNote);
    }
}

/// <summary>
/// What the rows are about, which is decided by the reading the screen is handed.
///
/// The trainer constructs the recording's run against a supplied complete unlock
/// state, so that is the reading its screen asks for and every row is a requirement
/// of the fight on offer. The profile reading still exists and still behaves as it
/// did; what changed is which question the in-game host asks.
/// </summary>
public sealed class SuppliedReadingRowTests
{
    /// <summary>
    /// The row this exists for. A profile whose ascension ceiling is below the
    /// recording's does not stop the trainer constructing the run at the recording's
    /// ascension, so a red row above an enabled offer would be warning about
    /// something that stops nothing.
    /// </summary>
    [Fact]
    public void TheAscensionRowReportsTheStateTheRunIsGeneratedAgainst()
    {
        var screen = Fixtures.Screen(Fixtures.SuppliedPrerequisites(), fightOffered: true);
        var row = screen.Row("Ascension 10 available on");

        Assert.True(row.Met);
        Assert.Equal("Ascension 10 available on Ironclad", row.Label);
        Assert.True(screen.Eligible);
    }

    /// <summary>
    /// The profile note names the profile the rows were measured against, so it is
    /// said only where there was one. Pointing a player at the game's profile import
    /// over rows nothing read from a profile would send them to fix something that is
    /// not broken.
    /// </summary>
    [Fact]
    public void TheProfileNoteIsAbsentWhenNoProfileWasRead()
    {
        Assert.Equal(string.Empty, Fixtures.Screen(Fixtures.SuppliedPrerequisites()).ProfileNote);
        Assert.Equal(TrainerCopy.ProfileNote, Fixtures.Screen().ProfileNote);
    }

    /// <summary>
    /// The other reading is untouched. Asked about a real profile, the same rule still
    /// refuses an ascension the profile cannot reach and still says what raises it.
    /// </summary>
    [Fact]
    public void TheProfileReadingStillRefusesAnAscensionThatProfileCannotReach()
    {
        var screen = Fixtures.Screen(Fixtures.Prerequisites(ascensionCeiling: 9));
        var row = screen.Row("Ascension 10 available on");

        Assert.False(row.Met);
        Assert.Contains("highest available ascension", row.Note!, StringComparison.Ordinal);
        Assert.Equal(TrainerCopy.ProfileNote, screen.ProfileNote);
    }

    /// <summary>
    /// The rows that no host can supply are read from this installation either way, so
    /// a mismatched build still refuses whichever question was asked.
    /// </summary>
    [Fact]
    public void TheRowsNoHostCanSupplyStillGateUnderASuppliedReading()
    {
        var reading = Fixtures.SuppliedPrerequisites() with { BuildVersion = "v0.110.0" };
        var screen = Fixtures.Screen(reading);

        Assert.False(screen.Eligible);
        Assert.False(screen.Row("Build ").Met);
    }
}

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
/// </summary>
public sealed class PlaybackTransportTests
{
    private static readonly PrefightChoice Blessing = new PrefightChoice.Blessing(0, "RELIC.LEAFY_POULTICE");

    /// <summary>The shipped recording's map move: column 3 of the act's seven.</summary>
    private static readonly PrefightChoice MapMove = new PrefightChoice.MapMove(1, "Monster", 3, 7);

    [Fact]
    public void TheFirstRevealReadsAsTheApprovedWordingForTheShippedRecording()
    {
        var transport = PlaybackTransport.Revealing(
            "NaveGreed", Blessing, number: 1, count: 2, playing: false, noteShown: false);

        Assert.Equal(TransportMode.Watching, transport.Mode);
        Assert.Equal("Watching NaveGreed", transport.Chip);
        Assert.Equal("1 of 2", transport.Counter);
        Assert.Equal("NaveGreed took Leafy Poultice", transport.Caption);
        Assert.Equal(
            "NaveGreed's choices are shown as recorded. This shows what was chosen, not why.",
            transport.Note);
        Assert.Equal("Forward", transport.Forward.Label);
        Assert.Equal("Play", transport.Play.Label);
        Assert.Equal("Back", transport.Back.Label);
    }

    [Fact]
    public void TheSecondRevealIsTheMapMoveAndSaysNothingAboutHowToReadIt()
    {
        var transport = PlaybackTransport.Revealing(
            "NaveGreed", MapMove, number: 2, count: 2, playing: false, noteShown: true);

        Assert.Equal("2 of 2", transport.Counter);
        Assert.Equal("NaveGreed moved to the Monster node, centre column", transport.Caption);
        Assert.Equal(string.Empty, transport.Note);
    }

    /// <summary>
    /// The sentence about how to read these screens is said once. It is a rule, and a
    /// rule repeated above every decision is a rule nobody reads.
    /// </summary>
    [Fact]
    public void TheOnceOnlySentenceIsSaidOnceAndOnTheFirstDecision()
    {
        Assert.NotEqual(string.Empty, PlaybackTransport
            .Revealing("NaveGreed", Blessing, 1, 2, playing: false, noteShown: false).Note);
        Assert.Equal(string.Empty, PlaybackTransport
            .Revealing("NaveGreed", Blessing, 1, 2, playing: false, noteShown: true).Note);
        Assert.Equal(string.Empty, PlaybackTransport
            .Revealing("NaveGreed", MapMove, 2, 2, playing: false, noteShown: false).Note);
    }

    /// <summary>
    /// Back is offered from the second decision on. There is nothing behind the first
    /// one, and a control that does nothing is worse than one that is plainly off.
    /// </summary>
    [Fact]
    public void BackIsOfferedOnlyOnceThereIsSomethingBehind()
    {
        Assert.False(PlaybackTransport
            .Revealing("NaveGreed", Blessing, 1, 2, playing: false, noteShown: false).Back.Enabled);
        Assert.True(PlaybackTransport
            .Revealing("NaveGreed", MapMove, 2, 2, playing: false, noteShown: true).Back.Enabled);
    }

    /// <summary>Play is the only control that changes what it says, and it says what
    /// pressing it does next.</summary>
    [Fact]
    public void PlayBecomesPauseWhileItIsRunning()
    {
        Assert.Equal("Play", PlaybackTransport
            .Revealing("NaveGreed", Blessing, 1, 2, playing: false, noteShown: true).Play.Label);
        Assert.Equal("Pause", PlaybackTransport
            .Revealing("NaveGreed", Blessing, 1, 2, playing: true, noteShown: true).Play.Label);
    }

    /// <summary>
    /// Looking back says so. A counter alone over a decision already made would read
    /// as the one about to happen, which is the one misreading this mode can cause.
    /// </summary>
    [Fact]
    public void LookingBackNamesItselfAndKeepsTheSameCaption()
    {
        var transport = PlaybackTransport.LookingBackAt("NaveGreed", Blessing, number: 1, count: 2);

        Assert.Equal(TransportMode.LookingBack, transport.Mode);
        Assert.Equal("Last step · 1 of 2", transport.Counter);
        Assert.Equal("NaveGreed took Leafy Poultice", transport.Caption);
        Assert.Equal(string.Empty, transport.Note);
        Assert.False(transport.Back.Enabled);
        Assert.True(transport.Forward.Enabled);
    }

    /// <summary>
    /// The player's own fight. The strip collapses to a chip that carries the
    /// trainer's name and offers nothing: the recording's line is not shown beside a
    /// fight it is not part of.
    /// </summary>
    [Fact]
    public void DuringThePlayersFightTheStripIsASilentChip()
    {
        var transport = PlaybackTransport.DuringYourFight();

        Assert.Equal(TransportMode.Chip, transport.Mode);
        Assert.False(transport.HasControls);
        Assert.Equal("Combat Trainer", transport.Chip);
        Assert.Equal(string.Empty, transport.Counter);
        Assert.Equal(string.Empty, transport.Caption);
        Assert.Equal(string.Empty, transport.Note);
        Assert.False(transport.Back.Enabled);
        Assert.False(transport.Forward.Enabled);
        Assert.False(transport.Play.Enabled);
    }

    /// <summary>
    /// The same sentences about a different recording. Nothing about NaveGreed, the
    /// Underdocks or a Sludge Spinner is in the wording, so a second manifest reaches
    /// the screens without any of it being edited.
    /// </summary>
    [Fact]
    public void AnotherRecordingIsDescribedByTheSameSentences()
    {
        var blessing = PlaybackTransport.Revealing(
            "Someone Else", new PrefightChoice.Blessing(0, "RELIC.ARCANE_SCROLL"), 1, 2, false, true);
        var move = PlaybackTransport.Revealing(
            "Someone Else", new PrefightChoice.MapMove(1, "Event", 0, 7), 2, 2, false, true);

        Assert.Equal("Watching Someone Else", blessing.Chip);
        Assert.Equal("Someone Else took Arcane Scroll", blessing.Caption);
        Assert.Equal("Someone Else moved to the Event node, left column", move.Caption);
    }

    /// <summary>
    /// A host reaches the recording's later screens one at a time, because a caption
    /// names what the run is standing in front of. The counter still has to say where
    /// in the whole journey a step is.
    /// </summary>
    [Fact]
    public void TheCounterCountsTheWholeJourneyNotTheStepsRevealedSoFar()
    {
        Assert.Equal("2 of 5", PlaybackTransport
            .Revealing("NaveGreed", MapMove, number: 2, count: 5, playing: false, noteShown: true).Counter);
    }

    [Fact]
    public void RefusesAStepNumberTheJourneyDoesNotHave()
    {
        Assert.Throws<ManifestException>(() =>
            PlaybackTransport.Revealing("NaveGreed", Blessing, 0, 2, false, true));
        Assert.Throws<ManifestException>(() =>
            PlaybackTransport.Revealing("NaveGreed", Blessing, 3, 2, false, true));
        Assert.Throws<ManifestException>(() =>
            PlaybackTransport.LookingBackAt("NaveGreed", Blessing, 3, 2));
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
            PlaybackTransport.Revealing("NaveGreed", new UnknownChoice(4), 1, 1, false, true));

        Assert.Contains("no way to describe", refusal.Message, StringComparison.Ordinal);
    }

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

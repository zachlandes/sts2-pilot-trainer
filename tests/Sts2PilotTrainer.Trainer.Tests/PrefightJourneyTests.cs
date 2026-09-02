using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// What a player reads while the recording makes the decisions that lead to its
/// fight.
///
/// Every assertion here is the approved sentence, character for character, produced
/// from data rather than written down. That is the whole claim being tested: a
/// second recording, by somebody else, past a different node, says the right thing
/// without any of these sentences changing.
/// </summary>
public sealed class PrefightJourneyTests
{
    private static readonly PrefightChoice Blessing = new PrefightChoice.Blessing(0, "RELIC.LEAFY_POULTICE");

    /// <summary>The shipped recording's map move: column 3 of the act's seven.</summary>
    private static readonly PrefightChoice MapMove = new PrefightChoice.MapMove(1, "Monster", 3, 7);

    [Fact]
    public void TheJourneyReadsAsTheApprovedWordingForTheShippedRecording()
    {
        var journey = PrefightJourney.For("NaveGreed", [Blessing, MapMove]);

        Assert.Equal("Watching NaveGreed", journey.Chip);
        Assert.Equal("Next", journey.NextButton);
        Assert.Equal("Skip to the fight", journey.SkipButton);
        Assert.Equal(
            "NaveGreed's choices are shown as recorded. This shows what was chosen, not why.",
            journey.ChoicesShownAsRecorded);
        Assert.Equal("1 of 2", journey.Steps[0].Counter);
        Assert.Equal("NaveGreed took Leafy Poultice", journey.Steps[0].Caption);
        Assert.Equal("2 of 2", journey.Steps[1].Counter);
        Assert.Equal("NaveGreed moved to the Monster node, centre column", journey.Steps[1].Caption);
    }

    /// <summary>
    /// The same sentences about a different recording. Nothing about NaveGreed, the
    /// Underdocks or a Sludge Spinner is in the wording, so a second manifest reaches
    /// the screens without any of it being edited.
    /// </summary>
    [Fact]
    public void AnotherRecordingIsDescribedByTheSameSentences()
    {
        var journey = PrefightJourney.For("Someone Else",
        [
            new PrefightChoice.Blessing(0, "RELIC.ARCANE_SCROLL"),
            new PrefightChoice.MapMove(1, "Event", 0, 7),
        ]);

        Assert.Equal("Watching Someone Else", journey.Chip);
        Assert.Equal("Someone Else took Arcane Scroll", journey.Steps[0].Caption);
        Assert.Equal("Someone Else moved to the Event node, left column", journey.Steps[1].Caption);
    }

    /// <summary>
    /// A host reaches the recording's later screens one at a time, because a caption
    /// names what the run is standing in front of. The counter still has to say where
    /// in the whole journey a step is.
    /// </summary>
    [Fact]
    public void TheCounterCountsTheWholeJourneyNotTheStepsDescribedSoFar()
    {
        var journey = PrefightJourney.For("NaveGreed", [Blessing], stepCount: 2);

        Assert.Single(journey.Steps);
        Assert.Equal("1 of 2", journey.Steps[0].Counter);
    }

    [Fact]
    public void RefusesToDescribeMoreStepsThanTheRecordingMakes()
    {
        var refusal = Assert.Throws<ManifestException>(() =>
            PrefightJourney.For("NaveGreed", [Blessing, MapMove], stepCount: 1));

        Assert.Contains("more than the recording makes", refusal.Message, StringComparison.Ordinal);
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
            PrefightJourney.For("NaveGreed", [new UnknownChoice(4)]));

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

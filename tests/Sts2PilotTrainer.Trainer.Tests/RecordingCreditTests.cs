using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// Whose recording a screen says it is, over the two kinds this build carries.
///
/// The defect this pins: every sentence the playback shows was a template over a
/// channel name, and a run the player recorded themselves has none - so naming its
/// creator threw, and the journey refused before the run was built. Crediting it
/// needed more than a substitute name, because the same slot is a sentence subject in
/// one caption and a possessive in the next.
///
/// So the assertions are about English as much as about the reading: every template
/// that names anybody is rendered for both kinds and read back. A single name forced
/// into all of them produces "Watch You's fight", which is the failure a test that only
/// checked "does not throw" would have shipped.
/// </summary>
public sealed class RecordingCreditTests
{
    private static readonly RecordingCredit Theirs = RecordingIdentity.Credit(Fixtures.Recording());
    private static readonly RecordingCredit Mine = RecordingIdentity.Credit(Fixtures.NativeRecording());

    [Fact]
    public void ARecordingFromAVideoIsCreditedToItsChannel()
    {
        Assert.Equal("NaveGreed", Theirs.Subject);
        Assert.Equal("NaveGreed", Theirs.OpeningSubject);
        Assert.Equal("NaveGreed's", Theirs.Possessive);
        Assert.Equal("NaveGreed", Theirs.Label);
        Assert.False(Theirs.IsYours);
        Assert.Equal("NaveGreed", RecordingIdentity.Creator(Fixtures.Recording()));
    }

    [Fact]
    public void ARunThePlayerRecordedIsCreditedToThemRatherThanRefused()
    {
        Assert.Equal("you", Mine.Subject);
        Assert.Equal("You", Mine.OpeningSubject);
        Assert.Equal("your", Mine.Possessive);
        Assert.Equal("Your run", Mine.Label);
        Assert.True(Mine.IsYours);
        Assert.Equal("Your run", RecordingIdentity.Creator(Fixtures.NativeRecording()));
    }

    /// <summary>
    /// The library's own creator line is unchanged, and deliberately so.
    ///
    /// It asks a different question - does this recording carry an attribution - and a
    /// row that answered "Your run" there would be telling the player who they are
    /// under their own run's name.
    /// </summary>
    [Fact]
    public void TheLibraryStillReadsNoCreatorOffARunOfYourOwn()
    {
        Assert.Equal("NaveGreed", RecordingIdentity.CreatorOrNull(Fixtures.Recording()));
        Assert.Null(RecordingIdentity.CreatorOrNull(Fixtures.NativeRecording()));
    }

    /// <summary>A manifest that is neither is still refused: a generated fixture has
    /// nobody behind it and was played by nobody, so there is nothing to credit.</summary>
    [Fact]
    public void ARecordingThatIsNeitherIsStillRefused()
    {
        var neither = Fixtures.Recording(creator: null);

        Assert.Null(RecordingIdentity.CreditOrNull(neither));
        var refusal = Assert.Throws<ManifestException>(() => RecordingIdentity.Credit(neither));
        Assert.Contains("does not say whose run it is", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EverySentenceThatNamesTheirRecordingReadsAsItAlwaysDid()
    {
        Assert.Equal("Shows what NaveGreed chose here.", TrainerCopy.ShowTooltipBody(Theirs));
        Assert.Equal("Start NaveGreed's fight again?", TrainerCopy.ConfirmJumpToTheBeginningTitle(Theirs));
        Assert.Equal("NaveGreed took Burning Blood", TrainerCopy.BlessingCaption(Theirs, "RELIC.BURNING_BLOOD"));
        Assert.Equal("Watch NaveGreed's fight", TrainerCopy.WatchTheirFight(Theirs));
        Assert.Equal("Your fight and NaveGreed's", TrainerCopy.ComparisonTitle(Theirs));
        Assert.StartsWith("NaveGreed's choices are shown as recorded", TrainerCopy.ChoicesShownAsRecorded(Theirs),
            StringComparison.Ordinal);
        Assert.StartsWith("You have seen NaveGreed's fight this sitting", TrainerCopy.ShownThisSittingTooltip(Theirs),
            StringComparison.Ordinal);
        Assert.Contains("doesn't match NaveGreed's recording", TrainerCopy.RefusalHeadline(Theirs, "card"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The same sentences about a run of your own, read back in full.
    ///
    /// Every one of these is a slot a bare name would have broken, and they are asserted
    /// as whole strings rather than by absence of an apostrophe: what has to be true is
    /// that a player reads English, not that one wrong spelling is missing.
    /// </summary>
    [Fact]
    public void EverySentenceThatNamesYourOwnRecordingIsInTheSecondPerson()
    {
        Assert.Equal("Shows what you chose here.", TrainerCopy.ShowTooltipBody(Mine));
        Assert.Equal("Start your fight again?", TrainerCopy.ConfirmJumpToTheBeginningTitle(Mine));
        Assert.Equal("You took Burning Blood", TrainerCopy.BlessingCaption(Mine, "RELIC.BURNING_BLOOD"));
        Assert.Equal("You chose Strike Ironclad", TrainerCopy.CardFromScreenCaption(Mine, "CARD.STRIKE_IRONCLAD"));
        Assert.Equal("Watch your fight", TrainerCopy.WatchTheirFight(Mine));
        Assert.Equal("This fight and your recorded one", TrainerCopy.ComparisonTitle(Mine));
        Assert.StartsWith("Your choices are shown as recorded", TrainerCopy.ChoicesShownAsRecorded(Mine),
            StringComparison.Ordinal);
        Assert.StartsWith("You have seen your fight this sitting", TrainerCopy.ShownThisSittingTooltip(Mine),
            StringComparison.Ordinal);
        Assert.Contains("doesn't match your recording", TrainerCopy.RefusalHeadline(Mine, "card"),
            StringComparison.Ordinal);
        Assert.StartsWith("Your run · ", RecordingIdentity.Subtitle(Fixtures.NativeRecording()),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The two surfaces that put the credit beside the player's own column say which is
    /// which. "You" against "You" would be a panel of two identical headings.
    /// </summary>
    [Fact]
    public void TheResultPanelTellsThisAttemptFromTheRecordedOne()
    {
        Assert.Equal(TrainerCopy.YouColumn, "You");
        Assert.NotEqual(TrainerCopy.YouColumn, Mine.Label);
    }
}

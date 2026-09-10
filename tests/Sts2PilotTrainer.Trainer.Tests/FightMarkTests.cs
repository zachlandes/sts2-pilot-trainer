using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// The bookmark tag, state by state.
///
/// One state draws it - a recorder attached, a fight just ended, the run still on
/// that floor - and every other set of facts is nothing at all, because a control
/// that could not save anything would only be a control saying why. The derivation
/// reads exactly <see cref="FightMarkFacts"/>, so each case pins the surface, the
/// fight and the words together, the way <see cref="RecorderPresenceTests"/> does.
/// </summary>
public sealed class FightMarkTests
{
    [Fact]
    public void AFightJustEndedDrawsAPressableHollowTagOfferingTheBookmark()
    {
        var mark = FightMark.For(new FightMarkFacts(
            true, RunCaptureState.Recording, FightJustEnded: 3, MovedOn: false, Bookmarked: false));

        Assert.Equal(Presence.Drawn, mark.Control.Presence);
        Assert.True(mark.Control.Pressable);
        Assert.Equal(3, mark.Fight);
        Assert.False(mark.Bookmarked);
        Assert.Equal(RecorderCopy.BookmarkThisFight, mark.Control.TooltipTitle);
        Assert.Equal(string.Empty, mark.Control.TooltipBody);
    }

    [Fact]
    public void ABookmarkedFightDrawsTheTagFilledAndOffersTheUndo()
    {
        var mark = FightMark.For(new FightMarkFacts(
            true, RunCaptureState.Recording, FightJustEnded: 3, MovedOn: false, Bookmarked: true));

        Assert.Equal(Presence.Drawn, mark.Control.Presence);
        Assert.True(mark.Control.Pressable);
        Assert.True(mark.Bookmarked);
        Assert.Equal(RecorderCopy.Bookmarked, mark.Control.TooltipTitle);
        Assert.Equal(RecorderCopy.PressToRemoveBookmark, mark.Control.TooltipBody);
    }

    /// <summary>
    /// A lost fight is bookmarked on the game's death screen, which is drawn after the
    /// run ended and the manifest was written. So a capture that has finished still
    /// offers the tag, and the press is the rewrite that path exists for.
    /// </summary>
    [Fact]
    public void AFinishedCaptureStillOffersTheTagOnTheDeathScreen()
    {
        var mark = FightMark.For(
            new FightMarkFacts(true, RunCaptureState.Finished, FightJustEnded: 3, MovedOn: false, Bookmarked: false));

        Assert.Equal(Presence.Drawn, mark.Control.Presence);
        Assert.True(mark.Control.Pressable);
        Assert.Equal(3, mark.Fight);
    }

    /// <summary>
    /// A recording that stopped draws no tag. The overlay row has just said RECORDING
    /// STOPPED, and a control offering to save the fight into that recording would say
    /// the opposite of it.
    /// </summary>
    [Theory]
    [InlineData(RunCaptureState.Broken)]
    [InlineData(RunCaptureState.Unmapped)]
    public void ARecordingThatStoppedOffersNoTag(RunCaptureState state)
    {
        var mark = FightMark.For(
            new FightMarkFacts(true, state, FightJustEnded: 3, MovedOn: false, Bookmarked: false));

        Assert.Same(FightMark.Nothing, mark);
    }

    [Theory]
    [InlineData(false, 3, false, false)]
    [InlineData(true, null, false, false)]
    [InlineData(true, 3, true, false)]
    [InlineData(true, 3, true, true)]
    [InlineData(false, null, false, false)]
    public void EverythingElseIsNothingAtAll(bool recorderActive, int? fightJustEnded, bool movedOn, bool bookmarked)
    {
        var mark = FightMark.For(new FightMarkFacts(
            recorderActive,
            recorderActive ? RunCaptureState.Recording : null,
            fightJustEnded,
            movedOn,
            bookmarked));

        Assert.Same(FightMark.Nothing, mark);
        Assert.Equal(Presence.Absent, mark.Control.Presence);
        Assert.False(mark.Control.Pressable);
        Assert.Null(mark.Fight);
    }
}

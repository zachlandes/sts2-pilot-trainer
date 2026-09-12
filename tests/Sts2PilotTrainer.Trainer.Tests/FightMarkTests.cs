using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// The bookmark tag, state by state.
///
/// One state draws it - a recorder attached, a fight just ended, the run still on
/// that floor and not on its way off it - and every other set of facts is nothing at
/// all, because a control that could not save anything would only be a control saying
/// why. The derivation
/// reads exactly <see cref="FightMarkFacts"/>, so each case pins the surface, the
/// fight and the words together, the way <see cref="RecorderPresenceTests"/> does.
/// </summary>
public sealed class FightMarkTests
{
    [Fact]
    public void AFightJustEndedDrawsAPressableHollowTagOfferingTheBookmark()
    {
        var mark = FightMark.For(new FightMarkFacts(
            true, RunCaptureState.Recording, FightJustEnded: 3, MovedOn: false, LeavingTheFloor: false, Bookmarked: false));

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
            true, RunCaptureState.Recording, FightJustEnded: 3, MovedOn: false, LeavingTheFloor: false, Bookmarked: true));

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
            new FightMarkFacts(true, RunCaptureState.Finished, FightJustEnded: 3, MovedOn: false, LeavingTheFloor: false, Bookmarked: false));

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
            new FightMarkFacts(true, state, FightJustEnded: 3, MovedOn: false, LeavingTheFloor: false, Bookmarked: false));

        Assert.Same(FightMark.Nothing, mark);
    }

    /// <summary>
    /// A map move pressed and not yet recorded is the run leaving the floor, and the
    /// tag goes with it. This is the state the next fight's opening frames are in: the
    /// recording still holds the previous fight as the last one ended and no floor
    /// entered since, because the move reaches it only once the engine has settled at
    /// the other end - so without this fact the previous fight's tag was drawn over
    /// the start of the next one.
    /// </summary>
    [Theory]
    [InlineData(RunCaptureState.Recording, false)]
    [InlineData(RunCaptureState.Recording, true)]
    [InlineData(RunCaptureState.Finished, false)]
    public void AMapMoveAnnouncedAndNotYetRecordedTakesTheTagDown(RunCaptureState capture, bool bookmarked)
    {
        var mark = FightMark.For(new FightMarkFacts(
            true, capture, FightJustEnded: 3, MovedOn: false, LeavingTheFloor: true, Bookmarked: bookmarked));

        Assert.Same(FightMark.Nothing, mark);
    }

    [Theory]
    [InlineData(false, 3, false, false, false)]
    [InlineData(true, null, false, false, false)]
    [InlineData(true, null, false, true, false)]
    [InlineData(true, 3, true, false, false)]
    [InlineData(true, 3, true, false, true)]
    [InlineData(true, 3, true, true, true)]
    [InlineData(false, null, false, false, false)]
    public void EverythingElseIsNothingAtAll(
        bool recorderActive, int? fightJustEnded, bool movedOn, bool leavingTheFloor, bool bookmarked)
    {
        var mark = FightMark.For(new FightMarkFacts(
            recorderActive,
            recorderActive ? RunCaptureState.Recording : null,
            fightJustEnded,
            movedOn,
            leavingTheFloor,
            bookmarked));

        Assert.Same(FightMark.Nothing, mark);
        Assert.Equal(Presence.Absent, mark.Control.Presence);
        Assert.False(mark.Control.Pressable);
        Assert.Null(mark.Fight);
    }
}

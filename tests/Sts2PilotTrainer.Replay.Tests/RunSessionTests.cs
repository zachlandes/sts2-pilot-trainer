namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// What kind of game permits what, asked of the rule rather than of the client.
///
/// The reading itself needs the game and lives in <c>LiveRun.ReadSession</c>; what is
/// decided from it does not, and is here so that "the MVP records singleplayer runs
/// only" and "a multiplayer game gets no surfaces at all" are two statements a machine
/// with no game installed can check.
/// </summary>
public sealed class RunSessionTests
{
    [Fact]
    public void OnlyASingleplayerRunIsRecorded()
    {
        Assert.True(RunSession.MayBeRecorded(RunSessionKind.Singleplayer));

        Assert.All(
            Enum.GetValues<RunSessionKind>().Where(kind => kind != RunSessionKind.Singleplayer),
            kind => Assert.False(RunSession.MayBeRecorded(kind), $"{kind} was recorded"));
    }

    /// <summary>
    /// A multiplayer game gets nothing at all, and neither does a reading that failed.
    ///
    /// Stronger than "records nothing" on purpose: an indicator saying a run is not
    /// being recorded is still this mod drawing in a game somebody else is also
    /// playing. The menu is the one state besides a singleplayer run where there is
    /// nothing to be wrong about yet, which is where the mod's own card lives.
    /// </summary>
    [Fact]
    public void NothingIsDrawnInAMultiplayerGameOrOnAReadingThatFailed()
    {
        Assert.True(RunSession.MaySpeakIn(RunSessionKind.Singleplayer));
        Assert.True(RunSession.MaySpeakIn(RunSessionKind.NoRunInProgress));

        Assert.False(RunSession.MaySpeakIn(RunSessionKind.NetworkedMultiplayer));
        Assert.False(RunSession.MaySpeakIn(RunSessionKind.LocalMultiplayer));
        Assert.False(RunSession.MaySpeakIn(RunSessionKind.Spectated));
        Assert.False(RunSession.MaySpeakIn(RunSessionKind.Unreadable));
    }

    /// <summary>
    /// Two players sharing one client is a multiplayer run here, whatever the game's
    /// own networking calls it.
    ///
    /// <c>RunManager.IsSingleplayerOrFakeMultiplayer</c> answers true for it, because
    /// what that property is about is whether anything has to be sent over a wire. What
    /// this is about is whose decisions the history holds, and a run two people played
    /// holds decisions this client's player did not make.
    /// </summary>
    [Fact]
    public void TwoPlayersOnOneClientIsAMultiplayerRun()
    {
        Assert.True(RunSession.IsMultiplayer(RunSessionKind.LocalMultiplayer));
        Assert.True(RunSession.IsMultiplayer(RunSessionKind.NetworkedMultiplayer));

        Assert.False(RunSession.IsMultiplayer(RunSessionKind.Singleplayer));
        Assert.False(RunSession.IsMultiplayer(RunSessionKind.NoRunInProgress));
    }

    /// <summary>Every kind explains itself, because the sentence is what a player reads
    /// in the game's log when a run of theirs is not recorded.</summary>
    [Fact]
    public void EveryKindSaysWhatItIs()
    {
        var described = Enum.GetValues<RunSessionKind>().Select(RunSession.Describe).ToList();

        Assert.All(described, sentence => Assert.False(string.IsNullOrWhiteSpace(sentence)));
        Assert.Equal(described.Count, described.Distinct(StringComparer.Ordinal).Count());
    }
}

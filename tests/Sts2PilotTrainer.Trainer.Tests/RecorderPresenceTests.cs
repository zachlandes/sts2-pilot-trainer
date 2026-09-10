using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// The recorder's row of the game's own version overlay, state by state.
///
/// Three rows the design names: recording, stopped, and no recorder at all. The
/// derivation reads exactly <see cref="RecorderFacts.RecorderActive"/> and
/// <see cref="RecorderFacts.Capture"/>, so each case below pins the row, the exact
/// approved text and the tone together, the way <see cref="PlaybackTransportTests"/>
/// pins a whole transport rather than one field at a time.
/// </summary>
public sealed class RecorderPresenceTests
{
    [Fact]
    public void ARecordingInProgressReadsRecordingInTheOverlaysOwnColour()
    {
        var presence = RecorderPresence.For(new RecorderFacts(true, RunCaptureState.Recording, Continuous: true));

        Assert.Equal(Presence.Drawn, presence.Row.Presence);
        Assert.False(presence.Row.Pressable);
        Assert.Equal(RecorderCopy.Recording, presence.Text);
        Assert.Equal(RecorderRowTone.Overlay, presence.Tone);
    }

    [Fact]
    public void ABrokenWatchReadsRecordingStoppedInTheWarningHue()
    {
        var presence = RecorderPresence.For(new RecorderFacts(true, RunCaptureState.Broken, Continuous: true));

        Assert.Equal(Presence.Drawn, presence.Row.Presence);
        Assert.False(presence.Row.Pressable);
        Assert.Equal(RecorderCopy.RecordingStopped, presence.Text);
        Assert.Equal(RecorderRowTone.Warning, presence.Tone);
    }

    [Fact]
    public void NoRecorderAttachedDrawsNoRowAtAll()
    {
        var presence = RecorderPresence.For(new RecorderFacts(false, null, Continuous: true));

        Assert.Equal(Presence.Absent, presence.Row.Presence);
        Assert.Equal(string.Empty, presence.Text);
    }

    /// <summary>Off, a trainer run and a refused module all reach the row the same
    /// way: recorder inactive. The row does not distinguish among them, because none
    /// of that is a fact <see cref="RecorderFacts"/> carries.</summary>
    [Fact]
    public void ARecorderReportedInactiveDrawsNothingRegardlessOfWhyItIsInactive()
    {
        Assert.Equal(RecorderPresence.Nothing, RecorderPresence.For(new RecorderFacts(false, RunCaptureState.Recording, Continuous: true)));
        Assert.Equal(RecorderPresence.Nothing, RecorderPresence.For(new RecorderFacts(false, RunCaptureState.Broken, Continuous: true)));
    }

    /// <summary>
    /// A reload that rewound the run behind what was recorded.
    ///
    /// The recorder goes on watching, so the capture is still Recording, and the
    /// recording can never be shared, so its continuity is gone. The row states the
    /// second: a player told only RECORDING would learn nothing changed, and one told
    /// RECORDING STOPPED would be told the recorder gave up, which it did not.
    /// </summary>
    [Fact]
    public void ARunStillRecordedAfterAReloadReadsNotShareableInTheWarningHue()
    {
        var presence = RecorderPresence.For(
            new RecorderFacts(true, RunCaptureState.Recording, Continuous: false));

        Assert.Equal(Presence.Drawn, presence.Row.Presence);
        Assert.False(presence.Row.Pressable);
        Assert.Equal(RecorderCopy.RecordingNotShareable, presence.Text);
        Assert.Equal(RecorderRowTone.Warning, presence.Tone);
    }

    /// <summary>A finished capture is a run that is over; nothing is being recorded
    /// any more, so the row says nothing rather than a stale RECORDING.</summary>
    [Fact]
    public void AFinishedCaptureDrawsNothing()
    {
        Assert.Equal(RecorderPresence.Nothing, RecorderPresence.For(new RecorderFacts(true, RunCaptureState.Finished, Continuous: true)));
    }

    /// <summary>An active recorder with no capture state is a contradiction in the
    /// facts rather than a state the design names, and the derivation draws nothing
    /// rather than guessing which half is right.</summary>
    [Fact]
    public void AnActiveRecorderWithNoCaptureStateDrawsNothing()
    {
        Assert.Equal(RecorderPresence.Nothing, RecorderPresence.For(new RecorderFacts(true, null, Continuous: true)));
    }
}

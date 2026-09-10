using Godot;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What a player looks at while a run is being restored, assembled in a process with
/// no game.
///
/// One drawing and one sentence. The surface is this mod's own plate, so the whole of
/// what a player reads during the wait can be put up here and asked what it says: the
/// scrim, the line, and that taking it away leaves nothing behind. The client's own
/// loading overlay was borrowed here for a while; it ships hidden, borrowing it drew a
/// present-but-invisible surface over the whole wait, and no test process could execute
/// that half at all.
/// </summary>
public sealed class RestoringOverlayTests : IDisposable
{
    private readonly Node _parent;

    /// <summary>The surface reads this build's native heading role, so putting it up
    /// at all needs the prepared assembly the engine's startup points at.</summary>
    public RestoringOverlayTests()
    {
        _ = EngineHost.StartupPhase();
        _parent = new Node { Name = "Game" };
    }

    public void Dispose()
    {
        RestoringOverlay.Remove();
        _parent.Free();

        // The startup above is process-wide, and a suite that shares one process gets
        // the answer this test left behind. The same reset every other test that
        // reaches the engine does.
        HeadlessEngine.Forget();
    }

    [GameFact]
    public void TheWaitIsSaidOnScreenRatherThanDrawingNothing()
    {
        var notice = RestoringNotice.For(JourneyPhase.Preparing)!;

        RestoringOverlay.ShowUnder(_parent, notice);

        var plate = _parent.GetNodeOrNull<Control>(RestoringOverlay.RootName);
        Assert.NotNull(plate);
        Assert.True(plate.IsVisibleInTree());

        Assert.NotNull(plate.GetNodeOrNull<ColorRect>("Scrim"));

        var headline = plate.GetNodeOrNull<Label>("Headline");
        Assert.NotNull(headline);

        // The notice's own line rather than anything this surface composes.
        Assert.Equal(notice.Line(0), headline.Text);
        Assert.Equal("Restoring your run", headline.Text);
    }

    /// <summary>Taking it away leaves nothing behind: the wait is over and the run's own
    /// interface is what a player looks at next.</summary>
    [GameFact]
    public void RemovingItTakesTheSurfaceOffScreen()
    {
        RestoringOverlay.ShowUnder(_parent, RestoringNotice.For(JourneyPhase.Preparing)!);
        RestoringOverlay.Remove();

        Assert.Null(_parent.GetNodeOrNull<Control>(RestoringOverlay.RootName));
    }
}

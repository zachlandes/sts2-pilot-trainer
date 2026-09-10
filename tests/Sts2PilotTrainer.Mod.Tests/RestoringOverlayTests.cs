using Godot;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What a player looks at while a run is being restored, assembled in a process with
/// no game.
///
/// The borrow cannot succeed here - there is no package to load the client's own
/// loading overlay out of - which is exactly the case the plate exists for, so this
/// drives the rule the retail client got wrong: the surface must be present, on
/// screen and saying the sentence, and never absent or invisible. The scene the mod
/// prefers ships with <c>visible = false</c> on its root, and borrowing it without
/// switching it on put a present-but-invisible overlay over the whole wait.
/// </summary>
public sealed class RestoringOverlayTests : IDisposable
{
    private readonly Node _parent;

    /// <summary>The surface holds a label of the game's own type, so resolving this
    /// class at all needs the prepared assembly the engine's startup points at.</summary>
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
    public void AWaitWithNoBorrowedSceneStillSaysTheSentenceOnScreen()
    {
        var notice = RestoringNotice.For(JourneyPhase.Preparing)!;

        RestoringOverlay.ShowUnder(_parent, notice);

        var plate = _parent.GetNodeOrNull<Control>(RestoringOverlay.RootName);
        Assert.NotNull(plate);
        Assert.True(plate.Visible);

        var headline = plate.GetNodeOrNull<Label>("Headline");
        Assert.NotNull(headline);
        Assert.True(headline.Visible);

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

using Godot;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What a player looks at while a run is being restored, assembled in a process with
/// no game.
///
/// Two rules are driven here. The client's own loading overlay cannot be loaded in
/// this process - there is no package to load it out of - which is the case the plate
/// exists for, so <see cref="RestoringOverlay.ShowUnder"/> has to draw the plate and
/// say the sentence. And the borrowed root is switched on and read for whether it is
/// actually on screen, which is the rule the retail client got wrong: that scene
/// ships with <c>visible = false</c> on its root, so borrowing it without switching
/// it on put a present-but-invisible surface over the whole wait and never fell
/// through to the plate. That half is driven over a root built here, because the
/// decision is about a <c>Control</c> rather than about that one scene.
///
/// What is not driven here is the refusal that follows it. Whether a control is on
/// screen is a question about a live scene tree, and outside one this process answers
/// it the same way however the control is parented, so a root that cannot be shown
/// cannot be built here at all.
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
        Assert.True(plate.IsVisibleInTree());

        var headline = plate.GetNodeOrNull<Label>("Headline");
        Assert.NotNull(headline);

        // The notice's own line rather than anything this surface composes.
        Assert.Equal(notice.Line(0), headline.Text);
        Assert.Equal("Restoring your run", headline.Text);
    }

    /// <summary>
    /// A borrowed root that ships hidden is switched on, the way the game's own callers
    /// switch it, and is then the surface a player reads.
    /// </summary>
    [GameFact]
    public void ABorrowedRootThatShipsHiddenIsSwitchedOn()
    {
        var borrowed = new Control { Name = "LoadingOverlay", Visible = false };
        _parent.AddChild(borrowed);

        Assert.True(RestoringOverlay.SwitchOnTheBorrowedRoot(borrowed));
        Assert.True(borrowed.IsVisibleInTree());
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

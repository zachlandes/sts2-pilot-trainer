using Godot;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The recorder's row applied to a stock Godot label, in a process with no game.
///
/// <see cref="RecorderPresenceTests"/> in <c>Sts2PilotTrainer.Trainer.Tests</c> pins
/// what the derivation says; this pins what <see cref="RecorderPresenceRow.Apply"/>
/// does with it once it reaches an actual node - the overlay-hides-row rule, the
/// shell's own draw gate, and the colour override that is the warning hue for a
/// broken watch and no override at all for the overlay's own row.
/// </summary>
public sealed class RecorderPresenceRowTests
{
    private const string FontColorEntry = "font_color";

    [Fact]
    public void ARecordingRowIsVisibleInTheOverlaysOwnColour()
    {
        var moddedRow = new Label { Visible = true };
        var row = new Label();

        RecorderPresenceRow.Apply(
            row, moddedRow, RecorderPresence.For(new RecorderFacts(true, RunCaptureState.Recording)), mayDraw: true);

        Assert.True(row.Visible);
        Assert.Equal(RecorderCopy.Recording, row.Text);
        Assert.Null(row.ThemeColorOverride(FontColorEntry));
    }

    [Fact]
    public void ABrokenWatchIsVisibleInTheWarningHue()
    {
        var moddedRow = new Label { Visible = true };
        var row = new Label();

        RecorderPresenceRow.Apply(
            row, moddedRow, RecorderPresence.For(new RecorderFacts(true, RunCaptureState.Broken)), mayDraw: true);

        Assert.True(row.Visible);
        Assert.Equal(RecorderCopy.RecordingStopped, row.Text);
        Assert.Equal(new Color(ScreenMarkup.NotMetColor), row.ThemeColorOverride(FontColorEntry));
    }

    /// <summary>A broken watch that recovers back to the overlay's own colour is the
    /// path the derivation does not name today, but the row still has to drop the
    /// override rather than leave the warning hue stuck on screen.</summary>
    [Fact]
    public void TheWarningColourIsDroppedOnceTheRowGoesBackToTheOverlaysOwnColour()
    {
        var moddedRow = new Label { Visible = true };
        var row = new Label();
        RecorderPresenceRow.Apply(
            row, moddedRow, RecorderPresence.For(new RecorderFacts(true, RunCaptureState.Broken)), mayDraw: true);

        RecorderPresenceRow.Apply(
            row, moddedRow, RecorderPresence.For(new RecorderFacts(true, RunCaptureState.Recording)), mayDraw: true);

        Assert.Null(row.ThemeColorOverride(FontColorEntry));
    }

    /// <summary>Hiding the game's own overlay hides this with it, even while a row is
    /// otherwise being drawn.</summary>
    [Fact]
    public void HidingTheModdedRowHidesTheRecordingRowWithIt()
    {
        var moddedRow = new Label { Visible = false };
        var row = new Label();

        RecorderPresenceRow.Apply(
            row, moddedRow, RecorderPresence.For(new RecorderFacts(true, RunCaptureState.Recording)), mayDraw: true);

        Assert.False(row.Visible);
    }

    /// <summary>The shell's draw gate wins over a row the derivation would otherwise
    /// show, the way it wins over every other module's surface.</summary>
    [Fact]
    public void TheShellsDrawGateHidesTheRowEvenWhileRecording()
    {
        var moddedRow = new Label { Visible = true };
        var row = new Label();

        RecorderPresenceRow.Apply(
            row, moddedRow, RecorderPresence.For(new RecorderFacts(true, RunCaptureState.Recording)), mayDraw: false);

        Assert.False(row.Visible);
    }

    [Fact]
    public void NoRecorderAttachedLeavesTheRowHidden()
    {
        var moddedRow = new Label { Visible = true };
        var row = new Label();

        RecorderPresenceRow.Apply(row, moddedRow, RecorderPresence.For(new RecorderFacts(false, null)), mayDraw: true);

        Assert.False(row.Visible);
        Assert.Equal(string.Empty, row.Text);
    }
}

using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// A run the console was used in is kept and nothing more: neither played from nor
/// shared, and every surface that offers either says so before the press.
///
/// The recording is the committed native one with what the recorder writes for an
/// accepted console command put on it - <c>integrity = non-standard</c> and nothing
/// else - because that is exactly what <c>RunCapture.MarkNonStandard</c> changes.
/// <c>RewoundRunPlaybackTests</c> is the sibling for the run that is played from and
/// never shared; this holds the other valve, because the first build of it went green
/// with a run-history plate that offered play-from and an entry that refused the press
/// with the validator's publication sentence, and a Mine pane that offered Submit
/// and refused it after the form was filled in.
/// </summary>
public sealed class KeptOnlyRunPlaybackTests
{
    private const string Recording = "native-3LACFJ5NJ371-20260906-015901.replay.json";

    [Fact]
    public void AConsoleRunIsRefusedByTheValidatorAndOfferedNowhere()
    {
        var committed = Committed();
        var console = Console(committed);

        Assert.False(ManifestValidator.Validate(console).IsValid);
        Assert.True(console.Source.Native!.KeptOnly);
        Assert.False(committed.Source.Native!.KeptOnly);

        // The run view: no place to stand, every row refused in the one sentence.
        Assert.DoesNotContain(RunView.PositionsIn(console), position => position.Playable);
        var view = RunView.For(console, RunProgress.Empty);
        Assert.All(view.Rows, row => Assert.False(row.Enabled));
        Assert.All(view.Rows, row => Assert.Equal(LibraryCopy.KeptOnly, row.Reason));

        // The Mine pane: Submit refused up front, in the same sentence.
        var run = LibraryRun.From(console, RunOrigin.Mine, RunVerdict.Passed);
        var pane = RunBrowser.For(
            LibraryTab.MyRuns, [run], console.Environment.BuildVersion.Value,
            selectedEntryId: run.EntryId, submitAvailable: true).Pane!;
        Assert.False(pane.Plate.Single(row => row.Kind == PaneRowKind.Submit).Enabled);
        Assert.Equal(LibraryCopy.KeptOnly, pane.PlateReason);
        Assert.All(pane.Strip, cell => Assert.False(cell.Playable));

        // The share form's seal agrees.
        Assert.Equal("Recording is not eligible to share", ShareRunForm.For(console).IntegritySeal);

        // And the committed recording is offered, so it is the mark that refuses.
        Assert.Contains(RunView.PositionsIn(committed), position => position.Playable);
    }

    [GameFact]
    public void AConsoleRunIsRefusedAtTheEntryWithTheSentenceTheSurfacesRefuseOn()
    {
        var directory = Path.Combine(
            Arbiter.RepoRoot, "build", "test-scratch", $"kept-only-run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var console = Console(Committed());
            var path = Path.Combine(directory, "console.replay.json");
            ManifestJson.Save(console, path);

            var entered = Arbiter.Run("enter-fight", path, "--fight", "1", "--out", Path.Combine(directory, "enter"));
            Assert.False(entered.Verified, entered.All);
            Assert.Contains("integrity is 'non-standard'", entered.All, StringComparison.Ordinal);
            Assert.DoesNotContain("ENTERED", entered.Output, StringComparison.Ordinal);

            var gate = Arbiter.Run("gate", path, "--out", Path.Combine(directory, "evidence"));
            Assert.NotEqual(0, gate.ExitCode);
            Assert.Contains("FAIL  provenance", gate.Output, StringComparison.Ordinal);
            Assert.Contains("NOT PUBLISHABLE", gate.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ReplayManifest Committed() =>
        ManifestJson.Load(Path.Combine(Arbiter.RepoRoot, "manifests", Recording));

    /// <summary>The committed run with an accepted console command's mark on it and
    /// nothing else changed - which is all <c>RunCapture.MarkNonStandard</c> changes.</summary>
    private static ReplayManifest Console(ReplayManifest committed) =>
        committed with
        {
            Source = committed.Source with
            {
                Native = committed.Source.Native! with { Integrity = NativeSource.NonStandardIntegrity },
            },
        };
}

using System.Text.Json;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The player's-fight loop against the real engine, with the recording standing in
/// for the player.
///
/// Two things are pinned. The recorded fight this repository ships is the engine's
/// own replay of the shipped manifest, re-derived in a fresh process to be trusted at
/// all. And the same actions, played through the capture the in-game host observes a
/// player with, project to a line the comparison reports as identical to that replay
/// on every field - so a line that came through the capture and differed would be a
/// defect in the capture rather than a difference in the fight.
/// </summary>
public class PlayerFightTests
{
    private static string RecordedFightPath =>
        Path.Combine(Arbiter.RepoRoot, "manifests", "navegreed-OJ-6QXhNgdg.recorded-fights.json");

    [Fact]
    public void TheShippedRecordedFightBindsToTheShippedManifest()
    {
        var fights = RecordedFights.Load(RecordedFightPath);
        fights.Bind(ManifestJson.Load(Arbiter.Manifest));

        var projection = fights.Projection();
        Assert.Equal("victory", projection.Summary.Outcome);
        Assert.Equal(4, projection.Summary.TotalTurns);
        Assert.Equal(64, projection.Summary.StartingHealth);
        Assert.Equal(57, projection.Summary.FinalHealth);
    }

    [GameFact]
    public void TheShippedRecordedFightIsTheEnginesOwnReplayOfTheManifest()
    {
        var outDir = TempDir();
        var outPath = Path.Combine(outDir, "regenerated.recorded-fights.json");
        var result = Arbiter.Run("recorded-fight", Arbiter.Manifest, "--out", outPath);
        Assert.True(result.Verified, result.All);

        var regenerated = RecordedFights.Load(outPath);
        var shipped = RecordedFights.Load(RecordedFightPath);
        Assert.Equal(shipped.Serialize(), regenerated.Serialize());
    }

    [GameFact]
    public void PlayingTheRecordingThroughTheCaptureComparesIdenticalToItsReplay()
    {
        var outDir = TempDir();
        var result = Arbiter.Run("enter-fight", Arbiter.Manifest, "--play", "--out", outDir);
        Assert.True(result.Verified, result.All);
        Assert.Contains("[Your fight and NaveGreed's]", result.Output, StringComparison.Ordinal);

        // The panel as a terminal draws it: what each side played on a turn, then the
        // chart's own numbers for the same turn on both lines.
        Assert.Contains("Hellraiser, Defend Ironclad         Hellraiser, Defend Ironclad", result.Output,
            StringComparison.Ordinal);
        Assert.Contains("Health lost each turn", result.Output, StringComparison.Ordinal);
        Assert.Contains("Enemy health lost", result.Output, StringComparison.Ordinal);
        Assert.Contains("     1   8            8            4            4", result.Output,
            StringComparison.Ordinal);

        var played = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "enter-fight.json")))
            .RootElement.GetProperty("played");
        Assert.Equal("Completed", played.GetProperty("capture_state").GetString());

        var comparison = played.GetProperty("comparison");
        Assert.Equal("player", comparison.GetProperty("left").GetProperty("source_id").GetString());
        Assert.Equal("navegreed-OJ-6QXhNgdg", comparison.GetProperty("right").GetProperty("source_id").GetString());
        Assert.All(
            comparison.GetProperty("summary").EnumerateArray(),
            field => Assert.True(field.GetProperty("matches").GetBoolean(), field.ToString()));
        Assert.Equal(4, comparison.GetProperty("turns").GetArrayLength());
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", $"player-fight-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}

using System.Text.Json;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The whole-combat comparison, against the real engine.
///
/// The unit is a fight that finished. Until this milestone no fixture had ever
/// carried one past its opening turn, and the canonical state could not have said so
/// if one had: the player's combat state outlives the fight, so a won combat still
/// read as in progress. Both halves are checked here.
///
/// Two engine-produced lines stand in for a person's fight against the VOD's. That
/// substitution is the honest limit of what can be shown without a mod host, and the
/// comparison says so in its own caveats rather than leaving it to be inferred.
/// </summary>
public class CombatComparisonTests
{
    [GameFact]
    public void TheGeneratedFixtureCarriesItsFightToAVictoryTheCanonicalStateCanSee()
    {
        var manifest = ManifestJson.Load(Arbiter.SyntheticReplayFixture());
        var completion = manifest.Checkpoints.Single(checkpoint => checkpoint.Id == "combat-complete");

        Assert.Equal("victory", completion.Expect["combat.outcome"].Value);
        Assert.Equal("false", completion.Expect["combat.in_progress"].Value);
        Assert.Equal("0", completion.Expect["combat.enemy_count"].Value);

        // Pinned by replaying it, not by reading the file: the checkpoint is only
        // evidence because the engine reproduces it.
        var result = Arbiter.Run("replay", Arbiter.SyntheticReplayFixture());
        Assert.True(result.Verified, result.All);
        Assert.DoesNotContain("FAIL", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void ComparesTwoCompletedLinesOfTheSameFight()
    {
        var outDir = TempDir();
        var reference = Fixture(outDir, "reference");
        var alternate = Fixture(outDir, "alternate");

        var result = Arbiter.Run("combat-compare", reference, alternate, "--out", outDir);
        Assert.True(result.Verified, result.All);

        var comparison = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(outDir, "combat-comparison.json")))
            .RootElement.GetProperty("comparison");

        // Both lines won the same fight, and they did it differently. A comparison in
        // which nothing differs would be passing without exercising anything.
        foreach (var side in new[] { "left", "right" })
        {
            Assert.Equal("victory", comparison.GetProperty(side).GetProperty("summary")
                .GetProperty("outcome").GetString());
        }

        var summary = comparison.GetProperty("summary").EnumerateArray().ToList();
        Assert.False(summary.Single(f => f.GetProperty("field").GetString() == "total_turns")
            .GetProperty("matches").GetBoolean());
        Assert.False(summary.Single(f => f.GetProperty("field").GetString() == "health_lost")
            .GetProperty("matches").GetBoolean());
        Assert.True(summary.Single(f => f.GetProperty("field").GetString() == "starting_health")
            .GetProperty("matches").GetBoolean());

        // The turn detail carries the chronology the summary does not, including a turn
        // one line reached and the other never did.
        var turns = comparison.GetProperty("turns").EnumerateArray().ToList();
        Assert.True(turns.Count > 1);
        Assert.Contains(turns, turn =>
            turn.TryGetProperty("left", out _) != turn.TryGetProperty("right", out _));

        // Differences only. Nothing here says which line was better.
        foreach (var forbidden in new[] { "\"score\"", "\"better\"", "\"rank\"", "\"winner\"" })
        {
            Assert.DoesNotContain(forbidden, comparison.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [GameFact]
    public void TheSummarysHealthOutcomeAndTheTurnDetailsLossesAreDifferentMeasurements()
    {
        // Ironclad's starting relic heals six the moment the last enemy dies, so the
        // fight's health outcome is smaller than the health that came off during its
        // turns. The two projections measure different things and are not required to
        // agree; this pins that as a fact about the engine rather than a note in a
        // comment nobody re-checks.
        var outDir = TempDir();
        var result = Arbiter.Run(
            "combat-compare", Fixture(outDir, "reference"), Fixture(outDir, "alternate"), "--out", outDir);
        Assert.True(result.Verified, result.All);

        var left = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "combat-comparison.json")))
            .RootElement.GetProperty("comparison").GetProperty("left");

        var healthLost = left.GetProperty("summary").GetProperty("health_lost").GetInt32();
        var lostInTurns = left.GetProperty("turns").EnumerateArray()
            .Sum(turn => turn.GetProperty("health_lost").GetInt32());

        Assert.True(healthLost > 0, "the reference line takes damage");
        Assert.Equal(healthLost + 6, lostInTurns);
    }

    [GameFact]
    public void RefusesToProjectAHistoryWhoseFightNeverFinishes()
    {
        // The shipped VOD reconstruction covers the opening turn and leaves the fight
        // running. Refusing it, with a message that says why, is the right answer -
        // and it is what the manifest needs before it can be one side of a comparison.
        var outDir = TempDir();

        var result = Arbiter.Run(
            "combat-compare", Arbiter.Manifest, Fixture(outDir, "reference"), "--out", outDir);

        Assert.False(result.Verified);
        Assert.Contains("still in progress", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void TheBoundaryCheckDoesNotRefuseTheSameFightUnderAnotherName()
    {
        // The refusal itself is proved without the game, over hand-written traces. What
        // needs the engine is the other half: a check that refuses two different fights
        // is only useful if it keys on the fight rather than on the manifest's name or
        // its checkpoint list, and this is the same history wearing both.
        var outDir = TempDir();
        var reference = Fixture(outDir, "reference");

        var elsewhere = Path.Combine(outDir, "elsewhere.replay.json");
        var manifest = ManifestJson.Load(reference);
        var boundary = manifest.Checkpoints.Single(checkpoint => checkpoint.Id == "combat-start");
        ManifestJson.Save(
            manifest with
            {
                RunId = manifest.RunId + "-relabelled",
                Checkpoints = manifest.Checkpoints.Where(checkpoint => checkpoint != boundary).ToList(),
            },
            elsewhere);

        var result = Arbiter.Run("combat-compare", reference, elsewhere, "--out", outDir);
        Assert.True(result.Verified, result.All);
    }

    private static string Fixture(string outDir, string line)
    {
        var path = Path.Combine(outDir, $"{line}.replay.json");
        var result = Arbiter.Run("generate-synthetic-fixture", "--out", path, "--line", line);
        Assert.True(result.Verified, result.All);
        return path;
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}

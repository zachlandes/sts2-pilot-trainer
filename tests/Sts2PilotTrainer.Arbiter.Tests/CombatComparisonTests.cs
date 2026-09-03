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
        Assert.Equal(
            comparison.GetProperty("left").GetProperty("combat_start_snapshot_digest").GetString(),
            comparison.GetProperty("right").GetProperty("combat_start_snapshot_digest").GetString());

        var summary = comparison.GetProperty("summary").EnumerateArray().ToList();
        Assert.False(summary.Single(f => f.GetProperty("field").GetString() == "total_turns")
            .GetProperty("matches").GetBoolean());
        Assert.False(summary.Single(f => f.GetProperty("field").GetString() == "net_health_change")
            .GetProperty("matches").GetBoolean());
        Assert.True(summary.Single(f => f.GetProperty("field").GetString() == "starting_health")
            .GetProperty("matches").GetBoolean());

        // The turn detail carries the chronology the summary does not, including a turn
        // one line reached and the other never did.
        var turns = comparison.GetProperty("turns").EnumerateArray().ToList();
        Assert.True(turns.Count > 1);
        Assert.Contains(turns, turn =>
            turn.TryGetProperty("left", out _) != turn.TryGetProperty("right", out _));
        var firstLeft = turns[0].GetProperty("left");
        Assert.True(firstLeft.TryGetProperty("enemy_health_lost", out _));
        Assert.False(firstLeft.TryGetProperty("damage_dealt", out _));

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
        // net health change and the health that came off during turns are different.
        // This pins that as a fact about the engine rather than a note nobody re-checks.
        var outDir = TempDir();
        var result = Arbiter.Run(
            "combat-compare", Fixture(outDir, "reference"), Fixture(outDir, "alternate"), "--out", outDir);
        Assert.True(result.Verified, result.All);

        var left = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "combat-comparison.json")))
            .RootElement.GetProperty("comparison").GetProperty("left");

        var netHealthChange = left.GetProperty("summary").GetProperty("net_health_change").GetInt32();
        var lostInTurns = left.GetProperty("turns").EnumerateArray()
            .Sum(turn => turn.GetProperty("health_lost").GetInt32());

        Assert.True(netHealthChange < 0, "the reference line finishes below its starting health");
        Assert.Equal(-netHealthChange + 6, lostInTurns);
    }

    [GameFact]
    public void RefusesToProjectAHistoryWhoseFightNeverFinishes()
    {
        // The shipped VOD reconstruction used to stop after the opening turn and leave
        // its fight running, and this is that manifest cut back to where it stopped.
        // Refusing it, with a message that says why, is the right answer, and it is
        // what had to change before the recording could be one side of a comparison.
        var outDir = TempDir();

        var result = Arbiter.Run(
            "combat-compare", OpeningTurnOnly(outDir), Fixture(outDir, "reference"), "--out", outDir);

        Assert.False(result.Verified);
        Assert.Contains("still in progress", result.All, StringComparison.Ordinal);
    }

    /// <summary>
    /// The recording's own fight, projected. This is the milestone: the VOD's solution
    /// is a completed side rather than a history the contract has to refuse.
    ///
    /// Both sides are the same recorded line, because no second line of this fight
    /// exists - nobody has played it in a retail client through a mod host, and
    /// authoring an alternative would be inventing a decision no player made. What is
    /// under test is that the recording projects and compares at all, from its own
    /// combat-start boundary.
    /// </summary>
    [GameFact]
    public void ProjectsTheRecordedFightAsOneCompletedSide()
    {
        var outDir = TempDir();

        var result = Arbiter.Run(
            "combat-compare", Arbiter.Manifest, Arbiter.Manifest, "--out", outDir);
        Assert.True(result.Verified, result.All);

        var comparison = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(outDir, "combat-comparison.json")))
            .RootElement.GetProperty("comparison");
        var left = comparison.GetProperty("left");

        Assert.Equal("victory", left.GetProperty("summary").GetProperty("outcome").GetString());
        Assert.Equal(4, left.GetProperty("summary").GetProperty("total_turns").GetInt32());
        Assert.Equal(64, left.GetProperty("summary").GetProperty("starting_health").GetInt32());
        Assert.Equal(57, left.GetProperty("summary").GetProperty("final_health").GetInt32());
        Assert.Equal(
            "ENCOUNTER.SLUDGE_SPINNER_WEAK",
            left.GetProperty("boundary").GetProperty("combat.encounter").GetString());
        Assert.All(
            comparison.GetProperty("summary").EnumerateArray(),
            field => Assert.True(field.GetProperty("matches").GetBoolean()));

        // The caveat that keeps the output honest survives a VOD side: the host can
        // hand the fight to a person, but it does not capture that person's completed
        // line for comparison.
        Assert.Contains(
            "hands the player the recorded fight at the verified combat-start boundary", result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "no fight played by a person has been compared", result.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shipped manifest cut back to the end of the first turn: the history it
    /// carried before this milestone, kept as the negative control for a fight that
    /// does not finish. Derived from the manifest rather than stored beside it, so it
    /// cannot drift into being a control for something else.
    /// </summary>
    private static string OpeningTurnOnly(string outDir)
    {
        var manifest = ManifestJson.Load(Arbiter.Manifest);
        var throughFirstTurn = manifest.Actions
            .TakeWhile(action => action.Verb != ActionVerb.EndTurn)
            .Concat(manifest.Actions.Where(action => action.Verb == ActionVerb.EndTurn).Take(1))
            .ToList();
        var path = Path.Combine(outDir, "opening-turn-only.replay.json");
        ManifestJson.Save(
            manifest with
            {
                RunId = manifest.RunId + "+opening-turn-only",
                Actions = throughFirstTurn,
                Checkpoints = manifest.Checkpoints
                    .Where(checkpoint => checkpoint.AfterSeq <= throughFirstTurn[^1].Seq)
                    .ToList(),
            },
            path);
        return path;
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

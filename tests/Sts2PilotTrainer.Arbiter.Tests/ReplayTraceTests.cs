using System.Globalization;
using System.Text.Json;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// That the stored replay result is enough to answer the questions the product will
/// ask of it later, without replaying anything again.
///
/// These tests derive the quantities named in <c>docs/comparison-direction.md</c>
/// out of a real trace, by hand. Deriving them here is the proof that the trace kept
/// them, which is a different claim from the contract that reads them:
/// <c>CombatProjection</c> owns that, and it is exercised in
/// <c>CombatProjectionTests</c> and <c>CombatComparisonTests</c>. Keeping this
/// hand-derivation is deliberate - it would still fail if the trace stopped sampling
/// a field, even if the contract were rewritten around the gap. Nothing here ranks a
/// line or scores an outcome.
/// </summary>
public class ReplayTraceTests
{
    [GameFact]
    public void AVerifiedReplayKeepsAStepForEveryActionAndTheStateBeforeItRan()
    {
        var trace = Trace();

        // One sample before anything ran, then one per action.
        Assert.Equal(ManifestJson.Load(Arbiter.Manifest).Actions.Count + 1, trace.Steps.Count);
        Assert.Equal(-1, trace.Steps[0].Seq);
        Assert.Equal("run_start", trace.Steps[0].Verb);
        Assert.All(trace.Steps, step =>
        {
            Assert.NotEmpty(step.Before);
            Assert.NotEmpty(step.After);
        });
    }

    [GameFact]
    public void TheCombatSummaryProjectionIsDerivable()
    {
        // Total turns, the health outcome, and which consumables were used - with no
        // turn numbers, because the summary does not carry chronology.
        var steps = Trace().Steps;
        var combat = steps.Where(step => step.After.GetValueOrDefault("combat.in_progress") == "true").ToList();
        Assert.NotEmpty(combat);

        var totalTurns = combat.Max(step => Int(step.After, "combat.turn"));
        var startingHp = Int(combat[0].After, "combat.player_hp");
        var finalHp = Int(combat[^1].After, "combat.player_hp");
        var consumablesUsed = steps
            .SelectMany(step => Potions(step.Before).Except(Potions(step.After), StringComparer.Ordinal))
            .ToList();

        Assert.True(totalTurns >= 1, $"turns derived: {totalTurns}");
        Assert.Equal(64, startingHp);
        Assert.Equal(60, finalHp);
        Assert.Equal(4, startingHp - finalHp);
        // The reconstructed prefix uses no potion; deriving an empty list is the
        // point, since the alternative is being unable to tell.
        Assert.Empty(consumablesUsed);
    }

    [GameFact]
    public void TheTurnChronologyProjectionIsDerivable()
    {
        // The same events read the other way: per turn, damage dealt and received.
        var byTurn = new SortedDictionary<int, (int Dealt, int Received)>();
        foreach (var step in Trace().Steps.Where(s => s.Before.GetValueOrDefault("combat.in_progress") == "true"))
        {
            var turn = Int(step.Before, "combat.turn");
            var dealt = Int(step.Before, "combat.enemy.0.hp") - Int(step.After, "combat.enemy.0.hp");
            var received = Int(step.Before, "combat.player_hp") - Int(step.After, "combat.player_hp");
            var running = byTurn.TryGetValue(turn, out var existing) ? existing : (0, 0);
            byTurn[turn] = (running.Item1 + Math.Max(0, dealt), running.Item2 + Math.Max(0, received));
        }

        Assert.NotEmpty(byTurn);
        // Turn 1 of this fight, as the engine actually resolves it: both cards are
        // played without moving a hit point, and everything lands when the turn ends -
        // the enemy drops 42 to 34, and its 9-damage attack arrives as 4 through the
        // 5 block Defend put up. Attributing that to the turn it belongs to is exactly
        // the derivation a chronology projection has to make, which is why the trace
        // keeps the turn number on both sides of every step.
        Assert.Equal((8, 4), byTurn[1]);
    }

    [GameFact]
    public void PermanentCardRemovalIsRepresentable()
    {
        // Nothing in the reconstructed prefix removes a card, so what is proved here
        // is that a removal would be visible: the deck is sampled either side of every
        // action, and a set difference is all a later projection needs.
        var steps = Trace().Steps;
        Assert.All(steps, step =>
        {
            Assert.True(step.Before.ContainsKey("player.deck"));
            Assert.True(step.After.ContainsKey("player.deck"));
        });

        var removals = steps
            .SelectMany(step => Deck(step.Before).Except(Deck(step.After), StringComparer.Ordinal))
            .ToList();
        Assert.Empty(removals);
    }

    [GameFact]
    public void ARejectedReplayStillKeepsWhatHappenedBeforeItDiverged()
    {
        // The history that diverged is the one whose intermediate states are worth
        // reading, so the trace has to survive a rejection.
        var report = Report("rejected-trace", "replay", ContradictedManifest());

        Assert.Equal(VerificationStatus.Rejected, report.Status);
        Assert.NotNull(report.Trace);
        Assert.NotEmpty(report.Trace!.Steps);
    }

    /// <summary>
    /// A manifest whose last checkpoint claims a health the run does not reach, so the
    /// replay runs the whole history and is then contradicted. A rejection that
    /// happened at the end is the one with the most trace to keep.
    /// </summary>
    private static string ContradictedManifest()
    {
        var dir = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "contradicted.json");
        var manifest = ManifestJson.Load(Arbiter.Manifest);
        var last = manifest.Checkpoints.OrderBy(checkpoint => checkpoint.AfterSeq).Last();
        var field = last.Expect.Keys.First(key => key == "combat.player_hp");
        var rewritten = manifest.Checkpoints
            .Select(checkpoint => checkpoint != last
                ? checkpoint
                : checkpoint with
                {
                    Expect = checkpoint.Expect.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Key == field
                            ? pair.Value with { Value = "63" }
                            : pair.Value,
                        StringComparer.Ordinal),
                })
            .ToList();
        ManifestJson.Save(manifest with { Checkpoints = rewritten }, path);
        return path;
    }

    private static ReplayTrace Trace() => Report("verified-trace", "replay", Arbiter.Manifest).Trace
        ?? throw new InvalidOperationException("The replay report carried no trace.");

    private static VerificationReport Report(string name, params string[] args)
    {
        var dir = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", name);
        Directory.CreateDirectory(dir);
        var outPath = Path.Combine(dir, "report.json");
        Arbiter.Run([.. args, "--out", outPath]);
        return ManifestJson.Load(outPath).Verification
            ?? throw new InvalidOperationException($"{outPath} carried no verification report.");
    }

    private static int Int(IReadOnlyDictionary<string, string> sample, string field) =>
        sample.TryGetValue(field, out var value)
            ? int.Parse(value, CultureInfo.InvariantCulture)
            : throw new InvalidOperationException($"Trace sample has no '{field}'.");

    private static IEnumerable<string> Potions(IReadOnlyDictionary<string, string> sample) =>
        Sequence(sample, "player.potions").Where(entry => entry != "empty");

    private static IEnumerable<string> Deck(IReadOnlyDictionary<string, string> sample) =>
        Sequence(sample, "player.deck");

    private static IEnumerable<string> Sequence(IReadOnlyDictionary<string, string> sample, string field) =>
        sample.TryGetValue(field, out var value) && value.Length > 0
            ? value.Split('|')
            : [];
}

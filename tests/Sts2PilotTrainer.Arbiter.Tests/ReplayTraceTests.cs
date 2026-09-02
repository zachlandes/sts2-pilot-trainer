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
        var (entry, fight) = FirstFight();
        Assert.NotEmpty(fight);

        var outcome = fight[^1].After.GetValueOrDefault("combat.outcome");
        var totalTurns = fight.Max(step => Int(step.Before, "combat.turn"));
        var startingHp = Int(entry.After, "combat.player_hp");
        var finalHp = Int(fight[^1].After, "combat.player_hp");
        // Scoped to the fight, like every other quantity in the summary. The history
        // picks up a potion from this fight's loot, which is an addition to the belt
        // and not a use, and a summary counted over the whole run would be answering a
        // different question from the one it says it answers.
        var consumablesUsed = fight
            .SelectMany(step => Potions(step.Before).Except(Potions(step.After), StringComparer.Ordinal))
            .ToList();

        // The fight the recording shows: four turns, won on the fourth. Health ends
        // seven below where it started even though thirteen came off during the turns,
        // because the starting relic heals six as the last enemy dies. The turn detail
        // measures the one and the summary measures the other, and the trace has to
        // keep enough for both to be derived without replaying anything.
        Assert.Equal("victory", outcome);
        Assert.Equal(4, totalTurns);
        Assert.Equal(64, startingHp);
        Assert.Equal(57, finalHp);
        Assert.Equal(-7, finalHp - startingHp);
        // The reconstructed history uses no potion; deriving an empty list is the
        // point, since the alternative is being unable to tell.
        Assert.Empty(consumablesUsed);
    }

    [GameFact]
    public void TheTurnChronologyProjectionIsDerivable()
    {
        // The same events read the other way: per-turn enemy and player health lost.
        //
        // Bounded to the first fight, because turn numbers restart with every combat
        // and a chronology that summed them across fights would be adding turn 2 of one
        // to turn 2 of another. That bound is the projection's too - CombatProjection
        // reads one completed fight - and it is the reason the history's later fights
        // do not change the numbers below.
        var byTurn = new SortedDictionary<int, (int Dealt, int Received)>();
        foreach (var step in FirstFight().Steps)
        {
            var turn = Int(step.Before, "combat.turn");
            // The engine takes a dead enemy out of the combat state instead of leaving
            // it at zero health, so the killing step has no enemy afterwards to
            // subtract from. When they all go, each one's remaining health is what the
            // step dealt - which is the one case the sampled state still resolves
            // exactly, and the reason a fight that ends in a kill is derivable at all.
            var dealt = Int(step.After, "combat.enemy_count") == 0
                ? Enumerable.Range(0, Int(step.Before, "combat.enemy_count"))
                    .Sum(i => Int(step.Before, $"combat.enemy.{i}.hp"))
                : Int(step.Before, "combat.enemy.0.hp") - Int(step.After, "combat.enemy.0.hp");
            var received = Int(step.Before, "combat.player_hp") - Int(step.After, "combat.player_hp");
            var running = byTurn.TryGetValue(turn, out var existing) ? existing : (0, 0);
            byTurn[turn] = (running.Item1 + Math.Max(0, dealt), running.Item2 + Math.Max(0, received));
        }

        // The whole fight, turn by turn, as the engine actually resolves it. Turn 1 is
        // the one worth reading twice: both cards are played without moving a hit
        // point, and everything lands when the turn ends - the enemy drops 42 to 34,
        // and its 9-damage attack arrives as 4 through the 5 block Defend put up.
        // Attributing that to the turn it belongs to is exactly the derivation a
        // chronology projection has to make, which is why the trace keeps the turn
        // number on both sides of every step.
        Assert.Equal([1, 2, 3, 4], byTurn.Keys);
        Assert.Equal((8, 4), byTurn[1]);
        Assert.Equal((24, 2), byTurn[2]);
        Assert.Equal((6, 7), byTurn[3]);
        Assert.Equal((4, 0), byTurn[4]);
    }

    [GameFact]
    public void PermanentCardRemovalIsRepresentable()
    {
        // Nothing in the reconstructed prefix removes a card, so what is proved here
        // is that a removal would be visible: the deck is sampled either side of every
        // action, and a set difference is all a later projection needs.
        //
        // With one wrinkle the history now exercises. A card's canonical identity
        // carries its enchantment, so the event that enchants Bash and a Defend turns
        // 'CARD.BASH' into 'CARD.BASH@ENCHANTMENT.STEADY' - and a naive set difference
        // reads that as a removal and an addition. It is not one, and a removal
        // projection that treated it as one would report the wrong thing about the
        // rarest event it exists to describe. Comparing base identities is what
        // separates the two, and it is recorded here rather than left to be
        // rediscovered.
        var steps = Trace().Steps;
        Assert.All(steps, step =>
        {
            Assert.True(step.Before.ContainsKey("player.deck"));
            Assert.True(step.After.ContainsKey("player.deck"));
        });

        var removals = steps
            .SelectMany(step => BaseCards(step.Before).Except(BaseCards(step.After), StringComparer.Ordinal))
            .ToList();
        Assert.Empty(removals);

        // And the enchantment really is in the trace, or the paragraph above would be
        // describing a hazard this history does not contain.
        Assert.Contains(steps, step => Deck(step.After).Any(card => card.Contains('@')));
    }

    /// <summary>Card identities with any enchantment suffix removed.</summary>
    private static IEnumerable<string> BaseCards(IReadOnlyDictionary<string, string> sample) =>
        Deck(sample).Select(card => card.Split('@')[0]);

    /// <summary>
    /// The step that entered the history's first combat, and the steps of that fight
    /// up to the one that ended it. The same window <see cref="CombatProjection"/>
    /// reads, and the reason the fights this history goes on to do not change any
    /// number derived here.
    /// </summary>
    private static (ReplayStep Entry, IReadOnlyList<ReplayStep> Steps) FirstFight()
    {
        var steps = Trace().Steps;
        var entry = steps.Select((step, index) => (step, index))
            .Where(pair => pair.step.After.GetValueOrDefault("combat.in_progress") == "true")
            .Select(pair => pair.index)
            .DefaultIfEmpty(-1)
            .First();
        Assert.True(entry >= 0, "the history enters combat");
        return (
            steps[entry],
            steps.Skip(entry + 1)
                .TakeWhile(step => step.Before.GetValueOrDefault("combat.in_progress") == "true")
                .ToList());
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

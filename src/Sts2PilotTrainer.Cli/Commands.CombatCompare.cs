using System.Text.Json;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// Replays two manifests, projects each one's completed fight, and prints the
    /// differences.
    ///
    /// The two projections stay apart in the output exactly as they do in the
    /// contract: the combat summary first, then the turn detail. Reading the two
    /// blocks is meant to feel like reading two different answers, because they are.
    ///
    /// Both sides go through the real engine in fresh processes. A comparison of two
    /// stored results neither of which was reproduced would be arithmetic over
    /// somebody's notes, which is the standard this project exists not to accept.
    /// </summary>
    internal static int CombatCompare(string[] args)
    {
        var leftPath = Args.Positional(args, 0, "left manifest path");
        var rightPath = Args.Positional(args, 1, "right manifest path");
        var outDir = Args.Value(args, "--out") ?? "build/evidence";
        var artifact = EvidenceArtifact.Prepare(outDir, "combat-comparison.json");

        var left = Project(leftPath, outDir, "left");
        var right = Project(rightPath, outDir, "right");
        var comparison = CombatComparison.Between(left, right);

        Console.WriteLine($"left  : {left.SourceId}");
        Console.WriteLine($"right : {right.SourceId}");
        Console.WriteLine($"fight : {left.Boundary.GetValueOrDefault("combat.encounter", "unknown")}, " +
                          "same combat-start boundary on both sides");
        Console.WriteLine();

        Console.WriteLine("combat summary (no chronology - see the turn detail for when):");
        foreach (var field in comparison.Summary)
        {
            Console.WriteLine(
                $"  {(field.Matches ? "same" : "diff")} {field.Field,-17} " +
                $"left={Show(field.Left),-14} right={Show(field.Right)}");
        }

        Console.WriteLine();
        Console.WriteLine("turn detail:");
        foreach (var turn in comparison.Turns)
        {
            Console.WriteLine($"  turn {turn.Turn}");
            Console.WriteLine($"    left  {Describe(turn.Left)}");
            Console.WriteLine($"    right {Describe(turn.Right)}");
        }

        Console.WriteLine();
        foreach (var caveat in comparison.Caveats) Console.WriteLine($"  note: {caveat}");

        artifact.WriteAtomic(
            JsonSerializer.Serialize(new
            {
                schema = "sts2-pilot-trainer/combat-comparison/v1",
                left_manifest = Path.GetFileName(leftPath),
                right_manifest = Path.GetFileName(rightPath),
                comparison,
            }, Json.Indented) + "\n");

        Console.WriteLine();
        Console.WriteLine($"report: {Paths.Display(artifact.Path)}");
        return 0;
    }

    /// <summary>
    /// Replays one manifest in a fresh process and projects its completed fight.
    ///
    /// The refusals the projection makes are the interesting output when they happen -
    /// a history whose fight never finishes is a real answer - so they are reported
    /// against the manifest that caused them rather than as a bare failure.
    /// </summary>
    private static CombatProjection Project(string manifestPath, string outDir, string side)
    {
        var verifiedPath = Path.Combine(outDir, $"combat-comparison.{side}.verified.json");
        var replay = SelfProcess.Run("replay", manifestPath, "--out", verifiedPath);
        if (replay.ExitCode != 0)
        {
            Console.Write(replay.StandardOutput);
            Console.Error.Write(replay.StandardError);
            throw new ManifestException(
                $"{Path.GetFileName(manifestPath)} does not replay cleanly, so it has no verified fight to " +
                "compare. A comparison against an unverified replay would be a comparison against a guess.");
        }

        var manifest = ManifestJson.Load(verifiedPath);
        var trace = manifest.Verification?.Trace
            ?? throw new ManifestException(
                $"The replay of {Path.GetFileName(manifestPath)} wrote no trace, so its fight cannot be projected.");
        var combatStart = CombatStartSeq(trace)
            ?? throw new ManifestException(
                $"The replay of {Path.GetFileName(manifestPath)} never entered combat, so it has no " +
                "combat-start snapshot to compare.");
        var snapshot = ReplayPrefix(
            manifestPath,
            combatStart,
            Path.Combine(outDir, $"combat-comparison.{side}.start.state"));
        return CombatProjection.FromTrace(manifest.RunId, trace, DigestOf(snapshot));
    }

    private static string Show(string value) => value.Length == 0 ? "(none)" : value;

    private static string Describe(CombatTurn? turn) => turn is null
        ? "(this line's fight was already over)"
        : $"enemy hp lost {turn.EnemyHealthLost,3}  player hp lost {turn.HealthLost,3}  " +
          $"consumables {(turn.ConsumablesUsed.Count == 0 ? "none" : string.Join(",", turn.ConsumablesUsed))}  " +
          $"actions {string.Join(" ", turn.Actions.Select(Describe))}";

    private static string Describe(TurnAction action) => action.Verb switch
    {
        "PlayCard" => action.Args.GetValueOrDefault("card_id", "PlayCard"),
        _ => action.Verb,
    };
}

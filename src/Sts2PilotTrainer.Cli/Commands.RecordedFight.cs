using System.Globalization;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// Replays a manifest through the real engine and writes the recording's own line
    /// of its first fight: the trace through the end of that fight, bound to the
    /// history it replayed and to the combat-start snapshot digest.
    ///
    /// This is the recording's side of the in-game comparison. The retail client
    /// cannot replay - there is one process and one run in it, and the run is the
    /// player's - so the recording's line is produced here, from a fresh replay, and
    /// shipped inside the mod beside the manifest. Nothing in the file is transcribed
    /// or declared: every value is what the engine produced, and
    /// <see cref="RecordedFight.Bind"/> refuses the file unless it is the replay of
    /// exactly the manifest in hand.
    /// </summary>
    internal static int RecordedFightCommand(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");
        var outPath = Args.Value(args, "--out")
            ?? Path.Combine("build", "evidence", Path.GetFileName(manifestPath)
                .Replace(".replay.json", ".recorded-fight.json", StringComparison.Ordinal));
        var artifact = EvidenceArtifact.PreparePath(outPath);
        var scratch = Path.Combine(
            Path.GetDirectoryName(artifact.Path)!,
            $".{Path.GetFileName(artifact.Path)}.{Guid.NewGuid():N}.scratch");
        Directory.CreateDirectory(scratch);
        try
        {
            return WriteRecordedFight(manifestPath, artifact, scratch);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static int WriteRecordedFight(string manifestPath, EvidenceArtifact artifact, string scratch)
    {
        var manifest = ManifestJson.Load(manifestPath);
        var verifiedPath = Path.Combine(scratch, "recorded-fight.verified.json");
        var replay = SelfProcess.Run("replay", manifestPath, "--out", verifiedPath);
        if (replay.ExitCode != 0)
        {
            Console.Write(replay.StandardOutput);
            Console.Error.Write(replay.StandardError);
            throw new ManifestException(
                $"{Path.GetFileName(manifestPath)} does not replay cleanly, so it has no verified fight to " +
                "record. The recording's line has to be a reproduction, not a reading.");
        }

        var verified = ManifestJson.Load(verifiedPath);
        var trace = verified.Verification?.Trace
            ?? throw new ManifestException(
                $"The replay of {Path.GetFileName(manifestPath)} wrote no trace, so its fight cannot be recorded.");
        var combatStart = CombatStartSeq(trace)
            ?? throw new ManifestException(
                $"The replay of {Path.GetFileName(manifestPath)} never entered combat, so it has no fight to record.");
        var snapshot = ReplayPrefix(manifestPath, combatStart, Path.Combine(scratch, "recorded-fight.start.state"));

        var fight = RecordedFight.From(manifest, trace, DigestOf(snapshot));
        fight.Bind(manifest);
        artifact.WriteAtomic(fight.Serialize() + "\n");

        var projection = fight.Projection();
        Console.WriteLine($"recording       : {fight.RunId}");
        Console.WriteLine($"fight           : {projection.Boundary.GetValueOrDefault("combat.encounter", "unknown")}");
        Console.WriteLine(
            $"covered         : actions through {fight.CoveredThroughSeq.ToString(CultureInfo.InvariantCulture)}, " +
            $"{fight.Trace.Steps.Count.ToString(CultureInfo.InvariantCulture)} sampled step(s)");
        Console.WriteLine($"history hash    : {fight.ActionHistoryHash}");
        Console.WriteLine($"snapshot digest : {fight.CombatStartSnapshotDigest}");
        Console.WriteLine(
            $"outcome         : {projection.Summary.Outcome} on turn " +
            $"{projection.Summary.TotalTurns.ToString(CultureInfo.InvariantCulture)}, " +
            $"{projection.Summary.StartingHealth.ToString(CultureInfo.InvariantCulture)} -> " +
            $"{projection.Summary.FinalHealth.ToString(CultureInfo.InvariantCulture)} health");
        Console.WriteLine();
        Console.WriteLine($"recorded fight: {Paths.Display(artifact.Path)}");
        return 0;
    }
}

using System.Globalization;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// Replays a manifest through the real engine and writes the recording's own line
    /// of each fight the manifest declares a combat-start boundary for: the trace
    /// through the end of that fight, bound to the history it replayed and to the
    /// boundary's combat-start snapshot digest.
    ///
    /// Exactly the declared boundaries, and never a fight the replay happened to
    /// reach. A fight with no declared boundary has no digest a comparison could be
    /// shown to be against, so cutting one would put a line in the file that nothing
    /// could ever bind - and deriving the boundary is the arbiter's other job, not
    /// something to be done implicitly here.
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
                .Replace(".replay.json", ".recorded-fights.json", StringComparison.Ordinal));
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
        var verifiedPath = Path.Combine(scratch, "recorded-fights.verified.json");
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
        var declared = manifest.Boundaries
            .Where(boundary => boundary.IsCombatStart && boundary.Fight is not null)
            .OrderBy(boundary => boundary.Fight!.Value)
            .ToList();
        if (declared.Count == 0)
        {
            throw new ManifestException(
                $"{Path.GetFileName(manifestPath)} declares no combat-start boundary, so there is no fight of it " +
                "a comparison could be shown to be against. Derive one with combat-snapshot first.");
        }

        var digests = new SortedDictionary<int, string>();
        foreach (var boundary in declared)
        {
            var state = ReplayPrefix(
                manifestPath, boundary.AfterSeq,
                Path.Combine(scratch, $"recorded-fight.{boundary.Fight!.Value}.start.state"));
            var derived = DigestOf(state);
            if (!string.Equals(derived, boundary.Digest.Value, StringComparison.Ordinal))
            {
                throw new ManifestException(
                    $"Replaying to {boundary.Describe()} produced {derived} and the recording declares " +
                    $"{boundary.Digest.Value}. Refusing to record a fight from a boundary that has drifted.");
            }
            digests[boundary.Fight!.Value] = derived;
        }

        var fights = RecordedFights.From(manifest, trace, digests);
        fights.Bind(manifest);
        artifact.WriteAtomic(fights.Serialize() + "\n");

        Console.WriteLine($"recording       : {fights.RunId}");
        foreach (var fight in fights.Fights)
        {
            var projection = fights.Projection(fight.Fight);
            Console.WriteLine();
            Console.WriteLine(
                $"fight {fight.Fight.ToString(CultureInfo.InvariantCulture)}         : " +
                $"{projection.Boundary.GetValueOrDefault("combat.encounter", "unknown")}");
            Console.WriteLine(
                $"covered         : actions {fight.CombatStartSeq.ToString(CultureInfo.InvariantCulture)} " +
                $"through {fight.CoveredThroughSeq.ToString(CultureInfo.InvariantCulture)}, " +
                $"{fight.Trace.Steps.Count.ToString(CultureInfo.InvariantCulture)} sampled step(s)");
            Console.WriteLine($"history hash    : {fight.ActionHistoryHash}");
            Console.WriteLine($"snapshot digest : {fight.CombatStartSnapshotDigest}");
            Console.WriteLine(
                $"outcome         : {projection.Summary.Outcome} on turn " +
                $"{projection.Summary.TotalTurns.ToString(CultureInfo.InvariantCulture)}, " +
                $"{projection.Summary.StartingHealth.ToString(CultureInfo.InvariantCulture)} -> " +
                $"{projection.Summary.FinalHealth.ToString(CultureInfo.InvariantCulture)} health");
        }
        Console.WriteLine();
        Console.WriteLine($"recorded fights: {Paths.Display(artifact.Path)}");
        return 0;
    }
}

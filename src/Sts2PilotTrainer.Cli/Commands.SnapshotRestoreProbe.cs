using System.Text.Json;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// Asks whether the game's own save format can carry a run across a process
    /// boundary without changing it, at the one boundary this project enters runs at.
    ///
    /// Three processes, because two of them must not be able to help each other: one
    /// replays the manifest and finds where its fight begins, one replays that prefix
    /// again and serializes the run, and one starts from the bytes alone and restores
    /// it. The comparison happens here, over the two artifacts, and refuses before it
    /// compares anything if either side's act room set could not be read - see
    /// SnapshotRestoreProbe on why that particular absence would otherwise look like
    /// agreement.
    ///
    /// This answers a design question and verifies nothing about a manifest. Its
    /// artifact is evidence for the boundary-cache decision, and is not a publication
    /// gate condition.
    /// </summary>
    internal static int SnapshotRestoreProbe(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");

        // The two phases run as this same command in a fresh process each. The engine
        // keeps a run manager singleton that refuses a second run, so a restore in the
        // process that replayed would not be a restore at all.
        if (Args.Value(args, "--phase") is { } phase)
        {
            return SnapshotRestorePhase(args, manifestPath, phase);
        }

        var outDir = Args.Value(args, "--out") ?? "build/evidence";
        var control = Args.Value(args, "--control");

        // A control writes beside the real report rather than over it. The evidence a
        // decision rests on and a demonstration that the guard fires are two different
        // artifacts, and one file holding whichever ran last is neither.
        var reportArtifact = EvidenceArtifact.Prepare(
            outDir,
            control is null ? "snapshot-restore-probe.json" : $"snapshot-restore-probe.control-{control}.json");

        var combatStart = LocateCombatStart(manifestPath, outDir);
        if (combatStart is null) return 1;

        var savePath = EvidenceArtifact.Prepare(outDir, "snapshot-restore-probe.run-save.json").Path;
        var capturePath = EvidenceArtifact.Prepare(outDir, "snapshot-restore-probe.capture.json").Path;
        var restorePath = EvidenceArtifact.Prepare(outDir, "snapshot-restore-probe.restore.json").Path;

        var capture = RunPhase<SnapshotRestoreCapture>(
            capturePath,
            "snapshot-restore-probe", manifestPath, "--phase", "capture",
            "--stop-after", combatStart.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--save", savePath, "--out", capturePath);
        if (capture is null) return 1;

        var restoration = RunPhase<SnapshotRestoreRestoration>(
            restorePath,
            "snapshot-restore-probe", manifestPath, "--phase", "restore",
            "--save", savePath, "--out", restorePath);
        if (restoration is null) return 1;

        if (control is not null)
        {
            (capture, restoration) = Engine.SnapshotRestoreProbe.ApplyControl(control, capture, restoration);
            Console.WriteLine(
                $"control         : {control} - both states damaged into the same unreadable-room-set " +
                "reading, so their digests agree and mean nothing");
        }

        var report = Engine.SnapshotRestoreProbe.Compare(
            Path.GetFileName(manifestPath), capture, restoration);
        reportArtifact.WriteAtomic(JsonSerializer.Serialize(report, Json.Indented) + "\n");

        Console.WriteLine($"manifest        : {report.RunId}");
        Console.WriteLine($"combat starts   : after action {report.CombatStartSeq}");
        Console.WriteLine(
            $"run save        : schema v{report.SaveSchemaVersion}, {report.SaveByteCount} bytes, " +
            $"{report.SaveSha256}");
        Console.WriteLine($"replayed state  : {report.ReplayedDigest}");
        Console.WriteLine($"                  {report.ReplayedFieldCount} fields, act room set " +
                          $"{report.ReplayedActRoomSet.Reading}");

        foreach (var stage in report.Stages)
        {
            Console.WriteLine();
            Console.WriteLine($"restored ({stage.Stage}): {stage.RestoredDigest}");
            Console.WriteLine($"                  {stage.RestoredFieldCount} fields, act room set " +
                              $"{stage.RestoredActRoomSet.Reading}");
            Console.WriteLine($"                  {stage.AgreeingFieldCount} field(s) agree, " +
                              $"{stage.DifferingFields.Count} differ");
            foreach (var difference in stage.DifferingFields)
            {
                Console.WriteLine($"                    {difference.Field}: {Abbreviate(difference.Replayed)} " +
                                  $"-> {Abbreviate(difference.Restored)}");
            }
        }

        foreach (var refusal in report.Refusals)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(refusal);
        }

        Console.WriteLine();
        Console.WriteLine(report.Answer);
        Console.WriteLine();
        Console.WriteLine($"report: {Paths.Display(reportArtifact.Path)}");

        // A refused comparison is a failure; a measured disagreement is a result. The
        // probe exists to find out which of the two answers is true, so answering
        // "not restorable" is a successful run of it.
        //
        // Under a control the expectation is inverted: the control exists to be
        // refused, and a control that produced an answer is the failure.
        if (control is null) return report.Refusals.Count > 0 ? 1 : 0;
        if (report.Refusals.Count > 0) return 0;

        Console.Error.WriteLine(
            $"Control '{control}' was not refused. Two states that carry no act content agreed on their " +
            "digests and the probe called it agreement, which is the one reading this probe must never " +
            "produce.");
        return 1;
    }

    private static int SnapshotRestorePhase(string[] args, string manifestPath, string phase)
    {
        var outPath = Args.Value(args, "--out")
            ?? throw new ManifestException("snapshot-restore-probe --phase needs --out <path>.");
        var savePath = Args.Value(args, "--save")
            ?? throw new ManifestException("snapshot-restore-probe --phase needs --save <path>.");
        var artifact = EvidenceArtifact.PreparePath(outPath);

        switch (phase)
        {
            case "capture":
            {
                var stopAfter = Args.Value(args, "--stop-after")
                    ?? throw new ManifestException("snapshot-restore-probe --phase capture needs --stop-after <seq>.");
                var capture = Engine.SnapshotRestoreProbe.Capture(
                    ManifestJson.Load(manifestPath),
                    int.Parse(stopAfter, System.Globalization.CultureInfo.InvariantCulture),
                    Sts2PilotTrainer.IO.WorktreePath.Require(savePath));
                artifact.WriteAtomic(JsonSerializer.Serialize(capture, Json.Indented) + "\n");
                return 0;
            }

            case "restore":
            {
                var restoration = Engine.SnapshotRestoreProbe.Restore(
                    Sts2PilotTrainer.IO.WorktreePath.Require(savePath));
                artifact.WriteAtomic(JsonSerializer.Serialize(restoration, Json.Indented) + "\n");
                return 0;
            }

            default:
                throw new ManifestException(
                    $"Unknown snapshot-restore-probe phase '{phase}'. Known phases: capture, restore.");
        }
    }

    /// <summary>
    /// Where this manifest's fight begins, read out of a full replay's trace through
    /// the same call the snapshot and the gate use. Asking the manifest instead would
    /// let the boundary this probe measures differ from the boundary everything else
    /// means by combat start.
    /// </summary>
    private static int? LocateCombatStart(string manifestPath, string outDir)
    {
        var verifiedPath = Path.Combine(outDir, "snapshot-restore-probe.verified.json");
        var replayed = SelfProcess.Run("replay", manifestPath, "--out", verifiedPath);
        if (replayed.ExitCode != 0)
        {
            Console.Write(replayed.StandardOutput);
            Console.Error.Write(replayed.StandardError);
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "The manifest does not replay cleanly, so it has no verified boundary to serialize.");
            return null;
        }

        var trace = ManifestJson.Load(verifiedPath).Verification?.Trace
            ?? throw new ManifestException("The replay wrote no trace, so combat start cannot be located.");
        var combatStart = CombatProjection.CoverageOf(trace).CombatStartSeq;
        if (combatStart is null)
        {
            Console.Error.WriteLine(
                "This history never enters combat, so it has no combat-start boundary to restore to.");
            return null;
        }

        return combatStart;
    }

    private static T? RunPhase<T>(string artifactPath, params string[] args) where T : class
    {
        var child = SelfProcess.Run(args);
        if (child.ExitCode != 0)
        {
            Console.Write(child.StandardOutput);
            Console.Error.Write(child.StandardError);
            return null;
        }

        return JsonSerializer.Deserialize<T>(File.ReadAllText(artifactPath), ManifestJson.Options);
    }

    /// <summary>A canonical value fit for one terminal line. The artifact keeps the
    /// whole value; an ordered deck rendered in full would bury the field names that
    /// are the point of the list.</summary>
    private static string Abbreviate(string value) =>
        value.Length <= 60 ? value : value[..57] + "...";
}

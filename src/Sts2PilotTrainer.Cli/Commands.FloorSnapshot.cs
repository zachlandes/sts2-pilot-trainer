using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.IO;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// Materialises the floor-entry snapshot for one floor arrival: takes the game's own
    /// save there, restores it in a process that replayed nothing, and caches it only if
    /// the restored run is the recorded arrival.
    ///
    /// The one place a boundary is stored as a serialized run rather than re-derived, and
    /// the conditions are what make that sound rather than a shortcut. The save is keyed
    /// by the whole ordered history that produced it, so it can never be served for a run
    /// that would not reach it. It is restored through the retail continue path with the
    /// save's own <c>preFinishedRoom</c>, which is what stops the engine generating a
    /// second, different room. And it is written into the cache only after a restore has
    /// already reproduced the digest the recording declares - so an unverified save is
    /// never a file anything could find, and a restore is never trusted on the strength
    /// of having been cached.
    ///
    /// Floor arrivals with a live fight only. An arrival with none carries the previous
    /// fight's finished combat state in the live run and not in the game's save: the same
    /// run in every other respect and a different canonical state, which is the most
    /// convincing shape a wrong answer has. See docs/native-replay-format.md.
    ///
    /// <c>--control wrong-floor</c> restores this floor's save against another floor's
    /// boundary and requires the comparison to refuse it. A cache whose guard cannot be
    /// shown firing is a cache nobody has reason to believe.
    /// </summary>
    internal static int FloorSnapshot(string[] args)
    {
        var manifestPath = Args.Positional(args, 0, "manifest path");
        var phase = Args.Value(args, "--phase");
        var floorOption = Args.Value(args, "--floor");
        var outOption = Args.Value(args, "--out");
        var cacheOption = Args.Value(args, "--cache");
        var control = Args.Value(args, "--control");

        // Read and discarded: the phases below take their save path from it, and this is
        // where a --save with no value is refused, before any output directory exists.
        Args.Value(args, "--save");

        if (Args.Value(args, "--fight") is not null)
        {
            throw new ManifestException(
                "floor-snapshot caches a floor arrival, and a fight's start is not one. Only an arrival on a " +
                "floor is stored as a serialized run; a fight's boundary is re-derived by replaying the " +
                "history, which is what combat-snapshot does.");
        }

        // Named before anything is written, so a control nobody offers is refused with
        // the list of the ones that exist rather than after a report path has been
        // composed out of it.
        if (control is not null && control != WrongFloorControl)
        {
            throw new ManifestException(
                $"'{control}' is not a control of this command. Available: {WrongFloorControl}.");
        }

        var manifest = ManifestJson.Load(manifestPath);
        if (phase is not null) return FloorSnapshotPhase(args, manifest, phase);

        var floor = floorOption ?? throw new ManifestException(
            "floor-snapshot needs the floor to snapshot: --floor <n>, counting the floors the run arrived on.");
        var plan = FloorPlan(manifest, floor);

        var outDir = outOption ?? "build/evidence";
        var cacheDir = cacheOption ?? "build/snapshots";
        var reportArtifact = EvidenceArtifact.Prepare(
            outDir, control is null ? "floor-snapshot.json" : $"floor-snapshot.control-{control}.json");

        var workspace = WorktreePath.RequireChild(
            WorktreePath.Require(outDir), $".floor-snapshot.{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            return RunFloorSnapshot(
                manifestPath, manifest, plan, cacheDir, workspace, control, reportArtifact);
        }
        finally
        {
            if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
        }
    }

    private static int RunFloorSnapshot(
        string manifestPath, ReplayManifest manifest, FloorEntryPlan plan, string cacheDir,
        string workspace, string? control, EvidenceArtifact reportArtifact)
    {
        var savePath = Path.Combine(workspace, FloorEntrySnapshot.SaveFileName);
        var capturePath = Path.Combine(workspace, "capture.json");
        var restorePath = Path.Combine(workspace, "restore.json");

        Console.WriteLine($"recording       : {manifest.RunId}");
        Console.WriteLine($"boundary        : {plan.Describe()}, after action {Text(plan.BoundarySeq)}");
        Console.WriteLine($"snapshot key    : {plan.SnapshotKey.ToCacheDirectoryName()}");

        // The floor the restored save is compared against. The same one for a real
        // materialisation; a different one for the control, whose whole content is that
        // the comparison refuses it. Named before the capture, so a recording that
        // cannot form the control is told so rather than after a replay of its history.
        var against = control is null ? plan : ControlPlan(manifest, plan);

        var captured = SelfProcess.Run(
            "floor-snapshot", manifestPath, "--phase", "capture",
            "--floor", Text(plan.FloorNumber), "--save", savePath, "--out", capturePath);
        if (captured.ExitCode != 0)
        {
            Console.Write(captured.StandardOutput);
            Console.Error.Write(captured.StandardError);
            return 1;
        }

        var candidate = JsonSerializer.Deserialize<FloorEntrySnapshotCandidate>(
            File.ReadAllText(capturePath), ManifestJson.Options)!;

        Console.WriteLine($"declared digest : {candidate.DeclaredDigest} [{candidate.DeclaredDigestSource}]");
        Console.WriteLine($"replayed digest : {candidate.ReplayedDigest}");
        Console.WriteLine($"live fight there: {candidate.LiveCombat}");
        Console.WriteLine();
        Console.WriteLine($"the game asked to save {Text(candidate.SavesTaken.Count)} time(s) on the way:");
        foreach (var save in candidate.SavesTaken) Console.WriteLine($"  {save}");
        // The last of them, named rather than left to be counted off the list above: it
        // is the one a snapshot keeps, and whether it is an arrival save at all is what
        // the pre-finished room says.
        Console.WriteLine($"the last of them, which is the one a snapshot keeps: {candidate.SaveKept}");

        if (candidate.Refusals.Count > 0)
        {
            Console.WriteLine();
            foreach (var refusal in candidate.Refusals) Console.Error.WriteLine(refusal);
            reportArtifact.WriteAtomic(JsonSerializer.Serialize(
                Report(manifestPath, plan, against, candidate, restored: null, control, cached: false,
                    refusals: candidate.Refusals),
                Json.Indented) + "\n");
            Console.WriteLine();
            Console.WriteLine("NOT SNAPSHOTTABLE: nothing was written into the cache.");
            Console.WriteLine($"report: {Paths.Display(reportArtifact.Path)}");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine(control is null
            ? "restoring that save in a fresh process, through the retail continue path:"
            : $"CONTROL: restoring that save against {against.Describe()}, which it must not reproduce:");

        var restoredRun = SelfProcess.Run(
            "floor-snapshot", manifestPath, "--phase", "restore",
            "--floor", Text(against.FloorNumber), "--save", savePath, "--out", restorePath);
        if (restoredRun.ExitCode != 0)
        {
            Console.Write(restoredRun.StandardOutput);
            Console.Error.Write(restoredRun.StandardError);
            return 1;
        }

        var restored = JsonSerializer.Deserialize<FloorEntryRestoreReading>(
            File.ReadAllText(restorePath), ManifestJson.Options)!;

        Console.WriteLine($"restored digest : {restored.Digest}");
        Console.WriteLine($"observed values : {Text(restored.Comparisons.Count)} compared, " +
                          $"{Text(restored.Comparisons.Count(comparison => !comparison.Matches))} disagreed");
        foreach (var comparison in restored.Comparisons.Where(comparison => !comparison.Matches))
        {
            Console.WriteLine(
                $"  {comparison.Field}: recording '{comparison.Expected}', restored '{comparison.Actual}'");
        }

        var refusals = RestoreRefusals(candidate, restored);
        if (control is not null)
        {
            var refused = refusals.Count == 0 && !restored.Matches;
            Console.WriteLine();
            foreach (var refusal in refusals) Console.Error.WriteLine(refusal);
            Console.WriteLine(refused
                ? $"CONTROL HELD: {restored.Refusal}"
                : refusals.Count > 0
                    ? "CONTROL ESTABLISHED NOTHING: the comparison it rests on was itself refused, so whether " +
                      "another floor's save reproduces this boundary is still unmeasured."
                    : "CONTROL FAILED: restoring another floor's save reproduced this boundary, so the comparison " +
                      "discriminates nothing and no snapshot may be trusted on it.");
            reportArtifact.WriteAtomic(JsonSerializer.Serialize(
                Report(manifestPath, plan, against, candidate, restored, control, cached: false, refusals),
                Json.Indented) + "\n");
            Console.WriteLine($"report: {Paths.Display(reportArtifact.Path)}");
            return refused ? 0 : 1;
        }

        if (refusals.Count > 0 || !restored.Matches)
        {
            Console.WriteLine();
            foreach (var refusal in refusals) Console.Error.WriteLine(refusal);
            if (restored.Refusal is { } why) Console.Error.WriteLine(why);
            reportArtifact.WriteAtomic(JsonSerializer.Serialize(
                Report(manifestPath, plan, against, candidate, restored, control, cached: false, refusals),
                Json.Indented) + "\n");
            Console.WriteLine();
            Console.WriteLine("NOT SNAPSHOTTABLE: nothing was written into the cache.");
            Console.WriteLine($"report: {Paths.Display(reportArtifact.Path)}");
            return 1;
        }

        var snapshot = new FloorEntrySnapshot(
            Schema: FloorEntrySnapshot.SchemaId,
            RunId: candidate.RunId,
            BuildVersion: candidate.BuildVersion,
            BuildCommit: candidate.BuildCommit,
            BoundaryKind: plan.Kind,
            Floor: plan.FloorNumber,
            AfterSeq: plan.BoundarySeq,
            ActIndex: candidate.ActIndex,
            DeclaredDigest: candidate.DeclaredDigest,
            DeclaredDigestSource: candidate.DeclaredDigestSource,
            VerifiedDigest: restored.Digest,
            SaveFile: FloorEntrySnapshot.SaveFileName,
            SaveSha256: candidate.SaveSha256,
            SaveByteCount: candidate.SaveByteCount,
            SaveSchemaVersion: candidate.SaveSchemaVersion,
            SavePreFinishedRoom: candidate.SavePreFinishedRoom,
            Key: plan.SnapshotKey);

        var directory = snapshot.WriteInto(cacheDir, File.ReadAllText(savePath));

        reportArtifact.WriteAtomic(JsonSerializer.Serialize(
            Report(manifestPath, plan, against, candidate, restored, control, cached: true, refusals),
            Json.Indented) + "\n");

        Console.WriteLine();
        Console.WriteLine(
            $"SNAPSHOTTED: the game's own save at {plan.Describe()}, restored in a fresh process through the " +
            "retail continue path, reproduces the digest this recording declares field for field.");
        Console.WriteLine($"cache: {Paths.Display(directory)}");
        Console.WriteLine($"report: {Paths.Display(reportArtifact.Path)}");
        return 0;
    }

    /// <summary>
    /// Every reason the restore's agreement would not mean what it looks like, collected
    /// before the agreement is read. Two states that both lost the act's generated
    /// content agree on a sentinel, and two builds cannot answer one question about one
    /// build.
    /// </summary>
    private static List<string> RestoreRefusals(
        FloorEntrySnapshotCandidate candidate, FloorEntryRestoreReading restored)
    {
        var refusals = new List<string>();

        if (!restored.ActRoomSet.Present)
        {
            refusals.Add(
                $"The restored state's act room set is '{restored.ActRoomSet.Reading}': the act's generated " +
                "content is absent from it, and two states that both degrade to that sentinel agree on it " +
                "exactly while saying nothing about either run.");
        }

        if (!string.Equals(candidate.BuildVersion, restored.BuildVersion, StringComparison.Ordinal) ||
            !string.Equals(candidate.BuildCommit, restored.BuildCommit, StringComparison.Ordinal))
        {
            refusals.Add(
                $"The replay ran on {candidate.BuildVersion} ({candidate.BuildCommit}) and the restore on " +
                $"{restored.BuildVersion} ({restored.BuildCommit}). Two builds cannot answer one question " +
                "about one build.");
        }

        if (!string.Equals(candidate.SaveSha256, restored.SaveSha256, StringComparison.Ordinal))
        {
            refusals.Add(
                "The restore read a different save than the capture wrote, so the two states are of two " +
                "different runs.");
        }

        return refusals;
    }

    private static object Report(
        string manifestPath, FloorEntryPlan plan, FloorEntryPlan against,
        FloorEntrySnapshotCandidate candidate, FloorEntryRestoreReading? restored, string? control, bool cached,
        IReadOnlyList<string> refusals) => new
    {
        schema = "sts2-pilot-trainer/floor-snapshot/v1",
        manifest = Path.GetFileName(manifestPath),
        run_id = candidate.RunId,
        build_version = candidate.BuildVersion,
        build_commit = candidate.BuildCommit,
        boundary = new BoundarySelector
        {
            Kind = plan.Kind,
            Floor = plan.FloorNumber,
        }.ToString(),
        floor = plan.FloorNumber,
        after_seq = plan.BoundarySeq,
        act_index = candidate.ActIndex,
        control,
        // The boundary the restored save was measured against, which is this floor's
        // for a materialisation and another floor's for the control. Without it a
        // reader of a control report cannot re-check what it refused.
        restored_against_floor = against.FloorNumber,
        restored_against_after_seq = against.BoundarySeq,
        snapshot_key = plan.SnapshotKey,
        declared_digest = candidate.DeclaredDigest,
        declared_digest_source = candidate.DeclaredDigestSource,
        replayed_digest = candidate.ReplayedDigest,
        restored_digest = restored?.Digest,
        live_combat_at_arrival = candidate.LiveCombat,
        replayed_act_room_set = candidate.ActRoomSet,
        restored_act_room_set = restored?.ActRoomSet,
        saves_taken = candidate.SavesTaken,
        save_kept = candidate.SaveKept,
        save_pre_finished_room = candidate.SavePreFinishedRoom,
        save_sha256 = candidate.SaveSha256,
        save_byte_count = candidate.SaveByteCount,
        save_schema_version = candidate.SaveSchemaVersion,
        observed_values_compared = restored?.Comparisons.Count ?? 0,
        observed_values_disagreeing =
            restored?.Comparisons.Count(comparison => !comparison.Matches) ?? 0,
        restored_run_saving = restored?.RunSaving,
        boundary_matches = restored?.Matches ?? false,
        boundary_refusal = restored?.Refusal,
        refusals,
        cached,
        restore_path =
            "RunState.FromSerializable, SetUpSavedSingleplayer, Launch, GenerateMap, then " +
            "LoadIntoLatestMapCoord with the save's own pre-finished room, as NGame.LoadRun passes it.",
        skipped_presentation_steps = new[]
        {
            "PreloadManager.LoadRunAssets and LoadActAssets - asset loading, no run state",
            "NGame.RootSceneContainer.SetCurrentScene(NRun.Create) - the run's scene",
            "NRun.Instance.GlobalUi.MapScreen.Drawings.LoadDrawings - the player's map drawings",
        },
    };

    private static int FloorSnapshotPhase(string[] args, ReplayManifest manifest, string phase)
    {
        var outPath = Args.Value(args, "--out")
            ?? throw new ManifestException("A phase writes its reading to --out <path>.");
        var savePath = Args.Value(args, "--save")
            ?? throw new ManifestException("A phase needs the save it works on: --save <path>.");
        var plan = FloorPlan(
            manifest,
            Args.Value(args, "--floor") ?? throw new ManifestException("A phase needs --floor <n>."));

        switch (phase)
        {
            case "capture":
            {
                var candidate = FloorEntrySnapshotCapture.Capture(manifest, plan, savePath);
                File.WriteAllText(
                    WorktreePath.Require(outPath),
                    JsonSerializer.Serialize(candidate, Json.Indented) + "\n");
                return 0;
            }

            case "restore":
            {
                var saveJson = File.ReadAllText(WorktreePath.Require(savePath));
                using var entry = RecordedFightEntry.RestoreHeadless(manifest, plan, saveJson);
                var equality = entry.VerifyBoundary();
                var state = entry.LiveState();
                var identity = GameIdentity.Read();

                File.WriteAllText(
                    WorktreePath.Require(outPath),
                    JsonSerializer.Serialize(
                        new FloorEntryRestoreReading(
                            BuildVersion: identity.BuildVersion,
                            BuildCommit: identity.Commit,
                            SaveSha256: FloorEntrySnapshot.HashOf(saveJson),
                            Digest: equality.ActualDigest,
                            RunSaving: entry.RunSaving,
                            ActRoomSet: Engine.SnapshotRestoreProbe.RoomSetReading(state),
                            Matches: equality.Matches,
                            Refusal: equality.Refusal,
                            Comparisons: equality.Comparisons),
                        Json.Indented) + "\n");
                return 0;
            }

            default:
                throw new ManifestException($"Unknown phase '{phase}'.");
        }
    }

    /// <summary>The floor arrival this command works on, read through the one owner of
    /// what a boundary coordinate means.</summary>
    private static FloorEntryPlan FloorPlan(ReplayManifest manifest, string floor) =>
        (FloorEntryPlan)BoundarySelector.ParseFightOrFloor(fight: null, floor: floor).PlanFor(manifest);

    /// <summary>
    /// The other floor a control restores against: the nearest arrival this recording
    /// declares on either side of the one being snapshotted.
    ///
    /// Nearest rather than first, because the control is stronger the closer the two
    /// runs are. A recording with only one floor arrival has no control available and
    /// says so rather than inventing one.
    /// </summary>
    private static FloorEntryPlan ControlPlan(ReplayManifest manifest, FloorEntryPlan plan)
    {
        var others = manifest.Boundaries
            .Where(boundary => boundary.IsFloorEntry && boundary.Floor is { } floor && floor != plan.FloorNumber)
            .Select(boundary => boundary.Floor!.Value)
            .OrderBy(floor => Math.Abs(floor - plan.FloorNumber))
            .ThenByDescending(floor => floor < plan.FloorNumber)
            .ToList();

        if (others.Count == 0)
        {
            throw new ManifestException(
                $"This recording declares one floor arrival, so there is no other floor to restore " +
                $"{plan.Describe()}'s save against. The control needs a second arrival to be a control.");
        }

        return FloorEntryPlan.For(manifest, others[0]);
    }

    private const string WrongFloorControl = "wrong-floor";

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// What one restore in a fresh process reached, and whether it is the recorded arrival.
///
/// Written by the restore phase and read by the parent, because a restore has to happen
/// in a process that replayed nothing for its agreement to mean anything.
/// </summary>
internal sealed record FloorEntryRestoreReading(
    [property: JsonPropertyName("build_version")] string BuildVersion,
    [property: JsonPropertyName("build_commit")] string BuildCommit,
    [property: JsonPropertyName("save_sha256")] string SaveSha256,
    [property: JsonPropertyName("digest")] string Digest,
    [property: JsonPropertyName("run_saving")] bool RunSaving,
    [property: JsonPropertyName("act_room_set")] ActRoomSetReading ActRoomSet,
    [property: JsonPropertyName("matches")] bool Matches,
    [property: JsonPropertyName("refusal")] string? Refusal,
    [property: JsonPropertyName("comparisons")] IReadOnlyList<FieldComparison> Comparisons);

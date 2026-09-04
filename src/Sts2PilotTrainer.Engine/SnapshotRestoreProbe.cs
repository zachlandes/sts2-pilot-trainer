using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Measures whether the game's own save format can carry a run across processes
/// without changing it.
///
/// The question is narrow and consequential: a boundary cache that stored a
/// serialized run rather than the history that produces it would be a great deal
/// faster to enter, and would be worth nothing unless a restored run is the same run
/// by the canonical state's own definition. So this replays a manifest to its
/// combat-start boundary, hands the run to <c>RunManager.ToSave</c>, restores it in a
/// fresh process through the call sequence the retail continue-run path uses, and
/// compares the two canonical states field by field.
///
/// Two things make the answer trustworthy rather than merely encouraging. The
/// restore never touches the run it is compared against - it starts from bytes, in a
/// process that replayed nothing. And a state whose act room set is unreadable is
/// refused on both sides before any digest is compared: that field degrades to the
/// sentinel <c>"unavailable"</c> when the engine's private <c>_rooms</c> cannot be
/// read, and two sentinels agree with each other perfectly while saying nothing about
/// the run. See <see cref="CanonicalStateProjection"/>.
/// </summary>
public static class SnapshotRestoreProbe
{
    public const string CaptureSchema = "sts2-pilot-trainer/snapshot-restore-probe/capture/v1";
    public const string RestoreSchema = "sts2-pilot-trainer/snapshot-restore-probe/restore/v1";
    public const string ReportSchema = "sts2-pilot-trainer/snapshot-restore-probe/v1";

    /// <summary>The canonical field the projection emits only when it could not read
    /// the act's room set. Its presence is the failure, not its value.</summary>
    public const string RoomSetSentinelField = "act.room_set";

    /// <summary>Canonical fields the act room set is projected as when it was
    /// readable. All three are present together or the read failed.</summary>
    public static readonly IReadOnlyList<string> RoomSetFields =
        ["act.normal_encounters", "act.elite_encounters", "act.events"];

    /// <summary>
    /// Replays the manifest to its combat-start boundary and writes the run out
    /// through the game's own serializer.
    ///
    /// <c>RunManager.ToSave</c> is what the retail client calls to write a run save,
    /// and it is called here for the same reason every other verb in this project maps
    /// onto a real one: a save assembled by hand would be a save of what this project
    /// believes a run is made of, which is the belief under test.
    /// </summary>
    public static SnapshotRestoreCapture Capture(ReplayManifest manifest, int combatStartSeq, string savePath)
    {
        var outcome = Arbiter.Run(manifest, stopAfterSeq: combatStartSeq);
        if (outcome.Report.Status is not (VerificationStatus.Verified or VerificationStatus.Partial))
        {
            throw new EngineException(
                $"Replaying {manifest.RunId} to action {combatStartSeq} did not succeed " +
                $"({outcome.Report.Status}). There is no boundary to serialize, and serializing whatever the " +
                "engine was left holding would measure the wrong run.\n" +
                string.Join("\n", outcome.Report.Diagnostics));
        }

        var state = outcome.FinalState
            ?? throw new EngineException("The replay produced no engine state to serialize.");

        var save = RunManager.Instance.ToSave(preFinishedRoom: null);
        var json = JsonSerializationUtility.ToJson(save);
        File.WriteAllText(savePath, json);

        var identity = GameIdentity.Read();
        return new SnapshotRestoreCapture(
            Schema: CaptureSchema,
            RunId: manifest.RunId,
            BuildVersion: identity.BuildVersion,
            BuildCommit: identity.Commit,
            CombatStartSeq: combatStartSeq,
            ReplayStatus: outcome.Report.Status.ToString(),
            SaveSchemaVersion: save.SchemaVersion,
            SaveSha256: Sha256(json),
            SaveByteCount: Encoding.UTF8.GetByteCount(json),
            ActRoomSet: RoomSetReading(state),
            Digest: state.Digest(),
            CanonicalFields: state.Fields.ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal));
    }

    /// <summary>
    /// Rebuilds the run from the save alone, in a process that has replayed nothing.
    ///
    /// The call sequence is the retail continue-run path's, read out of this build:
    /// <c>NMainMenu.OnContinueButtonPressedAsync</c> calls
    /// <see cref="RunState.FromSerializable"/>, then
    /// <c>RunManager.SetUpSavedSingleplayer</c> - which is what reaches the private
    /// <c>InitializeSavedRun</c> - and then <c>NGame.LoadRun</c>. The last of those is
    /// a Godot node's method and cannot run here; its engine steps are called directly
    /// (<c>Launch</c>, then <c>GenerateMap</c>, which loads the map the save carries
    /// rather than generating one, then <c>LoadIntoLatestMapCoord</c>, which re-enters
    /// the room), and its asset preloading and scene construction are skipped.
    ///
    /// The state is projected twice, and that is the point rather than thoroughness.
    /// Stopping after the save is restored asks what the save format carries; going on
    /// through the room re-entry asks what the retail path then does with it. A probe
    /// that measured only one of the two could be answered with "you stopped in the
    /// wrong place", and the two answers are not the same answer.
    /// </summary>
    public static SnapshotRestoreRestoration Restore(string savePath)
    {
        EngineHost.Start();

        var json = File.ReadAllText(savePath);
        var read = JsonSerializationUtility.FromJson<SerializableRun>(json);
        var save = read.SaveData;
        if (!read.Success || save is null)
        {
            throw new EngineException(
                $"The game's own reader refused this save ({read.Status}): {read.ErrorMessage ?? "no detail"}. " +
                "A run that cannot be read back is already the answer to whether it can be restored.");
        }

        var steps = new List<SnapshotRestoreStep>();
        var runState = Step(steps, "RunState.FromSerializable", () => RunState.FromSerializable(save));
        Step(steps, "RunManager.SetUpSavedSingleplayer", () =>
        {
            RunManager.Instance.SetUpSavedSingleplayer(runState, save).GetAwaiter().GetResult();
            return runState;
        });
        Step(steps, "RunManager.Launch", () => RunManager.Instance.Launch());
        Step(steps, "RunManager.GenerateMap", () =>
        {
            RunManager.Instance.GenerateMap().GetAwaiter().GetResult();
            return runState;
        });

        var stages = new List<SnapshotRestoreStage>
        {
            Stage(
                "save_restored",
                "RunState.FromSerializable, SetUpSavedSingleplayer, Launch, GenerateMap: everything the save " +
                "itself carries, with no room entered.",
                runState),
        };

        Step(steps, "RunManager.LoadIntoLatestMapCoord", () =>
        {
            RunManager.Instance.LoadIntoLatestMapCoord(preFinishedRoom: null).GetAwaiter().GetResult();
            return runState;
        });

        stages.Add(Stage(
            "room_re_entered",
            "The engine half of NGame.LoadRun completed: the last visited map coordinate re-entered, which is " +
            "how a continued run gets a room to be in.",
            runState));

        var identity = GameIdentity.Read();
        return new SnapshotRestoreRestoration(
            Schema: RestoreSchema,
            BuildVersion: identity.BuildVersion,
            BuildCommit: identity.Commit,
            SaveSchemaVersion: save.SchemaVersion,
            SaveSha256: Sha256(json),
            Steps: steps,
            SkippedPresentationSteps:
            [
                "PreloadManager.LoadRunAssets and LoadActAssets - asset loading, no run state",
                "NGame.RootSceneContainer.SetCurrentScene(NRun.Create) - the run's scene",
                "NRun.Instance.GlobalUi.MapScreen.Drawings.LoadDrawings - the player's map drawings",
            ],
            Stages: stages);
    }

    private static SnapshotRestoreStage Stage(string name, string description, RunState runState)
    {
        var state = CanonicalStateProjection.Project(runState);
        return new SnapshotRestoreStage(
            name,
            description,
            RoomSetReading(state),
            state.Digest(),
            state.Fields.ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal));
    }

    /// <summary>
    /// The one control this probe has: two states that agree perfectly and say nothing.
    ///
    /// Both sides are replaced by the same state with the act's generated content
    /// removed and the projection's <c>"unavailable"</c> sentinel in its place, which
    /// is exactly what a build where <c>_rooms</c> could not be read would produce. The
    /// digests then agree by construction. A probe that reported that as "restorable"
    /// would be reporting agreement between two absences, and the whole Phase 5
    /// decision would rest on it - so the control asserts the refusal, and it is run
    /// against the same code path the real comparison uses rather than a copy of it.
    /// </summary>
    public const string UnreadableRoomSetControl = "unreadable-room-set";

    public static (SnapshotRestoreCapture Capture, SnapshotRestoreRestoration Restoration) ApplyControl(
        string name, SnapshotRestoreCapture capture, SnapshotRestoreRestoration restoration)
    {
        if (name != UnreadableRoomSetControl)
        {
            throw new EngineException(
                $"'{name}' is not a control of this probe. Available: {UnreadableRoomSetControl}.");
        }

        var damaged = WithoutRoomSet(capture.CanonicalFields);
        var reading = RoomSetReading(damaged);
        var digest = damaged.Digest();
        var fields = damaged.Fields.ToDictionary(field => field.Key, field => field.Value, StringComparer.Ordinal);

        return (
            capture with { ActRoomSet = reading, Digest = digest, CanonicalFields = fields },
            restoration with
            {
                Stages = restoration.Stages
                    .Select(stage => stage with
                    {
                        ActRoomSet = reading,
                        Digest = digest,
                        CanonicalFields = fields,
                    })
                    .ToList(),
            });
    }

    private static CanonicalState WithoutRoomSet(IReadOnlyDictionary<string, string> fields)
    {
        // Every act field goes, not only the three encounter lists: the projection's
        // unavailable path adds the sentinel and returns, so a state that could not
        // read _rooms has no other act field either. A control that left the visited
        // counts behind would be a state the engine cannot produce.
        var builder = CanonicalState.Build();
        foreach (var (field, value) in fields.OrderBy(field => field.Key, StringComparer.Ordinal))
        {
            if (field.StartsWith("act.", StringComparison.Ordinal)) continue;
            builder.Add(field, value);
        }
        builder.Add(RoomSetSentinelField, "unavailable");
        return builder.ToState();
    }

    /// <summary>
    /// The verdict, computed from the two artifacts and from nothing else.
    ///
    /// Written as a refusal first: any reason the comparison cannot mean what it looks
    /// like is collected before the digests are so much as compared, and an agreement
    /// reported alongside a refusal is not an agreement. That ordering is the whole
    /// defence against the failure this probe exists to avoid - two states that agree
    /// because neither of them could be read.
    /// </summary>
    public static SnapshotRestoreReport Compare(
        string manifestFileName, SnapshotRestoreCapture capture, SnapshotRestoreRestoration restoration)
    {
        var refusals = new List<string>();

        foreach (var (side, reading) in restoration.Stages
                     .Select(stage => ($"restored state '{stage.Name}'", stage.ActRoomSet))
                     .Prepend(("replayed state", capture.ActRoomSet)))
        {
            if (reading.Present) continue;
            refusals.Add(
                $"The {side} has act room set '{reading.Reading}': the engine's private _rooms field could not " +
                "be read, so the generated content of the act is absent from that state. Two states that both " +
                "degrade to this sentinel agree on it exactly, which is why this is a refusal and not a " +
                "difference.");
        }

        if (restoration.Stages.Count == 0)
        {
            refusals.Add("The restore produced no state to compare.");
        }

        if (!string.Equals(capture.BuildVersion, restoration.BuildVersion, StringComparison.Ordinal) ||
            !string.Equals(capture.BuildCommit, restoration.BuildCommit, StringComparison.Ordinal))
        {
            refusals.Add(
                $"The replay ran on {capture.BuildVersion} ({capture.BuildCommit}) and the restore on " +
                $"{restoration.BuildVersion} ({restoration.BuildCommit}). Two builds cannot answer one question " +
                "about one build.");
        }

        if (!string.Equals(capture.SaveSha256, restoration.SaveSha256, StringComparison.Ordinal))
        {
            refusals.Add(
                "The restore read a different save than the replay wrote. Refusing: the two states would be " +
                "of two different runs.");
        }

        var comparisons = restoration.Stages
            .Select(stage => CompareStage(capture, stage))
            .ToList();

        // A digest is one string and the fields are what it is a digest of. They cannot
        // disagree unless something is projecting or hashing differently on the two
        // sides, which would make every other number here meaningless.
        foreach (var comparison in comparisons.Where(c => c.DigestsAgree != (c.DifferingFields.Count == 0)))
        {
            refusals.Add(
                $"At stage '{comparison.Stage}' the digests " +
                $"{(comparison.DigestsAgree ? "agree" : "differ")} while the fields " +
                $"{(comparison.DifferingFields.Count == 0 ? "agree" : "differ")}. One of the two sides is not " +
                "hashing the state it reported; refusing to draw a conclusion from either.");
        }

        var restorable = refusals.Count == 0 && comparisons.Any(comparison => comparison.DigestsAgree);
        return new SnapshotRestoreReport(
            Schema: ReportSchema,
            Manifest: manifestFileName,
            RunId: capture.RunId,
            BuildVersion: capture.BuildVersion,
            BuildCommit: capture.BuildCommit,
            CombatStartSeq: capture.CombatStartSeq,
            SaveSchemaVersion: capture.SaveSchemaVersion,
            SaveSha256: capture.SaveSha256,
            SaveByteCount: capture.SaveByteCount,
            ReplayedDigest: capture.Digest,
            ReplayedActRoomSet: capture.ActRoomSet,
            ReplayedFieldCount: capture.CanonicalFields.Count,
            RoomSetReadableOnBothSides: capture.ActRoomSet.Present &&
                                        restoration.Stages.All(stage => stage.ActRoomSet.Present),
            RestoreSteps: restoration.Steps,
            SkippedPresentationSteps: restoration.SkippedPresentationSteps,
            Stages: comparisons,
            Refusals: refusals,
            Answer: Answer(refusals, comparisons),
            Restorable: restorable);
    }

    private static SnapshotRestoreStageComparison CompareStage(
        SnapshotRestoreCapture capture, SnapshotRestoreStage stage)
    {
        var differences = capture.CanonicalFields.Keys
            .Concat(stage.CanonicalFields.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(field => new SnapshotRestoreFieldDifference(
                field,
                capture.CanonicalFields.GetValueOrDefault(field, Absent),
                stage.CanonicalFields.GetValueOrDefault(field, Absent)))
            .Where(difference => !string.Equals(difference.Replayed, difference.Restored, StringComparison.Ordinal))
            .ToList();

        return new SnapshotRestoreStageComparison(
            Stage: stage.Name,
            Description: stage.Description,
            RestoredDigest: stage.Digest,
            RestoredActRoomSet: stage.ActRoomSet,
            RestoredFieldCount: stage.CanonicalFields.Count,
            AgreeingFieldCount: capture.CanonicalFields.Count(field =>
                stage.CanonicalFields.TryGetValue(field.Key, out var other) &&
                string.Equals(field.Value, other, StringComparison.Ordinal)),
            DifferingFields: differences,
            DigestsAgree: string.Equals(capture.Digest, stage.Digest, StringComparison.Ordinal));
    }

    /// <summary>What the probe is entitled to say, in the words the decision it feeds
    /// needs. Never "close enough": a boundary either restores or it does not.</summary>
    private static string Answer(
        IReadOnlyList<string> refusals, IReadOnlyList<SnapshotRestoreStageComparison> comparisons)
    {
        if (refusals.Count > 0)
        {
            return "No answer. The comparison was refused before the digests were read; see refusals.";
        }

        if (comparisons.FirstOrDefault(comparison => comparison.DigestsAgree) is { } agreeing)
        {
            return
                "A run serialized at combat start and restored in a fresh process through the game's own save " +
                $"format produces the same canonical state at stage '{agreeing.Stage}', field for field. A " +
                "boundary cache may store the save.";
        }

        var detail = string.Join(
            ", ",
            comparisons.Select(comparison =>
                $"{comparison.Stage}: {comparison.DifferingFields.Count} field(s)"));
        return
            "A run serialized at combat start and restored in a fresh process does not produce the same " +
            $"canonical state at any stage of the retail continue-run sequence ({detail}). A boundary cache " +
            "that stored the save would be storing a different run, so the boundary has to keep being " +
            "re-derived by replaying the history that produced it.";
    }

    private const string Absent = "<absent>";

    /// <summary>
    /// Whether the projection could read the act's room set on this side.
    ///
    /// Read from the state rather than from the engine, because the state is what a
    /// digest is computed over: the question is not whether <c>_rooms</c> existed at
    /// some moment, it is whether the thing being compared contains it.
    /// </summary>
    public static ActRoomSetReading RoomSetReading(CanonicalState state)
    {
        if (state.Fields.TryGetValue(RoomSetSentinelField, out var sentinel))
        {
            return new ActRoomSetReading(false, sentinel, []);
        }

        var missing = RoomSetFields.Where(field => !state.Fields.ContainsKey(field)).ToList();
        return missing.Count > 0
            ? new ActRoomSetReading(false, "incomplete", missing)
            : new ActRoomSetReading(true, "present", []);
    }

    private static T Step<T>(List<SnapshotRestoreStep> steps, string name, Func<T> call)
    {
        try
        {
            var result = call();
            steps.Add(new SnapshotRestoreStep(name, "ran", null));
            return result;
        }
        catch (Exception ex)
        {
            steps.Add(new SnapshotRestoreStep(name, "threw", $"{ex.GetType().Name}: {ex.Message}"));
            throw new EngineException(
                $"The restore sequence's step '{name}' failed: {ex.GetType().Name}: {ex.Message}. Refusing to " +
                "project a half-restored run: its state would be neither the save's nor the replay's.");
        }
    }

    private static string Sha256(string content) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}

/// <summary>Whether one side's canonical state carries the act's generated content,
/// and what it says instead when it does not.</summary>
public sealed record ActRoomSetReading(
    [property: JsonPropertyName("present")] bool Present,
    [property: JsonPropertyName("reading")] string Reading,
    [property: JsonPropertyName("missing_fields")] IReadOnlyList<string> MissingFields);

public sealed record SnapshotRestoreStep(
    [property: JsonPropertyName("call")] string Call,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("detail")] string? Detail);

public sealed record SnapshotRestoreFieldDifference(
    [property: JsonPropertyName("field")] string Field,
    [property: JsonPropertyName("replayed")] string Replayed,
    [property: JsonPropertyName("restored")] string Restored);

public sealed record SnapshotRestoreCapture(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("build_version")] string BuildVersion,
    [property: JsonPropertyName("build_commit")] string BuildCommit,
    [property: JsonPropertyName("combat_start_seq")] int CombatStartSeq,
    [property: JsonPropertyName("replay_status")] string ReplayStatus,
    [property: JsonPropertyName("save_schema_version")] int SaveSchemaVersion,
    [property: JsonPropertyName("save_sha256")] string SaveSha256,
    [property: JsonPropertyName("save_byte_count")] int SaveByteCount,
    [property: JsonPropertyName("act_room_set")] ActRoomSetReading ActRoomSet,
    [property: JsonPropertyName("digest")] string Digest,
    [property: JsonPropertyName("canonical_fields")] IReadOnlyDictionary<string, string> CanonicalFields);

public sealed record SnapshotRestoreStage(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("act_room_set")] ActRoomSetReading ActRoomSet,
    [property: JsonPropertyName("digest")] string Digest,
    [property: JsonPropertyName("canonical_fields")] IReadOnlyDictionary<string, string> CanonicalFields);

public sealed record SnapshotRestoreStageComparison(
    [property: JsonPropertyName("stage")] string Stage,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("restored_digest")] string RestoredDigest,
    [property: JsonPropertyName("restored_act_room_set")] ActRoomSetReading RestoredActRoomSet,
    [property: JsonPropertyName("restored_field_count")] int RestoredFieldCount,
    [property: JsonPropertyName("agreeing_field_count")] int AgreeingFieldCount,
    [property: JsonPropertyName("differing_fields")] IReadOnlyList<SnapshotRestoreFieldDifference> DifferingFields,
    [property: JsonPropertyName("digests_agree")] bool DigestsAgree);

public sealed record SnapshotRestoreRestoration(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("build_version")] string BuildVersion,
    [property: JsonPropertyName("build_commit")] string BuildCommit,
    [property: JsonPropertyName("save_schema_version")] int SaveSchemaVersion,
    [property: JsonPropertyName("save_sha256")] string SaveSha256,
    [property: JsonPropertyName("steps")] IReadOnlyList<SnapshotRestoreStep> Steps,
    [property: JsonPropertyName("skipped_presentation_steps")] IReadOnlyList<string> SkippedPresentationSteps,
    [property: JsonPropertyName("stages")] IReadOnlyList<SnapshotRestoreStage> Stages);

public sealed record SnapshotRestoreReport(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("manifest")] string Manifest,
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("build_version")] string BuildVersion,
    [property: JsonPropertyName("build_commit")] string BuildCommit,
    [property: JsonPropertyName("combat_start_seq")] int CombatStartSeq,
    [property: JsonPropertyName("save_schema_version")] int SaveSchemaVersion,
    [property: JsonPropertyName("save_sha256")] string SaveSha256,
    [property: JsonPropertyName("save_byte_count")] int SaveByteCount,
    [property: JsonPropertyName("replayed_digest")] string ReplayedDigest,
    [property: JsonPropertyName("replayed_act_room_set")] ActRoomSetReading ReplayedActRoomSet,
    [property: JsonPropertyName("replayed_field_count")] int ReplayedFieldCount,
    [property: JsonPropertyName("room_set_readable_on_both_sides")] bool RoomSetReadableOnBothSides,
    [property: JsonPropertyName("restore_steps")] IReadOnlyList<SnapshotRestoreStep> RestoreSteps,
    [property: JsonPropertyName("skipped_presentation_steps")] IReadOnlyList<string> SkippedPresentationSteps,
    [property: JsonPropertyName("stages")] IReadOnlyList<SnapshotRestoreStageComparison> Stages,
    [property: JsonPropertyName("refusals")] IReadOnlyList<string> Refusals,
    [property: JsonPropertyName("answer")] string Answer,
    [property: JsonPropertyName("restorable")] bool Restorable);

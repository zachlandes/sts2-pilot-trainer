using System.Text;
using System.Text.Json.Serialization;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Takes the game's own run save at a floor arrival, by replaying the history that
/// reaches it and collecting the save the engine asks for on the way.
///
/// The capture half of the floor-entry snapshot cache. It produces a candidate and
/// judges nothing about whether the candidate works: that is settled by restoring it in
/// a process that replayed nothing and comparing the state it reaches against the
/// digest the recording declares, which is <see cref="RecordedFightEntry.RestoreHeadless"/>
/// followed by <see cref="RecordedFightEntry.VerifyBoundary"/>. Nothing reaches the
/// cache until that has happened, so a save that would not restore is never a file a
/// consumer could find.
///
/// Three refusals are collected here rather than downstream, because each of them makes
/// the restore's answer mean something other than it appears to. A replay that did not
/// reproduce the recording's own declared digest is not standing at the recording's
/// boundary at all, so what it saved is a different run. A state whose act room set
/// degraded to the projection's <c>"unavailable"</c> sentinel would agree with any other
/// such state exactly while saying nothing about either. And an arrival where no fight
/// is live is the one shape of floor entry the measurement found does not restore -
/// see <see cref="FloorEntrySnapshotEligibility"/>.
/// </summary>
public static class FloorEntrySnapshotCapture
{
    public const string Schema = "sts2-pilot-trainer/floor-entry-snapshot/capture/v1";

    /// <summary>
    /// Replays the recording to the floor arrival and keeps the save the game took
    /// there.
    /// </summary>
    /// <param name="savePath">Where the candidate save is written. A quarantine path,
    /// not the cache: the cache is for saves a restore has already reproduced.</param>
    public static FloorEntrySnapshotCandidate Capture(
        ReplayManifest manifest, FloorEntryPlan plan, string savePath)
    {
        var collected = new List<InterceptedRunSave>();
        ArbiterOutcome outcome;
        using (RunSaveInterception.Collect(collected.Add))
        {
            outcome = Arbiter.Run(manifest, stopAfterSeq: plan.BoundarySeq);
        }

        if (outcome.Report.Status is not (VerificationStatus.Verified or VerificationStatus.Partial))
        {
            throw new EngineException(
                $"Replaying {manifest.RunId} to action {plan.BoundarySeq} did not succeed " +
                $"({outcome.Report.Status}), so there is no boundary to snapshot. Snapshotting whatever the " +
                "engine was left holding would cache a different run.\n" +
                string.Join("\n", outcome.Report.Diagnostics));
        }

        var state = outcome.FinalState
            ?? throw new EngineException("The replay produced no engine state to snapshot.");

        var refusals = new List<string>();
        var declared = manifest.BoundaryAt(plan.Kind, floor: plan.Floor)?.Digest
            ?? throw new ManifestException(
                $"This recording declares no boundary for {plan.Describe()}, so a snapshot taken there would " +
                "have nothing to be verified against.");

        var digest = state.Digest();
        if (!string.Equals(digest, declared.Value, StringComparison.Ordinal))
        {
            refusals.Add(
                $"Replaying this recording's own history to {plan.Describe()} produced {digest} and the " +
                $"recording declares {declared.Value}. The run the save was taken from is not the run the " +
                "recording describes, so caching it would cache the disagreement.");
        }

        var roomSet = SnapshotRestoreProbe.RoomSetReading(state);
        if (!roomSet.Present)
        {
            refusals.Add(
                $"The replayed state's act room set is '{roomSet.Reading}': the engine's private _rooms field " +
                "could not be read, so the act's generated content is absent from the state a restore would " +
                "be compared against. Two states that both degrade to that sentinel agree on it exactly.");
        }

        if (FloorEntrySnapshotEligibility.Refusal(state.Fields) is { } ineligible)
        {
            refusals.Add(ineligible);
        }

        var chosen = collected.Count > 0 ? collected[^1] : null;
        if (chosen is null)
        {
            refusals.Add(
                "The game asked to save nothing while this history was replayed, so there is no floor-entry " +
                "save to keep. The retail client takes one inside EnterMapPointInternal; a history that " +
                "reached this arrival without one is not a history this host can snapshot.");
        }
        else if (!chosen.IsFloorEntry)
        {
            refusals.Add(
                $"The last save the game took before this arrival carries a finished {chosen.PreFinishedRoom} " +
                "room, so it is the save taken on leaving a room rather than the one taken on arriving at a " +
                "floor. Only the arrival save is a floor-entry snapshot.");
        }

        if (chosen is not null && refusals.Count == 0)
        {
            File.WriteAllText(Sts2PilotTrainer.IO.WorktreePath.Require(savePath), chosen.Json);
        }

        var identity = GameIdentity.Read();
        return new FloorEntrySnapshotCandidate(
            Schema: Schema,
            RunId: manifest.RunId,
            BuildVersion: identity.BuildVersion,
            BuildCommit: identity.Commit,
            Floor: plan.FloorNumber,
            AfterSeq: plan.BoundarySeq,
            ActIndex: state.Fields.GetValueOrDefault("run.act_index", "unknown"),
            ReplayStatus: outcome.Report.Status.ToString(),
            DeclaredDigest: declared.Value,
            DeclaredDigestSource: declared.Source.ToString(),
            ReplayedDigest: digest,
            LiveCombat: state.Fields.GetValueOrDefault(
                FloorEntrySnapshotEligibility.LiveCombatField, "<absent>"),
            ActRoomSet: roomSet,
            SavesTaken: collected.Select(save => save.Describe()).ToList(),
            SaveKept: chosen?.Describe() ?? "none",
            SaveSchemaVersion: chosen?.SchemaVersion ?? 0,
            SaveSha256: chosen is null ? "none" : FloorEntrySnapshot.HashOf(chosen.Json),
            SaveByteCount: chosen is null ? 0 : Encoding.UTF8.GetByteCount(chosen.Json),
            SavePreFinishedRoom: chosen?.PreFinishedRoom ?? "none",
            Refusals: refusals);
    }
}

/// <summary>
/// What one capture produced: the save it kept, the state the replay reached, and every
/// reason that pairing may not be trusted.
///
/// Serialized between two processes, because a restore has to start in one that replayed
/// nothing.
/// </summary>
public sealed record FloorEntrySnapshotCandidate(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("build_version")] string BuildVersion,
    [property: JsonPropertyName("build_commit")] string BuildCommit,
    [property: JsonPropertyName("floor")] int Floor,
    [property: JsonPropertyName("after_seq")] int AfterSeq,
    [property: JsonPropertyName("act_index")] string ActIndex,
    [property: JsonPropertyName("replay_status")] string ReplayStatus,
    [property: JsonPropertyName("declared_digest")] string DeclaredDigest,
    [property: JsonPropertyName("declared_digest_source")] string DeclaredDigestSource,
    [property: JsonPropertyName("replayed_digest")] string ReplayedDigest,
    [property: JsonPropertyName("live_combat")] string LiveCombat,
    [property: JsonPropertyName("act_room_set")] ActRoomSetReading ActRoomSet,
    [property: JsonPropertyName("saves_taken")] IReadOnlyList<string> SavesTaken,
    [property: JsonPropertyName("save_kept")] string SaveKept,
    [property: JsonPropertyName("save_schema_version")] int SaveSchemaVersion,
    [property: JsonPropertyName("save_sha256")] string SaveSha256,
    [property: JsonPropertyName("save_byte_count")] int SaveByteCount,
    [property: JsonPropertyName("save_pre_finished_room")] string SavePreFinishedRoom,
    [property: JsonPropertyName("refusals")] IReadOnlyList<string> Refusals);

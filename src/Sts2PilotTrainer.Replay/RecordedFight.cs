using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// The recording's own line of its fight, as the real engine replayed it: the trace
/// from run start through the end of the first fight, with the identity that ties it
/// to the manifest it came from.
///
/// This exists because the retail client cannot replay. The headless host reproduces
/// the recording's fight through the shipped engine and keeps the trace; inside the
/// game there is one process, one run, and it is the player's. So the recording's
/// side of an in-game comparison is this file, produced by <c>./scripts/arbiter
/// recorded-fight</c> from a fresh replay and shipped beside the manifest. Every value
/// in it is engine-produced, the same provenance as the manifest's combat-start
/// snapshot digest, and it is read only after <see cref="Bind"/> has shown it belongs
/// to the manifest in hand.
///
/// Binding is by identity, not by trust. The run id, the exact history the trace
/// covers and the combat-start snapshot digest all have to agree with the manifest,
/// and the trace has to hold a fight that finished. A file that drifted from its
/// manifest - a re-transcribed action, a different digest - is refused rather than
/// compared against, because a comparison over two fights that are not the same fight
/// states differences that mean nothing.
/// </summary>
public sealed record RecordedFight
{
    public const string Schema = "sts2-pilot-trainer/recorded-fight/v1";

    [JsonPropertyName("schema")]
    public required string SchemaId { get; init; }

    /// <summary>The manifest this fight was replayed from.</summary>
    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    /// <summary>The last action the trace covers: the one that ended the fight.</summary>
    [JsonPropertyName("covered_through_seq")]
    public required int CoveredThroughSeq { get; init; }

    /// <summary>Hash of the manifest's actions up to and including
    /// <see cref="CoveredThroughSeq"/>, so the trace can be shown to be a replay of
    /// exactly this history.</summary>
    [JsonPropertyName("action_history_hash")]
    public required string ActionHistoryHash { get; init; }

    /// <summary>The engine-produced digest of the complete canonical state at the
    /// fight's combat start, re-derived by the replay that produced the trace.</summary>
    [JsonPropertyName("combat_start_snapshot_digest")]
    public required string CombatStartSnapshotDigest { get; init; }

    [JsonPropertyName("trace")]
    public required ReplayTrace Trace { get; init; }

    /// <summary>
    /// Cuts a verified replay's trace down to the recording's first fight and binds
    /// it to the history it replayed.
    /// </summary>
    /// <exception cref="ManifestException">When the trace holds no completed fight.</exception>
    public static RecordedFight From(ReplayManifest manifest, ReplayTrace trace, string combatStartSnapshotDigest)
    {
        var coverage = CombatProjection.CoverageOf(trace);
        if (!coverage.IsCompletedFight)
        {
            throw new ManifestException(coverage.Refusal!);
        }

        var steps = trace.Steps.OrderBy(step => step.Seq).ToList();
        var end = steps.FindIndex(step =>
            step.Seq > coverage.CombatStartSeq!.Value &&
            step.After.GetValueOrDefault("combat.outcome") != "in_progress");
        var covered = steps.Take(end + 1).ToList();
        var through = covered[^1].Seq;

        return new RecordedFight
        {
            SchemaId = Schema,
            RunId = manifest.RunId,
            CoveredThroughSeq = through,
            ActionHistoryHash = SnapshotCacheKey.HashActions(manifest.Actions.Where(action => action.Seq <= through)),
            CombatStartSnapshotDigest = combatStartSnapshotDigest,
            Trace = new ReplayTrace { Steps = covered },
        };
    }

    /// <summary>
    /// Refuses unless this file is the replay of exactly this manifest's fight.
    /// </summary>
    /// <exception cref="ManifestException">On any disagreement.</exception>
    public void Bind(ReplayManifest manifest)
    {
        if (!string.Equals(SchemaId, Schema, StringComparison.Ordinal))
        {
            throw new ManifestException(
                $"The recorded fight's schema is '{SchemaId}', and this build reads '{Schema}'.");
        }

        if (!string.Equals(RunId, manifest.RunId, StringComparison.Ordinal))
        {
            throw new ManifestException(
                $"The recorded fight is from run '{RunId}', and the recording is '{manifest.RunId}'.");
        }

        var expectedHash = SnapshotCacheKey.HashActions(manifest.Actions.Where(action => action.Seq <= CoveredThroughSeq));
        if (!string.Equals(ActionHistoryHash, expectedHash, StringComparison.Ordinal))
        {
            throw new ManifestException(
                $"The recorded fight was replayed from a history that is not this recording's through action " +
                $"{CoveredThroughSeq}: it hashes to {ActionHistoryHash}, the recording's hashes to {expectedHash}. " +
                "Re-run recorded-fight against the current manifest.");
        }

        var expectedDigest = manifest.Source.CombatStartSnapshotDigest?.Value;
        if (string.IsNullOrWhiteSpace(expectedDigest))
        {
            throw new ManifestException(
                "The recording has no engine-produced combat-start snapshot digest, so nothing can be bound to it.");
        }

        if (!string.Equals(CombatStartSnapshotDigest, expectedDigest, StringComparison.Ordinal))
        {
            throw new ManifestException(
                $"The recorded fight's combat-start snapshot digest is {CombatStartSnapshotDigest} and the " +
                $"recording declares {expectedDigest}, so they are not the same fight from the same boundary.");
        }

        var coverage = CombatProjection.CoverageOf(Trace);
        if (!coverage.IsCompletedFight)
        {
            throw new ManifestException("The recorded fight does not hold a completed fight: " + coverage.Refusal);
        }
    }

    /// <summary>The recording's line, as the comparison contract reads it.</summary>
    public CombatProjection Projection() => CombatProjection.FromTrace(RunId, Trace, CombatStartSnapshotDigest);

    public string Serialize() => JsonSerializer.Serialize(this, ManifestJson.Options);

    public static RecordedFight Deserialize(string json) =>
        ManifestJson.DeserializeRequired<RecordedFight>(json, "Recorded fight");

    public static RecordedFight Load(string path) => Deserialize(File.ReadAllText(path));

    public void Save(string path) => File.WriteAllText(path, Serialize() + "\n");
}

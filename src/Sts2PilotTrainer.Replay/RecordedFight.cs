using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// The recording's own line of every fight in it, as the real engine replayed them:
/// the trace through the end of each fight, with the identity that ties the set to
/// the manifest it came from.
///
/// This exists because the retail client cannot replay. The headless host reproduces
/// the recording's fights through the shipped engine and keeps the traces; inside the
/// game there is one process, one run, and it is the player's. So the recording's
/// side of an in-game comparison is this file, produced by <c>./scripts/arbiter
/// recorded-fight</c> from a fresh replay and shipped beside the manifest. Every value
/// in it is engine-produced, the same provenance as a manifest boundary's snapshot
/// digest, and it is read only after <see cref="Bind"/> has shown it belongs to the
/// manifest in hand.
///
/// It holds a list because a recording is a whole run and a player can be stood in
/// any fight of it. The set and the manifest's
/// <see cref="ReplayManifest.Boundaries"/> are two readings of the same run and each
/// fight is bound to the boundary of the same ordinal, so a file that drifted from
/// its manifest is refused rather than compared against.
/// </summary>
public sealed record RecordedFights
{
    public const string Schema = "sts2-pilot-trainer/recorded-fights/v2";

    /// <summary>The single-fight file this replaced. Named so a build that meets one
    /// can say what it is instead of calling it unrecognisable.</summary>
    public const string RetiredSchema = "sts2-pilot-trainer/recorded-fight/v1";

    [JsonPropertyName("schema")]
    public required string SchemaId { get; init; }

    /// <summary>The manifest these fights were replayed from.</summary>
    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    /// <summary>Each fight of the recording that was cut, in run order.</summary>
    [JsonPropertyName("fights")]
    public required IReadOnlyList<RecordedFight> Fights { get; init; }

    /// <summary>
    /// Cuts a verified replay's trace into the recording's fights and binds them to
    /// the history they replayed.
    /// </summary>
    /// <param name="manifest">The recording these came from.</param>
    /// <param name="trace">A verified replay's whole-run trace.</param>
    /// <param name="digests">
    /// The combat-start snapshot digest of each fight to cut, by ordinal. Exactly
    /// these fights are cut: a fight nothing derived a digest for cannot be identified
    /// and so cannot be compared against, and cutting it anyway would put a line in
    /// the file that no <see cref="Bind"/> could ever accept.
    /// </param>
    /// <exception cref="ManifestException">
    /// When the trace holds no completed fight, or when a named fight is not one the
    /// trace holds and finished.
    /// </exception>
    public static RecordedFights From(
        ReplayManifest manifest, ReplayTrace trace, IReadOnlyDictionary<int, string> digests)
    {
        var coverage = RunCoverage.Of(trace);
        var finished = coverage.Fights.Where(fight => fight.Finished).ToList();
        if (finished.Count == 0)
        {
            throw new ManifestException(CombatProjection.CoverageOf(trace).Refusal
                ?? "This trace holds no completed fight, so the recording has no line to record.");
        }

        if (digests.Count == 0)
        {
            throw new ManifestException(
                "No combat-start snapshot digest was supplied, so none of this replay's fights can be " +
                "identified. A fight cut without one is a line no comparison could show was the recorded one.");
        }

        var steps = trace.Steps.OrderBy(step => step.Seq).ToList();
        var fights = new List<RecordedFight>();
        foreach (var (ordinal, digest) in digests.OrderBy(entry => entry.Key))
        {
            var fight = finished.FirstOrDefault(candidate => candidate.Fight == ordinal)
                ?? throw new ManifestException(
                    $"A digest was supplied for fight {ordinal.ToString(CultureInfo.InvariantCulture)} and this " +
                    $"replay holds no completed fight with that ordinal. It holds " +
                    $"{string.Join(", ", finished.Select(f => f.Fight.ToString(CultureInfo.InvariantCulture)))}.");

            if (string.IsNullOrWhiteSpace(digest))
            {
                throw new ManifestException(
                    $"Fight {ordinal.ToString(CultureInfo.InvariantCulture)}'s combat-start snapshot digest is " +
                    "empty, so nothing could show a comparison was against the recorded fight.");
            }

            var covered = steps
                .Where(step => step.Seq >= fight.CombatStartSeq && step.Seq <= fight.EndSeq!.Value)
                .ToList();

            fights.Add(new RecordedFight
            {
                Fight = fight.Fight,
                CombatStartSeq = fight.CombatStartSeq,
                CoveredThroughSeq = fight.EndSeq!.Value,
                ActionHistoryHash = SnapshotCacheKey.HashActions(
                    manifest.Actions.Where(action => action.Seq <= fight.EndSeq!.Value)),
                CombatStartSnapshotDigest = digest,
                Trace = new ReplayTrace { Steps = covered },
            });
        }

        return new RecordedFights { SchemaId = Schema, RunId = manifest.RunId, Fights = fights };
    }

    /// <summary>
    /// Refuses unless this file is the replay of exactly this manifest's fights.
    ///
    /// The schema and the run are this file's to answer for; everything else is each
    /// fight's, and <see cref="RecordedFight.Bind"/> answers it there so that a file
    /// carrying five fights refuses on the one that drifted rather than on the set.
    /// </summary>
    /// <exception cref="ManifestException">On any disagreement.</exception>
    public void Bind(ReplayManifest manifest)
    {
        if (!string.Equals(SchemaId, Schema, StringComparison.Ordinal))
        {
            throw new ManifestException(
                string.Equals(SchemaId, RetiredSchema, StringComparison.Ordinal)
                    ? $"This is a single-fight recorded-fight file ('{RetiredSchema}'), and this build reads " +
                      $"'{Schema}', which holds every fight of a recording. Re-run recorded-fight to produce one."
                    : $"The recorded fights' schema is '{SchemaId}', and this build reads '{Schema}'.");
        }

        if (!string.Equals(RunId, manifest.RunId, StringComparison.Ordinal))
        {
            throw new ManifestException(
                $"The recorded fights are from run '{RunId}', and the recording is '{manifest.RunId}'.");
        }

        if (Fights.Count == 0)
        {
            throw new ManifestException(
                "The recorded fights file holds no fight, so there is nothing for a player's fight to be " +
                "compared against.");
        }

        foreach (var fight in Fights) fight.Bind(manifest);
    }

    /// <summary>The fight with this ordinal, or a refusal naming what the file holds.</summary>
    public RecordedFight Fight(int fight) =>
        Fights.FirstOrDefault(entry => entry.Fight == fight)
        ?? throw new ManifestException(
            $"This recording's replayed fights are " +
            $"{string.Join(", ", Fights.Select(entry => entry.Fight.ToString(CultureInfo.InvariantCulture)))}, " +
            $"and fight {fight.ToString(CultureInfo.InvariantCulture)} is not among them.");

    /// <summary>One fight's line, as the comparison contract reads it.</summary>
    public CombatProjection Projection(int fight = 1) => Fight(fight).Projection(RunId);

    public string Serialize() => JsonSerializer.Serialize(this, ManifestJson.Options);

    public static RecordedFights Deserialize(string json) =>
        ManifestJson.DeserializeRequired<RecordedFights>(json, "Recorded fights");

    public static RecordedFights Load(string path) => Deserialize(File.ReadAllText(path));

    public void Save(string path) => File.WriteAllText(path, Serialize() + "\n");
}

/// <summary>
/// One fight of a recording, as the engine replayed it.
///
/// Bound to the manifest by identity rather than by trust: the exact history the
/// trace covers and the combat-start snapshot digest both have to agree with the
/// manifest's boundary of the same ordinal, and the trace has to hold a fight that
/// finished. A fight that drifted from its manifest - a re-transcribed action, a
/// different digest - is refused rather than compared against, because a comparison
/// over two fights that are not the same fight states differences that mean nothing.
/// </summary>
public sealed record RecordedFight
{
    /// <summary>Its ordinal in the run, counting from 1.</summary>
    [JsonPropertyName("fight")]
    public required int Fight { get; init; }

    /// <summary>The sequence number after which this fight was live.</summary>
    [JsonPropertyName("combat_start_seq")]
    public required int CombatStartSeq { get; init; }

    /// <summary>The last action the trace covers: the one that ended the fight.</summary>
    [JsonPropertyName("covered_through_seq")]
    public required int CoveredThroughSeq { get; init; }

    /// <summary>Hash of the manifest's actions up to and including
    /// <see cref="CoveredThroughSeq"/>, so the trace can be shown to be a replay of
    /// exactly this history.</summary>
    [JsonPropertyName("action_history_hash")]
    public required string ActionHistoryHash { get; init; }

    /// <summary>The engine-produced digest of the complete canonical state at this
    /// fight's combat start, re-derived by the replay that produced the trace.</summary>
    [JsonPropertyName("combat_start_snapshot_digest")]
    public required string CombatStartSnapshotDigest { get; init; }

    [JsonPropertyName("trace")]
    public required ReplayTrace Trace { get; init; }

    /// <summary>
    /// Refuses unless this is the replay of exactly this fight of this manifest.
    /// </summary>
    /// <exception cref="ManifestException">On any disagreement.</exception>
    public void Bind(ReplayManifest manifest)
    {
        var expectedHash = SnapshotCacheKey.HashActions(
            manifest.Actions.Where(action => action.Seq <= CoveredThroughSeq));
        if (!string.Equals(ActionHistoryHash, expectedHash, StringComparison.Ordinal))
        {
            throw new ManifestException(
                $"Fight {Fight.ToString(CultureInfo.InvariantCulture)} was replayed from a history that is not " +
                $"this recording's through action {CoveredThroughSeq.ToString(CultureInfo.InvariantCulture)}: " +
                $"it hashes to {ActionHistoryHash}, the recording's hashes to {expectedHash}. " +
                "Re-run recorded-fight against the current manifest.");
        }

        var boundary = manifest.BoundaryAt(ReplayBoundary.CombatStartKind, fight: Fight);
        if (boundary is null)
        {
            throw new ManifestException(
                $"The recording declares no combat-start boundary for fight " +
                $"{Fight.ToString(CultureInfo.InvariantCulture)}, so nothing can be bound to it.");
        }

        if (boundary.AfterSeq != CombatStartSeq)
        {
            throw new ManifestException(
                $"Fight {Fight.ToString(CultureInfo.InvariantCulture)} was cut from action " +
                $"{CombatStartSeq.ToString(CultureInfo.InvariantCulture)} and the recording puts its boundary " +
                $"after action {boundary.AfterSeq.ToString(CultureInfo.InvariantCulture)}, so they are not the " +
                "same fight.");
        }

        if (!string.Equals(CombatStartSnapshotDigest, boundary.Digest.Value, StringComparison.Ordinal))
        {
            throw new ManifestException(
                $"Fight {Fight.ToString(CultureInfo.InvariantCulture)}'s combat-start snapshot digest is " +
                $"{CombatStartSnapshotDigest} and the recording declares {boundary.Digest.Value}, so they are " +
                "not the same fight from the same boundary.");
        }

        var coverage = CombatProjection.CoverageOf(Trace);
        if (!coverage.IsCompletedFight)
        {
            throw new ManifestException(
                $"Fight {Fight.ToString(CultureInfo.InvariantCulture)} does not hold a completed fight: " +
                coverage.Refusal);
        }

        var traceFights = RunCoverage.Of(Trace).Fights;
        if (traceFights.Count != 1 ||
            traceFights[0].CombatStartSeq != CombatStartSeq ||
            traceFights[0].EndSeq != CoveredThroughSeq)
        {
            var actualCoverage = traceFights.Count == 1
                ? $"actions {traceFights[0].CombatStartSeq.ToString(CultureInfo.InvariantCulture)} through " +
                  $"{traceFights[0].EndSeq?.ToString(CultureInfo.InvariantCulture) ?? "an unfinished fight"}"
                : $"{traceFights.Count.ToString(CultureInfo.InvariantCulture)} fights";
            throw new ManifestException(
                $"Fight {Fight.ToString(CultureInfo.InvariantCulture)} says its trace covers actions " +
                $"{CombatStartSeq.ToString(CultureInfo.InvariantCulture)} through " +
                $"{CoveredThroughSeq.ToString(CultureInfo.InvariantCulture)}, but the trace holds " +
                $"{actualCoverage}.");
        }
    }

    /// <summary>This fight's line, as the comparison contract reads it.</summary>
    public CombatProjection Projection(string runId) =>
        CombatProjection.FromTrace(runId, Trace, CombatStartSnapshotDigest);
}

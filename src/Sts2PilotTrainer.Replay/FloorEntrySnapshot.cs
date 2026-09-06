using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sts2PilotTrainer.IO;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// A materialised floor-entry snapshot: the game's own run save at a floor arrival,
/// stored beside the key of the history that produced it.
///
/// It is a derived cache and never a source of truth, which is what keeps
/// docs/native-replay-format.md's rule intact rather than bent. Three things are what
/// make that more than a claim. The key is the whole ordered history that produced the
/// state, so a save can never be served for a run that would not actually reach it.
/// The digest recorded here is the one a restore has to reproduce, and a restore that
/// does not is refused rather than trusted. And nothing is written into the cache until
/// a restore in a fresh process has already reproduced it, so an unverified save is
/// never a file a consumer could find.
///
/// The bytes of the save are deliberately not part of its identity. The game stamps
/// <c>save_time</c>, <c>run_time</c> and <c>start_time</c> into every save it writes,
/// so two captures of the same floor entry differ in exactly those three leaves and
/// agree everywhere else. A cache addressed by the save's own hash would therefore miss
/// every time; the history is what addresses it, and <see cref="SaveSha256"/> is an
/// integrity check on this one file rather than a name for its content.
/// </summary>
public sealed record FloorEntrySnapshot(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("run_id")] string RunId,
    [property: JsonPropertyName("build_version")] string BuildVersion,
    [property: JsonPropertyName("build_commit")] string BuildCommit,
    [property: JsonPropertyName("boundary_kind")] string BoundaryKind,
    [property: JsonPropertyName("floor")] int Floor,
    [property: JsonPropertyName("after_seq")] int AfterSeq,
    [property: JsonPropertyName("act_index")] string ActIndex,
    [property: JsonPropertyName("declared_digest")] string DeclaredDigest,
    [property: JsonPropertyName("declared_digest_source")] string DeclaredDigestSource,
    [property: JsonPropertyName("verified_digest")] string VerifiedDigest,
    [property: JsonPropertyName("save_file")] string SaveFile,
    [property: JsonPropertyName("save_sha256")] string SaveSha256,
    [property: JsonPropertyName("save_byte_count")] int SaveByteCount,
    [property: JsonPropertyName("save_schema_version")] int SaveSchemaVersion,
    [property: JsonPropertyName("save_pre_finished_room")] string SavePreFinishedRoom,
    [property: JsonPropertyName("key")] SnapshotCacheKey Key)
{
    public const string SchemaId = "sts2-pilot-trainer/floor-entry-snapshot/v1";

    /// <summary>The game's own serialized run, verbatim as the game wrote it.</summary>
    public const string SaveFileName = "run-save.json";

    /// <summary>What this record is called inside a key's cache directory.</summary>
    public const string MetadataFileName = "floor-entry-snapshot.json";

    private static readonly JsonSerializerOptions Format = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// The snapshot cached for this key, or null when none has been materialised here.
    ///
    /// Null covers every way of not having one - no directory, no metadata, no save
    /// beside it - because a consumer's answer to all three is the same: replay the
    /// history instead. A snapshot that is present but wrong is a different thing
    /// entirely and is <see cref="Binds"/>'s question.
    /// </summary>
    public static FloorEntrySnapshot? Read(SnapshotCacheKey key, string cacheRoot)
    {
        var directory = key.ResolveCacheDirectory(cacheRoot);
        if (!Directory.Exists(directory)) return null;

        var metadataPath = SnapshotCacheKey.ResolveCacheArtifact(directory, MetadataFileName);
        if (!File.Exists(metadataPath)) return null;

        var snapshot = JsonSerializer.Deserialize<FloorEntrySnapshot>(
                           File.ReadAllText(metadataPath), Format)
                       ?? throw new ManifestException(
                           $"The floor-entry snapshot at {metadataPath} could not be read.");

        return File.Exists(snapshot.SavePathIn(directory)) ? snapshot : null;
    }

    /// <summary>
    /// Publishes a verified snapshot into the cache: the save first, then the metadata
    /// that says it was verified.
    ///
    /// That order is the point rather than an implementation detail. <see cref="Read"/>
    /// finds a snapshot only when both files are there, so a crash between the two
    /// leaves a cache with nothing in it rather than a metadata record pointing at a
    /// save that was never written.
    /// </summary>
    public string WriteInto(string cacheRoot, string saveJson)
    {
        var directory = Key.ResolveCacheDirectory(cacheRoot);
        Directory.CreateDirectory(directory);

        AtomicFile.WriteAllText(SavePathIn(directory), saveJson);
        AtomicFile.WriteAllText(
            SnapshotCacheKey.ResolveCacheArtifact(directory, MetadataFileName),
            JsonSerializer.Serialize(this, Format) + "\n");

        return directory;
    }

    /// <summary>Where this snapshot's save sits inside its own cache directory.</summary>
    public string SavePathIn(string cacheDirectory) =>
        SnapshotCacheKey.ResolveCacheArtifact(cacheDirectory, SaveFile);

    /// <summary>
    /// Every reason this cached snapshot is not the one a plan asked for, or nothing.
    ///
    /// Read before the save is so much as opened. A cache directory is named by a hash
    /// of the key, so finding one is already strong evidence - and "strong evidence"
    /// is not the standard this project uses anywhere else, so each field the key
    /// summarises is compared again in the open, and the digest the recording declares
    /// is compared against the digest this snapshot was verified at. The restore then
    /// re-derives that digest from the bytes, which is the check none of these
    /// replaces.
    /// </summary>
    public IReadOnlyList<string> Binds(
        ReplayManifest recording, IBoundaryPlan plan, string expectedBuildVersion, string expectedBuildCommit)
    {
        var refusals = new List<string>();

        if (!string.Equals(Schema, SchemaId, StringComparison.Ordinal))
        {
            refusals.Add($"The cached snapshot declares schema '{Schema}' and this reader knows '{SchemaId}'.");
        }

        if (!string.Equals(RunId, recording.RunId, StringComparison.Ordinal))
        {
            refusals.Add($"The cached snapshot is of run '{RunId}' and this recording is '{recording.RunId}'.");
        }

        if (Key != plan.SnapshotKey)
        {
            refusals.Add(
                "The cached snapshot's key is not the key of the history this plan replays, so it is a " +
                "snapshot of a different run or a different prefix of this one.");
        }

        if (!string.Equals(BoundaryKind, plan.Kind, StringComparison.Ordinal) || Floor != plan.Floor ||
            AfterSeq != plan.BoundarySeq)
        {
            refusals.Add(
                $"The cached snapshot is {BoundaryKind} floor {Text(Floor)} after action {Text(AfterSeq)}, and " +
                $"this plan reaches {plan.Kind} floor {Text(plan.Floor)} after action {Text(plan.BoundarySeq)}.");
        }

        if (!string.Equals(BuildVersion, expectedBuildVersion, StringComparison.Ordinal) ||
            !string.Equals(BuildCommit, expectedBuildCommit, StringComparison.Ordinal))
        {
            refusals.Add(
                $"The cached snapshot was verified on {BuildVersion} ({BuildCommit}) and this game is " +
                $"{expectedBuildVersion} ({expectedBuildCommit}). A save is a build's own format.");
        }

        var declared = recording.BoundaryAt(plan.Kind, fight: plan.Fight, floor: plan.Floor)?.Digest.Value;
        if (declared is null)
        {
            refusals.Add(
                $"This recording declares no boundary for {plan.Describe()}, so there is nothing for a " +
                "restored state to be proved equal to.");
        }
        else if (!string.Equals(declared, VerifiedDigest, StringComparison.Ordinal))
        {
            refusals.Add(
                $"The recording declares {declared} at {plan.Describe()} and the cached snapshot was verified " +
                $"at {VerifiedDigest}. The recording has been edited since the snapshot was taken, or the " +
                "snapshot is of another run that happens to hash its history the same way.");
        }

        return refusals;
    }

    /// <summary>Whether the save on disk is still the save this record was written
    /// about. A cache is a file somebody can edit; the digest check downstream would
    /// catch an edit that changed the run, and this catches one that did not.</summary>
    public string? SaveIntegrity(string saveJson)
    {
        var actual = HashOf(saveJson);
        return string.Equals(actual, SaveSha256, StringComparison.Ordinal)
            ? null
            : $"The cached save hashes to {actual} and its record says {SaveSha256}. Refusing to restore a " +
              "save that is not the one that was verified.";
    }

    public static string HashOf(string saveJson) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(saveJson)));

    private static string Text(int? value) =>
        value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";
}

/// <summary>
/// Which floor arrivals may be cached as a serialized run, and why the rest may not.
///
/// The rule is one field wide and it is the whole finding of the floor-entry
/// measurement. A floor arrival where a fight is live restores through the retail
/// continue path field for field. A floor arrival where one is not carries the
/// previous fight's finished <c>PlayerCombatState</c> in the live run, and the game's
/// own save has no representation of it, so the restored run is missing seventeen
/// <c>combat.*</c> fields that describe a fight that already ended. Nothing about the
/// run itself differs there - seed, every RNG stream position, deck, relics, gold and
/// the map coordinate all agree - which is exactly why it must be refused by a rule
/// rather than noticed later: it is the most convincing shape a wrong answer has.
///
/// Making those arrivals pass by dropping a finished fight from the projection is a
/// different change with its own migration - it would move every boundary digest in
/// every committed recording, including ones a recorder captured inside a player's own
/// client - and it must not happen as a side effect of wanting a cache to hit. See
/// docs/native-replay-format.md.
///
/// Pure, and asked of the state the replay derived rather than of the manifest, because
/// whether a fight was live at an arrival is a fact about what the engine did.
/// </summary>
public static class FloorEntrySnapshotEligibility
{
    /// <summary>The canonical field that decides it.</summary>
    public const string LiveCombatField = "combat.in_progress";

    /// <summary>Why this arrival cannot be cached, or null when it can.</summary>
    public static string? Refusal(IReadOnlyDictionary<string, string> boundaryFields)
    {
        if (!boundaryFields.TryGetValue(LiveCombatField, out var live))
        {
            return
                $"The state at this arrival has no {LiveCombatField} field at all, so whether a fight is live " +
                "there cannot be read. Only a floor arrival with a live fight is cached.";
        }

        if (string.Equals(live, "true", StringComparison.Ordinal)) return null;

        var outcome = boundaryFields.GetValueOrDefault("combat.outcome", "none");
        return
            $"No fight is live at this arrival ({LiveCombatField}={live}, combat.outcome={outcome}). The " +
            "game's own save carries no combat at all, so a run restored here would be missing the finished " +
            "fight the live run is still carrying - the same run in every other respect, and a different " +
            "canonical state. Only a floor arrival with a live fight is cached; see " +
            "docs/native-replay-format.md.";
    }
}

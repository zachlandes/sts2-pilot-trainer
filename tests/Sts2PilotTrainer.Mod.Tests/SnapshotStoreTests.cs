using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The snapshot cache under the mod's own store: where it is, how a cached entry is
/// read back and bound, and what retention learns about it.
///
/// The arbiter that writes into it is a separate process and is exercised by
/// <c>OwnRunPlaybackTests</c>; what is tested here is everything the mod decides on
/// its own side of that process, against a real directory.
/// </summary>
public sealed class SnapshotStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"runmobile-snapshots-{Guid.NewGuid():N}", "Runmobile", "steam", "test", "profile1");

    public SnapshotStoreTests()
    {
        _ = EngineHost.StartupPhase();
        Directory.CreateDirectory(_root);
        RunmobileStore.UseRootForTesting(_root);
    }

    public void Dispose()
    {
        RunmobileStore.UseRootForTesting(null);
        var sandbox = _root[.._root.IndexOf("Runmobile", StringComparison.Ordinal)];
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
    }

    private static ReplayManifest Recording() =>
        ManifestJson.Deserialize(File.ReadAllText(Path.Combine(
            Arbiter.RepoRoot, "manifests", "native-3LACFJ5NJ371-20260906-015901.replay.json")));

    [Fact]
    public void TheCacheLivesUnderTheStoreAndIsEmptyUntilTheArbiterWrites()
    {
        Assert.StartsWith(RunmobileStore.Root, RunmobileStore.PathOf(SnapshotStore.CacheDirectory), StringComparison.Ordinal);
        Assert.Empty(SnapshotStore.Cached());
        Assert.Equal(0, SnapshotStore.SizeOf(new HashSet<string>([Recording().RunId], StringComparer.Ordinal)));
    }

    [Fact]
    public void ReadingWhereNothingIsCachedSaysSoRatherThanThrowing()
    {
        var recording = Recording();

        Assert.Null(SnapshotStore.Read(recording, FloorEntryPlan.For(recording, 3), out var whyNot));
        Assert.Contains("nothing has been materialised", whyNot, StringComparison.Ordinal);
    }

    /// <summary>
    /// A cached snapshot is read back through the store, bound to the plan and its save
    /// hashed against its record, exactly as the headless command binds one; a fight
    /// plan at the arrival's own action binds the same snapshot.
    /// </summary>
    [Fact]
    public void ACachedSnapshotIsReadBackBoundAndHashed()
    {
        var recording = Recording();
        var arrival = FloorEntryPlan.For(recording, 3);
        var identity = GameIdentity.Read();
        const string saveJson = """{"schema_version":3,"seed":"3LACFJ5NJ371"}""";
        Write(recording, arrival, identity, saveJson);

        var read = SnapshotStore.Read(recording, arrival, out var whyNot);

        Assert.NotNull(read);
        Assert.Equal(string.Empty, whyNot);
        Assert.Equal(saveJson, read.SaveJson);
        Assert.Equal(arrival.BoundarySeq, read.AfterSeq);
        Assert.Equal(3, read.Floor);
        Assert.Equal(recording.CombatStartDigest(2), read.VerifiedDigest);

        Assert.Equal([(SnapshotStore.CacheDirectory + "/" + arrival.SnapshotKey.ToCacheDirectoryName(), recording.RunId)],
            SnapshotStore.Cached());
        Assert.True(SnapshotStore.SizeOf(new HashSet<string>([recording.RunId], StringComparer.Ordinal)) > saveJson.Length);
    }

    [Fact]
    public void ASnapshotWhoseSaveWasEditedIsRefused()
    {
        var recording = Recording();
        var arrival = FloorEntryPlan.For(recording, 3);
        var directory = Write(recording, arrival, GameIdentity.Read(), """{"schema_version":3}""");
        File.WriteAllText(Path.Combine(directory, FloorEntrySnapshot.SaveFileName), """{"schema_version":4}""");

        Assert.Null(SnapshotStore.Read(recording, arrival, out var whyNot));
        Assert.Contains("Refusing to restore", whyNot, StringComparison.Ordinal);
    }

    /// <summary>A directory whose record cannot be read is listed with no run, so a
    /// purge still takes it and a run's removal leaves it alone.</summary>
    [Fact]
    public void ADirectoryWithAnUnreadableRecordIsListedWithNoRun()
    {
        RunmobileStore.Write($"{SnapshotStore.CacheDirectory}/stray/{FloorEntrySnapshot.MetadataFileName}", "not json");

        Assert.Equal([($"{SnapshotStore.CacheDirectory}/stray", (string?)null)], SnapshotStore.Cached());
    }

    /// <summary>
    /// A record this build cannot read is a cache miss, not a refusal.
    ///
    /// Under the plan's own key rather than a stray directory, because this is the one
    /// the press reads: a truncated write or a record from a build whose shape has
    /// moved on would otherwise throw out of the read, past the materialisation that is
    /// the only thing that rewrites it, and refuse every later press the same way.
    /// </summary>
    [Fact]
    public void ARecordThisBuildCannotReadIsACacheMissRatherThanARefusal()
    {
        var recording = Recording();
        var plan = FloorEntryPlan.For(recording, 3);
        RunmobileStore.Write(
            $"{SnapshotStore.CacheDirectory}/{plan.SnapshotKey.ToCacheDirectoryName()}/" +
            FloorEntrySnapshot.MetadataFileName,
            "{\"schema\":");

        Assert.Null(SnapshotStore.Read(recording, plan, out var whyNot));
        Assert.Contains("could not be read", whyNot, StringComparison.Ordinal);
        Assert.Contains(plan.SnapshotKey.ToCacheDirectoryName(), whyNot, StringComparison.Ordinal);
    }

    private static string Write(ReplayManifest recording, FloorEntryPlan plan, GameIdentity identity, string saveJson)
    {
        var declared = recording.BoundaryAt(ReplayBoundary.FloorEntryKind, floor: plan.FloorNumber)!.Digest.Value;
        var snapshot = new FloorEntrySnapshot(
            Schema: FloorEntrySnapshot.SchemaId,
            RunId: recording.RunId,
            BuildVersion: identity.BuildVersion,
            BuildCommit: identity.Commit,
            BoundaryKind: plan.Kind,
            Floor: plan.FloorNumber,
            AfterSeq: plan.BoundarySeq,
            ActIndex: "0",
            DeclaredDigest: declared,
            DeclaredDigestSource: "Engine",
            VerifiedDigest: declared,
            SaveFile: FloorEntrySnapshot.SaveFileName,
            SaveSha256: FloorEntrySnapshot.HashOf(saveJson),
            SaveByteCount: System.Text.Encoding.UTF8.GetByteCount(saveJson),
            SaveSchemaVersion: 3,
            SavePreFinishedRoom: "none",
            Key: plan.SnapshotKey);
        // Written the way the arbiter leaves them, through the store rather than through
        // the worktree-rooted writer, because this process has no workspace under the
        // store the way the arbiter's does.
        var directory = $"{SnapshotStore.CacheDirectory}/{plan.SnapshotKey.ToCacheDirectoryName()}";
        RunmobileStore.Write($"{directory}/{FloorEntrySnapshot.SaveFileName}", saveJson);
        RunmobileStore.Write($"{directory}/{FloorEntrySnapshot.MetadataFileName}", snapshot.Serialize());
        return RunmobileStore.PathOf(directory);
    }
}

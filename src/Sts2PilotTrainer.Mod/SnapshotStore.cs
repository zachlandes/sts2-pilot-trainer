using System.Globalization;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The floor-entry snapshots this mod keeps under its own store, and how one is
/// materialised when a journey needs it.
///
/// The cache is derived state and lives under <c>RunmobileStore</c> like everything
/// else this mod writes - <c>snapshots/cache</c>, keyed by <c>SnapshotCacheKey</c>
/// exactly as the headless cache is, so the same directory name means the same
/// history here and on a developer's machine. The mod itself never writes into it.
/// The packaged arbiter does, in its own process, through the same <c>floor-snapshot</c>
/// command a developer runs: it replays the history to the arrival, restores the save
/// in a fresh process, and writes the cache only once the restored state has
/// reproduced the digest the recording declares. That keeps "the mod has one writer"
/// true in the same way publication already keeps it: the one writer delegates to a
/// subprocess whose workspace is a directory under the store.
///
/// A snapshot found here is still not trusted. It is bound to the plan in the open,
/// its save is hashed against its record, and the run it restores is proved at the
/// boundary the way a walked one is.
///
/// Removal is <see cref="RecordingRetention"/>'s: a recording's snapshots go when the
/// recording goes, and a purge takes them all. Nothing here is ever the run the game
/// can Continue - a snapshot is of the recording's run and never the player's live
/// save - so the continuable-run rule has nothing to say about it.
/// </summary>
internal static class SnapshotStore
{
    /// <summary>The whole subtree, which the arbiter's workspace is pointed at.</summary>
    internal const string Directory = "snapshots";

    /// <summary>Where verified snapshots live, keyed by history.</summary>
    internal const string CacheDirectory = "snapshots/cache";

    /// <summary>Where one materialisation's manifest, evidence and sandbox go while it
    /// runs. Removed afterwards, and swept by retention if the process was interrupted.</summary>
    internal const string WorkDirectory = "snapshots/work";

    /// <summary>A verified save to restore from, and the action it was taken after.</summary>
    internal sealed record RestorableSave(string SaveJson, int AfterSeq, int Floor, string VerifiedDigest);

    /// <summary>
    /// The verified save for this arrival of this recording, materialising it first
    /// where the cache has none.
    ///
    /// Asked at the press and not before, so a row is offered on the history and the
    /// wait is paid only by somebody who chose the row. The cache is read first and
    /// bound; where it does not answer, the arbiter is run once and the cache is read
    /// again, and a second failure is a refusal that quotes what the arbiter said.
    /// </summary>
    internal static async Task<RestorableSave> EnsureAsync(ReplayManifest recording, RestorableArrival arrival)
    {
        var plan = FloorEntryPlan.For(recording, arrival.Floor);
        if (Read(recording, plan, out var whyNot) is { } cached) return cached;

        Log.Info(
            $"[{RunmobileMod.ModId}] no verified snapshot for arrival on floor " +
            $"{arrival.Floor.ToString(CultureInfo.InvariantCulture)} ({whyNot}); asking the packaged arbiter " +
            "to materialise one", 2);

        var result = await MaterialiseAsync(recording, arrival.Floor).ConfigureAwait(false);
        if (Read(recording, plan, out whyNot) is { } materialised) return materialised;

        throw new InvalidOperationException(RefusalFor(result, arrival.Floor, whyNot));
    }

    /// <summary>
    /// Why the arbiter did not leave a snapshot behind, in the words the player who
    /// pressed Continue reads.
    ///
    /// A run that outlived its bound is the one case with nothing to quote: it was
    /// killed mid-replay, so its last few lines are a replay in progress rather than a
    /// reason, and what a player needs to be told is that their run could not be
    /// restored. Every other failure has the arbiter's own account and gives it.
    /// </summary>
    internal static string RefusalFor(PackagedArbiter.Result result, int floor, string whyNot) =>
        result.TimedOut
            ? TrainerCopy.CouldNotRestoreYourRun(PackagedArbiter.SnapshotTimeout.TotalMinutes)
            : $"The packaged arbiter did not produce a verified snapshot for arrival on floor " +
              $"{floor.ToString(CultureInfo.InvariantCulture)} ({whyNot}). It reported: {result.Tail()}";

    /// <summary>The cached snapshot for this plan where one is present, binds and is
    /// intact, or null with the reason.</summary>
    internal static RestorableSave? Read(ReplayManifest recording, FloorEntryPlan plan, out string whyNot)
    {
        if (!System.IO.Directory.Exists(RunmobileStore.PathOf(CacheDirectory)))
        {
            whyNot = "nothing has been materialised on this profile yet";
            return null;
        }

        // Read through the store's own gate rather than FloorEntrySnapshot.Read, whose
        // containment rule is the worktree's: in the client there is no worktree, and
        // the store is the one thing that says where this mod's files may be.
        var directory = $"{CacheDirectory}/{plan.SnapshotKey.ToCacheDirectoryName()}";
        var metadata = RunmobileStore.Read($"{directory}/{FloorEntrySnapshot.MetadataFileName}");
        if (metadata is null)
        {
            whyNot = $"nothing is cached under {plan.SnapshotKey.ToCacheDirectoryName()}";
            return null;
        }

        var snapshot = FloorEntrySnapshot.Parse(metadata, directory);

        var identity = GameIdentity.ReadForCurrentEngine();
        var refusals = snapshot.Binds(recording, plan, identity.BuildVersion, identity.Commit);
        if (refusals.Count > 0)
        {
            whyNot = "the cached snapshot does not bind: " + string.Join(" ", refusals);
            return null;
        }

        var saveJson = RunmobileStore.Read($"{directory}/{snapshot.SaveFile}");
        if (saveJson is null)
        {
            whyNot = "the cached snapshot's save is gone";
            return null;
        }

        if (snapshot.SaveIntegrity(saveJson) is { } damaged)
        {
            whyNot = damaged;
            return null;
        }

        whyNot = string.Empty;
        return new RestorableSave(saveJson, snapshot.AfterSeq, snapshot.Floor, snapshot.VerifiedDigest);
    }

    /// <summary>
    /// Runs <c>floor-snapshot</c> for one arrival, in a workspace of its own under the
    /// store, with the shared cache as its <c>--cache</c>.
    ///
    /// The arbiter's workspace is the whole <c>snapshots</c> subtree, because its
    /// <c>--cache</c> and its <c>--out</c> both have to be inside the workspace it is
    /// given and the cache is shared between presses while the evidence is not.
    /// </summary>
    private static async Task<PackagedArbiter.Result> MaterialiseAsync(ReplayManifest recording, int floor)
    {
        var id = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        var workspace = $"{WorkDirectory}/{id}";
        var storeRoot = RunmobileStore.Root;
        RecordingRetention.BeginDerivedWorkspace(storeRoot, workspace);
        var removeWorkspace = true;
        try
        {
            var relativeManifestPath = $"{workspace}/recording.replay.json";
            RunmobileStore.Write(relativeManifestPath, ManifestJson.Serialize(recording));
            System.IO.Directory.CreateDirectory(RunmobileStore.PathOf(CacheDirectory));

            var start = PackagedArbiter.StartInfo(
                RunmobileStore.PathOf(Directory),
                RunmobileStore.PathOf($"{workspace}/sandbox"),
                "floor-snapshot", RunmobileStore.PathOf(relativeManifestPath),
                "--floor", floor.ToString(CultureInfo.InvariantCulture),
                "--cache", RunmobileStore.PathOf(CacheDirectory),
                "--out", RunmobileStore.PathOf($"{workspace}/evidence"));

            return await Task
                .Run(() => PackagedArbiter.RunAsync(start, PackagedArbiter.SnapshotTimeout))
                .ConfigureAwait(false);
        }
        catch (ArbiterStillRunningException)
        {
            removeWorkspace = false;
            throw;
        }
        finally
        {
            if (removeWorkspace)
                RecordingRetention.RemoveDerivedWorkspace(storeRoot, workspace);
            else
                RecordingRetention.ReleaseDerivedWorkspace(storeRoot, workspace);
        }
    }

    /// <summary>
    /// Every snapshot directory in the cache, with the run it is of, read off the
    /// record the arbiter wrote beside the save.
    ///
    /// For retention, which removes a recording's snapshots with the recording and
    /// counts their bytes with it. A directory whose record cannot be read is listed
    /// with no run rather than skipped, so a purge still takes it.
    /// </summary>
    internal static IReadOnlyList<(string RelativeDirectory, string? RunId)> Cached()
    {
        var cacheRoot = RunmobileStore.PathOf(CacheDirectory);
        if (!System.IO.Directory.Exists(cacheRoot)) return [];

        var cached = new List<(string, string?)>();
        foreach (var directory in System.IO.Directory.EnumerateDirectories(cacheRoot).Order(StringComparer.Ordinal))
        {
            var relative = $"{CacheDirectory}/{Path.GetFileName(directory)}";
            string? runId = null;
            try
            {
                var metadata = RunmobileStore.Read($"{relative}/{FloorEntrySnapshot.MetadataFileName}");
                if (metadata is not null)
                {
                    using var document = JsonDocument.Parse(metadata);
                    if (document.RootElement.TryGetProperty("run_id", out var id)) runId = id.GetString();
                }
            }
            catch (JsonException)
            {
                runId = null;
            }

            cached.Add((relative, runId));
        }

        return cached;
    }

    /// <summary>How many bytes the cached snapshots of these runs occupy, measured
    /// file by file through the store's own gate.</summary>
    internal static long SizeOf(IReadOnlySet<string> runIds)
    {
        long bytes = 0;
        foreach (var (relative, runId) in Cached())
        {
            if (runId is null || !runIds.Contains(runId)) continue;
            foreach (var file in RunmobileStore.ListFileNames(relative))
            {
                bytes += RunmobileStore.SizeOf($"{relative}/{file}");
            }
        }

        return bytes;
    }
}

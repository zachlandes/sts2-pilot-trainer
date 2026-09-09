using System.Text.Json;
using System.Text.Json.Nodes;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The library's own offer, driven to the engine's own entry, over both recordings the
/// recorder wrote of a person's run.
///
/// The loop this repository exists for - record your own run, play it back from any of
/// its fights - was green while broken, because nothing walked the product's offer into
/// the entry: the run view was tested on what it drew, the entry on what it entered, and
/// the two agreed with themselves. These tests take every row the run view offers on the
/// two committed native recordings, build the plan the browser's press builds, and put
/// it through <c>enter-fight</c> by the route <c>RetailPlayback.RouteTo</c> says the
/// client would take - walked, or restored from a snapshot <c>floor-snapshot</c>
/// materialised in the same test - and require <c>ENTERED</c> at the boundary's own
/// digest. The greyed rows are held to be exactly the ones no route reaches.
///
/// Every expectation here is the section-2 table of the fight-walk plan, and it is
/// meant to be edited as the walk is built: a floor between fights moves from
/// <c>Unreachable</c> to <c>RestoreThenWalk</c> the day the client can walk a fight,
/// and that edit is the evidence.
///
/// <para>The committed copies are migrated; the files in a player's store are what
/// the recorder wrote. The first retail proof of the restore route found the
/// difference the hard way - the library offered the version-5 file and the entry
/// refused it - so the same offer is driven from the recorder's own form too, rebuilt
/// from the committed copy by undoing exactly what the migration adds.</para>
/// </summary>
public sealed class OwnRunPlaybackTests
{
    public static TheoryData<string> Recordings =>
    [
        "native-3LACFJ5NJ371-20260906-015901.replay.json",
        "native-9F8CY60C5BK7-20260906-005737.replay.json",
    ];

    private static string ManifestPath(string fileName) => Path.Combine(Arbiter.RepoRoot, "manifests", fileName);

    private static ReplayManifest Load(string fileName) => ManifestJson.Deserialize(File.ReadAllText(ManifestPath(fileName)));

    /// <summary>
    /// The recording as its version-5 recorder wrote it, rebuilt from the committed
    /// migrated copy: the version it stated, no integrity, no migration note, and none
    /// of the arrival checkpoints the derivation added - a derived one is the inferred
    /// checkpoint of that kind, and a recorder's own would be captured.
    /// </summary>
    private static string RecordersOwnWrite(string fileName)
    {
        var document = JsonNode.Parse(File.ReadAllText(ManifestPath(fileName)))!.AsObject();
        var native = document["source"]!["native"]!.AsObject();
        Assert.Equal(ManifestJson.PreviousManifestVersion, native["migrated_from_version"]!.GetValue<int>());
        document["manifest_version"] = ManifestJson.PreviousManifestVersion;
        native.Remove("integrity");
        native.Remove("migrated_from_version");

        var checkpoints = document["checkpoints"]!.AsArray();
        var derived = checkpoints
            .Where(checkpoint =>
                checkpoint!["kind"]!.GetValue<string>() == FloorArrival.CheckpointKind &&
                checkpoint["expect"]!.AsObject().All(field => field.Value!["Source"]!.GetValue<string>() == "Inferred"))
            .ToList();
        Assert.NotEmpty(derived);
        foreach (var checkpoint in derived) checkpoints.Remove(checkpoint);

        var path = Path.Combine(Scratch(Path.Combine(Path.GetFileNameWithoutExtension(fileName), "recorder-wrote")), fileName);
        File.WriteAllText(path, document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        return path;
    }

    /// <summary>The plan the browser builds for a row: a fight's where the position holds
    /// one, else the floor's. The same rule as <c>RunBrowserScreen.Enter</c>.</summary>
    private static IBoundaryPlan PlanFor(ReplayManifest recording, RunViewPosition position) =>
        position.Fight is { } fight ? RecordedFightPlan.For(recording, fight) : FloorEntryPlan.For(recording, position.Floor);

    private static string Scratch(string name)
    {
        var path = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", "own-run-playback", name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// The recorder's own write of the run reads as the committed copy: the same
    /// checkpoints, the same rows offered, the same route to each, and a manifest the
    /// validator passes. This is the half of the tripwire that needs no game, and it is
    /// the half that was missing when the library offered a file the entry refused.
    /// </summary>
    [Theory]
    [MemberData(nameof(Recordings))]
    public void TheRecordersOwnWriteOffersWhatTheCommittedCopyOffers(string fileName)
    {
        var committed = Load(fileName);
        var written = ManifestJson.Load(RecordersOwnWrite(fileName));

        var validation = ManifestValidator.Validate(written);
        Assert.True(validation.IsValid, validation.Describe());
        Assert.Equal(ManifestJson.Serialize(committed), ManifestJson.Serialize(written));

        var expected = RunView.PositionsIn(committed).Select(position => (position.Floor, position.Fight, position.Playable, Route: RetailPlayback.RouteTo(committed, position.AfterSeq).ToString()));
        var actual = RunView.PositionsIn(written).Select(position => (position.Floor, position.Fight, position.Playable, Route: RetailPlayback.RouteTo(written, position.AfterSeq).ToString()));
        Assert.Equal(expected, actual);
        Assert.Contains(RunView.PositionsIn(written), position => position.Playable && position.Fight is > 1);
    }

    /// <summary>
    /// The recorder's own write, driven to the engine's own entry by both routes the
    /// client takes: the first fight walked and the first later fight restored from
    /// the arrival that dealt it, each at the digest the file declares. One fight per
    /// route rather than every fight, because the route tests below already cover
    /// every fight on the migrated copy and the two files read as one manifest.
    /// </summary>
    [GameTheory]
    [MemberData(nameof(Recordings))]
    public void TheRecordersOwnWriteIsEnteredByWalkingAndByRestoring(string fileName)
    {
        var path = RecordersOwnWrite(fileName);
        var scratch = Path.Combine(Path.GetFileNameWithoutExtension(fileName), "recorder-wrote");
        var recording = ManifestJson.Load(path);
        var offered = RunView.PositionsIn(recording).Where(position => position.Playable).ToList();
        var walked = Assert.Single(offered, position => position.Fight == 1);
        var restored = offered.First(position => RetailPlayback.RouteTo(recording, position.AfterSeq) is PlaybackRoute.Restore);

        var walk = EnterFight(path, scratch, PlanFor(recording, walked), restore: false, cache: null, out var walkResult);
        Assert.True(walkResult.Verified, walkResult.All);
        Assert.Equal("replayed", walk.GetProperty("entry_route").GetString());
        Assert.Equal(recording.CombatStartDigest(1), walk.GetProperty("this_game_digest").GetString());

        var cache = Scratch(Path.Combine(scratch, "cache"));
        var snapshot = Arbiter.Run(
            "floor-snapshot", path, "--floor", restored.Floor.ToString(), "--cache", cache,
            "--out", Scratch(Path.Combine(scratch, $"snap-{restored.Floor}")));
        Assert.True(snapshot.ExitCode == 0, snapshot.All);

        var restore = EnterFight(path, scratch, PlanFor(recording, restored), restore: true, cache: cache, out var restoreResult);
        Assert.True(restoreResult.Verified, restoreResult.All);
        Assert.Equal("restored", restore.GetProperty("entry_route").GetString());
        Assert.Empty(restore.GetProperty("steps").EnumerateArray());
        Assert.Equal(recording.CombatStartDigest(restored.Fight!.Value), restore.GetProperty("this_game_digest").GetString());
    }

    /// <summary>
    /// Every row the library offers is one the arbiter enters, by walking. The engine can
    /// walk every verb, so this passes on any build; it pins that the offer and the
    /// engine's own coordinates agree before the route is asked.
    /// </summary>
    [GameTheory]
    [MemberData(nameof(Recordings))]
    public void EveryRowTheLibraryOffersIsOneTheArbiterEnters(string fileName)
    {
        var recording = Load(fileName);
        var offered = RunView.PositionsIn(recording).Where(position => position.Playable).ToList();
        Assert.NotEmpty(offered);

        foreach (var position in offered)
        {
            var plan = PlanFor(recording, position);
            var report = EnterFight(ManifestPath(fileName), Path.GetFileNameWithoutExtension(fileName), plan, restore: false, cache: null, out var result);

            Assert.True(result.Verified, result.All);
            Assert.Equal("replayed", report.GetProperty("entry_route").GetString());
            Assert.Equal(plan.BoundarySeq, report.GetProperty("boundary_seq").GetInt32());
            Assert.Equal(
                recording.BoundaryAt(plan.Kind, fight: plan.Fight, floor: plan.Floor)!.Digest.Value,
                report.GetProperty("this_game_digest").GetString());
        }
    }

    /// <summary>
    /// Every row the library offers has a route the client can take, and the route is
    /// the one the plan measured: the first fight walked, every later fight restored
    /// from the arrival that dealt it, every floor between fights unreachable at the
    /// first card of the fight before it.
    /// </summary>
    [GameTheory]
    [MemberData(nameof(Recordings))]
    public void EveryRowTheLibraryOffersHasARouteTheClientCanTake(string fileName)
    {
        var recording = Load(fileName);
        var positions = RunView.PositionsIn(recording).Where(position => !position.IsRunStart).ToList();

        var restored = 0;
        foreach (var position in positions)
        {
            var route = RetailPlayback.RouteTo(recording, position.AfterSeq);
            switch (position)
            {
                case { Fight: 1 }:
                    Assert.IsType<PlaybackRoute.Walk>(route);
                    Assert.True(position.Playable);
                    break;
                case { Fight: not null }:
                    Assert.IsType<PlaybackRoute.Restore>(route);
                    Assert.True(position.Playable);
                    restored++;
                    break;
                default:
                    var refused = Assert.IsType<PlaybackRoute.Unreachable>(route);
                    Assert.Equal(ActionVerb.PlayCard, refused.Refused.Verb);
                    Assert.False(position.Playable);
                    break;
            }
        }

        // Both recordings have fights past the first; a recording where nothing was
        // restored would be one this test had nothing to say about.
        Assert.True(restored > 0);
    }

    /// <summary>
    /// The restore route, driven through the engine. For every fight the library offers
    /// by restoring, <c>floor-snapshot</c> materialises the arrival's save and
    /// <c>enter-fight --fight n --restore</c> continues it, and the fight opens at the
    /// fight's own combat-start digest without a decision being replayed.
    /// </summary>
    [GameTheory]
    [MemberData(nameof(Recordings))]
    public void EveryFightTheLibraryOffersByRestoringIsEnteredByRestoring(string fileName)
    {
        var recording = Load(fileName);
        var cache = Scratch(Path.Combine(Path.GetFileNameWithoutExtension(fileName), "cache"));
        var restorable = RunView.PositionsIn(recording)
            .Where(position => position.Playable && RetailPlayback.RouteTo(recording, position.AfterSeq) is PlaybackRoute.Restore)
            .ToList();
        Assert.NotEmpty(restorable);

        foreach (var position in restorable)
        {
            var snapshot = Arbiter.Run(
                "floor-snapshot", ManifestPath(fileName), "--floor", position.Floor.ToString(),
                "--cache", cache, "--out", Scratch(Path.Combine(Path.GetFileNameWithoutExtension(fileName), $"snap-{position.Floor}")));
            Assert.True(snapshot.ExitCode == 0, snapshot.All);
            Assert.Contains("SNAPSHOTTED", snapshot.Output, StringComparison.Ordinal);

            var plan = PlanFor(recording, position);
            Assert.IsType<RecordedFightPlan>(plan);
            var report = EnterFight(ManifestPath(fileName), Path.GetFileNameWithoutExtension(fileName), plan, restore: true, cache: cache, out var result);

            Assert.True(result.Verified, result.All);
            Assert.Equal("restored", report.GetProperty("entry_route").GetString());
            Assert.StartsWith("restored from the game's own save at " + plan.Describe(), report.GetProperty("entry_source").GetString(), StringComparison.Ordinal);
            Assert.DoesNotContain("then walked", report.GetProperty("entry_source").GetString(), StringComparison.Ordinal);
            Assert.Empty(report.GetProperty("steps").EnumerateArray());
            Assert.Equal(recording.CombatStartDigest(position.Fight!.Value), report.GetProperty("this_game_digest").GetString());
            Assert.True(report.GetProperty("profile_unchanged").GetBoolean());
        }
    }

    /// <summary>
    /// The floors between fights are exactly the arrivals the eligibility rule refuses:
    /// <c>floor-snapshot</c> says so of the replayed state, and the manifest's own
    /// reading in <c>RetailPlayback.RestorableArrivals</c> agrees with it floor for
    /// floor. The two are held together because the library offers on the second and
    /// the cache is written on the first.
    /// </summary>
    [GameTheory]
    [MemberData(nameof(Recordings))]
    public void TheManifestsReadingOfRestorableArrivalsIsTheEnginesOwn(string fileName)
    {
        var recording = Load(fileName);
        var cache = Scratch(Path.Combine(Path.GetFileNameWithoutExtension(fileName), "eligibility"));
        var restorable = RetailPlayback.RestorableArrivals(recording).Select(arrival => arrival.Floor).ToHashSet();

        foreach (var position in RunView.PositionsIn(recording).Where(position => !position.IsRunStart && !position.Unfinished))
        {
            var snapshot = Arbiter.Run(
                "floor-snapshot", ManifestPath(fileName), "--floor", position.Floor.ToString(),
                "--cache", cache, "--out", Scratch(Path.Combine(Path.GetFileNameWithoutExtension(fileName), $"eligibility-{position.Floor}")));

            if (restorable.Contains(position.Floor))
            {
                Assert.True(snapshot.ExitCode == 0, snapshot.All);
                Assert.Contains("SNAPSHOTTED", snapshot.Output, StringComparison.Ordinal);
            }
            else
            {
                Assert.NotEqual(0, snapshot.ExitCode);
                Assert.Contains("No fight is live at this arrival", snapshot.All, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>
    /// The row-gating invariant over the real recordings: no enabled row routes to
    /// <c>Unreachable</c>, and every refused play-from row on a floor between fights
    /// carries the reason rather than nothing.
    /// </summary>
    [GameTheory]
    [MemberData(nameof(Recordings))]
    public void NoRowIsEnabledWhoseRouteIsUnreachable(string fileName)
    {
        var recording = Load(fileName);
        var positions = RunView.PositionsIn(recording);

        foreach (var position in positions)
        {
            var view = RunView.For(recording, RunProgress.Empty.WithFightPlayed(recording.RunId, 1), selectedFloor: position.Floor);
            foreach (var row in view.Rows)
            {
                if (row.Fight is null && row.Floor is null) continue;

                // A floor row's coordinate is the position's own, which is -1 for the run's
                // start - a place no boundary names and no plan can be built for.
                var seq = row.Fight is { } fight
                    ? RecordedFightPlan.For(recording, fight).BoundarySeq
                    : positions.Single(candidate => candidate.Floor == row.Floor!.Value).AfterSeq;
                var route = RetailPlayback.RouteTo(recording, seq);

                if (row.Enabled) Assert.True(route.Reachable, $"{row.Kind} on floor {row.Floor} is enabled and routes to {route}.");
                else if (row.Kind == RunViewRowKind.PlayFrom && !position.IsRunStart && !position.Unfinished)
                    Assert.Equal(LibraryCopy.EarlierFightNotReplayable, row.Reason);
            }
        }
    }

    /// <param name="scratch">Where under the test scratch this entry's evidence goes, so
    /// the recorder's own form and the committed copy never share a report.</param>
    private static JsonElement EnterFight(string manifestPath, string scratch, IBoundaryPlan plan, bool restore, string? cache, out Arbiter.Result result)
    {
        var outDir = Scratch(Path.Combine(scratch, $"{(restore ? "restore" : "walk")}-{plan.Kind}-{plan.Fight ?? plan.Floor}"));
        var args = new List<string> { "enter-fight", manifestPath, "--out", outDir };
        if (plan.Fight is { } fight) args.AddRange(["--fight", fight.ToString()]);
        else args.AddRange(["--floor", plan.Floor!.Value.ToString()]);
        if (restore) args.AddRange(["--restore", "--cache", cache!]);

        result = Arbiter.Run([.. args]);
        var reportPath = Path.Combine(outDir, "enter-fight.json");
        Assert.True(File.Exists(reportPath), result.All);
        return JsonDocument.Parse(File.ReadAllText(reportPath)).RootElement;
    }
}

using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// Which floor arrivals may be stored as a serialized run.
///
/// The rule is one field wide and it is the whole finding of the floor-entry
/// measurement, so it is tested where the game is not: an arrival with a live fight
/// restores, an arrival without one carries a finished fight the game's own save has no
/// representation of, and a state that cannot say which is not guessed about.
/// </summary>
public sealed class FloorEntrySnapshotEligibilityTests
{
    [Fact]
    public void AcceptsAnArrivalWhereAFightIsLive()
    {
        Assert.Null(FloorEntrySnapshotEligibility.Refusal(Fields("true", "none")));
    }

    /// <summary>
    /// The one that matters. Nothing about the run differs at such an arrival - the
    /// seed, every stream position, the deck and the map coordinate all agree - and the
    /// restored state is still not the same state, which is the most convincing shape a
    /// wrong answer has.
    /// </summary>
    [Fact]
    public void RefusesAnArrivalWhoseOnlyCombatIsAFightThatAlreadyEnded()
    {
        var refusal = FloorEntrySnapshotEligibility.Refusal(Fields("false", "victory"));

        Assert.NotNull(refusal);
        Assert.Contains("combat.outcome=victory", refusal, StringComparison.Ordinal);
        Assert.Contains("No fight is live at this arrival", refusal, StringComparison.Ordinal);
    }

    /// <summary>An arrival with no fight before it and none at it restores the same way
    /// as any other arrival with no live fight, and is refused for the same reason: it
    /// was not measured, and the rule is what a floor arrival may be cached on rather
    /// than what happens to work.</summary>
    [Fact]
    public void RefusesAnArrivalWithNoFightAtAllEitherSideOfIt()
    {
        Assert.NotNull(FloorEntrySnapshotEligibility.Refusal(Fields("false", "none")));
    }

    [Fact]
    public void RefusesAStateThatCannotSayWhetherAFightIsLive()
    {
        var refusal = FloorEntrySnapshotEligibility.Refusal(
            new Dictionary<string, string>(StringComparer.Ordinal) { ["run.total_floor"] = "5" });

        Assert.NotNull(refusal);
        Assert.Contains("combat.in_progress", refusal, StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> Fields(string inProgress, string outcome) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["combat.in_progress"] = inProgress,
            ["combat.outcome"] = outcome,
        };
}

/// <summary>
/// What a cached floor-entry snapshot has to prove before anything restores from it.
///
/// A cache directory is named by a hash of the key, so finding one is already strong
/// evidence - and every field that key summarises is still compared in the open here,
/// because "strong evidence" is not the standard used anywhere else in this project. A
/// snapshot that binds is not thereby trusted either: the restore re-derives the digest
/// from the bytes, which is the check none of these replaces.
/// </summary>
public sealed class FloorEntrySnapshotTests
{
    [Fact]
    public void ReadsBackTheSnapshotItWrote()
    {
        var root = TempRoot();
        var recording = EntryFixtures.WholeRun();
        var plan = FloorEntryPlan.For(recording, 2);
        Snapshot(recording, plan).WriteInto(root, SaveJson);

        var read = FloorEntrySnapshot.Read(plan.SnapshotKey, root);

        Assert.NotNull(read);
        Assert.Equal(2, read.Floor);
        Assert.Equal(plan.BoundarySeq, read.AfterSeq);
        Assert.Equal(plan.SnapshotKey, read.Key);
        Assert.Empty(read.Binds(recording, plan, "v0.111.0", "41cef1ea"));
    }

    [Fact]
    public void FindsNothingWhereNothingWasMaterialised()
    {
        var plan = FloorEntryPlan.For(EntryFixtures.WholeRun(), 2);

        Assert.Null(FloorEntrySnapshot.Read(plan.SnapshotKey, TempRoot()));
    }

    /// <summary>
    /// The save is written before the record that says it was verified, so a crash
    /// between the two leaves a cache with nothing in it rather than a record pointing
    /// at a save that never arrived. This is the other half of that ordering: a record
    /// with no save beside it is not a snapshot.
    /// </summary>
    [Fact]
    public void FindsNothingWhenTheSaveBesideTheRecordIsGone()
    {
        var root = TempRoot();
        var recording = EntryFixtures.WholeRun();
        var plan = FloorEntryPlan.For(recording, 2);
        var directory = Snapshot(recording, plan).WriteInto(root, SaveJson);

        File.Delete(Path.Combine(directory, FloorEntrySnapshot.SaveFileName));

        Assert.Null(FloorEntrySnapshot.Read(plan.SnapshotKey, root));
    }

    [Fact]
    public void RefusesASnapshotOfAnotherRun()
    {
        var recording = EntryFixtures.WholeRun();
        var plan = FloorEntryPlan.For(recording, 2);
        var snapshot = Snapshot(recording, plan) with { RunId = "somebody-elses-run" };

        Assert.Contains(
            snapshot.Binds(recording, plan, "v0.111.0", "41cef1ea"),
            refusal => refusal.Contains("somebody-elses-run", StringComparison.Ordinal));
    }

    [Fact]
    public void RefusesASnapshotOfAnotherPrefixOfThisRun()
    {
        var recording = EntryFixtures.WholeRun();
        var plan = FloorEntryPlan.For(recording, 2);
        var snapshot = Snapshot(recording, plan) with
        {
            Key = SnapshotCacheKey.For(recording, plan.BoundarySeq - 1),
        };

        Assert.Contains(
            snapshot.Binds(recording, plan, "v0.111.0", "41cef1ea"),
            refusal => refusal.Contains("not the key of the history", StringComparison.Ordinal));
    }

    [Fact]
    public void RefusesASnapshotTakenAtAnotherBoundary()
    {
        var recording = EntryFixtures.WholeRun();
        var plan = FloorEntryPlan.For(recording, 2);
        var snapshot = Snapshot(recording, plan) with { Floor = 7, AfterSeq = 40 };

        Assert.Contains(
            snapshot.Binds(recording, plan, "v0.111.0", "41cef1ea"),
            refusal => refusal.Contains("floor 7 after action 40", StringComparison.Ordinal));
    }

    /// <summary>A save is a build's own format, and a restore on another build is a
    /// different question answered with this one's evidence.</summary>
    [Fact]
    public void RefusesASnapshotVerifiedOnAnotherBuild()
    {
        var recording = EntryFixtures.WholeRun();
        var plan = FloorEntryPlan.For(recording, 2);

        Assert.Contains(
            Snapshot(recording, plan).Binds(recording, plan, "v0.112.0", "beefcafe"),
            refusal => refusal.Contains("v0.112.0", StringComparison.Ordinal));
    }

    /// <summary>
    /// The recording is the authority and the cache is not. A snapshot verified at a
    /// digest the recording no longer declares is a snapshot of the run the recording
    /// used to describe.
    /// </summary>
    [Fact]
    public void RefusesASnapshotVerifiedAtADigestTheRecordingNoLongerDeclares()
    {
        var recording = EntryFixtures.WholeRun();
        var plan = FloorEntryPlan.For(recording, 2);
        var snapshot = Snapshot(recording, plan) with { VerifiedDigest = "sha256:" + new string('0', 64) };

        Assert.Contains(
            snapshot.Binds(recording, plan, "v0.111.0", "41cef1ea"),
            refusal => refusal.Contains("has been edited since the snapshot was taken", StringComparison.Ordinal));
    }

    [Fact]
    public void RefusesASnapshotWrittenBySomeOtherSchema()
    {
        var recording = EntryFixtures.WholeRun();
        var plan = FloorEntryPlan.For(recording, 2);
        var snapshot = Snapshot(recording, plan) with { Schema = "something/else/v9" };

        Assert.Contains(
            snapshot.Binds(recording, plan, "v0.111.0", "41cef1ea"),
            refusal => refusal.Contains("something/else/v9", StringComparison.Ordinal));
    }

    /// <summary>
    /// A cache is a file somebody can edit. The digest comparison downstream catches an
    /// edit that changed the run; this catches one that did not, which is the edit
    /// nothing else would notice.
    /// </summary>
    [Fact]
    public void RefusesASaveThatIsNotTheOneItWasVerifiedAt()
    {
        var recording = EntryFixtures.WholeRun();
        var snapshot = Snapshot(recording, FloorEntryPlan.For(recording, 2));

        Assert.Null(snapshot.SaveIntegrity(SaveJson));
        Assert.Contains("Refusing to restore", snapshot.SaveIntegrity(SaveJson + " ")!, StringComparison.Ordinal);
    }

    private const string SaveJson = """{"schema_version":3,"seed":"SFXT47K77RFK"}""";

    private static FloorEntrySnapshot Snapshot(ReplayManifest recording, FloorEntryPlan plan) => new(
        Schema: FloorEntrySnapshot.SchemaId,
        RunId: recording.RunId,
        BuildVersion: "v0.111.0",
        BuildCommit: "41cef1ea",
        BoundaryKind: plan.Kind,
        Floor: plan.FloorNumber,
        AfterSeq: plan.BoundarySeq,
        ActIndex: "0",
        DeclaredDigest: Declared(recording, plan),
        DeclaredDigestSource: "Engine",
        VerifiedDigest: Declared(recording, plan),
        SaveFile: FloorEntrySnapshot.SaveFileName,
        SaveSha256: FloorEntrySnapshot.HashOf(SaveJson),
        SaveByteCount: System.Text.Encoding.UTF8.GetByteCount(SaveJson),
        SaveSchemaVersion: 3,
        SavePreFinishedRoom: "none",
        Key: plan.SnapshotKey);

    private static string Declared(ReplayManifest recording, FloorEntryPlan plan) =>
        recording.BoundaryAt(ReplayBoundary.FloorEntryKind, floor: plan.FloorNumber)!.Digest.Value;

    private static string TempRoot()
    {
        var path = Path.GetFullPath(Path.Combine(
            "build", "test-scratch", "floor-entry-snapshot", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(path);
        return path;
    }
}

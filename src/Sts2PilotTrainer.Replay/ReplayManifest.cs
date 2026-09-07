using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// A complete, inspectable description of one reconstructed run: the environment
/// it must be replayed in, the ordered actions that constitute it, the independent
/// checkpoints it must satisfy, and - once an arbiter has run - what actually
/// happened when it was replayed.
///
/// A manifest is a claim, not a result. It becomes a result only when
/// <see cref="Verification"/> is filled in by the arbiter.
/// </summary>
public sealed record ReplayManifest
{
    /// <summary>Bumped whenever a change would make an older arbiter misread a
    /// newer manifest. Readers must refuse an unknown version rather than guess.</summary>
    public const int CurrentManifestVersion = 6;

    [JsonPropertyName("manifest_version")]
    public int ManifestVersion { get; init; } = CurrentManifestVersion;

    /// <summary>Stable identifier for this reconstruction. Never derived from a
    /// video title: this creator A/B-tests titles, so a title is not an identifier.</summary>
    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    [JsonPropertyName("environment")]
    public required EnvironmentIdentity Environment { get; init; }

    [JsonPropertyName("source")]
    public required SourceProvenance Source { get; init; }

    /// <summary>The complete ordered history from run start. Order is the whole
    /// point: the game's RNG streams persist across the run, so a reordering is a
    /// different run even when every individual action is right.</summary>
    [JsonPropertyName("actions")]
    public required IReadOnlyList<ActionRecord> Actions { get; init; }

    /// <summary>Independently observed state the replay must agree with. These are
    /// what turn a replay from "it ran" into "it reproduced the run".</summary>
    [JsonPropertyName("checkpoints")]
    public required IReadOnlyList<Checkpoint> Checkpoints { get; init; }

    /// <summary>
    /// Every point in this history a player can be stood at, with the digest that
    /// proves the state reached there is the recorded one.
    ///
    /// Empty for a fixture that makes no publication claim. Never a place to record
    /// a boundary nobody derived: each digest is engine-produced or captured live,
    /// and one that was neither would be an identity nobody established.
    /// </summary>
    [JsonPropertyName("boundaries")]
    public IReadOnlyList<ReplayBoundary> Boundaries { get; init; } = [];

    /// <summary>Filled in by the arbiter. Null means unverified - and an unverified
    /// manifest is never evidence of anything.</summary>
    [JsonPropertyName("verification")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VerificationReport? Verification { get; init; }

    /// <summary>
    /// The boundary of this kind that matches the ordinal asked for, or null when the
    /// recording records none.
    ///
    /// One lookup so that "which boundary is fight 2's" has one answer. A caller that
    /// searched the list itself would be a second answer waiting to disagree with the
    /// plan and the equality check about which moment they all mean.
    /// </summary>
    public ReplayBoundary? BoundaryAt(string kind, int? fight = null, int? floor = null, int? turn = null) =>
        Boundaries.FirstOrDefault(boundary =>
            string.Equals(boundary.Kind, kind, StringComparison.Ordinal) &&
            (fight is null || boundary.Fight == fight) &&
            (floor is null || boundary.Floor == floor) &&
            (turn is null || boundary.Turn == turn));

    /// <summary>The engine-produced or captured digest at the start of the fight with
    /// this ordinal, or null when the recording carries none for it.</summary>
    public string? CombatStartDigest(int fight = 1) =>
        BoundaryAt(ReplayBoundary.CombatStartKind, fight: fight)?.Digest.Value;
}

/// <summary>
/// The four values that decide whether a replay can even be attempted here, plus
/// the run parameters they imply. All four must match the local environment
/// exactly; there is no approximate path, and no field here may be guessed.
/// </summary>
public sealed record EnvironmentIdentity
{
    /// <summary>e.g. <c>v0.111.0</c>. The game shows this in its version overlay.</summary>
    [JsonPropertyName("build_version")]
    public required Fact<string> BuildVersion { get; init; }

    /// <summary>e.g. <c>2026.08.14</c>, as the overlay renders it - which is the UTC
    /// date of the release timestamp, not the local one. Compared as a string.</summary>
    [JsonPropertyName("build_date_utc")]
    public required Fact<string> BuildDateUtc { get; init; }

    /// <summary><c>standard</c>, <c>custom</c> or <c>daily</c>. Persisted by the game
    /// on every run and every save, and it changes run setup, so it is identity.</summary>
    [JsonPropertyName("game_mode")]
    public required Fact<string> GameMode { get; init; }

    /// <summary>The run seed exactly as the game displays it.</summary>
    [JsonPropertyName("seed")]
    public required Fact<string> Seed { get; init; }

    /// <summary>The game's own ModelDb content hash - what its multiplayer layer
    /// compares as <c>idDatabaseHash</c>. This is the mod-parity gate: matching
    /// hashes mean the two environments agree on the content that exists, without
    /// needing to know which mods produced it.</summary>
    [JsonPropertyName("content_hash")]
    public required Fact<string> ContentHash { get; init; }

    [JsonPropertyName("ascension")]
    public required Fact<int> Ascension { get; init; }

    /// <summary>
    /// The unlock state the run was generated against.
    ///
    /// Identity rather than player detail: the game builds a run's content pools
    /// from the player's unlocks, so the same seed on the same build gives a player
    /// with less unlocked a different run. Measured, not argued - see
    /// <c>docs/environment-identity.md</c>. The preflight compares this against what
    /// the replaying environment actually has, and refuses a shortfall.
    /// </summary>
    [JsonPropertyName("unlocks")]
    public required Fact<UnlockRequirement> Unlocks { get; init; }

    /// <summary>Model id, e.g. <c>CHARACTER.IRONCLAD</c>.</summary>
    [JsonPropertyName("character")]
    public required Fact<string> Character { get; init; }

    /// <summary>
    /// The named set of mods the run was played under.
    ///
    /// Recorded alongside the content hash rather than instead of it. The hash gates
    /// content and is blind to behaviour; this names the environment so each mod can
    /// be reasoned about individually. Neither is a proof of parity on its own.
    /// </summary>
    [JsonPropertyName("mods")]
    public required Fact<ModEnvironment> Mods { get; init; }

    /// <summary>
    /// The acts this run climbs, in order, as model ids.
    ///
    /// Identity rather than configuration: this build ships two different acts at
    /// index 0, and a run through the other one generates entirely different
    /// encounters, events and relics from the same seed. It also produces the same
    /// map, because map topology is generated from a separate seed-keyed generator -
    /// so nothing about the map would reveal the substitution.
    ///
    /// Fortunately the game puts the act's name on the map screen, so this is
    /// readable from a video rather than guessed.
    /// </summary>
    [JsonPropertyName("acts")]
    public required Fact<IReadOnlyList<string>> Acts { get; init; }
}

/// <summary>Where the reconstruction came from, in enough detail to re-check it.</summary>
public sealed record SourceProvenance
{
    /// <summary><c>vod</c> for publication evidence from a public video,
    /// <c>native</c> for a run this project's own recorder watched being played, and
    /// <c>synthetic-engine</c> for pinned engine fixtures that exercise replay
    /// without making a source claim.</summary>
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("video")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VideoSource? Video { get; init; }

    [JsonPropertyName("synthetic")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SyntheticSource? Synthetic { get; init; }

    [JsonPropertyName("native")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeSource? Native { get; init; }

    /// <summary>How the ordered history was produced. <c>manual</c> means a human
    /// read the frames; that is the honest label for this milestone and it should
    /// not silently become <c>automatic</c> when an extractor is written.</summary>
    [JsonPropertyName("extraction_method")]
    public required string ExtractionMethod { get; init; }

    /// <summary>How far into the run the manifest claims to describe, and why it
    /// stops there. A partial history is fine; a partial history pretending to be
    /// complete is not.</summary>
    [JsonPropertyName("coverage")]
    public required string Coverage { get; init; }

    /// <summary>
    /// Evidence that the recording begins at the run's beginning. Required for a
    /// video source: replaying an ordered history from run start against a recording
    /// of a *resumed* run reconstructs a different run, and every other gate passes.
    /// </summary>
    [JsonPropertyName("run_start")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RunStartEvidence? RunStart { get; init; }

    /// <summary>
    /// A second reading of the environment, from the end-of-run summary screen. The
    /// validator requires it to agree with the environment identity.
    /// </summary>
    [JsonPropertyName("run_summary")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RunSummaryObservation? RunSummary { get; init; }
}

/// <summary>
/// A run this project's own recorder watched being played, on the player's machine.
///
/// It carries what makes the recording checkable and nothing that identifies the
/// person who made it: no Steam id, no path on their disk, no profile id, no
/// hardware. A recording is meant to be shareable, and a field here that named its
/// author would travel with every copy of it forever.
///
/// The two facts that cannot be established downstream live here.
/// <see cref="WitnessedRunStart"/> is the native counterpart of a video's run-start
/// evidence: a history replayed from run start against a run the recorder joined
/// half way through reconstructs a different run, and every other gate passes.
/// <see cref="Continuity"/> is the counterpart of the end-of-run reading: a resumed
/// recorder must either account for the gap between sessions or mark the watch broken.
/// </summary>
public sealed record NativeSource
{
    /// <summary>An unbroken watch from the first decision to the last.</summary>
    public const string ContinuousContinuity = "continuous";

    /// <summary>The recorder missed part of the run, so the history is not this run's.</summary>
    public const string BrokenContinuity = "broken";

    public static readonly string[] Continuities = [ContinuousContinuity, BrokenContinuity];

    /// <summary>The recorder met no decision it could not name and no console
    /// command.</summary>
    public const string CompleteIntegrity = "complete";

    /// <summary>The recorder stopped at a decision it could not name. The history ends
    /// at the ordinal before it and <see cref="Unmapped"/> says what it met. Kept,
    /// and never publishable: a prefix that stops short of a decision replays into a
    /// run that never made it.</summary>
    public const string UnmappedIntegrity = "unmapped";

    /// <summary>This run was not played entirely by the game's own rules. The run is
    /// still a run and the recording is still what happened, and it is never
    /// publishable: whatever changed the state is not in the history, so replaying
    /// the history reconstructs a different run. The field states that and no cause -
    /// more than one thing about a run puts it here.</summary>
    public const string NonStandardIntegrity = "non-standard";

    public static readonly string[] Integrities = [CompleteIntegrity, UnmappedIntegrity, NonStandardIntegrity];

    /// <summary>The one older format a migrated file may declare it was written in.
    /// A later format widens this when it adds a migration of its own.</summary>
    public static readonly int[] MigratableVersions = [5];

    /// <summary>Won, lost, or given up. A give-up is a completed recording: the run is
    /// over, the history is whole, and the fights in it were really played.</summary>
    public static readonly string[] Outcomes = ["won", "lost", "abandoned"];

    /// <summary>Which build of the recorder produced this, so a defect found in one
    /// can be traced to everything it wrote.</summary>
    [JsonPropertyName("recorder_version")]
    public required string RecorderVersion { get; init; }

    /// <summary>Whether the recorder was watching when the run began. Captured,
    /// because it is a fact about the recorder's own session.</summary>
    [JsonPropertyName("witnessed_run_start")]
    public required Fact<bool> WitnessedRunStart { get; init; }

    [JsonPropertyName("continuity")]
    public required string Continuity { get; init; }

    [JsonPropertyName("outcome")]
    public required string Outcome { get; init; }

    /// <summary>
    /// Whether anything happened in this run that stops it being published, and what.
    /// One of <see cref="Integrities"/>, and required: a recording that states none
    /// is a version-5 file, which reads as <see cref="CompleteIntegrity"/> through the
    /// migration and says so in <see cref="MigratedFromVersion"/>.
    /// </summary>
    [JsonPropertyName("integrity")]
    public required string Integrity { get; init; }

    /// <summary>
    /// The decision the recorder stopped at, when <see cref="Integrity"/> is
    /// <see cref="UnmappedIntegrity"/>, and absent otherwise. A list, because a build
    /// may one day record more than one seam's account of the same moment; every
    /// entry's ordinal is the one the history stops before.
    /// </summary>
    [JsonPropertyName("unmapped")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<UnmappedDecision>? Unmapped { get; init; }

    /// <summary>
    /// Branches the player played after entering a fight and the game's own save
    /// later rolled back. Kept as captured evidence and excluded from the continued
    /// run's ordered history; the publication gate replays each branch separately
    /// through its final captured state.
    /// </summary>
    [JsonPropertyName("discarded")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DiscardedBranch>? Discarded { get; init; }

    /// <summary>
    /// The oldest format this file was written in, when it was migrated from one.
    ///
    /// Written only by <c>arbiter migrate-manifest</c> and absent from a recording a
    /// current recorder wrote. A declared fact about the file rather than the run: it
    /// says fields introduced after that version may be absent because nothing could
    /// have captured them, which is how the validator knows to waive them. It survives
    /// a later migration unchanged, because what it records is where the file began.
    /// </summary>
    [JsonPropertyName("migrated_from_version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MigratedFromVersion { get; init; }

    [JsonIgnore]
    public bool IsContinuous => string.Equals(Continuity, ContinuousContinuity, StringComparison.Ordinal);

    /// <summary>Whether the recording states an integrity that is not
    /// <see cref="CompleteIntegrity"/>, which is what refuses it for publication.</summary>
    [JsonIgnore]
    public bool StatesSomethingOtherThanComplete =>
        !string.Equals(Integrity, CompleteIntegrity, StringComparison.Ordinal);

    /// <summary>Whether this file was migrated from a format older than the one that
    /// introduced a field, so that field's absence is the migration's rather than a
    /// recorder's omission.</summary>
    public bool PredatesVersion(int version) => MigratedFromVersion is { } from && from < version;
}

/// <summary>A recorded branch removed by the game's observed room-entry rollback.</summary>
public sealed record DiscardedBranch
{
    /// <summary>The room-entry action the continued run returned to.</summary>
    [JsonPropertyName("rollback_to_seq")]
    public required int RollbackToSeq { get; init; }

    /// <summary>The captured digest which both the journal boundary and resumed run held.</summary>
    [JsonPropertyName("rollback_to_digest")]
    public required string RollbackToDigest { get; init; }

    /// <summary>The decisions observed after that boundary before the quit.</summary>
    [JsonPropertyName("actions")]
    public required IReadOnlyList<ActionRecord> Actions { get; init; }

    /// <summary>The captured states showing where combat began and the complete final
    /// sample the branch replay must match exactly.</summary>
    [JsonPropertyName("trace")]
    public required ReplayTrace Trace { get; init; }
}

public sealed record SyntheticSource
{
    [JsonPropertyName("fixture_id")]
    public required string FixtureId { get; init; }

    [JsonPropertyName("fixture_version")]
    public required int FixtureVersion { get; init; }

    [JsonPropertyName("generator")]
    public required string Generator { get; init; }

    [JsonPropertyName("generated_build")]
    public required string GeneratedBuild { get; init; }
}

/// <summary>
/// Identifies the video without reproducing any of it. Everything here is public
/// metadata; no footage, frames, or stills are stored by this project.
/// </summary>
public sealed record VideoSource
{
    [JsonPropertyName("platform")]
    public required string Platform { get; init; }

    /// <summary>The only stable identifier. Titles on this channel are A/B tested.</summary>
    [JsonPropertyName("video_id")]
    public required string VideoId { get; init; }

    [JsonPropertyName("channel_id")]
    public required string ChannelId { get; init; }

    /// <summary>
    /// The channel's display name, as a host names the person whose run this is.
    ///
    /// Here rather than in a host's own copy because it is a fact about the
    /// recording, and a mod that hardcoded it would be a mod that could only ever
    /// carry one recording. It is an identifier the source declares, like the
    /// platform and the video id beside it, and never a gate: nothing in the
    /// preflight, the replay or the comparison reads it.
    /// </summary>
    [JsonPropertyName("channel_name")]
    public required string ChannelName { get; init; }

    [JsonPropertyName("duration_s")]
    public required int DurationSeconds { get; init; }

    /// <summary>
    /// The recording's title as the platform published it, for a host that needs to name
    /// the recording a player is about to play from.
    ///
    /// Declared, and deliberately not an identifier: this channel A/B tests its titles, so
    /// the same recording can carry two of them and only <see cref="VideoId"/> is stable.
    /// Optional because the manifests written before discovery existed do not have one, and
    /// backfilling a title nobody read at the time would be inventing provenance. Nothing in
    /// the preflight, the replay or the comparison reads it.
    /// </summary>
    [JsonPropertyName("title")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; init; }

    [JsonIgnore]
    public string Url => Platform == "youtube"
        ? $"https://www.youtube.com/watch?v={VideoId}"
        : VideoId;
}

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
    public const int CurrentManifestVersion = 2;

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

    /// <summary>Filled in by the arbiter. Null means unverified - and an unverified
    /// manifest is never evidence of anything.</summary>
    [JsonPropertyName("verification")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VerificationReport? Verification { get; init; }
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
    /// <summary><c>vod</c> for publication evidence and <c>synthetic-engine</c> for
    /// pinned engine fixtures that exercise replay without making a source claim.</summary>
    [JsonPropertyName("kind")]
    public required string Kind { get; init; }

    [JsonPropertyName("video")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VideoSource? Video { get; init; }

    [JsonPropertyName("synthetic")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SyntheticSource? Synthetic { get; init; }

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

    [JsonPropertyName("duration_s")]
    public required int DurationSeconds { get; init; }

    [JsonPropertyName("url")]
    public string Url => Platform == "youtube"
        ? $"https://www.youtube.com/watch?v={VideoId}"
        : VideoId;
}

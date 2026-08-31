using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// The named set of mods a run was played under.
///
/// This exists because the content hash cannot do this job. The hash is a checksum
/// over the model-id database: it covers content contributed by mods that declare
/// themselves gameplay-affecting, and it is blind to a mod that patches behaviour
/// without adding content, or one that declares itself non-gameplay. So the hash
/// stays a necessary gate and the environment gets a name and a membership list,
/// which is a different claim and has to be recorded as one.
///
/// Naming the mods is not the same as proving they changed nothing. What it buys is
/// the ability to reason about each one specifically, and — for the one that can
/// actually invalidate a reconstruction — to write a check that looks for its
/// fingerprints. See <see cref="RunStartEvidence"/>.
/// </summary>
public sealed record ModEnvironment
{
    /// <summary>A short stable name for this environment, so artifacts can refer to
    /// it without repeating the list.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>How many mods the game's own overlay reported loaded. Recorded
    /// separately from the list so that "we identified three of three" is
    /// distinguishable from "we identified three".</summary>
    [JsonPropertyName("reported_count")]
    public required int ReportedCount { get; init; }

    [JsonPropertyName("mods")]
    public required IReadOnlyList<InstalledMod> Mods { get; init; }

    [JsonPropertyName("headless_parity_waiver")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public HeadlessParityWaiver? HeadlessParityWaiver { get; init; }
}

public sealed record HeadlessParityWaiver
{
    [JsonPropertyName("justification")]
    public required string Justification { get; init; }

    [JsonPropertyName("residual_closed")]
    public required string ResidualClosed { get; init; }

    [JsonPropertyName("executable_command")]
    public required string ExecutableCommand { get; init; }

    [JsonPropertyName("modded_event_digest")]
    public required string ModdedEventDigest { get; init; }

    [JsonPropertyName("headless_event_digest")]
    public required string HeadlessEventDigest { get; init; }

    [JsonPropertyName("modded_state_checksum")]
    public required string ModdedStateChecksum { get; init; }

    [JsonPropertyName("headless_state_checksum")]
    public required string HeadlessStateChecksum { get; init; }

    [JsonIgnore]
    public bool IsEstablished =>
        !string.IsNullOrWhiteSpace(Justification) &&
        ResidualClosed.Contains("BaseLib v3.4.5 PowerCmd.Apply", StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(ExecutableCommand) &&
        !string.IsNullOrWhiteSpace(ModdedEventDigest) &&
        string.Equals(ModdedEventDigest, HeadlessEventDigest, StringComparison.Ordinal) &&
        !string.IsNullOrWhiteSpace(ModdedStateChecksum) &&
        string.Equals(ModdedStateChecksum, HeadlessStateChecksum, StringComparison.Ordinal);
}

/// <summary>
/// One mod, and an assessment of whether it could move a replay.
///
/// <paramref name="ReplayRisk"/> is a judgement, written down so it can be argued
/// with. A mod recorded here with no assessment would be a list that looks like
/// diligence and carries none.
/// </summary>
public sealed record InstalledMod(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("replay_risk")] string ReplayRisk);

/// <summary>
/// Evidence that the recording starts at the beginning of the run, rather than part
/// way through one that was resumed.
///
/// This is the specific defence against the one identified mod that can invalidate a
/// reconstruction outright. A run resumed from run history is not a run from start:
/// its seed, build, content hash and act all match, every gate passes, and replaying
/// an ordered history "from run start" against it silently reconstructs a different
/// run. Nothing downstream would catch it, because everything downstream is
/// comparing against a recording of the wrong thing.
///
/// So the check is at ingestion, on the recording itself, and it fails closed.
/// </summary>
public sealed record RunStartEvidence
{
    /// <summary>
    /// How late in the run the first timer reading may be and still count as the
    /// start. The game's run timer begins at zero and the map screen is the first
    /// thing shown, so a genuine from-start recording reads a handful of seconds.
    /// A resumed run reads whatever the original run had accumulated.
    /// </summary>
    public const int MaxRunTimeSecondsAtStart = 15;

    [JsonPropertyName("first_observed_run_time_s")]
    public required Fact<int> FirstObservedRunTimeSeconds { get; init; }

    [JsonPropertyName("first_observed_floor")]
    public required Fact<int> FirstObservedFloor { get; init; }

    /// <summary>Whether the recording shows the run being entered from the run
    /// history screen, which is how a resumed run begins.</summary>
    [JsonPropertyName("entered_from_run_history")]
    public required Fact<bool> EnteredFromRunHistory { get; init; }

    /// <summary>Whether the resume mod's own confirmation dialog appears.</summary>
    [JsonPropertyName("resume_modal_seen")]
    public required Fact<bool> ResumeModalSeen { get; init; }
}

/// <summary>
/// What the end-of-run summary screen showed.
///
/// A second, independent reading of the environment, taken from the other end of the
/// recording. Its value is not that it is more legible — it is the same overlay — but
/// that it is *far away*: a recording spliced from two runs, or a misreading that
/// drifted, cannot agree at both ends by accident. The validator requires it to
/// agree with the environment identity.
/// </summary>
public sealed record RunSummaryObservation
{
    [JsonPropertyName("video_t_ms")]
    public required int VideoTimeMs { get; init; }

    [JsonPropertyName("seed")]
    public required Fact<string> Seed { get; init; }

    [JsonPropertyName("build_version")]
    public required Fact<string> BuildVersion { get; init; }

    [JsonPropertyName("build_date_utc")]
    public required Fact<string> BuildDateUtc { get; init; }

    [JsonPropertyName("content_hash")]
    public required Fact<string> ContentHash { get; init; }

    [JsonPropertyName("ascension")]
    public required Fact<int> Ascension { get; init; }

    [JsonPropertyName("floors_climbed")]
    public required Fact<int> FloorsClimbed { get; init; }

    [JsonPropertyName("player_max_hp")]
    public required Fact<int> PlayerMaxHp { get; init; }

    [JsonPropertyName("deck_size")]
    public required Fact<int> DeckSize { get; init; }

    [JsonPropertyName("relic_count")]
    public required Fact<int> RelicCount { get; init; }

    /// <summary>
    /// What this screen does not display, named rather than left as an absence. The
    /// game mode in particular is not on it, which is why the manifest carries the
    /// mode as an inference and says so.
    /// </summary>
    [JsonPropertyName("not_shown")]
    public required IReadOnlyList<string> NotShown { get; init; }
}

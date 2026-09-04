using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Sts2PilotTrainer.Replay;

/// <summary>Where a recording's seed can be recovered from, for one creator.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SeedSource>))]
public enum SeedSource
{
    /// <summary>The creator writes it in the video description. Text, so no character
    /// recognition is involved and the whole class of confident misreads disappears.</summary>
    Description,

    /// <summary>The creator leaves the game's own version-info overlay switched on, so it
    /// is on screen for the whole run and has to be read off pixels.</summary>
    VersionOverlay,
}

/// <summary>
/// One creator's recording habits, as a bounded adapter.
///
/// This is deliberately not a general video-understanding configuration. It answers the
/// few questions whose answers differ between creators and decide whether a recording
/// can be reconstructed at all, and it is data rather than code so that a creator who
/// changes their format is a file edit rather than a release.
///
/// The set of creators is small and finite on purpose. A creator whose recordings cannot
/// yield a seed is not a creator with a harder adapter - it is a creator this project
/// cannot ingest, and saying so cheaply is the adapter's most valuable job.
/// </summary>
public sealed record CreatorProfile
{
    public const string CurrentSchema = "sts2-pilot-trainer/creator-profile/v1";

    [JsonPropertyName("channel_name")]
    public required string ChannelName { get; init; }

    [JsonPropertyName("channel_id")]
    public required string ChannelId { get; init; }

    [JsonPropertyName("seed_source")]
    public required SeedSource SeedSource { get; init; }

    /// <summary>
    /// Extracts the seed from the description, when that is where it lives. A regex with
    /// one capturing group. Null for an overlay creator, where the seed is pixels.
    /// </summary>
    [JsonPropertyName("seed_pattern")]
    public string? SeedPattern { get; init; }

    /// <summary>Extracts a stated build from the description, when the creator writes one.
    /// A build the creator states beats anything dated off the upload.</summary>
    [JsonPropertyName("build_pattern")]
    public string? BuildPattern { get; init; }

    /// <summary>
    /// Things in this creator's layout that cover part of the game. Recorded because an
    /// occlusion is not a reading difficulty, it is a value that is absent: a webcam over
    /// the top-right corner does not make the version overlay hard to read, it makes it
    /// unreadable at any resolution.
    /// </summary>
    [JsonPropertyName("occlusions")]
    public IReadOnlyList<string> Occlusions { get; init; } = [];

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    /// <summary>
    /// Pulls the seed out of a description. Returns null when this creator does not put it
    /// there, when there is no description, or when the pattern does not match - all three
    /// are the same answer to the caller, which is "not available from here".
    /// </summary>
    public string? SeedFromDescription(string? description)
        => CaptureOne(SeedSource == SeedSource.Description ? SeedPattern : null, description);

    /// <summary>The build the creator stated, if they state one.</summary>
    public string? BuildFromDescription(string? description) => CaptureOne(BuildPattern, description);

    private static string? CaptureOne(string? pattern, string? text)
    {
        if (pattern is null || string.IsNullOrEmpty(text)) return null;

        // A creator's description is arbitrary text this project did not write, so the
        // match is bounded in time rather than trusted to terminate.
        var match = Regex.Match(text, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!match.Success) return null;
        return match.Groups.Count > 1 ? match.Groups[1].Value.Trim() : match.Value.Trim();
    }
}

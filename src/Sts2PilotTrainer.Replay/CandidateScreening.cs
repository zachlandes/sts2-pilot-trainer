using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>A chapter the creator marked on their own upload.</summary>
public sealed record VideoChapter(
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("start_s")] double StartSeconds,
    [property: JsonPropertyName("end_s")] double EndSeconds);

/// <summary>
/// What a video platform will tell you about a recording without transferring any of it.
/// Everything here is free: no download, no frame, no decode.
/// </summary>
public sealed record VideoMetadata
{
    [JsonPropertyName("video_id")] public required string VideoId { get; init; }
    [JsonPropertyName("channel_id")] public required string ChannelId { get; init; }
    [JsonPropertyName("channel_name")] public required string ChannelName { get; init; }
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("duration_s")] public required int DurationSeconds { get; init; }
    [JsonPropertyName("uploaded_utc")] public required DateOnly UploadedUtc { get; init; }
    [JsonPropertyName("chapters")] public IReadOnlyList<VideoChapter> Chapters { get; init; } = [];
}

/// <summary>What screening decided, before anything expensive has run.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ScreeningVerdict>))]
public enum ScreeningVerdict
{
    /// <summary>Seed and build are both in hand from metadata alone. Nothing was downloaded.</summary>
    Eligible,

    /// <summary>Metadata got as far as it can; one cheap frame is needed to finish the decision.</summary>
    NeedsFrameProbe,

    /// <summary>This recording cannot be reconstructed. Stop here rather than spend anything on it.</summary>
    Refused,
}

/// <summary>
/// The cheapest possible first gate: can this recording be reconstructed at all?
///
/// It runs on metadata a platform hands over for free, and it exists to make refusal
/// cheap. The expensive parts of ingestion - acquiring the video, reading frames,
/// transcribing decisions - are all downstream of a seed and a build, and a recording
/// that cannot yield those is not a harder job, it is not a job. Establishing that from
/// a description and an upload date costs nothing and saves everything.
///
/// Nothing here decides that a recording IS reconstructible. A seed that screening
/// recovered is a candidate for <c>verify-seed</c>, and a build it dated is a candidate
/// for <c>preflight</c>. Both are guesses this project then tests against the engine,
/// which is the only thing that settles either.
/// </summary>
public sealed record CandidateScreening
{
    [JsonPropertyName("video_id")] public required string VideoId { get; init; }
    [JsonPropertyName("channel_name")] public required string ChannelName { get; init; }
    [JsonPropertyName("title")] public required string Title { get; init; }
    [JsonPropertyName("duration_s")] public required int DurationSeconds { get; init; }
    [JsonPropertyName("verdict")] public required ScreeningVerdict Verdict { get; init; }

    /// <summary>The candidate seed, when metadata carried one. Never confirmed here.</summary>
    [JsonPropertyName("candidate_seed")] public string? CandidateSeed { get; init; }

    /// <summary>The candidate build, stated by the creator or dated from the upload.</summary>
    [JsonPropertyName("candidate_build")] public string? CandidateBuild { get; init; }

    /// <summary>How the build was arrived at, so a reader can see which it was.</summary>
    [JsonPropertyName("build_basis")] public required string BuildBasis { get; init; }

    [JsonPropertyName("blockers")] public IReadOnlyList<string> Blockers { get; init; } = [];
    [JsonPropertyName("notes")] public IReadOnlyList<string> Notes { get; init; } = [];

    public const string BuildStatedByCreator = "stated in the description";
    public const string BuildDatedFromUpload = "dated from the upload against the patch calendar";
    public const string BuildUnknown = "not established";

    public static CandidateScreening Screen(
        VideoMetadata video, CreatorProfile creator, PatchCalendar calendar)
    {
        var blockers = new List<string>();
        var notes = new List<string>();

        if (!string.Equals(video.ChannelId, creator.ChannelId, StringComparison.Ordinal))
        {
            blockers.Add(
                $"This video is from channel {video.ChannelId}, but the profile applied to it is " +
                $"{creator.ChannelName} ({creator.ChannelId}). A creator's profile describes their layout and " +
                "their habits, so applying it to somebody else's recording would read the wrong screen.");
        }

        var (seed, seedPending) = ScreenSeed(video, creator, blockers, notes);
        var (build, basis) = ScreenBuild(video, creator, calendar, blockers, notes);

        NoteChapters(video, notes);

        var verdict = blockers.Count > 0
            ? ScreeningVerdict.Refused
            : seedPending
                ? ScreeningVerdict.NeedsFrameProbe
                : ScreeningVerdict.Eligible;

        return new CandidateScreening
        {
            VideoId = video.VideoId,
            ChannelName = video.ChannelName,
            Title = video.Title,
            DurationSeconds = video.DurationSeconds,
            Verdict = verdict,
            CandidateSeed = seed,
            CandidateBuild = build,
            BuildBasis = basis,
            Blockers = blockers,
            Notes = notes,
        };
    }

    private static (string? Seed, bool Pending) ScreenSeed(
        VideoMetadata video, CreatorProfile creator, List<string> blockers, List<string> notes)
    {
        if (creator.SeedSource == SeedSource.VersionOverlay)
        {
            if (creator.Occlusions.Count > 0)
            {
                blockers.Add(
                    $"{creator.ChannelName} is recorded as reading the seed off the game's version overlay, but " +
                    $"their layout covers it: {string.Join("; ", creator.Occlusions)}. An occluded overlay is not " +
                    "a hard read, it is an absent value, and no resolution fixes it.");
                return (null, false);
            }

            notes.Add(
                "The seed is on screen rather than in the description, so one frame is still needed. " +
                "Read it at source resolution: agreement between readings that share a source and an engine is " +
                "not evidence of accuracy.");
            return (null, true);
        }

        var seed = creator.SeedFromDescription(video.Description);
        if (seed is null)
        {
            blockers.Add(
                $"{creator.ChannelName} publishes the seed in the description, and this description does not " +
                "contain one. Without a seed there is nothing to verify against the map, and the space is far " +
                "too large to search.");
            return (null, false);
        }

        var illegal = ManifestValidator.IllegalSeedCharacters(seed);
        if (illegal.Count > 0)
        {
            blockers.Add(
                $"The seed '{seed}' in this description contains {string.Join(", ", illegal.Select(c => $"'{c}'"))}, " +
                "which the game's generator never produces. Either the description has a typo or this is not a seed.");
            return (null, false);
        }

        notes.Add($"Seed '{seed}' read from the description as text, so no character recognition was involved.");
        return (seed, false);
    }

    private static (string? Build, string Basis) ScreenBuild(
        VideoMetadata video, CreatorProfile creator, PatchCalendar calendar,
        List<string> blockers, List<string> notes)
    {
        var stated = creator.BuildFromDescription(video.Description);
        if (stated is not null)
        {
            if (!calendar.Knows(stated))
            {
                notes.Add(
                    $"The description states build {stated}, which this patch calendar does not list. " +
                    "Preflight will decide whether the installed game can represent it.");
            }
            return (stated, BuildStatedByCreator);
        }

        var inference = calendar.InferForUpload(video.UploadedUtc);
        if (inference.IsResolved)
        {
            notes.Add(inference.Reason);
            return (inference.Version, BuildDatedFromUpload);
        }

        blockers.Add(inference.Reason);
        return (null, BuildUnknown);
    }

    private static void NoteChapters(VideoMetadata video, List<string> notes)
    {
        if (video.Chapters.Count == 0) return;

        var first = video.Chapters[0];
        if (first.StartSeconds <= 0.5 && first.EndSeconds > 0)
        {
            notes.Add(
                $"The creator marked '{first.Title}' over the first {first.EndSeconds:0} seconds. " +
                "Gameplay reading should start after it rather than at the file's beginning.");
        }

        notes.Add(
            $"{video.Chapters.Count} creator-marked chapters: " +
            string.Join(", ", video.Chapters.Select(c => $"{c.Title} @{c.StartSeconds:0}s")) +
            ". These are the creator's own segmentation and cost nothing to read.");
    }
}

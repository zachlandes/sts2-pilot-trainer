using System.Diagnostics;
using System.Text.Json;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// Finds recent recordings from the configured creators and decides, from metadata
    /// alone, which of them could be reconstructed at all.
    ///
    /// Nothing here downloads a video, reads a frame or starts an engine. That is the
    /// whole point: acquiring and transcribing a recording is the expensive half of this
    /// pipeline, and a recording with no recoverable seed is not a harder job, it is not
    /// a job. Establishing that from a description and an upload date costs nothing.
    ///
    /// The output is a list for a person to confirm, not a queue that runs itself. What
    /// it produces are candidates - a candidate seed for <c>verify-seed</c> and a
    /// candidate build for <c>preflight</c> - and both are settled by the engine, never
    /// here.
    /// </summary>
    internal static int Discover(string[] args)
    {
        var configPath = Args.Value(args, "--config") ?? "ingestion/creators.json";
        var config = IngestionConfig.Load(configPath);
        var count = ParseCount(Args.Value(args, "--count") ?? "3");
        var outDir = Args.Value(args, "--out") ?? "build/evidence";
        var fromFile = Args.Value(args, "--from");
        var requested = Args.Values(args, "--creator");

        var creators = requested.Count > 0
            ? requested.Select(config.Creator).ToList()
            : config.Creators.ToList();

        var calendar = config.Calendar();
        var metadata = fromFile is not null
            ? LoadMetadataFile(fromFile)
            : creators.SelectMany(creator => FetchRecent(creator, count)).ToList();

        var screenings = new List<CandidateScreening>();
        foreach (var video in metadata)
        {
            var creator = config.ForChannel(video.ChannelId);
            if (creator is null)
            {
                Console.WriteLine($"  skip  {video.VideoId}  from an unconfigured channel ({video.ChannelId})");
                continue;
            }
            screenings.Add(CandidateScreening.Screen(video, creator, calendar));
        }

        Report(screenings);

        var artifact = EvidenceArtifact.Prepare(outDir, "discovery.json");
        artifact.WriteAtomic(JsonSerializer.Serialize(new
        {
            schema = "sts2-pilot-trainer/discovery/v1",
            config = configPath,
            screened = screenings.Count,
            eligible = screenings.Count(s => s.Verdict == ScreeningVerdict.Eligible),
            candidates = screenings,
        }, Json.Indented));

        Console.WriteLine();
        Console.WriteLine($"screened   : {screenings.Count}");
        Console.WriteLine($"report     : {Paths.Display(artifact.Path)}");

        var ready = screenings.Where(s => s.Verdict == ScreeningVerdict.Eligible).ToList();
        if (ready.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("Nothing here can be reconstructed. Ingest nothing.");
            return 0;
        }

        Console.WriteLine();
        Console.WriteLine("Confirm before ingesting. These are the recordings that would be reconstructed:");
        foreach (var candidate in ready)
        {
            Console.WriteLine($"  {candidate.VideoId}  {candidate.ChannelName}  seed {candidate.CandidateSeed}  " +
                              $"build {candidate.CandidateBuild}");
        }
        Console.WriteLine();
        Console.WriteLine("Each still has to show it was recorded from its run's start, and each seed is still a");
        Console.WriteLine("candidate until the engine reproduces the map the recording shows.");
        return 0;
    }

    private static int ParseCount(string raw) =>
        int.TryParse(raw, out var count) && count is > 0 and <= 25
            ? count
            : throw new ManifestException(
                $"--count '{raw}' is not a whole number between 1 and 25. Discovery is deliberately bounded: " +
                "the point is to look at a handful of recent uploads, not to crawl a channel.");

    private static void Report(IReadOnlyList<CandidateScreening> screenings)
    {
        foreach (var screening in screenings)
        {
            var verdict = screening.Verdict switch
            {
                ScreeningVerdict.Eligible => "ok   ",
                ScreeningVerdict.NeedsFrameProbe => "probe",
                _ => "REFUSE",
            };
            Console.WriteLine();
            Console.WriteLine($"  {verdict} {screening.VideoId}  {screening.ChannelName}");
            Console.WriteLine($"         {screening.Title}");
            Console.WriteLine($"         seed  : {screening.CandidateSeed ?? "(not recovered)"}");
            Console.WriteLine($"         build : {screening.CandidateBuild ?? "(not established)"} " +
                              $"[{screening.BuildBasis}]");
            foreach (var blocker in screening.Blockers) Console.WriteLine($"         cannot: {blocker}");
            foreach (var note in screening.Notes) Console.WriteLine($"         note  : {note}");
        }
    }

    /// <summary>
    /// Reads metadata a previous fetch saved. Keeps the demonstration and the tests off
    /// the network, and lets a screening decision be re-examined later against exactly
    /// the bytes it was made from.
    /// </summary>
    private static IReadOnlyList<VideoMetadata> LoadMetadataFile(string path)
    {
        var json = File.ReadAllText(path);
        return ManifestJson.DeserializeRequired<List<VideoMetadata>>(json, $"Video metadata at {path}");
    }

    /// <summary>
    /// Asks the platform for the most recent uploads. This is the one place in the
    /// project that reaches the network, it transfers no media, and it is proof-only
    /// tooling: nothing a published mod ships comes anywhere near it.
    /// </summary>
    private static IReadOnlyList<VideoMetadata> FetchRecent(CreatorProfile creator, int count)
    {
        var url = $"https://www.youtube.com/channel/{creator.ChannelId}/videos";
        var start = Stopwatch.GetTimestamp();
        Console.WriteLine($"fetching   : {creator.ChannelName}, {count} most recent (metadata only)");

        var info = new ProcessStartInfo("yt-dlp")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "--skip-download", "-J", "--playlist-items", $"1:{count}", url })
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)
            ?? throw new ManifestException("Could not start yt-dlp. Discovery needs it on PATH.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new ManifestException(
                $"yt-dlp failed for {creator.ChannelName} (exit {process.ExitCode}). Its extractors go stale " +
                "against the platform on a scale of weeks, so a failure here is usually a stale tool rather " +
                $"than a missing channel.\n{stderr.Trim()}");
        }

        Console.WriteLine($"           : {Stopwatch.GetElapsedTime(start).TotalSeconds:0.0}s");
        return ParseYtDlp(stdout, creator);
    }

    internal static IReadOnlyList<VideoMetadata> ParseYtDlp(string json, CreatorProfile creator)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var entries = root.TryGetProperty("entries", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().ToList()
            : [root];

        var videos = new List<VideoMetadata>();
        foreach (var entry in entries)
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;
            var uploaded = entry.TryGetProperty("upload_date", out var date) && date.GetString() is { } stamp &&
                           DateOnly.TryParseExact(stamp, "yyyyMMdd", out var parsed)
                ? parsed
                : throw new ManifestException(
                    $"An entry from {creator.ChannelName} carries no usable upload_date, so its build cannot " +
                    "be dated. Refusing rather than assuming today.");

            videos.Add(new VideoMetadata
            {
                VideoId = Text(entry, "id") ?? throw new ManifestException("An entry carries no video id."),
                ChannelId = Text(entry, "channel_id") ?? creator.ChannelId,
                ChannelName = Text(entry, "channel") ?? creator.ChannelName,
                Title = Text(entry, "title") ?? "(untitled)",
                Description = Text(entry, "description"),
                DurationSeconds = entry.TryGetProperty("duration", out var d) && d.ValueKind is JsonValueKind.Number
                    ? (int)d.GetDouble()
                    : 0,
                UploadedUtc = uploaded,
                Chapters = Chapters(entry),
            });
        }
        return videos;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<VideoChapter> Chapters(JsonElement entry)
    {
        if (!entry.TryGetProperty("chapters", out var chapters) || chapters.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return chapters.EnumerateArray()
            .Where(chapter => chapter.ValueKind == JsonValueKind.Object)
            .Select(chapter => new VideoChapter(
                Text(chapter, "title") ?? "(untitled)",
                chapter.TryGetProperty("start_time", out var s) ? s.GetDouble() : 0,
                chapter.TryGetProperty("end_time", out var e) ? e.GetDouble() : 0))
            .ToList();
    }
}

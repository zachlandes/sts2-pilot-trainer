using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// The creators this project ingests from, and the release dates it dates recordings
/// against. Data rather than code, because both go stale for reasons that have nothing
/// to do with this repository: a creator changes their layout, and MegaCrit ships a patch.
///
/// The creator list is small and finite by design. It is not a registry to be grown; it
/// is the set of people whose recordings have been established as reconstructible, and a
/// creator is added by demonstrating that, not by adding a row.
/// </summary>
public sealed record IngestionConfig
{
    public const string CurrentSchema = "sts2-pilot-trainer/ingestion-config/v1";

    [JsonPropertyName("schema")]
    public string Schema { get; init; } = CurrentSchema;

    [JsonPropertyName("releases")]
    public required IReadOnlyList<ReleaseEntry> Releases { get; init; }

    [JsonPropertyName("creators")]
    public required IReadOnlyList<CreatorProfile> Creators { get; init; }

    public PatchCalendar Calendar() =>
        new(Releases.Select(entry => new GameRelease(entry.Version, entry.ReleasedUtc)));

    public CreatorProfile Creator(string name) =>
        Creators.FirstOrDefault(creator =>
            string.Equals(creator.ChannelName, name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ManifestException(
            $"No creator profile named '{name}'. This config knows: " +
            string.Join(", ", Creators.Select(creator => creator.ChannelName)) + ".");

    public CreatorProfile? ForChannel(string channelId) =>
        Creators.FirstOrDefault(creator =>
            string.Equals(creator.ChannelId, channelId, StringComparison.Ordinal));

    public static IngestionConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        return ManifestJson.RefuseInvalidJson("Ingestion config", () => Deserialize(json, Path.GetFileName(path)));
    }

    private static IngestionConfig Deserialize(string json, string fileName)
    {
        using var probe = JsonDocument.Parse(json);
        if (!probe.RootElement.TryGetProperty("schema", out var schemaElement))
        {
            throw new ManifestException("Ingestion config has no 'schema'. Refusing to guess which format this is.");
        }

        var schema = schemaElement.ValueKind == JsonValueKind.String ? schemaElement.GetString() : null;
        if (schema != CurrentSchema)
        {
            throw new ManifestException(
                $"Ingestion config schema '{schema}' is not '{CurrentSchema}'. Refusing to read it partially.");
        }

        var config = JsonSerializer.Deserialize<IngestionConfig>(json, ManifestJson.Options)
            ?? throw new ManifestException($"Ingestion config at {fileName} deserialized to null.");
        ManifestJson.ValidateRequiredMembers(config, "Ingestion config");

        if (config.Creators.Count == 0)
        {
            throw new ManifestException("Ingestion config lists no creators, so there is nothing to discover from.");
        }

        var duplicates = config.Creators
            .GroupBy(creator => creator.ChannelId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            throw new ManifestException(
                $"Ingestion config lists channel {string.Join(", ", duplicates)} more than once. " +
                "Two profiles for one channel would read the same recording two different ways.");
        }

        foreach (var creator in config.Creators)
        {
            if (creator.SeedSource == SeedSource.Description && string.IsNullOrWhiteSpace(creator.SeedPattern))
            {
                throw new ManifestException(
                    $"Creator '{creator.ChannelName}' takes its seed from the description but declares no " +
                    "seed_pattern, so nothing would ever be extracted.");
            }
        }

        // Constructing the calendar is what enforces its own rules, so do it now rather
        // than at the first use, where the failure would surface far from its cause.
        _ = config.Calendar();
        return config;
    }

    public sealed record ReleaseEntry(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("released_utc")] DateOnly ReleasedUtc);
}

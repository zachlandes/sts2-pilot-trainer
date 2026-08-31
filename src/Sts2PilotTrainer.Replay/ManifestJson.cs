using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>Reading and writing manifests. Indented and stable-ordered on purpose:
/// a manifest is meant to be read by a person and diffed in review.</summary>
public static class ManifestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Serialize(ReplayManifest manifest) =>
        JsonSerializer.Serialize(manifest, Options);

    /// <summary>
    /// Parses a manifest, refusing anything this build cannot faithfully interpret.
    /// A version it does not know is a refusal rather than a best effort: silently
    /// ignoring a field added by a newer writer is exactly how a replay ends up
    /// exact-looking and wrong.
    /// </summary>
    public static ReplayManifest Deserialize(string json)
    {
        var probe = JsonDocument.Parse(json);
        if (!probe.RootElement.TryGetProperty("manifest_version", out var versionElement))
        {
            throw new ManifestException("Manifest has no 'manifest_version'. Refusing to guess which format this is.");
        }

        var version = versionElement.GetInt32();
        if (version != ReplayManifest.CurrentManifestVersion)
        {
            throw new ManifestException(
                $"Manifest version {version} is not supported by this build " +
                $"(which reads version {ReplayManifest.CurrentManifestVersion}). " +
                "Refusing rather than reading it partially.");
        }

        return JsonSerializer.Deserialize<ReplayManifest>(json, Options)
               ?? throw new ManifestException("Manifest deserialized to null.");
    }

    public static ReplayManifest Load(string path) => Deserialize(File.ReadAllText(path));

    public static void Save(ReplayManifest manifest, string path) =>
        File.WriteAllText(path, Serialize(manifest) + "\n");
}

public sealed class ManifestException(string message) : Exception(message);

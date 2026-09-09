using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>Reading and writing manifests. Indented and stable-ordered on purpose:
/// a manifest is meant to be read by a person and diffed in review.</summary>
public static class ManifestJson
{
    private static readonly NullabilityInfoContext Nullability = new();
    private static readonly object NullabilityLock = new();

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
    public static ReplayManifest Deserialize(string json) =>
        RefuseInvalidJson("Manifest", () => DeserializeCore(json));

    private static ReplayManifest DeserializeCore(string json)
    {
        using var probe = JsonDocument.Parse(json);
        if (!probe.RootElement.TryGetProperty("manifest_version", out var versionElement))
        {
            throw new ManifestException("Manifest has no 'manifest_version'. Refusing to guess which format this is.");
        }

        var version = versionElement.GetInt32();
        if (version == PreviousManifestVersion) return MigrateFromVersion5(json);

        if (version != ReplayManifest.CurrentManifestVersion)
        {
            throw new ManifestException(
                $"Manifest version {version} is not supported by this build " +
                $"(which reads version {ReplayManifest.CurrentManifestVersion}, and migrates version " +
                $"{PreviousManifestVersion} in memory). Refusing rather than reading it partially.");
        }

        var manifest = JsonSerializer.Deserialize<ReplayManifest>(json, Options)
            ?? throw new ManifestException("Manifest deserialized to null.");
        ValidateRequiredMembers(manifest, "Manifest");
        return manifest;
    }

    /// <summary>
    /// The one older version this build still reads. Version 5 carried no integrity
    /// on a native source and named no event option by key; version 6 requires the
    /// first and, of a native recording, the second.
    /// </summary>
    public const int PreviousManifestVersion = 5;

    /// <summary>
    /// Reads a version-5 manifest as the version-6 manifest it means.
    ///
    /// In memory and never on disk: a file on disk is migrated once, deliberately, by
    /// <c>arbiter migrate-manifest</c>, so a reader can never silently rewrite
    /// somebody's evidence. Nothing about the run is invented. A native source gains
    /// an integrity where it stated none - <c>complete</c>, because a version-5
    /// recorder had no unmapped stop and refused rather than stopping at anything it
    /// could not name; one it did state is kept as it was - and it declares that it
    /// was migrated from 5, which is what lets the validator waive the option keys no
    /// version-5 recorder read. A native recording also gains, at every floor arrival
    /// its boundaries declare, the checkpoint a version-5 recorder could not write: it
    /// sampled no map coordinate, and the arrival is derived through
    /// <see cref="FloorArrival"/> - the one owner of that derivation - from the map move
    /// the boundary already names, marked inferred with its reasoning, exactly as the
    /// validator re-derives it. No action and no boundary is touched, so the history
    /// hash and every captured digest stay exactly what they were.
    /// </summary>
    private static ReplayManifest MigrateFromVersion5(string json)
    {
        var node = JsonNode.Parse(json)?.AsObject()
            ?? throw new ManifestException("Manifest deserialized to null.");
        node["manifest_version"] = ReplayManifest.CurrentManifestVersion;

        var source = node["source"]?.AsObject();
        if (source?["kind"]?.GetValue<string>() == "native" && source["native"] is JsonObject native)
        {
            native["integrity"] ??= NativeSource.CompleteIntegrity;
            native["migrated_from_version"] = PreviousManifestVersion;
        }

        var migrated = JsonSerializer.Deserialize<ReplayManifest>(node.ToJsonString(), Options)
            ?? throw new ManifestException("Manifest deserialized to null.");
        ValidateRequiredMembers(migrated, "Manifest");
        return migrated.Source.Native is null ? migrated : FloorArrival.WithArrivalCheckpoints(migrated);
    }

    public static ReplayManifest Load(string path) => Deserialize(File.ReadAllText(path));

    public static T DeserializeRequired<T>(string json, string contractName) where T : class =>
        RefuseInvalidJson(contractName, () =>
        {
            var value = JsonSerializer.Deserialize<T>(json, Options)
                ?? throw new ManifestException($"{contractName} deserialized to null.");
            ValidateRequiredMembers(value, contractName);
            return value;
        });

    internal static T RefuseInvalidJson<T>(string contractName, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (ManifestException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or InvalidOperationException or FormatException)
        {
            throw new ManifestException($"{contractName} JSON is invalid: {exception.Message}");
        }
    }

    public static void Save(ReplayManifest manifest, string path) =>
        File.WriteAllText(path, Serialize(manifest) + "\n");

    internal static void ValidateRequiredMembers(object value, string contractName)
    {
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        ValidateRequiredMembers(value, contractName, visited);
    }

    private static void ValidateRequiredMembers(object value, string path, HashSet<object> visited)
    {
        var type = value.GetType();
        if (type.IsValueType || value is string || !visited.Add(value)) return;

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
            {
                if (entry.Value is null)
                {
                    throw new ManifestException($"{path} contains a null value.");
                }
                ValidateRequiredMembers(entry.Value, $"{path}[{entry.Key}]", visited);
            }
            return;
        }

        if (value is IEnumerable sequence)
        {
            var index = 0;
            foreach (var item in sequence)
            {
                if (item is null)
                {
                    throw new ManifestException($"{path}[{index}] is null.");
                }
                ValidateRequiredMembers(item, $"{path}[{index}]", visited);
                index++;
            }
            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => property.CanRead && property.GetIndexParameters().Length == 0))
        {
            var propertyValue = property.GetValue(value);
            var propertyName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
            var propertyPath = $"{path}.{propertyName}";
            if (propertyValue is null)
            {
                if (property.GetCustomAttribute<RequiredMemberAttribute>() is not null ||
                    NullabilityReadState(property) == NullabilityState.NotNull)
                {
                    throw new ManifestException($"{propertyPath} is required and cannot be null.");
                }
                continue;
            }
            ValidateRequiredMembers(propertyValue, propertyPath, visited);
        }
    }

    private static NullabilityState NullabilityReadState(PropertyInfo property)
    {
        lock (NullabilityLock)
        {
            return Nullability.Create(property).ReadState;
        }
    }
}

public sealed class ManifestException(string message) : Exception(message);

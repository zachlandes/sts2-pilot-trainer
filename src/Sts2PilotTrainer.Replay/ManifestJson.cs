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
        if (version == PreviousManifestVersion) return MigrateFromVersion4(json);

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
    /// The one older version this build still reads. Version 4 carried a single
    /// combat-start digest on the source; version 5 carries a list of boundaries, and
    /// the old scalar is the first entry of that list.
    /// </summary>
    public const int PreviousManifestVersion = 4;

    /// <summary>
    /// Reads a version-4 manifest as the version-5 manifest it means.
    ///
    /// In memory and never on disk: a file on disk is migrated once, deliberately, by
    /// <c>arbiter migrate-manifest</c>, so a reader can never silently rewrite
    /// somebody's evidence. Nothing is invented here - the digest was engine-produced
    /// and stays engine-produced, and where the first fight begins is read the same
    /// way the version-4 entry path read it.
    /// </summary>
    private static ReplayManifest MigrateFromVersion4(string json)
    {
        var node = JsonNode.Parse(json)?.AsObject()
            ?? throw new ManifestException("Manifest deserialized to null.");
        node["manifest_version"] = ReplayManifest.CurrentManifestVersion;

        var source = node["source"]?.AsObject();
        var digest = source?["combat_start_snapshot_digest"]?.DeepClone();
        source?.Remove("combat_start_snapshot_digest");
        node.Remove("boundaries");

        var migrated = JsonSerializer.Deserialize<ReplayManifest>(node.ToJsonString(), Options)
            ?? throw new ManifestException("Manifest deserialized to null.");
        ValidateRequiredMembers(migrated, "Manifest");

        if (digest is null) return migrated;

        var fact = digest.Deserialize<Fact<string>>(Options)
            ?? throw new ManifestException(
                "This version-4 manifest's combat_start_snapshot_digest could not be read, so its boundary " +
                "cannot be migrated.");

        int combatStartSeq;
        try
        {
            combatStartSeq = RecordedFightPlan.FirstCombatStartSeq(migrated);
        }
        catch (ManifestException refusal)
        {
            throw new ManifestException(
                $"This version-{PreviousManifestVersion} manifest declares a combat-start digest and its history " +
                $"does not reach a fight, so where that boundary sits cannot be established: {refusal.Message}");
        }

        return migrated with
        {
            Boundaries = [ReplayBoundary.CombatStart(fight: 1, afterSeq: combatStartSeq, digest: fact)],
        };
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

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
        if (version == ReplayManifest.CurrentManifestVersion)
        {
            var manifest = JsonSerializer.Deserialize<ReplayManifest>(json, Options)
                ?? throw new ManifestException("Manifest deserialized to null.");
            ValidateRequiredMembers(manifest, "Manifest");
            return manifest;
        }

        if (!MigratedVersions.Contains(version))
        {
            throw new ManifestException(
                $"Manifest version {version} is not supported by this build " +
                $"(which reads version {ReplayManifest.CurrentManifestVersion}, and migrates versions " +
                $"{string.Join(" and ", MigratedVersions)} in memory). Refusing rather than reading it partially.");
        }

        var versionSeven = version switch
        {
            OldestMigratedVersion => MigrateFromVersion6(MigrateFromVersion5(json), version),
            FinishedFightResidueManifestVersion => MigrateFromVersion6(json, version),
            PreviousManifestVersion => ReadVersion7(json),
            _ => throw new ManifestException($"Manifest version {version} has no migration path."),
        };
        return MigrateFromVersion7(versionSeven, version);
    }

    /// <summary>
    /// The older versions this build still reads, each migrated in memory to the
    /// current one. Version 5 carried no integrity on a native source and named no
    /// event option by key; version 6 required the first and, of a native recording,
    /// the second. Version 6 projected a finished fight into every state after it
    /// until the next fight; version 7 projects nothing of a fight outside a live one.
    /// Version 8 adds the recorder's patch-roster reading at run end; older files keep
    /// its absence and say which format they predate.
    /// </summary>
    public static readonly int[] MigratedVersions = [5, 6, 7];

    /// <summary>The version before this one: what the newest migration reads.</summary>
    public const int PreviousManifestVersion = 7;

    /// <summary>The version whose finished-fight projection is migrated by name.</summary>
    public const int FinishedFightResidueManifestVersion = 6;

    /// <summary>The oldest version this build still reads.</summary>
    public const int OldestMigratedVersion = 5;

    /// <summary>
    /// Reads a version-5 manifest as the version-6 manifest it means, as text, for
    /// the version-6 migration to read on.
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
    private static string MigrateFromVersion5(string json)
    {
        var node = JsonNode.Parse(json)?.AsObject()
            ?? throw new ManifestException("Manifest deserialized to null.");
        node["manifest_version"] = FinishedFightResidueManifestVersion;

        var source = node["source"]?.AsObject();
        if (source?["kind"]?.GetValue<string>() == "native" && source["native"] is JsonObject native)
        {
            native["integrity"] ??= NativeSource.CompleteIntegrity;
            native["migrated_from_version"] = OldestMigratedVersion;
        }

        return node.ToJsonString();
    }

    /// <summary>
    /// Reads a version-6 manifest as the version-7 manifest it means.
    ///
    /// Version 6 projected a finished fight into every state after it until the next
    /// fight, and version 7 projects nothing of a fight outside a live one, so a
    /// version-6 checkpoint taken outside a live fight expects fields this build never
    /// produces. <see cref="FinishedFightResidue"/> owns which those are and takes
    /// them away; nothing else in the file is read differently, and no digest is
    /// touched - a digest is a hash of the whole state and cannot be migrated, so a
    /// version-6 boundary at a floor arrival with no live fight after a fight is left
    /// as the older claim it is, marked on the boundary as one hashed under the
    /// version the file declared (<see cref="ReplayBoundary.Projection"/>), which the
    /// gate names and <c>migrate-manifest --derive-boundaries</c> re-derives. A native
    /// source declares the oldest format the file was written in, so the validator
    /// can say what it predates; where a version-5 file passed through here it keeps
    /// saying 5, and it is what a captured branch's readings are held against. A
    /// native recording migrated from 5 also gains the arrival
    /// checkpoints described on <see cref="MigrateFromVersion5"/>, here, because that
    /// derivation reads the typed manifest.
    /// </summary>
    private static ReplayManifest MigrateFromVersion6(string json, int writtenIn)
    {
        var node = JsonNode.Parse(json)?.AsObject()
            ?? throw new ManifestException("Manifest deserialized to null.");
        node["manifest_version"] = PreviousManifestVersion;

        var source = node["source"]?.AsObject();
        if (source?["kind"]?.GetValue<string>() == "native" && source["native"] is JsonObject native)
        {
            native["migrated_from_version"] ??= writtenIn;
        }

        var migrated = JsonSerializer.Deserialize<ReplayManifest>(node.ToJsonString(), Options)
            ?? throw new ManifestException("Manifest deserialized to null.");
        ValidateRequiredMembers(migrated, "Manifest");
        migrated = FinishedFightResidue.ReadFromOlderFormat(migrated, writtenIn);
        return migrated.Source.Native is null || writtenIn != OldestMigratedVersion
            ? migrated
            : FloorArrival.WithArrivalCheckpoints(migrated);
    }

    /// <summary>Reads a version-7 file into the current type without inventing the
    /// run-end patch roster its recorder never captured.</summary>
    private static ReplayManifest ReadVersion7(string json)
    {
        var node = JsonNode.Parse(json)?.AsObject()
            ?? throw new ManifestException("Manifest deserialized to null.");
        node["manifest_version"] = ReplayManifest.CurrentManifestVersion;
        var migrated = JsonSerializer.Deserialize<ReplayManifest>(node.ToJsonString(), Options)
            ?? throw new ManifestException("Manifest deserialized to null.");
        ValidateRequiredMembers(migrated, "Manifest");
        return migrated;
    }

    /// <summary>
    /// Reads a version-7 manifest as the version-8 manifest it means.
    ///
    /// Version 8 records Harmony's registry again at run end. Nothing can recover
    /// that reading from an older file, so migration keeps it absent and records the
    /// format the native file was written in. The validator then distinguishes an old
    /// recording whose recorder could not take the reading from a current recording
    /// that skipped it.
    /// </summary>
    private static ReplayManifest MigrateFromVersion7(ReplayManifest manifest, int writtenIn)
    {
        var source = manifest.Source;
        if (source.Native is { } native)
        {
            source = source with
            {
                Native = native with { MigratedFromVersion = native.MigratedFromVersion ?? writtenIn },
            };
        }

        return manifest with
        {
            ManifestVersion = ReplayManifest.CurrentManifestVersion,
            Source = source,
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

public class ManifestException(string message) : Exception(message);

/// <summary>A run journal in a schema this build does not read: the one refusal out of
/// <see cref="RunJournal.Parse"/> that says nothing about the recording, as opposed to
/// a journal in this build's own schema that is malformed.</summary>
public sealed class UnreadableJournalSchemaException(string message) : ManifestException(message);

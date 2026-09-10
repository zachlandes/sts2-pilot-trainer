using System.Text;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The game's own scene files, read straight out of the shipped pack.
///
/// Every native text role names a node in one of these scenes, and nothing else can
/// check that the node is there. The client resolves it through Godot, which is not
/// running under <c>dotnet test</c> - <c>GodotStubs</c> loads no resources - so a
/// wrong path used to reach a player before it reached a test. Reading the pack is
/// how a test asks this build the same question the client asks.
///
/// The pack is a read-only input like the rest of the installation, and nothing
/// extracted from it is written anywhere: the scene text is parsed in memory and
/// dropped.
/// </summary>
internal static class NativeScenes
{
    private const string PackOverride = "STS2_GAME_PCK";

    /// <summary>The shipped pack, or null where this machine has no installation.</summary>
    internal static string? Pack { get; } = FindPack();

    internal static string SkipReason =>
        $"Needs the game's own pack file from a Slay the Spire 2 installation. Set {PackOverride} " +
        "for an installation outside Steam's usual directories.";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Entries =
        new(() => ReadDirectory(Pack ?? throw new InvalidOperationException(SkipReason)));

    /// <summary>The text of one <c>res://</c> resource, or null where this build has
    /// no such file.</summary>
    internal static string? Read(string resourcePath)
    {
        var name = resourcePath.StartsWith("res://", StringComparison.Ordinal)
            ? resourcePath["res://".Length..]
            : resourcePath;
        return Entries.Value.TryGetValue(name, out var text) ? text : null;
    }

    /// <summary>
    /// The nodes of one scene, by the path <c>Node.GetNodeOrNull</c> would take to
    /// each: <c>"Parent/Child"</c> for an ordinary node, and additionally
    /// <c>"%Child"</c> for one the scene marks unique in its owner, which is the
    /// spelling the roles use.
    /// </summary>
    internal static IReadOnlyDictionary<string, SceneNode> Nodes(string sceneText)
    {
        var nodes = new Dictionary<string, SceneNode>(StringComparer.Ordinal);
        foreach (var section in Sections(sceneText))
        {
            if (!section.Header.StartsWith("node ", StringComparison.Ordinal)) continue;

            var name = Attribute(section.Header, "name");
            if (name is null) continue;
            var parent = Attribute(section.Header, "parent");

            // The root declares no parent; a child of the root declares ".".
            if (parent is null) continue;
            var path = parent is "." ? name : $"{parent}/{name}";

            var node = new SceneNode(
                path,
                Attribute(section.Header, "type") ?? "",
                section.Body.Contains("theme_override_fonts/font =", StringComparison.Ordinal) ||
                section.Body.Contains("theme_override_fonts/normal_font =", StringComparison.Ordinal));
            nodes[path] = node;

            if (section.Body.Contains("unique_name_in_owner = true", StringComparison.Ordinal))
            {
                nodes[$"%{name}"] = node;
            }
        }

        return nodes;
    }

    internal readonly record struct SceneNode(string Path, string Type, bool CarriesAFont);

    private readonly record struct Section(string Header, string Body);

    private static IEnumerable<Section> Sections(string text)
    {
        var header = (string?)null;
        var body = new StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            if (line.StartsWith('[') && line.TrimEnd('\r').EndsWith(']'))
            {
                if (header is not null) yield return new Section(header, body.ToString());
                header = line.TrimEnd('\r')[1..^1];
                body.Clear();
                continue;
            }

            body.Append(line).Append('\n');
        }

        if (header is not null) yield return new Section(header, body.ToString());
    }

    private static string? Attribute(string header, string name)
    {
        var key = $"{name}=\"";
        var start = header.IndexOf(key, StringComparison.Ordinal);
        if (start < 0) return null;
        start += key.Length;
        var end = header.IndexOf('"', start);
        return end < 0 ? null : header[start..end];
    }

    private static string? FindPack()
    {
        var declared = Environment.GetEnvironmentVariable(PackOverride);
        if (!string.IsNullOrEmpty(declared)) return File.Exists(declared) ? declared : null;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] directories =
        [
            Path.Combine(
                home,
                "Library/Application Support/Steam/steamapps/common/Slay the Spire 2/" +
                "SlayTheSpire2.app/Contents/Resources"),
            Path.Combine(home, ".steam/steam/steamapps/common/Slay the Spire 2"),
            Path.Combine(home, ".local/share/Steam/steamapps/common/Slay the Spire 2"),
            @"C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2",
        ];

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory)) continue;
            var pack = Directory.GetFiles(directory, "*.pck").FirstOrDefault();
            if (pack is not null) return pack;
        }

        return null;
    }

    /// <summary>
    /// Godot's own pack directory. Version 3 (Godot 4.5) keeps the directory at the
    /// end of the file and names its offset in the header; earlier versions put it
    /// straight after the reserved block. Only text resources are kept, because that
    /// is all a scene path can be.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ReadDirectory(string pack)
    {
        using var stream = File.OpenRead(pack);
        using var reader = new BinaryReader(stream, Encoding.UTF8);

        if (new string(reader.ReadChars(4)) != "GDPC")
        {
            throw new InvalidDataException($"'{pack}' is not a Godot pack file.");
        }

        var version = reader.ReadUInt32();
        reader.ReadUInt32();
        reader.ReadUInt32();
        reader.ReadUInt32();
        var packFlags = reader.ReadUInt32();
        var fileBase = reader.ReadUInt64();
        if (version >= 3)
        {
            stream.Position = (long)reader.ReadUInt64();
        }
        else
        {
            stream.Seek(16 * sizeof(uint), SeekOrigin.Current);
        }

        // Offsets are relative to the first file rather than to the start of the pack
        // when the pack says so.
        const uint OffsetsAreRelative = 2;
        var origin = (packFlags & OffsetsAreRelative) != 0 ? (long)fileBase : 0;

        var count = reader.ReadUInt32();
        var directory = new List<(string Name, long Offset, int Size)>((int)count);
        for (var i = 0; i < count; i++)
        {
            var nameLength = (int)reader.ReadUInt32();
            var name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength)).TrimEnd('\0');
            var offset = (long)reader.ReadUInt64();
            var size = (long)reader.ReadUInt64();
            reader.ReadBytes(16);
            reader.ReadUInt32();
            if (name.EndsWith(".tscn", StringComparison.Ordinal) && size < int.MaxValue)
            {
                directory.Add((name, offset + origin, (int)size));
            }
        }

        var contents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, offset, size) in directory)
        {
            stream.Position = offset;
            contents[name] = Encoding.UTF8.GetString(reader.ReadBytes(size));
        }

        return contents;
    }
}

/// <summary>
/// A fact that reads the game's own scene files. Skips with an explanation where
/// there is no installation, the same bargain <c>GameFact</c> strikes.
/// </summary>
public sealed class NativeSceneFactAttribute : FactAttribute
{
    public NativeSceneFactAttribute()
    {
        if (NativeScenes.Pack is null) Skip = NativeScenes.SkipReason;
    }
}

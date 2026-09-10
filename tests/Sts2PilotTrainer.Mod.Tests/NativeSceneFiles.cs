using System.Text;
using System.Text.Json.Nodes;

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
    private const string PreparedReleaseInfo = "release_info.json.copy";

    /// <summary>Where the pack was looked for, and why the look failed where it
    /// did: a declared path that is not there is a different fact from no
    /// installation, and the reason is what a developer needs.</summary>
    internal readonly record struct PackSearch(string? Path, string? Fault);

    /// <summary>The build a set of scene files belongs to. <c>commit</c> and
    /// <c>main_assembly_hash</c> are what decide identity; the version is carried
    /// because it is what a developer recognises.</summary>
    internal readonly record struct BuildIdentity(string Version, string Commit, long MainAssemblyHash);

    internal enum AvailabilityState
    {
        Run,
        NotPrepared,
        NoPack,
        BuildMismatch,
    }

    internal readonly record struct Availability(AvailabilityState State, string Message);

    /// <summary>
    /// What a native-scene fact does on this machine, from the four facts that decide it.
    ///
    /// Every outcome that is not <see cref="AvailabilityState.Run"/> is a skip that says
    /// which one it is, and none of them is silent. The pack comes out of the live
    /// installation while the role table belongs to the assemblies in <c>build/lib</c>,
    /// so a Steam update since the last bootstrap is a skip and not a red: failing there
    /// would accuse the role table of a defect whose real cause is a different game
    /// build. Where the two agree the facts run, and a wrong node path fails - that case
    /// is the one this whole check exists for and it is never softened.
    /// </summary>
    internal static Availability Decide(
        bool prepared,
        BuildIdentity? preparedBuild,
        PackSearch pack,
        BuildIdentity? installed)
    {
        if (!prepared)
        {
            return new Availability(
                AvailabilityState.NotPrepared,
                "No prepared game assembly. Run ./scripts/build.sh, which copies your own " +
                "Slay the Spire 2 installation into build/lib without modifying it.");
        }

        if (pack.Path is null)
        {
            return new Availability(
                AvailabilityState.NoPack,
                (pack.Fault ?? "No .pck was found beside an installed Slay the Spire 2.") +
                $" Set {PackOverride} to the pack file to point these facts at an installation. " +
                "Note that ./scripts/bootstrap.sh --archive prepares assemblies only and copies " +
                "no pack, so a prepared build is not by itself a pack.");
        }

        if (preparedBuild is not { } prepared_ || installed is not { } live)
        {
            var missing = preparedBuild is null
                ? $"build/lib/{PreparedReleaseInfo}"
                : "the installation's own release_info.json";
            return new Availability(
                AvailabilityState.BuildMismatch,
                $"Cannot tell which build these scene files belong to: {missing} could not be " +
                "read, so the pack cannot be matched to the prepared assemblies. Rebuild " +
                "against the installed game with ./scripts/build.sh.");
        }

        if (prepared_.Commit != live.Commit || prepared_.MainAssemblyHash != live.MainAssemblyHash)
        {
            return new Availability(
                AvailabilityState.BuildMismatch,
                $"Prepared {prepared_.Version} (commit {prepared_.Commit}) but the installed game " +
                $"is {live.Version} (commit {live.Commit}), so this pack's scene files are not the " +
                "ones the role table is compiled against. Rebuild against the installed game with " +
                "./scripts/build.sh.");
        }

        return new Availability(AvailabilityState.Run, "");
    }

    private static readonly PackSearch Search = FindPack();

    /// <summary>This machine's answer, derived once.</summary>
    internal static Availability Here { get; } = Decide(
        Arbiter.GamePrepared,
        ReadIdentity(Path.Combine(Arbiter.RepoRoot, "build", "lib", PreparedReleaseInfo)),
        Search,
        Search.Path is { } pack ? ReadIdentity(FindReleaseInfo(pack)) : null);

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Entries = new(() =>
        Here.State is AvailabilityState.Run
            ? ReadDirectory(Search.Path!)
            : throw new InvalidOperationException(Here.Message));

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

    private static PackSearch FindPack()
    {
        var declared = Environment.GetEnvironmentVariable(PackOverride);
        if (!string.IsNullOrEmpty(declared))
        {
            return File.Exists(declared)
                ? new PackSearch(declared, null)
                : new PackSearch(null, $"{PackOverride} names '{declared}', which is not a file.");
        }

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
            if (pack is not null) return new PackSearch(pack, null);
        }

        return new PackSearch(null, null);
    }

    /// <summary>
    /// The build identity one <c>release_info.json</c> publishes, or null where it
    /// cannot be read. The game's own file and the bootstrapper's copy of it are the
    /// same shape, which is what lets one reader answer for both.
    /// </summary>
    private static BuildIdentity? ReadIdentity(string? path)
    {
        if (path is null || !File.Exists(path)) return null;

        try
        {
            var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            return new BuildIdentity(
                json["version"]!.GetValue<string>(),
                json["commit"]!.GetValue<string>(),
                json["main_assembly_hash"]!.GetValue<long>());
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The installation's own <c>release_info.json</c>, resolved from where the pack
    /// was found the way the bootstrapper resolves it from the assembly: beside it on
    /// macOS, and up to two directories above elsewhere.
    /// </summary>
    private static string? FindReleaseInfo(string pack)
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(pack)!);
        for (var i = 0; i < 3 && dir is not null; i++, dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "release_info.json");
            if (File.Exists(path)) return path;
        }

        return null;
    }

    /// <summary>
    /// Godot's own pack directory. Version 3 (Godot 4.5) keeps the directory at the end
    /// of the file and names its offset in the header, and version 3 is the only layout
    /// read here: the mod compiles against one game build, so an older pack is refused
    /// by name rather than parsed by a second rule this repository has no example of.
    /// Only text resources are kept, because that is all a scene path can be.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ReadDirectory(string pack)
    {
        using var stream = File.OpenRead(pack);
        using var reader = new BinaryReader(stream, Encoding.UTF8);

        if (new string(reader.ReadChars(4)) != "GDPC")
        {
            throw new InvalidDataException($"'{pack}' is not a Godot pack file.");
        }

        const uint SupportedVersion = 3;
        var version = reader.ReadUInt32();
        if (version != SupportedVersion)
        {
            throw new InvalidDataException(
                $"'{pack}' is a version {version} Godot pack; only version {SupportedVersion} " +
                "(Godot 4.5) is read here.");
        }

        reader.ReadUInt32();
        reader.ReadUInt32();
        reader.ReadUInt32();
        var packFlags = reader.ReadUInt32();
        var fileBase = reader.ReadUInt64();
        stream.Position = (long)reader.ReadUInt64();

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
/// A fact that reads the game's own scene files. It runs where the installed pack is
/// the build the role table is compiled against, and otherwise skips saying which of
/// the three reasons it was - never silently, and never as a red that blames the role
/// table for a Steam update. <c>NativeScenes.Decide</c> owns that answer.
/// </summary>
public sealed class NativeSceneFactAttribute : FactAttribute
{
    public NativeSceneFactAttribute()
    {
        if (NativeScenes.Here.State is not NativeScenes.AvailabilityState.Run)
        {
            Skip = NativeScenes.Here.Message;
        }
    }
}

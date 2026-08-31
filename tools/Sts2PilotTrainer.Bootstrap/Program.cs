using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Sts2PilotTrainer.Bootstrap;

/// <summary>
/// Prepares a private, loadable copy of the installed Slay the Spire 2 assembly.
///
/// The installed game is a read-only input. Everything this tool writes lands in
/// the output directory (gitignored <c>build/lib</c> by default); the install is
/// hashed before and after and the run fails if a single byte moved.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Assemblies the headless host needs beside <c>sts2.dll</c>. Godot itself is
    /// not among them: <c>GodotSharp.dll</c> is supplied by third_party/godot-stubs.
    /// </summary>
    private static readonly string[] RequiredAssemblies =
    [
        "sts2.dll",
        "SmartFormat.dll",
        "SmartFormat.ZString.dll",
        "Sentry.dll",
        "Sentry.Godot.dll",
        "Steamworks.NET.dll",
        "MonoMod.Backports.dll",
        "MonoMod.ILHelpers.dll",
        "0Harmony.dll",
        "System.IO.Hashing.dll",
    ];

    private static int Main(string[] args)
    {
        try
        {
            var gameDir = ArgValue(args, "--game-dir") ?? DetectGameDir();
            var outDir = Path.GetFullPath(ArgValue(args, "--out") ?? "build/lib");

            if (gameDir is null || !Directory.Exists(gameDir))
            {
                Fail("""
                     Could not locate a Slay the Spire 2 installation.

                     Pass one explicitly:
                       dotnet run --project tools/Sts2PilotTrainer.Bootstrap -- --game-dir <path>

                     The path wanted is the directory holding sts2.dll, e.g. on macOS
                       .../Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64
                     """);
                return 2;
            }

            gameDir = ResolvePath(Path.GetFullPath(gameDir));
            outDir = ResolvePath(outDir);
            RefuseProtectedOutput(gameDir, outDir);

            Console.WriteLine($"game install : {Redact(gameDir)}");
            var identity = ReadInstalledIdentity(gameDir);
            Console.WriteLine($"build        : {identity.Version} ({identity.BuildDateUtc}) commit {identity.Commit}");

            var before = HashInstall(gameDir);

            Directory.CreateDirectory(outDir);
            var copied = CopyAssemblies(gameDir, outDir);
            CopyReleaseInfo(gameDir, outDir);
            var patches = PatchAssembly(Path.Combine(outDir, "sts2.dll"));

            var after = HashInstall(gameDir);
            if (before != after)
            {
                Fail($"The game install changed during bootstrap ({before} -> {after}). Refusing to continue.");
                return 3;
            }
            Console.WriteLine($"install sha256 unchanged: {before[..16]}...");

            var outputHashes = HashPreparedOutputs(outDir, copied);
            WriteReceipt(outDir, identity, before, copied, patches, outputHashes);
            Console.WriteLine($"prepared     : {copied.Count} assemblies, {patches.Count} IL patches -> {Relative(outDir)}");
            return 0;
        }
        catch (Exception ex)
        {
            Fail($"{ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    // ── Locating and identifying the install ────────────────────────────────

    private static string? DetectGameDir()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates =
        [
            // macOS, Apple Silicon then Intel
            Path.Combine(home, "Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64"),
            Path.Combine(home, "Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_x86_64"),
            // Linux
            Path.Combine(home, ".steam/steam/steamapps/common/Slay the Spire 2"),
            Path.Combine(home, ".local/share/Steam/steamapps/common/Slay the Spire 2"),
            // Windows
            @"C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2",
        ];

        foreach (var c in candidates)
        {
            if (File.Exists(Path.Combine(c, "sts2.dll"))) return c;
            // Windows/Linux ship the assembly in a data_* subdirectory too.
            if (!Directory.Exists(c)) continue;
            var nested = Directory.GetDirectories(c, "data_sts2_*")
                .FirstOrDefault(d => File.Exists(Path.Combine(d, "sts2.dll")));
            if (nested is not null) return nested;
        }
        return null;
    }

    /// <summary>
    /// Reads the build identity the game itself publishes. <c>release_info.json</c>
    /// sits two directories above the assembly on macOS and beside it elsewhere.
    /// </summary>
    private static InstalledIdentity ReadInstalledIdentity(string gameDir)
    {
        var path = FindReleaseInfo(gameDir)
                   ?? throw new FileNotFoundException("release_info.json not found near the game assembly.");
        var json = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        var rawDate = json["date"]!.GetValue<string>();
        // The in-game version overlay renders this timestamp in UTC, so a build
        // stamped 2026-08-13T17:39-07:00 shows as "2026.08.14". Normalise here or
        // every comparison against an observed overlay is off by a day.
        var utcDate = DateTimeOffset.Parse(rawDate, System.Globalization.CultureInfo.InvariantCulture)
            .ToUniversalTime().ToString("yyyy.MM.dd");

        return new InstalledIdentity(
            Version: json["version"]!.GetValue<string>(),
            BuildDateUtc: utcDate,
            Commit: json["commit"]!.GetValue<string>(),
            Branch: json["branch"]!.GetValue<string>(),
            MainAssemblyHash: json["main_assembly_hash"]!.GetValue<long>());
    }

    private static string? FindReleaseInfo(string gameDir)
    {
        var dir = new DirectoryInfo(gameDir);
        for (var i = 0; i < 3 && dir is not null; i++, dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, "release_info.json");
            if (File.Exists(p)) return p;
        }
        return null;
    }

    // ── Copy and patch ──────────────────────────────────────────────────────

    private static List<string> CopyAssemblies(string gameDir, string outDir)
    {
        var copied = new List<string>();
        foreach (var name in RequiredAssemblies)
        {
            var src = Path.Combine(gameDir, name);
            if (!File.Exists(src))
            {
                Console.WriteLine($"  - {name} (absent in this build; skipped)");
                continue;
            }
            File.Copy(src, Path.Combine(outDir, name), overwrite: true);
            copied.Add(name);
        }
        if (!copied.Contains("sts2.dll"))
            throw new FileNotFoundException($"sts2.dll not found in {Redact(gameDir)}");
        return copied;
    }

    /// <summary>
    /// The game reads its own version from release_info.json at runtime. Placing a
    /// copy beside the prepared assembly means the engine reports its real identity
    /// rather than a default, which is what the preflight compares against.
    /// </summary>
    private static void CopyReleaseInfo(string gameDir, string outDir)
    {
        var src = FindReleaseInfo(gameDir);
        if (src is null) return;
        File.Copy(src, Path.Combine(outDir, "release_info.json"), overwrite: true);
    }

    /// <summary>
    /// Every IL patch applied to the private copy, why it exists, and the promise
    /// that it does not change which actions the engine takes. Each must apply at
    /// least once: a patch that silently stops matching is version drift, and the
    /// only safe response is a loud failure.
    /// </summary>
    private static readonly IlPatch[] DeclaredPatches =
    [
        new IlPatch(
            Name: "combat-queue-wait-completes",
            Type: "CombatManager",
            Method: "WaitUntilQueueIsEmptyOrWaitingOnNonPlayerDrivenAction",
            Rationale:
                "The headless host drains the game action queue inline on a synchronous " +
                "SynchronizationContext, so the queue is already empty by the time this " +
                "wait is awaited. Left intact, the await never resumes - there is no " +
                "frame loop to pump it. Returning a completed Task changes when the " +
                "caller resumes, not which actions ran or which RNG streams advanced.")
    ];

    private static List<AppliedPatch> PatchAssembly(string dllPath)
    {
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(dllPath)!);

        using var module = ModuleDefinition.ReadModule(
            dllPath,
            new ReaderParameters { AssemblyResolver = resolver, ReadingMode = ReadingMode.Deferred, ReadWrite = false });

        var applied = new List<AppliedPatch>();
        foreach (var patch in DeclaredPatches)
        {
            var hits = 0;
            foreach (var type in module.Types)
            {
                if (type.Name != patch.Type) continue;
                foreach (var method in type.Methods)
                {
                    if (method.Name != patch.Method || method.Body is null) continue;
                    var il = method.Body.GetILProcessor();
                    il.Body.Instructions.Clear();
                    il.Body.ExceptionHandlers.Clear();
                    il.Body.Variables.Clear();
                    var completedTask = module.ImportReference(
                        typeof(System.Threading.Tasks.Task).GetProperty("CompletedTask")!.GetGetMethod()!);
                    il.Emit(OpCodes.Call, completedTask);
                    il.Emit(OpCodes.Ret);
                    hits++;
                }
            }

            if (hits == 0)
            {
                throw new InvalidOperationException(
                    $"""
                     IL patch '{patch.Name}' matched nothing in this build.

                     It targets {patch.Type}.{patch.Method}, which this game version does not
                     appear to have. That is version drift: the headless host cannot be trusted
                     to behave like the retail engine until the patch set is re-derived.

                     Fix the patch, do not remove it.
                     """);
            }

            Console.WriteLine($"  patch {patch.Name}: {hits} site(s)");
            applied.Add(new AppliedPatch(patch.Name, $"{patch.Type}.{patch.Method}", hits, patch.Rationale));
        }

        var tmp = dllPath + ".patched";
        module.Write(tmp);
        module.Dispose();
        File.Move(tmp, dllPath, overwrite: true);
        return applied;
    }

    // ── Receipt ─────────────────────────────────────────────────────────────

    private static void WriteReceipt(
        string outDir, InstalledIdentity identity, string installHash,
        List<string> copied, List<AppliedPatch> patches, IReadOnlyDictionary<string, string> outputHashes)
    {
        var receipt = new
        {
            schema = "sts2-pilot-trainer/prepared-assembly/v2",
            prepared_at_utc = DateTimeOffset.UtcNow.ToString("O"),
            // Deliberately no install path: this file is a build artifact, and a
            // machine-specific absolute path has a habit of ending up in a log,
            // a screenshot, or a bug report.
            build = new
            {
                version = identity.Version,
                build_date_utc = identity.BuildDateUtc,
                commit = identity.Commit,
                branch = identity.Branch,
                main_assembly_hash = identity.MainAssemblyHash,
            },
            pristine_sts2_sha256 = installHash,
            assemblies = copied,
            prepared_output_sha256 = outputHashes,
            il_patches = patches.Select(p => new
            {
                name = p.Name,
                target = p.Target,
                sites = p.Sites,
                rationale = p.Rationale,
            }),
        };
        File.WriteAllText(
            Path.Combine(outDir, "prepared-assembly.json"),
            JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static string HashInstall(string gameDir) => HashFile(Path.Combine(gameDir, "sts2.dll"));

    private static SortedDictionary<string, string> HashPreparedOutputs(string outDir, IEnumerable<string> copied)
    {
        var names = copied.Append("release_info.json")
            .Where(name => File.Exists(Path.Combine(outDir, name)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);
        return new SortedDictionary<string, string>(
            names.ToDictionary(name => name, name => HashFile(Path.Combine(outDir, name)), StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void RefuseProtectedOutput(string gameDir, string outDir)
    {
        if (IsWithin(outDir, gameDir) || HasProtectedInstallComponent(outDir))
        {
            throw new InvalidOperationException(
                $"Output directory {Redact(outDir)} is inside a protected Steam or Slay the Spire 2 path. " +
                "Choose an isolated directory inside the project worktree.");
        }
    }

    private static string ResolvePath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        var components = full[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < components.Length; i++)
        {
            var candidate = Path.Combine(current, components[i]);
            FileSystemInfo? entry = Directory.Exists(candidate)
                ? new DirectoryInfo(candidate)
                : File.Exists(candidate) ? new FileInfo(candidate) : null;
            if (entry is null)
            {
                return Path.Combine(current, Path.Combine(components[i..]));
            }

            current = entry.LinkTarget is null
                ? entry.FullName
                : entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                  ?? throw new IOException($"Could not resolve symbolic link {Redact(entry.FullName)}.");
        }

        return current;
    }

    private static bool IsWithin(string path, string parent)
    {
        var relative = Path.GetRelativePath(parent, path);
        return relative == "." ||
               (!relative.Equals("..", StringComparison.Ordinal) &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    private static bool HasProtectedInstallComponent(string path)
    {
        var components = Path.GetFullPath(path)
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
        return components.Any(component =>
            component.Equals("Steam", StringComparison.OrdinalIgnoreCase) ||
            component.Equals("steamapps", StringComparison.OrdinalIgnoreCase) ||
            component.Equals("Slay the Spire 2", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>
    /// A path fit to print. Relative to the working directory where possible, so that
    /// neither a home directory nor a checkout location ends up in a log, a
    /// screenshot, or a demo document that gets published.
    /// </summary>
    private static string Relative(string path)
    {
        var full = Path.GetFullPath(path);
        var cwd = Directory.GetCurrentDirectory();
        return full.StartsWith(cwd + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            ? full[(cwd.Length + 1)..]
            : Redact(full);
    }

    /// <summary>Keeps home directories out of stdout, logs, and screenshots.</summary>
    private static string Redact(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrEmpty(home) ? path : path.Replace(home, "~", StringComparison.Ordinal);
    }

    private static void Fail(string message)
    {
        Console.Error.WriteLine();
        Console.Error.WriteLine(message);
        Console.Error.WriteLine();
    }

    private sealed record InstalledIdentity(
        string Version, string BuildDateUtc, string Commit, string Branch, long MainAssemblyHash);

    private sealed record IlPatch(string Name, string Type, string Method, string Rationale);

    private sealed record AppliedPatch(string Name, string Target, int Sites, string Rationale);
}

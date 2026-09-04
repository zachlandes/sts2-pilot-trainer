using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Sts2PilotTrainer.IO;

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
    /// Assemblies the headless host needs beside <c>sts2.dll</c>, plus the one the
    /// in-game mod needs and the headless host must never load.
    ///
    /// <c>GodotSharp.dll</c> is copied for the mod host to compile against and for
    /// nothing else. The headless projects link against the stand-ins in
    /// third_party/godot-stubs, which emit the same assembly identity and are what
    /// ends up beside the CLI; the real binding here would reach for a Godot runtime
    /// that a console process does not have. The mod runs inside that runtime, so
    /// guessing at the API rather than compiling against it would only move the
    /// mistake to a player's machine.
    /// </summary>
    private static readonly string[] RequiredAssemblies =
    [
        "sts2.dll",
        "GodotSharp.dll",
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
            var archiveArg = ArgValue(args, "--archive");

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

            gameDir = PathContainment.ResolveExistingPath(Path.GetFullPath(gameDir));
            outDir = WorktreePath.Require(outDir);
            RefuseProtectedOutput(gameDir, outDir);

            // Resolved here, with the output directory, and not at the moment the copy
            // happens. Every refusal this tool makes about where it may write is made
            // before it has written anything, so a rejected destination is a run that
            // left the disk as it found it.
            string? archiveDir = null;
            if (archiveArg is not null)
            {
                archiveDir = WorktreePath.Require(Path.GetFullPath(archiveArg));
                RefuseProtectedOutput(gameDir, archiveDir);
                RefuseOverlappingArchive(outDir, archiveDir);
            }

            Console.WriteLine($"game install : {Redact(gameDir)}");
            var identity = ReadInstalledIdentity(gameDir);
            Console.WriteLine($"build        : {identity.Version} ({identity.BuildDateUtc}) commit {identity.Commit}");

            var before = HashInstall(gameDir);

            Directory.CreateDirectory(outDir);
            RemovePriorPreparedOutputs(outDir);
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

            if (archiveDir is not null)
            {
                var archived = Archive(outDir, archiveDir, identity, before, outputHashes);
                Console.WriteLine($"archived     : {identity.Version} -> {Relative(archived)}");
            }

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

    private static void RemovePriorPreparedOutputs(string outDir)
    {
        foreach (var name in RequiredAssemblies.Append("release_info.json").Append(ReceiptName))
        {
            var path = Path.Combine(outDir, name);
            if (File.Exists(path)) File.Delete(path);
        }

        foreach (var path in Directory.GetFiles(outDir, "*.patched", SearchOption.TopDirectoryOnly))
        {
            File.Delete(path);
        }
    }

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

    // ── Archive ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Keeps a copy of the prepared set under the build it was prepared from, so a
    /// recording made on this build can still be verified after the installed game
    /// updates.
    ///
    /// The bootstrap already owns copying and receipting these files, and this is the
    /// same set: the assemblies it copied, the release info the engine reads its
    /// version from, and the receipt that says what all of them hash to. Splitting the
    /// archive off into its own tool would mean a second definition of "the prepared
    /// set", and the two would eventually disagree about a file.
    ///
    /// Every archived file is verified against the receipt after it lands, and an
    /// existing archive of this version prepared from a different installation is
    /// refused rather than overwritten. One version string naming two different builds
    /// is exactly the drift the receipt exists to catch, and quietly replacing the
    /// older copy would destroy the evidence that it happened.
    /// </summary>
    internal static string Archive(
        string outDir, string archiveDir, InstalledIdentity identity, string installHash,
        IReadOnlyDictionary<string, string> outputHashes)
    {
        var target = PathContainment.RequireContained(
            archiveDir, WorktreePath.RequireChild(archiveDir, identity.Version));
        var existingReceipt = Path.Combine(target, ReceiptName);
        if (Directory.Exists(target))
        {
            if (!File.Exists(existingReceipt))
            {
                throw new InvalidOperationException(
                    $"Build {identity.Version} already has an archive directory, but it has no receipt. Its " +
                    "identity cannot be established, and replacing it could destroy the only retained copy " +
                    "of a build a recording was made on. Move it aside or archive this build under a " +
                    "directory of its own.");
            }

            RefuseDriftedArchive(
                existingReceipt, identity.Commit, installHash, outputHashes);
        }

        // An entry already in the archive may be a symlink, so confinement must
        // check each resolved destination rather than only the name being copied.
        foreach (var name in outputHashes.Keys.Append(ReceiptName))
        {
            PathContainment.RequireContained(
                target, WorktreePath.RequireChild(target, name));
        }

        var staging = PathContainment.RequireContained(
            archiveDir,
            WorktreePath.RequireChild(
                archiveDir, $".{identity.Version}.archive.{Guid.NewGuid():N}"));
        string? backup = null;
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var name in outputHashes.Keys.Append(ReceiptName))
            {
                var source = Path.Combine(outDir, name);
                if (!File.Exists(source))
                {
                    throw new InvalidOperationException(
                        $"Prepared source {name} disappeared before it could be archived. Refusing: " +
                        "an archive that is not the whole prepared set is worse than no archive.");
                }

                var destination = PathContainment.RequireContained(
                    staging, WorktreePath.RequireChild(staging, name));
                File.Copy(source, destination);

                if (outputHashes.TryGetValue(name, out var expected) &&
                    HashFile(destination) != expected)
                {
                    throw new InvalidOperationException(
                        $"Archived {name} does not hash to what the receipt says it should. Refusing: " +
                        "an archive that is not the prepared set is worse than no archive.");
                }
            }

            ValidatePreparedReceipt(
                Path.Combine(staging, ReceiptName), identity, installHash, outputHashes);

            // Match install-mod.sh: prepare and verify a complete temporary sibling,
            // then replace the named directory without ever exposing a partial set.
            if (Directory.Exists(target))
            {
                backup = PathContainment.RequireContained(
                    archiveDir,
                    WorktreePath.RequireChild(
                        archiveDir, $".{identity.Version}.previous.{Guid.NewGuid():N}"));
                Directory.Move(target, backup);
            }

            Directory.Move(staging, target);
            staging = string.Empty;
            if (backup is not null)
            {
                Directory.Delete(backup, recursive: true);
                backup = null;
            }
            return target;
        }
        finally
        {
            if (!string.IsNullOrEmpty(staging) && Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
            if (backup is not null && Directory.Exists(backup))
            {
                if (!Directory.Exists(target))
                {
                    Directory.Move(backup, target);
                }
                else
                {
                    Directory.Delete(backup, recursive: true);
                }
            }
        }
    }

    private static void ValidatePreparedReceipt(
        string receiptPath, InstalledIdentity identity, string installHash,
        IReadOnlyDictionary<string, string> outputHashes)
    {
        var receipt = JsonNode.Parse(File.ReadAllText(receiptPath))!.AsObject();
        var build = receipt["build"] as JsonObject;
        var receiptHashes = receipt["prepared_output_sha256"] as JsonObject;
        var identityMatches = receipt["schema"]?.GetValue<string>() ==
                                  "sts2-pilot-trainer/prepared-assembly/v2" &&
                              build?["version"]?.GetValue<string>() == identity.Version &&
                              build?["build_date_utc"]?.GetValue<string>() == identity.BuildDateUtc &&
                              build?["commit"]?.GetValue<string>() == identity.Commit &&
                              build?["branch"]?.GetValue<string>() == identity.Branch &&
                              build?["main_assembly_hash"]?.GetValue<long>() == identity.MainAssemblyHash &&
                              receipt["pristine_sts2_sha256"]?.GetValue<string>() == installHash;
        var hashesMatch = receiptHashes is not null &&
                          receiptHashes.Count == outputHashes.Count &&
                          outputHashes.All(expected =>
                              receiptHashes[expected.Key]?.GetValue<string>() == expected.Value);
        if (!identityMatches || !hashesMatch)
        {
            throw new InvalidOperationException(
                $"Prepared source {ReceiptName} changed before it could be archived. Refusing: " +
                "an archive whose receipt does not describe the whole prepared set is worse than no archive.");
        }
    }

    /// <summary>
    /// Refuses to archive over a copy of the same version whose build identity or
    /// deterministic prepared outputs differ.
    ///
    /// The patched <c>sts2.dll</c> is the only prepared output excluded: the IL
    /// patcher rewrites it on every bootstrap, and Mono.Cecil does not reproduce it
    /// byte for byte. Its identity is carried by <c>pristine_sts2_sha256</c> instead.
    /// Every sibling assembly and release-info file is deterministic, including
    /// whether the file is present at all.
    /// </summary>
    internal static void RefuseDriftedArchive(
        string existingReceipt, string currentCommit, string installHash,
        IReadOnlyDictionary<string, string> currentOutputHashes)
    {
        var archived = JsonNode.Parse(File.ReadAllText(existingReceipt))!.AsObject();
        var archivedInstall = archived["pristine_sts2_sha256"]?.GetValue<string>();
        var archivedCommit = archived["build"]?["commit"]?.GetValue<string>();
        var archivedOutputs = archived["prepared_output_sha256"] as JsonObject;
        var differences = new List<string>();

        if (archivedCommit != currentCommit)
        {
            differences.Add(
                $"commit: archived {archivedCommit ?? "unknown"}, this run {currentCommit}");
        }
        if (archivedInstall != installHash)
        {
            differences.Add(
                $"pristine sts2.dll: archived {Abbreviate(archivedInstall)}, " +
                $"this run {Abbreviate(installHash)}");
        }

        if (archivedOutputs is null)
        {
            differences.Add("prepared_output_sha256: archived receipt is missing it");
        }
        else
        {
            var outputNames = archivedOutputs.Select(entry => entry.Key)
                .Union(currentOutputHashes.Keys, StringComparer.Ordinal)
                .Where(name => !name.Equals("sts2.dll", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal);
            foreach (var name in outputNames)
            {
                var archivedHash = archivedOutputs[name]?.GetValue<string>();
                currentOutputHashes.TryGetValue(name, out var currentHash);
                if (archivedHash == currentHash) continue;

                differences.Add(
                    $"prepared output {name}: archived {Abbreviate(archivedHash)}, " +
                    $"this run {Abbreviate(currentHash)}");
            }
        }

        if (differences.Count == 0) return;

        throw new InvalidOperationException(
            $"""
             This build version is already archived, but its prepared set differs.

             {string.Join(Environment.NewLine, differences.Select(difference => "- " + difference))}

             Refusing to overwrite the archived copy: it is the evidence of what that build was,
             and a recording verified against it would silently be verified against something else.
             Archive this one under a directory of its own.
             """);
    }

    private const string ReceiptName = "prepared-assembly.json";

    private static string Abbreviate(string? hash) =>
        hash is null ? "unknown" : hash.Length <= 16 ? hash : hash[..16] + "...";

    private static void RefuseOverlappingArchive(string outDir, string archiveDir)
    {
        if (PathContainment.IsResolvedWithin(archiveDir, outDir) ||
            PathContainment.IsResolvedWithin(outDir, archiveDir))
        {
            throw new InvalidOperationException(
                $"Archive directory {Relative(archiveDir)} overlaps the prepared output directory " +
                $"{Relative(outDir)}. The archive is a copy of that set and cannot live inside it.");
        }
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
            Path.Combine(outDir, ReceiptName),
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
        if (PathContainment.IsResolvedWithin(outDir, gameDir) || ProtectedInstallPath.HasProtectedComponent(outDir))
        {
            throw new InvalidOperationException(
                $"Output directory {Redact(outDir)} is inside a protected Steam or Slay the Spire 2 path. " +
                "Choose an isolated directory inside the project worktree.");
        }
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        if (i < 0) return null;

        var value = i + 1 < args.Length ? args[i + 1] : null;
        // A patch-day run must not report success without retaining the build because
        // its archive option was present but empty, so every present option is strict.
        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"Option {name} requires a value.");
        }

        return value;
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

    internal sealed record InstalledIdentity(
        string Version, string BuildDateUtc, string Commit, string Branch, long MainAssemblyHash);

    private sealed record IlPatch(string Name, string Type, string Method, string Rationale);

    private sealed record AppliedPatch(string Name, string Target, int Sites, string Rationale);
}

using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

public sealed class ModHostBoundaryTests
{
    [GameFact]
    public void AdoptLiveRefusesAConsoleProcessWithoutWritingGameInputs()
    {
        var before = GameInputSnapshot();

        var result = Arbiter.Run("adopt-live");

        var after = GameInputSnapshot();
        Assert.False(result.Verified, result.All);
        Assert.Contains("startup phase : None", result.Output, StringComparison.Ordinal);
        Assert.Contains("not a game whose state can be read honestly", result.Output, StringComparison.Ordinal);
        Assert.Equal(before, after);
    }

    [GameFact]
    public void AdoptionRefusesDuplicateGameAssembliesBeforeReadingTheirState()
    {
        _ = EngineHost.StartupPhase();
        HeadlessEngine.Forget();
        var gamePath = Path.Combine(Arbiter.RepoRoot, "build", "lib", "sts2.dll");
        var duplicateContext = ExerciseDuplicateAssemblyRefusal(gamePath);

        for (var attempt = 0; attempt < 10 && duplicateContext.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    /// <summary>
    /// The refusal these two adoption tests are about is the game's own startup phase,
    /// so the one refusal that would answer ahead of it is taken off this process
    /// first: a headless engine started by any earlier test in this assembly makes
    /// <c>AdoptRunningGame</c> refuse for that instead, and the test would then be
    /// asserting whichever test ran before it rather than the host.
    /// </summary>
    [GameFact]
    public void AdoptionRefusesUntilEssentialInitializationHasFinished()
    {
        _ = EngineHost.StartupPhase();
        HeadlessEngine.Forget();
        var gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .Single(assembly => assembly.GetName().Name == "sts2");
        var initialization = gameAssembly.GetType("MegaCrit.Sts2.Core.Helpers.OneTimeInitialization")!;
        var state = initialization.GetField(
            "_state", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var original = state.GetValue(null);
        state.SetValue(null, Enum.Parse(state.FieldType, "Essential"));

        try
        {
            var refusal = Assert.Throws<EngineException>(EngineHost.AdoptRunningGame);

            Assert.Contains("startup phase is 'Essential'", refusal.Message, StringComparison.Ordinal);
            Assert.Contains(gameAssembly.Location, refusal.Message, StringComparison.Ordinal);
        }
        finally
        {
            state.SetValue(null, original);
        }
    }

    [Fact]
    public void TheModManifestDeclaresItselfNonGameplayAndPacksItsModImage()
    {
        var path = Path.Combine(
            Arbiter.RepoRoot, "src", "Sts2PilotTrainer.Mod", "Runmobile.json");
        var manifest = JsonDocument.Parse(File.ReadAllText(path)).RootElement;

        Assert.False(manifest.GetProperty("affects_gameplay").GetBoolean());
        Assert.True(manifest.GetProperty("has_pck").GetBoolean());
        Assert.True(manifest.GetProperty("has_dll").GetBoolean());
        Assert.Empty(manifest.GetProperty("dependencies").EnumerateArray());
        Assert.Equal("Runmobile", manifest.GetProperty("id").GetString());
        Assert.Equal("Runmobile", manifest.GetProperty("name").GetString());
    }

    [Fact]
    public void PackageOutputCannotBeRedirectedToAnExistingDirectory()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), $"runmobile-package-output-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sandbox);
        var sentinel = Path.Combine(sandbox, "keep.txt");
        File.WriteAllText(sentinel, "keep");

        try
        {
            var result = RunScript(
                Path.Combine(Arbiter.RepoRoot, "scripts", "package-mod.sh"),
                "--directory",
                sandbox);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Equal("keep", File.ReadAllText(sentinel));
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void FailedPackagingRemovesStaleAndPartialDistributables()
    {
        if (OperatingSystem.IsWindows()) return;

        var sandbox = Path.Combine(Path.GetTempPath(), $"runmobile-package-failure-{Guid.NewGuid():N}");
        var scripts = Path.Combine(sandbox, "scripts");
        Directory.CreateDirectory(scripts);
        var packageScript = Path.Combine(scripts, "package-mod.sh");
        var buildScript = Path.Combine(scripts, "build.sh");
        File.Copy(
            Path.Combine(Arbiter.RepoRoot, "scripts", "package-mod.sh"),
            packageScript);
        File.WriteAllText(buildScript, "#!/usr/bin/env bash\nexit 0\n");
        File.SetUnixFileMode(
            buildScript,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
        var output = Path.Combine(sandbox, "build", "distribution", $"Runmobile-{rid}");
        var archive = output + ".tar.gz";
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "stale.txt"), "stale");
        File.WriteAllText(archive, "stale");

        try
        {
            var result = RunScript(packageScript);

            Assert.NotEqual(0, result.ExitCode);
            Assert.False(Directory.Exists(output));
            Assert.False(File.Exists(archive));
            Assert.False(Directory.Exists(Path.Combine(
                sandbox, "build", "publish", "runmobile-package", rid)));
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void IncompletePackagePreservesTheWorkingInstallation()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), $"runmobile-partial-package-{Guid.NewGuid():N}");
        var package = Path.Combine(sandbox, "package");
        var mods = Path.Combine(sandbox, "mods");
        var installed = Path.Combine(mods, "Runmobile");
        Directory.CreateDirectory(package);
        Directory.CreateDirectory(installed);
        File.Copy(
            Path.Combine(Arbiter.RepoRoot, "scripts", "install-package.sh"),
            Path.Combine(package, "install.sh"));
        File.WriteAllText(Path.Combine(package, "runtime-id"), "test-runtime");
        File.WriteAllText(Path.Combine(installed, "working.txt"), "working");

        try
        {
            var result = RunScript(Path.Combine(package, "install.sh"), "--mods-dir", mods);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Equal("working", File.ReadAllText(Path.Combine(installed, "working.txt")));
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact]
    public void MissingInventoriedArbiterDependencyPreservesTheWorkingInstallation()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), $"runmobile-partial-arbiter-{Guid.NewGuid():N}");
        var package = Path.Combine(sandbox, "package");
        var payload = Path.Combine(package, "payload");
        var arbiter = Path.Combine(payload, "arbiter");
        var bootstrap = Path.Combine(package, "bootstrap");
        var mods = Path.Combine(sandbox, "mods");
        var installed = Path.Combine(mods, "Runmobile");
        Directory.CreateDirectory(arbiter);
        Directory.CreateDirectory(bootstrap);
        Directory.CreateDirectory(installed);
        File.Copy(
            Path.Combine(Arbiter.RepoRoot, "scripts", "install-package.sh"),
            Path.Combine(package, "install.sh"));
        File.WriteAllText(Path.Combine(package, "runtime-id"), "test-runtime");
        foreach (var file in new[]
        {
            "Runmobile.json",
            "Runmobile.pck",
            "Runmobile.dll",
            "Sts2PilotTrainer.Trainer.dll",
            "Sts2PilotTrainer.Engine.dll",
            "Sts2PilotTrainer.Replay.dll",
            "Sts2PilotTrainer.IO.dll",
        })
        {
            File.WriteAllText(Path.Combine(payload, file), "payload");
        }
        File.WriteAllText(Path.Combine(arbiter, "sts2-arbiter"), "arbiter");
        File.WriteAllText(Path.Combine(bootstrap, "Sts2PilotTrainer.Bootstrap"), "bootstrap");
        File.WriteAllText(
            Path.Combine(package, "arbiter-files.txt"),
            "./missing-runtime-file\n./sts2-arbiter\n");
        File.WriteAllText(Path.Combine(installed, "working.txt"), "working");

        try
        {
            var result = RunScript(Path.Combine(package, "install.sh"), "--mods-dir", mods);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Equal("working", File.ReadAllText(Path.Combine(installed, "working.txt")));
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    /// <summary>
    /// The retail client walks the complete installed mod recursively and opens every
    /// <c>*.json</c> as a mod manifest. The root Runmobile.json is the only manifest
    /// the artifact intends to carry. Another JSON with an id registers as another
    /// mod; one with any other manifest field logs an error and an error-level Sentry
    /// breadcrumb on every launch when its id is absent, which is what the prepared
    /// copy of the game's own release_info.json used to do.
    ///
    /// Asked of a real install rather than a reconstruction, and asked from this class
    /// because every test here that runs the packager shares its collection. Two of
    /// them in different assemblies would run at once, and packaging begins by deleting
    /// the distribution directory the other one is reading.
    /// </summary>
    [GameFact]
    public void TheInstalledArtifactCarriesOnlyItsRootModManifest()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), $"runmobile-manifest-scan-{Guid.NewGuid():N}");
        var mods = Path.Combine(sandbox, "mods");
        Directory.CreateDirectory(mods);

        try
        {
            var result = RunInstaller(mods);
            Assert.Equal(0, result.ExitCode);

            var installed = Path.Combine(mods, "Runmobile");
            foreach (var path in Directory.GetFiles(installed, "*.json", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(installed, path);
                if (relative.Equals("Runmobile.json", StringComparison.Ordinal)) continue;

                using var stream = File.OpenRead(path);
                var json = JsonDocument.Parse(stream).RootElement;
                var carried = json.ValueKind == JsonValueKind.Object
                    ? ModManifestFields.Where(field => json.TryGetProperty(field, out _)).ToArray()
                    : [];
                Assert.True(
                    carried.Length == 0,
                    carried.Contains("id")
                        ? $"{relative} ships inside Runmobile, where the game registers it as a second mod. " +
                          "Only the root Runmobile.json may carry a top-level mod-manifest field."
                        : $"{relative} ships inside Runmobile with top-level {string.Join(", ", carried)}, so " +
                          "the game reads it as a mod manifest missing its id and logs an error on every " +
                          "launch. Give it a name that does not end in .json.");
            }

            Assert.True(
                File.Exists(Path.Combine(installed, "arbiter", "lib", "release_info.json.copy")),
                "The prepared release info is absent, so the engine cannot report the build it is running. " +
                "Renaming it is the fix here; removing it is not.");
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    private static readonly string[] ModManifestFields =
        ["id", "name", "author", "description", "version"];

    [GameFact]
    public void TheBuiltModInstallsUnderTheIdPreflightAccepts()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), $"runmobile-install-{Guid.NewGuid():N}");
        var mods = Path.Combine(sandbox, "mods");
        var former = Path.Combine(mods, "CombatTrainer");
        Directory.CreateDirectory(former);
        File.WriteAllText(Path.Combine(former, "leftover.txt"), "old");
        var preparedReceipt = Path.Combine(Arbiter.RepoRoot, "build", "lib", "prepared-assembly.json");
        var receiptBeforeInstall = File.ReadAllBytes(preparedReceipt);

        try
        {
            var result = RunInstaller(mods);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(receiptBeforeInstall, File.ReadAllBytes(preparedReceipt));
            Assert.False(Directory.Exists(former));
            var installed = Path.Combine(mods, "Runmobile");
            Assert.Equal(
                [
                    "Runmobile.dll",
                    "Runmobile.json",
                    "Runmobile.pck",
                    "Sts2PilotTrainer.Engine.dll",
                    "Sts2PilotTrainer.IO.dll",
                    "Sts2PilotTrainer.Replay.dll",
                    "Sts2PilotTrainer.Trainer.dll",
                ],
                Directory.EnumerateFiles(installed).Select(Path.GetFileName).Order(StringComparer.Ordinal));
            var iconResources = ReadPckEntries(Path.Combine(installed, "Runmobile.pck"));
            var icon = iconResources[CompendiumCard.IconPath["res://".Length..]];
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(
                    Arbiter.RepoRoot, "src", "Sts2PilotTrainer.Mod", "Assets", "Runmobile", "mod_image.png")),
                icon);
            Assert.Equal(64u, BinaryPrimitives.ReadUInt32BigEndian(icon.AsSpan(16)));
            Assert.Equal(64u, BinaryPrimitives.ReadUInt32BigEndian(icon.AsSpan(20)));
            Assert.Contains("Runmobile/mod_image.png.import", iconResources.Keys);
            Assert.Contains(iconResources.Keys, path =>
                path.StartsWith(".godot/imported/mod_image.png-", StringComparison.Ordinal) &&
                path.EndsWith(".ctex", StringComparison.Ordinal));

            var arbiterDirectory = Path.Combine(installed, "arbiter");
            Assert.True(Directory.Exists(arbiterDirectory));
            Assert.True(File.Exists(Path.Combine(
                arbiterDirectory,
                OperatingSystem.IsWindows() ? "sts2-arbiter.exe" : "sts2-arbiter")));
            Assert.True(File.Exists(Path.Combine(arbiterDirectory, "lib", "prepared-assembly.json")));
            Assert.True(File.Exists(Path.Combine(arbiterDirectory, "lib", "sts2.dll")));

            var rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
            var package = Path.Combine(Arbiter.RepoRoot, "build", "distribution", $"Runmobile-{rid}");
            Assert.Equal(rid, File.ReadAllText(Path.Combine(package, "runtime-id")).Trim());
            Assert.True(File.Exists(Path.Combine(package, "install.sh")));
            Assert.True(Directory.Exists(Path.Combine(package, "bootstrap")));
            Assert.False(Directory.Exists(Path.Combine(package, "payload", "arbiter", "lib")));

            var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(installed, "Runmobile.json")))
                .RootElement;
            var declared = manifest.GetProperty("id").GetString()!;
            Assert.Equal("Runmobile", declared);
            Assert.Equal(declared, AssemblyName.GetAssemblyName(Path.Combine(installed, "Runmobile.dll")).Name);
            Assert.Equal(RunmobileMod.ModId, declared);

            var expected = RecordedFightModule.Instance.Recording.Environment;
            var preflight = EnvironmentPreflight.Prerequisites(
                expected,
                new LocalPrerequisites
                {
                    BuildVersion = expected.BuildVersion.Value,
                    BuildDateUtc = expected.BuildDateUtc.Value,
                    ContentHash = expected.ContentHash.Value,
                    Mods =
                    [
                        new LocalMod(
                            declared,
                            manifest.GetProperty("name").GetString()!,
                            manifest.GetProperty("version").GetString()!,
                            manifest.GetProperty("affects_gameplay").GetBoolean(),
                            "Loaded"),
                    ],
                    Unlocks = new UnlockInventory
                    {
                        Origin = "complete test inventory",
                        FromPlayerProfile = false,
                        Categories = [],
                    },
                    LockedActs = [],
                });

            Assert.True(
                preflight.Fields.Single(field => field.Field == "loaded_mod_environment").Matches);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [GameFact]
    public void AStagingFailurePreservesTheFormerInstallation()
    {
        if (OperatingSystem.IsWindows()) return;

        var sandbox = Path.Combine(Path.GetTempPath(), $"runmobile-install-failure-{Guid.NewGuid():N}");
        var mods = Path.Combine(sandbox, "mods");
        var former = Path.Combine(mods, "CombatTrainer");
        var tools = Path.Combine(sandbox, "tools");
        Directory.CreateDirectory(former);
        Directory.CreateDirectory(tools);
        File.WriteAllText(Path.Combine(former, "working.txt"), "working");
        var failingMktemp = Path.Combine(tools, "mktemp");
        File.WriteAllText(failingMktemp, "#!/usr/bin/env bash\nexit 23\n");
        File.SetUnixFileMode(failingMktemp,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            var result = RunInstaller(mods, tools);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Equal("working", File.ReadAllText(Path.Combine(former, "working.txt")));
            Assert.False(Directory.Exists(Path.Combine(mods, "Runmobile")));
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    /// <summary>
    /// How long the installer gets before this test calls it a hang. A cold worktree has to
    /// restore and build the mod and everything under it, which is seconds rather than
    /// minutes; anything past this is not a slow build.
    /// </summary>
    private static readonly TimeSpan InstallerTimeout = TimeSpan.FromMinutes(5);

    /// <summary>How long the last of the installer's output gets to arrive after it has exited.</summary>
    private static readonly TimeSpan OutputDrainGrace = TimeSpan.FromSeconds(30);

    private static Arbiter.Result RunInstaller(string modsDirectory, string? pathPrefix = null)
    {
        var startInfo = ScriptStartInfo(
            Path.Combine(Arbiter.RepoRoot, "scripts", "install-mod.sh"),
            "--mods-dir",
            modsDirectory);
        if (pathPrefix is not null)
        {
            startInfo.Environment["PATH"] =
                pathPrefix + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        }

        // The installer builds the mod, and MSBuild leaves its worker nodes running for the
        // next build to reuse. Those nodes inherit the pipes read below and outlive the script
        // that started them, so end of stream never arrives and this test used to hang for as
        // long as anybody let it. Reuse is off for this child only; the player's own builds are
        // not this test's business.
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = Process.Start(startInfo)!;

        // Both streams are drained at once. Reading one to the end and then the other deadlocks
        // as soon as the child fills the pipe nobody is reading.
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();

        // Waits on the process rather than on end of stream, and refuses rather than waiting
        // forever: a test that never finishes reports nothing to anybody.
        if (!process.WaitForExit((int)InstallerTimeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"scripts/install-mod.sh did not exit within {InstallerTimeout.TotalMinutes:0} minutes.");
        }

        if (!Task.WhenAll(output, error).Wait(OutputDrainGrace))
        {
            throw new TimeoutException(
                $"scripts/install-mod.sh exited with {process.ExitCode}, but {OutputDrainGrace.TotalSeconds:0} " +
                "seconds later something it started is still holding its output open.");
        }

        return new Arbiter.Result(process.ExitCode, output.Result, error.Result);
    }

    private static Arbiter.Result RunScript(string script, params string[] arguments)
    {
        using var process = Process.Start(ScriptStartInfo(script, arguments))!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(output, error);
        return new Arbiter.Result(process.ExitCode, output.Result, error.Result);
    }

    private static ProcessStartInfo ScriptStartInfo(string script, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            WorkingDirectory = Arbiter.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(script);
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ExerciseDuplicateAssemblyRefusal(string gamePath)
    {
        var duplicateContext = new AssemblyLoadContext("duplicate-sts2", isCollectible: true);
        duplicateContext.LoadFromAssemblyPath(gamePath);

        try
        {
            var refusal = Assert.Throws<EngineException>(EngineHost.AdoptRunningGame);

            Assert.Contains("2 assemblies named sts2 are loaded", refusal.Message, StringComparison.Ordinal);
            Assert.Contains(gamePath, refusal.Message, StringComparison.Ordinal);
        }
        finally
        {
            duplicateContext.Unload();
        }

        return new WeakReference(duplicateContext);
    }

    private static IReadOnlyDictionary<string, byte[]> ReadPckEntries(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        Assert.Equal(0x43504447u, reader.ReadUInt32());
        Assert.Equal(3u, reader.ReadUInt32());
        stream.Position = 24;
        var fileBase = reader.ReadUInt64();
        var directory = reader.ReadUInt64();
        stream.Position = (long)directory;
        var entries = new List<(string Path, ulong Offset, ulong Length)>();
        var count = reader.ReadUInt32();
        for (var index = 0; index < count; index++)
        {
            var length = reader.ReadUInt32();
            var resourcePath = System.Text.Encoding.UTF8.GetString(reader.ReadBytes((int)length)).TrimEnd('\0');
            var offset = reader.ReadUInt64();
            var size = reader.ReadUInt64();
            reader.ReadBytes(16);
            reader.ReadUInt32();
            entries.Add((resourcePath, offset, size));
        }

        return entries.ToDictionary(
            entry => entry.Path,
            entry =>
            {
                stream.Position = checked((long)(fileBase + entry.Offset));
                return reader.ReadBytes(checked((int)entry.Length));
            },
            StringComparer.Ordinal);
    }

    private static IReadOnlyList<FileFingerprint> GameInputSnapshot()
    {
        var files = new[]
            {
                Path.Combine(Arbiter.RepoRoot, "build", "lib"),
                Path.Combine(Arbiter.RepoRoot, "build", "sandbox"),
            }
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal);

        return files.Select(path => new FileFingerprint(
                Path.GetRelativePath(Arbiter.RepoRoot, path),
                new FileInfo(path).Length,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)))))
            .ToList();
    }

    private sealed record FileFingerprint(string Path, long Length, string Sha256);
}

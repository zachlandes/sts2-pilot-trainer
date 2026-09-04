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
        var gamePath = Path.Combine(Arbiter.RepoRoot, "build", "lib", "sts2.dll");
        var duplicateContext = ExerciseDuplicateAssemblyRefusal(gamePath);

        for (var attempt = 0; attempt < 10 && duplicateContext.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    [GameFact]
    public void AdoptionRefusesUntilEssentialInitializationHasFinished()
    {
        _ = EngineHost.StartupPhase();
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
    public void TheModManifestDeclaresItselfNonGameplayAndPackless()
    {
        var path = Path.Combine(
            Arbiter.RepoRoot, "src", "Sts2PilotTrainer.Mod", "Runmobile.json");
        var manifest = JsonDocument.Parse(File.ReadAllText(path)).RootElement;

        Assert.False(manifest.GetProperty("affects_gameplay").GetBoolean());
        Assert.False(manifest.GetProperty("has_pck").GetBoolean());
        Assert.True(manifest.GetProperty("has_dll").GetBoolean());
        Assert.Empty(manifest.GetProperty("dependencies").EnumerateArray());
        Assert.Equal("Runmobile", manifest.GetProperty("id").GetString());
        Assert.Equal("Runmobile", manifest.GetProperty("name").GetString());
    }

    [GameFact]
    public void TheBuiltModInstallsUnderTheIdLivePreflightAccepts()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), $"runmobile-install-{Guid.NewGuid():N}");
        var mods = Path.Combine(sandbox, "mods");
        var former = Path.Combine(mods, "CombatTrainer");
        Directory.CreateDirectory(former);
        File.WriteAllText(Path.Combine(former, "leftover.txt"), "old");

        try
        {
            var result = RunInstaller(mods);

            Assert.Equal(0, result.ExitCode);
            Assert.False(Directory.Exists(former));
            var installed = Path.Combine(mods, "Runmobile");
            Assert.Equal(
                [
                    "Runmobile.dll",
                    "Runmobile.json",
                    "Sts2PilotTrainer.Engine.dll",
                    "Sts2PilotTrainer.IO.dll",
                    "Sts2PilotTrainer.Replay.dll",
                    "Sts2PilotTrainer.Trainer.dll",
                ],
                Directory.EnumerateFiles(installed).Select(Path.GetFileName).Order(StringComparer.Ordinal));

            var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(installed, "Runmobile.json")))
                .RootElement;
            var declared = manifest.GetProperty("id").GetString()!;
            Assert.Equal("Runmobile", declared);
            Assert.Equal(declared, AssemblyName.GetAssemblyName(Path.Combine(installed, "Runmobile.dll")).Name);
            Assert.Equal(RunmobileMod.ModId, declared);

            var expected = CombatTrainerModule.Instance.Recording.Environment;
            var preflight = EnvironmentPreflight.LiveGame(
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
                },
                run: null);

            Assert.True(
                preflight.Prerequisites.Fields.Single(field => field.Field == "loaded_mod_environment").Matches);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    private static Arbiter.Result RunInstaller(string modsDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            WorkingDirectory = Arbiter.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Path.Combine(Arbiter.RepoRoot, "scripts", "install-mod.sh"));
        startInfo.ArgumentList.Add("--mods-dir");
        startInfo.ArgumentList.Add(modsDirectory);

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new Arbiter.Result(process.ExitCode, output, error);
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

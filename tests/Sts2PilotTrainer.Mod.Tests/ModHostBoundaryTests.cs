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

    /// <summary>
    /// The id in the manifest, the id the mod logs under, the assembly the installer
    /// ships and the id the mod-environment gate permits are one id. They are read
    /// by four different things - the game, the log, install-mod.sh and the preflight
    /// - so a rename that reached three of them would leave a game that loads a mod
    /// the trainer then refuses to recognise.
    /// </summary>
    [Fact]
    public void TheModIdIsTheSameOneEverywhereItIsWrittenDown()
    {
        var manifestPath = Path.Combine(
            Arbiter.RepoRoot, "src", "Sts2PilotTrainer.Mod", "Runmobile.json");
        var declared = JsonDocument.Parse(File.ReadAllText(manifestPath))
            .RootElement.GetProperty("id").GetString();
        var project = File.ReadAllText(Path.Combine(
            Arbiter.RepoRoot, "src", "Sts2PilotTrainer.Mod", "Sts2PilotTrainer.Mod.csproj"));
        var installer = File.ReadAllText(Path.Combine(Arbiter.RepoRoot, "scripts", "install-mod.sh"));

        Assert.Equal("Runmobile", declared);
        Assert.Equal(RunmobileMod.ModId, declared);
        Assert.Contains($"<AssemblyName>{declared}</AssemblyName>", project, StringComparison.Ordinal);
        Assert.Contains($"mod_id=\"{declared}\"", installer, StringComparison.Ordinal);
        Assert.Contains("former_mod_id=\"CombatTrainer\"", installer, StringComparison.Ordinal);

        // The one gate that has to agree with the game's own mod list. Read off the
        // constant rather than through a whole preflight fixture, because what is
        // being pinned here is that the two spellings are one spelling; what the gate
        // then does with it is LivePreflightTests' subject.
        var permitted = typeof(EnvironmentPreflight)
            .GetField("HostModId", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetRawConstantValue();
        Assert.Equal(declared, permitted);
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

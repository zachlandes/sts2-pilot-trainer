using System.Diagnostics;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// Drives the built arbiter CLI, one process per invocation.
///
/// These tests deliberately go through the command line rather than calling the
/// library, for two reasons. The engine keeps static state, so a determinism claim
/// has to be made across processes to mean anything. And the command line is what
/// the demo document runs, so testing it is testing the thing that gets shown.
/// </summary>
internal static class Arbiter
{
    internal sealed record Result(int ExitCode, string Output, string Error)
    {
        internal bool Verified => ExitCode == 0;

        internal string All => Output + Error;
    }

    /// <summary>Repository root, found by walking up to the solution file.</summary>
    internal static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>
    /// Whether a prepared game assembly exists. Without one there is nothing to
    /// replay, and these tests skip rather than fail: not owning the game is a
    /// perfectly good reason to be unable to run them, and reporting it as a defect
    /// would train people to ignore red.
    /// </summary>
    internal static bool GameAvailable =>
        File.Exists(Path.Combine(RepoRoot, "build", "lib", "sts2.dll")) &&
        File.Exists(CliPath);

    internal static string SkipReason =>
        "Needs a prepared game assembly and a built CLI. Run ./scripts/build.sh, which copies your own " +
        "Slay the Spire 2 installation into build/lib without modifying it.";

    private static string CliPath =>
        Path.Combine(RepoRoot, "build", "bin", "Sts2PilotTrainer.Cli", "Release", "net9.0", "sts2-arbiter.dll");

    internal static string Manifest =>
        Path.Combine(RepoRoot, "manifests", "navegreed-OJ-6QXhNgdg.replay.json");

    internal static string MapObservation =>
        Path.Combine(RepoRoot, "manifests", "navegreed-OJ-6QXhNgdg.map-observation.json");

    internal static string SyntheticReplayFixture()
    {
        var path = Path.Combine(RepoRoot, "build", "test-scratch", "synthetic-engine.replay.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ManifestJson.Save(Sts2PilotTrainer.Replay.SyntheticReplayFixture.Create(), path);
        return path;
    }

    internal static IReadOnlyList<string> SyntheticLines()
    {
        var dir = Path.Combine(RepoRoot, "build", "test-scratch", "synthetic-lines");
        Directory.CreateDirectory(dir);
        var paths = new List<string>();
        foreach (var (name, line) in Sts2PilotTrainer.Replay.SyntheticReplayFixture.CreateLines())
        {
            var path = Path.Combine(dir, name + ".line.json");
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(line, ManifestJson.Options));
            paths.Add(path);
        }
        return paths;
    }

    internal static Result Run(params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(CliPath);
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new Result(process.ExitCode, output, error);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "sts2-pilot-trainer.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not find the repository root.");
    }
}

/// <summary>
/// A fact that needs the game. Skips with an explanation when the game is not
/// prepared, so the suite stays green on a machine that cannot run it and stays
/// honest about what it did not check.
/// </summary>
public sealed class GameFactAttribute : FactAttribute
{
    public GameFactAttribute()
    {
        if (!Arbiter.GameAvailable) Skip = Arbiter.SkipReason;
    }
}

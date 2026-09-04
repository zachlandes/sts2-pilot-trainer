using System.Diagnostics;

namespace Sts2PilotTrainer.Arbiter.Tests;

public class BootstrapSafetyTests
{
    [Fact]
    public void RefusesAnOutputDirectoryOutsideTheWorktreeBeforeWriting()
    {
        var outside = OutsideThisWorktree("bootstrap-escape");

        var result = RunBootstrap("--out", outside);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("resolves outside the allowed root", result.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outside));
    }

    /// <summary>
    /// The archive keeps a copy of the prepared set for a build a recording was made
    /// on, and it is a copy the tool makes on its own: the containment check has to
    /// happen before anything is prepared, or a refused destination would still have
    /// left the bootstrap's other writes behind.
    ///
    /// The game directory named here is an empty scratch directory, so this run would
    /// fail on its own a step later. That is what makes the assertion mean something:
    /// the refusal has to come from the archive path, before the tool ever looks at
    /// what is installed.
    /// </summary>
    [Fact]
    public void RefusesAnArchiveDirectoryOutsideTheWorktreeBeforeWriting()
    {
        var outside = OutsideThisWorktree("bootstrap-archive-escape");

        var result = RunBootstrap("--archive", outside);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("resolves outside the allowed root", result.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outside));

        // The run stopped before it read the installation, which is the step that would
        // have followed. A refusal that arrived after the copying had started would
        // still be a refusal, and would not be this one.
        Assert.DoesNotContain("build        :", result.Output, StringComparison.Ordinal);
    }

    private static string OutsideThisWorktree(string name) =>
        Path.GetFullPath(Path.Combine(Arbiter.RepoRoot, "..", $"{name}-{Guid.NewGuid():N}"));

    private static (int ExitCode, string Output) RunBootstrap(params string[] args)
    {
        var scratch = Path.Combine(
            Arbiter.RepoRoot, "build", "test-scratch", Guid.NewGuid().ToString("N"));
        var gameDir = Path.Combine(scratch, "game");
        Directory.CreateDirectory(gameDir);
        var bootstrap = Path.Combine(
            Arbiter.RepoRoot, "build", "bin", "Sts2PilotTrainer.Bootstrap", "Release", "net9.0",
            "Sts2PilotTrainer.Bootstrap.dll");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Arbiter.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(bootstrap);
        startInfo.ArgumentList.Add("--game-dir");
        startInfo.ArgumentList.Add(gameDir);
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}

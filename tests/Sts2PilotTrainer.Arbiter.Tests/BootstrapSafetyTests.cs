using System.Diagnostics;

namespace Sts2PilotTrainer.Arbiter.Tests;

public class BootstrapSafetyTests
{
    [Fact]
    public void RefusesAnOutputDirectoryOutsideTheWorktreeBeforeWriting()
    {
        var scratch = Path.Combine(
            Arbiter.RepoRoot, "build", "test-scratch", Guid.NewGuid().ToString("N"));
        var gameDir = Path.Combine(scratch, "game");
        Directory.CreateDirectory(gameDir);
        var outside = Path.GetFullPath(Path.Combine(
            Arbiter.RepoRoot, "..", $"bootstrap-escape-{Guid.NewGuid():N}"));
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
        startInfo.ArgumentList.Add("--out");
        startInfo.ArgumentList.Add(outside);

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains("resolves outside the allowed root", output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outside));
    }
}

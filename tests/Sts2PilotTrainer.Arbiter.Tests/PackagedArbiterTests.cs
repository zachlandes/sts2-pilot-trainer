using System.Diagnostics;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The arbiter as the mod runs it: from a game installation, where there is no
/// worktree, with only the three variables the mod sets to say where its runtime,
/// its sandbox and its workspace are.
///
/// Every other test here runs the arbiter from inside this worktree, which is why
/// the packaged one could die in a type initializer that looked for the worktree
/// and nothing was red. This copies the built arbiter outside the repository and
/// runs the engine's own startup from there.
/// </summary>
public sealed class PackagedArbiterTests : IDisposable
{
    // Under the resolved temp directory, because the resolver compares the library path
    // it was given with the one beside the executable as strings, and the runtime
    // reports the executable's real path while the temp path is reached through a link.
    private readonly string _root = Path.Combine(
        Sts2PilotTrainer.IO.PathContainment.ResolveExistingPath(Path.GetTempPath()),
        $"sts2-arbiter-outside-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [GameFact]
    public void RunsTheEngineFromOutsideAnyWorktree()
    {
        // Laid out as the package is: the arbiter with its prepared runtime in a lib
        // directory beside it, which is the one library location the resolver accepts
        // outside a worktree.
        var built = Path.GetDirectoryName(Arbiter.CliPath)!;
        var copy = Path.Combine(_root, "arbiter");
        CopyDirectory(built, copy);
        CopyDirectory(Path.Combine(Arbiter.RepoRoot, "build", "lib"), Path.Combine(copy, "lib"));
        var workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(Path.Combine(workspace, "sandbox"));
        Assert.Null(FindWorktreeAbove(copy));

        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workspace,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(copy, "sts2-arbiter.dll"));
        start.ArgumentList.Add("engine-commands");
        start.Environment["STS2_PILOT_TRAINER_LIB"] = Path.Combine(copy, "lib");
        start.Environment["STS2_PILOT_TRAINER_SANDBOX"] = Path.Combine(workspace, "sandbox");
        start.Environment["STS2_PILOT_TRAINER_WORKSPACE"] = workspace;

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, output + error);
        Assert.Contains("build    : v0.111.0", output, StringComparison.Ordinal);
        Assert.DoesNotContain("worktree root", error, StringComparison.Ordinal);
    }

    private static string? FindWorktreeAbove(string path)
    {
        for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "sts2-pilot-trainer.sln"))) return directory.FullName;
        }
        return null;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }
}

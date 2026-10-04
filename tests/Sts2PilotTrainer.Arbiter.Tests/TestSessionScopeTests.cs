using System.Diagnostics;
using System.Xml.Linq;

namespace Sts2PilotTrainer.Arbiter.Tests;

public sealed class TestSessionScopeTests : IDisposable
{
    private const string Exclusion = "FullyQualifiedName!~Sts2PilotTrainer.Arbiter.Tests.GeneratedCoverageTests";
    private readonly string _root = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", "session-scope", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Theory]
    [InlineData("docs/guide.md", true)]
    [InlineData("README.md", true)]
    [InlineData("src/Sts2PilotTrainer.Trainer/LibraryCopy.cs", true)]
    [InlineData("src/Sts2PilotTrainer.Mod/RunRecorder.cs", false)]
    [InlineData("src/Sts2PilotTrainer.Replay/RunCapture.cs", false)]
    [InlineData("src/Sts2PilotTrainer.Engine/RunDriver.cs", false)]
    [InlineData("tests/Sts2PilotTrainer.Mod.Tests/GeneratedCoverageTests.cs", false)]
    [InlineData("manifests/run.replay.json", false)]
    [InlineData("scripts/test-session.sh", false)]
    [InlineData("unclassified/file.cs", false)]
    public void OnlyKnownIndependentPathsOmitGeneratedRows(string path, bool omit)
    {
        if (OperatingSystem.IsWindows()) return;
        Prepare();
        Put(path, "changed");
        Git("add", ".");
        Git("commit", "-qm", "change");
        var result = Run();
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(omit, result.Args.Contains(Exclusion, StringComparer.Ordinal));
        Assert.Contains("Generated coverage:", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("untracked")]
    [InlineData("unstaged")]
    [InlineData("staged")]
    [InlineData("deleted")]
    [InlineData("renamed")]
    public void UncommittedAndRenamedRelevantPathsKeepGeneratedRows(string kind)
    {
        if (OperatingSystem.IsWindows()) return;
        Prepare();
        Put("docs/guide.md", "doc change");
        var path = Path.Combine(_root, "src", "Sts2PilotTrainer.Replay", "RunCapture.cs");
        switch (kind)
        {
            case "untracked": Put("src/Sts2PilotTrainer.Mod/NewRecorder.cs", "new"); break;
            case "unstaged": File.AppendAllText(path, "changed"); break;
            case "staged": File.AppendAllText(path, "changed"); Git("add", "."); break;
            case "deleted": File.Delete(path); break;
            case "renamed": Git("mv", "src/Sts2PilotTrainer.Replay/RunCapture.cs", "docs/renamed.md"); break;
        }
        Assert.DoesNotContain(Exclusion, Run().Args);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingHistoryOrNoChangesKeepTheFullSuite(bool missingHistory)
    {
        if (OperatingSystem.IsWindows()) return;
        Prepare();
        if (missingHistory)
        {
            Git("update-ref", "-d", "refs/remotes/origin/main");
            Put("docs/guide.md", "changed");
        }
        Assert.DoesNotContain(Exclusion, Run().Args);
    }

    [Fact]
    public void FullOverridesAnIndependentChangeAndThreadsAreConfigurable()
    {
        if (OperatingSystem.IsWindows()) return;
        Prepare();
        Put("docs/guide.md", "changed");
        var result = Run("3", "--full");
        Assert.DoesNotContain(Exclusion, result.Args);
        Assert.Contains("xUnit.MaxParallelThreads=3", result.Args);
        Assert.Contains("-m:1", result.Args);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("not-a-number")]
    public void InvalidThreadBudgetsRefuseBeforeStartingTests(string threads)
    {
        if (OperatingSystem.IsWindows()) return;
        Prepare();
        var result = Run(threads);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.Args);
    }

    [Fact]
    public void ExplicitScopesAreNeverAutomaticallyFiltered()
    {
        if (OperatingSystem.IsWindows()) return;
        Prepare();
        Put("docs/guide.md", "changed");
        var result = Run(null, "a.dll", "--filter", "FullyQualifiedName~GeneratedCoverageTests");
        Assert.Equal(new[] { "test", "a.dll", "--filter", "FullyQualifiedName~GeneratedCoverageTests" }, result.Args);
    }

    [Fact]
    public void DirectRunsHaveTheSameBoundAndDefaultCollectionBudget()
    {
        var settings = XDocument.Load(Path.Combine(Arbiter.RepoRoot, ".runsettings"));
        Assert.Equal(14400000, (int)settings.Root!.Element("RunConfiguration")!.Element("TestSessionTimeout")!);
        Assert.Equal(1, (int)settings.Root.Element("RunConfiguration")!.Element("MaxCpuCount")!);
        Assert.Equal(12, (int)settings.Root.Element("xUnit")!.Element("MaxParallelThreads")!);
    }

    private void Prepare()
    {
        Directory.CreateDirectory(_root);
        Git("init", "-q", "-b", "main");
        Git("config", "user.email", "fixture@example.invalid");
        Git("config", "user.name", "Fixture");
        Put("README.md", "original");
        Put("src/Sts2PilotTrainer.Replay/RunCapture.cs", "original");
        Git("add", ".");
        Git("commit", "-qm", "base");
        Git("update-ref", "refs/remotes/origin/main", "HEAD");
        Put("tools/dotnet", "#!/usr/bin/env bash\nprintf '%s\\n' \"$@\" > \"$CAPTURE_ARGS\"\necho 'Passed! - Failed: 0, Passed: 1'\n");
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(Path.Combine(_root, "tools", "dotnet"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Put(".gitignore", "tools/\nargs.txt\n.gitignore\n");
    }

    private void Put(string path, string text)
    {
        var file = Path.Combine(_root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    private void Git(params string[] args)
    {
        var result = Execute("git", args);
        Assert.True(result.ExitCode == 0, result.Output);
    }

    private (int ExitCode, string Output) Execute(string command, string[] args, Dictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(command) { WorkingDirectory = _root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        if (environment is not null)
            foreach (var (name, value) in environment) start.Environment[name] = value;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + error);
    }

    private (int ExitCode, string Output, string[] Args) Run(string? threads = null, params string[] args)
    {
        var environment = new Dictionary<string, string>
        {
            ["PATH"] = Path.Combine(_root, "tools") + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["CAPTURE_ARGS"] = Path.Combine(_root, "args.txt"),
            ["RUNMOBILE_TEST_THREADS"] = threads ?? "12",
        };
        var result = Execute("bash", new[] { Path.Combine(Arbiter.RepoRoot, "scripts", "test-session.sh") }.Concat(args).ToArray(), environment);
        var capture = Path.Combine(_root, "args.txt");
        return (result.ExitCode, result.Output, File.Exists(capture) ? File.ReadAllLines(capture) : []);
    }
}

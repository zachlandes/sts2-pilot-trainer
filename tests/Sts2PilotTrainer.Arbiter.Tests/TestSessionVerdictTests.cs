using System.Diagnostics;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// Holds ./scripts/test-session.sh to the one claim it exists to make: a session
/// that did not run to completion is not a pass.
///
/// `dotnet test` prints "Passed!" with a partial total for an aborted run, and three
/// runs were read as green that way. So the verdict is asserted against both signals
/// a completed session leaves - the exit code and the abort marker - with a stub
/// `dotnet` for the combinations a real run cannot be made to produce on demand, and
/// against a real timed-out session for the one that matters most.
/// </summary>
public sealed class TestSessionVerdictTests : IDisposable
{
    private const string Aborted = "TEST SESSION ABORTED";
    private const string Passed = "TEST SESSION PASSED";
    private const string Failed = "TEST SESSION FAILED";

    /// <summary>What `dotnet test` prints when .runsettings' TestSessionTimeout fires.</summary>
    private const string AbortedTranscript = """
        Aborting test run: test run timeout of 900000 milliseconds exceeded.

        Passed!  - Failed:     0, Passed:    80, Skipped:     0, Total:    80, Duration: 12 s
        Test Run Aborted.
        """;

    private const string CompletedTranscript =
        "Passed!  - Failed:     0, Passed:   835, Skipped:     0, Total:   835, Duration: 699 ms";

    private readonly string _sandbox = Path.Combine(
        Arbiter.RepoRoot, "build", "test-scratch", "test-session", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_sandbox)) Directory.Delete(_sandbox, recursive: true);
    }

    /// <summary>
    /// The defect itself: the tool exits zero, the summary says Passed, and the run
    /// never finished. Only the abort marker disagrees, so only it can be believed.
    /// </summary>
    [Fact]
    public void AnAbortedSessionFailsEvenWhenTheToolReportsPassedAndExitsZero()
    {
        if (OperatingSystem.IsWindows()) return;

        var verdict = RunWithStubbedDotnet(AbortedTranscript, exitCode: 0);

        Assert.NotEqual(0, verdict.ExitCode);
        Assert.Contains(Aborted, verdict.All, StringComparison.Ordinal);
        Assert.Contains("Incomplete!  - Failed:", verdict.All, StringComparison.Ordinal);
        Assert.DoesNotContain("Passed", verdict.All, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbortedSessionFailsWhenTheToolAlsoExitsNonZero()
    {
        if (OperatingSystem.IsWindows()) return;

        var verdict = RunWithStubbedDotnet(AbortedTranscript, exitCode: 1);

        Assert.NotEqual(0, verdict.ExitCode);
        Assert.Contains(Aborted, verdict.All, StringComparison.Ordinal);
        Assert.DoesNotContain("Passed", verdict.All, StringComparison.Ordinal);
    }

    [Fact]
    public void ACompletedSessionPasses()
    {
        if (OperatingSystem.IsWindows()) return;

        var verdict = RunWithStubbedDotnet(CompletedTranscript, exitCode: 0);

        Assert.Equal(0, verdict.ExitCode);
        Assert.Contains(Passed, verdict.All, StringComparison.Ordinal);
    }

    [Fact]
    public void ATranscriptThatCannotBeCreatedFails()
    {
        if (OperatingSystem.IsWindows()) return;

        var verdict = RunWithStubbedDotnet(
            CompletedTranscript,
            exitCode: 0,
            environment: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TMPDIR"] = Path.Combine(_sandbox, "missing"),
            });

        AssertRefused(verdict);
    }

    [Fact]
    public void ATranscriptThatCannotBeWrittenFails()
    {
        if (OperatingSystem.IsWindows()) return;

        var verdict = RunWithStubbedDotnet(
            CompletedTranscript,
            exitCode: 0,
            transcriptWriteFails: true);

        AssertRefused(verdict);
    }

    [Fact]
    public void AFailedSessionKeepsTheToolsExitCode()
    {
        if (OperatingSystem.IsWindows()) return;

        var verdict = RunWithStubbedDotnet("Failed!  - Failed:     3, Passed:   832", exitCode: 3);

        Assert.Equal(3, verdict.ExitCode);
        Assert.Contains(Failed, verdict.All, StringComparison.Ordinal);
        Assert.DoesNotContain(Passed, verdict.All, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same claim against a session vstest really did abort. It is this assembly
    /// under a one-millisecond session bound and a filter that matches no test, so the
    /// bound expires before anything runs and this test cannot re-enter itself.
    /// </summary>
    [Fact]
    public void ARealTimedOutSessionFails()
    {
        if (OperatingSystem.IsWindows()) return;

        var assembly = typeof(TestSessionVerdictTests).Assembly.Location;

        var verdict = RunTestSession(
            environment: new Dictionary<string, string>(StringComparer.Ordinal),
            assembly,
            "--nologo",
            "--filter",
            "FullyQualifiedName~NoTestBearsThisName",
            "--",
            "RunConfiguration.TestSessionTimeout=1");

        Assert.NotEqual(0, verdict.ExitCode);
        Assert.Contains(Aborted, verdict.All, StringComparison.Ordinal);
        Assert.DoesNotContain("Passed", verdict.All, StringComparison.Ordinal);
    }

    /// <summary>
    /// Runs the script against a `dotnet` that prints a recorded transcript and exits
    /// as told. A real abort cannot be asked for an exit code, and the pairing of a
    /// zero exit with an abort is exactly what the script must not be caught out by.
    /// </summary>
    private Arbiter.Result RunWithStubbedDotnet(
        string transcript,
        int exitCode,
        IReadOnlyDictionary<string, string>? environment = null,
        bool transcriptWriteFails = false)
    {
        var tools = Path.Combine(_sandbox, "tools");
        Directory.CreateDirectory(tools);
        var stub = Path.Combine(tools, "dotnet");
        File.WriteAllText(stub, $"#!/usr/bin/env bash\ncat <<'TRANSCRIPT'\n{transcript}\nTRANSCRIPT\nexit {exitCode}\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(stub,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var processEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["PATH"] = tools + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
        };
        if (environment is not null)
        {
            foreach (var (name, value) in environment) processEnvironment[name] = value;
        }

        if (transcriptWriteFails)
        {
            var mktemp = Path.Combine(tools, "mktemp");
            File.WriteAllText(mktemp,
                "#!/usr/bin/env bash\nprintf '%s\\n' \"$BROKEN_TRANSCRIPT\"\n");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(mktemp,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            processEnvironment["BROKEN_TRANSCRIPT"] = Path.Combine(_sandbox, "missing", "transcript");
        }

        return RunTestSession(processEnvironment, "a-session-the-stub-ignores");
    }

    private static void AssertRefused(Arbiter.Result verdict)
    {
        Assert.NotEqual(0, verdict.ExitCode);
        Assert.Contains(Failed, verdict.All, StringComparison.Ordinal);
        Assert.DoesNotContain(Passed, verdict.All, StringComparison.Ordinal);
        var lines = verdict.Output.Split(
            '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.StartsWith(Failed, lines[^1], StringComparison.Ordinal);
    }

    private static Arbiter.Result RunTestSession(
        IReadOnlyDictionary<string, string> environment, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            WorkingDirectory = Arbiter.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Path.Combine(Arbiter.RepoRoot, "scripts", "test-session.sh"));
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        foreach (var (name, value) in environment) startInfo.Environment[name] = value;

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new Arbiter.Result(process.ExitCode, output, error);
    }
}

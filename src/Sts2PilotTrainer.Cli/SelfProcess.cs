using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Sts2PilotTrainer.Cli;

/// <summary>
/// Runs one of this tool's own commands in a fresh process.
///
/// Not an optimisation and not indirection for its own sake: the game engine keeps
/// a great deal of static state - a run manager singleton, a model database, a
/// serialization cache - and a second run in the same process starts from whatever
/// the first one left behind. Every claim this project makes about determinism is a
/// claim about starting from nothing, so each run gets a process that started from
/// nothing.
///
/// A child lives exactly as long as the command that started it. Two things hold
/// that, because neither holds it alone. The parent takes every live child with it
/// when it is asked to stop - SIGTERM, SIGINT, SIGHUP - by killing the child's whole
/// tree and then ending with the signal's own exit status. And the
/// child watches for its parent going away without asking, through
/// <see cref="ParentProcess"/>, because a parent killed outright is never given the
/// chance to say so. Without both, a gate stopped twelve seconds in left its
/// negative-controls child replaying for another quarter of an hour, reparented to
/// init and still writing into the gate's evidence directory.
/// </summary>
internal static class SelfProcess
{
    internal sealed record Result(int ExitCode, string StandardOutput, string StandardError);

    /// <summary>The children still running, so a signal can name them.</summary>
    private static readonly ConcurrentDictionary<int, Process> Live = new();

    private static readonly Lazy<IReadOnlyList<PosixSignalRegistration>> Signals = new(RegisterSignals);

    /// <summary>The status this process is ending with once a stop signal arrived,
    /// or zero while none has.</summary>
    private static volatile int StopExitCode;

    /// <summary>The engine's own test hook for a forced initialisation failure, and
    /// the variable a test sets to have it fire in this process's children only.</summary>
    internal const string RequiredInitFailure = "STS2_PILOT_TRAINER_TEST_REQUIRED_INIT_FAILURE";
    internal const string ChildRequiredInitFailure = "STS2_PILOT_TRAINER_TEST_CHILD_REQUIRED_INIT_FAILURE";

    internal static Result Run(params string[] args)
    {
        var executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine this process's own executable path.");

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // A framework-dependent build launches through the dotnet host, in which case
        // ProcessPath is the host and the managed assembly has to be named again.
        if (Path.GetFileNameWithoutExtension(executable) is "dotnet")
        {
            startInfo.ArgumentList.Add(Environment.GetCommandLineArgs()[0]);
        }

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.Environment[ParentProcess.Variable] =
            Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // A test that wants the child alone to fail engine initialisation hands the
        // failure over here, because the parent starts the engine too: a corpus command
        // reads the build under test off it before it spawns anything
        if (Environment.GetEnvironmentVariable(ChildRequiredInitFailure) is { Length: > 0 } childFailure)
        {
            startInfo.Environment[RequiredInitFailure] = childFailure;
        }

        // Registered before the child exists, so there is no moment a child is alive
        // and a signal would not reach it.
        _ = Signals.Value;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start a child process.");
        Live[process.Id] = process;

        try
        {
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            // A child that ended because this process was told to stop answered
            // nothing; a gate that read its exit status as a failed condition wrote a
            // verdict for a run that never finished.
            if (StopExitCode != 0) Environment.Exit(StopExitCode);

            return new Result(process.ExitCode, stdout, stderr);
        }
        finally
        {
            Live.TryRemove(process.Id, out _);
        }
    }

    /// <summary>
    /// Kills every live child, tree and all, then ends this process with the exit
    /// status the signal gives it, so whatever asked can tell "stopped" from
    /// "finished". Ended here rather than left to the runtime's own handling of the
    /// signal, which runs beside the main thread: with the children dead the main
    /// thread is free again, and a gate got as far as reading a killed child as a
    /// failed condition and writing that verdict before the runtime ended it. The
    /// child is killed rather than signalled because it is this tool again, whose own
    /// work is a replay with nothing to save on the way out, and because a signal it
    /// might not act on would leave the orphan this exists to prevent.
    /// </summary>
    private static IReadOnlyList<PosixSignalRegistration> RegisterSignals()
    {
        var registrations = new List<PosixSignalRegistration>();
        foreach (var (signal, exitCode) in new[]
                 {
                     (PosixSignal.SIGTERM, 128 + 15),
                     (PosixSignal.SIGINT, 128 + 2),
                     (PosixSignal.SIGHUP, 128 + 1),
                 })
        {
            try
            {
                registrations.Add(PosixSignalRegistration.Create(signal, context =>
                {
                    context.Cancel = true;
                    StopExitCode = exitCode;
                    KillLiveChildren();
                    Environment.Exit(exitCode);
                }));
            }
            catch (PlatformNotSupportedException)
            {
                // A platform without this signal has no way to deliver it either.
            }
        }

        return registrations;
    }

    internal static void KillLiveChildren()
    {
        foreach (var child in Live.Values)
        {
            try
            {
                if (child.HasExited) continue;
                if (OperatingSystem.IsWindows())
                {
                    child.Kill(entireProcessTree: true);
                }
                else
                {
                    KillTree(child.Id);
                }
            }
            catch (InvalidOperationException)
            {
                // Exited between the check and the kill.
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Nothing further this process can do to it from here.
            }
        }
    }

    /// <summary>
    /// Kills a process and every descendant it has, read from the process table,
    /// parents before children so nothing is left that could start another.
    ///
    /// Not <see cref="Process.Kill(bool)"/>, whose tree walk on Unix stops each
    /// process with SIGSTOP before it looks up that process's children. A stopped
    /// child is one this runtime's own child reaper cannot account for on macOS: the
    /// thread that reaps spins on it holding the lock every process start takes, so
    /// anything the walk needs from then on waits for ever, and the descendant it
    /// stopped is never killed - reparented to init, in state T for good. Nothing
    /// here is stopped. A descendant started between the table being read and the
    /// kill is one <see cref="ParentProcess"/> ends: its parent is gone and it is
    /// running, so it notices.
    /// </summary>
    private static void KillTree(int rootPid)
    {
        var table = ProcessTable();
        var tree = new List<int> { rootPid };
        for (var i = 0; i < tree.Count; i++)
        {
            var parent = tree[i];
            foreach (var (pid, parentPid) in table)
            {
                if (parentPid == parent) tree.Add(pid);
            }
        }

        foreach (var pid in tree) _ = kill(pid, SIGKILL);
    }

    private static IReadOnlyList<(int Pid, int ParentPid)> ProcessTable()
    {
        var ps = new ProcessStartInfo
        {
            FileName = "ps",
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        ps.ArgumentList.Add("-eo");
        ps.ArgumentList.Add("pid=,ppid=");
        var rows = new List<(int, int)>();
        // A table that cannot be read still leaves the root to kill, and its
        // descendants to ParentProcess.
        using var process = Process.Start(ps);
        if (process is null) return rows;
        foreach (var line in process.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            rows.Add((
                int.Parse(parts[0], CultureInfo.InvariantCulture),
                int.Parse(parts[1], CultureInfo.InvariantCulture)));
        }

        process.WaitForExit();
        return rows;
    }

    private const int SIGKILL = 9;

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int kill(int pid, int signal);
}

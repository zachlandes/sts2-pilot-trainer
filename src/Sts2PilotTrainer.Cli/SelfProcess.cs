using System.Collections.Concurrent;
using System.Diagnostics;
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
/// tree before the runtime's own handling of the signal ends the process. And the
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

            return new Result(process.ExitCode, stdout, stderr);
        }
        finally
        {
            Live.TryRemove(process.Id, out _);
        }
    }

    /// <summary>
    /// Kills every live child, tree and all, and lets the signal go on to end this
    /// process the way it would have.
    ///
    /// Not cancelled: a gate asked to stop should stop with the exit status the signal
    /// gives it, so whatever asked can tell "stopped" from "finished". The child is
    /// killed rather than signalled because it is this tool again, whose own work is a
    /// replay with nothing to save on the way out, and because a signal it might not
    /// act on would leave the orphan this exists to prevent.
    /// </summary>
    private static IReadOnlyList<PosixSignalRegistration> RegisterSignals()
    {
        var registrations = new List<PosixSignalRegistration>();
        foreach (var signal in new[] { PosixSignal.SIGTERM, PosixSignal.SIGINT, PosixSignal.SIGHUP })
        {
            try
            {
                registrations.Add(PosixSignalRegistration.Create(signal, context =>
                {
                    KillLiveChildren();
                    context.Cancel = false;
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
                if (!child.HasExited) child.Kill(entireProcessTree: true);
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
}

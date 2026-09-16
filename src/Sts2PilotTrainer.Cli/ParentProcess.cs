using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Sts2PilotTrainer.Cli;

/// <summary>
/// Ends this process when the one that started it is gone.
///
/// The other half of <see cref="SelfProcess"/>'s promise that a child outlives no
/// parent. A parent that is terminated kills its children itself; a parent that is
/// killed outright, or dies, cannot, and the operating system reparents the child to
/// init and lets it run on. So a child started by <see cref="SelfProcess"/> is told
/// its parent's id and watches it from a background thread: on Unix by asking who
/// its parent is now, which changes the moment the parent dies and cannot be fooled
/// by a reused id; on Windows by waiting on the parent's process handle.
///
/// Watched only where <see cref="SelfProcess"/> asked for it. A command somebody runs
/// by hand has no parent it belongs to.
/// </summary>
internal static class ParentProcess
{
    /// <summary>The environment variable a child reads its parent's id from; set by
    /// <see cref="SelfProcess"/> and by nothing else.</summary>
    internal const string Variable = "STS2_ARBITER_PARENT_PID";

    /// <summary>The exit status of a child that stopped because its parent went away:
    /// the one a process ends with on SIGTERM, which is what the parent would have
    /// sent had it been able to.</summary>
    internal const int OrphanedExitCode = 143;

    private const int PollMilliseconds = 250;

    /// <summary>Starts the watch where the environment names a parent, and does
    /// nothing where it does not.</summary>
    internal static void WatchIfStartedByOne()
    {
        var value = Environment.GetEnvironmentVariable(Variable);
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parentId)) return;

        var watch = new Thread(() => Watch(parentId))
        {
            IsBackground = true,
            Name = "parent-process-watch",
        };
        watch.Start();
    }

    private static void Watch(int parentId)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var parent = Process.GetProcessById(parentId);
                parent.WaitForExit();
            }
            catch (ArgumentException)
            {
                // Already gone.
            }
            catch (InvalidOperationException)
            {
                // Exited before it could be waited on.
            }
        }
        else
        {
            // getppid changes the instant the parent dies, whatever took its place.
            while (getppid() == parentId)
            {
                Thread.Sleep(PollMilliseconds);
            }
        }

        // Whatever this one started goes too, and at once rather than when each
        // notices for itself.
        SelfProcess.KillLiveChildren();
        Console.Error.WriteLine(
            $"The process that started this one (pid {parentId.ToString(CultureInfo.InvariantCulture)}) is " +
            "gone; stopping rather than running on as an orphan.");
        Console.Error.Flush();
        Environment.Exit(OrphanedExitCode);
    }

    [DllImport("libc", EntryPoint = "getppid")]
    private static extern int getppid();
}

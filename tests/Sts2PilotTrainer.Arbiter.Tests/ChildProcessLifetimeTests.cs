using System.Diagnostics;
using System.Globalization;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// A gate's children live exactly as long as the gate.
///
/// The gate runs every engine condition in a child process of its own, and a child
/// that outlived a stopped gate was found the hard way: a gate terminated twelve
/// seconds in left its negative-controls child replaying for another quarter of an
/// hour, reparented to init and still writing into the gate's evidence directory. Two
/// things now hold the child to its parent and each is proved here on the real gate,
/// because a stand-in child would prove only that the stand-in stops. A terminated
/// gate kills its children itself, tree and all, before the signal ends it; a gate
/// killed outright cannot, so the child notices its parent is gone and stops on its
/// own. Both are measured with the process table: the descendants a gate has at the
/// moment it is stopped, and whether any is still there afterwards.
///
/// The children stopped here are the gate's long ones - mode discrimination and what
/// follows it take tens of seconds each on this history - so a descendant gone
/// within a few seconds of the signal was stopped by it and did not simply finish.
/// Unix only, because the signals and the process table are.
/// </summary>
public sealed class ChildProcessLifetimeTests
{
    /// <summary>How long a descendant may take to be gone after the gate is stopped:
    /// the parent's kill is immediate, and the child's own watch polls its parent four
    /// times a second.</summary>
    private static readonly TimeSpan GoneWithin = TimeSpan.FromSeconds(5);

    /// <summary>How long to wait for the gate to reach a long child. The first two
    /// conditions are quick and spawn short children of their own.</summary>
    private static readonly TimeSpan LongChildWithin = TimeSpan.FromSeconds(90);

    /// <summary>The children that finish in a second or two, which a signal landing
    /// during one would leave nothing to measure.</summary>
    private static readonly string[] ShortChildren = ["validate", "preflight"];

    [UnixGameFact]
    public void ATerminatedGateTakesItsChildrenWithIt() => AStoppedGateLeavesNoChild("TERM", expectedExit: 143, ALongChild);

    [UnixGameFact]
    public void AKilledGatesChildrenStopOnTheirOwn() => AStoppedGateLeavesNoChild("KILL", expectedExit: 137, ALongChild);

    /// <summary>
    /// The scenario the orphan was found in: the gate stopped while its
    /// negative-controls child has a replay of its own running, so the tree is three
    /// deep and the last condition is the one interrupted. A grandchild is what a
    /// best-effort tree kill can miss, and a gate whose last child was killed can
    /// finish its report as though that child had answered.
    /// </summary>
    [UnixGameFact]
    public void ATerminatedGateTakesItsGrandchildrenWithIt() =>
        AStoppedGateLeavesNoChild("TERM", expectedExit: 143, NegativeControlsWithAReplayOfItsOwn);

    [UnixGameFact]
    public void AKilledGatesGrandchildrenStopOnTheirOwn() =>
        AStoppedGateLeavesNoChild("KILL", expectedExit: 137, NegativeControlsWithAReplayOfItsOwn);

    private static void AStoppedGateLeavesNoChild(
        string signal, int expectedExit, Func<IReadOnlyList<ProcessRow>, bool> readyToStop)
    {
        var outDir = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", $"gate-{signal}-{Guid.NewGuid():N}"[..16]);
        Directory.CreateDirectory(outDir);

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Arbiter.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { Arbiter.CliPath, "gate", Arbiter.Manifest, "--out", outDir })
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var gate = Process.Start(startInfo)!;
        var output = gate.StandardOutput.ReadToEndAsync();
        var error = gate.StandardError.ReadToEndAsync();
        // Every descendant seen before the signal, so a reparented one is still swept.
        var known = new List<ProcessRow>();
        try
        {
            var descendants = WaitForALongChild(gate.Id, readyToStop);
            Assert.NotEmpty(descendants);
            known.AddRange(descendants);

            Signal(gate.Id, signal);
            Assert.True(gate.WaitForExit(GoneWithin), "The gate did not stop on the signal.");

            var deadline = Stopwatch.StartNew();
            while (descendants.Any(IsAlive))
            {
                Assert.True(
                    deadline.Elapsed < GoneWithin,
                    $"Descendants of the {signal}ed gate are still there: " +
                    string.Join("; ", descendants.Where(IsAlive).Select(DescribeLive)));
                Thread.Sleep(100);
            }

            // A stopped gate reached no verdict; one written here would name a
            // condition that never ran to its end as failed.
            Assert.False(
                File.Exists(Path.Combine(outDir, "publication-gate.json")),
                $"The {signal}ed gate wrote a verdict: {ReadVerdict(outDir)}");
            Assert.Equal(expectedExit, gate.ExitCode);
        }
        finally
        {
            // A failed run is one whose orphans are the finding, so they are taken down
            // here rather than left to the next test; swept until the table shows none,
            // because an orphan can spawn a child of its own between two readings.
            if (!gate.HasExited) gate.Kill(entireProcessTree: true);
            for (var sweep = 0; sweep < 10; sweep++)
            {
                var stragglers = Descendants(gate.Id).Concat(known.Where(IsAlive)).ToList();
                if (stragglers.Count == 0) break;
                foreach (var straggler in stragglers) Signal(straggler.Pid, "KILL");
                Thread.Sleep(100);
            }
        }
    }

    private sealed record ProcessRow(int Pid, int ParentPid, string State, string Args);

    /// <summary>Every descendant of the gate once one of them is a long child, and
    /// the whole tree at that moment, which is what a kill has to take.</summary>
    private static IReadOnlyList<ProcessRow> WaitForALongChild(
        int gatePid, Func<IReadOnlyList<ProcessRow>, bool> readyToStop)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < LongChildWithin)
        {
            var descendants = Descendants(gatePid);
            if (readyToStop(descendants))
            {
                return descendants;
            }

            Thread.Sleep(100);
        }

        throw new Xunit.Sdk.XunitException(
            "The gate never reached a long child within the wait. Its process tree: " +
            string.Join("; ", Descendants(gatePid).Select(Describe)));
    }

    private static bool ALongChild(IReadOnlyList<ProcessRow> descendants) =>
        descendants.Any(row => IsArbiter(row) && !ShortChildren.Any(row.Args.Contains));

    /// <summary>The negative-controls child with a replay child of its own: the tree
    /// as it stood when the orphan was found.</summary>
    private static bool NegativeControlsWithAReplayOfItsOwn(IReadOnlyList<ProcessRow> descendants) =>
        descendants.Any(row => IsArbiter(row)
            && row.Args.Contains(" negative-controls ", StringComparison.Ordinal)
            && descendants.Any(child => child.ParentPid == row.Pid && IsArbiter(child)
                && child.Args.Contains(" replay ", StringComparison.Ordinal)));

    private static bool IsArbiter(ProcessRow row) => row.Args.Contains("sts2-arbiter.dll", StringComparison.Ordinal);

    private static string ReadVerdict(string outDir)
    {
        try
        {
            return File.ReadAllText(Path.Combine(outDir, "publication-gate.json"));
        }
        catch (IOException)
        {
            return "(unreadable)";
        }
    }

    private static string Describe(ProcessRow row) =>
        $"{row.Pid.ToString(CultureInfo.InvariantCulture)} ({row.Args})";

    /// <summary>The row as the table shows it now: parent and state, because an
    /// orphan reparented to init and left stopped is a different finding from one
    /// still running.</summary>
    private static string DescribeLive(ProcessRow row)
    {
        var now = ProcessTable().FirstOrDefault(current => current.Pid == row.Pid);
        return now is null ? Describe(row) : $"{Describe(row)} [ppid {now.ParentPid}, stat {now.State}]";
    }

    private static bool IsAlive(ProcessRow row)
    {
        try
        {
            using var process = Process.GetProcessById(row.Pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The process table, read whole once, and walked down from the gate.</summary>
    private static IReadOnlyList<ProcessRow> Descendants(int rootPid)
    {
        var table = ProcessTable();
        var found = new List<ProcessRow>();
        var frontier = new Queue<int>([rootPid]);
        while (frontier.TryDequeue(out var parent))
        {
            foreach (var row in table.Where(row => row.ParentPid == parent))
            {
                found.Add(row);
                frontier.Enqueue(row.Pid);
            }
        }

        return found;
    }

    private static IReadOnlyList<ProcessRow> ProcessTable()
    {
        var ps = new ProcessStartInfo("ps")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { "-eo", "pid=,ppid=,stat=,args=" }) ps.ArgumentList.Add(arg);
        using var process = Process.Start(ps)!;
        var rows = new List<ProcessRow>();
        foreach (var line in process.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) continue;
            rows.Add(new ProcessRow(
                int.Parse(parts[0], CultureInfo.InvariantCulture),
                int.Parse(parts[1], CultureInfo.InvariantCulture),
                parts[2],
                parts.Length > 3 ? parts[3] : string.Empty));
        }

        process.WaitForExit();
        return rows;
    }

    private static void Signal(int pid, string signal)
    {
        var kill = new ProcessStartInfo("kill") { UseShellExecute = false };
        foreach (var arg in new[] { $"-{signal}", pid.ToString(CultureInfo.InvariantCulture) })
        {
            kill.ArgumentList.Add(arg);
        }

        using var process = Process.Start(kill)!;
        process.WaitForExit();
    }
}

/// <summary>A game fact that also needs a Unix process table and signals.</summary>
public sealed class UnixGameFactAttribute : FactAttribute
{
    public UnixGameFactAttribute()
    {
        if (!Arbiter.GameAvailable)
        {
            Skip = Arbiter.SkipReason;
        }
        else if (OperatingSystem.IsWindows())
        {
            Skip = "Needs POSIX signals and a ps process table.";
        }
    }
}

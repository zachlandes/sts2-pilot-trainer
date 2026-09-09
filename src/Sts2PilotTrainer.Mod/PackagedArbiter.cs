using System.Diagnostics;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The headless arbiter this mod ships beside itself, started as its own process.
///
/// The one way the mod reaches the replay engine: <c>EngineHost.Start</c> must never
/// run inside the retail client, so anything that has to replay a history - proving a
/// run for publication, materialising the save a floor arrival is restored from - is
/// done by the packaged CLI in a process of its own, with its workspace and its sandbox
/// pointed at a directory under the store. Two callers and one launcher, so what the
/// subprocess may see and where it may write are decided once.
///
/// It refuses rather than guesses where the package is incomplete: an arbiter with no
/// prepared assembly copy beside it is one that cannot replay anything.
/// </summary>
internal static class PackagedArbiter
{
    /// <summary>Longer than any replay this build has measured, and the bound a run
    /// that hung is killed at.</summary>
    internal static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(15);

    private static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(30);

    /// <summary>What one arbiter run reported.</summary>
    internal sealed record Result(int ExitCode, string Output, string Error, bool TimedOut)
    {
        internal bool Succeeded => ExitCode == 0 && !TimedOut;

        /// <summary>The last of what it printed, for a refusal that has to say why.</summary>
        internal string Tail(int lines = 6)
        {
            var all = (Output + Error).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", all.TakeLast(lines).Select(line => line.Trim()));
        }
    }

    /// <summary>
    /// The start info for one run of the packaged arbiter, or a refusal naming what is
    /// missing from the package.
    /// </summary>
    /// <param name="workspacePath">The directory the arbiter may write evidence and
    /// caches under; every <c>--out</c> and <c>--cache</c> it is given has to be inside
    /// it, which <c>WorktreePath</c> enforces on its side.</param>
    /// <param name="sandboxPath">Where the engine's own profile writes are routed.</param>
    internal static ProcessStartInfo StartInfo(string workspacePath, string sandboxPath, params string[] arguments) =>
        StartInfoIn(InstalledDirectory(), workspacePath, sandboxPath, arguments);

    /// <summary>Where the installed package keeps the arbiter: beside the mod assembly.</summary>
    internal static string InstalledDirectory() =>
        Path.Combine(Path.GetDirectoryName(typeof(PackagedArbiter).Assembly.Location)!, "arbiter");

    /// <summary>
    /// A variable the running game sets in its own process, which a child would inherit.
    ///
    /// The game's Sentry bridge exports the path of its native library at runtime, and
    /// the game assembly's own initializer loads that library wherever it finds the
    /// variable. In an arbiter process there is no Godot for it to attach to: from a
    /// terminal the process died at that load, and spawned from the client it stood
    /// still inside a type initializer for as long as it was left. Neither is a state
    /// the arbiter can report, so the variable is left out of everything this mod
    /// starts. Measured on v0.111.0; the prefix is stripped rather than the one name
    /// because the same bridge owns every variable it exports.
    /// </summary>
    internal const string GameProcessVariablePrefix = "SENTRY_";

    /// <summary>
    /// The same start info for an arbiter in a named directory. Not an overload of
    /// <see cref="StartInfo"/>: every argument there is a string, so an overload with
    /// one more string bound the mod's own calls in expanded form and made the
    /// workspace the arbiter directory.
    /// </summary>
    /// <param name="arbiterDirectory">The directory holding the arbiter executable and
    /// its prepared <c>lib</c>.</param>
    internal static ProcessStartInfo StartInfoIn(
        string arbiterDirectory, string workspacePath, string sandboxPath, params string[] arguments)
    {
        var arbiter = Path.Combine(
            arbiterDirectory,
            OperatingSystem.IsWindows() ? "sts2-arbiter.exe" : "sts2-arbiter");
        if (!File.Exists(arbiter))
        {
            throw new InvalidOperationException("The installed local replay arbiter is unavailable.");
        }

        var preparedAssemblyDirectory = Path.Combine(arbiterDirectory, "lib");
        if (!File.Exists(Path.Combine(preparedAssemblyDirectory, "prepared-assembly.json")))
        {
            throw new InvalidOperationException("The installed replay runtime is incomplete.");
        }

        var start = new ProcessStartInfo
        {
            FileName = arbiter,
            WorkingDirectory = workspacePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var inherited in start.Environment.Keys
                     .Where(name => name.StartsWith(GameProcessVariablePrefix, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            start.Environment.Remove(inherited);
        }
        start.Environment["STS2_PILOT_TRAINER_LIB"] = preparedAssemblyDirectory;
        start.Environment["STS2_PILOT_TRAINER_SANDBOX"] = sandboxPath;
        start.Environment["STS2_PILOT_TRAINER_WORKSPACE"] = workspacePath;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    /// <summary>
    /// Runs the arbiter to completion, off the game's thread, and answers what it
    /// reported. A run that outlives the timeout is killed, and one that will not die is
    /// reported as such rather than abandoned quietly.
    /// </summary>
    internal static async Task<Result> RunAsync(ProcessStartInfo start)
    {
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("The local replay arbiter could not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        var timedOut = false;
        using var timeout = new CancellationTokenSource(ProcessTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                timedOut = true;
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    if (!process.HasExited)
                    {
                        throw new ArbiterStillRunningException(
                            $"The local replay arbiter timed out and could not be stopped: {ex.Message}");
                    }
                }

                if (!process.WaitForExit((int)TerminationTimeout.TotalMilliseconds))
                {
                    throw new ArbiterStillRunningException(
                        "The local replay arbiter timed out and did not stop when asked.");
                }
            }
        }

        await Task.WhenAll(output, errors).ConfigureAwait(false);
        return new Result(timedOut ? -1 : process.ExitCode, output.Result, errors.Result, timedOut);
    }
}

/// <summary>An arbiter process that is still alive after it was told to stop. The
/// workspace it may still be writing into must not be removed under it.</summary>
internal sealed class ArbiterStillRunningException(string message) : InvalidOperationException(message);

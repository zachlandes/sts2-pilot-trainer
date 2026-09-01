using System.Diagnostics;

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
/// </summary>
internal static class SelfProcess
{
    internal sealed record Result(int ExitCode, string StandardOutput, string StandardError);

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

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start a child process.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return new Result(process.ExitCode, stdout, stderr);
    }
}

using System.Diagnostics;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

internal static class PublicationGate
{
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(30);

    internal static Task<bool> RunAsync(ReplayManifest recording)
    {
        var id = Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        var workspace = $"publication/{id}";
        var storeRoot = RunmobileStore.Root;
        RecordingRetention.BeginPublicationWorkspace(storeRoot, workspace);
        try
        {
            var relativeManifestPath = $"{workspace}/candidate.replay.json";
            RunmobileStore.Write(relativeManifestPath, ManifestJson.Serialize(recording));
            var manifestPath = RunmobileStore.PathOf(relativeManifestPath);
            var workspacePath = RunmobileStore.PathOf(workspace);
            var evidencePath = RunmobileStore.PathOf($"{workspace}/evidence");
            var sandboxPath = RunmobileStore.PathOf($"{workspace}/sandbox");

            var arbiterDirectory = Path.Combine(
                Path.GetDirectoryName(typeof(PublicationGate).Assembly.Location)!, "arbiter");
            var arbiter = Path.Combine(
                arbiterDirectory,
                OperatingSystem.IsWindows() ? "sts2-arbiter.exe" : "sts2-arbiter");
            if (!File.Exists(arbiter))
            {
                throw new ShareValidationException(
                    "The installed local replay arbiter is unavailable, so this run was not sent.");
            }

            var preparedAssemblyDirectory = Path.Combine(arbiterDirectory, "lib");
            if (!File.Exists(Path.Combine(preparedAssemblyDirectory, "prepared-assembly.json")))
            {
                throw new ShareValidationException(
                    "The installed replay runtime is incomplete, so this run was not sent.");
            }

            return Task.Run(async () =>
            {
                var removeWorkspace = true;
                try
                {
                    var start = new ProcessStartInfo
                    {
                        FileName = arbiter,
                        WorkingDirectory = workspacePath,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                    };
                    start.Environment["STS2_PILOT_TRAINER_LIB"] = preparedAssemblyDirectory;
                    start.Environment["STS2_PILOT_TRAINER_SANDBOX"] = sandboxPath;
                    start.Environment["STS2_PILOT_TRAINER_WORKSPACE"] = workspacePath;
                    start.ArgumentList.Add("gate");
                    start.ArgumentList.Add(manifestPath);
                    start.ArgumentList.Add("--out");
                    start.ArgumentList.Add(evidencePath);

                    using var process = Process.Start(start)
                        ?? throw new ShareValidationException("The local replay arbiter could not start.");
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
                                    removeWorkspace = false;
                                    throw new ShareValidationException(
                                        $"The local replay arbiter timed out and could not be stopped: {ex.Message}");
                                }
                            }

                            if (!process.WaitForExit((int)TerminationTimeout.TotalMilliseconds))
                            {
                                removeWorkspace = false;
                                throw new ShareValidationException(
                                    "The local replay arbiter timed out and did not stop when asked.");
                            }
                        }
                    }

                    await Task.WhenAll(output, errors).ConfigureAwait(false);
                    if (timedOut)
                    {
                        throw new ShareValidationException(
                            "The local replay arbiter timed out, so this run was not sent.");
                    }
                    return process.ExitCode == 0;
                }
                finally
                {
                    if (removeWorkspace)
                        RecordingRetention.RemovePublicationWorkspace(storeRoot, workspace);
                    else
                        RecordingRetention.ReleasePublicationWorkspace(storeRoot, workspace);
                }
            });
        }
        catch
        {
            RecordingRetention.RemovePublicationWorkspace(storeRoot, workspace);
            throw;
        }
    }
}

using System.Diagnostics;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

internal static class PublicationGate
{
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
                    await process.WaitForExitAsync().ConfigureAwait(false);
                    await Task.WhenAll(output, errors).ConfigureAwait(false);
                    return process.ExitCode == 0;
                }
                finally
                {
                    RecordingRetention.RemovePublicationWorkspace(storeRoot, workspace);
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

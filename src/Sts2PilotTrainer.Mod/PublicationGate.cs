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
        RecordingRetention.BeginDerivedWorkspace(storeRoot, workspace);
        try
        {
            var relativeManifestPath = $"{workspace}/candidate.replay.json";
            RunmobileStore.Write(relativeManifestPath, ManifestJson.Serialize(recording));
            var manifestPath = RunmobileStore.PathOf(relativeManifestPath);
            var workspacePath = RunmobileStore.PathOf(workspace);
            var evidencePath = RunmobileStore.PathOf($"{workspace}/evidence");
            var sandboxPath = RunmobileStore.PathOf($"{workspace}/sandbox");

            var start = Start(workspacePath, sandboxPath, "gate", manifestPath, "--out", evidencePath);

            return Task.Run(async () =>
            {
                var removeWorkspace = true;
                try
                {
                    var result = await PackagedArbiter.RunAsync(start).ConfigureAwait(false);
                    if (result.TimedOut)
                    {
                        throw new ShareValidationException(
                            "The local replay arbiter timed out, so this run was not sent.");
                    }

                    return result.Succeeded;
                }
                catch (ArbiterStillRunningException ex)
                {
                    removeWorkspace = false;
                    throw new ShareValidationException(ex.Message);
                }
                finally
                {
                    if (removeWorkspace)
                        RecordingRetention.RemoveDerivedWorkspace(storeRoot, workspace);
                    else
                        RecordingRetention.ReleaseDerivedWorkspace(storeRoot, workspace);
                }
            });
        }
        catch
        {
            RecordingRetention.RemoveDerivedWorkspace(storeRoot, workspace);
            throw;
        }
    }

    /// <summary>The packaged arbiter's start info, with its refusals said in this
    /// surface's words: a run that cannot be proved is a run that was not sent.</summary>
    private static System.Diagnostics.ProcessStartInfo Start(
        string workspacePath, string sandboxPath, params string[] arguments)
    {
        try
        {
            return PackagedArbiter.StartInfo(workspacePath, sandboxPath, arguments);
        }
        catch (InvalidOperationException ex)
        {
            throw new ShareValidationException($"{ex.Message} This run was not sent.");
        }
    }
}

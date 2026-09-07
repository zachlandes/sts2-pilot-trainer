using System.Diagnostics;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

internal static class PublicationGate
{
    private const string ArbiterEnvironmentVariable = "RUNMOBILE_ARBITER";

    internal static Task<bool> RunAsync(ReplayManifest recording)
    {
        var id = Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture);
        var relativeManifestPath = $"publication/{id}.candidate.replay.json";
        RunmobileStore.Write(relativeManifestPath, ManifestJson.Serialize(recording));
        var manifestPath = RunmobileStore.PathOf(relativeManifestPath);
        var evidencePath = RunmobileStore.PathOf($"publication/{id}.evidence");

        var configured = Environment.GetEnvironmentVariable(ArbiterEnvironmentVariable);
        var besideMod = Path.Combine(
            Path.GetDirectoryName(typeof(PublicationGate).Assembly.Location)!, "sts2-arbiter.dll");
        var repositoryArbiter = Path.Combine(Environment.CurrentDirectory, "scripts", "arbiter");
        var arbiter = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : File.Exists(besideMod) ? besideMod : repositoryArbiter;
        if (!File.Exists(arbiter))
        {
            RunmobileStore.Remove(relativeManifestPath);
            throw new ShareValidationException(
                "The local replay arbiter is unavailable, so this run was not sent.");
        }

        return Task.Run(async () =>
        {
            try
            {
                var start = new ProcessStartInfo
                {
                    FileName = arbiter.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        ? "dotnet"
                        : arbiter,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                if (arbiter.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    start.ArgumentList.Add(arbiter);
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
                RunmobileStore.Remove(relativeManifestPath);
            }
        });
    }
}

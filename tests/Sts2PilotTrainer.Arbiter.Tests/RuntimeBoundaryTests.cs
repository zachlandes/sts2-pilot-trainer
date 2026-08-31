using Godot;

namespace Sts2PilotTrainer.Arbiter.Tests;

public class RuntimeBoundaryTests
{
    [Fact]
    public void RefusesAnOutOfWorktreeSandboxRoot()
    {
        var outside = Path.GetFullPath(Path.Combine(
            Arbiter.RepoRoot, "..", $"sandbox-escape-{Guid.NewGuid():N}"));

        Assert.ThrowsAny<InvalidOperationException>(() => HeadlessSandbox.SetRoot(outside));
        Assert.False(Directory.Exists(outside));
    }

    [GameFact]
    public void RefusesAnOutOfWorktreePreparedLibraryOverride()
    {
        var result = Arbiter.RunWithEnvironment(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["STS2_PILOT_TRAINER_LIB"] = Path.GetPathRoot(Arbiter.RepoRoot)!,
            },
            "preflight", Arbiter.Manifest);

        Assert.False(result.Verified);
        Assert.Contains("resolves outside the allowed root", result.All, StringComparison.Ordinal);
    }
}

using Sts2PilotTrainer.IO;

namespace Sts2PilotTrainer.Arbiter.Tests;

public sealed class GameBuildTests
{
    [GameFact]
    public void PreparedLibraryMatchesTheAdoptedGameBuild()
    {
        var expected = GameBuildRecord.Read(Path.Combine(Arbiter.RepoRoot, "scripts", "game-build.txt"));
        var actual = GameBuildRecord.Read(Path.Combine(Arbiter.RepoRoot, "build", "lib", "game-build.txt"));
        Assert.True(expected.Matches(actual),
            $"prepared library game-build.txt does not match scripts/game-build.txt: " +
            $"expected {expected.Version}/{expected.Commit}/{expected.PristineAssemblySha256}, " +
            $"actual {actual.Version}/{actual.Commit}/{actual.PristineAssemblySha256}");
    }
}

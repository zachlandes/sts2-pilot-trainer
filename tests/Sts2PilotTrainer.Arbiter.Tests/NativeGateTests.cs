using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Replay.Tests;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// Which conditions the publication gate applies to a recording this project's own
/// recorder made.
///
/// Four of the gate's conditions read a public video: the map a seed has to reproduce,
/// the mode its overlay implies, the mod whose branch has to be shown unreachable in
/// it, and the binding between those two reports. A recording made inside the player's
/// own game has no video for any of them, so they are <em>absent</em> for that kind
/// rather than reported as met - a condition reported as met is a claim somebody
/// checked something, and nothing checked those.
///
/// What is not absent is the engine standard. Every condition that replays the history
/// through the real engine applies to both kinds, which is what keeps "publishable"
/// meaning the same thing whoever made the recording.
///
/// The manifest is one <see cref="RunCapture"/> produced from the decisions of a short
/// run, which is the thing under test: what the gate asks a native recording only means
/// something if it is asked of what a recorder actually writes. Nothing about this
/// machine is expected to match it, and what is under test is which questions the gate
/// asks rather than what it answers - whether a recording can answer all of them is
/// <c>RecordedRunControlsTests</c>'s question, which needs no game and so runs
/// everywhere.
/// </summary>
public sealed class NativeGateTests
{
    private static readonly string[] VideoOnlyConditions =
        ["game-mode", "seed-topology", "baselib-path", "evidence-binding"];

    private static readonly string[] EngineConditions =
        ["reproduction", "covered-fight", "combat-boundary", "determinism", "rejection"];

    [GameFact]
    public void ANativeRecordingIsNeverAskedTheFourQuestionsThatReadAVideo()
    {
        var conditions = GateConditions(Native());

        Assert.All(VideoOnlyConditions, condition => Assert.DoesNotContain(condition, conditions));
        Assert.All(EngineConditions, condition => Assert.Contains(condition, conditions));
        Assert.Contains("publication-source", conditions);
        Assert.Contains("provenance", conditions);
        Assert.Contains("environment", conditions);
    }

    /// <summary>
    /// And a video recording is still asked all of them, so the arm above is a branch
    /// rather than a removal.
    /// </summary>
    [GameFact]
    public void AVideoRecordingIsStillAskedEveryOneOfThem()
    {
        var conditions = GateConditions(ManifestJson.Load(
            Path.Combine(Arbiter.RepoRoot, "manifests", "navegreed-OJ-6QXhNgdg.replay.json")));

        Assert.All(VideoOnlyConditions, condition => Assert.Contains(condition, conditions));
        Assert.All(EngineConditions, condition => Assert.Contains(condition, conditions));
    }

    /// <summary>The condition names the gate printed, in the order it printed them.</summary>
    private static IReadOnlyList<string> GateConditions(ReplayManifest manifest)
    {
        // Inside the repository, because an evidence artifact refuses to be written
        // anywhere else - which is the sandbox rule, not an accident of this test.
        var directory = Path.Combine(
            Arbiter.RepoRoot, "build", "test-scratch", $"native-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "fixture.replay.json");
            ManifestJson.Save(manifest, path);

            var result = Arbiter.Run("gate", path, "--out", Path.Combine(directory, "evidence"));

            return [.. result.Output
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("pass ", StringComparison.Ordinal) ||
                               line.StartsWith("FAIL ", StringComparison.Ordinal))
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1])];
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>A recording, built the way the recorder builds one and written only
    /// into the scratch directory the gate is pointed at.</summary>
    private static ReplayManifest Native() => RecordedRun.Manifest();
}

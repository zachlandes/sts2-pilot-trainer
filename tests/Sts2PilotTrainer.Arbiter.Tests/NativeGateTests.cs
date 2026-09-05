using Sts2PilotTrainer.Replay;

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
/// The manifest here is a fixture rather than a recording: it says captured because
/// the gate dispatches on that, and nothing about this machine is expected to match
/// it. What is under test is which questions the gate asks, not what it answers.
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

    /// <summary>
    /// The shipped recording, said the way a recorder would have said it.
    ///
    /// A conversion rather than a recording, and it never leaves the scratch directory
    /// it is written into: what the gate does with a native source is decided by the
    /// kind, and the kind is all this needs to carry.
    /// </summary>
    private static ReplayManifest Native()
    {
        var vod = ManifestJson.Load(
            Path.Combine(Arbiter.RepoRoot, "manifests", "navegreed-OJ-6QXhNgdg.replay.json"));
        var atStart = FactEvidence.AtActionOrdinal(-1);

        return vod with
        {
            RunId = "native-gate-fixture",
            Environment = new EnvironmentIdentity
            {
                BuildVersion = Fact<string>.Captured(vod.Environment.BuildVersion.Value, atStart),
                BuildDateUtc = Fact<string>.Captured(vod.Environment.BuildDateUtc.Value, atStart),
                GameMode = Fact<string>.Captured(vod.Environment.GameMode.Value, atStart),
                Seed = Fact<string>.Captured(vod.Environment.Seed.Value, atStart),
                ContentHash = Fact<string>.Captured(vod.Environment.ContentHash.Value, atStart),
                Ascension = Fact<int>.Captured(vod.Environment.Ascension.Value, atStart),
                Character = Fact<string>.Captured(vod.Environment.Character.Value, atStart),
                Acts = Fact<IReadOnlyList<string>>.Captured(vod.Environment.Acts.Value, atStart),
                Unlocks = Fact<UnlockRequirement>.Captured(
                    UnlockRequirement.Exact(
                        "a fixture, so this names the state nobody read",
                        new UnlockStateInventory { Epochs = [], EncountersSeen = [], Runs = 0 }),
                    atStart),
                Mods = Fact<ModEnvironment>.Captured(ModEnvironment.AsRecorded([]), atStart),
            },
            Source = new SourceProvenance
            {
                Kind = "native",
                ExtractionMethod = "captured",
                Coverage = vod.Source.Coverage,
                Native = new NativeSource
                {
                    RecorderVersion = "runmobile-recorder/fixture",
                    WitnessedRunStart = Fact<bool>.Captured(true, atStart),
                    Continuity = NativeSource.ContinuousContinuity,
                    Outcome = "abandoned",
                },
            },
            Actions = [.. vod.Actions.Select(action => action with
            {
                Source = FactSource.Captured,
                Evidence = FactEvidence.AtActionOrdinal(action.Seq),
            })],
            Checkpoints = [.. vod.Checkpoints.Select(checkpoint => checkpoint with
            {
                Expect = checkpoint.Expect.ToDictionary(
                    entry => entry.Key,
                    entry => Fact<string>.Captured(
                        entry.Value.Value, FactEvidence.AtActionOrdinal(checkpoint.AfterSeq)),
                    StringComparer.Ordinal),
            })],
        };
    }
}

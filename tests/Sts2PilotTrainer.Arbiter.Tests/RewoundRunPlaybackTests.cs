using System.Globalization;
using System.Text.Json;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// A run a reload rewound behind what was recorded is the player's to play from and
/// never theirs to share, held at the engine's own entry and the gate's own verdict.
///
/// The recording is the committed native one with what the recorder writes for a
/// reload put on it: the answer the reload abandoned kept as a discarded branch
/// marked as the reload's, from the opening reading, and <c>continuity = rewound</c>.
/// <c>RunCaptureTests</c> holds that the recorder writes exactly that; this holds
/// that what it writes is a run the library offers, <c>enter-fight</c> enters and
/// <c>gate</c> refuses - because the first build of this went green with a
/// recording the browser offered and the entry aborted on.
/// </summary>
public sealed class RewoundRunPlaybackTests
{
    private const string Recording = "native-3LACFJ5NJ371-20260906-015901.replay.json";

    [Fact]
    public void ARewoundRecordingValidatesIsOfferedAndIsSealedUnshareable()
    {
        var rewound = Rewound(Committed());

        var validation = ManifestValidator.Validate(rewound);
        Assert.True(validation.IsValid, validation.Describe());
        Assert.Contains(RunView.PositionsIn(rewound), position => position.Playable && position.Fight == 1);
        Assert.Contains("Not shareable", ShareRunForm.For(rewound).IntegritySeal, StringComparison.Ordinal);
    }

    [GameFact]
    public void ARewoundRecordingIsEnteredAtItsFirstFightAndRefusedByTheGate()
    {
        var directory = Path.Combine(
            Arbiter.RepoRoot, "build", "test-scratch", $"rewound-run-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var rewound = Rewound(Committed());
            var path = Path.Combine(directory, "rewound.replay.json");
            ManifestJson.Save(rewound, path);

            var outDir = Path.Combine(directory, "enter");
            var entered = Arbiter.Run("enter-fight", path, "--fight", "1", "--out", outDir);
            Assert.True(entered.Verified, entered.All);
            var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "enter-fight.json"))).RootElement;
            Assert.Equal("replayed", report.GetProperty("entry_route").GetString());
            Assert.Equal(rewound.CombatStartDigest(1), report.GetProperty("this_game_digest").GetString());

            var gate = Arbiter.Run("gate", path, "--out", Path.Combine(directory, "evidence"));
            Assert.NotEqual(0, gate.ExitCode);
            Assert.Contains("pass  provenance", gate.Output, StringComparison.Ordinal);
            Assert.Contains("FAIL  continuity", gate.Output, StringComparison.Ordinal);
            Assert.Contains("NOT PUBLISHABLE", gate.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ReplayManifest Committed() =>
        ManifestJson.Load(Path.Combine(Arbiter.RepoRoot, "manifests", Recording));

    /// <summary>
    /// The committed run with the Neow blessing answered once before the reload: that
    /// first answer is the branch, from the opening reading, and the run as committed
    /// is what was played after it.
    /// </summary>
    private static ReplayManifest Rewound(ReplayManifest committed)
    {
        var abandoned = committed.Actions[0] with
        {
            Args = new Dictionary<string, string>(StringComparer.Ordinal) { ["option_index"] = "1" },
        };
        var opening = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["combat.outcome"] = "none",
            ["run.total_floor"] = "1",
        };
        var branch = new DiscardedBranch
        {
            Reload = true,
            RollbackToSeq = -1,
            RollbackToDigest = "sha256:" + new string('0', 64),
            Actions = [abandoned],
            Trace = new ReplayTrace
            {
                Steps =
                [
                    new ReplayStep
                    {
                        Seq = -1,
                        Verb = RunCapture.RunStartVerb,
                        Args = new Dictionary<string, string>(StringComparer.Ordinal),
                        Before = opening,
                        After = opening,
                    },
                    new ReplayStep
                    {
                        Seq = abandoned.Seq,
                        Verb = abandoned.Verb.ToString(),
                        Args = abandoned.Args,
                        Before = opening,
                        After = opening,
                    },
                ],
            },
        };
        return committed with
        {
            Source = committed.Source with
            {
                Native = committed.Source.Native! with
                {
                    Continuity = NativeSource.RewoundContinuity,
                    Discarded = [branch],
                },
            },
        };
    }
}

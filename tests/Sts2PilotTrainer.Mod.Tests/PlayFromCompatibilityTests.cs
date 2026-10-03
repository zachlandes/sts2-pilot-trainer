using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

public sealed class PlayFromCompatibilityTests
{
    private static readonly List<string> Reasons = [];
    private static readonly List<string?> Titles = [];
    private static int _returns;
    private static int _snapshots;
    private static int _launches;

    public PlayFromCompatibilityTests() => EngineHost.Start();

    [GameTheory]
    [InlineData(1, "build_version")]
    [InlineData(2, "build_version")]
    [InlineData(1, "build_date_utc")]
    [InlineData(2, "build_date_utc")]
    [InlineData(1, "content_hash")]
    [InlineData(2, "content_hash")]
    [InlineData(1, "matching")]
    public async Task ABuildMismatchIsExplainedAtTheClickWithoutChangingScenes(int fight, string field)
    {
        var recording = ManifestJson.Deserialize(File.ReadAllText(Path.Combine(
            Arbiter.RepoRoot, "manifests", "native-3LACFJ5NJ371-20260906-015901.replay.json")));
        var actual = GameIdentity.ReadForCurrentEngine().Build;
        Assert.All(EnvironmentPreflight.Build(recording.Environment, actual), item => Assert.True(item.Matches));
        recording = recording with
        {
            Environment = field switch
            {
                "build_version" => recording.Environment with
                {
                    BuildVersion = recording.Environment.BuildVersion with { Value = "v0.110.0" },
                },
                "build_date_utc" => recording.Environment with
                {
                    BuildDateUtc = recording.Environment.BuildDateUtc with { Value = "2000.01.01" },
                },
                "content_hash" => recording.Environment with
                {
                    ContentHash = recording.Environment.ContentHash with { Value = "1" },
                },
                _ => recording.Environment,
            },
        };
        Assert.True(ManifestValidator.Validate(recording).IsValid);
        var plan = RecordedFightPlan.For(recording, fight);
        var route = RetailPlayback.RouteTo(recording, plan);
        if (fight == 1) Assert.IsType<PlaybackRoute.Walk>(route);
        else Assert.IsType<PlaybackRoute.Restore>(route);
        var credit = RecordingIdentity.Credit(recording, isPlayersOwn: true);
        var harmony = new Harmony($"play-from-compatibility.{Guid.NewGuid():N}");
        var instance = typeof(NGame).GetProperty(nameof(NGame.Instance))!;
        var previous = NGame.Instance;
        Reasons.Clear();
        Titles.Clear();
        _returns = 0;
        _snapshots = 0;
        _launches = 0;
        try
        {
            instance.SetValue(null, new NGame());
            harmony.Patch(
                typeof(NGame).GetMethod(nameof(NGame.ReturnToMainMenu))!,
                prefix: new HarmonyMethod(typeof(PlayFromCompatibilityTests), nameof(ReturnToMenu)));
            harmony.Patch(
                typeof(PrefightScreen).GetMethod("ShowRefusal", BindingFlags.Static | BindingFlags.NonPublic,
                    [typeof(RecordingCredit), typeof(string), typeof(string), typeof(string)])!,
                prefix: new HarmonyMethod(typeof(PlayFromCompatibilityTests), nameof(Explain)));

            harmony.Patch(
                typeof(SnapshotStore).GetMethod("EnsureAsync", BindingFlags.Static | BindingFlags.NonPublic)!,
                prefix: new HarmonyMethod(typeof(PlayFromCompatibilityTests), nameof(RefuseSnapshot)));

            harmony.Patch(
                typeof(RecordedFightRun).GetMethod("ConstructAndLaunch", BindingFlags.Static | BindingFlags.NonPublic)!,
                prefix: new HarmonyMethod(typeof(PlayFromCompatibilityTests), nameof(RefuseLaunch)));

            await RecordedFightRun.Start(recording, plan, credit);

            if (field == "matching")
            {
                Assert.Equal(1, _launches);
                Assert.Equal(1, _returns);
                Assert.Empty(Reasons);
                return;
            }

            Assert.Equal(0, _launches);
            Assert.Equal(0, _returns);
            Assert.Equal(0, _snapshots);
            Assert.Equal(
                field == "build_version"
                    ? LibraryCopy.LookupRefusedBuild("v0.110.0", actual.BuildVersion)
                    : LibraryCopy.LookupRefusedNoLongerMatches,
                Assert.Single(Reasons));
            Assert.Equal(LibraryCopy.LookupRefusedTitle, Assert.Single(Titles));
            Assert.True(RecordedFightRun.Idle);
            Assert.False(ProfileWriteBarrier.IsActive);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            instance.SetValue(null, previous);
            RecordedFightRun.Finish();
            Reasons.Clear();
            Titles.Clear();
        }
    }

    // A failed return after its fade must not hide a refusal made before a run exists
    private static bool ReturnToMenu(ref Task __result)
    {
        _returns++;
        __result = Task.FromException(new InvalidOperationException("The menu failed after fading out."));
        return false;
    }

    private static void RefuseLaunch()
    {
        _launches++;
        throw new InvalidOperationException("The build passed; stop before constructing a run in this test.");
    }

    private static void RefuseSnapshot()
    {
        _snapshots++;
        throw new InvalidOperationException("An incompatible run must never materialise a snapshot.");
    }

    private static bool Explain(string reason, string? title)
    {
        Reasons.Add(reason);
        Titles.Add(title);
        return false;
    }
}

using System.Globalization;
using System.Text.Json;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What version a recording this build writes says its recorder was.
///
/// It said the wrong one. Nothing set a version, so Runmobile.dll carried .NET's
/// default 1.0.0.0 and the recorder derived its string from that - while the mod set
/// in the same recording, read from the same DLL's manifest by the game itself, said
/// 0.1.0. The version string is the patch gate a reader uses to decide whether a
/// recording is browsable and reproducible, so a recording naming a build that was
/// never released is evidence nobody can act on.
///
/// These need no game: the version is decided at build time and read from the
/// assembly, and the capture that carries it is pure.
///
/// This half also covers the stamped version of the three assemblies only a project
/// that references the mod can load - Runmobile, Engine and Trainer. Replay, IO, the
/// Bootstrap tool and the test assembly are asked the same question by
/// VersionAgreementTests in Sts2PilotTrainer.Arbiter.Tests, which is in the game-free
/// solution filter and cannot reference the mod project. GodotStubs is deliberately
/// out of both: that assembly keeps GodotSharp's identity.
/// </summary>
public sealed class RecorderVersionTests
{
    [Fact]
    public void TheRecorderWritesTheVersionTheInstalledModDeclares()
    {
        Assert.Equal($"runmobile-recorder/{DeclaredByTheMod}", RunRecorder.RecorderVersion);
    }

    [Fact]
    public void EveryAssemblyOnlyTheModCanLoadIsStampedWithThatSameVersion()
    {
        var ours = new[]
        {
            typeof(RunRecorder).Assembly,
            typeof(Sts2PilotTrainer.Engine.BaseLibReachabilityProbe).Assembly,
            typeof(Sts2PilotTrainer.Trainer.TrainerCopy).Assembly,
        };

        Assert.All(ours, assembly => Assert.Equal(DeclaredByTheMod, RunmobileVersion.Of(assembly)));
    }

    [Fact]
    public void AFreshRecordingNamesTheVersionTheInstalledModDeclares()
    {
        // source.native.recorder_version on a manifest this build's capture produces.
        // The version string is the patch gate a reader uses, so it has to be the one
        // the mod the game loaded says it is.
        var capture = RunCapture.Begin(new RunRecordingStart
        {
            RunId = "native-SFXT47K77RFK-20260906-120000",
            RecorderVersion = RunRecorder.RecorderVersion,
            Identity = Identity(),
            State = FirstFloor,
            Digest = "digest-before-any-decision",
            RunClockMs = 0,
        });
        capture.Record(ActionVerb.ChooseNeowBlessing, Args, FirstFloor, "digest-0");
        capture.Finish("abandoned");

        var manifest = capture.ToManifest();

        Assert.Equal($"runmobile-recorder/{DeclaredByTheMod}", manifest.Source.Native!.RecorderVersion);
    }

    /// <summary>The version out of the manifest the game reads to load this mod.</summary>
    private static string DeclaredByTheMod =>
        JsonDocument
            .Parse(File.ReadAllText(
                Path.Combine(Arbiter.RepoRoot, "src", "Sts2PilotTrainer.Mod", "Runmobile.json")))
            .RootElement.GetProperty("version").GetString()!;

    /// <summary>The mod set as the game would report it, taken from that same manifest.</summary>
    private static ModEnvironment InstalledMods => ModEnvironment.AsRecorded(
        [new LocalMod(
            RunmobileMod.ModId, RunmobileMod.ModId, DeclaredByTheMod, AffectsGameplay: false, "Loaded")]);

    private static readonly IReadOnlyDictionary<string, string> Args =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["option_index"] = "0" };

    private static readonly IReadOnlyDictionary<string, string> FirstFloor =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["combat.in_progress"] = "false",
            ["combat.outcome"] = "none",
            ["run.total_floor"] = 1.ToString(CultureInfo.InvariantCulture),
            ["run.act_floor"] = 1.ToString(CultureInfo.InvariantCulture),
            ["player.hp"] = "68",
            ["player.max_hp"] = "68",
        };

    private static RunIdentityReading Identity() => new()
    {
        BuildVersion = "v0.111.0",
        BuildDateUtc = "2026.08.14",
        ContentHash = "1568834832",
        GameMode = "standard",
        Seed = "SFXT47K77RFK",
        Ascension = 10,
        Character = "CHARACTER.IRONCLAD",
        Acts = ["ACT.UNDERDOCKS"],
        Unlocks = new UnlockStateInventory
        {
            Epochs = ["EPOCH.ONE"],
            EncountersSeen = ["ENCOUNTER.TEST"],
            Runs = 137,
        },
        Mods = InstalledMods,
    };
}

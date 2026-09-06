using System.Globalization;
using System.Text.Json;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Replay.Tests;

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
/// This half also runs the same sweep VersionAgreementTests does, over the assemblies
/// beside this test binary rather than a list - which is where Runmobile and Engine
/// appear, because they need the game assembly and so cannot be asked from the
/// game-free suite CI runs. Neither sweep names an assembly, so a project added
/// tomorrow is asked as soon as anything here references it; docs/distribution.md owns
/// the two things deliberately outside the arrangement, GodotStubs and Arbiter.Version.
/// </summary>
public sealed class RecorderVersionTests
{
    [Fact]
    public void TheRecorderWritesTheVersionTheInstalledModDeclares()
    {
        Assert.Equal($"runmobile-recorder/{DeclaredByTheMod}", RunRecorder.RecorderVersion);
    }

    [Fact]
    public void EveryAssemblyThatNeedsTheGameIsStampedWithThatSameVersion()
    {
        var ours = OurAssembliesBesideThisOne.All();

        Assert.Contains(typeof(RunRecorder).Assembly.GetName().Name, ours.Select(a => a.GetName().Name));
        Assert.All(ours, assembly => Assert.Equal(DeclaredByTheMod, RunmobileVersion.Of(assembly)));
    }

    [Fact]
    public void AFreshRecordingNamesTheVersionTheInstalledModDeclares()
    {
        // That RunCapture carries the recorder's own string all the way through to the
        // emitted source.native.recorder_version, on top of the property assertion
        // above: what a reader acts on is the field in the file, not the property.
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
            RunmobileMod.ModId, RunmobileMod.ModId, DeclaredByTheMod, AffectsGameplay: false, "Loaded")],
        HarmonyRoster.Read());

    private static readonly IReadOnlyDictionary<string, string> Args =
        new Dictionary<string, string>(StringComparer.Ordinal) { ["option_index"] = "0", ["option_key"] = "NEOW.BLESSING" };

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

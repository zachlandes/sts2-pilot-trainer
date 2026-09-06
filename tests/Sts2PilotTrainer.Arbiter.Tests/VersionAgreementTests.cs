using System.Text.Json;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The version this build says it is, asked at every place it is written down.
///
/// Three of them disagreed. The mod manifest declared 0.1.0, no project declared a
/// version at all so every assembly carried .NET's default 1.0.0.0, and the recorder
/// derived its string from the assembly - so a native recording said its recorder was
/// runmobile-recorder/1.0.0.0 while the mod set in the same file said Runmobile 0.1.0.
/// Both were describing the same DLL.
///
/// This is the sweep that fails when a future project declares a version of its own:
/// a stated invariant with no test that can fail is not an invariant. It asks every
/// assembly that runs in CI - Replay, IO, Trainer, the Bootstrap tool and this test
/// assembly. Runmobile and Engine are asked the same question by RecorderVersionTests
/// in Sts2PilotTrainer.Mod.Tests, because they need the game assembly and this project
/// is in the game-free solution filter. Between the two, everything shipped is asked
/// except GodotStubs, which keeps GodotSharp's identity so the game assembly's
/// references resolve. Nothing else is: Sts2PilotTrainer.Cli may not be referenced
/// from the solution at all, and the remaining test assemblies are not shipped.
///
/// These need no game: they read the manifest this repository ships and the version
/// stamped into the assemblies loaded to run them.
/// </summary>
public class VersionAgreementTests
{
    [Fact]
    public void EveryAssemblyWeShipIsStampedWithTheVersionTheModManifestDeclares()
    {
        var ours = new[]
        {
            typeof(RunmobileVersion).Assembly,
            typeof(Sts2PilotTrainer.IO.AtomicFile).Assembly,
            typeof(Sts2PilotTrainer.Trainer.TrainerCopy).Assembly,
            typeof(Sts2PilotTrainer.Bootstrap.Program).Assembly,
            typeof(VersionAgreementTests).Assembly,
        };

        Assert.All(ours, assembly => Assert.Equal(Declared, RunmobileVersion.Of(assembly)));
    }

    [Fact]
    public void TheRecorderNamesItselfWithThatSameVersion()
    {
        // The string every native recording carries as source.native.recorder_version.
        Assert.Equal($"runmobile-recorder/{Declared}", RunmobileVersion.Recorder);
    }

    /// <summary>The one field a human edits, and what everything above must agree with.</summary>
    private static string Declared =>
        JsonDocument
            .Parse(File.ReadAllText(
                Path.Combine(Arbiter.RepoRoot, "src", "Sts2PilotTrainer.Mod", "Runmobile.json")))
            .RootElement.GetProperty("version").GetString()!;
}

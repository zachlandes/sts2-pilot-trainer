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
/// This half covers the assemblies a game-free suite can load: Replay, IO, the
/// Bootstrap tool and this test assembly. Runmobile, Engine and Trainer are asked the
/// same question by RecorderVersionTests in Sts2PilotTrainer.Mod.Tests, because this
/// project is in the game-free solution filter and cannot reference the mod. Between
/// the two, a version declared anywhere but the mod manifest shows up here as a
/// stamped-attribute mismatch. GodotStubs is deliberately out of both: that assembly
/// keeps GodotSharp's identity so the game assembly's references resolve.
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

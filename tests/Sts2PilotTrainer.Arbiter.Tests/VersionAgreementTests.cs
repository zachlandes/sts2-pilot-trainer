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
/// This is the sweep that fails when a project declares a version of its own: a stated
/// invariant with no test that can fail is not an invariant. It names no assembly - it
/// asks every one of ours sitting beside the test binary, so a project added tomorrow
/// is asked the moment anything here references it, without an array to remember to
/// edit. Here that is the game-free set, which is what CI runs; RecorderVersionTests in
/// Sts2PilotTrainer.Mod.Tests runs the same sweep over its own output, which is where
/// Runmobile and Engine appear, because they need the game assembly and this project is
/// in the game-free solution filter.
///
/// GodotStubs is out by construction: it builds as GodotSharp, keeping that identity so
/// the game assembly's references resolve. Sts2PilotTrainer.Cli is asked by neither,
/// because nothing in the solution may reference it. So is Arbiter.Version, which is a
/// deliberately independent version for a separate artifact rather than an assembly
/// stamp; docs/distribution.md owns both exceptions.
///
/// These need no game: they read the manifest this repository ships and the version
/// stamped into the assemblies loaded to run them.
/// </summary>
public class VersionAgreementTests
{
    [Fact]
    public void EveryAssemblyWeShipIsStampedWithTheVersionTheModManifestDeclares()
    {
        var ours = OurAssembliesBesideThisOne.All();

        Assert.Contains(typeof(RunmobileVersion).Assembly.GetName().Name, ours.Select(a => a.GetName().Name));
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

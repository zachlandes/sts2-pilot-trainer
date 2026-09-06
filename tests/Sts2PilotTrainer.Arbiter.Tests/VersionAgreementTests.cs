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
/// These need no game: they read the manifest this repository ships, the projects that
/// build it, and the version stamped into the assemblies loaded to run them.
/// </summary>
public class VersionAgreementTests
{
    [Fact]
    public void EveryAssemblyWeShipIsStampedWithTheVersionTheModManifestDeclares()
    {
        var ours = new[]
        {
            typeof(RunmobileVersion).Assembly,
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

    [Fact]
    public void NoProjectDeclaresAVersionOfItsOwn()
    {
        // Directory.Build.props reads the mod manifest and stamps every project from
        // it. A project setting its own would be a second source, and a second source
        // is what this whole file exists to keep from coming back. GodotStubs is the
        // one exception and is not ours: that assembly has to keep GodotSharp's
        // identity for the game assembly's references to resolve.
        var declaring = Directory
            .EnumerateFiles(Arbiter.RepoRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(project => !project.Contains($"{Path.DirectorySeparatorChar}build{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(project => File.ReadAllText(project).Contains("<Version>", StringComparison.Ordinal))
            .Select(project => Path.GetFileName(project))
            .Order(StringComparer.Ordinal);

        Assert.Equal(new[] { "GodotStubs.csproj" }, declaring);
    }

    /// <summary>The one field a human edits, and what everything above must agree with.</summary>
    private static string Declared =>
        JsonDocument
            .Parse(File.ReadAllText(
                Path.Combine(Arbiter.RepoRoot, "src", "Sts2PilotTrainer.Mod", "Runmobile.json")))
            .RootElement.GetProperty("version").GetString()!;
}

using System.Diagnostics;
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

    /// <summary>The declared field is one the release bar can order a recorder by:
    /// <c>RecordingStanding</c> reads it through <see cref="RecorderVersion"/>, and a
    /// declaration that could not be read there would abort both corpus numbers.</summary>
    [Fact]
    public void TheDeclaredVersionIsOneTheReleaseBarOrdersRecordersBy()
    {
        Assert.NotNull(RecorderVersion.TryParse(Declared));
    }

    /// <summary>
    /// The build refuses at the declaration exactly what <see cref="RecorderVersion"/>
    /// cannot read, naming the field, so a version the release bar cannot order is a
    /// build error rather than an abort at the first recording of a corpus; a
    /// prerelease is admitted, because the bar orders one below its release.
    /// </summary>
    [Theory]
    [InlineData("0.2.0")]
    [InlineData("0.3.0-rc1")]
    [InlineData("0.3.0-rc.1.beta-2")]
    [InlineData("0.3")]
    [InlineData("0.3.0.1")]
    [InlineData("1.0.0.0")]
    [InlineData("03.0.0")]
    [InlineData("0.3.0-01")]
    [InlineData("0.3.0-")]
    [InlineData("0.3.0+beta")]
    [InlineData("fixture")]
    public void TheBuildRefusesADeclarationTheReleaseBarCannotOrder(string declared)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Arbiter.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[]
                 {
                     "msbuild", Path.Combine(Arbiter.RepoRoot, "Directory.Build.props"),
                     "-t:RequireAReadableRunmobileVersion", $"-p:RunmobileVersion={declared}", "-nologo", "-v:q",
                 })
        {
            startInfo.ArgumentList.Add(arg);
        }
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (RecorderVersion.TryParse(declared) is not null)
        {
            Assert.True(process.ExitCode == 0, output);
            return;
        }
        Assert.NotEqual(0, process.ExitCode);
        Assert.Contains($"The \"version\" field of", output, StringComparison.Ordinal);
        Assert.Contains($"is '{declared}', which is not a version this build reads", output, StringComparison.Ordinal);
    }

    /// <summary>The one field a human edits, and what everything above must agree with.</summary>
    private static string Declared =>
        JsonDocument
            .Parse(File.ReadAllText(
                Path.Combine(Arbiter.RepoRoot, "src", "Sts2PilotTrainer.Mod", "Runmobile.json")))
            .RootElement.GetProperty("version").GetString()!;
}

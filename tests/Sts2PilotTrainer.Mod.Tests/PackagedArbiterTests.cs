using System.Diagnostics;
using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Mod.Tests;

/// <summary>
/// What the mod hands the packaged arbiter when it starts one.
///
/// The first retail proof of the restore route stood still in this process for as
/// long as it was left: the child had inherited a variable the running game sets in
/// its own process, and the game assembly's initializer acted on it. The environment
/// a child gets is this class's decision and nowhere else's, so it is pinned here.
/// </summary>
public sealed class PackagedArbiterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"runmobile-arbiter-{Guid.NewGuid():N}");
    private readonly string? _inherited = Environment.GetEnvironmentVariable("SENTRY_GODOT_LIB_PATH");

    public PackagedArbiterTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "arbiter", "lib"));
        File.WriteAllText(Path.Combine(_root, "arbiter", "sts2-arbiter"), string.Empty);
        File.WriteAllText(Path.Combine(_root, "arbiter", "lib", "prepared-assembly.json"), "{}");
        Directory.CreateDirectory(Path.Combine(_root, "workspace", "sandbox"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("SENTRY_GODOT_LIB_PATH", _inherited);
        Directory.Delete(_root, recursive: true);
    }

    private ProcessStartInfo Start(params string[] arguments) =>
        PackagedArbiter.StartInfoIn(
            Path.Combine(_root, "arbiter"),
            Path.Combine(_root, "workspace"),
            Path.Combine(_root, "workspace", "sandbox"),
            arguments);

    [Fact]
    public void TheChildDoesNotInheritWhatTheGameSetInItsOwnProcess()
    {
        Environment.SetEnvironmentVariable("SENTRY_GODOT_LIB_PATH", "/game/Frameworks/libsentry.dylib");

        var start = Start("floor-snapshot", "recording.replay.json", "--floor", "3");

        Assert.DoesNotContain(
            start.Environment.Keys,
            name => name.StartsWith(PackagedArbiter.GameProcessVariablePrefix, StringComparison.OrdinalIgnoreCase));
        Assert.True(start.Environment.ContainsKey("PATH") || start.Environment.ContainsKey("Path"));
    }

    [Fact]
    public void TheChildIsToldWhereItsRuntimeSandboxAndWorkspaceAre()
    {
        var start = Start("engine-commands");

        Assert.Equal(Path.Combine(_root, "arbiter", "sts2-arbiter"), start.FileName);
        Assert.Equal(Path.Combine(_root, "workspace"), start.WorkingDirectory);
        Assert.Equal(Path.Combine(_root, "arbiter", "lib"), start.Environment["STS2_PILOT_TRAINER_LIB"]);
        Assert.Equal(Path.Combine(_root, "workspace", "sandbox"), start.Environment["STS2_PILOT_TRAINER_SANDBOX"]);
        Assert.Equal(Path.Combine(_root, "workspace"), start.Environment["STS2_PILOT_TRAINER_WORKSPACE"]);
        Assert.Equal(["engine-commands"], start.ArgumentList);
        Assert.True(start.RedirectStandardOutput && start.RedirectStandardError && !start.UseShellExecute);
    }

    /// <summary>
    /// The mod's own calls name no directory and pass only strings, and a second
    /// entry point taking one more string once bound them in expanded form, with the
    /// workspace as the arbiter directory. So the installed-directory path is driven
    /// exactly as the mod drives it: an arbiter laid beside the mod assembly, found.
    ///
    /// It stands one there only where there is none. The installed directory is this
    /// assembly's own output directory, which is the layout a packaged build lays a
    /// real arbiter into, and a test that wrote an empty file over it would leave it
    /// truncated for everything that ran afterwards. Only what this test laid down is
    /// taken away again.
    /// </summary>
    [Fact]
    public void TheInstalledArbiterIsTheOneBesideTheModAssembly()
    {
        var installed = PackagedArbiter.InstalledDirectory();
        Assert.Equal(Path.Combine(Path.GetDirectoryName(typeof(PackagedArbiter).Assembly.Location)!, "arbiter"), installed);

        var executable = Path.Combine(installed, OperatingSystem.IsWindows() ? "sts2-arbiter.exe" : "sts2-arbiter");
        var prepared = Path.Combine(installed, "lib", "prepared-assembly.json");
        var createdTree = !Directory.Exists(installed);
        var laidExecutable = !File.Exists(executable);
        var laidPrepared = !File.Exists(prepared);
        Directory.CreateDirectory(Path.Combine(installed, "lib"));
        try
        {
            if (laidExecutable) File.WriteAllText(executable, string.Empty);
            if (laidPrepared) File.WriteAllText(prepared, "{}");

            var start = PackagedArbiter.StartInfo(
                Path.Combine(_root, "workspace"), Path.Combine(_root, "workspace", "sandbox"), "floor-snapshot", "r.json", "--floor", "3");

            Assert.Equal(executable, start.FileName);
            Assert.Equal(Path.Combine(_root, "workspace"), start.WorkingDirectory);
            Assert.Equal(Path.Combine(_root, "workspace", "sandbox"), start.Environment["STS2_PILOT_TRAINER_SANDBOX"]);
            Assert.Equal(["floor-snapshot", "r.json", "--floor", "3"], start.ArgumentList);
        }
        finally
        {
            if (createdTree)
            {
                Directory.Delete(installed, recursive: true);
            }
            else
            {
                if (laidExecutable) File.Delete(executable);
                if (laidPrepared) File.Delete(prepared);
            }
        }
    }

    [Fact]
    public void RefusesAPackageWithNoArbiterOrNoPreparedRuntime()
    {
        File.Delete(Path.Combine(_root, "arbiter", "lib", "prepared-assembly.json"));
        Assert.Contains("incomplete", Assert.Throws<InvalidOperationException>(() => Start("engine-commands")).Message);

        File.Delete(Path.Combine(_root, "arbiter", "sts2-arbiter"));
        Assert.Contains("unavailable", Assert.Throws<InvalidOperationException>(() => Start("engine-commands")).Message);
    }
}

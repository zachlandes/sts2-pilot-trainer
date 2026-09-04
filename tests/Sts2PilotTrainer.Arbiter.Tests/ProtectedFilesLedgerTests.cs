using System.Diagnostics;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The measurement that says a session changed nothing it must not.
///
/// It is driven the way a session drives it - the script, two roots, a ledger - over
/// directories this test owns rather than the player's real ones, because what is
/// being tested is what it reports, not what is in anybody's save folder. Nothing
/// here needs the game.
/// </summary>
public sealed class ProtectedFilesLedgerTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(
        Path.GetTempPath(), $"protected-files-{Guid.NewGuid():N}");

    private readonly string _userDir;
    private readonly string _modsDir;
    private readonly string _ledger;

    public ProtectedFilesLedgerTests()
    {
        _userDir = Path.Combine(_sandbox, "user");
        _modsDir = Path.Combine(_sandbox, "mods");
        _ledger = Path.Combine(_sandbox, "before.ledger");
        Directory.CreateDirectory(Path.Combine(_userDir, "Runmobile"));
        Directory.CreateDirectory(Path.Combine(_userDir, "default"));
        Directory.CreateDirectory(Path.Combine(_modsDir, "Runmobile"));
        Directory.CreateDirectory(Path.Combine(_modsDir, "SomebodyElse"));
        Directory.CreateDirectory(Path.Combine(_userDir, "logs"));
        Write("user/logs/godot.log", "launched");
        Write("user/default/progress.save", "progress");
        Write("user/default/run_history.save", "history");
        Write("user/Runmobile/progress.json", "{}");
        Write("mods/Runmobile/Runmobile.json", "{}");
        Write("mods/SomebodyElse/mod.json", "{}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_sandbox)) Directory.Delete(_sandbox, recursive: true);
    }

    [Fact]
    public void AnUnchangedSessionReportsNothingAndSucceeds()
    {
        Snapshot();

        var compared = Compare();

        Assert.Equal(0, compared.ExitCode);
        Assert.Contains("protected files (must not change):\n  nothing", compared.All, StringComparison.Ordinal);
        Assert.Contains("store):\n  nothing", compared.All, StringComparison.Ordinal);
        Assert.Contains("mod or not):\n  nothing", compared.All, StringComparison.Ordinal);
    }

    /// <summary>
    /// The game writes its own log on every launch, with or without this mod. It is
    /// reported by name in its own section rather than hidden, and it is not a
    /// failure: a measurement that went red on every session is one people stop
    /// reading.
    /// </summary>
    [Fact]
    public void TheGamesOwnChurnIsReportedSeparatelyAndDoesNotFail()
    {
        Snapshot();
        Write("user/logs/godot.log", "launched again");

        var compared = Compare();

        Assert.Equal(0, compared.ExitCode);
        Assert.Contains("mod or not):\n  changed  user/logs/godot.log", compared.All, StringComparison.Ordinal);
        Assert.Contains("protected files (must not change):\n  nothing", compared.All, StringComparison.Ordinal);
    }

    [Fact]
    public void AChangedProtectedFileIsReportedAndFails()
    {
        Snapshot();
        Write("user/default/progress.save", "changed");

        var compared = Compare();

        Assert.Equal(1, compared.ExitCode);
        Assert.Contains("changed  user/default/progress.save", compared.All, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAddedAndARemovedProtectedFileAreDifferentFindings()
    {
        Snapshot();
        Write("user/default/settings.save", "new");
        File.Delete(Path.Combine(_sandbox, "mods", "SomebodyElse", "mod.json"));

        var compared = Compare();

        Assert.Equal(1, compared.ExitCode);
        Assert.Contains("added    user/default/settings.save", compared.All, StringComparison.Ordinal);
        Assert.Contains("removed  mods/SomebodyElse/mod.json", compared.All, StringComparison.Ordinal);
    }

    /// <summary>
    /// The store is where this mod is supposed to write, so a change there is
    /// reported in its own section and is not a failure. That separation is the whole
    /// point of the ledger: without it, proving the mod wrote nothing would mean
    /// proving it did nothing.
    /// </summary>
    [Fact]
    public void TheStoresOwnSubtreeIsReportedSeparatelyAndDoesNotFail()
    {
        Snapshot();
        Write("user/Runmobile/progress.json", "{\"fights\":1}");
        Write("user/Runmobile/cache/entry.bin", "cached");

        var compared = Compare();

        Assert.Equal(0, compared.ExitCode);
        Assert.Contains("changed  user/Runmobile/progress.json", compared.All, StringComparison.Ordinal);
        Assert.Contains("added    user/Runmobile/cache/entry.bin", compared.All, StringComparison.Ordinal);
        Assert.Contains("protected files (must not change):\n  nothing", compared.All, StringComparison.Ordinal);
    }

    [Fact]
    public void ASnapshotCountsEveryFileUnderBothRoots()
    {
        var snapshot = Snapshot();

        Assert.Contains("files        : 6", snapshot.All, StringComparison.Ordinal);
    }

    /// <summary>
    /// A ledger written inside what it measures would change the thing it is a
    /// measurement of, and the second one would then find its own first.
    /// </summary>
    [Fact]
    public void ItRefusesToWriteTheLedgerInsideWhatItMeasures()
    {
        var inside = Run("snapshot", Path.Combine(_userDir, "ledger.txt"));

        Assert.Equal(2, inside.ExitCode);
        Assert.Contains("Refusing to write the ledger inside", inside.All, StringComparison.Ordinal);
    }

    [Fact]
    public void ComparingWithoutALedgerSaysHowToTakeOne()
    {
        var compared = Compare();

        Assert.Equal(2, compared.ExitCode);
        Assert.Contains("protected-files.sh snapshot", compared.All, StringComparison.Ordinal);
    }

    [Fact]
    public void ALedgerForDifferentRootsIsRefused()
    {
        Snapshot();
        var otherUser = Path.Combine(_sandbox, "other-user");
        Directory.CreateDirectory(otherUser);

        var compared = Run("compare", _ledger, otherUser, _modsDir);

        Assert.Equal(2, compared.ExitCode);
        Assert.Contains("different roots", compared.All, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("find", "enumerate every file")]
    [InlineData("shasum", "Could not hash")]
    public void AScanFailureLeavesNoLedger(string command, string diagnostic)
    {
        if (OperatingSystem.IsWindows()) return;

        var tools = Path.Combine(_sandbox, "tools");
        Directory.CreateDirectory(tools);
        var executable = Path.Combine(tools, command);
        File.WriteAllText(executable, "#!/usr/bin/env bash\nexit 23\n");
        File.SetUnixFileMode(executable,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var snapshot = Run("snapshot", _ledger, pathPrefix: tools);

        Assert.Equal(2, snapshot.ExitCode);
        Assert.Contains(diagnostic, snapshot.All, StringComparison.Ordinal);
        Assert.False(File.Exists(_ledger));
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_sandbox, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private Arbiter.Result Snapshot() => Run("snapshot", _ledger);

    private Arbiter.Result Compare() => Run("compare", _ledger);

    private Arbiter.Result Run(
        string command,
        string ledger,
        string? userDirectory = null,
        string? modsDirectory = null,
        string? pathPrefix = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            WorkingDirectory = Arbiter.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(Path.Combine(Arbiter.RepoRoot, "scripts", "protected-files.sh"));
        startInfo.ArgumentList.Add(command);
        startInfo.ArgumentList.Add(ledger);
        startInfo.ArgumentList.Add("--user-dir");
        startInfo.ArgumentList.Add(userDirectory ?? _userDir);
        startInfo.ArgumentList.Add("--mods-dir");
        startInfo.ArgumentList.Add(modsDirectory ?? _modsDir);
        if (pathPrefix is not null)
        {
            startInfo.Environment["PATH"] =
                pathPrefix + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new Arbiter.Result(process.ExitCode, output, error);
    }
}

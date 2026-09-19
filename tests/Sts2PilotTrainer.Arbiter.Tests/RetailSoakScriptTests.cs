using System.Diagnostics;
using System.Text.Json;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// One night of the retail soak, run by <c>scripts/retail-soak.sh</c> against the
/// stand-in retail executable and a stand-in process table, so the whole lifecycle -
/// the plan written whole, the headless launch through the retail helper, the wait
/// for <c>soak-done</c>, the release, the copy into dated evidence, the two numbers
/// over that copy beside the committed corpus, the figure and the exit code - is
/// held without a game window and, for everything short of the numbers, without the
/// game at all.
///
/// The stand-in plays the soak's client: under <c>--headless</c> it writes
/// <c>soak-done</c> the way <c>RetailSoak</c> writes it and quits the way
/// <c>NGame.Quit</c> does; without the flag it never writes it, which is what makes
/// the flag load-bearing here. The recordings the night "made" are a journalled
/// recording the way <see cref="ParityTests"/> builds one, placed in the sandbox
/// store ahead of the launch, because a stand-in plays no run.
/// </summary>
public sealed class RetailSoakScriptTests : IDisposable
{
    private const string SoakRun = "native-9F8CY60C5BK7-20260919-010101";

    private readonly string _sandbox = Path.Combine(
        Arbiter.RepoRoot, "build", "test-scratch", $"retail-soak-{Guid.NewGuid():N}");

    private readonly string _home;
    private readonly string _tools;
    private readonly string _game;
    private readonly string _gameLog;
    private readonly string _gamePids;
    private readonly string _out;
    private readonly string _store;
    private readonly Dictionary<string, string> _environment = new(StringComparer.Ordinal);

    public RetailSoakScriptTests()
    {
        _home = Path.Combine(_sandbox, "home");
        _tools = Path.Combine(_sandbox, "tools");
        _game = Path.Combine(_sandbox, "install", "SlayTheSpire2.app", "Contents", "MacOS", "Slay the Spire 2");
        _gameLog = Path.Combine(_sandbox, "game.log");
        _gamePids = Path.Combine(_sandbox, "game.pids");
        _out = Path.Combine(_sandbox, "evidence");
        var user = Path.Combine(_home, "Library", "Application Support", "SlayTheSpire2");
        _store = Path.Combine(user, "Runmobile", "default", "2", "modded", "profile1");
        if (OperatingSystem.IsWindows()) return;

        Directory.CreateDirectory(_tools);
        Directory.CreateDirectory(Path.Combine(user, "default", "2", "modded"));
        File.WriteAllText(
            Path.Combine(user, "default", "2", "modded", "profile.save"),
            "{\n  \"last_profile_id\": 1,\n  \"schema_version\": 2\n}\n");
        Directory.CreateDirectory(Path.Combine(_store, "recordings"));
        WriteExecutable(_game, File.ReadAllText(RetailClientLaunchTests.StandInSource));
        WriteExecutable(Path.Combine(_tools, "ps"),
            "#!/usr/bin/env bash\n" +
            "if [[ -n \"${FAKE_GAME_PIDS:-}\" && -f \"$FAKE_GAME_PIDS\" ]]; then\n" +
            "  while read -r pid; do\n" +
            "    if kill -0 \"$pid\" 2>/dev/null; then printf '%5s %5s %s\\n' \"$pid\" 1 \"$FAKE_GAME_EXECUTABLE\"; fi\n" +
            "  done < \"$FAKE_GAME_PIDS\"\n" +
            "fi\n");
        _environment["FAKE_SOAK_DONE"] = Path.Combine(_store, "soak-done");
        _environment["FAKE_SOAK_DONE_AFTER"] = "2";
    }

    public void Dispose()
    {
        if (File.Exists(_gamePids))
        {
            foreach (var line in File.ReadAllLines(_gamePids))
            {
                if (!int.TryParse(line, out var pid)) continue;
                try { Process.GetProcessById(pid).Kill(); } catch (ArgumentException) { } catch (InvalidOperationException) { }
            }
        }

        if (Directory.Exists(_sandbox)) Directory.Delete(_sandbox, recursive: true);
    }

    /// <summary>
    /// The night as planned: the plan written whole, the client launched headless
    /// through the helper, <c>soak-done</c> awaited, the client released, the store's
    /// recordings copied and never moved, parity and coverage run over the copy beside
    /// <c>manifests/</c>, the figure printed, exit 0.
    /// </summary>
    [GameFact]
    public void ANightRunsTheWholeLifecycleAndPrintsTheFigure()
    {
        if (OperatingSystem.IsWindows()) return;
        var (manifest, journal) = ARecordingInTheStore();

        var night = Run("--runs", "1", "--seeds", "ABC123, DEF456", "--stop-after-minutes", "1", "--ascension", "0");

        Assert.Equal(0, night.ExitCode);
        var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(_store, "settings.json"))).RootElement;
        Assert.Equal("sts2-pilot-trainer/runmobile-settings/v1", settings.GetProperty("schema").GetString());
        Assert.True(settings.GetProperty("record_my_runs").GetBoolean());
        Assert.Equal(100000, settings.GetProperty("keep_recent_runs").GetInt32());
        var plan = settings.GetProperty("retail_soak");
        Assert.Equal(1, plan.GetProperty("runs").GetInt32());
        Assert.Equal(["ABC123", "DEF456"], plan.GetProperty("seeds").EnumerateArray().Select(seed => seed.GetString()));
        Assert.Equal("CHARACTER.IRONCLAD", plan.GetProperty("character").GetString());
        Assert.Equal(0, plan.GetProperty("ascension").GetInt32());
        Assert.Equal(1, plan.GetProperty("stop_after_minutes").GetInt32());

        Assert.Contains("args\t--force-steam=off --clientId=2 --headless", File.ReadAllText(_gameLog), StringComparison.Ordinal);
        Assert.Contains("soak-done arrived", night.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_out, "soak-done.json")));
        Assert.True(File.Exists(Path.Combine(_store, "soak-done")), "the store's soak-done was removed");
        Assert.True(File.Exists(manifest) && File.Exists(journal), "a recording was removed from the store");
        Assert.True(File.Exists(Path.Combine(_out, "recordings", Path.GetFileName(manifest))));
        Assert.True(File.Exists(Path.Combine(_out, "recordings", Path.GetFileName(journal))));

        var parity = JsonDocument.Parse(File.ReadAllText(Path.Combine(_out, "parity.json"))).RootElement;
        Assert.True(parity.GetProperty("at_parity").GetBoolean());
        Assert.Equal(1, parity.GetProperty("summary").GetProperty("at_parity").GetInt32());
        Assert.Equal(3, parity.GetProperty("summary").GetProperty("native_recordings").GetInt32());
        Assert.True(File.Exists(Path.Combine(_out, "coverage.json")));
        Assert.Contains("the third figure: parity holds over the night's copy beside manifests/; coverage holds; 0 run(s) to read", night.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("record state : live", Run("status").Output, StringComparison.Ordinal);
    }

    /// <summary>A recording the replay does not reproduce fails the figure and the
    /// script, which is the whole point of the night.</summary>
    [GameFact]
    public void AParityFailureExitsOne()
    {
        if (OperatingSystem.IsWindows()) return;
        var (_, journal) = ARecordingInTheStore();
        ParityTests.Rewrite(journal, seq: 5, entry => entry["before"]!["player.hp"] = "1");

        var night = Run("--runs", "1", "--stop-after-minutes", "1");

        Assert.Equal(1, night.ExitCode);
        Assert.Contains("parity does not hold", night.Output, StringComparison.Ordinal);
        Assert.Contains("NOT AT PARITY", night.Output, StringComparison.Ordinal);
    }

    /// <summary>A run the mod gave up in a state it did not know is named from
    /// <c>soak-done</c> and is a finding for the morning, with its own exit code.</summary>
    [GameFact]
    public void ARunTheModCouldNotFinishExitsFour()
    {
        if (OperatingSystem.IsWindows()) return;
        ARecordingInTheStore();
        _environment["FAKE_SOAK_OUTCOME"] = "unknown-state";

        var night = Run("--runs", "1", "--stop-after-minutes", "1");

        Assert.Equal(4, night.ExitCode);
        Assert.Contains("a run ended 'unknown-state'; read godot.log", night.Output, StringComparison.Ordinal);
        Assert.Contains("parity holds", night.Output, StringComparison.Ordinal);
        Assert.Contains("1 run(s) to read", night.Output, StringComparison.Ordinal);
    }

    /// <summary>A client that went away without writing <c>soak-done</c> measured
    /// nothing, and the script says so rather than reading an older night's file.</summary>
    [Fact]
    public void AClientThatQuitWithoutSoakDoneExitsThree()
    {
        if (OperatingSystem.IsWindows()) return;
        File.WriteAllText(Path.Combine(_store, "soak-done"), "{ \"schema\": \"an earlier night\" }\n");
        File.SetLastWriteTimeUtc(Path.Combine(_store, "soak-done"), DateTime.UtcNow.AddDays(-1));
        _environment["FAKE_SOAK_EXIT_WITHOUT_DONE"] = "1";

        var night = Run("--runs", "1", "--stop-after-minutes", "1");

        Assert.Equal(3, night.ExitCode);
        Assert.Contains("the night ended without soak-done", night.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_out, "parity.json")));
    }

    /// <summary>A profile whose settings are somebody's - no plan in them - is not
    /// replaced whole by accident.</summary>
    [Fact]
    public void ASettingsFileThatIsNotASoaksIsRefusedWithoutAdoptProfile()
    {
        if (OperatingSystem.IsWindows()) return;
        var settings = Path.Combine(_store, "settings.json");
        const string theirs = "{\"schema\":\"sts2-pilot-trainer/runmobile-settings/v1\",\"sharing_service_url\":\"https://example.test/\"}\n";
        File.WriteAllText(settings, theirs);

        var refused = Run("--runs", "1");

        Assert.Equal(2, refused.ExitCode);
        Assert.Contains("is not a soak profile's", refused.Error, StringComparison.Ordinal);
        Assert.Equal(theirs, File.ReadAllText(settings));
        Assert.False(File.Exists(_gameLog), "the client was launched anyway");
    }

    [Fact]
    public void ATreeWithNoProfilePointerIsRefused()
    {
        if (OperatingSystem.IsWindows()) return;

        var refused = Run("--client-id", "9", "--runs", "1");

        Assert.Equal(2, refused.ExitCode);
        Assert.Contains("has not been played modded yet", refused.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(_gameLog), "the client was launched anyway");
    }

    [Theory]
    [InlineData("--runs", "0")]
    [InlineData("--runs", "six")]
    [InlineData("--character", "IRONCLAD")]
    [InlineData("--stop-after-minutes", "0")]
    public void APlanThatCannotBeRunIsRefusedBeforeAnythingIsWritten(string flag, string value)
    {
        if (OperatingSystem.IsWindows()) return;

        var refused = Run(flag, value);

        Assert.Equal(2, refused.ExitCode);
        Assert.False(File.Exists(Path.Combine(_store, "settings.json")));
        Assert.False(File.Exists(_gameLog), "the client was launched anyway");
    }

    /// <summary>A journalled recording under another run id, in the sandbox store's
    /// recordings, as the night's own.</summary>
    private (string Manifest, string Journal) ARecordingInTheStore()
    {
        var scratch = Path.Combine(_sandbox, "source");
        Directory.CreateDirectory(scratch);
        var (manifest, journal) = ParityTests.RecordingWithAJournal(scratch);
        var recordings = Path.Combine(_store, "recordings");
        var journalCopy = ParityTests.CopyUnder(manifest, journal, recordings, SoakRun);
        return (Path.Combine(recordings, $"{SoakRun}{RecordingLibrary.ManifestExtension}"), journalCopy);
    }

    private static void WriteExecutable(string path, string content)
    {
        if (OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private Arbiter.Result Run(params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            WorkingDirectory = Arbiter.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        if (args.Length == 1 && args[0] == "status")
        {
            startInfo.ArgumentList.Add(Path.Combine(Arbiter.RepoRoot, "scripts", "retail-client.sh"));
            startInfo.ArgumentList.Add("status");
        }
        else
        {
            startInfo.ArgumentList.Add(Path.Combine(Arbiter.RepoRoot, "scripts", "retail-soak.sh"));
            foreach (var arg in new[] { "--game", _game, "--skip-install", "--client-id", "2", "--out", _out }) startInfo.ArgumentList.Add(arg);
            foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        }

        startInfo.Environment["HOME"] = _home;
        startInfo.Environment["XDG_STATE_HOME"] = string.Empty;
        startInfo.Environment["STS2_GAME_EXECUTABLE"] = string.Empty;
        startInfo.Environment["FAKE_GAME_LOG"] = _gameLog;
        startInfo.Environment["FAKE_GAME_PIDS"] = _gamePids;
        startInfo.Environment["FAKE_GAME_EXECUTABLE"] = _game;
        startInfo.Environment.Remove("SteamAppId");
        startInfo.Environment.Remove("SteamGameId");
        startInfo.Environment["PATH"] = _tools + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        foreach (var (name, value) in _environment) startInfo.Environment[name] = value;

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new Arbiter.Result(process.ExitCode, output.Result, error.Result);
    }
}

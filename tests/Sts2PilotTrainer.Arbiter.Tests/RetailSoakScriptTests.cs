using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
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
/// the flag load-bearing here. The recording the night "makes" is a journalled
/// recording the way <see cref="ParityTests"/> builds one, staged where the stand-in
/// copies it into the sandbox store during the night, because a stand-in plays no run
/// and the script copies only what was written after its launch.
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
    private readonly string _night;
    private readonly Dictionary<string, string> _environment = new(StringComparer.Ordinal);

    public RetailSoakScriptTests()
    {
        _home = Path.Combine(_sandbox, "home");
        _tools = Path.Combine(_sandbox, "tools");
        _game = Path.Combine(_sandbox, "install", "SlayTheSpire2.app", "Contents", "MacOS", "Slay the Spire 2");
        _gameLog = Path.Combine(_sandbox, "game.log");
        _gamePids = Path.Combine(_sandbox, "game.pids");
        _out = Path.Combine(_sandbox, "evidence");
        _night = Path.Combine(_sandbox, "night");
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
        Directory.CreateDirectory(_night);
        _environment["FAKE_SOAK_DONE"] = Path.Combine(_store, "soak-done");
        _environment["FAKE_SOAK_DONE_AFTER"] = "2";
        _environment["FAKE_SOAK_RECORDS_FROM"] = _night;
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
        var (manifest, journal) = ARecordingTheNightMakes();

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
        Assert.Contains("the third figure: parity holds over all 1 night recording(s) beside manifests/; coverage holds; 0 run(s) to read", night.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("record state : live", Run("status").Output, StringComparison.Ordinal);
    }

    /// <summary>A recording the replay does not reproduce fails the figure and the
    /// script, which is the whole point of the night.</summary>
    [GameFact]
    public void AParityFailureExitsOne()
    {
        if (OperatingSystem.IsWindows()) return;
        var (_, journal) = ARecordingTheNightMakes();
        ParityTests.Rewrite(Path.Combine(_night, Path.GetFileName(journal)), seq: 5, entry => entry["before"]!["player.hp"] = "1");

        var night = Run("--runs", "1", "--stop-after-minutes", "1");

        Assert.Equal(1, night.ExitCode);
        Assert.Contains("parity does not hold; 1 of 1 night recording(s) without parity evidence", night.Output, StringComparison.Ordinal);
        Assert.Contains("NOT AT PARITY", night.Output, StringComparison.Ordinal);
    }

    /// <summary>A recording of the night the recorder stopped watching holds nothing
    /// for the parity command, which counts it in the denominator and still exits 0;
    /// the night is held to every one of its recordings being PARITY by its own line
    /// in the artifact, so such a recording is a run to read rather than a figure that
    /// holds over none. What a recorder refusal on retail timing looks like in the
    /// morning.</summary>
    [GameFact]
    public void ANightRecordingWithoutParityEvidenceExitsFourWithTheCount()
    {
        if (OperatingSystem.IsWindows()) return;
        var (manifest, _) = ARecordingTheNightMakes();
        var staged = Path.Combine(_night, Path.GetFileName(manifest));
        var broken = JsonNode.Parse(File.ReadAllText(staged))!;
        broken["source"]!["native"]!["continuity"] = NativeSource.BrokenContinuity;
        File.WriteAllText(staged, broken.ToJsonString());

        var night = Run("--runs", "1", "--stop-after-minutes", "1");

        Assert.Equal(4, night.ExitCode);
        Assert.Contains($"night recording {SoakRun} holds no parity evidence: continuity", night.Output, StringComparison.Ordinal);
        Assert.Contains("parity unproven for 1 of 1 night recording(s)", night.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("parity holds", night.Output, StringComparison.Ordinal);
        var parity = JsonDocument.Parse(File.ReadAllText(Path.Combine(_out, "parity.json"))).RootElement;
        Assert.True(parity.GetProperty("at_parity").GetBoolean(), "the command's own verdict is what the night must not rest on");
    }

    /// <summary>A run the mod gave up in a state it did not know is named from
    /// <c>soak-done</c> and is a finding for the morning, with its own exit code.</summary>
    [GameFact]
    public void ARunTheModCouldNotFinishExitsFour()
    {
        if (OperatingSystem.IsWindows()) return;
        ARecordingTheNightMakes();
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

    /// <summary>The figure is this night's: a recording an earlier night left in the
    /// same profile is neither copied nor measured, and stays where it is. Without
    /// this, a profile that had run before a game update failed every later night on
    /// the earlier build's recordings.</summary>
    [GameFact]
    public void AnEarlierNightsRecordingIsNotCopiedOrMeasured()
    {
        if (OperatingSystem.IsWindows()) return;
        var (manifest, journal) = ARecordingTheNightMakes();
        var earlier = AnEarlierNightsRecordingInTheStore();

        var night = Run("--runs", "1", "--stop-after-minutes", "1");

        Assert.Equal(0, night.ExitCode);
        Assert.Contains("copied 1 recording(s)", night.Output, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_out, "recordings", Path.GetFileName(manifest))));
        Assert.True(File.Exists(Path.Combine(_out, "recordings", Path.GetFileName(journal))));
        Assert.False(File.Exists(Path.Combine(_out, "recordings", Path.GetFileName(earlier.Manifest))), "an earlier night's recording was copied");
        Assert.False(File.Exists(Path.Combine(_out, "recordings", Path.GetFileName(earlier.Journal))), "an earlier night's journal was copied");
        Assert.True(File.Exists(earlier.Manifest) && File.Exists(earlier.Journal), "an earlier night's recording was removed from the store");
        var parity = JsonDocument.Parse(File.ReadAllText(Path.Combine(_out, "parity.json"))).RootElement;
        Assert.Equal(3, parity.GetProperty("summary").GetProperty("native_recordings").GetInt32());
    }

    /// <summary>A plan the mod refuses before starting a run - a character this build
    /// has not got, an ascension the profile has not unlocked - is a zero-run
    /// <c>soak-done</c> with the sentence, so the client quits and the morning reads
    /// the refusal rather than waiting out the deadline and reading nothing.</summary>
    [Fact]
    public void ANightTheModRefusedExitsThreeWithTheSentence()
    {
        if (OperatingSystem.IsWindows()) return;
        _environment["FAKE_SOAK_REFUSAL"] = "this build has no character 'CHARACTER.NOBODY'";

        var night = Run("--runs", "1", "--character", "CHARACTER.NOBODY", "--stop-after-minutes", "1");

        Assert.Equal(3, night.ExitCode);
        Assert.Contains("the mod refused the night: this build has no character 'CHARACTER.NOBODY'", night.Output, StringComparison.Ordinal);
        var done = JsonDocument.Parse(File.ReadAllText(Path.Combine(_out, "soak-done.json"))).RootElement;
        Assert.Equal(0, done.GetProperty("runs_started").GetInt32());
        Assert.Empty(done.GetProperty("runs").EnumerateArray());
        Assert.False(File.Exists(Path.Combine(_out, "parity.json")));
        Assert.False(Directory.Exists(Path.Combine(_out, "recordings")));
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

    /// <summary>An evidence directory is one night's: a second night pointed at a
    /// directory that already holds a night's copy is refused before anything is
    /// written or launched, because the figure is computed over what the directory
    /// holds and a second copy into it would fold the first night in.</summary>
    [GameFact]
    public void ASecondNightIntoTheSameEvidenceDirectoryIsRefused()
    {
        if (OperatingSystem.IsWindows()) return;
        ARecordingTheNightMakes();
        Assert.Equal(0, Run("--runs", "1", "--stop-after-minutes", "1").ExitCode);
        var settingsWritten = File.GetLastWriteTimeUtc(Path.Combine(_store, "settings.json"));
        var launches = File.ReadAllText(_gameLog);

        var refused = Run("--runs", "1", "--stop-after-minutes", "1");

        Assert.Equal(2, refused.ExitCode);
        Assert.Contains("already holds a night's recordings", refused.Error, StringComparison.Ordinal);
        Assert.Equal(settingsWritten, File.GetLastWriteTimeUtc(Path.Combine(_store, "settings.json")));
        Assert.Equal(launches, File.ReadAllText(_gameLog));
    }

    [Theory]
    [InlineData("--runs", "0")]
    [InlineData("--runs", "six")]
    [InlineData("--character", "IRONCLAD")]
    [InlineData("--stop-after-minutes", "0")]
    [InlineData("--seeds", "ABC123,DE\"F")]
    [InlineData("--seeds", "ABC123, DE F")]
    public void APlanThatCannotBeRunIsRefusedBeforeAnythingIsWritten(string flag, string value)
    {
        if (OperatingSystem.IsWindows()) return;

        var refused = Run(flag, value);

        Assert.Equal(2, refused.ExitCode);
        Assert.False(File.Exists(Path.Combine(_store, "settings.json")));
        Assert.False(File.Exists(_gameLog), "the client was launched anyway");
    }

    /// <summary>A journalled recording under another run id, staged for the stand-in
    /// to write into the sandbox store during the night, as the night's own; the paths
    /// are where the store will hold it.</summary>
    private (string Manifest, string Journal) ARecordingTheNightMakes()
    {
        var scratch = Path.Combine(_sandbox, "source");
        Directory.CreateDirectory(scratch);
        var (manifest, journal) = ParityTests.RecordingWithAJournal(scratch);
        ParityTests.CopyUnder(manifest, journal, _night, SoakRun);
        var recordings = Path.Combine(_store, "recordings");
        return (
            Path.Combine(recordings, $"{SoakRun}{RecordingLibrary.ManifestExtension}"),
            Path.Combine(recordings, $"{SoakRun}{RunJournal.FileExtension}"));
    }

    /// <summary>The same recording under a third run id, already in the sandbox store
    /// and dated a day before the night, as an earlier night's.</summary>
    private (string Manifest, string Journal) AnEarlierNightsRecordingInTheStore()
    {
        const string earlierRun = "native-1A2B3C4D5E6F-20260918-010101";
        var scratch = Path.Combine(_sandbox, "earlier");
        Directory.CreateDirectory(scratch);
        var (manifest, journal) = ParityTests.RecordingWithAJournal(scratch);
        var recordings = Path.Combine(_store, "recordings");
        var journalCopy = ParityTests.CopyUnder(manifest, journal, recordings, earlierRun);
        var manifestCopy = Path.Combine(recordings, $"{earlierRun}{RecordingLibrary.ManifestExtension}");
        var yesterday = DateTime.UtcNow.AddDays(-1);
        File.SetLastWriteTimeUtc(manifestCopy, yesterday);
        File.SetLastWriteTimeUtc(journalCopy, yesterday);
        return (manifestCopy, journalCopy);
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

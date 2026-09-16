using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The one way a retail client is launched, watched and released here, held to the
/// two failures it exists to make unreachable.
///
/// "Game already running" is Steam's answer to a launch request while a client
/// exists; "No appID found" is Steamworks' answer to the retail executable opened
/// directly. Both are reproduced here against a stand-in for the executable that
/// behaves as the retail client does at those two points - it stops on the Steam
/// error without --force-steam=off and runs until TERM with it - and a stand-in
/// process table the test writes, because what is being tested is what the helper
/// does with the answers, not whose game is installed. The real process table is
/// asked once, on macOS, about the one reading that used to count the checker
/// itself. Nothing here needs the game.
/// </summary>
public sealed class RetailClientLaunchTests : IDisposable
{
    private const string SteamlessFlag = "--force-steam=off";
    private const string NoAppIdFound = "No appID found";

    private readonly string _sandbox = Path.Combine(
        Path.GetTempPath(), $"retail-client-{Guid.NewGuid():N}");

    private readonly string _home;
    private readonly string _tools;
    private readonly string _game;
    private readonly string _gameLog;
    private readonly string _gamePids;
    private readonly string _openLog;
    private readonly string _rows;
    private readonly List<Process> _bystanders = new();

    public RetailClientLaunchTests()
    {
        _home = Path.Combine(_sandbox, "home");
        _tools = Path.Combine(_sandbox, "tools");
        _game = Path.Combine(
            _sandbox, "install", "SlayTheSpire2.app", "Contents", "MacOS", "Slay the Spire 2");
        _gameLog = Path.Combine(_sandbox, "game.log");
        _gamePids = Path.Combine(_sandbox, "game.pids");
        _openLog = Path.Combine(_sandbox, "open.log");
        _rows = Path.Combine(_sandbox, "process-table.rows");
        Directory.CreateDirectory(_home);
        Directory.CreateDirectory(_tools);
        if (OperatingSystem.IsWindows()) return;
        WriteStandInClient(_game, secondsToExitAfterTerm: 0);
        WriteStandInProcessTable();
        WriteStandInOpen();
    }

    public void Dispose()
    {
        foreach (var bystander in _bystanders)
        {
            try { if (!bystander.HasExited) bystander.Kill(); } catch (InvalidOperationException) { }
            bystander.Dispose();
        }
        // Stand-in clients are this test's own bash processes and are stopped here so
        // a failed assertion never leaves one behind; the helper is what is held to
        // never doing this to a real client.
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
    /// The launch the investigation proved: the retail executable, the Steamless
    /// flag, an explicit isolated client id, an empty working directory, no app-id
    /// override in the environment, and Steam never asked. Then the same client
    /// released through the record, with TERM, and the record cleared.
    /// </summary>
    [Fact]
    public void TheLaunchIsTheProvedOneAndNeverAsksSteam()
    {
        if (OperatingSystem.IsWindows()) return;

        var launched = Run("launch", "--game", _game, "--owner", "worker A");

        Assert.Equal(0, launched.ExitCode);
        var record = RecordPath(launched);
        Assert.True(File.Exists(record), launched.All);
        var seen = StandInSaw();
        Assert.Equal($"{SteamlessFlag} --clientId=1", seen["args"]);
        Assert.Equal("unset", seen["SteamAppId"]);
        Assert.Equal(string.Empty, seen["entries"].Trim());
        Assert.StartsWith(Path.Combine(_home, "Library"), seen["cwd"], StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(seen["cwd"], "steam_appid.txt")));
        Assert.False(File.Exists(_openLog), "Steam was asked to launch: " + ReadOrEmpty(_openLog));
        Assert.Contains("Steam initialization skipped", File.ReadAllText(RecordValue(record, "log")), StringComparison.Ordinal);
        Assert.DoesNotContain(NoAppIdFound, File.ReadAllText(RecordValue(record, "log")), StringComparison.Ordinal);
        var pid = int.Parse(RecordValue(record, "pid"));
        Assert.True(IsAlive(pid));
        Assert.Equal("worker A", RecordValue(record, "owner"));
        // The record carries the physical path, so a symlinked temp root reads differently here.
        Assert.EndsWith(Path.Combine("MacOS", "Slay the Spire 2"), RecordValue(record, "executable"), StringComparison.Ordinal);
        Assert.True(File.Exists(RecordValue(record, "executable")));
        Assert.Equal("1", RecordValue(record, "client_id"));
        Assert.Equal("user://default/1", RecordValue(record, "save_tree"));

        var status = Run("status");
        Assert.Equal(1, status.ExitCode);
        Assert.Contains("record state : live", status.All, StringComparison.Ordinal);
        Assert.Contains($"pid {pid}  owned by worker A", status.All, StringComparison.Ordinal);

        var released = Run("release", "--wait", "10");

        Assert.Equal(0, released.ExitCode);
        Assert.Contains($"Sent TERM to pid {pid}", released.All, StringComparison.Ordinal);
        Assert.False(File.Exists(record));
        Assert.False(Directory.Exists(seen["cwd"]));
        Assert.Contains("exited on TERM", File.ReadAllText(_gameLog), StringComparison.Ordinal);
        Assert.Equal(0, Run("status").ExitCode);
    }

    [Fact]
    public void TheClientIdSelectsTheSaveTreeAndNothingElseReachesTheGame()
    {
        if (OperatingSystem.IsWindows()) return;

        var launched = Run("launch", "--game", _game, "--client-id", "7");

        Assert.Equal(0, launched.ExitCode);
        Assert.Equal($"{SteamlessFlag} --clientId=7", StandInSaw()["args"]);
        Assert.Equal("user://default/7", RecordValue(RecordPath(launched), "save_tree"));
        Assert.Equal(0, Run("release", "--wait", "10").ExitCode);
    }

    /// <summary>
    /// The failure as it happened: the executable opened directly stops on
    /// Steamworks' "No appID found" and never reaches gameplay. The stand-in
    /// reproduces exactly that, which is what makes the launch test above a
    /// regression test rather than a description.
    /// </summary>
    [Fact]
    public void OpenedDirectlyTheClientStopsOnNoAppIdFound()
    {
        if (OperatingSystem.IsWindows()) return;

        var direct = RunDirectly(_game);

        Assert.Equal(1, direct.ExitCode);
        Assert.Contains(NoAppIdFound, direct.All, StringComparison.Ordinal);
        Assert.False(File.Exists(_gamePids), "a client that failed Steam init was counted as running");
    }

    [Theory]
    [InlineData("--")]
    [InlineData("--seed=ABCDEF")]
    [InlineData("--force-steam=on")]
    public void AnArgumentTheHelperDoesNotOwnIsRefusedRatherThanPassedToTheGame(string argument)
    {
        if (OperatingSystem.IsWindows()) return;

        var launched = Run("launch", "--game", _game, argument);

        Assert.Equal(2, launched.ExitCode);
        Assert.Contains($"unknown argument: {argument}", launched.All, StringComparison.Ordinal);
        Assert.False(File.Exists(_gameLog), "the client was launched anyway");
    }

    /// <summary>
    /// The environment variables Steamworks reads an app id from are the other way a
    /// launch would authenticate against the player's own account.
    /// </summary>
    [Theory]
    [InlineData("SteamAppId")]
    [InlineData("SteamGameId")]
    public void AnAppIdOverrideInTheEnvironmentIsRefused(string variable)
    {
        if (OperatingSystem.IsWindows()) return;

        var launched = RunWith(
            new Dictionary<string, string> { [variable] = "2868840" },
            "launch", "--game", _game);

        Assert.Equal(2, launched.ExitCode);
        Assert.Contains($"Refusing to launch with {variable} set", launched.All, StringComparison.Ordinal);
        Assert.False(File.Exists(_gameLog), "the client was launched anyway");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("one")]
    [InlineData("1.5")]
    public void AClientIdThatIsNotAPositiveIntegerIsRefused(string clientId)
    {
        if (OperatingSystem.IsWindows()) return;

        var launched = Run("launch", "--game", _game, "--client-id", clientId);

        Assert.Equal(2, launched.ExitCode);
        Assert.Contains("--client-id must be a positive integer", launched.All, StringComparison.Ordinal);
        Assert.False(File.Exists(_gameLog));
    }

    /// <summary>
    /// "Game already running" is what Steam says when asked to launch beside an
    /// existing client. The helper refuses first, names the client and who has it,
    /// launches nothing and asks Steam nothing.
    /// </summary>
    [Fact]
    public void LaunchRefusesWhileAClientItDidNotLaunchExists()
    {
        if (OperatingSystem.IsWindows()) return;
        var foreign = Bystander();
        WriteRows($"{foreign.Id} 1 /Applications/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/Slay the Spire 2");

        var launched = Run("launch", "--game", _game);

        Assert.Equal(1, launched.ExitCode);
        Assert.Contains("A Slay the Spire 2 client already exists", launched.All, StringComparison.Ordinal);
        Assert.Contains($"pid {foreign.Id}  not launched by this helper", launched.All, StringComparison.Ordinal);
        Assert.False(File.Exists(_gameLog), "a second client was launched");
        Assert.False(File.Exists(_openLog), "Steam was asked to launch: " + ReadOrEmpty(_openLog));
        Assert.False(foreign.HasExited);
    }

    [Fact]
    public void ASecondLaunchRefusesNamingTheOwner()
    {
        if (OperatingSystem.IsWindows()) return;
        var first = Run("launch", "--game", _game, "--owner", "worker A");
        Assert.Equal(0, first.ExitCode);
        var pid = RecordValue(RecordPath(first), "pid");

        var second = Run("launch", "--game", _game, "--owner", "worker B");

        Assert.Equal(1, second.ExitCode);
        Assert.Contains($"pid {pid}  owned by worker A", second.All, StringComparison.Ordinal);
        Assert.Single(File.ReadAllLines(_gamePids));
        Assert.Equal(0, Run("release", "--wait", "10").ExitCode);
    }

    /// <summary>
    /// A process check that reads argument lists counts its own grep, its own shell
    /// and the helper's own --game. This one reads executables, so rows for the
    /// commands the helper runs are not clients.
    /// </summary>
    [Fact]
    public void TheHelpersOwnCommandsAreNotCountedAsAClient()
    {
        if (OperatingSystem.IsWindows()) return;
        WriteRows(
            "11 1 bash",
            "12 11 ps",
            "13 11 grep",
            "14 11 awk",
            "15 11 /bin/sleep",
            "16 11 /opt/homebrew/bin/bash");

        var launched = Run("launch", "--game", _game);

        Assert.Equal(0, launched.ExitCode);
        Assert.Equal(0, Run("release", "--wait", "10").ExitCode);
    }

    /// <summary>
    /// The real process table, on the platform the launch is established on: a
    /// process whose arguments name the game - the shape that made an earlier check
    /// count the agent's own waiting shell - is not a client.
    /// </summary>
    [Fact]
    public void OnMacOSTheRealProcessTableDoesNotCountAProcessWhoseArgumentsNameTheGame()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var decoy = Bystander($"sleep 30 # {_game}");
        var realTools = Path.Combine(_sandbox, "real-tools");
        Directory.CreateDirectory(realTools);

        var status = RunUsingTools(realTools, "status");

        Assert.NotEqual(2, status.ExitCode);
        Assert.Contains("clients      :", status.All, StringComparison.Ordinal);
        Assert.DoesNotContain($"pid {decoy.Id} ", status.All, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseNeverSignalsAClientItDoesNotOwn()
    {
        if (OperatingSystem.IsWindows()) return;
        var foreign = Bystander();
        WriteRows($"{foreign.Id} 1 /somewhere/else/Slay the Spire 2");

        var released = Run("release", "--wait", "1");

        Assert.Equal(1, released.ExitCode);
        Assert.Contains("Refusing to signal a client this helper does not own", released.All, StringComparison.Ordinal);
        Assert.Contains($"pid {foreign.Id}  not launched by this helper", released.All, StringComparison.Ordinal);
        Thread.Sleep(500);
        Assert.False(foreign.HasExited);
    }

    /// <summary>
    /// The retail client takes anywhere from three seconds to forty minutes to
    /// finish after TERM. A release that runs out of patience keeps the record and
    /// says so; a later one waits without signalling again; nothing ever escalates.
    /// The "exited on TERM" line is the proof - a forced kill could not have written it.
    /// </summary>
    [Fact]
    public void ReleaseWaitsForASlowTeardownAndNeverForceKills()
    {
        if (OperatingSystem.IsWindows()) return;
        WriteStandInClient(_game, secondsToExitAfterTerm: 3);
        var launched = Run("launch", "--game", _game);
        Assert.Equal(0, launched.ExitCode);
        var record = RecordPath(launched);
        var pid = int.Parse(RecordValue(record, "pid"));

        var impatient = Run("release", "--wait", "1");

        Assert.Equal(3, impatient.ExitCode);
        Assert.Contains("still exiting", impatient.All, StringComparison.Ordinal);
        Assert.Contains("never force-killed", impatient.All, StringComparison.Ordinal);
        Assert.True(File.Exists(record));
        Assert.NotEqual(string.Empty, RecordValue(record, "term_sent_at"));
        Assert.True(IsAlive(pid));

        var patient = Run("release", "--wait", "15");

        Assert.Equal(0, patient.ExitCode);
        Assert.Contains("TERM was already sent", patient.All, StringComparison.Ordinal);
        Assert.False(File.Exists(record));
        Assert.Contains("exited on TERM", File.ReadAllText(_gameLog), StringComparison.Ordinal);
    }

    [Fact]
    public void AStaleRecordIsReportedAndClearedNotTrusted()
    {
        if (OperatingSystem.IsWindows()) return;
        var state = StateDirectory();
        var staleCwd = Path.Combine(state, "cwd.stale0");
        Directory.CreateDirectory(staleCwd);
        File.WriteAllText(
            Path.Combine(state, "owner"),
            "# sts2-pilot-trainer retail-client owner v1\npid\t999999\nowner\tan earlier worker\n" +
            $"working_directory\t{staleCwd}\n");

        var status = Run("status");
        Assert.Equal(0, status.ExitCode);
        Assert.Contains("record state : stale", status.All, StringComparison.Ordinal);

        var launched = Run("launch", "--game", _game, "--owner", "worker B");

        Assert.Equal(0, launched.ExitCode);
        Assert.Contains("Clearing a stale ownership record: pid 999999 (owner an earlier worker)", launched.All, StringComparison.Ordinal);
        Assert.False(Directory.Exists(staleCwd));
        Assert.Equal("worker B", RecordValue(RecordPath(launched), "owner"));
        Assert.Equal(0, Run("release", "--wait", "10").ExitCode);
    }

    [Fact]
    public void ALaunchInProgressElsewhereIsRefusedAndAnAbandonedLockIsBroken()
    {
        if (OperatingSystem.IsWindows()) return;
        var lockDirectory = Path.Combine(StateDirectory(), "launch.lock");
        Directory.CreateDirectory(lockDirectory);
        var holder = Bystander();
        File.WriteAllText(Path.Combine(lockDirectory, "pid"), holder.Id.ToString());

        var refused = Run("launch", "--game", _game);
        Assert.Equal(1, refused.ExitCode);
        Assert.Contains($"Another launch or release is in progress (pid {holder.Id})", refused.All, StringComparison.Ordinal);
        Assert.False(File.Exists(_gameLog));

        holder.Kill();
        holder.WaitForExit();
        var launched = Run("launch", "--game", _game);

        Assert.Equal(0, launched.ExitCode);
        Assert.Equal(0, Run("release", "--wait", "10").ExitCode);
    }

    /// <summary>
    /// Liveness is judged against what was launched, not against what this
    /// invocation was told: a client launched under --game with an executable of
    /// another name is still the record's client to a release that names no game.
    /// Before the fix that release reported it gone, cleared the record and left
    /// the client running with no owner.
    /// </summary>
    [Fact]
    public void ReleaseFindsAClientLaunchedUnderAnotherExecutableName()
    {
        if (OperatingSystem.IsWindows()) return;
        var renamed = Path.Combine(_sandbox, "copy", "sts2-bin");
        WriteStandInClient(renamed, secondsToExitAfterTerm: 0);
        // The table shows the physical path, which is what the helper records too.
        var environment = new Dictionary<string, string> { ["FAKE_GAME_EXECUTABLE"] = PhysicalPath(renamed) };
        var launched = RunWith(environment, "launch", "--game", renamed);
        Assert.Equal(0, launched.ExitCode);
        var record = RecordPath(launched);
        var pid = int.Parse(RecordValue(record, "pid"));

        var status = RunWith(environment, "status");
        Assert.Equal(1, status.ExitCode);
        Assert.Contains("record state : live", status.All, StringComparison.Ordinal);

        var released = RunWith(environment, "release", "--wait", "10");

        Assert.Equal(0, released.ExitCode);
        Assert.Contains($"Sent TERM to pid {pid}", released.All, StringComparison.Ordinal);
        Assert.False(File.Exists(record));
        Assert.False(IsAlive(pid));
        Assert.Contains("exited on TERM", File.ReadAllText(_gameLog), StringComparison.Ordinal);
    }

    private static string ScriptPath => Path.Combine(Arbiter.RepoRoot, "scripts", "retail-client.sh");

    private string StateDirectory()
    {
        var state = OperatingSystem.IsMacOS()
            ? Path.Combine(_home, "Library", "Application Support", "sts2-pilot-trainer", "retail-client")
            : Path.Combine(_home, ".local", "state", "sts2-pilot-trainer", "retail-client");
        Directory.CreateDirectory(state);
        return state;
    }

    /// <summary>
    /// A process of the test's own that a stand-in process-table row may name as a
    /// client, so "not signalled" is observable as "still alive".
    /// </summary>
    private Process Bystander(string command = "sleep 300")
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(command);
        var process = Process.Start(startInfo)!;
        _bystanders.Add(process);
        return process;
    }

    private static string PhysicalPath(string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("cd \"$(dirname \"$1\")\" && printf '%s/%s' \"$(pwd -P)\" \"$(basename \"$1\")\"");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(path);
        using var process = Process.Start(startInfo)!;
        var physical = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return physical;
    }

    private static bool IsAlive(int pid)
    {
        try { return !Process.GetProcessById(pid).HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static string RecordPath(Arbiter.Result launched)
    {
        var match = Regex.Match(launched.Output, @"^record\s+:\s+(.+)$", RegexOptions.Multiline);
        Assert.True(match.Success, launched.All);
        return match.Groups[1].Value.Trim();
    }

    private static string RecordValue(string record, string key)
    {
        foreach (var line in File.ReadAllLines(record))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length == 2 && parts[0] == key) return parts[1];
        }
        return string.Empty;
    }

    private Dictionary<string, string> StandInSaw()
    {
        Assert.True(File.Exists(_gameLog), "the stand-in client was never launched");
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(_gameLog))
        {
            var parts = line.Split('\t', 2);
            if (parts.Length == 2) seen[parts[0]] = parts[1];
        }
        return seen;
    }

    private static string ReadOrEmpty(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;

    private void WriteRows(params string[] rows) =>
        File.WriteAllText(_rows, string.Join("\n", rows.Select(row => "  " + row)) + "\n");

    /// <summary>
    /// The retail executable at the two points that matter: launched without
    /// --force-steam=off it prints Steamworks' error and stops, exactly as the
    /// client does on the popup; with it, it reports the skip the real log carries,
    /// counts itself as a running client and runs until TERM, taking the given time
    /// to finish - the retail client's teardown is anywhere from seconds to minutes.
    /// </summary>
    private void WriteStandInClient(string path, int secondsToExitAfterTerm)
    {
        var onTerm = secondsToExitAfterTerm > 0 ? $"sleep {secondsToExitAfterTerm}; " : string.Empty;
        WriteExecutable(path,
            "#!/usr/bin/env bash\n" +
            "{\n" +
            "  printf 'cwd\\t%s\\n' \"$PWD\"\n" +
            "  printf 'entries\\t%s\\n' \"$(ls -A | tr '\\n' ' ')\"\n" +
            "  printf 'args\\t%s\\n' \"$*\"\n" +
            "  printf 'SteamAppId\\t%s\\n' \"${SteamAppId-unset}\"\n" +
            "} >> \"$FAKE_GAME_LOG\"\n" +
            "case \" $* \" in\n" +
            "  *' --force-steam=off '*) ;;\n" +
            "  *) echo '[ERROR] Steamworks initialization failed! Result: k_ESteamAPIInitResult_FailedGeneric, message: " +
            "No appID found.  Either launch the game from Steam, or put the file steam_appid.txt containing the correct " +
            "appID in your game folder.'; exit 1 ;;\n" +
            "esac\n" +
            "echo '[INFO] Steam initialization skipped (editor mode). Use --force-steam to enable.'\n" +
            "echo \"$$\" >> \"$FAKE_GAME_PIDS\"\n" +
            $"trap '{onTerm}echo \"exited on TERM\" >> \"$FAKE_GAME_LOG\"; exit 0' TERM\n" +
            "while :; do sleep 1; done\n");
    }

    /// <summary>
    /// Stands in for `ps -Ao pid=,ppid=,comm=`: the rows the test wrote, then one row
    /// per stand-in client that is still alive, in the shape the real table prints.
    /// </summary>
    private void WriteStandInProcessTable() =>
        WriteExecutable(Path.Combine(_tools, "ps"),
            "#!/usr/bin/env bash\n" +
            "if [[ -n \"${FAKE_PS_ROWS:-}\" && -f \"$FAKE_PS_ROWS\" ]]; then cat \"$FAKE_PS_ROWS\"; fi\n" +
            "if [[ -n \"${FAKE_GAME_PIDS:-}\" && -f \"$FAKE_GAME_PIDS\" ]]; then\n" +
            "  while read -r pid; do\n" +
            "    if kill -0 \"$pid\" 2>/dev/null; then printf '%5s %5s %s\\n' \"$pid\" 1 \"$FAKE_GAME_EXECUTABLE\"; fi\n" +
            "  done < \"$FAKE_GAME_PIDS\"\n" +
            "fi\n");

    /// <summary>Records any attempt to hand a launch to the desktop, which is how Steam gets asked.</summary>
    private void WriteStandInOpen() =>
        WriteExecutable(Path.Combine(_tools, "open"),
            "#!/usr/bin/env bash\n" +
            "echo \"open $*\" >> \"$FAKE_OPEN_LOG\"\n");

    private static void WriteExecutable(string path, string content)
    {
        if (OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        File.SetUnixFileMode(path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private Arbiter.Result Run(params string[] args) =>
        Execute(new Dictionary<string, string>(StringComparer.Ordinal), args, _tools);

    private Arbiter.Result RunWith(IReadOnlyDictionary<string, string> environment, params string[] args) =>
        Execute(environment, args, _tools);

    private Arbiter.Result RunUsingTools(string tools, params string[] args) =>
        Execute(new Dictionary<string, string>(StringComparer.Ordinal), args, tools);

    private Arbiter.Result Execute(
        IReadOnlyDictionary<string, string> environment, string[] args, string tools)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            WorkingDirectory = Arbiter.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(ScriptPath);
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);
        startInfo.Environment["HOME"] = _home;
        startInfo.Environment["XDG_STATE_HOME"] = string.Empty;
        startInfo.Environment["STS2_GAME_EXECUTABLE"] = string.Empty;
        startInfo.Environment["FAKE_GAME_LOG"] = _gameLog;
        startInfo.Environment["FAKE_GAME_PIDS"] = _gamePids;
        startInfo.Environment["FAKE_GAME_EXECUTABLE"] = _game;
        startInfo.Environment["FAKE_OPEN_LOG"] = _openLog;
        startInfo.Environment["FAKE_PS_ROWS"] = _rows;
        startInfo.Environment.Remove("SteamAppId");
        startInfo.Environment.Remove("SteamGameId");
        startInfo.Environment["PATH"] =
            tools + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        foreach (var (name, value) in environment) startInfo.Environment[name] = value;

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new Arbiter.Result(process.ExitCode, output, error);
    }

    private Arbiter.Result RunDirectly(string executable)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "bash",
            WorkingDirectory = _sandbox,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(executable);
        startInfo.Environment["FAKE_GAME_LOG"] = _gameLog;
        startInfo.Environment["FAKE_GAME_PIDS"] = _gamePids;

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new Arbiter.Result(process.ExitCode, output, error);
    }
}

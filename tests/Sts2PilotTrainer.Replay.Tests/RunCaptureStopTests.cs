using System.Globalization;

namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// What a capture does at a decision the recorder could not name, and what every
/// decision carries from format v6: the reading it began from as well as the one it
/// settled into. Exercised without the game, like every other rule the capture owns.
/// </summary>
public sealed class RunCaptureStopTests
{
    [Fact]
    public void EveryDecisionCarriesTheReadingItBeganFrom()
    {
        var capture = RunCapture.Begin(Start());
        var entry = capture.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            new StateReading(Floor(1), Digest(-1)), new StateReading(Floor(1, hp: 60), Digest(0)));

        Assert.Equal(Digest(-1), entry.BeforeDigest);
        Assert.Equal("68", entry.Before!["player.hp"]);
        Assert.Equal("60", entry.State["player.hp"]);

        var step = capture.Trace.Steps[^1];
        Assert.Equal("68", step.Before["player.hp"]);
        Assert.Equal("60", step.After["player.hp"]);
    }

    /// <summary>Out of a fight the reward screen is generated on the client's clock
    /// between two decisions, so a before-reading that is not the previous
    /// after-reading is carried, not refused.</summary>
    [Fact]
    public void AGapBetweenTwoDecisionsOutsideAFightIsCarriedRatherThanRefused()
    {
        var capture = RunCapture.Begin(Start());
        capture.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            new StateReading(Floor(1), Digest(-1)), new StateReading(Floor(1), Digest(0)));
        capture.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "1"), ("column", "3")),
            new StateReading(Floor(1, hp: 50), "sha256:" + new string('b', 64)),
            new StateReading(Floor(2, hp: 50), Digest(1)));

        Assert.Equal(RunCaptureState.Recording, capture.State);
        Assert.Equal("50", capture.Trace.Steps[^1].Before["player.hp"]);
    }

    [Fact]
    public void ADecisionAfterAStopIsRefused()
    {
        var capture = Played();
        capture.MarkUnmapped(Stop(capture.NextSeq), new StateReading(Floor(2), Digest(4)));

        var refusal = Assert.Throws<ManifestException>(() => capture.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "2"), ("column", "3")),
            new StateReading(Floor(2), Digest(4)), new StateReading(Floor(3), Digest(5))));

        Assert.Contains("stopped at decision 5", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("NetMysteryAction", refusal.Message, StringComparison.Ordinal);
        Assert.Equal(RunCaptureState.Unmapped, capture.State);
        Assert.Equal(NativeSource.UnmappedIntegrity, capture.Integrity);
        Assert.Equal(NativeSource.ContinuousContinuity, capture.Continuity);
        Assert.Null(capture.Refusal);
    }

    [Fact]
    public void AStopStandsAtTheDecisionAfterTheLastAndHappensOnce()
    {
        var capture = Played();

        var early = Assert.Throws<ManifestException>(() =>
            capture.MarkUnmapped(Stop(2), new StateReading(Floor(2), Digest(4))));
        Assert.Contains("stopped at decision 2 and the next decision would be 5", early.Message, StringComparison.Ordinal);

        capture.MarkUnmapped(Stop(capture.NextSeq), new StateReading(Floor(2), Digest(4)));
        var twice = Assert.Throws<ManifestException>(() =>
            capture.MarkUnmapped(Stop(capture.NextSeq), new StateReading(Floor(2), Digest(4))));
        Assert.Contains("already stopped", twice.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The stop is a journal line, so the session after a crash resumes stopped
    /// rather than carrying on past a decision nothing named. The live digest is not
    /// compared: the run the game came back in is past the stop by exactly the
    /// decision the recorder could not name, and that is not a hole in the watch.
    /// </summary>
    [Fact]
    public void AStopSurvivesIntoTheSessionThatResumesTheRunAndStaysStopped()
    {
        var capture = Played();
        var line = capture.MarkUnmapped(Stop(capture.NextSeq), new StateReading(Floor(2), Digest(4)));

        Assert.StartsWith("{\"unmapped\":", line, StringComparison.Ordinal);
        Assert.Contains("\"before_digest\":\"" + Digest(4) + "\"", line, StringComparison.Ordinal);

        var read = RunJournal.Parse(capture.Journal.Render());
        Assert.NotNull(read.Stop);
        Assert.Equal(5, read.Stop.Decision.Seq);
        Assert.Equal("NetMysteryAction", read.Stop.Decision.Name);

        var resumed = RunCapture.Resume(read, "sha256:" + new string('c', 64));
        Assert.Equal(RunCaptureState.Unmapped, resumed.State);
        Assert.Equal(NativeSource.UnmappedIntegrity, resumed.Integrity);
        Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
        Assert.Null(resumed.Refusal);
        Assert.Equal(5, resumed.NextSeq);
        Assert.Throws<ManifestException>(() => resumed.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "2"), ("column", "3")),
            new StateReading(Floor(2), Digest(4)), new StateReading(Floor(3), Digest(5))));
    }

    [Fact]
    public void AJournalWithADecisionAfterItsStopIsRefused()
    {
        var capture = Played();
        var stop = capture.MarkUnmapped(Stop(capture.NextSeq), new StateReading(Floor(2), Digest(4)));
        var whole = Played().Journal.Render();
        var lines = whole.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

        // The stop between the fourth and fifth decisions, with the fifth after it.
        lines.Insert(lines.Count - 1, stop.TrimEnd('\n'));
        var refusal = Assert.Throws<ManifestException>(() => RunJournal.Parse(string.Join('\n', lines) + "\n"));

        Assert.Contains("after the line saying the recorder stopped", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>The manifest of a stopped capture validates as a recording and is
    /// refused for publication, naming what was met; a console mark on the same
    /// recording is on the journal and does not change which integrity it states.</summary>
    [Fact]
    public void AStoppedCapturesManifestIsKeptAndRefusedForPublication()
    {
        var capture = Played();
        capture.MarkUnmapped(Stop(capture.NextSeq), new StateReading(Floor(2), Digest(4)));
        capture.MarkNonStandard();
        capture.Finish("abandoned");

        var manifest = capture.ToManifest();

        Assert.Equal(NativeSource.UnmappedIntegrity, manifest.Source.Native!.Integrity);
        var entry = Assert.Single(manifest.Source.Native.Unmapped!);
        Assert.Equal(5, entry.Seq);
        Assert.Equal(5, entry.Evidence.ActionOrdinal);
        Assert.True(capture.Journal.NonStandard);
        Assert.Equal(5, manifest.Actions.Count);
        Assert.Contains("stopped at a decision it could not name", manifest.Source.Coverage, StringComparison.Ordinal);

        var result = ManifestValidator.Validate(manifest);
        var problem = Assert.Single(result.Problems);
        Assert.Contains("integrity is 'unmapped'", problem, StringComparison.Ordinal);
        Assert.Contains("net_action NetMysteryAction (Mystery) with target=3", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AVersionOneJournalIsRefusedRatherThanRepaired()
    {
        var text = Played().Journal.Render().Replace(RunJournal.Schema, "sts2-pilot-trainer/run-journal/v1", StringComparison.Ordinal);

        var refusal = Assert.Throws<ManifestException>(() => RunJournal.Parse(text));

        Assert.Contains("declares schema 'sts2-pilot-trainer/run-journal/v1'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADecisionLineWithoutItsBeforeReadingIsRefused()
    {
        var lines = Played().Journal.Render().Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();
        var document = System.Text.Json.Nodes.JsonNode.Parse(lines[2])!.AsObject();
        document.Remove("before");
        document.Remove("before_digest");
        lines[2] = document.ToJsonString();

        var refusal = Assert.Throws<ManifestException>(() => RunJournal.Parse(string.Join('\n', lines) + "\n"));

        Assert.Contains("decision 0 carries no reading of the state it began from", refusal.Message, StringComparison.Ordinal);
    }

    // ── fixtures, the shape RunCaptureTests uses ───────────────────────────

    private static UnmappedDecision Stop(int seq) => new()
    {
        Seq = seq,
        Seam = UnmappedDecision.NetActionSeam,
        Name = "NetMysteryAction",
        Discriminator = "Mystery",
        Args = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["target"] = "3" },
        Evidence = FactEvidence.AtActionOrdinal(seq, 9_000),
    };

    private static RunCapture Played()
    {
        var capture = RunCapture.Begin(Start());
        capture.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            Floor(1), Digest(0));
        capture.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "1"), ("column", "3")),
            InFight(2, turn: 1), Digest(1));
        capture.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.BASH"), ("hand_index", "0")),
            InFight(2, turn: 1, enemyHp: 30), Digest(2));
        capture.Record(ActionVerb.EndTurn, Args(), InFight(2, turn: 2, enemyHp: 30, hp: 58), Digest(3));
        capture.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.STRIKE_IRONCLAD"), ("hand_index", "1")),
            Won(2, hp: 58), Digest(4));
        return capture;
    }

    private static RunRecordingStart Start() => new()
    {
        RunId = "native-SFXT47K77RFK-20260905-030000",
        RecorderVersion = "runmobile-recorder/0.1.0",
        Identity = new RunIdentityReading
        {
            BuildVersion = "v0.111.0",
            BuildDateUtc = "2026.08.14",
            ContentHash = "1568834832",
            GameMode = "standard",
            Seed = "SFXT47K77RFK",
            Ascension = 10,
            Character = "CHARACTER.IRONCLAD",
            Acts = ["ACT.UNDERDOCKS"],
            Unlocks = new UnlockStateInventory
            {
                Epochs = ["EPOCH.ONE"],
                EncountersSeen = ["ENCOUNTER.TEST"],
                Runs = 137,
            },
            Mods = ModEnvironment.AsRecorded(
                [new LocalMod("Runmobile", "Runmobile", "0.1.0", AffectsGameplay: false, "Loaded")],
                RecordedPatchRoster.HostOnly()),
        },
        State = Floor(1),
        Digest = Digest(-1),
        RunClockMs = 0,
    };

    private static IReadOnlyDictionary<string, string> Floor(int floor, int hp = 68) => new Dictionary<string, string>(
        StringComparer.Ordinal)
    {
        ["combat.in_progress"] = "false",
        ["combat.outcome"] = "none",
        ["run.total_floor"] = floor.ToString(CultureInfo.InvariantCulture),
        ["run.map_coord"] = $"r{floor.ToString(CultureInfo.InvariantCulture)}c3",
        ["run.act_floor"] = floor.ToString(CultureInfo.InvariantCulture),
        ["player.hp"] = hp.ToString(CultureInfo.InvariantCulture),
        ["player.max_hp"] = "68",
    };

    private static IReadOnlyDictionary<string, string> InFight(
        int floor, int turn = 1, int enemyHp = 42, int hp = 68) => new Dictionary<string, string>(
        StringComparer.Ordinal)
        {
            ["combat.in_progress"] = "true",
            ["combat.outcome"] = "in_progress",
            ["combat.turn"] = turn.ToString(CultureInfo.InvariantCulture),
            ["combat.encounter"] = "ENCOUNTER.TEST",
            ["combat.enemy_count"] = "1",
            ["combat.enemy.0.model"] = "MONSTER.TEST",
            ["combat.enemy.0.hp"] = enemyHp.ToString(CultureInfo.InvariantCulture),
            ["run.total_floor"] = floor.ToString(CultureInfo.InvariantCulture),
            ["run.map_coord"] = $"r{floor.ToString(CultureInfo.InvariantCulture)}c3",
            ["run.act_floor"] = floor.ToString(CultureInfo.InvariantCulture),
            ["player.hp"] = hp.ToString(CultureInfo.InvariantCulture),
            ["player.max_hp"] = "68",
        };

    private static IReadOnlyDictionary<string, string> Won(int floor, int hp) => new Dictionary<string, string>(
        StringComparer.Ordinal)
    {
        ["combat.in_progress"] = "false",
        ["combat.outcome"] = "victory",
        ["combat.turn"] = "2",
        ["combat.encounter"] = "ENCOUNTER.TEST",
        ["combat.enemy_count"] = "0",
        ["run.total_floor"] = floor.ToString(CultureInfo.InvariantCulture),
        ["run.map_coord"] = $"r{floor.ToString(CultureInfo.InvariantCulture)}c3",
        ["run.act_floor"] = floor.ToString(CultureInfo.InvariantCulture),
        ["player.hp"] = hp.ToString(CultureInfo.InvariantCulture),
        ["player.max_hp"] = "68",
    };

    private static string Digest(int seq) =>
        "sha256:" + (seq + 1).ToString("x2", CultureInfo.InvariantCulture).PadLeft(64, 'a');

    private static IReadOnlyDictionary<string, string> Args(params (string Key, string Value)[] args) =>
        args.ToDictionary(arg => arg.Key, arg => arg.Value, StringComparer.Ordinal);
}

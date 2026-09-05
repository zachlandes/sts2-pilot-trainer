using System.Globalization;

namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The recording of a whole run, exercised without the game.
///
/// Every reading here is written by hand, for the same reason
/// <see cref="FightCaptureTests"/>'s are: the capture owns what a recording means -
/// where its boundaries are, which fight a decision belongs to, whether its watch has
/// a hole in it - and all of that has to hold on inputs nobody needs a game to
/// produce, including the ones it must refuse.
/// </summary>
public sealed class RunCaptureTests
{
    [Fact]
    public void ARunRecordedFromItsStartCarriesItsIdentityAsCapturedFacts()
    {
        var capture = RunCapture.Begin(Start());
        capture.Record(ActionVerb.ChooseNeowBlessing, Args(("option_index", "2")), Floor(1), Digest(0));
        capture.Finish("abandoned");

        var manifest = capture.ToManifest();

        Assert.Equal("native", manifest.Source.Kind);
        Assert.Equal("captured", manifest.Source.ExtractionMethod);
        Assert.Equal(FactSource.Captured, manifest.Environment.Seed.Source);
        Assert.Equal("SFXT47K77RFK", manifest.Environment.Seed.Value);
        Assert.Equal(-1, manifest.Environment.Seed.Evidence?.ActionOrdinal);
        Assert.True(manifest.Environment.Unlocks.Value.IsExact);
        Assert.Equal(["EPOCH.ONE"], manifest.Environment.Unlocks.Value.Inventory!.Epochs);
        Assert.True(manifest.Source.Native!.WitnessedRunStart.Value);
        Assert.Equal(NativeSource.ContinuousContinuity, manifest.Source.Native.Continuity);
        Assert.Equal("abandoned", manifest.Source.Native.Outcome);
    }

    [Fact]
    public void EveryDecisionIsCapturedAtItsOwnOrdinal()
    {
        var capture = RunCapture.Begin(Start());
        capture.Record(ActionVerb.ChooseNeowBlessing, Args(("option_index", "0")), Floor(1), Digest(0), 1000);
        capture.Record(ActionVerb.MapMove, Args(("act", "0"), ("row", "1"), ("column", "3")), Floor(2), Digest(1), 2000);
        capture.Finish("lost");

        var manifest = capture.ToManifest();

        Assert.Equal([0, 1], manifest.Actions.Select(action => action.Seq));
        Assert.All(manifest.Actions, action => Assert.Equal(FactSource.Captured, action.Source));
        Assert.Equal(0, manifest.Actions[0].Evidence?.ActionOrdinal);
        Assert.Equal(1000, manifest.Actions[0].Evidence?.RunClockMs);
        Assert.Equal(1, manifest.Actions[1].Evidence?.ActionOrdinal);
    }

    [Fact]
    public void ARunItDidNotSeeBeginIsRefused()
    {
        var refusal = Assert.Throws<ManifestException>(() =>
            RunCapture.Begin(Start() with { State = Floor(4) }));

        Assert.Contains("already on floor 4", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("replays perfectly into a different run", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunAlreadyInAFightIsRefusedToo()
    {
        var refusal = Assert.Throws<ManifestException>(() =>
            RunCapture.Begin(Start() with { State = InFight(1, turn: 3) }));

        Assert.Contains("already in a fight", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFightIsDelegatedToACaptureOfItsOwnAndEndsWhenTheEngineSaysSo()
    {
        var capture = Played();

        var fight = Assert.Single(capture.Fights);
        Assert.Equal(FightCaptureState.Completed, fight.State);
        Assert.Equal(
            ["combat_start", "PlayCard", "EndTurn", "PlayCard"],
            fight.Trace.Steps.Select(step => step.Verb));

        // The decision that entered the fight is the boundary it begins at, not one of
        // its actions - the same place the headless trace puts combat start.
        Assert.Equal(Digest(1), fight.CombatStartSnapshotDigest);
        Assert.Null(capture.Fight);
    }

    [Fact]
    public void EveryBoundaryTheRunReachedCarriesTheDigestReadThere()
    {
        var manifest = Played().ToManifest();

        Assert.Equal(
            [
                (ReplayBoundary.CombatStartKind, 1),
                (ReplayBoundary.FloorEntryKind, 1),
                (ReplayBoundary.TurnStartKind, 1),
                (ReplayBoundary.TurnStartKind, 3),
            ],
            manifest.Boundaries.Select(boundary => (boundary.Kind, boundary.AfterSeq)));

        Assert.All(manifest.Boundaries, boundary =>
        {
            Assert.Equal(FactSource.Captured, boundary.Digest.Source);
            Assert.Equal(boundary.AfterSeq, boundary.Digest.Evidence?.ActionOrdinal);
            Assert.Equal(Digest(boundary.AfterSeq), boundary.Digest.Value);
        });
        Assert.Equal(Digest(1), manifest.CombatStartDigest());
    }

    [Fact]
    public void EveryBoundaryIsAlsoACheckpointOfWhatWasReadThere()
    {
        var manifest = Played().ToManifest();

        var combatStart = Assert.Single(manifest.Checkpoints, c => c.Id == "fight-1-start");
        Assert.Equal(ReplayBoundary.CombatStartKind, combatStart.Kind);
        Assert.Equal(1, combatStart.AfterSeq);
        Assert.Equal("1", combatStart.Expect["combat.turn"].Value);
        Assert.All(combatStart.Expect.Values, fact =>
        {
            Assert.Equal(FactSource.Captured, fact.Source);
            Assert.Equal(1, fact.Evidence?.ActionOrdinal);
        });

        Assert.Contains(manifest.Checkpoints, c => c.Id == "floor-2-entry");
        Assert.Contains(manifest.Checkpoints, c => c.Id == "fight-1-turn-2");
    }

    [Fact]
    public void ARunThatReachedNoBoundaryStillHasSomethingToDisagreeWith()
    {
        // A validator rule rather than a nicety: a replay with nothing to disagree with
        // proves only that it ran.
        var capture = RunCapture.Begin(Start());
        capture.Record(ActionVerb.ChooseNeowBlessing, Args(("option_index", "0")), Floor(1), Digest(0));
        capture.Finish("abandoned");

        var checkpoint = Assert.Single(capture.ToManifest().Checkpoints);
        Assert.Equal("run-end", checkpoint.Id);
        Assert.Equal(RunCapture.RunEndCheckpointKind, checkpoint.Kind);
        Assert.Equal(0, checkpoint.AfterSeq);
    }

    [Fact]
    public void AGiveUpIsACompletedRecordingAndAnUnknownOutcomeIsNot()
    {
        var capture = Played();
        capture.Finish("abandoned");

        Assert.Equal(RunCaptureState.Finished, capture.State);
        Assert.Equal("abandoned", capture.ToManifest().Source.Native!.Outcome);

        var refusal = Assert.Throws<ManifestException>(() => RunCapture.Begin(Start()).Finish("quit"));
        Assert.Contains("not one of the outcomes", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AFightStillBeingFoughtWhenTheRunEndsHasNoLineToProject()
    {
        var capture = RunCapture.Begin(Start());
        capture.Record(ActionVerb.ChooseNeowBlessing, Args(("option_index", "0")), Floor(1), Digest(0));
        capture.Record(ActionVerb.MapMove, Args(("act", "0"), ("row", "1"), ("column", "3")), InFight(1), Digest(1));
        capture.Finish("abandoned");

        var fight = Assert.Single(capture.Fights);
        Assert.Equal(FightCaptureState.Abandoned, fight.State);
        Assert.Throws<ManifestException>(fight.Project);
    }

    [Fact]
    public void NoDecisionIsRecordedAfterTheRunIsOver()
    {
        var capture = Played();
        capture.Finish("won");

        var refusal = Assert.Throws<ManifestException>(() =>
            capture.Record(ActionVerb.MapMove, Args(("act", "0"), ("row", "2"), ("column", "1")), Floor(3), Digest(9)));

        Assert.Contains("A second run is a second recording", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLastDigestIsWhereTheRecordingStandsRightNow()
    {
        // What a host asks to tell a decision the engine turned down from one it made:
        // a digest covers the draw order and every random stream's position, so two
        // decisions apart it is the sharpest answer to whether anything happened.
        var capture = RunCapture.Begin(Start());
        Assert.Equal(Digest(-1), capture.LastDigest);

        capture.Record(ActionVerb.ChooseNeowBlessing, Args(("option_index", "0")), Floor(1), Digest(0));
        Assert.Equal(Digest(0), capture.LastDigest);
    }

    [Fact]
    public void ASessionThatResumesWhereItLeftOffCarriesOnRecording()
    {
        var journal = Played().Journal;

        var resumed = RunCapture.Resume(journal, Digest(4));

        Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
        Assert.Equal(RunCaptureState.Recording, resumed.State);
        Assert.Null(resumed.Refusal);
        Assert.Equal(5, resumed.NextSeq);
        Assert.Equal(FightCaptureState.Completed, Assert.Single(resumed.Fights).State);
    }

    [Fact]
    public void ASessionThatResumesEarlierThanItLeftOffIsBrokenAndKeepsEverything()
    {
        var journal = Played().Journal;

        var resumed = RunCapture.Resume(journal, Digest(1));

        Assert.Equal(NativeSource.BrokenContinuity, resumed.Continuity);
        Assert.Equal(RunCaptureState.Broken, resumed.State);
        Assert.Contains("resumed this run at decision 1", resumed.Refusal!, StringComparison.Ordinal);
        Assert.Contains("to decision 4", resumed.Refusal!, StringComparison.Ordinal);

        // Nothing is truncated. What was seen stays seen, and it is the continuity that
        // says the history is not this run's.
        resumed.Finish("abandoned");
        var manifest = resumed.ToManifest();
        Assert.Equal(5, manifest.Actions.Count);
        Assert.Equal(NativeSource.BrokenContinuity, manifest.Source.Native!.Continuity);
    }

    [Fact]
    public void ASessionThatResumesSomewhereTheRecorderNeverSawIsBrokenToo()
    {
        var resumed = RunCapture.Resume(Played().Journal, Digest(77));

        Assert.Equal(NativeSource.BrokenContinuity, resumed.Continuity);
        Assert.Contains("is not one this recording ever saw", resumed.Refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public void AJournalWrittenByARecorderThatJoinedLateStaysBrokenHoweverItResumes()
    {
        var journal = Played().Journal with { WitnessedRunStart = false };

        var resumed = RunCapture.Resume(journal, Digest(4));

        Assert.False(resumed.WitnessedRunStart);
        Assert.Equal(NativeSource.BrokenContinuity, resumed.Continuity);
        Assert.False(resumed.ToManifest().Source.Native!.WitnessedRunStart.Value);
    }

    [Fact]
    public void AJournalRoundTripsThroughItsOwnFileFormat()
    {
        var written = Played().Journal;

        var read = RunJournal.Parse(written.Render());

        Assert.Equal(RunJournal.Schema, read.SchemaId);
        Assert.Equal(written.RunId, read.RunId);
        Assert.Equal(written.Identity.Seed, read.Identity.Seed);
        Assert.Equal(
            written.Entries.Select(entry => (entry.Seq, entry.Verb, entry.Digest)),
            read.Entries.Select(entry => (entry.Seq, entry.Verb, entry.Digest)));
    }

    [Fact]
    public void AJournalWhoseLastLineWasCutOffByACrashKeepsThePrefix()
    {
        var whole = Played().Journal.Render();
        var truncated = whole[..(whole.Length - 30)];

        var read = RunJournal.Parse(truncated);

        Assert.Equal(5, read.Entries.Count);
        Assert.Equal(3, read.Entries[^1].Seq);
    }

    [Fact]
    public void AJournalThisBuildCannotReadIsRefusedRatherThanReadPartially()
    {
        var lines = Played().Journal.Render().Split('\n');
        lines[0] = lines[0].Replace(RunJournal.Schema, "somebody-elses/journal/v9", StringComparison.Ordinal);

        var refusal = Assert.Throws<ManifestException>(() => RunJournal.Parse(string.Join("\n", lines)));

        Assert.Contains("somebody-elses/journal/v9", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AJournalWithAGapInItsDecisionsIsRefused()
    {
        var journal = Played().Journal;
        var gapped = journal with { Entries = [journal.Entries[0], journal.Entries[1], journal.Entries[3]] };

        var refusal = Assert.Throws<ManifestException>(() => RunCapture.Resume(gapped, Digest(4)));

        Assert.Contains("a missing decision wearing a plausible face", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordingAPlayedRunProducesIsOneTheValidatorAccepts()
    {
        // The whole format contract in one assertion. Everything the recorder writes -
        // the captured provenance on every value, the ordinals, the boundary kinds and
        // their digests, the exact unlock requirement, the mod list read out of the
        // game - has to satisfy the rules Phase 1 wrote, and this is where that is
        // established without a game.
        var capture = Played();
        capture.Finish("abandoned");

        var result = ManifestValidator.Validate(capture.ToManifest());

        Assert.True(result.IsValid, result.Describe());
    }

    [Fact]
    public void ARecordingWithAHoleInItIsRefusedByTheValidator()
    {
        var capture = Played();
        capture.MarkBroken("the recorder stopped and started again");
        capture.Finish("abandoned");

        var result = ManifestValidator.Validate(capture.ToManifest());

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("continuity is 'broken'", StringComparison.Ordinal));
    }

    /// <summary>
    /// A short run: Neow, a map move into a fight, two cards and an ended turn either
    /// side of a second turn, and the killing blow.
    /// </summary>
    private static RunCapture Played()
    {
        var capture = RunCapture.Begin(Start());
        capture.Record(ActionVerb.ChooseNeowBlessing, Args(("option_index", "0")), Floor(1), Digest(0));
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
        Identity = Identity(),
        State = Floor(1),
        Digest = Digest(-1),
        RunClockMs = 0,
    };

    private static RunIdentityReading Identity() => new()
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
            [new LocalMod("Runmobile", "Runmobile", "0.1.0", AffectsGameplay: false, "Loaded")]),
    };

    /// <summary>A reading taken between fights, on a floor.</summary>
    private static IReadOnlyDictionary<string, string> Floor(int floor) => new Dictionary<string, string>(
        StringComparer.Ordinal)
    {
        ["combat.in_progress"] = "false",
        ["combat.outcome"] = "none",
        ["run.total_floor"] = floor.ToString(CultureInfo.InvariantCulture),
        ["run.act_floor"] = floor.ToString(CultureInfo.InvariantCulture),
        ["player.hp"] = "68",
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
        ["run.act_floor"] = floor.ToString(CultureInfo.InvariantCulture),
        ["player.hp"] = hp.ToString(CultureInfo.InvariantCulture),
        ["player.max_hp"] = "68",
    };

    /// <summary>A distinct digest per decision, so a test can say which moment a
    /// boundary or a resumed session is standing at.</summary>
    private static string Digest(int seq) =>
        "sha256:" + (seq + 1).ToString("x2", CultureInfo.InvariantCulture).PadLeft(64, 'a');

    private static IReadOnlyDictionary<string, string> Args(params (string Key, string Value)[] args) =>
        args.ToDictionary(arg => arg.Key, arg => arg.Value, StringComparer.Ordinal);
}

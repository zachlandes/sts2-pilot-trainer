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
        capture.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "2"), ("option_key", "NEOW.BLESSING")),
            Floor(1), Digest(0));
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
        capture.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            Floor(1), Digest(0), 1000);
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
        var manifest = Ended().ToManifest();

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
        var manifest = Ended().ToManifest();

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
        capture.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            Floor(1), Digest(0));
        capture.Finish("abandoned");

        var checkpoint = Assert.Single(capture.ToManifest().Checkpoints);
        Assert.Equal("run-end", checkpoint.Id);
        Assert.Equal(RunCapture.RunEndCheckpointKind, checkpoint.Kind);
        Assert.Equal(0, checkpoint.AfterSeq);
    }

    /// <summary>
    /// A run still being played has no manifest, because a manifest says how the run
    /// ended.
    ///
    /// A defaulted outcome would have a crashed session's prefix read as a give-up,
    /// indistinguishable from a player who gave up - and how a run ended is exactly the
    /// kind of value this project refuses to guess. The journal is the crash-surviving
    /// form and needs no outcome to be read.
    /// </summary>
    [Fact]
    public void ARunStillBeingPlayedHasNoManifest()
    {
        var capture = Played();

        var refusal = Assert.Throws<ManifestException>(capture.ToManifest);

        Assert.Contains("has not ended", refusal.Message, StringComparison.Ordinal);

        capture.Finish("lost");
        Assert.Equal("lost", capture.ToManifest().Source.Native!.Outcome);
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
        capture.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            Floor(1), Digest(0));
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

        capture.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            Floor(1), Digest(0));
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
    public void AMidFightSaveAndQuitResumesContinuouslyAndMarksTheDiscardedBranch()
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

        var resumed = RunCapture.Resume(RunJournal.Parse(capture.Journal.Render()), Digest(1));

        Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
        Assert.Equal(RunCaptureState.Recording, resumed.State);
        Assert.Null(resumed.Refusal);
        Assert.Equal(2, resumed.NextSeq);
        Assert.NotNull(resumed.ResumptionRecord);

        var persisted = RunJournal.Parse(resumed.Journal.Render());
        var discarded = Assert.Single(persisted.Discarded);
        Assert.Equal([2, 3], discarded.Entries.Select(entry => entry.Seq));

        resumed.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.STRIKE_IRONCLAD"), ("hand_index", "1")),
            Won(2, hp: 58), Digest(4));

        resumed = RunCapture.Resume(RunJournal.Parse(resumed.Journal.Render()), Digest(4));
        Assert.Equal(3, resumed.NextSeq);
        Assert.Single(resumed.Discarded);

        resumed.Finish("abandoned");
        var manifest = resumed.ToManifest();

        Assert.Equal([0, 1, 2], manifest.Actions.Select(action => action.Seq));
        Assert.Equal([2, 3], Assert.Single(manifest.Source.Native!.Discarded!).Actions.Select(action => action.Seq));
        Assert.Equal(NativeSource.ContinuousContinuity, manifest.Source.Native.Continuity);
        Assert.True(ManifestValidator.Validate(manifest).IsValid);
    }

    [Fact]
    public void AMidFightRollbackRemainsValidWhenTheContinuedFightIsAbandoned()
    {
        var capture = Played();
        capture.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "2"), ("column", "3")),
            InFight(3), Digest(5));
        capture.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.BASH"), ("hand_index", "0")),
            InFight(3, enemyHp: 30), Digest(6));

        var resumed = RunCapture.Resume(RunJournal.Parse(capture.Journal.Render()), Digest(5));
        resumed.Finish("abandoned");
        var verified = Verified(resumed);

        var result = ManifestValidator.Validate(verified);

        Assert.True(result.IsValid, result.Describe());
        Assert.Contains(verified.Verification!.Boundaries, boundary =>
            boundary.Kind == ReplayBoundary.FloorEntryKind && boundary.AfterSeq == 5);
        Assert.DoesNotContain(verified.Verification.Boundaries, boundary =>
            boundary.IsCombatStart && boundary.AfterSeq == 5);
    }

    [Fact]
    public void AnEventFightRollbackRemainsValidWhenTheContinuedFightIsAbandoned()
    {
        var capture = Played();
        capture.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "2"), ("column", "3")),
            Floor(3), Digest(5));
        capture.Record(
            ActionVerb.ChooseEventOption,
            Args(("event_id", "EVENT.TEST"), ("option_index", "0"), ("option_key", "EVENT.FIGHT")),
            InFight(3), Digest(6));
        capture.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.BASH"), ("hand_index", "0")),
            InFight(3, enemyHp: 30), Digest(7));

        var resumed = RunCapture.Resume(RunJournal.Parse(capture.Journal.Render()), Digest(5));
        resumed.Record(
            ActionVerb.ChooseEventOption,
            Args(("event_id", "EVENT.TEST"), ("option_index", "0"), ("option_key", "EVENT.FIGHT")),
            InFight(3), Digest(6));
        resumed.Finish("abandoned");
        var verified = Verified(resumed);

        var result = ManifestValidator.Validate(verified);

        Assert.True(result.IsValid, result.Describe());
        var discarded = Assert.Single(verified.Source.Native!.Discarded!);
        Assert.Equal([6, 7], discarded.Actions.Select(action => action.Seq));
        Assert.Contains(verified.Verification!.Trace!.Steps, step =>
            step.Seq == 6 && step.After["combat.outcome"] == "in_progress");
    }

    [Fact]
    public void AnEventFightRollbackRemainsValidWhenTheContinuedChoiceIsNotCombat()
    {
        var capture = Played();
        capture.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "2"), ("column", "3")),
            Floor(3), Digest(5));
        capture.Record(
            ActionVerb.ChooseEventOption,
            Args(("event_id", "EVENT.TEST"), ("option_index", "0"), ("option_key", "EVENT.FIGHT")),
            InFight(3), Digest(6));
        capture.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.BASH"), ("hand_index", "0")),
            InFight(3, enemyHp: 30), Digest(7));

        var resumed = RunCapture.Resume(RunJournal.Parse(capture.Journal.Render()), Digest(5));
        resumed.Record(
            ActionVerb.ChooseEventOption,
            Args(("event_id", "EVENT.TEST"), ("option_index", "1"), ("option_key", "EVENT.SAFE")),
            Floor(3), Digest(60));
        resumed = RunCapture.Resume(RunJournal.Parse(resumed.Journal.Render()), Digest(60));
        resumed.Finish("abandoned");
        var verified = Verified(resumed);

        var result = ManifestValidator.Validate(verified);

        Assert.True(result.IsValid, result.Describe());
        Assert.Single(RunCoverage.Of(verified.Verification!.Trace!).Fights);
        var discarded = Assert.Single(verified.Source.Native!.Discarded!);
        Assert.Contains(discarded.Trace.Steps, step =>
            step.Seq == 6 && step.After["combat.outcome"] == "in_progress");
    }

    [Fact]
    public void ASessionThatResumesAtAnEarlierNonFightBoundaryIsBroken()
    {
        var resumed = RunCapture.Resume(Played().Journal, Digest(0));

        Assert.Equal(NativeSource.BrokenContinuity, resumed.Continuity);
        Assert.Equal(RunCaptureState.Broken, resumed.State);
        Assert.Contains("not the game's rollback of a live fight", resumed.Refusal!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A session that stopped inside a fight resumes with that fight still live.
    ///
    /// The game saves when a room is entered, so quitting straight after walking onto a
    /// combat node leaves a journal whose last decision put the run in a fight. What
    /// picks that back up is <see cref="RunCapture.Resume"/> replaying the entries, and
    /// the fight it rebuilds is what the recorder has to start watching again - a fight
    /// held open with nothing watching it drops every card play and ended turn left in
    /// it while the recording still reports a continuous watch.
    /// </summary>
    [Fact]
    public void ASessionThatStoppedInsideAFightResumesWithThatFightStillLive()
    {
        var capture = RunCapture.Begin(Start());
        capture.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            Floor(1), Digest(0));
        capture.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "1"), ("column", "3")),
            InFight(2, turn: 1), Digest(1));
        Assert.NotNull(capture.Fight);

        var resumed = RunCapture.Resume(RunJournal.Parse(capture.Journal.Render()), Digest(1));

        Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
        Assert.NotNull(resumed.Fight);
        Assert.Equal(FightCaptureState.Live, resumed.Fight!.State);
        Assert.False(resumed.Fight.HasOpenStep);
        Assert.Equal(Digest(1), resumed.Fight.CombatStartSnapshotDigest);
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
        resumed.Finish("abandoned");
        Assert.False(resumed.ToManifest().Source.Native!.WitnessedRunStart.Value);
    }

    /// <summary>
    /// A break one session decided on is still a break two sessions later.
    ///
    /// The losing sequence without it: session one resumes at a rolled-back save and is
    /// marked broken, carries on recording, and is quit to the main menu; session two
    /// finds a journal whose last digest is exactly the live one, sees nothing wrong,
    /// and publishes <c>continuity = continuous</c> over a history with a hole in it.
    /// Continuity is the one fact nothing downstream can re-derive, so that recording
    /// would carry a false claim nobody could check.
    /// </summary>
    [Fact]
    public void ASessionResumedAfterAnEarlierOneWasBrokenIsStillBroken()
    {
        var first = RunCapture.Resume(Played().Journal, Digest(1));
        Assert.Equal(NativeSource.BrokenContinuity, first.Continuity);
        first.Record(ActionVerb.SkipRewards, Args(), Floor(2), Digest(5));

        var second = RunCapture.Resume(RunJournal.Parse(first.Journal.Render()), Digest(5));

        Assert.Equal(NativeSource.BrokenContinuity, second.Continuity);
        Assert.Equal(RunCaptureState.Broken, second.State);
        Assert.Contains("resumed this run at decision 1", second.Refusal!, StringComparison.Ordinal);
        Assert.Equal(6, second.NextSeq);
    }

    /// <summary>
    /// And so is a refusal raised in the middle of a session - a card screen answered
    /// with a card it did not offer, a settle that ran out of time - when the player
    /// quits and continues.
    /// </summary>
    [Fact]
    public void ARefusalRaisedMidSessionSurvivesAQuitAndContinue()
    {
        var first = Played();
        first.MarkBroken("a card screen came back with a card it never offered");

        var second = RunCapture.Resume(RunJournal.Parse(first.Journal.Render()), Digest(4));

        Assert.Equal(NativeSource.BrokenContinuity, second.Continuity);
        Assert.Equal(RunCaptureState.Broken, second.State);
        Assert.Contains("never offered", second.Refusal!, StringComparison.Ordinal);

        second.Finish("abandoned");
        Assert.Equal(
            NativeSource.BrokenContinuity, second.ToManifest().Source.Native!.Continuity);
    }

    /// <summary>The line a refusal is written as, which is what the recorder appends the
    /// moment it raises one rather than at the end of a run nobody may reach.</summary>
    [Fact]
    public void ARefusalIsAJournalLineOfItsOwnAndTheDecisionsAroundItAreUntouched()
    {
        var capture = Played();
        var line = capture.MarkBroken("the engine never settled");

        var read = RunJournal.Parse(capture.Journal.Render());

        Assert.Equal("the engine never settled", Assert.Single(read.Refusals));
        Assert.Equal(6, read.Entries.Count);
        Assert.Equal(RunJournal.RenderRefusal("the engine never settled"), line);
    }

    /// <summary>
    /// A run nobody touched the console in says so, and is publishable.
    ///
    /// The passing half of the pair below. Without it, a capture that marked every run
    /// non-standard would pass every refusal test here.
    /// </summary>
    [Fact]
    public void ARunPlayedByTheGamesOwnRulesIsCompleteAndPublishable()
    {
        var capture = Played();
        capture.Finish("won");

        var manifest = capture.ToManifest();

        Assert.Equal(NativeSource.CompleteIntegrity, capture.Integrity);
        Assert.False(capture.Journal.NonStandard);
        Assert.Equal(NativeSource.CompleteIntegrity, manifest.Source.Native!.Integrity);
        Assert.True(ManifestValidator.Validate(manifest).IsValid);
    }

    /// <summary>
    /// A run the console was used in is recorded to its end, kept whole, and refused
    /// for publication.
    ///
    /// The three claims are separate and all three matter. It is not truncated, because
    /// the player played it; it is not marked broken, because the recorder watched all
    /// of it; and it is not valid, because what the console did is not among the
    /// decisions the history holds and replaying them reconstructs a different run.
    /// </summary>
    [Fact]
    public void ARunTheConsoleWasUsedInIsKeptWholeAndRefusedForPublication()
    {
        var capture = RunCapture.Begin(Start());
        capture.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            Floor(1), Digest(0));
        capture.MarkNonStandard();
        capture.Record(ActionVerb.MapMove, Args(("act", "0"), ("row", "1"), ("column", "3")), Floor(2), Digest(1));
        capture.Finish("won");

        var manifest = capture.ToManifest();

        // Recorded to its end: the decision after the command is in the history.
        Assert.Equal([0, 1], manifest.Actions.Select(action => action.Seq));

        // And the watch is not what is wrong with it.
        Assert.Equal(RunCaptureState.Finished, capture.State);
        Assert.Equal(NativeSource.ContinuousContinuity, capture.Continuity);
        Assert.Null(capture.Refusal);

        Assert.Equal(NativeSource.NonStandardIntegrity, manifest.Source.Native!.Integrity);

        var result = ManifestValidator.Validate(manifest);
        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("integrity is 'non-standard'", StringComparison.Ordinal));
    }

    /// <summary>
    /// The mark is a journal line, so the session after a crash is told about it.
    ///
    /// The same reasoning a refusal line carries: a console command only the running
    /// session knows about is one a crash takes with it, and the session that resumed
    /// would publish a run the console had been used in with every value in the
    /// recording true.
    /// </summary>
    [Fact]
    public void AConsoleCommandSurvivesIntoTheSessionThatResumesTheRun()
    {
        var capture = Played();
        var line = capture.MarkNonStandard();

        Assert.Equal(RunJournal.RenderNonStandard(), line);

        // The mark and nothing else: no command the player typed is on the file.
        Assert.DoesNotContain("kill", capture.Journal.Render(), StringComparison.OrdinalIgnoreCase);

        var read = RunJournal.Parse(capture.Journal.Render());
        Assert.True(read.NonStandard);

        // Every decision is still there, and the resumed capture is non-standard
        // without having seen the command itself.
        Assert.Equal(6, read.Entries.Count);
        var resumed = RunCapture.Resume(read, Digest(4));
        Assert.Equal(NativeSource.NonStandardIntegrity, resumed.Integrity);
        Assert.True(resumed.Journal.NonStandard);
        Assert.Equal(RunCaptureState.Recording, resumed.State);
    }

    /// <summary>Marking twice means the same thing once: it is a statement about the
    /// run, not a counter anything acts on, so a journal carrying two marks resumes
    /// into exactly the state one mark leaves.</summary>
    [Fact]
    public void ASecondConsoleCommandDoesNotChangeWhatTheRecordingSays()
    {
        var capture = Played();
        var once = capture.MarkNonStandard();
        capture.MarkNonStandard();

        Assert.Equal(NativeSource.NonStandardIntegrity, capture.Integrity);

        var twiceMarked = RunJournal.Parse(capture.Journal.Render() + once);
        var resumed = RunCapture.Resume(twiceMarked, Digest(4));

        Assert.True(twiceMarked.NonStandard);
        Assert.Equal(NativeSource.NonStandardIntegrity, resumed.Integrity);
        Assert.Equal(RunCapture.Resume(RunJournal.Parse(capture.Journal.Render()), Digest(4)).Journal.Render(),
            resumed.Journal.Render());

        capture.Finish("abandoned");
        Assert.Equal(
            NativeSource.NonStandardIntegrity, capture.ToManifest().Source.Native!.Integrity);
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

    /// <summary>
    /// A second crash after the first does not cost the recording everything after it.
    ///
    /// The fragment a crash leaves behind is dropped by the reader but is still on the
    /// file, and an append onto it fuses the next entry into a line nothing can read.
    /// That line is last, so the session that wrote it resumes; one more decision puts
    /// it in the middle, where the truncation rule does not apply and the whole journal
    /// is refused - so the rest of the run goes unrecorded and no manifest is written.
    ///
    /// Driven on a real file through the same three operations the recorder performs on
    /// one: read it, repair it, append to it. The first assertion is the failure this
    /// exists to prevent, so the test cannot pass with the repair taken out.
    /// </summary>
    [Fact]
    public void AnEntryAppendedAfterACrashDoesNotFuseOntoTheLineItCutShort()
    {
        var whole = Played().Journal.Render();
        var path = Path.Combine(Path.GetTempPath(), $"runmobile-journal-{Guid.NewGuid():N}.journal.jsonl");
        File.WriteAllText(path, whole[..(whole.Length - 30)]);

        try
        {
            var crashed = File.ReadAllText(path);

            // What the file does after two more appends if nothing repairs it: the
            // fused line is no longer last, so the whole journal is refused.
            var refused = Record.Exception(() => RunJournal.Parse(crashed + Continued(crashed)));
            Assert.True(
                refused is System.Text.Json.JsonException or ManifestException,
                $"an unrepaired journal read back as {refused?.GetType().Name ?? "readable"}");

            var repair = RunJournal.RepairTruncatedTail(crashed);
            Assert.True(repair?.LostARecord);
            File.WriteAllText(path, repair!.Value.Text);
            File.AppendAllText(path, Continued(File.ReadAllText(path)));

            var read = RunJournal.Parse(File.ReadAllText(path));

            Assert.Equal([-1, 0, 1, 2, 3, 4, 5], read.Entries.Select(entry => entry.Seq));

            // And the decisions the crash did not touch are the ones that were written.
            Assert.Equal(
                Played().Journal.Entries.Take(5).Select(RunJournal.RenderEntry),
                read.Entries.Take(5).Select(RunJournal.RenderEntry));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>The two decisions a resumed session goes on to record.</summary>
    private static string Continued(string journal)
    {
        var resumed = RunCapture.Resume(RunJournal.Parse(journal), Digest(3));
        return RunJournal.RenderEntry(resumed.Record(
                   ActionVerb.PlayCard, Args(("card_id", "CARD.DEFEND_IRONCLAD"), ("hand_index", "0")),
                   InFight(2, turn: 2, enemyHp: 30, hp: 58), Digest(4))) +
               RunJournal.RenderEntry(resumed.Record(
                   ActionVerb.EndTurn, Args(), InFight(2, turn: 3, enemyHp: 30, hp: 58), Digest(5)));
    }

    /// <summary>
    /// A crash that took only the newline off a finished entry does not cost that
    /// entry.
    ///
    /// The reader keeps it - the line deserializes - so a repair that cut it back would
    /// delete a decision the capture had already resumed past, leaving a gap in the seq
    /// numbers that refuses the journal two sessions later. One rule decides whether
    /// that final line is a record, and both of them ask it.
    /// </summary>
    [Fact]
    public void AJournalWhoseFinalEntryLostOnlyItsNewlineKeepsThatEntry()
    {
        var whole = Played().Journal.Render();

        var repair = RunJournal.RepairTruncatedTail(whole[..^1]);

        Assert.False(repair?.LostARecord);
        Assert.Equal(whole, repair!.Value.Text);
        Assert.Equal(
            [-1, 0, 1, 2, 3, 4],
            RunJournal.Parse(repair.Value.Text).Entries.Select(entry => entry.Seq));
    }

    /// <summary>A journal that already ends on a complete line is left exactly as it
    /// is: there is nothing a crash cut short.</summary>
    [Fact]
    public void AJournalEndingOnACompleteLineNeedsNoRepair()
    {
        Assert.Null(RunJournal.RepairTruncatedTail(Played().Journal.Render()));
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
    /// A recording of a run that is over, which is the only kind
    /// <see cref="RunCapture.ToManifest"/> answers for: how a run ended is not a value
    /// it may guess.
    /// </summary>
    private static RunCapture Ended()
    {
        var capture = Played();
        capture.Finish("abandoned");
        return capture;
    }

    /// <summary>
    /// A short run: Neow, a map move into a fight, two cards and an ended turn either
    /// side of a second turn, and the killing blow.
    /// </summary>
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

    private static ReplayManifest Verified(RunCapture capture)
    {
        var manifest = capture.ToManifest();
        return manifest with
        {
            Verification = new VerificationReport
            {
                Status = VerificationStatus.Verified,
                ArbiterVersion = "test",
                Preflight = new PreflightResult(true, []),
                Trace = capture.Trace,
                Boundaries =
                [
                    .. manifest.Boundaries.Select(boundary => boundary with
                    {
                        Digest = Fact<string>.Engine(boundary.Digest.Value),
                    }),
                ],
            },
        };
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
            [new LocalMod("Runmobile", "Runmobile", "0.1.0", AffectsGameplay: false, "Loaded")],
            RecordedPatchRoster.HostOnly()),
    };

    /// <summary>A reading taken between fights, on a floor.</summary>
    private static IReadOnlyDictionary<string, string> Floor(int floor) => new Dictionary<string, string>(
        StringComparer.Ordinal)
    {
        ["combat.in_progress"] = "false",
        ["combat.outcome"] = "none",
        ["run.total_floor"] = floor.ToString(CultureInfo.InvariantCulture),
        ["run.map_coord"] = $"r{floor.ToString(CultureInfo.InvariantCulture)}c3",
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

    /// <summary>A distinct digest per decision, so a test can say which moment a
    /// boundary or a resumed session is standing at.</summary>
    private static string Digest(int seq) =>
        "sha256:" + (seq + 1).ToString("x2", CultureInfo.InvariantCulture).PadLeft(64, 'a');

    private static IReadOnlyDictionary<string, string> Args(params (string Key, string Value)[] args) =>
        args.ToDictionary(arg => arg.Key, arg => arg.Value, StringComparer.Ordinal);
}

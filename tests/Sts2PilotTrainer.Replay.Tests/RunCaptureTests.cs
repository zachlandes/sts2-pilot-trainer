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

    // ── Where the game saved ───────────────────────────────────────────────────
    //
    // The game saves at every map-point arrival, every fight won and every ancient
    // event finished, and Continue restores the latest of them. A resume that comes
    // back to that save is the game working as designed - what was done after it is
    // re-offered - and is continuous with those decisions kept as the branch the
    // restore discarded, whatever room the save was taken in. A resume behind it is
    // a reload of an older save, and a resume nowhere in the history is a hole.

    /// <summary>A purchase made after the shop arrival's save, quit, continue: the
    /// game re-stocks the shelf and puts the gold back, and the recording is still
    /// continuous, with the purchase as the branch.</summary>
    [Fact]
    public void AReturnToTheLatestSaveInAShopIsTheGamesOwnRollback()
    {
        var capture = AtTheShop();
        var bought = new Dictionary<string, string>(Shop(3, hp: 58), StringComparer.Ordinal)
        {
            ["player.gold"] = "63",
        };
        capture.Record(
            ActionVerb.ShopPurchase, Args(("kind", "character_card"), ("card_id", "CARD.CLEAVE"), ("option_index", "2")),
            bought, Digest(6));

        var resumed = RunCapture.Resume(RunJournal.Parse(capture.Journal.Render()), Shop(3, hp: 58), Digest(5));

        Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
        Assert.Empty(resumed.Refusals);
        Assert.Equal(6, resumed.NextSeq);
        Assert.Equal(5, resumed.LatestSavePointSeq);
        var branch = Assert.Single(resumed.Discarded);
        Assert.False(branch.Reload);
        Assert.Equal(5, branch.RollbackToSeq);
        Assert.Equal(ActionVerb.ShopPurchase, Assert.Single(branch.Actions).Verb);

        // And the manifest says where the saves were, as captured facts.
        resumed.Finish("abandoned");
        var manifest = resumed.ToManifest();
        Assert.Equal([0, 1, 4, 5], manifest.Source.Native!.SavePoints!.Select(point => point.AfterSeq));
        Assert.All(manifest.Source.Native.SavePoints!, point =>
        {
            Assert.True(point.Saved.Value);
            Assert.Equal(FactSource.Captured, point.Saved.Source);
            Assert.Equal(point.AfterSeq, point.Saved.Evidence?.ActionOrdinal);
        });
        var validation = ManifestValidator.Validate(manifest);
        Assert.True(validation.IsValid, validation.Describe());
    }

    /// <summary>A resume behind the latest save is a reload of an older one - a
    /// backup, a cloud copy - and the recording is rewound: whole, and never
    /// shareable.</summary>
    [Fact]
    public void AReturnToAnOlderSaveThanTheLatestIsAReload()
    {
        var capture = AtTheShop();

        var resumed = RunCapture.Resume(RunJournal.Parse(capture.Journal.Render()), Won(2, hp: 58), Digest(4));

        Assert.Equal(NativeSource.RewoundContinuity, resumed.Continuity);
        Assert.Contains("latest save (after decision 5)", resumed.Refusal!, StringComparison.Ordinal);
        Assert.Equal(5, resumed.NextSeq);
        var branch = Assert.Single(resumed.Discarded);
        Assert.True(branch.Reload);
        Assert.Equal(4, branch.RollbackToSeq);
        Assert.Equal(4, resumed.LatestSavePointSeq);
    }

    [Fact]
    public void AResumeNowhereInTheHistoryIsStillAHole()
    {
        var resumed = RunCapture.Resume(
            RunJournal.Parse(AtTheShop().Journal.Render()), Floor(9), "sha256:" + new string('e', 64));

        Assert.Equal(NativeSource.BrokenContinuity, resumed.Continuity);
        Assert.Equal(RunCaptureState.Broken, resumed.State);
    }

    /// <summary>The Neow re-offer: the blessing was answered and the game came back
    /// with it unanswered, because the save the finished event asked for never
    /// reached the disk and Continue restored the run-start save. No save point is on
    /// the file, so the run-start save is the latest, and the recording is
    /// continuous with the first answer kept as the branch.</summary>
    [Fact]
    public void ANeowReOfferWhoseFinishSaveNeverLandedIsTheGamesOwnRollback()
    {
        var first = RunCapture.Begin(Start());
        first.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            Floor(1), Digest(0));

        var resumed = Resume(RunJournal.Parse(first.Journal.Render()), Digest(-1));

        Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
        Assert.Empty(resumed.Refusals);
        Assert.Equal(0, resumed.NextSeq);
        var branch = Assert.Single(resumed.Discarded);
        Assert.False(branch.Reload);
        Assert.Equal(-1, branch.RollbackToSeq);

        resumed.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "2"), ("option_key", "NEOW.OTHER")),
            Floor(1), Digest(20));
        Saved(resumed);
        resumed.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "1"), ("column", "3")),
            InFight(2, turn: 1), Digest(21));
        Saved(resumed);
        resumed.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.BASH"), ("hand_index", "0")), Won(2, hp: 58), Digest(22));
        resumed.Finish("abandoned");

        var manifest = resumed.ToManifest();
        Assert.Equal(NativeSource.ContinuousContinuity, manifest.Source.Native!.Continuity);
        Assert.Equal([0, 1], manifest.Source.Native.SavePoints!.Select(point => point.AfterSeq));
        var validation = ManifestValidator.Validate(manifest);
        Assert.True(validation.IsValid, validation.Describe());
    }

    /// <summary>
    /// A journal an earlier build wrote is refused on resume, whatever it holds.
    ///
    /// Every reading a version-4 journal took after a fight carried that fight until
    /// the next one, and every complete digest on those lines hashes it; the
    /// projection now carries nothing of a fight outside a live one. A recording
    /// continued from such a journal would be two projections in one file, so the
    /// resume path refuses it the way it always refused a schema it does not read,
    /// and the run is simply not continued as a recording.
    /// </summary>
    [Theory]
    [InlineData("sts2-pilot-trainer/run-journal/v4")]
    [InlineData("sts2-pilot-trainer/run-journal/v3")]
    [InlineData("sts2-pilot-trainer/run-journal/v2")]
    [InlineData("sts2-pilot-trainer/run-journal/v1")]
    public void AJournalAnEarlierBuildWroteIsRefusedOnResume(string schema)
    {
        var text = Played().Journal.Render().Replace(RunJournal.Schema, schema, StringComparison.Ordinal);

        var refusal = Assert.Throws<ManifestException>(() => RunJournal.Parse(text));

        Assert.Contains($"declares schema '{schema}'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(RunJournal.Schema, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>The save-point line survives a quit and comes back where it was; a
    /// rollback drops the ones on the branch it discards, because the game's restore
    /// of the older save took them off the disk.</summary>
    [Fact]
    public void SavePointsAreKeptByTheJournalAndDroppedWithTheBranchARollbackDiscards()
    {
        var capture = AtTheShop();
        var read = RunJournal.Parse(capture.Journal.Render());
        Assert.Equal([0, 1, 4, 5], read.SavePoints.Select(point => point.AfterSeq));
        Assert.Equal(812_340, read.SavePoints[2].RunClockMs);

        var rewound = RunCapture.Resume(read, Won(2, hp: 58), Digest(4));
        Assert.Equal([0, 1, 4], rewound.SavePoints.Select(point => point.AfterSeq));

        var again = RunJournal.Parse(rewound.Journal.Render());
        Assert.Equal([0, 1, 4], again.SavePoints.Select(point => point.AfterSeq));
        Assert.Equal(4, again.LatestSavePointSeq);
    }

    [Fact]
    public void ASavePointNamingADecisionTheHistoryDoesNotHoldIsRefused()
    {
        var capture = Played();
        var refusal = Assert.Throws<ManifestException>(() => capture.MarkSavePoint(5));
        Assert.Contains("has not made", refusal.Message, StringComparison.Ordinal);

        var text = capture.Journal.Render() + RunJournal.RenderSavePoint(new JournalSavePoint { AfterSeq = 9 }) +
                   RunJournal.RenderEntry(capture.Journal.Entries[^1]);
        var unreadable = Assert.Throws<ManifestException>(() => RunJournal.Parse(text));
        Assert.Contains("names decision 9", unreadable.Message, StringComparison.Ordinal);
    }

    /// <summary>A rollback line claiming to be the game's own is held to the latest
    /// save on the file, so a journal cannot be edited into a continuous one.</summary>
    [Fact]
    public void ARollbackLineClaimingTheGamesOwnIsHeldToTheLatestSaveOnTheFile()
    {
        var capture = AtTheShop();
        var claimed = RunJournal.RenderRollback(new JournalRollback
        {
            RollbackToSeq = 4,
            RollbackToDigest = Digest(4),
            DiscardedFromSeq = 5,
            DiscardedThroughSeq = 5,
        });

        var refusal = Assert.Throws<ManifestException>(() => RunJournal.Parse(capture.Journal.Render() + claimed));

        Assert.Contains("latest save on the file is after decision 5", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>The validator holds a branch the game's own rollback made to a save
    /// the recording lists, so a reload's branch from somewhere the game never saved
    /// cannot be relabelled as the game's to make a rewound recording shareable.
    /// Which of the listed saves was the latest when the run came back is the
    /// journal's knowledge, and its reader holds the rollback line to it.</summary>
    [Fact]
    public void TheValidatorHoldsTheGamesOwnBranchToAListedSavePoint()
    {
        var capture = AtTheShop();
        var resumed = Resume(RunJournal.Parse(capture.Journal.Render()), Digest(3));
        Assert.Equal(NativeSource.RewoundContinuity, resumed.Continuity);
        resumed.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.STRIKE_IRONCLAD"), ("hand_index", "1")),
            Won(2, hp: 58), Digest(40));
        Saved(resumed);
        resumed.Finish("abandoned");
        var rewound = resumed.ToManifest();
        var accepted = ManifestValidator.Validate(rewound);
        Assert.True(accepted.IsValid, accepted.Describe());

        var branch = Assert.Single(rewound.Source.Native!.Discarded!);
        var relabelled = rewound with
        {
            Source = rewound.Source with
            {
                Native = rewound.Source.Native with
                {
                    Continuity = NativeSource.ContinuousContinuity,
                    Discarded = [branch with { Reload = false }],
                },
            },
        };

        var result = ManifestValidator.Validate(relabelled);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, problem =>
            problem.Contains("which source.native.save_points does not list", StringComparison.Ordinal));

        // Nor can the branch be given a save of its own that nothing took off the
        // list: a branch's save point leaves the continued history only with a
        // later reload.
        var forged = relabelled with
        {
            Source = relabelled.Source with
            {
                Native = relabelled.Source.Native! with
                {
                    Discarded =
                    [
                        branch with
                        {
                            Reload = false,
                            SavePoint = new SavePoint
                            {
                                AfterSeq = branch.RollbackToSeq,
                                Saved = Fact<bool>.Captured(true, FactEvidence.AtActionOrdinal(branch.RollbackToSeq)),
                            },
                        },
                    ],
                },
            },
        };

        var forgedResult = ManifestValidator.Validate(forged);

        Assert.False(forgedResult.IsValid);
        Assert.Contains(forgedResult.Problems, problem =>
            problem.Contains("no later reload rewound behind", StringComparison.Ordinal));
    }

    /// <summary>
    /// The game's own rollback at the shop, then a reload behind it: the purchase
    /// was undone by Continue at the arrival's save, the run went on, and a later
    /// Continue from an older backup restored the fight-won save. The reload takes
    /// the arrival's save off the continued history, so the first branch carries the
    /// save it returned to itself, and the rewound recording still validates - whole
    /// and the player's to play from - with the branch's boundary read from the
    /// branch the reload discarded.
    /// </summary>
    [Fact]
    public void AReloadBehindAnEarlierOwnRollbackLeavesARewoundRecordingTheValidatorTakes()
    {
        var capture = AtTheShop();
        var bought = new Dictionary<string, string>(Shop(3, hp: 58), StringComparer.Ordinal)
        {
            ["player.gold"] = "63",
        };
        capture.Record(
            ActionVerb.ShopPurchase, Args(("kind", "character_card"), ("card_id", "CARD.CLEAVE"), ("option_index", "2")),
            bought, Digest(6));

        var continued = RunCapture.Resume(RunJournal.Parse(capture.Journal.Render()), Shop(3, hp: 58), Digest(5));
        Assert.Equal(NativeSource.ContinuousContinuity, continued.Continuity);
        var own = Assert.Single(continued.Discarded);
        Assert.Equal(5, own.SavePoint?.AfterSeq);
        continued.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "3"), ("column", "3")),
            InFight(4, turn: 1), Digest(7));
        Saved(continued);

        var rewound = Resume(RunJournal.Parse(continued.Journal.Render()), Digest(4));
        Assert.Equal(NativeSource.RewoundContinuity, rewound.Continuity);
        Assert.Equal([0, 1, 4], rewound.SavePoints.Select(point => point.AfterSeq));
        Assert.Equal(2, rewound.Discarded.Count);
        Assert.Equal(5, rewound.Discarded[0].SavePoint?.AfterSeq);
        Assert.True(rewound.Discarded[1].Reload);
        Assert.Null(rewound.Discarded[1].SavePoint);
        rewound.Record(ActionVerb.ClaimReward, Args(("reward_type", "gold")), Won(2, hp: 58), Digest(30));
        rewound.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "2"), ("column", "2")),
            InFight(3, turn: 1), Digest(31));
        Saved(rewound);
        rewound.Finish("abandoned");

        var manifest = rewound.ToManifest();
        var validation = ManifestValidator.Validate(manifest);
        Assert.True(validation.IsValid, validation.Describe());
        Assert.Equal([0, 1, 4, 6], manifest.Source.Native!.SavePoints!.Select(point => point.AfterSeq));
        var first = manifest.Source.Native.Discarded![0];
        Assert.False(first.Reload);
        Assert.Equal(5, first.SavePoint?.AfterSeq);
        Assert.Equal(5, first.SavePoint?.Saved.Evidence?.ActionOrdinal);

        var written = ManifestJson.Deserialize(ManifestJson.Serialize(manifest));
        Assert.Equal(5, written.Source.Native!.Discarded![0].SavePoint?.AfterSeq);
        Assert.Null(written.Source.Native.Discarded[1].SavePoint);
        var reread = ManifestValidator.Validate(written);
        Assert.True(reread.IsValid, reread.Describe());
    }

    /// <summary>
    /// The game's own rollback at a fight's arrival, then a reload behind the shop
    /// before it. The first branch was played from the shop arrival, the purchase and
    /// the move to the fight; the reload made all three its own branch and the
    /// continued history remade them. The recording says which decisions the first
    /// branch was played from, and a branch the reload holds short of one of them is
    /// refused by name rather than filled in from the remade history.
    /// </summary>
    [Fact]
    public void ABranchIsHeldToTheHistoryItWasPlayedFromAndNotToTheOneRemadeBehindIt()
    {
        var capture = AtTheShop();
        var bought = new Dictionary<string, string>(Shop(3, hp: 58), StringComparer.Ordinal)
        {
            ["player.gold"] = "63",
        };
        capture.Record(
            ActionVerb.ShopPurchase, Args(("kind", "character_card"), ("card_id", "CARD.CLEAVE"), ("option_index", "2")),
            bought, Digest(6));
        capture.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "3"), ("column", "3")),
            InFight(4, turn: 1), Digest(7));
        Saved(capture);
        capture.Record(ActionVerb.EndTurn, Args(), InFight(4, turn: 2), Digest(8));

        var continued = Resume(RunJournal.Parse(capture.Journal.Render()), Digest(7));
        Assert.Equal(NativeSource.ContinuousContinuity, continued.Continuity);
        Assert.Equal(7, Assert.Single(continued.Discarded).RollbackToSeq);
        continued.Record(ActionVerb.EndTurn, Args(), InFight(4, turn: 2, hp: 60), Digest(9));

        var rewound = Resume(RunJournal.Parse(continued.Journal.Render()), Digest(4));
        Assert.Equal(NativeSource.RewoundContinuity, rewound.Continuity);
        rewound.Record(ActionVerb.ClaimReward, Args(("reward_type", "gold")), Won(2, hp: 58), Digest(30));
        rewound.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "2"), ("column", "2")),
            InFight(3, turn: 1), Digest(31));
        Saved(rewound);
        rewound.Finish("abandoned");

        var manifest = rewound.ToManifest();
        var validation = ManifestValidator.Validate(manifest);
        Assert.True(validation.IsValid, validation.Describe());
        var branches = manifest.Source.Native!.Discarded!;
        Assert.Equal(2, branches.Count);
        Assert.Equal(7, branches[0].RollbackToSeq);
        Assert.Equal([5, 6, 7, 8], branches[1].Actions.Select(action => action.Seq));

        // The first branch was played from the reload's decisions 5-7, and the
        // continued history's 5-6 are the remade ones.
        var prefix = DiscardedBranchHistory.PrefixOf(branches, 0, manifest.Actions);
        Assert.Equal(Enumerable.Range(0, 8), prefix.Select(action => action.Seq));
        Assert.Equal(ActionVerb.MapMove, prefix[5].Verb);
        Assert.Equal("1", prefix[5].Args["column"]);
        Assert.Equal(ActionVerb.ShopPurchase, prefix[6].Verb);
        Assert.Equal(ActionVerb.MapMove, prefix[7].Verb);
        Assert.Equal(ActionVerb.ClaimReward, manifest.Actions[5].Verb);
        Assert.Equal("2", manifest.Actions[6].Args["column"]);

        var short_ = manifest with
        {
            Source = manifest.Source with
            {
                Native = manifest.Source.Native with
                {
                    Discarded =
                    [
                        branches[0],
                        branches[1] with
                        {
                            Actions = branches[1].Actions.Where(action => action.Seq != 6).ToList(),
                            Trace = branches[1].Trace with
                            {
                                Steps = branches[1].Trace.Steps.Where(step => step.Seq != 6).ToList(),
                            },
                        },
                    ],
                },
            },
        };
        var refused = ManifestValidator.Validate(short_);
        Assert.False(refused.IsValid);
        Assert.Contains(
            refused.Problems,
            problem => problem.Contains(
                "source.native.discarded[0] was played from a history that neither the continued history nor " +
                "a later branch holds at decision 6", StringComparison.Ordinal));
        Assert.Throws<ManifestException>(() => DiscardedBranchHistory.PrefixOf(short_.Source.Native!.Discarded!, 0, short_.Actions));
    }

    /// <summary>Neow answered, the first fight won, and a shop entered straight after
    /// it, with the game's saves where it takes them: after the event, at each
    /// arrival and at the fight's end.</summary>
    private static RunCapture AtTheShop()
    {
        var capture = Played();
        capture.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "2"), ("column", "1")),
            Shop(3, hp: 58), Digest(5));
        Saved(capture);
        return capture;
    }

    // ── The loot screen the game restores ──────────────────────────────────────
    //
    // The projection carries nothing of a fight outside a live one, so a run the game
    // restored from a save reads exactly as the recorder read the moment the save was
    // taken, and the complete digest places every honest Continue but one: the
    // fight-won save. The retail client rolls the rewards after the killing play has
    // settled and the save has been taken, and the restore rolls them again, so the
    // run comes back at the state the next decision began from. These hold the
    // resume to that one latitude and to nothing looser.

    /// <summary>The recorder's reading at a shop arrival after a won fight and the
    /// restored run's are the same state, digest for digest, and the resume places
    /// it without discarding anything.</summary>
    [Fact]
    public void AnArrivalAfterAWonFightResumesContinuouslyOnItsOwnDigest()
    {
        var capture = Played();
        capture.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "2"), ("column", "1")),
            Shop(3, hp: 58), Digest(5));

        var resumed = RunCapture.Resume(RunJournal.Parse(capture.Journal.Render()), Shop(3, hp: 58), Digest(5));

        Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
        Assert.Equal(RunCaptureState.Recording, resumed.State);
        Assert.Empty(resumed.Refusals);
        Assert.Empty(resumed.Discarded);
        Assert.Equal(6, resumed.NextSeq);
    }

    /// <summary>A reading that differs in anything at all - here a point of health,
    /// and with it the digest - is a moment the journal never saw.</summary>
    [Fact]
    public void AnArrivalThatDiffersInAPointOfHealthIsBroken()
    {
        var capture = Played();
        capture.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "2"), ("column", "1")),
            Shop(3, hp: 58), Digest(5));

        var resumed = RunCapture.Resume(
            RunJournal.Parse(capture.Journal.Render()), Shop(3, hp: 57), "sha256:" + new string('e', 64));

        Assert.Equal(NativeSource.BrokenContinuity, resumed.Continuity);
        Assert.Equal(RunCaptureState.Broken, resumed.State);
    }

    /// <summary>The fight-won save comes back to the loot screen with nothing claimed,
    /// at the state the killing play settled into, so a claim made before the quit
    /// was rolled back by the game. The match is the killing play, which is where
    /// the game saved, so this is the game's own return to its latest save:
    /// continuous, with the claim kept as the branch the restore discarded.</summary>
    [Fact]
    public void ALootScreenQuitAfterAClaimResumesContinuouslyAtTheFightWonSave()
    {
        var capture = Played();
        var claimed = new Dictionary<string, string>(Won(2, hp: 58), StringComparer.Ordinal) { ["player.gold"] = "118" };
        capture.Record(ActionVerb.ClaimReward, Args(("reward_type", "gold")), claimed, Digest(5));

        var resumed = RunCapture.Resume(RunJournal.Parse(capture.Journal.Render()), Won(2, hp: 58), Digest(4));

        Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
        Assert.Empty(resumed.Refusals);
        Assert.Equal(RunCaptureState.Recording, resumed.State);
        Assert.Equal(5, resumed.NextSeq);
        var branch = Assert.Single(resumed.Discarded);
        Assert.False(branch.Reload);
        Assert.Equal(4, branch.RollbackToSeq);
        Assert.Equal(ActionVerb.ClaimReward, Assert.Single(branch.Actions).Verb);
    }

    /// <summary>
    /// The loot screen as the retail client records it. The client rolls the rewards
    /// on its own clock, after the killing play has settled, so the play's own digest
    /// is of a state before the roll, and the claim after it begins from the state
    /// after; the game's restore of the fight-won save rolls the rewards again, so the
    /// run comes back at the claim's before-reading and never at the play's. That
    /// entry is matched through its successor's before-digest, and the recording is
    /// continuous with the claim as the branch.
    /// </summary>
    [Fact]
    public void ALootScreenQuitInTheClientIsMatchedThroughTheClaimsBeforeReading()
    {
        var capture = RunCapture.Begin(Start());
        capture.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            Floor(1), Digest(0));
        Saved(capture);
        capture.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "1"), ("column", "3")),
            InFight(2, turn: 1), Digest(1));
        Saved(capture);
        capture.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.STRIKE_IRONCLAD"), ("hand_index", "1")),
            new StateReading(InFight(2, turn: 2, enemyHp: 6, hp: 58), Digest(3)),
            new StateReading(Won(2, hp: 58), Digest(4)));
        Saved(capture);
        var claimed = new Dictionary<string, string>(Won(2, hp: 58), StringComparer.Ordinal) { ["player.gold"] = "118" };
        capture.Record(
            ActionVerb.ClaimReward, Args(("reward_type", "gold")),
            new StateReading(Won(2, hp: 58), Digest(6)),
            new StateReading(claimed, Digest(5)));

        var resumed = RunCapture.Resume(RunJournal.Parse(capture.Journal.Render()), Won(2, hp: 58), Digest(6));

        Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
        Assert.Equal(3, resumed.NextSeq);
        var branch = Assert.Single(resumed.Discarded);
        Assert.False(branch.Reload);
        Assert.Equal(2, branch.RollbackToSeq);

        // A state that is none of those readings, however alike its sample, is not
        // the loot screen the journal knows.
        var elsewhere = RunCapture.Resume(
            RunJournal.Parse(capture.Journal.Render()), Won(2, hp: 58), "sha256:" + new string('e', 64));
        Assert.Equal(NativeSource.BrokenContinuity, elsewhere.Continuity);
    }

    /// <summary>With no decision made on the loot screen before the quit, the
    /// journal holds no reading of the rolled rewards, and the sample is what there
    /// is to compare: continuous, nothing discarded.</summary>
    [Fact]
    public void ALootScreenQuitBeforeAnyClaimIsMatchedBySampleWhereNothingExactIsHeld()
    {
        var capture = RunCapture.Begin(Start());
        capture.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            Floor(1), Digest(0));
        Saved(capture);
        capture.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "1"), ("column", "3")),
            InFight(2, turn: 1), Digest(1));
        Saved(capture);
        capture.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.STRIKE_IRONCLAD"), ("hand_index", "1")),
            new StateReading(InFight(2, turn: 2, enemyHp: 6, hp: 58), Digest(3)),
            new StateReading(Won(2, hp: 58), Digest(4)));
        Saved(capture);

        var resumed = RunCapture.Resume(
            RunJournal.Parse(capture.Journal.Render()), Won(2, hp: 58), "sha256:" + new string('e', 64));

        Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
        Assert.Empty(resumed.Discarded);
        Assert.Equal(3, resumed.NextSeq);

        // An entry that did not end a fight gets no such latitude: its settled
        // reading is exact, and an event page the game rolled back differs there
        // in a random stream the sample cannot see. The arrival's own digest places
        // the return; a digest nothing holds is a moment the journal never saw.
        var atAnEvent = RunCapture.Begin(Start());
        atAnEvent.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            Floor(1), Digest(0));
        atAnEvent.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "1"), ("column", "3")),
            Shop(3, hp: 58), Digest(5));
        Saved(atAnEvent);
        atAnEvent.Record(
            ActionVerb.ChooseEventOption,
            Args(("event_id", "EVENT.TEST"), ("option_index", "0"), ("option_key", "EVENT.PAGE")),
            Shop(3, hp: 58), Digest(6));

        var pageRolledBack = RunCapture.Resume(
            RunJournal.Parse(atAnEvent.Journal.Render()), Shop(3, hp: 58), Digest(5));
        Assert.Equal(NativeSource.ContinuousContinuity, pageRolledBack.Continuity);
        var page = Assert.Single(pageRolledBack.Discarded);
        Assert.Equal(1, page.RollbackToSeq);
        Assert.Equal(ActionVerb.ChooseEventOption, Assert.Single(page.Actions).Verb);

        var pageSomewhereElse = RunCapture.Resume(
            RunJournal.Parse(atAnEvent.Journal.Render()), Shop(3, hp: 58), "sha256:" + new string('e', 64));
        Assert.Equal(NativeSource.BrokenContinuity, pageSomewhereElse.Continuity);
    }

    /// <summary>The same quit where the fight-won save never landed on the disk - the
    /// journal holds no save point for it - is a return to the arrival save the game
    /// took before the fight, behind the recorder's latest: a reload that rewound
    /// the run, whole and playable and never shareable, and not a hole.</summary>
    [Fact]
    public void ALootScreenQuitWhoseSaveNeverLandedResumesAsARewindNotAHole()
    {
        var capture = RunCapture.Begin(Start());
        capture.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            Floor(1), Digest(0));
        Saved(capture);
        capture.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "1"), ("column", "3")),
            InFight(2, turn: 1), Digest(1));
        Saved(capture);
        capture.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.STRIKE_IRONCLAD"), ("hand_index", "1")),
            Won(2, hp: 58), Digest(4));
        var claimed = new Dictionary<string, string>(Won(2, hp: 58), StringComparer.Ordinal) { ["player.gold"] = "118" };
        capture.Record(ActionVerb.ClaimReward, Args(("reward_type", "gold")), claimed, Digest(5));

        var resumed = RunCapture.Resume(RunJournal.Parse(capture.Journal.Render()), Won(2, hp: 58), Digest(4));

        Assert.Equal(NativeSource.RewoundContinuity, resumed.Continuity);
        Assert.Equal(RunCaptureState.Recording, resumed.State);
        Assert.Equal(3, resumed.NextSeq);
        var branch = Assert.Single(resumed.Discarded);
        Assert.True(branch.Reload);
        Assert.Equal(2, branch.RollbackToSeq);
    }

    /// <summary>Two readings that agree in every sampled field while their complete
    /// digests disagree differ in something the sample does not carry - a random
    /// stream's position, the draw order - and are not the same moment.</summary>
    [Fact]
    public void AgreeingSamplesDoNotPassAsTheSameMoment()
    {
        var resumed = RunCapture.Resume(Played().Journal, Floor(1), "sha256:" + new string('e', 64));

        Assert.Equal(NativeSource.BrokenContinuity, resumed.Continuity);
    }

    [Fact]
    public void DifferencesNameEachFieldOnceInOrder()
    {
        var differences = ReplayTrace.Differences(Won(2, hp: 58), Shop(2, hp: 57));

        Assert.Equal(
            ["combat.outcome: victory -> none", "player.hp: 58 -> 57", "run.map_coord: r2c3 -> r2c1"],
            differences);
    }

    [Fact]
    public void ASessionThatResumesWhereItLeftOffCarriesOnRecording()
    {
        var journal = Played().Journal;

        var resumed = Resume(journal, Digest(4));

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
        Saved(capture);
        capture.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "1"), ("column", "3")),
            InFight(2, turn: 1), Digest(1));
        Saved(capture);
        capture.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.BASH"), ("hand_index", "0")),
            InFight(2, turn: 1, enemyHp: 30), Digest(2));
        capture.Record(ActionVerb.EndTurn, Args(), InFight(2, turn: 2, enemyHp: 30, hp: 58), Digest(3));

        var resumed = Resume(RunJournal.Parse(capture.Journal.Render()), Digest(1));

        Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
        Assert.Equal(RunCaptureState.Recording, resumed.State);
        Assert.Null(resumed.Refusal);
        Assert.Equal(2, resumed.NextSeq);
        Assert.NotNull(resumed.ResumptionRecord);
        Assert.Equal(1, resumed.LatestSavePointSeq);

        var persisted = RunJournal.Parse(resumed.Journal.Render());
        var discarded = Assert.Single(persisted.Discarded);
        Assert.Equal([2, 3], discarded.Entries.Select(entry => entry.Seq));

        resumed.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.STRIKE_IRONCLAD"), ("hand_index", "1")),
            Won(2, hp: 58), Digest(4));

        resumed = Resume(RunJournal.Parse(resumed.Journal.Render()), Digest(4));
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
        Saved(capture);
        capture.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.BASH"), ("hand_index", "0")),
            InFight(3, enemyHp: 30), Digest(6));

        var resumed = Resume(RunJournal.Parse(capture.Journal.Render()), Digest(5));
        Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
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
        Saved(capture);
        capture.Record(
            ActionVerb.ChooseEventOption,
            Args(("event_id", "EVENT.TEST"), ("option_index", "0"), ("option_key", "EVENT.FIGHT")),
            InFight(3), Digest(6));
        capture.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.BASH"), ("hand_index", "0")),
            InFight(3, enemyHp: 30), Digest(7));

        var resumed = Resume(RunJournal.Parse(capture.Journal.Render()), Digest(5));
        Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
        resumed.Record(
            ActionVerb.ChooseEventOption,
            Args(("event_id", "EVENT.TEST"), ("option_index", "0"), ("option_key", "EVENT.FIGHT")),
            InFight(3), Digest(6));
        resumed.Finish("abandoned");
        var verified = Verified(resumed);

        var result = ManifestValidator.Validate(verified);

        Assert.True(result.IsValid, result.Describe());
        var discarded = Assert.Single(verified.Source.Native!.Discarded!);
        Assert.False(discarded.Reload);
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
        Saved(capture);
        capture.Record(
            ActionVerb.ChooseEventOption,
            Args(("event_id", "EVENT.TEST"), ("option_index", "0"), ("option_key", "EVENT.FIGHT")),
            InFight(3), Digest(6));
        capture.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.BASH"), ("hand_index", "0")),
            InFight(3, enemyHp: 30), Digest(7));

        var resumed = Resume(RunJournal.Parse(capture.Journal.Render()), Digest(5));
        Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
        resumed.Record(
            ActionVerb.ChooseEventOption,
            Args(("event_id", "EVENT.TEST"), ("option_index", "1"), ("option_key", "EVENT.SAFE")),
            Floor(3), Digest(60));
        resumed = Resume(RunJournal.Parse(resumed.Journal.Render()), Digest(60));
        Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
        resumed.Finish("abandoned");
        var verified = Verified(resumed);

        var result = ManifestValidator.Validate(verified);

        Assert.True(result.IsValid, result.Describe());
        Assert.Single(RunCoverage.Of(verified.Verification!.Trace!).Fights);
        var discarded = Assert.Single(verified.Source.Native!.Discarded!);
        Assert.Contains(discarded.Trace.Steps, step =>
            step.Seq == 6 && step.After["combat.outcome"] == "in_progress");
    }

    /// <summary>
    /// A reload that rewound the run behind what was recorded costs the recording its
    /// sharing and the watch nothing, and keeps what the reload abandoned.
    ///
    /// The player quit and continued from an earlier save. The decisions past that
    /// point were played and then abandoned, so they go where the game's own rollback
    /// puts an unwound fight - a discarded branch, marked as the reload's - and the
    /// replayable history resumes at the decision the game came back to. That is what
    /// keeps the recording one the engine replays. What it can never be is shared,
    /// which <see cref="NativeSource.RewoundContinuity"/> says and the share form and
    /// the gate both read. What has not happened is the recorder giving up: it can
    /// account for every decision from here on exactly as it could before.
    /// </summary>
    [Fact]
    public void ASessionThatResumesAtAnEarlierNonFightBoundaryKeepsRecordingAndCannotBeShared()
    {
        var resumed = Resume(Played().Journal, Digest(0));

        Assert.Equal(NativeSource.RewoundContinuity, resumed.Continuity);
        Assert.Equal(RunCaptureState.Recording, resumed.State);
        Assert.Contains("no longer be shared", resumed.Refusal!, StringComparison.Ordinal);
        Assert.True(Assert.Single(resumed.Refusals).WatchContinues);
        Assert.Equal(1, resumed.NextSeq);

        var branch = Assert.Single(resumed.Discarded);
        Assert.True(branch.Reload);
        Assert.Equal(0, branch.RollbackToSeq);
        Assert.Equal(Digest(0), branch.RollbackToDigest);
        Assert.Equal([1, 2, 3, 4], branch.Actions.Select(action => action.Seq));
    }

    /// <summary>
    /// The run the captain save-scummed, as a regression.
    ///
    /// He answered Neow, quit to the menu and continued, and the game gave him the
    /// blessing to choose again: it came back at the reading before decision 0, which
    /// is behind everything the recorder had written. The build he was on wrote a
    /// refusal, stopped the watch, and put RECORDING STOPPED on the overlay while
    /// carrying on writing decisions into the journal underneath it. The recorder now
    /// keeps recording and says the one thing that changed - the run cannot be shared.
    /// </summary>
    [Fact]
    public void AReloadThatUndoesTheNeowBlessingKeepsRecordingTheRestOfTheRun()
    {
        var first = RunCapture.Begin(Start());
        first.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            Floor(1), Digest(0));
        // Neow is an ancient event, and the game saved when it finished; the run
        // that came back at its start was restored from the older run-start save.
        Saved(first);

        var resumed = Resume(RunJournal.Parse(first.Journal.Render()), Digest(-1));

        Assert.Equal(RunCaptureState.Recording, resumed.State);
        Assert.Equal(NativeSource.RewoundContinuity, resumed.Continuity);
        Assert.Contains("latest save (after decision 0)", resumed.Refusal!, StringComparison.Ordinal);
        Assert.Equal(0, resumed.NextSeq);

        // The blessing chosen a second time, and the run played on from it.
        resumed.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "2"), ("option_key", "NEOW.OTHER")),
            Floor(1), Digest(20));
        resumed.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "1"), ("column", "3")),
            InFight(2, turn: 1), Digest(21));
        resumed.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.BASH"), ("hand_index", "0")), Won(2, hp: 58), Digest(22));

        Assert.Equal(RunCaptureState.Recording, resumed.State);
        Assert.Equal(3, resumed.NextSeq);

        resumed.Finish("abandoned");
        var manifest = resumed.ToManifest();
        Assert.Equal(NativeSource.RewoundContinuity, manifest.Source.Native!.Continuity);
        Assert.False(manifest.Source.Native.IsContinuous);
        Assert.True(manifest.Source.Native.HistoryIsWhole);

        // The history is the run as it stands after the reload, and the first answer
        // is kept beside it as what the reload abandoned.
        Assert.Equal([0, 1, 2], manifest.Actions.Select(action => action.Seq));
        Assert.Equal("2", manifest.Actions[0].Args["option_index"]);
        Assert.Contains(manifest.Boundaries, boundary => boundary.Kind == ReplayBoundary.CombatStartKind);
        var branch = Assert.Single(manifest.Source.Native.Discarded!);
        Assert.True(branch.Reload);
        Assert.Equal(-1, branch.RollbackToSeq);
        Assert.Equal("0", Assert.Single(branch.Actions).Args["option_index"]);
        Assert.Contains("after a reload rewound it", manifest.Source.Coverage, StringComparison.Ordinal);

        // Replayable, and never shareable: the validator takes the whole history and
        // the gate is what refuses it.
        var validation = ManifestValidator.Validate(manifest);
        Assert.True(validation.IsValid, validation.Describe());
    }

    /// <summary>
    /// The reload's rollback and refusal survive the journal, so the session after the
    /// next quit resumes the rewound history rather than the abandoned one - and a
    /// rewind past a finished fight takes that fight's bookmark with it, because the
    /// continued run will deal the same ordinal to a different fight.
    ///
    /// The press stays on the file, so the fight is dealt again and won without a
    /// press before the second resume: a session reading the line back onto the
    /// re-dealt fight would be marking a fight nobody bookmarked.
    /// </summary>
    [Fact]
    public void AReloadsRollbackIsKeptByTheJournalAndDropsTheBookmarksItRewoundPast()
    {
        var played = Played();
        played.MarkBookmark(1, on: true, _ => { });
        Assert.True(played.IsBookmarked(1));

        var rewound = Resume(RunJournal.Parse(played.Journal.Render()), Digest(0));
        Assert.False(rewound.IsBookmarked(1));
        rewound.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "1"), ("column", "2")),
            InFight(2, turn: 1), Digest(30));
        rewound.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.BASH"), ("hand_index", "0")), Won(2, hp: 58), Digest(31));
        Assert.False(rewound.IsBookmarked(1));

        var again = Resume(RunJournal.Parse(rewound.Journal.Render()), Digest(31));

        Assert.Equal(NativeSource.RewoundContinuity, again.Continuity);
        Assert.Equal(RunCaptureState.Recording, again.State);
        Assert.Equal(3, again.NextSeq);
        Assert.Equal([0, 1, 2], again.Journal.Decisions.Select(entry => entry.Seq));
        Assert.True(Assert.Single(again.Discarded).Reload);
        Assert.False(again.IsBookmarked(1));
        Assert.Equal(Digest(31), again.Journal.Entries[^1].Digest);
        again.Finish("abandoned");
        Assert.Null(again.ToManifest().Source.Native!.Bookmarks);
    }

    /// <summary>The refusal is on the file before the rollback receipt, so a crash
    /// between the two leaves a journal the next session resumes - and rolls back
    /// again, from the same live digest - rather than one it cannot read.</summary>
    [Fact]
    public void AReloadsRefusalAloneOnTheFileResumesAndRollsBackAgain()
    {
        var rewound = Resume(RunJournal.Parse(Played().Journal.Render()), Digest(0));
        var text = rewound.Journal.Render();
        Assert.EndsWith(RunJournal.RenderRefusal(rewound.Refusals[0]) + rewound.ResumptionRecord, text);

        var interrupted = text[..^rewound.ResumptionRecord!.Length];
        var again = Resume(RunJournal.Parse(interrupted), Digest(0));

        Assert.Equal(NativeSource.RewoundContinuity, again.Continuity);
        Assert.Equal(RunCaptureState.Recording, again.State);
        Assert.Equal(1, again.NextSeq);
        Assert.True(Assert.Single(again.Discarded).Reload);
    }

    /// <summary>A hole outranks a rewind: a reload the recorder can place after one it
    /// could not does not make the history whole again.</summary>
    [Fact]
    public void AReloadAfterAHoleLeavesTheRecordingBroken()
    {
        var broken = Resume(Played().Journal, "sha256:" + new string('f', 64));
        Assert.Equal(RunCaptureState.Broken, broken.State);

        var again = Resume(RunJournal.Parse(broken.Journal.Render()), Digest(0));

        Assert.Equal(NativeSource.BrokenContinuity, again.Continuity);
        Assert.Equal(RunCaptureState.Broken, again.State);
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

        var resumed = Resume(RunJournal.Parse(capture.Journal.Render()), Digest(1));

        Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
        Assert.NotNull(resumed.Fight);
        Assert.Equal(FightCaptureState.Live, resumed.Fight!.State);
        Assert.False(resumed.Fight.HasOpenStep);
        Assert.Equal(Digest(1), resumed.Fight.CombatStartSnapshotDigest);
    }

    /// <summary>A resume the recorder cannot place in its own history is the other
    /// thing, and it stops the watch: nothing establishes what the run is from there,
    /// so there is no account left to go on keeping.</summary>
    [Fact]
    public void ASessionThatResumesSomewhereTheRecorderNeverSawIsBrokenToo()
    {
        var resumed = Resume(Played().Journal, Digest(77));

        Assert.Equal(NativeSource.BrokenContinuity, resumed.Continuity);
        Assert.Equal(RunCaptureState.Broken, resumed.State);
        Assert.False(Assert.Single(resumed.Refusals).WatchContinues);
        Assert.Contains("is not one this recording ever saw", resumed.Refusal!, StringComparison.Ordinal);
    }

    [Fact]
    public void AJournalWrittenByARecorderThatJoinedLateStaysBrokenHoweverItResumes()
    {
        var journal = Played().Journal with { WitnessedRunStart = false };

        var resumed = Resume(journal, Digest(4));

        Assert.False(resumed.WitnessedRunStart);
        Assert.Equal(NativeSource.BrokenContinuity, resumed.Continuity);
        resumed.Finish("abandoned");
        Assert.False(resumed.ToManifest().Source.Native!.WitnessedRunStart.Value);
    }

    /// <summary>
    /// A rewind one session decided on is still a rewind two sessions later.
    ///
    /// The losing sequence without it: session one resumes at a rolled-back save and is
    /// marked rewound, carries on recording, and is quit to the main menu; session two
    /// finds a journal whose last digest is exactly the live one, sees nothing wrong,
    /// and publishes <c>continuity = continuous</c> over a run that was reloaded.
    /// Continuity is the one fact nothing downstream can re-derive, so that recording
    /// would carry a false claim nobody could check.
    ///
    /// The refusal's own class survives with it, so session two goes on recording where
    /// session one did rather than reading the sentence and stopping.
    /// </summary>
    [Fact]
    public void ASessionResumedAfterAnEarlierOneWasRewoundIsStillRewound()
    {
        var first = Resume(Played().Journal, Digest(1));
        Assert.Equal(NativeSource.RewoundContinuity, first.Continuity);
        first.Record(ActionVerb.SkipRewards, Args(), Floor(2), Digest(5));

        var second = Resume(RunJournal.Parse(first.Journal.Render()), Digest(5));

        Assert.Equal(NativeSource.RewoundContinuity, second.Continuity);
        Assert.Equal(RunCaptureState.Recording, second.State);
        Assert.Contains("resumed this run at decision 1", second.Refusal!, StringComparison.Ordinal);
        Assert.True(Assert.Single(second.Refusals).WatchContinues);
        Assert.Equal(3, second.NextSeq);
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

        var second = Resume(RunJournal.Parse(first.Journal.Render()), Digest(4));

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

        var kept = Assert.Single(read.Refusals);
        Assert.Equal("the engine never settled", kept.Reason);
        Assert.False(kept.WatchContinues);
        Assert.Equal(6, read.Entries.Count);
        Assert.Equal(RunJournal.RenderRefusal(RunRefusal.Stopping("the engine never settled")), line);
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
        var resumed = Resume(read, Digest(4));
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
        var resumed = Resume(twiceMarked, Digest(4));

        Assert.True(twiceMarked.NonStandard);
        Assert.Equal(NativeSource.NonStandardIntegrity, resumed.Integrity);
        Assert.Equal(Resume(RunJournal.Parse(capture.Journal.Render()), Digest(4)).Journal.Render(),
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
        var whole = EndingInADecision(Played().Journal.Render());
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
        var whole = EndingInADecision(Played().Journal.Render());
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

    /// <summary>The journal with the save point the game took after its last
    /// decision not yet landed, so the decision's own line is the last one: what a
    /// crash inside a decision's append leaves.</summary>
    private static string EndingInADecision(string journal)
    {
        var savePoint = journal.LastIndexOf("{\"save_point\"", StringComparison.Ordinal);
        Assert.True(savePoint > 0 && journal.IndexOf('\n', savePoint) == journal.Length - 1);
        return journal[..savePoint];
    }

    /// <summary>The two decisions a resumed session goes on to record.</summary>
    private static string Continued(string journal)
    {
        var resumed = Resume(RunJournal.Parse(journal), Digest(3));
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

        var refusal = Assert.Throws<ManifestException>(() => Resume(gapped, Digest(4)));

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
    // ── Bookmarks ──────────────────────────────────────────────────────────

    /// <summary>
    /// A press is a journal line and, in the manifest, a declared fact on the fight it
    /// names, carrying the decision that fight ended on and that decision's run clock.
    /// </summary>
    [Fact]
    public void ABookmarkIsAJournalLineAndADeclaredFactInTheManifest()
    {
        var capture = Played();

        var line = capture.MarkBookmark(1, on: true, Nothing);
        capture.Finish("won");

        Assert.Contains("\"bookmark\":{\"fight\":1,\"on\":true,\"after_seq\":4,\"run_clock_ms\":812340}", line, StringComparison.Ordinal);
        Assert.True(capture.IsBookmarked(1));

        var bookmark = Assert.Single(capture.ToManifest().Source.Native!.Bookmarks!);
        Assert.Equal(1, bookmark.Fight);
        Assert.True(bookmark.Bookmarked.Value);
        Assert.Equal(FactSource.Declared, bookmark.Bookmarked.Source);
        Assert.Equal(4, bookmark.Bookmarked.Evidence!.ActionOrdinal);
        Assert.Equal(812_340, bookmark.Bookmarked.Evidence.RunClockMs);

        // And nothing that identifies the run moved: the history hash is over actions
        // and the boundaries are untouched
        var without = Played();
        without.Finish("won");
        Assert.Equal(SnapshotCacheKey.HashActions(without.ToManifest().Actions), SnapshotCacheKey.HashActions(capture.ToManifest().Actions));
        Assert.Equal(without.ToManifest().Boundaries, capture.ToManifest().Boundaries);
    }

    /// <summary>Pressing again is the undo: another line, and no entry in the manifest,
    /// which leaves the field absent rather than empty.</summary>
    [Fact]
    public void ABookmarkTakenOffLeavesTheManifestWithoutIt()
    {
        var capture = Played();
        capture.MarkBookmark(1, on: true, Nothing);
        var off = capture.MarkBookmark(1, on: false, Nothing);
        capture.Finish("won");

        Assert.Contains("\"on\":false", off, StringComparison.Ordinal);
        Assert.False(capture.IsBookmarked(1));
        Assert.Null(capture.ToManifest().Source.Native!.Bookmarks);
        Assert.Equal(2, capture.Journal.Render().Split('\n').Count(line => line.Contains("\"bookmark\"", StringComparison.Ordinal)));
    }

    /// <summary>A crash keeps whatever was pressed, and the last press per fight is
    /// what the resumed session holds.</summary>
    [Fact]
    public void ABookmarkSurvivesIntoTheSessionThatResumesTheRun()
    {
        var capture = Played();
        capture.MarkBookmark(1, on: true, Nothing);
        capture.MarkBookmark(1, on: false, Nothing);
        capture.MarkBookmark(1, on: true, Nothing);

        var resumed = Resume(RunJournal.Parse(capture.Journal.Render()), capture.LastDigest);
        resumed.Finish("won");

        Assert.True(resumed.IsBookmarked(1));
        var bookmark = Assert.Single(resumed.ToManifest().Source.Native!.Bookmarks!);
        Assert.Equal(812_340, bookmark.Bookmarked.Evidence!.RunClockMs);
        Assert.Equal(RunCaptureState.Finished, resumed.State);
        Assert.Equal(NativeSource.ContinuousContinuity, resumed.Continuity);
    }

    /// <summary>
    /// A bookmark marks the fight, so a press on the card-reward screen behind a loot
    /// decision names the decision the fight ended on and its run clock, not the loot
    /// decision recorded after it.
    /// </summary>
    [Fact]
    public void ABookmarkPressedAfterALootDecisionStillNamesTheFightsEnd()
    {
        var capture = Played();
        capture.Record(
            ActionVerb.ClaimReward, Args(("reward_index", "0")), Floor(2), Digest(5), runClockMs: 830_000);

        var line = capture.MarkBookmark(1, on: true, Nothing);
        capture.Finish("won");

        Assert.Contains("\"after_seq\":4,\"run_clock_ms\":812340", line, StringComparison.Ordinal);
        var bookmark = Assert.Single(capture.ToManifest().Source.Native!.Bookmarks!);
        Assert.Equal(4, bookmark.Bookmarked.Evidence!.ActionOrdinal);
        Assert.Equal(812_340, bookmark.Bookmarked.Evidence.RunClockMs);
    }

    /// <summary>
    /// A fight lost is bookmarked on the game's death screen, by which time the run is
    /// over and the game's own clock reads nothing. The press still carries a run clock,
    /// because it takes the one recorded at the decision the fight ended on.
    /// </summary>
    [Fact]
    public void ABookmarkPressedAfterTheRunEndedStillCarriesARunClock()
    {
        var capture = Played();
        capture.Finish("lost");

        capture.MarkBookmark(1, on: true, Nothing);

        var bookmark = Assert.Single(capture.ToManifest().Source.Native!.Bookmarks!);
        Assert.Equal(4, bookmark.Bookmarked.Evidence!.ActionOrdinal);
        Assert.Equal(812_340, bookmark.Bookmarked.Evidence.RunClockMs);
    }

    /// <summary>
    /// A press that could not be written is not one this recording holds: the mark, the
    /// journal and the manifest all read as they did before it, so the control draws
    /// what reached the disk.
    /// </summary>
    [Fact]
    public void APressThatCouldNotBeWrittenLeavesTheRecordingUnchanged()
    {
        var capture = Played();
        capture.MarkBookmark(1, on: true, Nothing);

        Assert.Throws<IOException>(
            () => capture.MarkBookmark(1, on: false, _ => throw new IOException("the store refused")));

        Assert.True(capture.IsBookmarked(1));
        capture.Finish("won");
        Assert.True(Assert.Single(capture.ToManifest().Source.Native!.Bookmarks!).Bookmarked.Value);
        Assert.Equal(
            1,
            capture.Journal.Render().Split('\n').Count(line => line.Contains("\"bookmark\"", StringComparison.Ordinal)));
    }

    /// <summary>The control exists only once a fight has ended, so a press on any other
    /// fight is one nothing could have made.</summary>
    [Fact]
    public void ABookmarkOnAFightTheRecordingHasNotFinishedIsRefused()
    {
        var capture = RunCapture.Begin(Start());
        capture.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "1"), ("column", "3")), InFight(2, turn: 1), Digest(0));

        Assert.Throws<ManifestException>(() => capture.MarkBookmark(1, on: true, Nothing));
        Assert.Throws<ManifestException>(() => capture.MarkBookmark(2, on: true, Nothing));
    }

    /// <summary>
    /// The one moment the tag is drawn: from the fight's last action until the run
    /// leaves its floor. A lost run never leaves, so the fact holds after the run ends.
    /// </summary>
    [Fact]
    public void TheFightThatJustEndedIsKnownUntilTheRunMovesOn()
    {
        var capture = RunCapture.Begin(Start());
        Assert.Null(capture.LastEndedFight);
        Assert.False(capture.MovedOnFromLastFight);

        capture.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "1"), ("column", "3")), InFight(2, turn: 1), Digest(0));
        Assert.Null(capture.LastEndedFight);

        capture.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.BASH"), ("hand_index", "0")), Won(2, hp: 58), Digest(1));
        Assert.Equal(1, capture.LastEndedFight);
        Assert.False(capture.MovedOnFromLastFight);

        // Claiming loot is a decision on the same floor
        capture.Record(ActionVerb.ClaimReward, Args(("reward_index", "0")), Floor(2), Digest(2));
        Assert.Equal(1, capture.LastEndedFight);
        Assert.False(capture.MovedOnFromLastFight);

        capture.Record(ActionVerb.MapMove, Args(("act", "0"), ("row", "2"), ("column", "3")), Floor(3), Digest(3));
        Assert.Equal(1, capture.LastEndedFight);
        Assert.True(capture.MovedOnFromLastFight);

        capture.Finish("lost");
        Assert.Equal(1, capture.LastEndedFight);
        Assert.True(capture.MovedOnFromLastFight);
    }

    private static RunCapture Ended()
    {
        var capture = Played();
        capture.Finish("abandoned");
        return capture;
    }

    /// <summary>A press with no file behind it, for the tests that are not about the
    /// writing.</summary>
    private static void Nothing(string line)
    {
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
        Saved(capture);
        capture.Record(
            ActionVerb.MapMove, Args(("act", "0"), ("row", "1"), ("column", "3")),
            InFight(2, turn: 1), Digest(1));
        Saved(capture);
        capture.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.BASH"), ("hand_index", "0")),
            InFight(2, turn: 1, enemyHp: 30), Digest(2));
        capture.Record(ActionVerb.EndTurn, Args(), InFight(2, turn: 2, enemyHp: 30, hp: 58), Digest(3));
        capture.Record(
            ActionVerb.PlayCard, Args(("card_id", "CARD.STRIKE_IRONCLAD"), ("hand_index", "1")),
            Won(2, hp: 58), Digest(4), runClockMs: 812_340);
        Saved(capture);
        return capture;
    }

    /// <summary>The game saved after the last decision recorded, and the save landed:
    /// what the recorder writes at every map-point arrival, every fight won and every
    /// ancient event finished.</summary>
    private static string Saved(RunCapture capture)
    {
        var line = capture.MarkSavePoint(capture.NextSeq - 1);
        Assert.NotNull(line);
        return line;
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

    /// <summary>The loot screen of a fight just won: no fight is live, and the room
    /// says how the last one ended. Nothing else of the fight, on purpose.</summary>
    private static IReadOnlyDictionary<string, string> Won(int floor, int hp) => new Dictionary<string, string>(
        StringComparer.Ordinal)
    {
        ["combat.in_progress"] = "false",
        ["combat.outcome"] = "victory",
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

    /// <summary>
    /// Resumes at the moment a digest names: the live sample is that entry's own
    /// reading, exactly as a game restored to it would read, and a digest no entry
    /// carries resumes with a reading nothing in the journal matches. The tests
    /// about what a save cannot carry pass their own sample instead.
    /// </summary>
    private static RunCapture Resume(RunJournal journal, string digest) =>
        RunCapture.Resume(
            journal,
            journal.Entries.LastOrDefault(entry => entry.Digest == digest)?.State
                ?? new Dictionary<string, string>(StringComparer.Ordinal) { ["run.total_floor"] = "unseen" },
            digest);

    /// <summary>The reading at a shop or any other room entered after a fight: the
    /// fight's room left behind, so nothing of it in the reading, exactly as the
    /// projection reads it there and as a run restored there reads.</summary>
    private static IReadOnlyDictionary<string, string> Shop(int floor, int hp) => new Dictionary<string, string>(
        StringComparer.Ordinal)
    {
        ["combat.in_progress"] = "false",
        ["combat.outcome"] = "none",
        ["run.total_floor"] = floor.ToString(CultureInfo.InvariantCulture),
        ["run.map_coord"] = $"r{floor.ToString(CultureInfo.InvariantCulture)}c1",
        ["run.act_floor"] = floor.ToString(CultureInfo.InvariantCulture),
        ["player.hp"] = hp.ToString(CultureInfo.InvariantCulture),
        ["player.max_hp"] = "68",
    };
}

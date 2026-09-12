namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// The plate under the game's own run history, state by state.
///
/// The derivation is total, so these tests are written as a table: every state the
/// design's section 5 names has a case here, and the structural rules are asserted in
/// every one of them. A refused state keeps the play-from row in place and says one
/// thing about why; the ordinary state has no head line at all, because the history
/// row's own record mark already says the run is recorded.
/// </summary>
public sealed class RunHistoryPlateTests
{
    private const string Build = "v0.111.0";

    private static RunHistoryFacts Facts(
        bool hasRecording = true,
        bool historyWhole = true,
        bool rewound = false,
        bool? consoleUsed = null,
        string recordedBuild = Build,
        bool runInProgress = false,
        bool submitAvailable = false,
        int? lastFloor = 11,
        FloorKind lastFloorKind = FloorKind.Combat,
        bool hasOtherFloors = true) =>
        new(hasRecording, historyWhole, rewound, consoleUsed, recordedBuild, Build,
            runInProgress, submitAvailable, lastFloor, lastFloorKind, hasOtherFloors);

    [Fact]
    public void ARunWithNoRecordingHasNoPlateAtAll()
    {
        Assert.Null(RunHistoryPlate.For(Facts(hasRecording: false)));
    }

    /// <summary>
    /// The healthy state as a player reaches it today: the way in and the way to the
    /// rest of the run offered, and the Submit row refused because the flow it leads to
    /// is not built. The default here is false for that reason - true is a value
    /// production never passes, and a test that asserted this state's shape under it
    /// would be asserting about a screen nobody can open.
    ///
    /// It carries no head line, which is the settled rule: the history row's own record
    /// mark already says the run is recorded, and a head repeating it would be the plate
    /// introducing itself.
    /// </summary>
    [Fact]
    public void ARecordedContinuousRunOnThisBuildOffersItsFloorAndSaysNothingAtItsHead()
    {
        var plate = RunHistoryPlate.For(Facts())!;

        Assert.Null(plate.Mark);
        Assert.Null(plate.Head);
        Assert.True(plate.Rows[0].Enabled);
        Assert.True(plate.Rows[1].Enabled);
        Assert.False(plate.Rows[2].Enabled);
        Assert.Equal(LibraryCopy.PlateSubmitComing, plate.Reason);
        Assert.Equal("Play from floor 11 · combat", plate.Rows[0].Label);
        Assert.Equal(LibraryCopy.ChooseAnotherFloor, plate.Rows[1].Label);
        Assert.Equal(LibraryCopy.SubmitThisRun, plate.Rows[2].Label);
    }

    /// <summary>
    /// The sentence that a played-from run is not saved is said once, beside the rows,
    /// and never as a head line - the way the shipped trainer says it beside its Enter
    /// button. A plate whose rows are all refused says nothing: there it would read as
    /// the reason they are.
    /// </summary>
    [Fact]
    public void TheNotSavedSentenceIsSaidBesideTheRowsAndNeverAtTheHead()
    {
        var offered = RunHistoryPlate.For(Facts())!;
        var refused = RunHistoryPlate.For(Facts(historyWhole: false))!;

        Assert.Equal(LibraryCopy.NotSaved, offered.NotSaved);
        Assert.Null(offered.Head);
        Assert.Null(refused.NotSaved);
    }

    /// <summary>Rows name floors, never fights. No player has the fight-number concept
    /// and the game's own screens count floors.</summary>
    [Fact]
    public void NoRowNamesAFight()
    {
        var plate = RunHistoryPlate.For(Facts())!;

        Assert.All(
            plate.Rows,
            row => Assert.DoesNotContain("fight", row.Label, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The shape section 5 specifies for the healthy state, which this build reaches the
    /// moment the submit flow exists: every row offered and no reason line. Nothing else
    /// changes with it - the one supplied fact turning true is the whole difference.
    /// </summary>
    [Fact]
    public void OnceTheSubmitFlowExistsTheHealthyStateHasNoReasonLine()
    {
        var plate = RunHistoryPlate.For(Facts(submitAvailable: true))!;

        Assert.Null(plate.Mark);
        Assert.Null(plate.Head);
        Assert.Null(plate.Reason);
        Assert.All(plate.Rows, row => Assert.True(row.Enabled));
    }

    /// <summary>
    /// A console command changes what the run was, so it stops the run being published
    /// and stops nothing else: the fights really were fought. It is also the more
    /// particular thing true of this run, so it is what the reason names even where the
    /// submit flow is missing too.
    /// </summary>
    [Fact]
    public void AConsoleCommandStopsTheSubmitRowAndNothingElse()
    {
        var plate = RunHistoryPlate.For(Facts(consoleUsed: true))!;

        Assert.Null(plate.Head);
        Assert.True(plate.Rows[0].Enabled);
        Assert.True(plate.Rows[1].Enabled);
        Assert.False(plate.Rows[2].Enabled);
        Assert.Equal(LibraryCopy.PlateConsoleUsed, plate.Reason);
    }

    /// <summary>
    /// A reload that rewound the run behind what was recorded is the same shape: the
    /// run as it stands is played from, and never submitted, and the plate says so here
    /// rather than leaving the publication gate to refuse it after the form is filled in.
    /// </summary>
    [Fact]
    public void ARewoundRunStopsTheSubmitRowAndNothingElse()
    {
        var plate = RunHistoryPlate.For(Facts(rewound: true, submitAvailable: true))!;

        Assert.Null(plate.Head);
        Assert.True(plate.Rows[0].Enabled);
        Assert.True(plate.Rows[1].Enabled);
        Assert.False(plate.Rows[2].Enabled);
        Assert.Equal(LibraryCopy.PlateRewound, plate.Reason);
        Assert.Equal(LibraryCopy.NotSaved, plate.NotSaved);
    }

    /// <summary>
    /// Nobody has established whether a console command was used, so nothing is claimed
    /// about it. Null is not "no": it is the absence of a reading, and the submit row is
    /// left as it is rather than being refused on a check that never ran. Asked with the
    /// submit flow present, so the console question is the only thing that could refuse
    /// the row.
    /// </summary>
    [Fact]
    public void AnUnaskedConsoleQuestionIsNotAnsweredAsNo()
    {
        var plate = RunHistoryPlate.For(Facts(consoleUsed: null, submitAvailable: true))!;

        Assert.Null(plate.Reason);
        Assert.True(plate.Rows[2].Enabled);
    }

    [Theory]
    [MemberData(nameof(RefusedStates))]
    public void ARefusedStateKeepsEveryRowAndStatesOneThing(
        RunHistoryFacts facts, PlateMark? mark, string? head, string? reason)
    {
        var plate = RunHistoryPlate.For(facts)!;

        Assert.Equal(mark, plate.Mark);
        Assert.Equal(head, plate.Head);
        Assert.Equal(reason, plate.Reason);
        Assert.Equal(3, plate.Rows.Count);
        Assert.All(plate.Rows, row => Assert.False(row.Enabled));
    }

    public static TheoryData<RunHistoryFacts, PlateMark?, string?, string?> RefusedStates() => new()
    {
        {
            Facts(historyWhole: false), PlateMark.Warning,
            LibraryCopy.PlateCantBeReplayed, LibraryCopy.PlateContinuityBroken
        },
        {
            // Both versions in the head line, in the eligibility screen's red, and no
            // reason underneath: the head line is the whole reason.
            Facts(recordedBuild: "v0.110.0"), PlateMark.OtherVersion,
            LibraryCopy.PlateRecordedOn("v0.110.0", Build), null
        },
        {
            // No head: nothing is wrong with the recording, this is just the wrong
            // moment to be offered it.
            Facts(runInProgress: true), null, null, LibraryCopy.PlateDuringARun
        },
    };

    /// <summary>
    /// The continuity sentence names what happened in the player's own terms rather
    /// than in the recorder's. "The game reloaded past a point already recorded" is a
    /// fact about a journal; this is a fact about their run.
    /// </summary>
    [Fact]
    public void AnIncompleteRecordingSaysWhatHappenedRatherThanWhatTheRecorderSaw()
    {
        var plate = RunHistoryPlate.For(Facts(historyWhole: false))!;

        Assert.Equal(
            "Part of this run was played while Runmobile wasn't recording.", plate.Reason);
    }

    /// <summary>
    /// A row cannot say "Play from floor 11" about a recording with no floor 11, so the
    /// label drops the number rather than inventing one - and keeps its place.
    /// </summary>
    [Fact]
    public void ARecordingThatProvesNoFloorNamesNoFloorAndStillHoldsTheRow()
    {
        var plate = RunHistoryPlate.For(Facts(lastFloor: null))!;

        Assert.Equal(LibraryCopy.PlayFromAFloor, plate.Rows[0].Label);
        Assert.False(plate.Rows[0].Enabled);
        Assert.Equal(3, plate.Rows.Count);
    }

    /// <summary>
    /// A floor whose kind nothing established is named by its number alone. The row and
    /// the run view's own row are one sentence, so a floor named one way there is named
    /// the same way here.
    /// </summary>
    [Fact]
    public void AFloorNothingEstablishedTheKindOfIsNamedByItsNumberAlone()
    {
        var plate = RunHistoryPlate.For(Facts(lastFloorKind: FloorKind.Unknown))!;

        Assert.Equal("Play from floor 11", plate.Rows[0].Label);
        Assert.Equal(LibraryCopy.PlayFromFloor(11), plate.Rows[0].Label);
    }

    /// <summary>
    /// A row nothing is behind is not drawn. A run of one floor has no other floor to
    /// choose, so the disclosure row is absent rather than refused - the same rule the
    /// run view applies to Continue and Start the run over.
    /// </summary>
    [Fact]
    public void ARunWithNoOtherFloorDoesNotOfferTheWayToChooseOne()
    {
        var plate = RunHistoryPlate.For(Facts(hasOtherFloors: false))!;

        Assert.DoesNotContain(plate.Rows, row => row.Kind == PlateRowKind.ChooseAnotherFloor);
        Assert.Equal(2, plate.Rows.Count);
    }
}

namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// The plate under the game's own run history, state by state.
///
/// The derivation is total, so these tests are written as a table: every state the
/// design names has a case here, and the two structural rules are asserted in every
/// one of them. A refused state keeps every row in place, and it says one thing about
/// why. A plate that collapsed to nothing on a multiplayer run would pass a test that
/// only checked the reason line.
/// </summary>
public sealed class RunHistoryPlateTests
{
    private const string Build = "v0.111.0";

    private static RunHistoryFacts Facts(
        bool hasRecording = true,
        bool multiplayer = false,
        bool continuous = true,
        bool? consoleUsed = null,
        string recordedBuild = Build,
        bool runInProgress = false,
        bool submitAvailable = true,
        int? lastFight = 4,
        int? lastFloor = 11) =>
        new(hasRecording, multiplayer, continuous, consoleUsed, recordedBuild, Build,
            runInProgress, submitAvailable, lastFight, lastFloor);

    [Fact]
    public void ARunWithNoRecordingHasNoPlateAtAll()
    {
        Assert.Null(RunHistoryPlate.For(Facts(hasRecording: false)));
    }

    [Fact]
    public void ARecordedContinuousRunOnThisBuildOffersEverything()
    {
        var plate = RunHistoryPlate.For(Facts())!;

        Assert.Equal(PlateMark.Recorded, plate.Mark);
        Assert.Equal(LibraryCopy.PlateRecorded, plate.Head);
        Assert.Null(plate.Reason);
        Assert.All(plate.Rows, row => Assert.True(row.Enabled));
        Assert.Equal("Play from fight 4", plate.Rows[0].Label);
        Assert.Equal("Play from floor 11", plate.Rows[1].Label);
        Assert.Equal(LibraryCopy.SubmitThisRun, plate.Rows[2].Label);
    }

    /// <summary>
    /// The flow the Submit row leads to is not built, so the row is refused and says so
    /// - one owner for the decision, and no greyed row a player has to guess about. The
    /// two play rows are untouched: whether a run can be submitted says nothing about
    /// whether it can be played from.
    /// </summary>
    [Fact]
    public void WithNoSubmitFlowTheSubmitRowIsRefusedAndSaysWhyAndNothingElseChanges()
    {
        var plate = RunHistoryPlate.For(Facts(submitAvailable: false))!;

        Assert.Equal(PlateMark.Recorded, plate.Mark);
        Assert.True(plate.Rows[0].Enabled);
        Assert.True(plate.Rows[1].Enabled);
        Assert.False(plate.Rows[2].Enabled);
        Assert.Equal(LibraryCopy.PlateSubmitComing, plate.Reason);
    }

    /// <summary>A console command is the more particular thing true of this run, so it
    /// is what the reason names even where the flow is missing too.</summary>
    [Fact]
    public void AConsoleCommandIsNamedAheadOfTheMissingFlow()
    {
        var plate = RunHistoryPlate.For(Facts(consoleUsed: true, submitAvailable: false))!;

        Assert.False(plate.Rows[2].Enabled);
        Assert.Equal(LibraryCopy.PlateConsoleUsed, plate.Reason);
    }

    /// <summary>
    /// A console command changes what the run was, so it stops the run being published
    /// and stops nothing else: the fights really were fought.
    /// </summary>
    [Fact]
    public void AConsoleCommandStopsTheSubmitRowAndNothingElse()
    {
        var plate = RunHistoryPlate.For(Facts(consoleUsed: true))!;

        Assert.Equal(PlateMark.Recorded, plate.Mark);
        Assert.True(plate.Rows[0].Enabled);
        Assert.True(plate.Rows[1].Enabled);
        Assert.False(plate.Rows[2].Enabled);
        Assert.Equal(LibraryCopy.PlateConsoleUsed, plate.Reason);
    }

    /// <summary>
    /// Nobody has established whether a console command was used, so nothing is claimed
    /// about it. Null is not "no": it is the absence of a reading, and the submit row
    /// is left as it is rather than being refused on a check that never ran.
    /// </summary>
    [Fact]
    public void AnUnaskedConsoleQuestionIsNotAnsweredAsNo()
    {
        var plate = RunHistoryPlate.For(Facts(consoleUsed: null))!;

        Assert.Null(plate.Reason);
        Assert.True(plate.Rows[2].Enabled);
    }

    [Theory]
    [MemberData(nameof(RefusedStates))]
    public void ARefusedStateKeepsEveryRowAndStatesOneReason(
        RunHistoryFacts facts, PlateMark mark, string head, string reason)
    {
        var plate = RunHistoryPlate.For(facts)!;

        Assert.Equal(mark, plate.Mark);
        Assert.Equal(head, plate.Head);
        Assert.Equal(reason, plate.Reason);
        Assert.Equal(3, plate.Rows.Count);
        Assert.All(plate.Rows, row => Assert.False(row.Enabled));
    }

    public static TheoryData<RunHistoryFacts, PlateMark, string, string> RefusedStates() => new()
    {
        {
            Facts(continuous: false), PlateMark.Warning,
            LibraryCopy.PlateRecordedWithAGap, LibraryCopy.PlateContinuityBroken
        },
        {
            Facts(recordedBuild: "v0.110.0"), PlateMark.RecordedMuted,
            LibraryCopy.PlateRecordedOn("v0.110.0"), LibraryCopy.PlateOtherBuild(Build)
        },
        {
            Facts(runInProgress: true), PlateMark.RecordedMuted,
            LibraryCopy.PlateRecordedPlain, LibraryCopy.PlateDuringARun
        },
        {
            Facts(hasRecording: false, multiplayer: true), PlateMark.Multiplayer,
            LibraryCopy.PlateMultiplayer, LibraryCopy.PlateMultiplayerReason
        },
    };

    /// <summary>
    /// The recorder writes nothing for a multiplayer run, so the plate's own existence
    /// there is the point: the game's history row is the game's, and the affordance's
    /// place is held and explained so a player learns when it does work.
    /// </summary>
    [Fact]
    public void AMultiplayerRunKeepsThePlateEvenThoughNothingRecordedIt()
    {
        var plate = RunHistoryPlate.For(Facts(hasRecording: false, multiplayer: true));

        Assert.NotNull(plate);
        Assert.Equal(3, plate.Rows.Count);
    }

    /// <summary>
    /// A row cannot say "play from fight 4" about a recording with no fight 4, so the
    /// label drops the number rather than inventing one - and keeps its place.
    /// </summary>
    [Fact]
    public void ARecordingThatProvesNoFightNamesNoFightAndStillHoldsTheRow()
    {
        var plate = RunHistoryPlate.For(Facts(lastFight: null, lastFloor: null))!;

        Assert.Equal(LibraryCopy.PlayFromAFight, plate.Rows[0].Label);
        Assert.Equal(LibraryCopy.PlayFromAFloor, plate.Rows[1].Label);
        Assert.False(plate.Rows[0].Enabled);
        Assert.False(plate.Rows[1].Enabled);
        Assert.True(plate.Rows[2].Enabled);
    }

    /// <summary>Multiplayer is answered before the build question, because nothing
    /// arriving later changes it and a "come back later" reading would be wrong.</summary>
    [Fact]
    public void AMultiplayerRunOnAnotherBuildStillReadsAsMultiplayer()
    {
        var plate = RunHistoryPlate.For(Facts(multiplayer: true, recordedBuild: "v0.110.0"))!;

        Assert.Equal(PlateMark.Multiplayer, plate.Mark);
    }
}

using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// One run, opened: which places the recording proves and what each of them offers.
///
/// The thing being pinned is that no row offers somewhere the entry would refuse to
/// stand a player. Every position comes from the recording's own boundaries, which is
/// the same list <c>RecordedFightEntry</c> walks to, so a fight the recording stops
/// inside has no row that would take somebody into it - and, separately, that the
/// refusal a player reads distinguishes "there was no fight here" from "the recording
/// does not reach the end of this one", because those are different facts and the
/// recording carries enough to tell them apart.
/// </summary>
public sealed class RunViewTests
{
    private const string Run = "native-SEED-20260906-120000";

    private static Fact<string> Digest(string value) => Fact<string>.Engine(value);

    private static ReplayManifest Recording(
        IReadOnlyList<ReplayBoundary>? boundaries = null,
        IReadOnlyList<ActionRecord>? actions = null)
    {
        var recording = Fixtures.Recording() with
        {
            RunId = Run,
            Boundaries = boundaries ?? [],
            Actions = actions ?? [],
        };
        return recording;
    }

    private static ActionRecord Combat(int seq) => new()
    {
        Seq = seq,
        Verb = ActionVerb.PlayCard,
        Source = FactSource.Captured,
        Args = new Dictionary<string, string>(StringComparer.Ordinal) { ["hand_index"] = "0" },
    };

    /// <summary>Floors 1 to 3, with a finished fight on floor 2.</summary>
    private static ReplayManifest ThreeFloors() => Recording(
    [
        ReplayBoundary.FloorEntry(floor: 2, afterSeq: 10, Digest("floor-2")),
        ReplayBoundary.CombatStart(fight: 1, afterSeq: 12, Digest("fight-1")),
        ReplayBoundary.FloorEntry(floor: 3, afterSeq: 20, Digest("floor-3")),
    ]);

    /// <summary>
    /// A run does not arrive at the floor it begins on, so the recording proves no
    /// boundary there - and leaving it out would start the strip at floor two.
    /// </summary>
    [Fact]
    public void TheRunStartIsAPlaceEvenThoughNoBoundaryProvesIt()
    {
        var positions = RunView.PositionsIn(ThreeFloors());

        Assert.Equal([1, 2, 3], positions.Select(position => position.Floor));
        Assert.True(positions[0].IsRunStart);
        Assert.False(positions[1].IsRunStart);
    }

    [Fact]
    public void AFloorCarriesTheFightThatStartsOnIt()
    {
        var positions = RunView.PositionsIn(ThreeFloors());

        Assert.Null(positions[0].Fight);
        Assert.Equal(1, positions[1].Fight);
        Assert.Null(positions[2].Fight);
    }

    [Fact]
    public void AFloorWithAFinishedFightOffersEveryWayIn()
    {
        var view = RunView.For(ThreeFloors(), RunProgress.Empty, selectedFloor: 2);

        Assert.All(view.Rows, row => Assert.True(row.Enabled));
        Assert.Equal(
            [
                RunViewRowKind.PlayFromFight, RunViewRowKind.PlayFromFloor,
                RunViewRowKind.Continue, RunViewRowKind.StartOver,
            ],
            view.Rows.Select(row => row.Kind));
        Assert.Equal(1, view.Rows[0].Fight);
    }

    /// <summary>
    /// Starting over walks to fight 1's combat start, so it carries fight 1 - which is
    /// what makes the row record the pip through the same path every other entering row
    /// uses, and what makes it the same destination as "Play from this fight" there.
    /// </summary>
    [Fact]
    public void StartingOverNamesFightOneSoItRecordsThePipLikeEveryOtherWayIn()
    {
        var view = RunView.For(ThreeFloors(), RunProgress.Empty, selectedFloor: 1);

        var startOver = view.Rows.Single(row => row.Kind == RunViewRowKind.StartOver);
        Assert.Equal(1, startOver.Fight);
        Assert.Equal(LibraryCopy.StartTheRunOver, startOver.Label);
        Assert.Equal(LibraryCopy.StartTheRunOverNote, startOver.Note);
    }

    /// <summary>A boundary the recording does not prove is not drawn, so a recording
    /// with no fight 1 offers no row that would walk to one.</summary>
    [Fact]
    public void StartingOverIsAbsentWhenTheRecordingProvesNoFirstFight()
    {
        var recording = Recording([ReplayBoundary.CombatStart(fight: 2, afterSeq: 9, Digest("two"))]);

        var view = RunView.For(recording, RunProgress.Empty);

        Assert.DoesNotContain(view.Rows, row => row.Kind == RunViewRowKind.StartOver);
    }

    /// <summary>
    /// A fight whose recording has been shown this sitting carries the mark on its
    /// row, and only that fight; the row stays offered. The set is whoever drew the
    /// comparison's, held in memory, so a view given nothing marks nothing.
    /// </summary>
    [Fact]
    public void AFightShownThisSittingIsMarkedAndStillOffered()
    {
        var marked = RunView.For(ThreeFloors(), RunProgress.Empty, selectedFloor: 2, shownThisSitting: [1]);
        var cold = RunView.For(ThreeFloors(), RunProgress.Empty, selectedFloor: 2);
        var other = RunView.For(ThreeFloors(), RunProgress.Empty, selectedFloor: 2, shownThisSitting: [2]);

        var row = marked.Rows.Single(entry => entry.Kind == RunViewRowKind.PlayFromFight);
        Assert.True(row.ShownThisSitting);
        Assert.True(row.Enabled);
        Assert.False(cold.Rows.Single(entry => entry.Kind == RunViewRowKind.PlayFromFight).ShownThisSitting);
        Assert.False(other.Rows.Single(entry => entry.Kind == RunViewRowKind.PlayFromFight).ShownThisSitting);
        Assert.All(marked.Rows.Where(entry => entry.Kind != RunViewRowKind.PlayFromFight),
            entry => Assert.False(entry.ShownThisSitting));
    }

    /// <summary>A refused row keeps its place, because the row's position is how a
    /// player learns the offer exists.</summary>
    [Fact]
    public void AFloorWithNoFightKeepsTheFightRowAndSaysWhyItIsRefused()
    {
        var view = RunView.For(ThreeFloors(), RunProgress.Empty, selectedFloor: 3);

        var row = view.Rows.Single(entry => entry.Kind == RunViewRowKind.PlayFromFight);
        Assert.False(row.Enabled);
        Assert.Equal(LibraryCopy.NoFightOnThisFloor, row.Reason);
    }

    /// <summary>
    /// A fight the recording stops inside gets its own sentence. The recording's combat
    /// actions say a fight happened; the absence of a boundary says it never finished,
    /// and there is no completed recorded line for a player's own to be set beside.
    /// </summary>
    [Fact]
    public void AFightTheRecordingDoesNotFinishIsRefusedForThatReasonAndNotForHavingNoFight()
    {
        var recording = Recording(
            [ReplayBoundary.FloorEntry(floor: 2, afterSeq: 10, Digest("floor-2"))],
            [Combat(11), Combat(12)]);

        var view = RunView.For(recording, RunProgress.Empty, selectedFloor: 2);

        var row = view.Rows.Single(entry => entry.Kind == RunViewRowKind.PlayFromFight);
        Assert.False(row.Enabled);
        Assert.Equal(LibraryCopy.FightNotFinished, row.Reason);
    }

    /// <summary>A run is not arrived at where it begins, so no floor entry proves it
    /// and there is nowhere for the floor row to stand anybody.</summary>
    [Fact]
    public void TheRunsFirstFloorRefusesTheFloorRowBecauseTheRunStartsThere()
    {
        var view = RunView.For(ThreeFloors(), RunProgress.Empty, selectedFloor: 1);

        var floor = view.Rows.Single(entry => entry.Kind == RunViewRowKind.PlayFromFloor);
        Assert.False(floor.Enabled);
        Assert.Equal(LibraryCopy.RunStartsHere, floor.Reason);
    }

    /// <summary>
    /// The pairing every real recording has: a floor entry and the combat it opens
    /// carry the same <c>after_seq</c>, because entering the room is the action that
    /// starts the fight. A window that excluded the floor's own seq handed every fight
    /// to the floor before the one it happened on - the run-start row offered fight 1,
    /// and pressing "Play from this fight" on floor 2 stood a player in a fight on
    /// another floor than the row named.
    /// </summary>
    [Fact]
    public void AFightBelongsToTheFloorWhoseEntryCarriesTheSameSeq()
    {
        var recording = Recording(
        [
            ReplayBoundary.CombatStart(fight: 1, afterSeq: 1, Digest("fight-1")),
            ReplayBoundary.FloorEntry(floor: 2, afterSeq: 1, Digest("floor-2")),
            ReplayBoundary.CombatStart(fight: 2, afterSeq: 14, Digest("fight-2")),
            ReplayBoundary.FloorEntry(floor: 3, afterSeq: 14, Digest("floor-3")),
            ReplayBoundary.FloorEntry(floor: 4, afterSeq: 28, Digest("floor-4")),
            ReplayBoundary.CombatStart(fight: 3, afterSeq: 32, Digest("fight-3")),
            ReplayBoundary.FloorEntry(floor: 5, afterSeq: 32, Digest("floor-5")),
        ]);

        var positions = RunView.PositionsIn(recording);

        Assert.Equal([1, 2, 3, 4, 5], positions.Select(position => position.Floor));
        Assert.Equal([null, 1, 2, null, 3], positions.Select(position => position.Fight));
        Assert.True(positions[0].IsRunStart);
        Assert.False(positions[3].Unfinished);
    }

    /// <summary>
    /// Continue names a fight a boundary proves, never the next number. A fight the
    /// recording stopped inside spends an ordinal and proves nothing, so counting the
    /// proved fights would have named one <c>RecordedFightPlan.For</c> then refuses.
    /// </summary>
    [Fact]
    public void ContinueSkipsAnOrdinalNoBoundaryProves()
    {
        var recording = Recording(
        [
            ReplayBoundary.CombatStart(fight: 1, afterSeq: 5, Digest("one")),
            ReplayBoundary.CombatStart(fight: 2, afterSeq: 9, Digest("two")),
            ReplayBoundary.CombatStart(fight: 4, afterSeq: 20, Digest("four")),
        ]);

        var view = RunView.For(
            recording, RunProgress.Empty.WithFightPlayed(Run, 1).WithFightPlayed(Run, 2));

        var row = view.Rows.Single(entry => entry.Kind == RunViewRowKind.Continue);
        Assert.Equal(4, row.Fight);
        Assert.Equal(LibraryCopy.ContinueAtFight(4), row.Label);
    }

    [Fact]
    public void OpeningARunWithNoFloorChosenStandsAtItsFirstPlace()
    {
        var view = RunView.For(ThreeFloors(), RunProgress.Empty);

        Assert.Equal(1, view.Selected!.Floor);
    }

    [Fact]
    public void ContinueNamesTheNextFightThisPlayerHasNotPlayedFrom()
    {
        var recording = Recording(
        [
            ReplayBoundary.CombatStart(fight: 1, afterSeq: 5, Digest("one")),
            ReplayBoundary.CombatStart(fight: 2, afterSeq: 9, Digest("two")),
        ]);

        var view = RunView.For(recording, RunProgress.Empty.WithFightPlayed(Run, 1));

        var row = view.Rows.Single(entry => entry.Kind == RunViewRowKind.Continue);
        Assert.Equal(LibraryCopy.ContinueAtFight(2), row.Label);
        Assert.Equal(2, row.Fight);
    }

    /// <summary>A row offering a fight the recording does not have would be an offer
    /// nothing could honour, so it is absent rather than refused.</summary>
    [Fact]
    public void ContinueIsAbsentOnceThereIsNothingLeftToContinueTo()
    {
        var recording = Recording([ReplayBoundary.CombatStart(fight: 1, afterSeq: 5, Digest("one"))]);

        var view = RunView.For(recording, RunProgress.Empty.WithFightPlayed(Run, 1));

        Assert.DoesNotContain(view.Rows, row => row.Kind == RunViewRowKind.Continue);
    }

    /// <summary>A truncated recording offers fewer places rather than a place that
    /// refuses when pressed.</summary>
    [Fact]
    public void ARecordingThatProvesNothingOffersOnePlaceAndNoWayIntoAFight()
    {
        var view = RunView.For(Recording(), RunProgress.Empty);

        Assert.Equal([1], view.Positions.Select(position => position.Floor));
        Assert.Equal(0, view.FightCount);
        Assert.DoesNotContain(view.Rows, row => row.Kind == RunViewRowKind.Continue);
        Assert.DoesNotContain(view.Rows, row => row.Kind == RunViewRowKind.StartOver);
        Assert.False(view.Rows.Single(row => row.Kind == RunViewRowKind.PlayFromFight).Enabled);
    }

    /// <summary>
    /// The run view and the row in the list count the same thing. Both count against
    /// the ordinals the recording proves, so a progress record holding a fight this
    /// recording does not have cannot make one screen say three and the other one.
    /// </summary>
    [Fact]
    public void TheRunViewAndTheListRowCountPlayedFightsTheSameWay()
    {
        var recording = Recording(
        [
            ReplayBoundary.CombatStart(fight: 1, afterSeq: 5, Digest("one")),
            ReplayBoundary.CombatStart(fight: 2, afterSeq: 9, Digest("two")),
            ReplayBoundary.CombatStart(fight: 4, afterSeq: 20, Digest("four")),
        ]);
        var progress = RunProgress.Empty
            .WithFightPlayed(Run, 1)
            .WithFightPlayed(Run, 3)
            .WithFightPlayed(Run, 99);

        var view = RunView.For(recording, progress);
        var row = LibraryRun.From(
            recording, RunOrigin.Mine, RunVerdict.Passed, progress.PlayedFrom(Run));

        Assert.Equal([1], view.FightsPlayed);
        Assert.Equal(row.PlayedCount, view.FightsPlayed.Count);
        Assert.Equal(row.FightCount, view.FightCount);
    }

    /// <summary>Nothing is compared from a floor entry, and the sentence that says so
    /// travels with the view rather than being written at a drawing site.</summary>
    [Fact]
    public void TheFloorNoteIsAlwaysTheOneSentence()
    {
        Assert.Equal(LibraryCopy.FloorIsYours, RunView.For(ThreeFloors(), RunProgress.Empty).FloorNote);
    }
}

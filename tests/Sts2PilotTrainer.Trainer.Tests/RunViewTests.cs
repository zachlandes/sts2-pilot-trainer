using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// One run, opened: which places the recording proves, what each of them held, and
/// what the one play-from row says about the selected one.
///
/// The thing being pinned is that no row offers somewhere the entry would refuse to
/// stand a player. Every position comes from the recording's own boundaries, which is
/// the same list <c>RecordedFightEntry</c> walks to, so a fight the recording stops
/// inside has no row that would take somebody into it - and, separately, that the
/// refusal a player reads distinguishes the run's own start from a fight the recording
/// does not reach the end of, because those are different facts and the recording
/// carries enough to tell them apart.
///
/// The second thing being pinned is the settled vocabulary: one play-from row rather
/// than two, its second line naming the floor and its kind, and no fight named by
/// number anywhere.
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

    private static ActionRecord Decision(int seq, ActionVerb verb) => new()
    {
        Seq = seq,
        Verb = verb,
        Source = FactSource.Captured,
        Args = new Dictionary<string, string>(StringComparer.Ordinal),
    };

    /// <summary>Floors 1 to 3, with a finished fight on floor 2.</summary>
    private static ReplayManifest ThreeFloors() => Recording(
    [
        ReplayBoundary.FloorEntry(floor: 2, afterSeq: 10, Digest("floor-2")),
        ReplayBoundary.CombatStart(fight: 1, afterSeq: 12, Digest("fight-1")),
        ReplayBoundary.FloorEntry(floor: 3, afterSeq: 20, Digest("floor-3")),
    ]);

    private static RunViewRow PlayFrom(RunView view) =>
        view.Rows.Single(row => row.Kind == RunViewRowKind.PlayFrom);

    private static RunViewRow? RowOf(RunView view, RunViewRowKind kind) =>
        view.Rows.SingleOrDefault(row => row.Kind == kind);

    /// <summary>
    /// The same three floors, with the recording's own decisions under them: the run
    /// walks to fight 1 on nothing but a blessing and a map move, and reaches floor 3
    /// only through that fight and the loot it dropped.
    ///
    /// This is the shape of every real recording. It is written out rather than reduced
    /// because the whole point of the gate is which of these decisions a running client
    /// can issue, and a fixture with no actions in it would pass either way.
    /// </summary>
    private static ReplayManifest ThreeFloorsAsPlayed() => Recording(
        [
            ReplayBoundary.FloorEntry(floor: 2, afterSeq: 1, Digest("floor-2")),
            ReplayBoundary.CombatStart(fight: 1, afterSeq: 1, Digest("fight-1")),
            ReplayBoundary.FloorEntry(floor: 3, afterSeq: 6, Digest("floor-3")),
            ReplayBoundary.CombatStart(fight: 2, afterSeq: 6, Digest("fight-2")),
        ],
        [
            Decision(0, ActionVerb.ChooseNeowBlessing),
            Decision(1, ActionVerb.MapMove),
            Combat(2),
            Decision(3, ActionVerb.EndTurn),
            Decision(4, ActionVerb.ClaimReward),
            Decision(5, ActionVerb.TakeCard),
            Decision(6, ActionVerb.MapMove),
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

    /// <summary>
    /// One play-from row, not two. A fight is one thing a floor can hold rather than a
    /// thing beside it, so there is one row whose label never changes and whose second
    /// line names the selected floor and its kind.
    /// </summary>
    [Fact]
    public void AFloorWithAFinishedFightOffersOnePlayFromRowNamingTheFloorAndItsKind()
    {
        var view = RunView.For(ThreeFloors(), RunProgress.Empty, selectedFloor: 2);

        Assert.All(view.Rows, row => Assert.True(row.Enabled));
        Assert.Equal(
            [RunViewRowKind.PlayFrom, RunViewRowKind.Continue, RunViewRowKind.StartOver],
            view.Rows.Select(row => row.Kind));

        var row = PlayFrom(view);
        Assert.Equal(LibraryCopy.PlayFromThisFloor, row.Label);
        Assert.Equal(LibraryCopy.FloorLine(2, FloorKind.Combat), row.Note);
        Assert.Equal(1, row.Fight);
        Assert.Equal(2, row.Floor);
    }

    /// <summary>
    /// The second line names what the floor held, and the kind is derived from the
    /// recording's own decisions on it rather than from a room type nothing records.
    /// </summary>
    [Theory]
    [InlineData(ActionVerb.ShopPurchase, FloorKind.Shop)]
    [InlineData(ActionVerb.ChooseRestSiteOption, FloorKind.Rest)]
    [InlineData(ActionVerb.ChooseEventOption, FloorKind.Event)]
    [InlineData(ActionVerb.TakeChestRelic, FloorKind.Treasure)]
    [InlineData(ActionVerb.SkipChestRelic, FloorKind.Treasure)]
    public void ANonCombatFloorIsNamedByTheDecisionTheRecordingMadeOnIt(
        ActionVerb verb, FloorKind kind)
    {
        var recording = Recording(
            [ReplayBoundary.FloorEntry(floor: 2, afterSeq: 10, Digest("floor-2"))],
            [Decision(11, verb)]);

        var view = RunView.For(recording, RunProgress.Empty, selectedFloor: 2);

        var row = PlayFrom(view);
        Assert.True(row.Enabled);
        Assert.Equal(LibraryCopy.FloorLine(2, kind), row.Note);
        Assert.Null(row.Fight);
    }

    /// <summary>
    /// A floor whose recording made no decision saying what was there is named by its
    /// number and nothing else. Guessing a kind would put a word on screen nobody
    /// established.
    /// </summary>
    [Fact]
    public void AFloorNothingEstablishedTheKindOfIsNamedByItsNumberAlone()
    {
        var view = RunView.For(ThreeFloors(), RunProgress.Empty, selectedFloor: 3);

        var row = PlayFrom(view);
        Assert.True(row.Enabled);
        Assert.Equal(LibraryCopy.FloorLine(3), row.Note);
        Assert.Null(LibraryCopy.KindWord(FloorKind.Unknown));
    }

    /// <summary>
    /// Starting over walks to fight 1's combat start, so it carries both the fight and
    /// its floor through the same progress path every other entering row uses. It
    /// carries no second line: one said nothing its label did not.
    /// </summary>
    [Fact]
    public void StartingOverNamesFightOneAndItsFloorAndCarriesNoSecondLine()
    {
        var view = RunView.For(ThreeFloors(), RunProgress.Empty, selectedFloor: 1);

        var startOver = view.Rows.Single(row => row.Kind == RunViewRowKind.StartOver);
        Assert.Equal(1, startOver.Fight);
        Assert.Equal(2, startOver.Floor);
        Assert.Equal(LibraryCopy.StartTheRunOver, startOver.Label);
        Assert.Null(startOver.Note);
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
    /// A fight whose recording has been shown this sitting carries the mark on the
    /// play-from row, and only when that fight is the selected one; the row stays
    /// offered. The set is whoever drew the comparison's, held in memory, so a view
    /// given nothing marks nothing.
    /// </summary>
    [Fact]
    public void AFightShownThisSittingIsMarkedAndStillOffered()
    {
        var marked = RunView.For(ThreeFloors(), RunProgress.Empty, selectedFloor: 2, shownThisSitting: [1]);
        var cold = RunView.For(ThreeFloors(), RunProgress.Empty, selectedFloor: 2);
        var other = RunView.For(ThreeFloors(), RunProgress.Empty, selectedFloor: 2, shownThisSitting: [2]);

        var row = PlayFrom(marked);
        Assert.True(row.ShownThisSitting);
        Assert.True(row.Enabled);
        Assert.False(PlayFrom(cold).ShownThisSitting);
        Assert.False(PlayFrom(other).ShownThisSitting);
        Assert.All(
            marked.Rows.Where(entry => entry.Kind != RunViewRowKind.PlayFrom),
            entry => Assert.False(entry.ShownThisSitting));
    }

    /// <summary>
    /// A fight the recording stops inside gets its own sentence. The recording's combat
    /// actions say a fight happened; the absence of a boundary says it never finished,
    /// and there is no completed recorded line for a player's own to be set beside.
    /// </summary>
    [Fact]
    public void AFightTheRecordingDoesNotFinishRefusesThePlayFromRowForThatReason()
    {
        var recording = Recording(
            [ReplayBoundary.FloorEntry(floor: 2, afterSeq: 10, Digest("floor-2"))],
            [Combat(11), Combat(12)]);

        var view = RunView.For(recording, RunProgress.Empty, selectedFloor: 2);

        var row = PlayFrom(view);
        Assert.False(row.Enabled);
        Assert.Equal(LibraryCopy.FightNotFinished, row.Reason);
        Assert.Null(row.Note);
    }

    /// <summary>A run is not arrived at where it begins, so no floor entry proves it
    /// and there is nowhere for the play-from row to stand anybody.</summary>
    [Fact]
    public void TheRunsFirstFloorRefusesThePlayFromRowBecauseTheRunStartsThere()
    {
        var view = RunView.For(ThreeFloors(), RunProgress.Empty, selectedFloor: 1);

        var row = PlayFrom(view);
        Assert.False(row.Enabled);
        Assert.Equal(LibraryCopy.RunStartsHere, row.Reason);
    }

    /// <summary>
    /// The pairing every real recording has: a floor entry and the combat it opens
    /// carry the same <c>after_seq</c>, because entering the room is the action that
    /// starts the fight. A window that excluded the floor's own seq handed every fight
    /// to the floor before the one it happened on - the run-start row offered fight 1,
    /// and pressing the play-from row on floor 2 stood a player in a fight on another
    /// floor than the row named.
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
    }

    /// <summary>
    /// Continue names no fight number. Its label is fixed and its second line is the
    /// floor that fight is on, which is the unit the strip and the game's own screens
    /// both count.
    /// </summary>
    [Fact]
    public void ContinueNamesAFloorAndNeverAFightNumber()
    {
        var recording = Recording(
        [
            ReplayBoundary.FloorEntry(floor: 2, afterSeq: 5, Digest("floor-2")),
            ReplayBoundary.CombatStart(fight: 1, afterSeq: 5, Digest("one")),
            ReplayBoundary.FloorEntry(floor: 3, afterSeq: 9, Digest("floor-3")),
            ReplayBoundary.CombatStart(fight: 2, afterSeq: 9, Digest("two")),
        ]);

        var view = RunView.For(recording, RunProgress.Empty.WithFightPlayed(Run, 1));

        var row = view.Rows.Single(entry => entry.Kind == RunViewRowKind.Continue);
        Assert.Equal(LibraryCopy.ContinueFromNextUnplayed, row.Label);
        Assert.Equal(LibraryCopy.FloorLine(3), row.Note);
        Assert.Equal(3, row.Floor);
        Assert.Equal(2, row.Fight);
        Assert.DoesNotContain("2", row.Label, StringComparison.Ordinal);
    }

    [Fact]
    public void OpeningARunWithNoFloorChosenStandsAtItsFirstPlace()
    {
        var view = RunView.For(ThreeFloors(), RunProgress.Empty);

        Assert.Equal(1, view.Selected!.Floor);
    }

    [Fact]
    public void ASharedEntryReadsProgressUnderItsOwnIdentity()
    {
        const string share = "shared-entry";
        var progress = RunProgress.Empty.WithFightPlayed(share, 1);

        var view = RunView.For(ThreeFloors(), progress, progressId: share);

        Assert.True(view.Strip.Single(cell => cell.Floor == 2).Played);
        Assert.DoesNotContain(view.Rows, row => row.Kind == RunViewRowKind.Continue);
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
        Assert.DoesNotContain(view.Rows, row => row.Kind == RunViewRowKind.Continue);
        Assert.DoesNotContain(view.Rows, row => row.Kind == RunViewRowKind.StartOver);
        Assert.False(PlayFrom(view).Enabled);
    }

    /// <summary>
    /// The strip enumerates floors, one cell per place the run reached, with the
    /// selected one ringed and the fights this player has stood in ticked. Nothing on
    /// it is a fight number.
    /// </summary>
    [Fact]
    public void TheStripIsOneCellPerFloorWithTheSelectedOneRingedAndPlayedOnesTicked()
    {
        var view = RunView.For(
            ThreeFloors(), RunProgress.Empty.WithFightPlayed(Run, 1), selectedFloor: 3);

        Assert.Equal([1, 2, 3], view.Strip.Select(cell => cell.Floor));
        Assert.Equal([false, true, false], view.Strip.Select(cell => cell.Played));
        Assert.Equal([false, false, true], view.Strip.Select(cell => cell.Selected));

        // The run's own start is not a place to be stood, so its cell is drawn and not
        // offered - the same rule the play-from row applies to it.
        Assert.Equal([false, true, true], view.Strip.Select(cell => cell.Playable));
    }

    /// <summary>
    /// A bookmark is a fact about the run, so its cell carries it whoever is looking,
    /// and the opened run says who marked it through the credit - "You" for the
    /// player's own recording, "This run" for one received from somebody else.
    /// </summary>
    [Fact]
    public void ABookmarkedFightMarksItsCellAndTheOpenedRunSaysWhoMarkedIt()
    {
        var recording = Bookmarked(ThreeFloors(), fight: 1);

        var mine = RunView.For(recording, RunProgress.Empty, selectedFloor: 2, isPlayersOwn: true);
        Assert.Equal([false, true, false], mine.Strip.Select(cell => cell.Bookmarked));
        Assert.Equal([false, true, false], mine.Positions.Select(position => position.Bookmarked));
        Assert.Equal("You bookmarked this fight.", mine.BookmarkNote);

        var theirs = RunView.For(recording, RunProgress.Empty, selectedFloor: 2);
        Assert.Equal("This run bookmarked this fight.", theirs.BookmarkNote);

        // Nowhere else: another floor selected says nothing, and a recording with no
        // marks has no cell marked and nothing to say
        Assert.Null(RunView.For(recording, RunProgress.Empty, selectedFloor: 3, isPlayersOwn: true).BookmarkNote);
        var plain = RunView.For(ThreeFloors(), RunProgress.Empty, selectedFloor: 2, isPlayersOwn: true);
        Assert.All(plain.Strip, cell => Assert.False(cell.Bookmarked));
        Assert.Null(plain.BookmarkNote);
    }

    /// <summary>The same recording as a native one with the given fight marked.</summary>
    private static ReplayManifest Bookmarked(ReplayManifest recording, int fight) =>
        recording with
        {
            Source = Fixtures.NativeRecording().Source with
            {
                Native = Fixtures.NativeRecording().Source.Native! with
                {
                    Bookmarks =
                    [
                        new FightBookmark
                        {
                            Fight = fight,
                            Bookmarked = new Fact<bool>(true, FactSource.Declared, FactEvidence.AtActionOrdinal(12)),
                        },
                    ],
                },
            },
        };

    /// <summary>
    /// The strip's playable cells and the play-from row agree, floor by floor. They are
    /// one rule read twice, so a cell a strip draws as playable is a cell the row
    /// offers.
    /// </summary>
    [Fact]
    public void AStripCellIsPlayableExactlyWhereThePlayFromRowIsOffered()
    {
        var recording = Recording(
        [
            ReplayBoundary.FloorEntry(floor: 2, afterSeq: 10, Digest("floor-2")),
            ReplayBoundary.FloorEntry(floor: 3, afterSeq: 20, Digest("floor-3")),
        ],
        [Combat(21)]);

        foreach (var floor in (int[])[1, 2, 3])
        {
            var view = RunView.For(recording, RunProgress.Empty, selectedFloor: floor);
            var cell = view.Strip.Single(entry => entry.Floor == floor);
            Assert.Equal(cell.Playable, PlayFrom(view).Enabled);
        }
    }

    /// <summary>
    /// The deck and the relics come out of the recording's own checkpoints at the
    /// selected position, and a position nothing was recorded at is a gap rather than
    /// an empty deck.
    /// </summary>
    [Fact]
    public void TheDeckIsReadAtTheSelectedPositionAndIsNullWhereNothingWasRecorded()
    {
        var recording = ThreeFloors() with
        {
            Checkpoints =
            [
                new Checkpoint
                {
                    Id = "floor-2-entry",
                    AfterSeq = 10,
                    Kind = ReplayBoundary.FloorEntryKind,
                    Expect = new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
                    {
                        ["player.deck"] = Fact<string>.Engine("CARD.STRIKE|CARD.STRIKE|CARD.BASH"),
                        ["player.relics"] = Fact<string>.Engine("RELIC.BURNING_BLOOD"),
                    },
                },
            ],
        };

        var atTwo = RunView.For(recording, RunProgress.Empty, selectedFloor: 2);
        var atThree = RunView.For(recording, RunProgress.Empty, selectedFloor: 3);

        Assert.Equal([("CARD.STRIKE", 2), ("CARD.BASH", 1)], atTwo.Deck!.Select(tile => (tile.CardId, tile.Count)));
        Assert.Equal(3, atTwo.DeckCount);
        Assert.Equal(["RELIC.BURNING_BLOOD"], atTwo.Relics.Select(relic => relic.Id));
        Assert.Null(atThree.Deck);
        Assert.Null(atThree.DeckCount);
    }

    /// <summary>
    /// The sentence that a played-from run is not saved is said once, beside the rows,
    /// and only where one of them would actually stand a player somewhere. On a screen
    /// whose rows are all refused it would read as the reason they are.
    /// </summary>
    [Fact]
    public void TheNotSavedSentenceIsSaidOnlyWhereARowWouldStandSomebodySomewhere()
    {
        var offered = RunView.For(ThreeFloors(), RunProgress.Empty, selectedFloor: 2);
        var nothing = RunView.For(Recording(), RunProgress.Empty);

        Assert.Equal(LibraryCopy.NotSaved, offered.NotSaved);
        Assert.Null(nothing.NotSaved);
        Assert.All(nothing.Rows, row => Assert.False(row.Enabled));
    }

    /// <summary>
    /// Every row about one floor carries what that floor held, so the drawing can mark
    /// it without reading the row's sentence. Start the run over is about no one floor
    /// and carries nothing.
    /// </summary>
    [Fact]
    public void EveryRowAboutOneFloorCarriesWhatThatFloorHeld()
    {
        var view = RunView.For(ThreeFloors(), RunProgress.Empty, selectedFloor: 2);

        Assert.Equal(FloorKind.Combat, PlayFrom(view).Held);
        Assert.Equal(
            FloorKind.Unknown,
            view.Rows.Single(row => row.Kind == RunViewRowKind.StartOver).Held);
    }

    /// <summary>Nothing is compared from a floor entry, and the sentence that says so
    /// travels with the view rather than being written at a drawing site.</summary>
    [Fact]
    public void TheFloorNoteIsAlwaysTheOneSentence()
    {
        Assert.Equal(LibraryCopy.FloorIsYours, RunView.For(ThreeFloors(), RunProgress.Empty).FloorNote);
    }

    // ── What the client can actually reach ─────────────────────────────────
    //
    // The library used to offer every place the recording proved. Reaching any of them
    // past the first fight means getting through that fight in the retail client, which
    // the driver refuses - so the offer built the run, showed a decision or two and
    // then aborted in front of the player. These pin the other half of the rule: a
    // place exists, and a place can be reached, and both have to be true before a row
    // is enabled. Reached is RetailPlayback.RouteTo's answer: walked from the start, or
    // restored from a floor arrival where a fight is live - which every fight past the
    // first is, and no floor between fights is.

    /// <summary>
    /// The same three floors with an event on the third instead of a fight: reached only
    /// by walking the first fight, which the client does not, and restorable from
    /// nowhere, because a floor with no live fight at its arrival cannot be cached.
    /// </summary>
    private static ReplayManifest ThreeFloorsWithAnEventAsPlayed() => Recording(
        [
            ReplayBoundary.FloorEntry(floor: 2, afterSeq: 1, Digest("floor-2")),
            ReplayBoundary.CombatStart(fight: 1, afterSeq: 1, Digest("fight-1")),
            ReplayBoundary.FloorEntry(floor: 3, afterSeq: 6, Digest("floor-3")),
        ],
        [
            Decision(0, ActionVerb.ChooseNeowBlessing),
            Decision(1, ActionVerb.MapMove),
            Combat(2),
            Decision(3, ActionVerb.EndTurn),
            Decision(4, ActionVerb.ClaimReward),
            Decision(5, ActionVerb.TakeCard),
            Decision(6, ActionVerb.MapMove),
            Decision(7, ActionVerb.ChooseEventOption),
        ]);

    /// <summary>
    /// A recording whose first fight begins inside an event rather than at a floor
    /// arrival, behind a fight the client cannot play: no arrival on the way has a live
    /// fight, so there is nothing to restore from and nothing is offered.
    /// </summary>
    private static ReplayManifest FirstFightBehindAFightAndInsideAnEvent() => Recording(
        [
            ReplayBoundary.FloorEntry(floor: 2, afterSeq: 2, Digest("floor-2")),
            ReplayBoundary.CombatStart(fight: 1, afterSeq: 3, Digest("fight-1")),
        ],
        [
            Decision(0, ActionVerb.ChooseNeowBlessing),
            Combat(1),
            Decision(2, ActionVerb.MapMove),
            Decision(3, ActionVerb.ChooseEventOption),
        ]);

    [Fact]
    public void AFightPastTheFirstIsAPlaceThisClientOffersByRestoring()
    {
        var recording = ThreeFloorsAsPlayed();
        var positions = RunView.PositionsIn(recording);

        Assert.True(positions[1].Reachable);
        Assert.True(positions[2].Reachable);
        Assert.True(positions[2].Playable);
        Assert.IsType<PlaybackRoute.Walk>(RetailPlayback.RouteTo(recording, positions[1].AfterSeq));
        Assert.IsType<PlaybackRoute.Restore>(RetailPlayback.RouteTo(recording, positions[2].AfterSeq));
    }

    [Fact]
    public void AFloorReachedOnlyThroughAnEarlierFightIsNotAPlaceThisClientOffers()
    {
        var positions = RunView.PositionsIn(ThreeFloorsWithAnEventAsPlayed());

        Assert.True(positions[1].Reachable);
        Assert.True(positions[1].Playable);
        Assert.False(positions[2].Reachable);
        Assert.False(positions[2].Playable);
    }

    [Fact]
    public void ThePlayFromRowSaysWhyRatherThanStandingSomebodyThere()
    {
        var view = RunView.For(ThreeFloorsWithAnEventAsPlayed(), RunProgress.Empty, selectedFloor: 3);
        var row = PlayFrom(view);

        Assert.False(row.Enabled);
        Assert.Equal(LibraryCopy.EarlierFightNotReplayable, row.Reason);
        Assert.Null(row.Note);
    }

    [Fact]
    public void TheFirstFightIsStillOffered()
    {
        var view = RunView.For(ThreeFloorsAsPlayed(), RunProgress.Empty, selectedFloor: 2);
        var row = PlayFrom(view);

        Assert.True(row.Enabled);
        Assert.Null(row.Reason);
        Assert.Equal(1, row.Fight);
    }

    /// <summary>Continue names the next unplayed fight, and that fight is reached by
    /// restoring the arrival that dealt it.</summary>
    [Fact]
    public void ContinueReachesTheNextFightByRestoring()
    {
        var played = RunProgress.Empty.WithFightPlayed(Run, 1);
        var view = RunView.For(ThreeFloorsAsPlayed(), played, selectedFloor: 2);
        var row = RowOf(view, RunViewRowKind.Continue);

        Assert.NotNull(row);
        Assert.Equal(2, row.Fight);
        Assert.Equal(3, row.Floor);
        Assert.True(row.Enabled);
        Assert.Null(row.Reason);
        Assert.Equal(LibraryCopy.FloorLine(3), row.Note);
    }

    /// <summary>
    /// Continue is refused rather than moved on to the next fight this build can reach.
    /// It means "the next fight you have not played from", and a row that quietly named
    /// a different one would answer a question nobody asked. A second fight that began
    /// inside an event is one no route reaches: its floor's arrival has no live fight to
    /// restore from, and the walk to it goes through the first fight.
    /// </summary>
    [Fact]
    public void ContinueRefusesRatherThanSkippingToAFightThisClientCanReach()
    {
        var secondFightInsideAnEvent = Recording(
            [
                ReplayBoundary.FloorEntry(floor: 2, afterSeq: 1, Digest("floor-2")),
                ReplayBoundary.CombatStart(fight: 1, afterSeq: 1, Digest("fight-1")),
                ReplayBoundary.FloorEntry(floor: 3, afterSeq: 6, Digest("floor-3")),
                ReplayBoundary.CombatStart(fight: 2, afterSeq: 7, Digest("fight-2")),
            ],
            [
                Decision(0, ActionVerb.ChooseNeowBlessing),
                Decision(1, ActionVerb.MapMove),
                Combat(2),
                Decision(3, ActionVerb.EndTurn),
                Decision(4, ActionVerb.ClaimReward),
                Decision(5, ActionVerb.TakeCard),
                Decision(6, ActionVerb.MapMove),
                Decision(7, ActionVerb.ChooseEventOption),
            ]);
        var played = RunProgress.Empty.WithFightPlayed(Run, 1);
        var view = RunView.For(secondFightInsideAnEvent, played, selectedFloor: 2);
        var row = RowOf(view, RunViewRowKind.Continue);

        Assert.NotNull(row);
        Assert.Equal(2, row.Fight);
        Assert.False(row.Enabled);
        Assert.Equal(LibraryCopy.EarlierFightNotReplayable, row.Reason);
    }

    [Fact]
    public void ContinueIsOfferedWhileTheNextUnplayedFightIsTheFirst()
    {
        var view = RunView.For(ThreeFloorsAsPlayed(), RunProgress.Empty, selectedFloor: 2);
        var row = RowOf(view, RunViewRowKind.Continue);

        Assert.NotNull(row);
        Assert.Equal(1, row.Fight);
        Assert.True(row.Enabled);
        Assert.Null(row.Reason);
    }

    /// <summary>Start the run over goes to fight 1, so it is offered wherever that
    /// fight is reachable and refused with the same sentence where it is not.</summary>
    [Fact]
    public void StartTheRunOverFollowsTheSameRule()
    {
        var reachable = RowOf(
            RunView.For(ThreeFloorsAsPlayed(), RunProgress.Empty), RunViewRowKind.StartOver);
        Assert.NotNull(reachable);
        Assert.True(reachable.Enabled);

        var refused = RowOf(
            RunView.For(FirstFightBehindAFightAndInsideAnEvent(), RunProgress.Empty), RunViewRowKind.StartOver);

        Assert.NotNull(refused);
        Assert.False(refused.Enabled);
        Assert.Equal(LibraryCopy.EarlierFightNotReplayable, refused.Reason);
    }

    /// <summary>A first fight behind a fight the client cannot play is still offered
    /// where its own arrival dealt it: the arrival's save is what the journey restores.</summary>
    [Fact]
    public void StartTheRunOverRestoresAFirstFightDealtAtItsArrival()
    {
        var behindAFight = Recording(
            [ReplayBoundary.CombatStart(fight: 1, afterSeq: 2, Digest("fight-1")),
             ReplayBoundary.FloorEntry(floor: 2, afterSeq: 2, Digest("floor-2"))],
            [Decision(0, ActionVerb.ChooseNeowBlessing), Combat(1), Decision(2, ActionVerb.MapMove)]);
        var row = RowOf(RunView.For(behindAFight, RunProgress.Empty), RunViewRowKind.StartOver);

        Assert.NotNull(row);
        Assert.True(row.Enabled);
        Assert.IsType<PlaybackRoute.Restore>(RetailPlayback.RouteTo(behindAFight, 2));
    }

    /// <summary>The strip is drawn from the same rule, so a cell a player can see is
    /// offered is a cell the row will actually take them to.</summary>
    [Fact]
    public void TheStripDrawsTheSameAnswerAsTheRow()
    {
        var view = RunView.For(ThreeFloorsAsPlayed(), RunProgress.Empty, selectedFloor: 2);

        Assert.Equal(
            view.Positions.Select(position => position.Playable),
            view.Strip.Select(cell => cell.Playable));
    }

    /// <summary>A screen whose every row is refused says nothing about a run not being
    /// saved: that sentence would read as the reason they are refused.</summary>
    [Fact]
    public void AViewWithNothingOnOfferSaysNothingAboutSaving()
    {
        var view = RunView.For(FirstFightBehindAFightAndInsideAnEvent(), RunProgress.Empty);

        Assert.All(view.Rows, row => Assert.False(row.Enabled));
        Assert.Null(view.NotSaved);
    }
}


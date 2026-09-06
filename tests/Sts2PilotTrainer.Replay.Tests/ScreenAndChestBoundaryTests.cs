using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The window of card-selection answers that belong to the decision which opened
/// their screen.
///
/// <see cref="CardScreenAnswers"/> has three readers - the snapshot key, the replay,
/// and the walk to a boundary - and one of them cutting the window a decision short
/// or a decision long is not a visible failure: the replay refuses a screen for an
/// omission the truncation caused, or a snapshot is served for a history that would
/// not produce it. Until now the window was only ever exercised through those
/// readers, so a change to the rule failed somewhere else or nowhere. These pin the
/// rule itself.
/// </summary>
public sealed class CardScreenAnswerWindowTests
{
    /// <summary>
    /// The answers are the contiguous run immediately after the decision, and the
    /// first decision that is not an answer ends it. A later screen's answers belong
    /// to whatever opened that screen.
    /// </summary>
    /// <summary>
    /// A bundle screen and a relic screen are answered inside the call that opened
    /// them, exactly as a card screen is, so their records are followers too. A card
    /// reward's alternative is not: on this build it is the loot-screen decision
    /// itself, so the window stops in front of it.
    /// </summary>
    [Fact]
    public void ABundleAndARelicAnswerFollowTheirOpenerAndAnAlternativeDoesNot()
    {
        var history = new[]
        {
            Fixtures.Action(1, ActionVerb.TakeChestRelic, ("relic_id", "RELIC.SCROLL_BOXES"), ("option_index", "0")),
            Fixtures.Action(2, ActionVerb.SelectBundleFromScreen, ("card_ids", "CARD.BASH,CARD.ANGER"), ("option_index", "1")),
            Fixtures.Action(3, ActionVerb.SelectRelicFromScreen, ("relic_id", "RELIC.ANCHOR"), ("option_index", "0")),
            Select(4, "CARD.HEADBUTT"),
            Fixtures.Action(5, ActionVerb.TakeCardRewardAlternative, ("option_id", "SACRIFICE"), ("option_index", "3")),
        };

        Assert.Equal([2, 3, 4], CardScreenAnswers.After(history, 1).Select(action => action.Seq));
        Assert.Equal(
            [ActionVerb.SelectCardFromScreen, ActionVerb.SelectBundleFromScreen, ActionVerb.SelectRelicFromScreen],
            CardScreenAnswers.Verbs);
    }

    [Fact]
    public void TakesTheContiguousRunOfAnswersAndStopsAtTheFirstDecisionThatIsNotOne()
    {
        var history = new[]
        {
            Fixtures.Action(1, ActionVerb.EndTurn),
            Select(2, "CARD.BASH"),
            Select(3, "CARD.ANGER"),
            Fixtures.Action(4, ActionVerb.PlayCard, ("card_id", "CARD.STRIKE_IRONCLAD"), ("hand_index", "0")),
            Select(5, "CARD.HEADBUTT"),
        };

        Assert.Equal([2, 3], CardScreenAnswers.After(history, 1).Select(action => action.Seq));
    }

    /// <summary>A decision whose next record is not an answer opened no screen, and
    /// answering one it did not open would be inventing a decision.</summary>
    [Fact]
    public void TakesNothingWhenTheNextDecisionIsNotAnAnswer()
    {
        var history = new[]
        {
            Fixtures.Action(1, ActionVerb.EndTurn),
            Fixtures.Action(2, ActionVerb.PlayCard, ("card_id", "CARD.BASH"), ("hand_index", "0")),
            Select(3, "CARD.ANGER"),
        };

        Assert.Empty(CardScreenAnswers.After(history, 1));
    }

    /// <summary>
    /// The window starts strictly after the decision it is given. An answer at that
    /// sequence number answered the screen before it, and handing it back here would
    /// replay one recorded selection twice.
    /// </summary>
    [Fact]
    public void StartsStrictlyAfterTheDecisionItIsGiven()
    {
        var history = new[] { Select(1, "CARD.BASH"), Select(2, "CARD.ANGER") };

        Assert.Equal([2], CardScreenAnswers.After(history, 1).Select(action => action.Seq));
    }

    /// <summary>Contiguity is in the run's own order, not in whatever order a caller
    /// happened to hold the history in.</summary>
    [Fact]
    public void ReadsTheHistoryInSequenceOrderRatherThanTheOrderItWasHandedIn()
    {
        var history = new[]
        {
            Select(3, "CARD.ANGER"),
            Fixtures.Action(1, ActionVerb.EndTurn),
            Fixtures.Action(4, ActionVerb.PlayCard, ("card_id", "CARD.STRIKE_IRONCLAD"), ("hand_index", "0")),
            Select(2, "CARD.BASH"),
        };

        Assert.Equal([2, 3], CardScreenAnswers.After(history, 1).Select(action => action.Seq));
    }

    /// <summary>
    /// The whole act's forge, which is the one card screen in a shipped history that
    /// is opened by a decision outside a fight. Its answer belongs to the rest-site
    /// decision and to nothing before it - not to the map move that arrived on the
    /// floor, which opened no screen at all.
    /// </summary>
    [Fact]
    public void TheForgesAnswerBelongsToTheRestSiteDecisionAndNotToTheArrivalBeforeIt()
    {
        var manifest = Fixtures.WholeActManifest();
        var forge = manifest.Actions
            .OrderBy(action => action.Seq)
            .First(action => action.Verb == ActionVerb.ChooseRestSiteOption &&
                             action.Args["option_id"] == "SMITH");
        var arrival = manifest.Actions
            .Where(action => action.Verb == ActionVerb.MapMove && action.Seq < forge.Seq)
            .Max(action => action.Seq);

        var answers = CardScreenAnswers.After(manifest.Actions, forge.Seq);

        Assert.Equal([ActionVerb.SelectCardFromScreen], answers.Select(action => action.Verb));
        Assert.Equal(forge.Seq + 1, answers[0].Seq);
        Assert.Empty(CardScreenAnswers.After(manifest.Actions, arrival));
    }

    private static ActionRecord Select(int seq, string cardId) =>
        Fixtures.Action(seq, ActionVerb.SelectCardFromScreen, ("card_id", cardId), ("option_index", "0"));
}

/// <summary>
/// The boundary either side of a treasure chest, asked for by its own coordinate.
///
/// A floor is asked for by the number the run stands on, and the floors of a whole
/// act are not the positions of the floor-entry boundaries in the list - the run
/// begins on a floor it never arrived at, so every position is off by one against
/// the floor it holds. A reader that resolved a floor by position would answer the
/// treasure floor with the rest site after it: the same request, a node of another
/// type, and nothing in the reply saying so. That is what these check.
/// </summary>
public sealed class TreasureBoundaryTests
{
    /// <summary>
    /// The chest floor of the whole act, and the decision the recording made next
    /// there. A player stood at this boundary is stood in front of an unopened chest,
    /// which is the only thing that makes the entry checkable against the recording.
    /// </summary>
    [Fact]
    public void StandsAtTheTreasureFloorsArrivalWithTheChestStillUnopened()
    {
        var manifest = Fixtures.WholeActManifest();
        var chest = ChestDecision(manifest);
        var treasureFloor = FloorOf(manifest, chest);

        var plan = BoundarySelector.Parse($"floor_entry:{treasureFloor.Floor}").PlanFor(manifest);

        Assert.Equal(treasureFloor.AfterSeq, plan.BoundarySeq);
        Assert.Equal(ActionVerb.MapMove, plan.PrefixActions[^1].Verb);
        Assert.DoesNotContain(chest.Seq, plan.PrefixActions.Select(action => action.Seq));
        Assert.Equal(chest.Seq, NextDecisionAfter(manifest, plan.BoundarySeq).Seq);
        Assert.Equal(ActionVerb.TakeChestRelic, NextDecisionAfter(manifest, plan.BoundarySeq).Verb);
    }

    /// <summary>
    /// The floor after the chest is a different node, and the decision at the chest
    /// is behind the player rather than ahead of them. A reader that answered both
    /// coordinates with the same boundary would pass every check the previous test
    /// makes.
    /// </summary>
    [Fact]
    public void TheNextFloorIsADifferentNodeWithTheChestDecisionBehindIt()
    {
        var manifest = Fixtures.WholeActManifest();
        var chest = ChestDecision(manifest);
        var treasureFloor = FloorOf(manifest, chest);

        var after = BoundarySelector.Parse($"floor_entry:{treasureFloor.Floor!.Value + 1}").PlanFor(manifest);

        Assert.NotEqual(treasureFloor.AfterSeq, after.BoundarySeq);
        Assert.Contains(chest.Seq, after.PrefixActions.Select(action => action.Seq));
        Assert.NotEqual(ActionVerb.TakeChestRelic, NextDecisionAfter(manifest, after.BoundarySeq).Verb);
    }

    /// <summary>
    /// The two arrivals either side of the chest are two different states, so they
    /// are two different snapshots. Proved by changing the chest decision and seeing
    /// which key moves: the floor before it cannot notice, and the floor after it
    /// must.
    /// </summary>
    [Fact]
    public void OnlyTheFloorAfterTheChestNoticesWhichRelicWasTaken()
    {
        var manifest = Fixtures.WholeActManifest();
        var chest = ChestDecision(manifest);
        var treasureFloor = FloorOf(manifest, chest);
        var otherRelic = manifest with
        {
            Actions =
            [
                .. manifest.Actions.Select(action => action.Seq == chest.Seq
                    ? Fixtures.Action(
                        action.Seq, ActionVerb.TakeChestRelic,
                        ("relic_id", "RELIC.BAG_OF_PREPARATION"), ("option_index", "1"))
                    : action),
            ],
        };

        var before = $"floor_entry:{treasureFloor.Floor}";
        var after = $"floor_entry:{treasureFloor.Floor!.Value + 1}";

        Assert.Equal(
            BoundarySelector.Parse(before).PlanFor(manifest).SnapshotKey,
            BoundarySelector.Parse(before).PlanFor(otherRelic).SnapshotKey);
        Assert.NotEqual(
            BoundarySelector.Parse(after).PlanFor(manifest).SnapshotKey,
            BoundarySelector.Parse(after).PlanFor(otherRelic).SnapshotKey);
    }

    /// <summary>
    /// A chest boundary that names the chest decision rather than the arrival is
    /// refused. The engine discards the relic either way, so a plan that stopped one
    /// decision late would stand a player past a decision they were meant to make and
    /// nothing about the state would say which.
    /// </summary>
    [Fact]
    public void RefusesATreasureBoundaryThatNamesTheChestDecisionRatherThanTheArrival()
    {
        var manifest = Fixtures.WholeActManifest();
        var chest = ChestDecision(manifest);
        var treasureFloor = FloorOf(manifest, chest);
        var moved = manifest with
        {
            Boundaries =
            [
                .. manifest.Boundaries.Where(boundary => boundary != treasureFloor),
                ReplayBoundary.FloorEntry(treasureFloor.Floor!.Value, chest.Seq, treasureFloor.Digest),
            ],
        };

        var refusal = Assert.Throws<ManifestException>(
            () => FloorEntryPlan.For(moved, treasureFloor.Floor!.Value));

        Assert.Contains("a floor is arrived on by moving on the map", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("TakeChestRelic", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A declined chest is carried the same way a taken one is. The verb exists
    /// because the engine discards the relic either way, so an omitted decision
    /// replays exactly like a declined one - which means a plan that quietly dropped
    /// it would produce a state that looks right and is not.
    ///
    /// No shipped history declines a chest, so this one is built here.
    /// </summary>
    [Fact]
    public void ADeclinedChestIsCarriedIntoTheNextFloorsPlanTheSameWayATakenOneIs()
    {
        var manifest = DeclinedChestRun();

        var atTheChest = FloorEntryPlan.For(manifest, 2);
        var afterIt = FloorEntryPlan.For(manifest, 3);

        Assert.Equal([0, 1], atTheChest.PrefixActions.Select(action => action.Seq));
        Assert.Equal([0, 1, 2, 3], afterIt.PrefixActions.Select(action => action.Seq));
        Assert.Equal(ActionVerb.SkipChestRelic, afterIt.PrefixActions[2].Verb);
        Assert.NotEqual(atTheChest.SnapshotKey, afterIt.SnapshotKey);
    }

    /// <summary>
    /// The same run with the declined chest simply left out of the history. It is a
    /// different run and the arrival after it is a different moment, so the plan that
    /// reaches it may not be the same plan.
    /// </summary>
    [Fact]
    public void ARunThatOmitsTheDeclinedChestReachesTheNextFloorByADifferentJourney()
    {
        var declined = DeclinedChestRun();
        var omitted = declined with
        {
            Actions = [.. declined.Actions.Where(action => action.Verb != ActionVerb.SkipChestRelic)],
        };

        Assert.NotEqual(
            FloorEntryPlan.For(declined, 3).PrefixActions.Count,
            FloorEntryPlan.For(omitted, 3).PrefixActions.Count);
        Assert.NotEqual(
            FloorEntryPlan.For(declined, 3).SnapshotKey,
            FloorEntryPlan.For(omitted, 3).SnapshotKey);
    }

    /// <summary>A run that arrives on a treasure floor, leaves the relic, and moves
    /// on. Built here rather than loaded so each test changes one thing about it.</summary>
    private static ReplayManifest DeclinedChestRun() => Fixtures.ValidManifest() with
    {
        Actions =
        [
            Fixtures.Action(0, ActionVerb.ChooseNeowBlessing, ("option_index", "2")),
            Fixtures.Action(1, ActionVerb.MapMove, ("act", "0"), ("row", "1"), ("column", "3")),
            Fixtures.Action(2, ActionVerb.SkipChestRelic, ("option_index", "0")),
            Fixtures.Action(3, ActionVerb.MapMove, ("act", "0"), ("row", "2"), ("column", "3")),
            Fixtures.Action(4, ActionVerb.PlayCard, ("card_id", "CARD.BASH"), ("hand_index", "0")),
        ],
        Checkpoints =
        [
            Arrival("floor-entry-2", 1, floor: "2", coord: "r1c3"),
            Arrival("floor-entry-3", 3, floor: "3", coord: "r2c3"),
        ],
        Boundaries =
        [
            ReplayBoundary.FloorEntry(2, 1, Fact<string>.Engine(Fixtures.Digest)),
            ReplayBoundary.FloorEntry(3, 3, Fact<string>.Engine(Fixtures.Digest)),
        ],
    };

    private static Checkpoint Arrival(string id, int afterSeq, string floor, string coord) => new()
    {
        Id = id,
        AfterSeq = afterSeq,
        Kind = ReplayBoundary.FloorEntryKind,
        Expect = new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
        {
            ["run.total_floor"] = Fact<string>.Observed(floor, FactEvidence.AtVideoTime(75600, "floor counter")),
            ["run.map_coord"] = Fact<string>.Observed(coord, FactEvidence.AtVideoTime(75600, "ringed node")),
        },
    };

    internal static ActionRecord ChestDecision(ReplayManifest manifest) =>
        manifest.Actions.OrderBy(action => action.Seq).First(action => action.Verb == ActionVerb.TakeChestRelic);

    /// <summary>The floor the recording was standing on when it opened that chest,
    /// read off the boundary list rather than through the reader under test.</summary>
    internal static ReplayBoundary FloorOf(ReplayManifest manifest, ActionRecord decision) =>
        manifest.Boundaries
            .Where(boundary => boundary.IsFloorEntry && boundary.AfterSeq < decision.Seq)
            .MaxBy(boundary => boundary.AfterSeq)!;

    internal static ActionRecord NextDecisionAfter(ReplayManifest manifest, int seq) =>
        manifest.Actions.OrderBy(action => action.Seq).First(action => action.Seq > seq);
}

/// <summary>
/// The card screens a journey passes on its way to a boundary.
///
/// A card selection is the one decision in the format that carries no coordinate of
/// its own - it is an option index on whatever screen was open - so two of them can
/// be identical records made at different moments of the same run. The journey is
/// what tells them apart, and a plan that told them apart by what they say rather
/// than by where they are would let a host answer one screen with the other's
/// decision.
/// </summary>
public sealed class CardScreenBoundaryPlanTests
{
    /// <summary>
    /// The forge's card selection is ahead of the player at the rest site's own
    /// arrival and behind them at the next one. A plan carrying it at the first
    /// boundary would stand somebody past a decision they had not made.
    /// </summary>
    [Fact]
    public void TheForgesSelectionIsAheadOfItsOwnArrivalAndBehindTheNextOne()
    {
        var manifest = Fixtures.WholeActManifest();
        var forge = manifest.Actions
            .OrderBy(action => action.Seq)
            .First(action => action.Verb == ActionVerb.ChooseRestSiteOption &&
                             action.Args["option_id"] == "SMITH");
        var selection = CardScreenAnswers.After(manifest.Actions, forge.Seq).Single();
        var arrival = manifest.Boundaries
            .Where(boundary => boundary.IsFloorEntry && boundary.AfterSeq < forge.Seq)
            .MaxBy(boundary => boundary.AfterSeq)!;
        var next = manifest.Boundaries
            .Where(boundary => boundary.IsFloorEntry && boundary.AfterSeq > selection.Seq)
            .MinBy(boundary => boundary.AfterSeq)!;

        var atTheRestSite = BoundarySelector.Parse($"floor_entry:{arrival.Floor}").PlanFor(manifest);
        var afterIt = BoundarySelector.Parse($"floor_entry:{next.Floor}").PlanFor(manifest);

        Assert.DoesNotContain(forge.Seq, atTheRestSite.PrefixActions.Select(action => action.Seq));
        Assert.DoesNotContain(selection.Seq, atTheRestSite.PrefixActions.Select(action => action.Seq));
        Assert.Equal(
            [forge.Seq, selection.Seq],
            afterIt.PrefixActions.Where(action => action.Seq >= forge.Seq && action.Seq <= selection.Seq)
                .Select(action => action.Seq));
    }

    /// <summary>
    /// Every card selection the recording made before a boundary is in the journey to
    /// it, and none of them is dropped for being an answer rather than a decision. A
    /// prefix that filtered them out would replay a run whose deck is not the recorded
    /// one and reach a boundary that fails for a reason nothing names.
    /// </summary>
    [Fact]
    public void EverySelectionTheRecordingMadeBeforeAFloorIsInTheJourneyToIt()
    {
        var manifest = Fixtures.WholeActManifest();
        var chest = TreasureBoundaryTests.ChestDecision(manifest);
        var treasureFloor = TreasureBoundaryTests.FloorOf(manifest, chest);

        var plan = BoundarySelector.Parse($"floor_entry:{treasureFloor.Floor}").PlanFor(manifest);

        var expected = manifest.Actions
            .OrderBy(action => action.Seq)
            .Where(action => action.Verb == ActionVerb.SelectCardFromScreen &&
                             action.Seq <= treasureFloor.AfterSeq)
            .Select(action => action.Seq)
            .ToList();

        Assert.NotEmpty(expected);
        Assert.Equal(
            expected,
            plan.PrefixActions
                .Where(action => action.Verb == ActionVerb.SelectCardFromScreen)
                .Select(action => action.Seq));
    }

    /// <summary>
    /// Two card selections that say exactly the same thing are still two decisions,
    /// and each is authorised only at its own point in the journey. This is where a
    /// plan that matched on what an action says would hand a screen the wrong answer
    /// while every field of it looked right.
    ///
    /// Both plan kinds are checked, because both implement the authority separately.
    /// </summary>
    [Theory]
    [InlineData(ReplayBoundary.FloorEntryKind)]
    [InlineData(ReplayBoundary.CombatStartKind)]
    public void TwoIdenticalSelectionsAreNotInterchangeableInTheJourney(string kind)
    {
        var manifest = Fixtures.WholeActManifest();
        var twins = manifest.Actions
            .OrderBy(action => action.Seq)
            .Where(action => action.Verb == ActionVerb.SelectCardFromScreen)
            .GroupBy(action => string.Join(
                ",", action.Args.Select(argument => $"{argument.Key}={argument.Value}")), StringComparer.Ordinal)
            .First(group => group.Count() > 1)
            .ToList();
        var plan = PlanCovering(manifest, kind, twins[^1].Seq);

        var first = plan.PrefixActions.ToList().FindIndex(action => action.Seq == twins[0].Seq);
        var second = plan.PrefixActions.ToList().FindIndex(action => action.Seq == twins[^1].Seq);

        Assert.True(first >= 0 && second > first);
        Assert.Equal(twins[0].Args, twins[^1].Args);
        Assert.True(plan.Authorises(first, twins[0]));
        Assert.True(plan.Authorises(second, twins[^1]));
        Assert.False(plan.Authorises(first, twins[^1]));
        Assert.False(plan.Authorises(second, twins[0]));
    }

    /// <summary>The first plan of this kind whose journey has passed that decision,
    /// found in the boundary list rather than through the reader under test.</summary>
    private static IBoundaryPlan PlanCovering(ReplayManifest manifest, string kind, int seq)
    {
        var boundary = manifest.Boundaries
            .Where(candidate => candidate.Kind == kind && candidate.AfterSeq >= seq)
            .MinBy(candidate => candidate.AfterSeq)!;
        var coordinate = boundary.IsFloorEntry
            ? $"{kind}:{boundary.Floor}"
            : $"{kind}:{boundary.Fight}";
        return BoundarySelector.Parse(coordinate).PlanFor(manifest);
    }
}

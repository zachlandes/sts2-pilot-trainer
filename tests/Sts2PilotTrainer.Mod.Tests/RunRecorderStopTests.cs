using System.Globalization;
using System.Reflection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The two things a recorder's refusal can be, told apart on the recording it writes.
///
/// A hole in the watch - a decision that went by unread - breaks the recording:
/// continuity <c>broken</c>, integrity untouched. A decision the recorder saw and could
/// not name stops it: integrity <c>unmapped</c>, continuity untouched, and what was
/// met written down raw. Every refusal used to be the first, so a recorder that met a
/// reward this format has no verb for reported a watch that stopped and started
/// again, which is not what happened, and the validator's sentence said so in those
/// words. Driven here through the same two entry points the recorder's patches reach,
/// against a store in a temporary directory, with the game loaded because the recorder
/// is a type of the mod.
/// </summary>
public sealed class RunRecorderStopTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"runmobile-stop-{Guid.NewGuid():N}", "Runmobile", "steam", "test", "profile1");

    /// <summary>Loading the engine assembly installs the resolver for the prepared
    /// game copy; nothing here reaches a game type through the engine first.</summary>
    static RunRecorderStopTests() => _ = typeof(EngineHost).Assembly;

    public RunRecorderStopTests()
    {
        Directory.CreateDirectory(_root);
        RunmobileStore.UseRootForTesting(_root);
    }

    public void Dispose()
    {
        RunmobileStore.UseRootForTesting(null);
        var sandbox = _root[.._root.IndexOf("Runmobile", StringComparison.Ordinal)];
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
    }

    [GameFact]
    public void ADecisionSeenAndNotNamedStopsTheRecordingWithItsWatchIntact()
    {
        var (recorder, capture, journalPath) = Recording();
        var seq = capture.NextSeq;

        recorder.StopAt(
            RunRecorder.MetAtMember(
                typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SelectLocalReward), "mystery",
                "This format has no verb for that kind of reward.",
                ("reward", "MysteryReward")),
            Reading(Floor(2), Digest(1), 4200));

        Assert.Equal(NativeSource.UnmappedIntegrity, capture.Integrity);
        Assert.Equal(NativeSource.ContinuousContinuity, capture.Continuity);
        Assert.Equal(RunCaptureState.Unmapped, capture.State);
        Assert.Empty(capture.Refusals);

        var stop = Assert.IsType<JournalStop>(capture.Stop);
        Assert.Equal(seq, stop.Decision.Seq);
        Assert.Equal(UnmappedDecision.MemberSeam, stop.Decision.Seam);
        Assert.Equal("RewardsSetSynchronizer.SelectLocalReward", stop.Decision.Name);
        Assert.Equal("mystery", stop.Decision.Discriminator);
        Assert.Equal("MysteryReward", stop.Decision.Args["reward"]);
        Assert.Equal(seq, stop.Decision.Evidence.ActionOrdinal);
        Assert.Equal(4200, stop.Decision.Evidence.RunClockMs);
        Assert.Equal("This format has no verb for that kind of reward.", stop.Decision.Evidence.Note);
        Assert.Equal(Digest(1), stop.BeforeDigest);

        // On the file as well as held, so a session continued from the journal stops
        // where this one did rather than recording past it.
        var journal = RunJournal.Parse(RunmobileStore.Read(journalPath)!);
        Assert.NotNull(journal.Stop);
        Assert.Equal(seq, journal.Stop!.Decision.Seq);
        Assert.Empty(journal.Refusals);
    }

    [GameFact]
    public void AHoleInTheWatchBreaksTheRecordingWithoutStoppingIt()
    {
        var (recorder, capture, journalPath) = Recording();

        recorder.Refuse("A MapMove could not be read: the engine never settled.");

        Assert.Equal(NativeSource.BrokenContinuity, capture.Continuity);
        Assert.Equal(NativeSource.CompleteIntegrity, capture.Integrity);
        Assert.Equal(RunCaptureState.Broken, capture.State);
        Assert.Null(capture.Stop);

        var journal = RunJournal.Parse(RunmobileStore.Read(journalPath)!);
        Assert.Null(journal.Stop);
        var refusal = Assert.Single(journal.Refusals);
        Assert.Contains("never settled", refusal.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The validator's sentence is the reader's account of what happened, and the two
    /// recordings get different ones: the stop names the decision the recorder met, and
    /// only the hole says the recorder stopped and started again.
    /// </summary>
    [GameFact]
    public void TheValidatorNamesTheDecisionForAStopAndTheHoleForABreak()
    {
        var (stopped, stoppedCapture, _) = Recording();
        stopped.StopAt(
            RunRecorder.MetAtMember(
                typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SelectLocalReward), "mystery",
                "This format has no verb for that kind of reward.",
                ("reward", "MysteryReward")),
            Reading(Floor(2), Digest(1), 4200));
        stoppedCapture.Finish("abandoned");
        var stopProblems = ManifestValidator.Validate(stoppedCapture.ToManifest()).Describe();

        Assert.Contains("source.native.integrity is 'unmapped'", stopProblems, StringComparison.Ordinal);
        Assert.Contains(
            "The recorder met: member RewardsSetSynchronizer.SelectLocalReward (mystery) with reward=MysteryReward",
            stopProblems, StringComparison.Ordinal);
        Assert.DoesNotContain("stopped and started again", stopProblems, StringComparison.Ordinal);

        var (broken, brokenCapture, _) = Recording();
        broken.Refuse("A MapMove could not be read: the engine never settled.");
        brokenCapture.Finish("abandoned");
        var breakProblems = ManifestValidator.Validate(brokenCapture.ToManifest()).Describe();

        Assert.Contains("stopped and started again", breakProblems, StringComparison.Ordinal);
        Assert.DoesNotContain("The recorder met", breakProblems, StringComparison.Ordinal);
    }

    /// <summary>
    /// Past a stop nothing is recorded, so nothing past it can go unrecorded: a
    /// refusal raised there would claim a hole in a watch that is not watching.
    /// </summary>
    [GameFact]
    public void ARefusalAfterAStopChangesNothing()
    {
        var (recorder, capture, journalPath) = Recording();
        recorder.StopAt(
            RunRecorder.MetAtMember(
                typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper), "MerchantOddEntry",
                "The entry is not on any shelf this recorder knows."),
            Reading(Floor(2), Digest(1), 4200));
        var written = RunmobileStore.Read(journalPath);

        recorder.Refuse("A MapMove could not be read: the engine never settled.");

        Assert.Equal(NativeSource.ContinuousContinuity, capture.Continuity);
        Assert.Equal(NativeSource.UnmappedIntegrity, capture.Integrity);
        Assert.Empty(capture.Refusals);
        Assert.Equal(written, RunmobileStore.Read(journalPath));
    }

    [GameFact]
    public void ARecordingStopsOnceAtTheFirstDecisionItCouldNotName()
    {
        var (recorder, capture, _) = Recording();
        recorder.StopAt(
            RunRecorder.MetAtMember(
                typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper), "MerchantOddEntry",
                "The entry is not on any shelf this recorder knows."),
            Reading(Floor(2), Digest(1), 4200));

        recorder.StopAt(
            RunRecorder.MetAtMember(
                typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SelectLocalReward), "mystery",
                "This format has no verb for that kind of reward."),
            Reading(Floor(2), Digest(1), 4300));

        Assert.Equal("MerchantEntry.OnTryPurchaseWrapper", capture.Stop!.Decision.Name);
        Assert.Equal(NativeSource.ContinuousContinuity, capture.Continuity);
    }

    /// <summary>
    /// A screen is answered from inside the decision that opened it, so a stop held
    /// beside the screen's answers stands at that decision's ordinal and with its
    /// before-reading, and the decision is not recorded: written without its answer it
    /// would be one a replay makes differently.
    /// </summary>
    [GameFact]
    public void AStopHeldBesideAScreensAnswersStopsTheDecisionThatOpenedTheScreen()
    {
        var (recorder, capture, _) = Recording();
        var seq = capture.NextSeq;
        var before = Reading(Floor(2), Digest(1), 4200);

        recorder.HoldScreenAnswerStop(RunRecorder.MetAtScreen(
            "NCardGridSelectionScreen", null,
            "The card is not one of the cards the screen offered.",
            ("card_id", "CARD.STRANGE"), ("offered", "3")));
        recorder.Commit(
            nameof(ActionVerb.ChooseEventOption),
            Args(("event_id", "EVENT.TEST"), ("option_index", "0"), ("option_key", "OPTION.TEST")),
            before,
            Reading(Floor(2, hp: 60), Digest(2), 4600));

        var stop = Assert.IsType<JournalStop>(capture.Stop);
        Assert.Equal(seq, capture.NextSeq);
        Assert.Equal(seq, stop.Decision.Seq);
        Assert.Equal(UnmappedDecision.PlayerChoiceSeam, stop.Decision.Seam);
        Assert.Equal("NCardGridSelectionScreen", stop.Decision.Name);
        Assert.Equal("CARD.STRANGE", stop.Decision.Args["card_id"]);
        Assert.Equal(Digest(1), stop.BeforeDigest);
        Assert.Equal(NativeSource.ContinuousContinuity, capture.Continuity);
    }

    /// <summary>
    /// A card the prompt never offered stops the recording at the entry point that
    /// asked, with the screen the client drew for it beside: a removal and a transform
    /// go through different entry points and open different screens, and a stop written
    /// against the base they share says less than the recorder saw.
    /// </summary>
    [GameFact]
    public void ACardThePromptNeverOfferedStopsAtTheEntryPointThatAsked()
    {
        EngineHost.Start();
        var (recorder, capture, _) = Recording();
        var cards = ModelDb.AllCards.Take(4).ToList();
        var offered = cards.Take(3).ToList();
        var stranger = cards[3];

        recorder.HoldCardScreenAnswers(
            "CardSelectCmd.FromDeckForTransformation", "NDeckTransformSelectScreen", offered, [stranger]);
        recorder.Commit(
            nameof(ActionVerb.ChooseEventOption),
            Args(("event_id", "EVENT.TEST"), ("option_index", "0"), ("option_key", "OPTION.TEST")),
            Reading(Floor(2), Digest(1), 4200),
            Reading(Floor(2, hp: 60), Digest(2), 4600));

        var stop = Assert.IsType<JournalStop>(capture.Stop);
        Assert.Equal(UnmappedDecision.PlayerChoiceSeam, stop.Decision.Seam);
        Assert.Equal("CardSelectCmd.FromDeckForTransformation", stop.Decision.Name);
        Assert.Equal("NDeckTransformSelectScreen", stop.Decision.Discriminator);
        Assert.Equal(stranger.Id.ToString(), stop.Decision.Args["card_id"]);
        Assert.Equal("3", stop.Decision.Args["offered"]);
        Assert.Equal(NativeSource.UnmappedIntegrity, capture.Integrity);
        Assert.Equal(NativeSource.ContinuousContinuity, capture.Continuity);
    }

    // ── What a card prompt's answer becomes ───────────────────────────────────────

    /// <summary>
    /// A prompt answered with what it asked for is written as one
    /// <c>SelectCardFromScreen</c> per pick, at the pick's position in the list the
    /// prompt offered, after the decision that opened it.
    /// </summary>
    [GameFact]
    public void APromptAnsweredWithWhatItAskedForIsWrittenAsPicksAfterTheDecision()
    {
        EngineHost.Start();
        var (recorder, capture, _) = Recording();
        var offered = ModelDb.AllCards.Take(4).ToList();
        var prompt = Offered(nameof(CardSelectCmd.FromHand), "NPlayerHand", 1, 1, offered);

        recorder.HoldCardPromptAnswers(prompt, [offered[2]]);
        recorder.Commit(
            nameof(ActionVerb.ChooseEventOption),
            Args(("event_id", "EVENT.TEST"), ("option_index", "0"), ("option_key", "OPTION.TEST")),
            Reading(Floor(2), Digest(1), 4200),
            Reading(Floor(2, hp: 60), Digest(2), 4600));

        Assert.Null(capture.Stop);
        capture.Finish("abandoned");
        var actions = capture.ToManifest().Actions;
        var pick = actions[^1];
        Assert.Equal(ActionVerb.SelectCardFromScreen, pick.Verb);
        Assert.Equal(offered[2].Id.ToString(), pick.Args["card_id"]);
        Assert.Equal("2", pick.Args["option_index"]);
        Assert.Equal(ActionVerb.ChooseEventOption, actions[^2].Verb);
    }

    /// <summary>
    /// A prompt the engine answered for itself is no decision: nothing is written
    /// and nothing stops, whatever the engine took.
    /// </summary>
    [GameFact]
    public void APromptTheEngineAnsweredItselfWritesNothing()
    {
        EngineHost.Start();
        var (recorder, capture, _) = Recording();
        var prompt = new CardPrompts.Prompt(
            nameof(CardSelectCmd.FromCombatPile), "NCombatPileCardSelectScreen", 1, 1, () => null);
        prompt.Derive();
        Assert.Equal(CardPrompts.PromptState.EngineAnswered, prompt.State);

        recorder.HoldCardPromptAnswers(prompt, [ModelDb.AllCards.First()]);
        recorder.Commit(
            nameof(ActionVerb.ChooseEventOption),
            Args(("event_id", "EVENT.TEST"), ("option_index", "0"), ("option_key", "OPTION.TEST")),
            Reading(Floor(2), Digest(1), 4200),
            Reading(Floor(2, hp: 60), Digest(2), 4600));

        Assert.Null(capture.Stop);
        capture.Finish("abandoned");
        Assert.Equal(ActionVerb.ChooseEventOption, capture.ToManifest().Actions[^1].Verb);
    }

    /// <summary>
    /// A prompt that asked for a range is written as its picks and then one
    /// <c>ConfirmCardScreen</c> with their count: an "up to N" answered with fewer is
    /// the picks it took and a confirmation of that many, and a choose-a-card prompt
    /// declined is a confirmation of none directly after the decision that opened it.
    /// Neither stops the recording, and both replay as the answer the player gave.
    /// </summary>
    [GameFact]
    public void ARangePromptIsWrittenAsItsPicksAndTheirConfirmationAndADeclineAsNone()
    {
        EngineHost.Start();
        var offered = ModelDb.AllCards.Take(3).ToList();

        var (partial, partialCapture, _) = Recording();
        partial.HoldCardPromptAnswers(
            Offered(nameof(CardSelectCmd.FromHand), "NPlayerHand", 0, 3, offered), [offered[1]]);
        partial.Commit(
            nameof(ActionVerb.PlayCard),
            Args(("card_id", "CARD.PURITY"), ("hand_index", "0")),
            Reading(Floor(2), Digest(1), 4200),
            Reading(Floor(2, hp: 60), Digest(2), 4600));

        Assert.Null(partialCapture.Stop);
        partialCapture.Finish("abandoned");
        var actions = partialCapture.ToManifest().Actions;
        Assert.Equal(ActionVerb.PlayCard, actions[^3].Verb);
        Assert.Equal(ActionVerb.SelectCardFromScreen, actions[^2].Verb);
        Assert.Equal(offered[1].Id.ToString(), actions[^2].Args["card_id"]);
        Assert.Equal("1", actions[^2].Args["option_index"]);
        Assert.Equal(ActionVerb.ConfirmCardScreen, actions[^1].Verb);
        Assert.Equal(new Dictionary<string, string> { ["count"] = "1" }, actions[^1].Args);
        Assert.Equal(NativeSource.CompleteIntegrity, partialCapture.Integrity);

        var (declined, declinedCapture, _) = Recording();
        declined.HoldCardPromptAnswers(
            Offered(nameof(CardSelectCmd.FromChooseACardScreen), "NChooseACardSelectionScreen", 0, 1, offered), []);
        declined.Commit(
            nameof(ActionVerb.PlayCard),
            Args(("card_id", "CARD.DISCOVERY"), ("hand_index", "0")),
            Reading(Floor(2), Digest(1), 4200),
            Reading(Floor(2, hp: 60), Digest(2), 4600));

        Assert.Null(declinedCapture.Stop);
        declinedCapture.Finish("abandoned");
        actions = declinedCapture.ToManifest().Actions;
        Assert.Equal(ActionVerb.PlayCard, actions[^2].Verb);
        Assert.Equal(ActionVerb.ConfirmCardScreen, actions[^1].Verb);
        Assert.Equal("0", actions[^1].Args["count"]);
        Assert.Equal(NativeSource.CompleteIntegrity, declinedCapture.Integrity);
    }

    /// <summary>
    /// A confirmation is written for a range prompt only. A prompt that asked for
    /// exactly N and was answered with another count is one the format has no way to
    /// state, so it still stops the recording at the decision that opened it, naming
    /// the count; and a range prompt answered outside its range is met the same way,
    /// because no screen of this build confirms such an answer.
    /// </summary>
    [GameFact]
    public void ACountOutsideWhatThePromptAskedForStopsTheRecordingNamingIt()
    {
        EngineHost.Start();
        var offered = ModelDb.AllCards.Take(3).ToList();

        var (exact, exactCapture, _) = Recording();
        exact.HoldCardPromptAnswers(
            Offered(nameof(CardSelectCmd.FromDeckForEnchantment), "NDeckEnchantSelectScreen", 2, 2, offered),
            [offered[1]]);
        exact.Commit(
            nameof(ActionVerb.ChooseEventOption),
            Args(("event_id", "EVENT.TEST"), ("option_index", "0"), ("option_key", "OPTION.TEST")),
            Reading(Floor(2), Digest(1), 4200),
            Reading(Floor(2, hp: 60), Digest(2), 4600));

        var stop = Assert.IsType<JournalStop>(exactCapture.Stop);
        Assert.Equal(UnmappedDecision.PlayerChoiceSeam, stop.Decision.Seam);
        Assert.Equal("CardSelectCmd.FromDeckForEnchantment", stop.Decision.Name);
        Assert.Equal("1", stop.Decision.Args["chosen"]);
        Assert.Equal("2", stop.Decision.Args["min_select"]);
        Assert.Equal("2", stop.Decision.Args["max_select"]);
        Assert.Equal(offered[1].Id.ToString(), stop.Decision.Args["card_ids"]);
        Assert.Contains("exactly that many", stop.Decision.Evidence.Note, StringComparison.Ordinal);
        Assert.Equal(NativeSource.UnmappedIntegrity, exactCapture.Integrity);
        Assert.Equal(NativeSource.ContinuousContinuity, exactCapture.Continuity);

        var (over, overCapture, _) = Recording();
        over.HoldCardPromptAnswers(
            Offered(nameof(CardSelectCmd.FromHand), "NPlayerHand", 0, 1, offered), [offered[0], offered[1]]);
        over.Commit(
            nameof(ActionVerb.PlayCard),
            Args(("card_id", "CARD.PURITY"), ("hand_index", "0")),
            Reading(Floor(2), Digest(1), 4200),
            Reading(Floor(2, hp: 60), Digest(2), 4600));

        stop = Assert.IsType<JournalStop>(overCapture.Stop);
        Assert.Equal("CardSelectCmd.FromHand", stop.Decision.Name);
        Assert.Equal("2", stop.Decision.Args["chosen"]);
        Assert.Equal("1", stop.Decision.Args["max_select"]);
        Assert.Contains("outside the range", stop.Decision.Evidence.Note, StringComparison.Ordinal);
    }

    /// <summary>
    /// A prompt that settled before the recorder saw the engine read it has no
    /// offered list to place the answer in, and one asked while another was still
    /// open cannot be told from it; both stop the recording rather than guess.
    /// </summary>
    [GameFact]
    public void APromptNeverReadOrOpenedOverAnotherStopsTheRecording()
    {
        EngineHost.Start();
        var offered = ModelDb.AllCards.Take(3).ToList();

        var (unread, unreadCapture, _) = Recording();
        var neverRead = new CardPrompts.Prompt(nameof(CardSelectCmd.FromHand), "NPlayerHand", 1, 1, () => offered);
        Assert.Equal(CardPrompts.PromptState.Asked, neverRead.State);
        unread.HoldCardPromptAnswers(neverRead, [offered[0]]);
        unread.Commit(
            nameof(ActionVerb.ChooseEventOption),
            Args(("event_id", "EVENT.TEST"), ("option_index", "0"), ("option_key", "OPTION.TEST")),
            Reading(Floor(2), Digest(1), 4200),
            Reading(Floor(2, hp: 60), Digest(2), 4600));

        var stop = Assert.IsType<JournalStop>(unreadCapture.Stop);
        Assert.Equal("CardSelectCmd.FromHand", stop.Decision.Name);
        Assert.Contains("before the engine paused", stop.Decision.Evidence.Note, StringComparison.Ordinal);

        var (conflicted, conflictedCapture, _) = Recording();
        var over = Offered(nameof(CardSelectCmd.FromCombatPile), "NCombatPileCardSelectScreen", 1, 1, offered);
        over.Conflict = nameof(CardSelectCmd.FromHand);
        conflicted.HoldCardPromptAnswers(over, [offered[0]]);
        conflicted.Commit(
            nameof(ActionVerb.ChooseEventOption),
            Args(("event_id", "EVENT.TEST"), ("option_index", "0"), ("option_key", "OPTION.TEST")),
            Reading(Floor(2), Digest(1), 4200),
            Reading(Floor(2, hp: 60), Digest(2), 4600));

        stop = Assert.IsType<JournalStop>(conflictedCapture.Stop);
        Assert.Equal("CardSelectCmd.FromCombatPile", stop.Decision.Name);
        Assert.Equal("CardSelectCmd.FromHand", stop.Decision.Args["other_prompt"]);
    }

    /// <summary>A prompt the engine has read, offering these cards.</summary>
    private static CardPrompts.Prompt Offered(
        string entryPoint, string screen, int minSelect, int maxSelect, IReadOnlyList<CardModel> cards)
    {
        var prompt = new CardPrompts.Prompt(entryPoint, screen, minSelect, maxSelect, () => cards);
        prompt.Derive();
        Assert.Equal(CardPrompts.PromptState.Offered, prompt.State);
        return prompt;
    }

    /// <summary>
    /// A stop at a constructor is one dotted name. The runtime spells a constructor
    /// <c>.ctor</c>, and joined to its type with another dot the manifest read
    /// <c>DiscardPotionGameAction..ctor</c>, a member no later build can look up.
    /// </summary>
    [GameFact]
    public void AStopAtAConstructorIsOneDottedName()
    {
        var (recorder, capture, _) = Recording();

        recorder.StopAt(
            RunRecorder.MetAtMember(
                typeof(DiscardPotionGameAction), ConstructorInfo.ConstructorName, null,
                "The slot holds nothing this recorder can see.",
                ("slot_index", "2")),
            Reading(Floor(2), Digest(1), 4200));

        var stop = Assert.IsType<JournalStop>(capture.Stop);
        Assert.Equal("DiscardPotionGameAction.ctor", stop.Decision.Name);

        capture.Finish("abandoned");
        Assert.Contains(
            "member DiscardPotionGameAction.ctor with slot_index=2",
            ManifestValidator.Validate(capture.ToManifest()).Describe(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A stop inside a fight names the game's own action, never the format's verb.
    ///
    /// The observer opens a step under the format verb it translates the action into,
    /// and a stop that wrote that verb as what was met would name nothing in the game:
    /// no later build can read <c>PlayCard</c> back to a member. So the recorder
    /// writes the <c>GameAction</c> type the observer met, and the verb beside it as
    /// the discriminator, with what the observer did resolve and the sentence saying
    /// what it could not.
    /// </summary>
    [GameFact]
    public void AStopInsideAFightNamesTheGamesOwnActionAndNotTheFormatVerb()
    {
        var (recorder, capture, _) = Recording();
        var seq = capture.NextSeq;

        recorder.StopAtFightStep(
            "PlayCardAction", "PlayCard", Args(("card_id", "CARD.BASH")), Reading(Floor(2), Digest(1), 5000),
            "A CARD.BASH was played and the hand this recorder can see does not hold it, so the recording " +
            "cannot say which position it came from.");

        Assert.Equal(NativeSource.UnmappedIntegrity, capture.Integrity);
        Assert.Equal(NativeSource.ContinuousContinuity, capture.Continuity);
        Assert.Empty(capture.Refusals);

        var stop = Assert.IsType<JournalStop>(capture.Stop);
        Assert.Equal(seq, stop.Decision.Seq);
        Assert.Equal(UnmappedDecision.MemberSeam, stop.Decision.Seam);
        Assert.Equal("PlayCardAction", stop.Decision.Name);
        Assert.Equal("PlayCard", stop.Decision.Discriminator);
        Assert.Equal("CARD.BASH", stop.Decision.Args["card_id"]);
        Assert.False(stop.Decision.Args.ContainsKey("hand_index"));
        Assert.Contains("does not hold it", stop.Decision.Evidence.Note);

        // And the validator's sentence names the action the game ran.
        capture.Finish("abandoned");
        var problems = ManifestValidator.Validate(capture.ToManifest()).Describe();
        Assert.Contains(
            "The recorder met: member PlayCardAction (PlayCard) with card_id=CARD.BASH",
            problems, StringComparison.Ordinal);
        Assert.DoesNotContain("stopped and started again", problems, StringComparison.Ordinal);
    }

    // ── Whose answer a card reward's is ──────────────────────────────────────────

    /// <summary>
    /// A card reward's answer is the card-reward decision's and nobody else's. A
    /// decision of another kind committed while one is on the shelf leaves it there
    /// and writes nothing of it - the pump commits behind a settle, and the answer can
    /// arrive while an earlier decision is still settling - and the card-reward
    /// decision then takes it as its own verb and arguments.
    /// </summary>
    [GameFact]
    public void ACardRewardsAnswerWaitsForTheCardRewardDecision()
    {
        EngineHost.Start();
        var (recorder, capture, _) = Recording();
        var offered = ModelDb.AllCards.Take(3).ToList();
        var alternatives = new List<CardRewardAlternative>
        {
            new("Skip", PostAlternateCardRewardAction.EndSelectionAndDoNotCompleteReward),
        };

        recorder.HoldCardRewardAnswer(offered, alternatives, 3);
        recorder.Commit(
            nameof(ActionVerb.ClaimReward), Args(("reward_type", "gold")),
            Reading(Floor(2), Digest(1), 4200), Reading(Floor(2, hp: 60), Digest(2), 4600));
        recorder.Commit(
            nameof(ActionVerb.TakeCard), Args(),
            Reading(Floor(2, hp: 60), Digest(2), 4700), Reading(Floor(2, hp: 60), Digest(2), 4900));

        Assert.Null(capture.Stop);
        Assert.Empty(capture.Refusals);
        capture.Finish("abandoned");
        var actions = capture.ToManifest().Actions;
        Assert.Equal(
            [ActionVerb.ChooseNeowBlessing, ActionVerb.ClaimReward, ActionVerb.TakeCardRewardAlternative],
            actions.Select(action => action.Verb));
        Assert.Equal("Skip", actions[^1].Args["option_id"]);
        Assert.Equal("3", actions[^1].Args["option_index"]);
    }

    /// <summary>
    /// Exactly one thing comes off a card reward per click, so a second answer on the
    /// shelf when the decision commits is refused rather than written: a duplicate
    /// would replay as a decision nobody made. A player who presses Skip and opens the
    /// same reward again is two clicks and two decisions, each with its one answer.
    /// </summary>
    [GameFact]
    public void ASecondAnswerToOneCardRewardIsRefused()
    {
        EngineHost.Start();
        var (recorder, capture, _) = Recording();
        var offered = ModelDb.AllCards.Take(3).ToList();
        var alternatives = new List<CardRewardAlternative>
        {
            new("Skip", PostAlternateCardRewardAction.EndSelectionAndDoNotCompleteReward),
        };

        recorder.HoldCardRewardAnswer(offered, alternatives, 3);
        recorder.HoldCardRewardAnswer(offered, alternatives, 3);
        recorder.Commit(
            nameof(ActionVerb.TakeCard), Args(),
            Reading(Floor(2), Digest(1), 4200), Reading(Floor(2), Digest(1), 4600));

        Assert.Contains(
            capture.Refusals,
            refusal => refusal.Reason.Contains("saw 2 answer(s) to it", StringComparison.Ordinal));
        Assert.Equal(NativeSource.BrokenContinuity, capture.Continuity);

        var (again, twice, _) = Recording();
        again.HoldCardRewardAnswer(offered, alternatives, 3);
        again.Commit(
            nameof(ActionVerb.TakeCard), Args(),
            Reading(Floor(2), Digest(1), 4200), Reading(Floor(2), Digest(1), 4600));
        again.HoldCardRewardAnswer(offered, alternatives, 3);
        again.Commit(
            nameof(ActionVerb.TakeCard), Args(),
            Reading(Floor(2), Digest(1), 4700), Reading(Floor(2), Digest(1), 4900));

        Assert.Empty(twice.Refusals);
        twice.Finish("abandoned");
        Assert.Equal(
            [ActionVerb.TakeCardRewardAlternative, ActionVerb.TakeCardRewardAlternative],
            twice.ToManifest().Actions.TakeLast(2).Select(action => action.Verb));
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────────

    /// <summary>A recorder over a capture one decision in, with its journal on the
    /// store the way <c>Attach</c> leaves it.</summary>
    private static (RunRecorder Recorder, RunCapture Capture, string JournalPath) Recording()
    {
        var capture = RunCapture.Begin(Start());
        capture.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            new StateReading(Floor(1), Digest(-1)), new StateReading(Floor(2), Digest(1)), runClockMs: 1000);

        var journalPath = $"{RunRecorder.RecordingsDirectory}/{capture.RunId}{RunJournal.FileExtension}";
        RunmobileStore.Write(journalPath, capture.Journal.Render());
        return (new RunRecorder(capture, journalPath), capture, journalPath);
    }

    private static RunRecorder.TakenReading Reading(
        IReadOnlyDictionary<string, string> sample, string digest, int clock) => new(sample, digest, clock);

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
                HostRoster()),
        },
        State = Floor(1),
        Digest = Digest(-1),
        RunClockMs = 0,
    };

    /// <summary>A roster shaped like the real one: the shell patches the profile write
    /// and the members it watches, and a roster with none of ours on it is the broken
    /// reading the preflight refuses.</summary>
    private static PatchRoster HostRoster() => new()
    {
        Members =
        [
            new PatchedMember(
                "MegaCrit.Sts2.Core.Saving.ProgressSaveManager", "SaveProgressFile()", [PatchRoster.HostOwnerId],
                Prefixes: 1, Postfixes: 0, Transpilers: 0, Finalizers: 0),
            new PatchedMember(
                "MegaCrit.Sts2.Core.Run.RunManager", "StartNewSingleplayerRun(RunSetup, Boolean)",
                [PatchRoster.HostOwnerId], Prefixes: 1, Postfixes: 0, Transpilers: 0, Finalizers: 0),
        ],
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

    private static string Digest(int seq) =>
        "sha256:" + (seq + 1).ToString("x2", CultureInfo.InvariantCulture).PadLeft(64, 'a');

    private static IReadOnlyDictionary<string, string> Args(params (string Key, string Value)[] args) =>
        args.ToDictionary(arg => arg.Key, arg => arg.Value, StringComparer.Ordinal);
}

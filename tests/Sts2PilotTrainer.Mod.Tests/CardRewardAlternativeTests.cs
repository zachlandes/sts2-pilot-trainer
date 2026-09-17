using System.Globalization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// Every answer a card reward can be given past its cards, obtained from the
/// engine's own producer and answered through the driver at a real loot screen.
///
/// One row per value of <c>PostAlternateCardRewardAction</c>, because the meaning of
/// an alternative is the value the engine attaches to it and nothing this project
/// decides: the loot screen's Skip ends the selection and leaves the reward on the
/// screen, Pael's Wing's sacrifice ends it and completes the reward, Driftwood's
/// reroll keeps it open for an answer the recorder never writes, and no producer on
/// this build yields <c>None</c>. The card-reward Skip defect lived in a test that
/// wrote the alternative list by hand with the one value the handler happened to
/// expect; a row here is obtained from <c>CardRewardAlternative.Generate</c> over a
/// reward the engine put on the loot screen of a fight the run won, with the relic
/// that produces it granted through the engine's own command, so the handler is held
/// to every alternative the build can offer rather than to the one somebody thought of.
/// </summary>
public sealed class CardRewardAlternativeTests
{
    /// <summary>The Ironclad's first fight on the probe seed, won by playing what is
    /// playable and ending the turn; a bound that says so if it stops being enough.</summary>
    private const int TurnLimit = 30;

    /// <summary>Rows are the value's name rather than the value: a game type in an
    /// attribute is loaded at discovery, before the engine's resolver has said where
    /// the game is, and on a runner without the game there is nothing to load.</summary>
    [GameTheory]
    [InlineData("EndSelectionAndDoNotCompleteReward")]
    [InlineData("EndSelectionAndCompleteReward")]
    [InlineData("DoNothing")]
    [InlineData("None")]
    public void EveryAlternativeTheBuildProducesIsAnsweredAsTheEngineMeansIt(string valueName) =>
        HeadlessRuns.WithARun(session =>
        {
            var value = Enum.Parse<PostAlternateCardRewardAction>(valueName);
            Assert.Contains(value, Enum.GetValues<PostAlternateCardRewardAction>());
            var player = session.RunState.Players[0];
            using var driver = new RunDriver(session);
            driver.ImproviseUnrecordedCardSelections();

            // The relic that produces the alternative, through the engine's own obtain
            // command, before the fight whose loot offers it
            switch (value)
            {
                case PostAlternateCardRewardAction.EndSelectionAndCompleteReward:
                    RelicCmd.Obtain(ModelDb.Relic<PaelsWing>().ToMutable(), player).GetAwaiter().GetResult();
                    break;
                case PostAlternateCardRewardAction.DoNothing:
                    RelicCmd.Obtain(ModelDb.Relic<Driftwood>().ToMutable(), player).GetAwaiter().GetResult();
                    break;
            }

            HeadlessRuns.EnterTheFirstFight(driver, session);
            var seq = WinTheFight(driver, session, 2);
            var cardReward = driver.OpenCardReward
                ?? throw new InvalidOperationException("The won fight put no card reward on the loot screen.");
            var alternatives = CardRewardAlternative.Generate(cardReward);

            if (value == PostAlternateCardRewardAction.None)
            {
                Assert.DoesNotContain(alternatives, alternative => alternative.AfterSelected == value);
                return;
            }

            var produced = Assert.Single(alternatives, alternative => alternative.AfterSelected == value);
            var index = cardReward.Cards.Count() + alternatives.ToList().IndexOf(produced);
            var answer = HeadlessRuns.Record(
                seq, ActionVerb.TakeCardRewardAlternative,
                ("option_id", produced.OptionId), ("option_index", HeadlessRuns.Number(index)));

            if (value == PostAlternateCardRewardAction.DoNothing)
            {
                // The reroll keeps the selection open for an answer no recording carries,
                // and the selector refuses it by name rather than guessing one
                var refusal = Assert.Throws<EngineException>(() => driver.Apply(answer));
                Assert.Contains("'REROLL'", refusal.Message, StringComparison.Ordinal);
                Assert.Contains("keeps the reward's selection open", refusal.Message, StringComparison.Ordinal);
                Assert.False(cardReward.SuccessfullySelected);
                return;
            }

            driver.Apply(answer);

            var completes = value == PostAlternateCardRewardAction.EndSelectionAndCompleteReward;
            Assert.Equal(completes, cardReward.SuccessfullySelected);
            Assert.Equal(completes, !driver.UnclaimedRewardKinds.Contains("card", StringComparer.Ordinal));
        });

    /// <summary>The rows above are the whole enum on this build; a fifth value is a
    /// row nobody has written and fails here rather than going unanswered.</summary>
    [GameFact]
    public void TheRowsAreEveryValueTheBuildDeclares()
    {
        Assert.Equal(
            ["None", "EndSelectionAndDoNotCompleteReward", "EndSelectionAndCompleteReward", "DoNothing"],
            Enum.GetNames<PostAlternateCardRewardAction>());
    }

    /// <summary>Plays the first playable card until none is, then ends the turn, until
    /// the fight is over; the next free ordinal is returned.</summary>
    private static int WinTheFight(RunDriver driver, GameSession session, int seq)
    {
        var player = session.RunState.Players[0];
        for (var turn = 0; turn < TurnLimit && Outcome(session) == "in_progress"; turn++)
        {
            while (Outcome(session) == "in_progress")
            {
                var hand = player.PlayerCombatState!.Hand.Cards.ToList();
                var index = hand.FindIndex(card => card.CanPlay(out _, out _));
                if (index < 0) break;

                var card = hand[index];
                var alive = CombatManager.Instance!.DebugOnlyGetState()!.Enemies.Count(enemy => enemy is { IsAlive: true });
                (string, string)[] target = card.TargetType == TargetType.AnyEnemy && alive > 1 ? [("target_index", "0")] : [];
                driver.Apply(HeadlessRuns.Record(
                    seq++, ActionVerb.PlayCard,
                    [("card_id", card.Id.ToString()), ("hand_index", HeadlessRuns.Number(index)), .. target]));
            }

            if (Outcome(session) != "in_progress") break;
            driver.Apply(HeadlessRuns.Record(seq++, ActionVerb.EndTurn));
        }

        Assert.Equal("victory", Outcome(session));
        return seq;
    }

    private static string Outcome(GameSession session) => HeadlessRuns.Field(session, "combat.outcome");
}

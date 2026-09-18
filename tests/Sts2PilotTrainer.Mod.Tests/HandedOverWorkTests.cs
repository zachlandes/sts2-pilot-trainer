using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;
using static Sts2PilotTrainer.Arbiter.Tests.HeadlessRuns;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What the driver does with a purchase's, a rest's or a claim's work once it has
/// handed the run to the player, held with a stand-in for that work: a task shaped
/// the way Orrery's and Neow's Bones' is - it offers a rewards set of its own through
/// the engine's own <see cref="RewardsSet.Offer"/>, which returns once the set is
/// answered - and then ends the way the test says.
///
/// The defect this holds was found in review: the task was read only at the start of
/// the next action, so a refusal it ended in went unread wherever the decision that
/// answered its set was the recording's last, and the replay reported verified.
/// </summary>
public sealed class HandedOverWorkTests
{
    /// <summary>The set is answered by the recording's next decision and nothing
    /// follows it, so the end of that answering step is the only place the refusal
    /// the work ended in can be raised.</summary>
    [GameTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkThatEndsInARefusalOnceItsSetIsAnsweredIsRaisedAtTheEndOfTheAnsweringStep(bool faulted)
    {
        WithARun(session =>
        {
            using var driver = new RunDriver(session);
            driver.EnterFirstRoom();

            var ending = new TaskCompletionSource<bool>();
            if (faulted) ending.SetException(new InvalidOperationException("the engine threw inside the claim"));
            else ending.SetResult(false);

            var claim = Record(0, ActionVerb.ClaimReward, ("reward_type", "relic"));
            var handedOver = driver.SettleOrHandOver(WorkOfferingASet(session, ending.Task), onOfferBefore: null, claim);
            Assert.Null(handedOver);
            Assert.Equal(["gold"], driver.UnclaimedRewardKinds);

            var answer = () => driver.Apply(Record(1, ActionVerb.SkipRewards));
            if (faulted)
            {
                Assert.Throws<InvalidOperationException>(answer);
            }
            else
            {
                var refusal = Assert.Throws<EngineException>(answer);
                Assert.Contains("Action 0 (ClaimReward)", refusal.Message, StringComparison.Ordinal);
            }
        });
    }

    /// <summary>A set already on offer before the work began is not the work's to
    /// wait on - a claim off an open loot screen has that screen's set beside it - so
    /// such work is awaited rather than handed over.</summary>
    [GameFact]
    public void WorkBegunUnderASetAlreadyOnOfferIsAwaited()
    {
        WithARun(session =>
        {
            using var driver = new RunDriver(session);
            driver.EnterFirstRoom();

            // Offered the way the theory above offers one, so the driver holds it as
            // the set on offer; the task is the set's own completion and is not waited on
            var open = ASetOfOneGoldReward(session);
            _ = open.Offer();
            Assert.Equal(["gold"], driver.UnclaimedRewardKinds);

            // Finishes on its own a moment after the hand-over decision; long enough
            // that it is still open when the driver decides
            var work = Task.Delay(100).ContinueWith(_ => false);
            var settled = driver.SettleOrHandOver(work, onOfferBefore: open, Record(0, ActionVerb.ClaimReward, ("reward_type", "relic")));
            Assert.False(settled);
        });
    }

    /// <summary>Work shaped like a relic's own reward-offering task: puts a set on
    /// offer through the engine, returns once the set is answered, and ends the way
    /// <paramref name="ending"/> says.</summary>
    private static async Task<bool> WorkOfferingASet(GameSession session, Task<bool> ending)
    {
        await ASetOfOneGoldReward(session).Offer();
        return await ending;
    }

    /// <summary>A set of one fixed gold reward, which rolls nothing and which the
    /// loot screen's Skip answers; an empty set the engine completes as it begins
    /// it, so it would never be on offer.</summary>
    private static RewardsSet ASetOfOneGoldReward(GameSession session)
    {
        var player = session.RunState.Players[0];
        return new RewardsSet(player).WithCustomRewards([new GoldReward(10, player, wasGoldStolenBack: false)]);
    }
}

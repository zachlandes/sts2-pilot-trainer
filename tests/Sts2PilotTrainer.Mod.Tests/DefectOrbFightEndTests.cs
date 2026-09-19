using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// A fight the end of the player's turn ends - the Defect's Lightning orb killing the
/// last enemy from its end-of-turn passive - ends cleanly headlessly, with the game's
/// own combat-ended event raised and nothing logged as an error.
///
/// Two things stood in the way, both this host's. The bootstrap's one IL patch made
/// the game's own <c>WaitUntilQueueIsEmptyOrWaitingOnNonPlayerDrivenAction</c> return
/// at once, so the turn loop ran through to the fight's end inside the ended turn's
/// own finish, before the executor had popped it, and <c>ActionQueueSet.CombatEnded</c>
/// cancelled that finished, unpopped action, which the executor then failed to pop.
/// And <c>GameAction.Cancel</c> defers the cancelled action's completion through
/// <c>Callable.From&lt;TResult&gt;(Func)</c>, which the vendored stubs had not got,
/// so that cancel threw <c>MissingMethodException</c> out of the turn loop and
/// <c>CombatEnded</c> was never raised. Both are gone
/// (<c>docs/headless-fidelity.md</c>, <c>third_party/godot-stubs/CHANGES.md</c>);
/// the first fact holds the fight, and the second holds the cancel, which the fight
/// no longer reaches: an action still queued behind the one that ends the fight is
/// what the end of combat cancels. The recorder saw the fault as a decision begun
/// before the ended turn was sampled and refused fourteen of seventeen Defect walks.
/// </summary>
public sealed class DefectOrbFightEndTests
{
    public DefectOrbFightEndTests() => EngineHost.Start();

    private const string OneFuzzyWurm = "ENCOUNTER.FUZZY_WURM_CRAWLER_WEAK";

    [GameFact]
    public void AFightTheLightningOrbEndsAtTheEndOfTheTurnEndsCleanly() =>
        HeadlessFights.InAFight("CHARACTER.DEFECT", OneFuzzyWurm, (session, driver) =>
        {
            var player = session.RunState.Players[0];
            var zap = HeadlessFights.Deal(ModelDb.Card<Zap>(), player);
            HeadlessFights.Play(driver, session, zap, 10);
            Assert.NotEmpty(player.PlayerCombatState!.OrbQueue.Orbs);

            // Wounded to less than the orb's passive deals, so the end of the turn is
            // what kills it
            var enemy = HeadlessFights.LivingEnemies().Single();
            HeadlessFights.WoundTo(enemy, 1, player.Creature);

            var ended = 0;
            void CombatEnded(CombatRoom room) => ended++;
            CombatManager.Instance!.CombatEnded += CombatEnded;
            IReadOnlyList<string> errors;
            try
            {
                errors = HeadlessFights.ErrorsLoggedDuring(() => driver.Apply(HeadlessRuns.Record(11, ActionVerb.EndTurn)));
            }
            finally
            {
                CombatManager.Instance!.CombatEnded -= CombatEnded;
            }

            Assert.Equal("victory", HeadlessRuns.Field(session, "combat.outcome"));
            Assert.Equal(1, ended);
            Assert.Empty(errors);
        });

    [GameFact]
    public void AnActionStillQueuedWhenTheFightEndsIsCancelledCleanly() =>
        HeadlessFights.InAFight("CHARACTER.DEFECT", OneFuzzyWurm, (session, driver) =>
        {
            var player = session.RunState.Players[0];
            var zap = HeadlessFights.Deal(ModelDb.Card<Zap>(), player);
            HeadlessFights.Play(driver, session, zap, 10);
            var enemy = HeadlessFights.LivingEnemies().Single();
            HeadlessFights.WoundTo(enemy, 1, player.Creature);

            // A card play queued behind the ended turn as the executor picks the turn
            // up, the way two clicks land in one frame: the turn ends the fight from
            // the orb's passive, and the play is still queued when the engine cancels
            // what is left of the player's combat actions (GameAction.Cancel)
            var queues = RunManager.Instance.ActionQueueSet;
            var executor = RunManager.Instance.ActionExecutor;
            var strike = player.PlayerCombatState!.Hand.Cards.First(card => card.Type == CardType.Attack);
            var play = new PlayCardAction(strike, enemy);
            void QueueThePlayBehind(GameAction action)
            {
                if (action is EndPlayerTurnAction) queues.EnqueueWithoutSynchronizing(play);
            }

            executor.BeforeActionExecuted += QueueThePlayBehind;
            IReadOnlyList<string> errors;
            try
            {
                errors = HeadlessFights.ErrorsLoggedDuring(() => driver.Apply(HeadlessRuns.Record(11, ActionVerb.EndTurn)));
            }
            finally
            {
                executor.BeforeActionExecuted -= QueueThePlayBehind;
            }

            Assert.Equal("victory", HeadlessRuns.Field(session, "combat.outcome"));
            Assert.Equal(GameActionState.Canceled, play.State);
            Assert.Empty(errors);
        });
}

using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// A card the engine plays in full and then returns to the hand is a play that took
/// effect, and the driver says so.
///
/// <c>RunDriver.PlayCard</c> used to read whether a play happened off the hand: a
/// card still at the same index afterwards was taken to mean the engine had declined
/// it. Two cards on this build go back to the hand after a play the engine executed -
/// Particle Wall, whose own result location is the hand
/// (<c>ParticleWall.GetResultLocationForCardPlay</c>), and any 0-cost attack while
/// Feral is up (<c>FeralPower.ModifyCardPlayResultLocation</c>) - and the driver
/// refused both, which refused every Regent recording that played the wall and every
/// Defect recording that played an attack under Feral. It now reads the game's own
/// combat history, where the engine writes each play as it begins.
/// </summary>
public sealed class PlayReturnedToHandTests
{
    public PlayReturnedToHandTests() => EngineHost.Start();

    private const string OneFuzzyWurm = "ENCOUNTER.FUZZY_WURM_CRAWLER_WEAK";

    [GameFact]
    public void ARegentsParticleWallGoesBackToTheHandAndThePlayIsEstablished() =>
        HeadlessFights.InAFight("CHARACTER.REGENT", OneFuzzyWurm, (session, driver) =>
        {
            var player = session.RunState.Players[0];
            var wall = HeadlessFights.Deal(ModelDb.Card<ParticleWall>(), player);
            var blockBefore = player.Creature.Block;

            HeadlessFights.Play(driver, session, wall, 10);

            Assert.Contains(wall, player.PlayerCombatState!.Hand.Cards);
            Assert.True(player.Creature.Block > blockBefore, "the wall was played and gained no block");
        });

    [GameFact]
    public void ADefectsZeroCostAttackUnderFeralGoesBackToTheHandAndThePlayIsEstablished() =>
        HeadlessFights.InAFight("CHARACTER.DEFECT", OneFuzzyWurm, (session, driver) =>
        {
            var player = session.RunState.Players[0];
            var feral = HeadlessFights.Deal(ModelDb.Card<Feral>(), player);
            HeadlessFights.Play(driver, session, feral, 10);
            Assert.Contains(player.Creature.Powers, power => power.Id.ToString() == "POWER.FERAL_POWER");

            // Dealt to the top of the hand, which is where Feral returns it
            // (FeralPower.ModifyCardPlayResultLocation), so the play leaves it at the
            // index it was played from
            var claw = HeadlessFights.Deal(ModelDb.Card<Claw>(), player, CardPilePosition.Top);
            var enemy = HeadlessFights.LivingEnemies().Single();
            var healthBefore = enemy.CurrentHp;

            HeadlessFights.Play(driver, session, claw, 11);

            Assert.Equal(claw, player.PlayerCombatState!.Hand.Cards[0]);
            Assert.True(enemy.CurrentHp < healthBefore, "the claw was played and dealt no damage");
        });
}

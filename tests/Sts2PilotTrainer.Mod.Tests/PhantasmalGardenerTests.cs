using MegaCrit.Sts2.Core.Combat;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The Underdocks' four-gardener elite ends a turn headlessly.
///
/// <c>PhantasmalGardener.EnlargeMove</c> scales the gardener through
/// <c>Mathf.Log</c>, which the vendored Godot stubs had not got: the move threw
/// <c>MissingMethodException</c> inside the enemy turn, the game's own
/// <c>TaskHelper.RunSafely</c> swallowed it, the enemy side never ended its turn, and
/// every headless walk that reached this elite was refused at its next end of turn
/// with the player's phase still <c>None</c> - a third of every Underdocks walk. The
/// stub carries the member now (<c>third_party/godot-stubs/CHANGES.md</c>), and this
/// holds the fight to a turn that ends and a next one that begins.
/// </summary>
public sealed class PhantasmalGardenerTests
{
    public PhantasmalGardenerTests() => EngineHost.Start();

    private const string TheGardeners = "ENCOUNTER.PHANTASMAL_GARDENERS_ELITE";

    [GameFact]
    public void TheGardenersEliteEndsATurnAndOpensTheNext() =>
        HeadlessFights.InAFight("CHARACTER.IRONCLAD", TheGardeners, (session, driver) =>
        {
            Assert.Equal("1", HeadlessRuns.Field(session, "combat.turn"));
            Assert.Equal(4, HeadlessFights.LivingEnemies().Count);

            var errors = HeadlessFights.ErrorsLoggedDuring(() =>
            {
                driver.Apply(HeadlessRuns.Record(10, ActionVerb.EndTurn));
                driver.Apply(HeadlessRuns.Record(11, ActionVerb.EndTurn));
            });

            Assert.DoesNotContain(errors, error => error.Contains("MissingMethodException", StringComparison.Ordinal));
            Assert.Equal("3", HeadlessRuns.Field(session, "combat.turn"));
            Assert.Equal("true", HeadlessRuns.Field(session, "combat.in_progress"));
            Assert.Equal(PlayerTurnPhase.Play, session.RunState.Players[0].PlayerCombatState!.Phase);
        });
}

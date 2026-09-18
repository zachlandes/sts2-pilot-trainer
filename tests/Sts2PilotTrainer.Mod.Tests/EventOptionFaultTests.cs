using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// An event option whose own work faults is refused by the driver by name, rather
/// than replayed from the page the option never left.
///
/// The game starts an option's task through <c>TaskHelper.RunSafely</c>, which logs a
/// fault and swallows it, and <c>EventSynchronizer.ChooseLocalOption</c> returns
/// nothing, so a driver that only drained the queue went on as if the option had
/// finished: the event stood on its first page, the next recorded decision named an
/// option it no longer offered, and the refusal blamed the recording. Five events
/// faulted that way headlessly on v0.111.0, on the scene-tree singletons their work
/// reaches (<c>PresentationStandIns</c>); this test forces the fault where no
/// gameplay seam can, with a prefix of this assembly's own on one option's work, and
/// holds the driver to refusing it with the fault's own words.
/// </summary>
public sealed class EventOptionFaultTests
{
    public EventOptionFaultTests() => EngineHost.Start();

    /// <summary>The Dense Vegetation row's seed: a first act whose first question
    /// mark opens Dense Vegetation.</summary>
    private const string DenseVegetationSeed = "HHNZNPJV6W";

    [HarmonyPatch(typeof(DenseVegetation), "TrudgeOn")]
    private static class FaultingTrudgeOn
    {
        [HarmonyPrefix]
        private static void Before() => throw new InvalidOperationException("the trudge faulted on purpose");
    }

    [GameFact]
    public void AFaultInAnOptionsOwnWorkIsRefusedByName()
    {
        var harmony = new Harmony($"event-option-fault.{Guid.NewGuid():N}");
        harmony.CreateClassProcessor(typeof(FaultingTrudgeOn)).Patch();
        if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
        try
        {
            var session = new GameSession();
            session.StartRun(DenseVegetationSeed, "CHARACTER.IRONCLAD", 0, "standard", RecordedActWalk.Acts);
            using var driver = new RunDriver(session);
            driver.ImproviseUnrecordedCardSelections();
            driver.EnterFirstRoom();

            var refusal = Assert.Throws<EngineException>(() => SyntheticFixtureGenerator.WalkTheAct(
                session, driver, [], null, false,
                new WalkPolicy
                {
                    EventId = "EVENT.DENSE_VEGETATION",
                    EventOptionKey = "DENSE_VEGETATION.pages.INITIAL.options.TRUDGE_ON",
                    RouteThrough = [MapPointType.Unknown],
                    StopOnceMet = true,
                }));
            Assert.Contains("the option's own work faulted", refusal.Message, StringComparison.Ordinal);
            Assert.Contains("the trudge faulted on purpose", refusal.Message, StringComparison.Ordinal);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            if (RunManager.Instance is { IsInProgress: true } manager) manager.CleanUp();
        }
    }
}

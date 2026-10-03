using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

public sealed class DollTitleContractTests
{
    public DollTitleContractTests() => EngineHost.Start();

    public static IEnumerable<object[]> Dolls() =>
        new[] { "BING_BONG", "DAUGHTER_OF_THE_WIND", "MR_STRUGGLES" }.Select(relic => new object[] { relic });

    [GameFact]
    public void TheDollRowsAreExactlyTheTitleIdentitiesExcusedOntoThisTest()
    {
        var keys = Dolls().Select(row => $"relics.{row[0]}.title").Order(StringComparer.Ordinal).ToList();
        Assert.Equal(keys, DecisionSurface.TitleKeyedOptions("EVENT.DOLL_ROOM").Order(StringComparer.Ordinal));
        var points = keys.Select(key => DecisionPoint.EventOption("EVENT.DOLL_ROOM", key)).OrderBy(point => point.ToString(), StringComparer.Ordinal);
        var excusals = DecisionExcusals.All.Where(pair => pair.Value.Reason.Contains(nameof(DollTitleContractTests), StringComparison.Ordinal)).ToList();
        Assert.All(excusals, pair => Assert.Equal(ExcusalClass.Generated, pair.Value.Class));
        Assert.Equal(points, excusals.Select(pair => pair.Key).OrderBy(point => point.ToString(), StringComparer.Ordinal));
    }

    /// <summary>The existing second-act route, with synthetic localized doll titles
    /// during capture only: a recipient with different text must replay the identity,
    /// not the label. No inventory or event is injected.</summary>
    [GameTheory]
    [MemberData(nameof(Dolls))]
    public void ALocalizedDollChoiceCapturesItsIdentityAndReplaysToParity(string relic)
    {
        var row = GeneratedCoverageTests.SecondActEventRowFor(
            "EVENT.DOLL_ROOM", "DOLL_ROOM.pages.INITIAL.options.EXAMINE");
        var key = $"relics.{relic}.title";
        using var harness = new RecordedActWalk();
        var localization = new Harmony($"doll-title-contract.{Guid.NewGuid():N}");
        RecordedActWalk.Recorded recorded;
        try
        {
            LocalizedDollTitles.Reads = 0;
            localization.CreateClassProcessor(typeof(LocalizedDollTitles)).Patch();
            recorded = harness.Walk(
                GeneratedCoverageTests.PolicyFor(row) with
                {
                    EventOptionKey = key,
                    EventOptionsOnTheWay = [row.Key],
                },
                row.Seed, visitEveryRoomType: true, RecordedActWalk.Acts, character: row.Character);
            Assert.True(LocalizedDollTitles.Reads > 0, "the diagnostic localization was never read");
        }
        finally
        {
            localization.UnpatchAll(localization.Id);
        }

        RecordedActWalk.AssertWhole(recorded);
        RecordedActWalk.ReplayToParity(recorded);
        Assert.True(recorded.AskMet);
        var choice = Assert.Single(recorded.Manifest.Actions, action =>
            action.Verb == ActionVerb.ChooseEventOption && action.Args["option_key"] == key);
        Assert.Equal("EVENT.DOLL_ROOM", choice.Args["event_id"]);
        Assert.Contains(DecisionPoint.EventOption("EVENT.DOLL_ROOM", key), DecisionFacts.Of(recorded.Manifest));
    }

    [HarmonyPatch(typeof(LocString), nameof(LocString.GetRawText))]
    private static class LocalizedDollTitles
    {
        internal static int Reads { get; set; }

        [HarmonyPostfix]
        private static void After(LocString __instance, ref string __result)
        {
            if (__instance.LocTable == "relics" && __instance.LocEntryKey is
                "BING_BONG.title" or "DAUGHTER_OF_THE_WIND.title" or "MR_STRUGGLES.title")
            {
                Reads++;
                __result = $"localized/{__instance.LocEntryKey}";
            }
        }
    }
}

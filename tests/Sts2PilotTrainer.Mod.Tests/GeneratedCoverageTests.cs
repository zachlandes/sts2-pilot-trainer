using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// Decision points the committed corpus never reaches, reached by a generated walk
/// through the real recorder, replayed, and held to parity.
///
/// The coverage number excuses a point no committed recording exercises with a
/// sentence a build can be held to; these rows are what a headless host can hold it
/// to. Each row points the whole-act journey at one decision through a
/// <see cref="WalkPolicy"/> - decline a card reward on its own screen, take a named
/// rest option, buy from a named shelf, discard or drink a potion on the map, skip
/// the chest, claim an elite's relic - plays the act through the recorder the way the won-run proof does,
/// replays what the recorder wrote, holds the replay to the journal decision for
/// decision through the same oracle <c>parity</c> uses, and asserts the recording
/// projects to the point the row is for; <see cref="RecordedActWalk"/> is that
/// harness. The recordings are written to a temporary
/// store and not committed, so <c>coverage --corpus manifests</c> still reads those
/// points as excused; the excusal names this test as the thing that reaches them.
///
/// A row asserts the walk met its ask as well as that the point was projected,
/// because the verb alone cannot tell the map's potion drink from the fight's.
///
/// What no row here can reach is what <c>DecisionExcusals</c> leaves excused with a
/// reason of its own: what the headless host has no screen for, the undo of an ended
/// turn, and every point whose producer the fixture seed's route does not pass.
/// </summary>
public sealed class GeneratedCoverageTests
{
    /// <summary>Before any test method here is prepared, so the game assembly the
    /// harness names resolves; the harness's own teardown forgets it again.</summary>
    public GeneratedCoverageTests() => EngineHost.Start();

    public static IEnumerable<object[]> Rows() =>
    [
        ["decline the first card reward", "verb  TakeCardRewardAlternative", "card-reward-alternative  Skip"],
        ["rest HEAL", "rest-option  HEAL", ""],
        ["shop relic", "shop-kind  relic", ""],
        ["shop potion", "shop-kind  potion", ""],
        ["shop colorless_card", "shop-kind  colorless_card", ""],
        ["claim the relic reward", "reward-kind  relic", ""],
        ["skip the chest", "verb  SkipChestRelic", ""],
        ["take the chest", "verb  TakeChestRelic", ""],
        ["discard a potion on the map", "verb  DiscardPotion", ""],
        ["drink a potion on the map", "verb  UsePotion", ""],
    ];

    /// <summary>Each row on the whole-act fixture's own seed, the one run the journey's
    /// rules are known to carry through every room type of the first act, which is
    /// what a row that needs a shop, a rest site or a chest on its route asks for.</summary>
    [GameTheory]
    [MemberData(nameof(Rows))]
    public void AGeneratedWalkReachesThePointRecordsItAndReplaysToParity(string row, string point, string alsoPoint)
    {
        using var harness = new RecordedActWalk();

        var recorded = harness.Walk(PolicyFor(row));
        Assert.True(recorded.AskMet, $"the walk finished without meeting the ask of row '{row}', so the point it reports was reached somewhere else");
        RecordedActWalk.AssertWhole(recorded);

        var points = DecisionFacts.Of(recorded.Manifest).Select(reached => reached.ToString()).ToList();
        Assert.Contains(point, points);
        if (alsoPoint.Length > 0) Assert.Contains(alsoPoint, points);

        RecordedActWalk.ReplayToParity(recorded);
    }

    /// <summary>The rows name every point <c>DecisionExcusals</c> credits to this test,
    /// and nothing else: a row for a point the committed corpus already reaches is a
    /// whole recorded act on every merge for nothing.</summary>
    [GameFact]
    public void TheRowsAreExactlyThePointsExcusedOntoThisTest()
    {
        var rows = Rows().SelectMany(row => new[] { (string)row[1], (string)row[2] }).Where(point => point.Length > 0).ToHashSet(StringComparer.Ordinal);
        var credited = DecisionExcusals.All
            .Where(excusal => excusal.Value.Contains(nameof(GeneratedCoverageTests), StringComparison.Ordinal))
            .Select(excusal => excusal.Key.ToString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(credited);
        Assert.Equal(credited.Order(StringComparer.Ordinal), rows.Order(StringComparer.Ordinal));
    }

    private static WalkPolicy PolicyFor(string row) => row switch
    {
        "decline the first card reward" => new WalkPolicy { DeclineTheFirstCardReward = true },
        "rest HEAL" => new WalkPolicy { RestOption = "HEAL" },
        "shop relic" => new WalkPolicy { ShopKind = ShopPurchaseKinds.Relic },
        "shop potion" => new WalkPolicy { ShopKind = ShopPurchaseKinds.Potion },
        "shop colorless_card" => new WalkPolicy { ShopKind = ShopPurchaseKinds.ColorlessCard },
        "claim the relic reward" => new WalkPolicy { ClaimTheRelicReward = true },
        "skip the chest" => new WalkPolicy { SkipTheChest = true },
        "take the chest" => new WalkPolicy { TakeTheChest = true },
        "discard a potion on the map" => new WalkPolicy { DiscardAPotionOnTheMap = true },
        "drink a potion on the map" => new WalkPolicy { DrinkAPotionOnTheMap = true },
        _ => throw new ArgumentOutOfRangeException(nameof(row), row, "no such row"),
    };
}

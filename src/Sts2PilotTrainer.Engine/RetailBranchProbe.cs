using System.Reflection;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.Rngs;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.TestSupport;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Measures whether the three gameplay paths that consume randomness only when the
/// engine's test-mode flag is off actually take retail's branch under this host.
///
/// The question this answers is not "did the patch apply" - a patch that fails to
/// apply is already a startup failure. It is the harder one: the flag is still on
/// everywhere else, so the only thing standing between a headless replay and a run
/// that generates different content is that these three calls, and only these three,
/// see it off. Nothing else in the project reaches two of the three: no committed
/// recording or fixture picks up Cauldron or Calling Bell, so without this probe those
/// two patches would ship measured by nothing at all.
///
/// Each site is exercised through the engine's own construction and read for a
/// consequence retail has and test mode does not - a stream that moved, a reward that
/// still has to be populated, a relic that has not been chosen yet. None of it asserts
/// a value: what the price or the relic turns out to be is the game's business, and a
/// probe that pinned one would fail on the next build for the wrong reason.
///
/// See docs/headless-fidelity.md. This answers a fidelity question about the host and
/// verifies nothing about any manifest, so it is not a publication gate condition.
/// </summary>
public static class RetailBranchProbe
{
    public const string ReportSchema = "sts2-pilot-trainer/retail-branch-probe/v1";

    /// <summary>A seed with nothing special about it. Nothing here reads the run's
    /// content: the probe needs a player to own a relic and a shop entry, and any
    /// run provides one.</summary>
    private const string ProbeSeed = "RETAILBRANCH1";

    private const BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    public static RetailBranchReport Run()
    {
        var session = new GameSession();
        session.StartRun(ProbeSeed, "CHARACTER.IRONCLAD", 0, "standard", ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"]);
        var player = session.RunState.Players[0];

        var sites = new List<RetailBranchSite>
        {
            MeasureMerchantPotionCost(player),
            MeasureGeneratedRewards<MegaCrit.Sts2.Core.Models.Relics.Cauldron>(
                player,
                "Cauldron.GenerateRewards",
                "every potion reward is still unpopulated, so each will draw from PlayerRng.Rewards",
                rewards => rewards.All(reward => reward is PotionReward { IsPopulated: false })),
            MeasureGeneratedRewards<MegaCrit.Sts2.Core.Models.Relics.CallingBell>(
                player,
                "CallingBell.GenerateRewards",
                "every relic reward is still unpopulated and carries a rarity, so each will pull from the " +
                "run's own relic grab bag",
                rewards => rewards.All(reward =>
                    reward is RelicReward { IsPopulated: false } relic && relic.Rarity != RelicRarity.None)),
        };

        // The flag has to be back on. A finalizer that failed to restore it would
        // leave the whole host in retail mode, where the next room constructor reaches
        // for a scene tree that is not there - a loud failure, but a much later one
        // than this, and one nothing would attribute to these patches.
        var flagRestored = TestMode.IsOn;

        return new RetailBranchReport
        {
            Schema = ReportSchema,
            Build = GameIdentity.Read().BuildVersion,
            Seed = ProbeSeed,
            TestModeRestored = flagRestored,
            Sites = sites,
        };
    }

    /// <summary>
    /// The merchant's potion price.
    ///
    /// Constructing the entry is what calls <c>CalcCost</c>, exactly as
    /// <c>MerchantInventory</c> does for each of a normal merchant's three potion
    /// slots. Retail multiplies the shelf price by one draw from
    /// <c>PlayerRng.Shops</c>; test mode skips it. So the measurement is the stream's
    /// own position, which is the thing that was silently wrong.
    /// </summary>
    private static RetailBranchSite MeasureMerchantPotionCost(Player player)
    {
        var before = CanonicalStateProjection.Counter(player.PlayerRng.Shops);
        _ = new MerchantPotionEntry(ModelDb.Potion<MegaCrit.Sts2.Core.Models.Potions.FlexPotion>().ToMutable(), player);
        var after = CanonicalStateProjection.Counter(player.PlayerRng.Shops);

        return new RetailBranchSite
        {
            Name = "MerchantPotionEntry.CalcCost",
            Expected = "constructing one potion entry advances PlayerRng.Shops by exactly 1",
            Observed = $"PlayerRng.Shops {before} -> {after}",
            Passed = after - before == 1,
        };
    }

    /// <summary>
    /// A relic whose pickup effect generates rewards.
    ///
    /// The owner is set directly rather than obtaining the relic, because obtaining it
    /// runs the pickup effect - which generates the rewards and then offers them to a
    /// screen this host stands in for. The generation is what is under test and
    /// offering it would only add a stand-in between the measurement and the thing
    /// measured.
    ///
    /// What is read is the shape of the rewards rather than their contents: test mode
    /// hands back rewards that already hold a hard-coded model and therefore never
    /// draw, and retail hands back rewards that still have to be populated. A probe
    /// that named the potions or the relics would be pinning content that is the
    /// game's to choose.
    /// </summary>
    private static RetailBranchSite MeasureGeneratedRewards<TRelic>(
        Player player, string name, string expectation, Func<IReadOnlyList<Reward>, bool> isRetailShape)
        where TRelic : RelicModel
    {
        var relic = ModelDb.Relic<TRelic>().ToMutable();
        relic.Owner = player;

        var generate = typeof(TRelic).GetMethod("GenerateRewards", NonPublicInstance)
            ?? throw new EngineException(
                $"{typeof(TRelic).Name}.GenerateRewards is absent from this build, so the retail branch this " +
                "host restores cannot be measured. Refusing rather than reporting a pass nothing checked.");

        var rewards = (IReadOnlyList<Reward>)generate.Invoke(relic, null)!;
        var populated = rewards.Count(reward => reward.IsPopulated);

        return new RetailBranchSite
        {
            Name = name,
            Expected = expectation,
            Observed = $"{rewards.Count} reward(s), {populated} of them already populated",
            Passed = rewards.Count > 0 && isRetailShape(rewards),
        };
    }
}

public sealed record RetailBranchReport
{
    [JsonPropertyName("schema")]
    public required string Schema { get; init; }

    [JsonPropertyName("build")]
    public required string Build { get; init; }

    [JsonPropertyName("seed")]
    public required string Seed { get; init; }

    /// <summary>Whether the headless flag was on again once every site had been
    /// exercised. False means a finalizer did not run and the host is now in retail
    /// mode.</summary>
    [JsonPropertyName("test_mode_restored")]
    public required bool TestModeRestored { get; init; }

    [JsonPropertyName("sites")]
    public required IReadOnlyList<RetailBranchSite> Sites { get; init; }

    [JsonIgnore]
    public bool AllRestored => TestModeRestored && Sites.Count > 0 && Sites.All(site => site.Passed);
}

public sealed record RetailBranchSite
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("expected")]
    public required string Expected { get; init; }

    [JsonPropertyName("observed")]
    public required string Observed { get; init; }

    [JsonPropertyName("passed")]
    public required bool Passed { get; init; }
}

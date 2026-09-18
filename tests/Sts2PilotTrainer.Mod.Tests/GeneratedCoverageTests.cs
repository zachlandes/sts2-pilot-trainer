using MegaCrit.Sts2.Core.Map;
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
/// The producer rows are the second table: one per relic that produces a seam and
/// that a run of act 1 deals from game-produced state - Neow's offer, or the run's
/// own relic bag at a chest or the merchant's shelf - each on a seed hunted so the run
/// deals that relic (<see cref="SeedHunt"/>). A row obtains the relic through the
/// recorded decision that deals it and answers what the relic adds, then asserts the
/// recording reaches every seam the producer map lists for the relic, by
/// co-occurrence, beside the points its policy is for. The table is held to the map:
/// every relic the map says Neow, a chest or a shop deals has a row, but for one the
/// game withholds from a singleplayer run.
///
/// The ancient rows are the third table: one per option of every ancient an act
/// opens on - act 2's and act 3's, every option of which is a relic - each on a run
/// whose acts list is that act alone, which the engine builds the way it builds the
/// won-run proof's one-act run and which opens on the act's ancient rather than on
/// Neow, on a seed hunted so that ancient is rolled and offers the relic. A
/// generated-only acts list: no client run has one, the recording is admissible as
/// coverage evidence on the footing of the won-run proof's one-act run and is never
/// listed or shared, and the row says so in its own words. A row obtains the relic
/// through the recorded <c>ChooseEventOption</c> naming the ancient and the key,
/// answers what the relic adds, and retires the ancient, its option and every seam
/// the map lists the relic under. The table is held to the map: every option of every
/// act's ancient has a row, and every relic the map says an act's ancient deals has
/// one; Darv's are a question mark's and wait on the event hunt.
///
/// What no row here can reach is what <c>DecisionExcusals</c> leaves excused with a
/// reason of its own: what the headless host has no screen for, the undo of an ended
/// turn, and every point whose producer an event deals.
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

    /// <summary>
    /// One producer row: the relic, who deals it on this seed, the seed the hunt found
    /// and the ask the walk is after once it holds the relic - obtaining it, where the
    /// relic's own work opens the seam - with the points past the relic's seams the
    /// row retires. The seed is a constant with the hunt's criterion beside it, as
    /// <see cref="SeedHunt"/> says; the row checks the criterion first, so a game
    /// update that moves the RNG fails by name rather than as a walk that met nothing.
    /// </summary>
    internal sealed record ProducerRow(string Relic, SeedHunt.Dealer Dealer, string Seed, string Ask, params string[] AlsoRetires);

    private const string ObtainIt = "obtain it";
    private const string ClaimTheRelicItOffers = "claim the relic it offers";
    private const string AFightHoldingIt = "a fight holding it";
    private const string TwoGolds = "two golds on one loot screen";

    /// <summary>The producer rows, by relic. The seeds were found by
    /// <c>SeedHunt.Find</c> over its own candidates on v0.111.0.</summary>
    internal static readonly IReadOnlyDictionary<string, ProducerRow> ProducerRows = new[]
    {
        // Neow's offer: the relic's own work opens the seam inside the blessing
        new ProducerRow("RELIC.HEFTY_TABLET", SeedHunt.Dealer.Neow, "KNU8ZJM21D", ObtainIt, "verb  ConfirmCardScreen"),
        new ProducerRow("RELIC.KALEIDOSCOPE", SeedHunt.Dealer.Neow, "S7LTRQKC10", ObtainIt),
        new ProducerRow("RELIC.LAVA_ROCK", SeedHunt.Dealer.Neow, "X5KY7YB3AE", ClaimTheRelicItOffers),
        new ProducerRow("RELIC.LEAD_PAPERWEIGHT", SeedHunt.Dealer.Neow, "ZS724YW1MP", ObtainIt, "verb  ConfirmCardScreen"),
        new ProducerRow("RELIC.LOST_COFFER", SeedHunt.Dealer.Neow, "HQUHYBESLV", ObtainIt),
        new ProducerRow("RELIC.NEOWS_BONES", SeedHunt.Dealer.Neow, "N2E2AGFGSN", ClaimTheRelicItOffers),
        new ProducerRow("RELIC.NEW_LEAF", SeedHunt.Dealer.Neow, "N2E2AGFGSN", ObtainIt),
        new ProducerRow("RELIC.POMANDER", SeedHunt.Dealer.Neow, "BBCLG8UV5X", ObtainIt),
        new ProducerRow("RELIC.PRECARIOUS_SHEARS", SeedHunt.Dealer.Neow, "41MV0020T4", ObtainIt),
        new ProducerRow("RELIC.PRECISE_SCISSORS", SeedHunt.Dealer.Neow, "H757G7M4DX", ObtainIt),
        new ProducerRow("RELIC.SCROLL_BOXES", SeedHunt.Dealer.Neow, "C1GAV23WHA", ObtainIt, "verb  SelectBundleFromScreen"),
        new ProducerRow("RELIC.SMALL_CAPSULE", SeedHunt.Dealer.Neow, "8DYGVNBPVT", ClaimTheRelicItOffers),
        // The merchant's shelf: the back of the shop bag, bought before anything else
        new ProducerRow("RELIC.CAULDRON", SeedHunt.Dealer.Shop, "ZZAC8RZ00W", ObtainIt),
        new ProducerRow("RELIC.DOLLYS_MIRROR", SeedHunt.Dealer.Shop, "TM0VT1L0SB", ObtainIt),
        new ProducerRow("RELIC.GNARLED_HAMMER", SeedHunt.Dealer.Shop, "X2BN5AEZ5Q", ObtainIt, "verb  ConfirmCardScreen"),
        new ProducerRow("RELIC.KIFUDA", SeedHunt.Dealer.Shop, "V856E12HSB", ObtainIt, "verb  ConfirmCardScreen"),
        new ProducerRow("RELIC.ORRERY", SeedHunt.Dealer.Shop, "E5KZPT0UDZ", ObtainIt),
        new ProducerRow("RELIC.PUNCH_DAGGER", SeedHunt.Dealer.Shop, "RVXC56NHQN", ObtainIt),
        new ProducerRow("RELIC.ROYAL_STAMP", SeedHunt.Dealer.Shop, "BRZ9JQ10SW", ObtainIt),
        new ProducerRow("RELIC.TOOLBOX", SeedHunt.Dealer.Shop, "30VQ5QF1Y7", AFightHoldingIt, "verb  ConfirmCardScreen"),
        // The chest: the front of the relic's rarity bag, dealt where the chest rolls
        // that rarity
        new ProducerRow("RELIC.AMETHYST_AUBERGINE", SeedHunt.Dealer.Chest, "62CWRWS8L1", TwoGolds),
        new ProducerRow("RELIC.GAMBLING_CHIP", SeedHunt.Dealer.Chest, "MQC16CJX70", AFightHoldingIt, "verb  ConfirmCardScreen"),
        new ProducerRow("RELIC.GIRYA", SeedHunt.Dealer.Chest, "6KGKGA4S8P", "rest LIFT", "rest-option  LIFT"),
        new ProducerRow("RELIC.PRAYER_WHEEL", SeedHunt.Dealer.Chest, "Q3P7J5WAQ4", AFightHoldingIt),
        new ProducerRow("RELIC.SHOVEL", SeedHunt.Dealer.Chest, "BWFSCP7QMB", "rest DIG", "rest-option  DIG"),
        new ProducerRow("RELIC.TINY_MAILBOX", SeedHunt.Dealer.Chest, "J379AB2U8B", "rest HEAL"),
        new ProducerRow("RELIC.WHITE_STAR", SeedHunt.Dealer.Chest, "UAU554G0RG", AFightHoldingIt),
    }.ToDictionary(row => row.Relic, StringComparer.Ordinal);

    public static IEnumerable<object[]> ProducerRelics() => ProducerRows.Keys.Order(StringComparer.Ordinal).Select(relic => new object[] { relic });

    /// <summary>
    /// Each producer row on its own hunted seed: the run deals the relic, the walk
    /// obtains it through the recorded decision that deals it and meets the ask past
    /// it, the recording reaches every seam the map lists for the relic and the points
    /// the row retires, and a fresh replay reproduces the journal decision for
    /// decision.
    /// </summary>
    [GameTheory]
    [MemberData(nameof(ProducerRelics))]
    public void AProducerRowDealsTheRelicReachesItsSeamsAndReplaysToParity(string relic)
    {
        var row = ProducerRows[relic];
        Assert.True(
            SeedHunt.ReadOpening(row.Seed).Deals(row.Relic, row.Dealer),
            $"seed {row.Seed} no longer deals {row.Relic} through {row.Dealer}: the game's RNG has moved, so rerun " +
            "SeedHunt.Find for this row");

        using var harness = new RecordedActWalk();
        var recorded = harness.Walk(PolicyFor(row), row.Seed, visitEveryRoomType: false);
        Assert.True(
            recorded.AskMet,
            $"the walk finished without meeting the ask of the {row.Relic} row ({row.Ask}); actions: " +
            string.Join(" ", recorded.Manifest.Actions.Select(action => action.Verb)));
        RecordedActWalk.AssertWhole(recorded);

        // The relic came through a recorded decision that names it - the blessing, the
        // chest, the purchase or a claim - so a replay deals it the same way
        Assert.Contains(recorded.Manifest.Actions, action => Deals(action, row.Relic));

        var points = DecisionFacts.Of(recorded.Manifest);
        var reached = DecisionCoverage.SeamsReachedBy(
                new CoveredRecording(
                    recorded.Manifest.RunId, points, RecordingStanding.Of(recorded.Manifest.Source.Native),
                    DecisionFacts.ModelsMet(recorded.Manifest)),
                DecisionSurface.ProducerMap())
            .ToHashSet();
        foreach (var point in RetiredBy(row))
        {
            Assert.True(
                points.Contains(point) || reached.Contains(point),
                $"the {row.Relic} row's recording does not reach {point}; it reaches " +
                string.Join(", ", points.Concat(reached).Select(reachedPoint => reachedPoint.ToString())));
        }

        RecordedActWalk.ReplayToParity(recorded);
    }

    /// <summary>
    /// One ancient row: the ancient, the relic its option grants, the seed the hunt
    /// found on the run of the ancient's act alone, and the ask the walk is after once
    /// it holds the relic, with the points past the relic's seams the row retires and
    /// the seams of the relic's it falls short of. The seed is a constant with the
    /// hunt's criterion beside it, as <see cref="SeedHunt"/> says; the row checks the
    /// criterion first, so a game update that moves the RNG fails by name rather than
    /// as a walk that met nothing.
    /// </summary>
    /// <param name="ShortOf">The seams the map lists the relic under that this row does
    /// not reach, each excused in <c>DecisionExcusals</c> by what stands in the way:
    /// the rest option a relic adds is a rest site away from the ancient, and the
    /// journey's mechanical line does not survive there at starter strength.</param>
    internal sealed record AncientRow(
        string Ancient, string Relic, string Seed, string Ask, string[]? AlsoRetires = null, string[]? ShortOf = null);

    /// <summary>The rest option a relic adds, which an ancient row falls short of: the
    /// seam of the option at its timing class.</summary>
    private static string[] ShortOfTheRest(string option) =>
        [$"seam  rest-option:{option} @ AbstractModel.TryModifyRestSiteOptions"];

    private const string AShopHoldingIt = "a shop holding it";
    private const string SacrificeACardReward = "sacrifice a card reward";

    /// <summary>The ancient rows, by relic. The seeds were found by
    /// <c>SeedHunt.Find</c> over its own candidates on v0.111.0, each on the acts
    /// list of the ancient's act alone.</summary>
    internal static readonly IReadOnlyDictionary<string, AncientRow> AncientRows = new[]
    {
        // Act 2: the Hive's three
        new AncientRow("EVENT.OROBAS", "RELIC.ALCHEMICAL_COFFER", "ADX2BRHFBC", ObtainIt),
        new AncientRow("EVENT.OROBAS", "RELIC.ARCHAIC_TOOTH", "C1GAV23WHA", ObtainIt),
        new AncientRow("EVENT.OROBAS", "RELIC.DRIFTWOOD", "C1GAV23WHA", ObtainIt),
        new AncientRow("EVENT.OROBAS", "RELIC.ELECTRIC_SHRYMP", "8CKSJT78DB", ObtainIt),
        new AncientRow("EVENT.OROBAS", "RELIC.GLASS_EYE", "C1GAV23WHA", ObtainIt),
        new AncientRow("EVENT.OROBAS", "RELIC.PRISMATIC_GEM", "N2E2AGFGSN", ObtainIt),
        new AncientRow("EVENT.OROBAS", "RELIC.RADIANT_PEARL", "N2E2AGFGSN", ObtainIt),
        new AncientRow("EVENT.OROBAS", "RELIC.SAND_CASTLE", "HCM40F3ZGY", ObtainIt),
        new AncientRow("EVENT.OROBAS", "RELIC.SEA_GLASS", "HCM40F3ZGY", ObtainIt),
        new AncientRow("EVENT.OROBAS", "RELIC.TOUCH_OF_OROBAS", "N2E2AGFGSN", ObtainIt),
        new AncientRow("EVENT.PAEL", "RELIC.PAELS_BLOOD", "Y1NN8NJF3P", ObtainIt),
        new AncientRow("EVENT.PAEL", "RELIC.PAELS_CLAW", "X7KJSBLHQ6", ObtainIt),
        new AncientRow("EVENT.PAEL", "RELIC.PAELS_EYE", "BEWP9FJU9V", ObtainIt),
        new AncientRow("EVENT.PAEL", "RELIC.PAELS_FLESH", "6KGKGA4S8P", ObtainIt),
        new AncientRow("EVENT.PAEL", "RELIC.PAELS_GROWTH", "87NGC17B9A", ObtainIt, ShortOf: ShortOfTheRest("CLONE")),
        new AncientRow("EVENT.PAEL", "RELIC.PAELS_HORN", "ZBCVW5ENJ4", ObtainIt),
        new AncientRow("EVENT.PAEL", "RELIC.PAELS_LEGION", "KNU8ZJM21D", ObtainIt),
        new AncientRow("EVENT.PAEL", "RELIC.PAELS_TEARS", "BEWP9FJU9V", ObtainIt),
        new AncientRow("EVENT.PAEL", "RELIC.PAELS_TOOTH", "BEWP9FJU9V", ObtainIt),
        new AncientRow("EVENT.PAEL", "RELIC.PAELS_WING", "0WA4C6C2KF", SacrificeACardReward, ["card-reward-alternative  SACRIFICE"]),
        new AncientRow("EVENT.TEZCATARA", "RELIC.BIIIG_HUG", "S7LTRQKC10", ObtainIt),
        new AncientRow("EVENT.TEZCATARA", "RELIC.GOLDEN_COMPASS", "41MV0020T4", ObtainIt),
        new AncientRow("EVENT.TEZCATARA", "RELIC.NUTRITIOUS_SOUP", "RAGWB3H44W", ObtainIt),
        new AncientRow("EVENT.TEZCATARA", "RELIC.PUMPKIN_CANDLE", "HQUHYBESLV", ObtainIt, ShortOf: ShortOfTheRest("KINDLE")),
        new AncientRow("EVENT.TEZCATARA", "RELIC.SEAL_OF_GOLD", "RAGWB3H44W", ObtainIt),
        new AncientRow("EVENT.TEZCATARA", "RELIC.STORYBOOK", "HHNZNPJV6W", ObtainIt),
        new AncientRow("EVENT.TEZCATARA", "RELIC.TOASTY_MITTENS", "RAGWB3H44W", AFightHoldingIt),
        new AncientRow("EVENT.TEZCATARA", "RELIC.TOY_BOX", "S7LTRQKC10", ClaimTheRelicItOffers),
        new AncientRow("EVENT.TEZCATARA", "RELIC.VERY_HOT_COCOA", "S7LTRQKC10", ObtainIt),
        new AncientRow("EVENT.TEZCATARA", "RELIC.YUMMY_COOKIE", "XTXVMBG3WB", ObtainIt),
        // Act 3: Glory's three
        new AncientRow("EVENT.NONUPEIPE", "RELIC.BEAUTIFUL_BRACELET", "N2E2AGFGSN", ObtainIt),
        new AncientRow("EVENT.NONUPEIPE", "RELIC.BLESSED_ANTLER", "N2E2AGFGSN", ObtainIt),
        new AncientRow("EVENT.NONUPEIPE", "RELIC.BRILLIANT_SCARF", "N2E2AGFGSN", ObtainIt),
        new AncientRow("EVENT.NONUPEIPE", "RELIC.DELICATE_FROND", "KNU8ZJM21D", ObtainIt),
        new AncientRow("EVENT.NONUPEIPE", "RELIC.DIAMOND_DIADEM", "X5KY7YB3AE", ObtainIt),
        new AncientRow("EVENT.NONUPEIPE", "RELIC.FUR_COAT", "KNU8ZJM21D", ObtainIt),
        new AncientRow("EVENT.NONUPEIPE", "RELIC.GLITTER", "C1GAV23WHA", ObtainIt),
        new AncientRow("EVENT.NONUPEIPE", "RELIC.JEWELRY_BOX", "0WA4C6C2KF", ObtainIt),
        new AncientRow("EVENT.NONUPEIPE", "RELIC.LOOMING_FRUIT", "X5KY7YB3AE", ObtainIt),
        new AncientRow("EVENT.NONUPEIPE", "RELIC.SIGNET_RING", "C1GAV23WHA", ObtainIt),
        new AncientRow("EVENT.TANX", "RELIC.CLAWS", "S7LTRQKC10", ObtainIt),
        new AncientRow("EVENT.TANX", "RELIC.CROSSBOW", "X7KJSBLHQ6", ObtainIt),
        new AncientRow("EVENT.TANX", "RELIC.IRON_CLUB", "X7KJSBLHQ6", ObtainIt),
        new AncientRow("EVENT.TANX", "RELIC.MEAT_CLEAVER", "XTXVMBG3WB", ObtainIt, ShortOf: ShortOfTheRest("COOK")),
        new AncientRow("EVENT.TANX", "RELIC.SAI", "HQUHYBESLV", ObtainIt),
        new AncientRow("EVENT.TANX", "RELIC.SPIKED_GAUNTLETS", "HCM40F3ZGY", ObtainIt),
        new AncientRow("EVENT.TANX", "RELIC.TANXS_WHISTLE", "HQUHYBESLV", ObtainIt),
        new AncientRow("EVENT.TANX", "RELIC.THROWING_AXE", "S7LTRQKC10", ObtainIt),
        new AncientRow("EVENT.TANX", "RELIC.TRI_BOOMERANG", "HCM40F3ZGY", ObtainIt),
        new AncientRow("EVENT.TANX", "RELIC.WAR_HAMMER", "S7LTRQKC10", ObtainIt),
        new AncientRow("EVENT.VAKUU", "RELIC.BLOOD_SOAKED_ROSE", "ZBCVW5ENJ4", ObtainIt),
        new AncientRow("EVENT.VAKUU", "RELIC.CHOICES_PARADOX", "41MV0020T4", AFightHoldingIt),
        new AncientRow("EVENT.VAKUU", "RELIC.DISTINGUISHED_CAPE", "BEWP9FJU9V", ObtainIt),
        new AncientRow("EVENT.VAKUU", "RELIC.FIDDLE", "BEWP9FJU9V", ObtainIt),
        new AncientRow("EVENT.VAKUU", "RELIC.JEWELED_MASK", "3DKM6HUFZY", ObtainIt),
        new AncientRow("EVENT.VAKUU", "RELIC.LORDS_PARASOL", "5U7CT06HNS", AShopHoldingIt),
        new AncientRow("EVENT.VAKUU", "RELIC.MUSIC_BOX", "ZBCVW5ENJ4", ObtainIt),
        new AncientRow("EVENT.VAKUU", "RELIC.PRESERVED_FOG", "41MV0020T4", ObtainIt),
        new AncientRow("EVENT.VAKUU", "RELIC.SERE_TALON", "ZBCVW5ENJ4", ObtainIt),
        new AncientRow("EVENT.VAKUU", "RELIC.WHISPERING_EARRING", "8DYGVNBPVT", ObtainIt),
    }.ToDictionary(row => row.Relic, StringComparer.Ordinal);

    public static IEnumerable<object[]> AncientRelics() => AncientRows.Keys.Order(StringComparer.Ordinal).Select(relic => new object[] { relic });

    /// <summary>The acts list an ancient row's run is built on: the ancient's own act
    /// alone, so the run opens on that act's ancient.</summary>
    internal static IReadOnlyList<string> ActsOf(AncientRow row) =>
        [DecisionSurface.ActAncients().Single(pair => pair.AncientId == row.Ancient).ActId];

    /// <summary>
    /// Each ancient row on its own hunted seed and its own one-act list: the run opens
    /// on the row's ancient offering the relic, the walk obtains it through the recorded
    /// decision that names both and meets the ask past it, the recording reaches the
    /// ancient, its option, every seam the map lists for the relic and the points the
    /// row retires, and a fresh replay reproduces the journal decision for decision.
    /// </summary>
    [GameTheory]
    [MemberData(nameof(AncientRelics))]
    public void AnAncientRowDealsTheRelicReachesItsSeamsAndReplaysToParity(string relic)
    {
        var row = AncientRows[relic];
        var acts = ActsOf(row);
        var opening = SeedHunt.ReadOpening(row.Seed, acts);
        Assert.True(
            opening.OpeningEventId == row.Ancient && opening.Deals(row.Relic, SeedHunt.Dealer.Ancient),
            $"seed {row.Seed} on {acts[0]} alone no longer opens on {row.Ancient} offering {row.Relic} (it opens on " +
            $"{opening.OpeningEventId} offering {string.Join(", ", opening.OfferedRelics)}): the game's RNG has moved, " +
            "so rerun SeedHunt.Find for this row");

        using var harness = new RecordedActWalk();
        var recorded = harness.Walk(PolicyFor(row), row.Seed, visitEveryRoomType: false, acts);
        Assert.True(
            recorded.AskMet,
            $"the walk finished without meeting the ask of the {row.Relic} row ({row.Ask}); actions: " +
            string.Join(" ", recorded.Manifest.Actions.Select(action => action.Verb)));
        RecordedActWalk.AssertWhole(recorded);

        // The relic came through the ancient's own recorded decision naming it, so a
        // replay deals it the same way
        Assert.Contains(recorded.Manifest.Actions, action =>
            action.Verb == ActionVerb.ChooseEventOption
            && action.Args.TryGetValue("event_id", out var eventId) && eventId == row.Ancient
            && action.Args.TryGetValue("option_key", out var key) && key == row.Relic);

        var points = DecisionFacts.Of(recorded.Manifest);
        var reached = DecisionCoverage.SeamsReachedBy(
                new CoveredRecording(
                    recorded.Manifest.RunId, points, RecordingStanding.Of(recorded.Manifest.Source.Native),
                    DecisionFacts.ModelsMet(recorded.Manifest)),
                DecisionSurface.ProducerMap())
            .ToHashSet();
        foreach (var point in RetiredBy(row))
        {
            Assert.True(
                points.Contains(point) || reached.Contains(point),
                $"the {row.Relic} row's recording does not reach {point}; it reaches " +
                string.Join(", ", points.Concat(reached).Select(reachedPoint => reachedPoint.ToString())));
        }

        RecordedActWalk.ReplayToParity(recorded);
    }

    /// <summary>The ancient rows are exactly the options of the ancients act 2 and act
    /// 3 open on - every one a relic - and cover every relic the map says one of those
    /// ancients deals: an option a game update adds is a row somebody has to hunt a
    /// seed for, and a row for one the ancient no longer offers is a walk for nothing.
    /// Neow is act 1's and its rows are the producer table's; Darv is every act's
    /// question mark's and waits on the event hunt.</summary>
    [GameFact]
    public void TheAncientRowsAreTheOptionsOfTheActsAncients()
    {
        var ancients = DecisionSurface.ActAncients()
            .Where(pair => pair.AncientId != DecisionFacts.NeowEventId)
            .ToList();
        Assert.All(ancients, pair => Assert.Single(DecisionSurface.ActsReaching(pair.AncientId)));

        var options = DecisionSurface.EventOptionKeys()
            .Where(option => ancients.Any(pair => pair.AncientId == option.EventId))
            .ToList();
        Assert.All(options, option => Assert.StartsWith("RELIC.", option.Key, StringComparison.Ordinal));
        Assert.Equal(
            options.Select(option => (option.EventId, option.Key)).Order(),
            AncientRows.Values.Select(row => (row.Ancient, row.Relic)).Order());

        var dealtByAnActsAncient = DecisionSurface.ProducerMap()
            .SelectMany(seam => seam.Producers)
            .Where(producer => producer.StartsWith("RELIC.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Where(relic => DecisionSurface.DealtBy(relic)
                .Any(dealing => dealing.Mechanism == "ancient" && ancients.Any(pair => pair.AncientId == dealing.By)))
            .ToList();
        Assert.All(dealtByAnActsAncient, relic => Assert.Contains(relic, AncientRows.Keys));
    }

    /// <summary>The producer rows are exactly the relics the map says Neow, a chest or
    /// a shop deals, less the one the game allows only with another player: a relic
    /// the map adds is a row somebody has to hunt a seed for, and a row for a relic
    /// the map no longer lists is a walk for nothing.</summary>
    [GameFact]
    public void TheProducerRowsAreTheMapsNeowAndBagProducers()
    {
        var dealt = DecisionSurface.ProducerMap()
            .SelectMany(seam => seam.Producers)
            .Where(producer => producer.StartsWith("RELIC.", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Where(relic => DecisionSurface.DealtBy(relic).Any(dealing => dealing.Mechanism is "neow" or "grab-bag" or "shop"))
            .ToList();
        var withheld = dealt.Where(DecisionSurface.OfferedOnlyWithAnotherPlayer).ToList();
        Assert.Equal(["RELIC.MASSIVE_SCROLL"], withheld);

        Assert.Equal(
            dealt.Except(withheld, StringComparer.Ordinal).Order(StringComparer.Ordinal),
            ProducerRows.Keys.Order(StringComparer.Ordinal));

        // Each row's dealer is one the map says deals its relic
        foreach (var row in ProducerRows.Values)
        {
            var mechanisms = DecisionSurface.DealtBy(row.Relic).Select(dealing => dealing.Mechanism).ToList();
            var expected = row.Dealer switch
            {
                SeedHunt.Dealer.Neow => "neow",
                SeedHunt.Dealer.Shop => "shop",
                SeedHunt.Dealer.Chest => "grab-bag",
                _ => throw new ArgumentOutOfRangeException(nameof(row), row.Dealer, "not a dealer"),
            };
            Assert.Contains(expected, mechanisms);
        }
    }

    /// <summary>The rows name every point <c>DecisionExcusals</c> credits to this test,
    /// and nothing else: a row for a point the committed corpus already reaches is a
    /// whole recorded act on every merge for nothing, and a point credited here that
    /// no row reaches is an excusal nothing holds.</summary>
    [GameFact]
    public void TheRowsAreExactlyThePointsExcusedOntoThisTest()
    {
        var rows = Rows()
            .SelectMany(row => new[] { (string)row[1], (string)row[2] })
            .Where(point => point.Length > 0)
            .Concat(ProducerRows.Values.SelectMany(RetiredBy).Select(point => point.ToString()))
            .Concat(AncientRows.Values.SelectMany(RetiredBy).Select(point => point.ToString()))
            .ToHashSet(StringComparer.Ordinal);
        var credited = DecisionExcusals.All
            .Where(excusal => excusal.Value.Reason.Contains(nameof(GeneratedCoverageTests), StringComparison.Ordinal))
            .Select(excusal => excusal.Key.ToString())
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(credited);
        Assert.Equal(credited.Order(StringComparer.Ordinal), rows.Order(StringComparer.Ordinal));
    }

    /// <summary>The points a producer row retires: every seam the map lists the relic
    /// under, the blessing where Neow deals it, and the points its policy is for.</summary>
    private static IEnumerable<DecisionPoint> RetiredBy(ProducerRow row)
    {
        foreach (var seam in DecisionSurface.ProducerMap().Where(seam => seam.Producers.Contains(row.Relic, StringComparer.Ordinal)))
        {
            yield return seam.Point;
        }

        if (row.Dealer == SeedHunt.Dealer.Neow) yield return DecisionPoint.EventOption(DecisionFacts.NeowEventId, row.Relic);
        foreach (var point in row.AlsoRetires)
        {
            var parts = point.Split("  ", 2);
            yield return new DecisionPoint(parts[0], parts[1]);
        }
    }

    /// <summary>The seam every ancient's page is answered at, retired by every ancient
    /// row: the ancients' own option pools. The initial-options seam the ancients
    /// share with every event is the committed corpus's already.</summary>
    private const string AncientOptionSeam = "seam  event-option @ AncientEventModel.AllPossibleOptions";

    /// <summary>The points an ancient row retires: the ancient, its option granting the
    /// relic, the ancients' own option seam, every seam the map lists the relic under
    /// but the ones the row falls short of, and the points its policy is for.</summary>
    private static IEnumerable<DecisionPoint> RetiredBy(AncientRow row)
    {
        yield return new DecisionPoint(DecisionKinds.Event, row.Ancient);
        yield return DecisionPoint.EventOption(row.Ancient, row.Relic);
        yield return Point(AncientOptionSeam);
        var shortOf = ShortOf(row).ToHashSet();
        foreach (var seam in DecisionSurface.ProducerMap().Where(seam => seam.Producers.Contains(row.Relic, StringComparer.Ordinal)))
        {
            if (!shortOf.Contains(seam.Point)) yield return seam.Point;
        }

        foreach (var point in row.AlsoRetires ?? [])
        {
            yield return Point(point);
        }
    }

    private static IEnumerable<DecisionPoint> ShortOf(AncientRow row) => (row.ShortOf ?? []).Select(Point);

    private static DecisionPoint Point(string point)
    {
        var parts = point.Split("  ", 2);
        return new DecisionPoint(parts[0], parts[1]);
    }

    /// <summary>What an ancient row falls short of is a seam the map lists its relic
    /// under, and is excused by what stands in the way rather than credited to this
    /// test: the sentence has to say the line does not survive there, so the excusal
    /// is read for what it is and retired by a line that does.</summary>
    [GameFact]
    public void WhatAnAncientRowFallsShortOfIsExcusedByTheLinesSurvival()
    {
        var shortOf = AncientRows.Values.SelectMany(row => ShortOf(row).Select(point => (row.Relic, Point: point))).ToList();
        Assert.NotEmpty(shortOf);
        foreach (var (relic, point) in shortOf)
        {
            Assert.Contains(
                DecisionSurface.ProducerMap(),
                seam => seam.Point == point && seam.Producers.Contains(relic, StringComparer.Ordinal));
            var excusal = Assert.Contains(point, DecisionExcusals.All);
            Assert.Equal(ExcusalClass.NotOnTheRoute, excusal.Class);
            Assert.Contains("does not survive", excusal.Reason, StringComparison.Ordinal);
            Assert.DoesNotContain(nameof(GeneratedCoverageTests), excusal.Reason, StringComparison.Ordinal);
        }
    }

    internal static WalkPolicy PolicyFor(AncientRow row)
    {
        var policy = new WalkPolicy { AncientRelic = row.Relic };
        return row.Ask switch
        {
            ObtainIt => policy,
            ClaimTheRelicItOffers => policy with { ClaimTheRelicReward = true },
            AFightHoldingIt => policy with { FightWhileHoldingIt = true, RouteThrough = [MapPointType.Monster] },
            AShopHoldingIt => policy with { ShopWhileHoldingIt = true, RouteThrough = [MapPointType.Shop] },
            SacrificeACardReward => policy with { CardRewardAlternative = "SACRIFICE", RouteThrough = [MapPointType.Monster] },
            _ => throw new ArgumentOutOfRangeException(nameof(row), row.Ask, "no such ask"),
        };
    }

    /// <summary>Whether an action is the recorded decision that dealt the relic.</summary>
    private static bool Deals(ActionRecord action, string relic) =>
        action.Verb switch
        {
            ActionVerb.ChooseNeowBlessing => action.Args.TryGetValue("option_key", out var key) && key == relic,
            ActionVerb.TakeChestRelic or ActionVerb.ShopPurchase or ActionVerb.ClaimReward =>
                action.Args.TryGetValue("relic_id", out var id) && id == relic,
            _ => false,
        };

    internal static WalkPolicy PolicyFor(ProducerRow row)
    {
        var policy = row.Dealer switch
        {
            SeedHunt.Dealer.Neow => new WalkPolicy { NeowRelic = row.Relic },
            SeedHunt.Dealer.Shop => new WalkPolicy { BagRelic = row.Relic, RouteThrough = [MapPointType.Shop] },
            SeedHunt.Dealer.Chest => new WalkPolicy { BagRelic = row.Relic, RouteThrough = [MapPointType.Treasure] },
            _ => throw new ArgumentOutOfRangeException(nameof(row), row.Dealer, "not a dealer"),
        };

        // The room the ask is met in comes after the room that deals the relic: the
        // route is ordered and ends there
        return row.Ask switch
        {
            ObtainIt => policy,
            ClaimTheRelicItOffers => policy with { ClaimTheRelicReward = true },
            AFightHoldingIt => policy with { FightWhileHoldingIt = true, RouteThrough = Then(policy, MapPointType.Monster) },
            TwoGolds => policy with { ClaimTwoOfAKind = true, RouteThrough = Then(policy, MapPointType.Monster) },
            _ when row.Ask.StartsWith("rest ", StringComparison.Ordinal) => policy with
            {
                RestOption = row.Ask["rest ".Length..],
                RouteThrough = Then(policy, MapPointType.RestSite),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(row), row.Ask, "no such ask"),
        };
    }

    private static IReadOnlyList<MapPointType>? Then(WalkPolicy policy, MapPointType type) =>
        policy.RouteThrough is { } through ? [.. through, type] : null;

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

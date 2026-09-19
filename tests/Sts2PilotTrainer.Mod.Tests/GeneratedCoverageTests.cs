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
/// one; Darv opens no act and is dealt to one act after the first at the run's start,
/// so its options wait on a walk that survives the first act.
///
/// The event rows are the fourth table: one per option of every event a question
/// mark opens on this build, each on a seed hunted so the act's first question mark
/// rolls an event and deals that one, on the acts list the event is reachable on -
/// the default progression, its Underdocks variant, or the Hive or Glory alone, on
/// the ancient rows' footing. The walk routes to the mark, answers the event's pages
/// by key through the recorded <c>ChooseEventOption</c>, fights where an option
/// fights and claims what an option offers, and the row retires the event, the
/// option and the seams its recording reaches by co-occurrence. Beside them the
/// thief row kills the Thieving Hopper holding the card it stole and claims the
/// special card off the loot screen. The table is held to the excusals: every
/// event and option <c>DecisionExcusals</c> credits to this test has a row, and
/// every row's points are credited.
///
/// What no row here can reach is what <c>DecisionExcusals</c> leaves excused with a
/// reason of its own: what the headless host has no screen for, the undo of an ended
/// turn, and every point only the second act on deals - Darv's options, the events
/// the game allows from act 2 on, the card removal only a Necrobinder's Forbidden
/// Grimoire puts on the loot screen - because no hunted seed of the walk's own line
/// survives the first act, and the one that does opens its second act on Orobas; a
/// row there takes a survival seed of the fifth stage, with the row field and the
/// hunt reading it needs added beside it (the walk into a second act itself,
/// <see cref="WalkPolicy.AskInTheNextAct"/>, is built and held by
/// <c>ReplayRefusalRegressionTests</c>).
/// </summary>
public sealed class GeneratedCoverageTests
{
    /// <summary>Before any test method here is prepared, so the game assembly the
    /// harness names resolves; the harness's own teardown forgets it again.</summary>
    public GeneratedCoverageTests() => EngineHost.Start();

    /// <summary>The build every row's recording is made on and stood against, read
    /// once because reading it hashes the prepared assemblies.</summary>
    private static readonly Lazy<LocalBuild> ThisBuild = new(() => GameIdentity.Read().Build);

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
                    recorded.Manifest.RunId, points,
                    RecordingStanding.Of(recorded.Manifest.Source.Native, recorded.Manifest.Environment, ThisBuild.Value, RunmobileVersion.Current),
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
                    recorded.Manifest.RunId, points,
                    RecordingStanding.Of(recorded.Manifest.Source.Native, recorded.Manifest.Environment, ThisBuild.Value, RunmobileVersion.Current),
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
    /// One event row: the event, the option it chooses by the key the recorder writes,
    /// the acts list its run is built on, the seed the hunt found - one whose first
    /// question mark opens the event, with the walk's own route to it - and the way to
    /// the option's page where the page is not the first and not the one the option
    /// named for it opens (<see cref="WalkPolicy.EventOptionsOnTheWay"/>), with the
    /// seams past the event's own points the row retires. A seed is a constant with
    /// the hunt's criterion beside it, as <see cref="SeedHunt"/> says, and here the
    /// walk is the criterion: which event a question mark opens is the act's shuffled
    /// set read past what the run allows by the time it gets there, so a seed that no
    /// longer opens the event fails the row by name at the mark rather than at a
    /// reading of the opening.
    /// </summary>
    /// <param name="ClaimsReward">The reward kind the row is after past the option -
    /// the special card the Lantern Key's fight earns - claimed off the loot screen
    /// the option's fight ends on, which is then the ask rather than the option.</param>
    /// <param name="FightsFirst">How many fights the route passes before the question
    /// mark: an event allowed only with gold in hand is reached with the fights' gold.</param>
    internal sealed record EventRow(
        string Event, string Key, string Seed, string[]? Via = null, string[]? AlsoRetires = null,
        string? ClaimsReward = null, int FightsFirst = 0);

    /// <summary>The acts lists the event rows run on: the default progression for act
    /// 1's events and the shared pool; its variant for the Underdocks' own; and the
    /// Hive and Glory alone for theirs, generated-only lists no client run has,
    /// admissible as the ancient rows' are and said to be so in the rows' excusals.</summary>
    internal static readonly IReadOnlyDictionary<string, string[]> EventRowActs = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["ACT.OVERGROWTH"] = ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"],
        ["ACT.UNDERDOCKS"] = ["ACT.UNDERDOCKS", "ACT.HIVE", "ACT.GLORY"],
        ["ACT.HIVE"] = ["ACT.HIVE"],
        ["ACT.GLORY"] = ["ACT.GLORY"],
    };

    /// <summary>The event rows, by the act whose question mark opens the event; the
    /// seeds were found in <c>SeedHunt</c>'s candidate order on v0.111.0, each the
    /// first whose opening's first mark rolls an event and whose walk under the row's
    /// policy chose the key (<c>SeedHunt.Find</c> over
    /// <c>Opening.OpensAtTheFirstQuestionMark</c>, with the walk as the second
    /// criterion). A row whose seed stops opening its event is hunted again the same
    /// way, from a scratch test.</summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<EventRow>> EventRows = new Dictionary<string, IReadOnlyList<EventRow>>(StringComparer.Ordinal)
    {
        ["ACT.OVERGROWTH"] =
        [
            new("EVENT.AROMA_OF_CHAOS", "AROMA_OF_CHAOS.pages.INITIAL.options.LET_GO", "C1GAV23WHA"),
            new("EVENT.AROMA_OF_CHAOS", "AROMA_OF_CHAOS.pages.INITIAL.options.MAINTAIN_CONTROL", "C1GAV23WHA"),
            new("EVENT.BRAIN_LEECH", "BRAIN_LEECH.pages.INITIAL.options.RIP", "HCM40F3ZGY"),
            new("EVENT.BRAIN_LEECH", "BRAIN_LEECH.pages.INITIAL.options.SHARE_KNOWLEDGE", "HCM40F3ZGY"),
            new("EVENT.BYRDONIS_NEST", "BYRDONIS_NEST.pages.INITIAL.options.EAT", "S7LTRQKC10"),
            new("EVENT.BYRDONIS_NEST", "BYRDONIS_NEST.pages.INITIAL.options.TAKE", "S7LTRQKC10"),
            new("EVENT.DENSE_VEGETATION", "DENSE_VEGETATION.pages.INITIAL.options.REST", "HHNZNPJV6W", AlsoRetires:
                ["seam  card-prompt:CardSelectCmd.FromChooseACardScreen(context, cards, player, canSkip) @ CardModel.OnPlay"]),
            new("EVENT.DENSE_VEGETATION", "DENSE_VEGETATION.pages.INITIAL.options.TRUDGE_ON", "HHNZNPJV6W"),
            new("EVENT.DENSE_VEGETATION", "DENSE_VEGETATION.pages.REST.options.FIGHT", "HHNZNPJV6W", ["DENSE_VEGETATION.pages.INITIAL.options.REST"]),
            new("EVENT.JUNGLE_MAZE_ADVENTURE", "JUNGLE_MAZE_ADVENTURE.pages.INITIAL.options.JOIN_FORCES", "41MV0020T4"),
            new("EVENT.JUNGLE_MAZE_ADVENTURE", "JUNGLE_MAZE_ADVENTURE.pages.INITIAL.options.SOLO_QUEST", "41MV0020T4"),
            new("EVENT.LUMINOUS_CHOIR", "LUMINOUS_CHOIR.pages.INITIAL.options.OFFER_TRIBUTE", "P2NESDSW51"),
            new("EVENT.LUMINOUS_CHOIR", "LUMINOUS_CHOIR.pages.INITIAL.options.REACH_INTO_THE_FLESH", "P2NESDSW51", AlsoRetires:
                ["seam  card-prompt:CardSelectCmd.FromDeckForRemoval(player, prefs, filter) @ EventModel.GenerateInitialOptions"]),
            new("EVENT.MORPHIC_GROVE", "MORPHIC_GROVE.pages.INITIAL.options.GROUP", "ZBCVW5ENJ4"),
            new("EVENT.MORPHIC_GROVE", "MORPHIC_GROVE.pages.INITIAL.options.LONER", "ZBCVW5ENJ4"),
            new("EVENT.ROOM_FULL_OF_CHEESE", "ROOM_FULL_OF_CHEESE.pages.INITIAL.options.GORGE", "RAGWB3H44W"),
            new("EVENT.ROOM_FULL_OF_CHEESE", "ROOM_FULL_OF_CHEESE.pages.INITIAL.options.SEARCH", "RAGWB3H44W"),
            new("EVENT.SAPPHIRE_SEED", "SAPPHIRE_SEED.pages.INITIAL.options.EAT", "XTXVMBG3WB"),
            new("EVENT.SAPPHIRE_SEED", "SAPPHIRE_SEED.pages.INITIAL.options.PLANT", "XTXVMBG3WB", AlsoRetires:
                ["seam  card-prompt:CardSelectCmd.FromChooseACardScreen(context, cards, player, canSkip) @ PotionModel.OnUse", "seam  card-prompt:CardSelectCmd.FromDeckForEnchantment(cards, enchantment, amount, prefs) @ EventModel.GenerateInitialOptions", "seam  card-prompt:CardSelectCmd.FromDeckForUpgrade(player, prefs) @ EventModel.GenerateInitialOptions"]),
            new("EVENT.SELF_HELP_BOOK", "SELF_HELP_BOOK.pages.INITIAL.options.READ_ENTIRE_BOOK", "3EJVKS6VER"),
            new("EVENT.SELF_HELP_BOOK", "SELF_HELP_BOOK.pages.INITIAL.options.READ_PASSAGE", "R6JEKSNR6W", AlsoRetires:
                ["seam  card-prompt:CardSelectCmd.FromDeckForEnchantment(player, enchantment, amount, additionalFilter, prefs) @ EventModel.GenerateInitialOptions"]),
            new("EVENT.SELF_HELP_BOOK", "SELF_HELP_BOOK.pages.INITIAL.options.READ_THE_BACK", "R6JEKSNR6W"),
            new("EVENT.SUNKEN_STATUE", "SUNKEN_STATUE.pages.INITIAL.options.DIVE_INTO_WATER", "X7KJSBLHQ6"),
            new("EVENT.SUNKEN_STATUE", "SUNKEN_STATUE.pages.INITIAL.options.GRAB_SWORD", "X7KJSBLHQ6", AlsoRetires:
                ["seam  card-prompt:CardSelectCmd.FromHandForUpgrade(context, player, source) @ CardModel.OnPlay"]),
            new("EVENT.TABLET_OF_TRUTH", "TABLET_OF_TRUTH.pages.DECIPHER.options.GIVE_UP", "HQUHYBESLV", ["TABLET_OF_TRUTH.pages.INITIAL.options.DECIPHER_1"],
                ["seam  card-prompt:CardSelectCmd.FromCombatPile(context, pile, player, prefs) @ CardModel.OnPlay"]),
            new("EVENT.TABLET_OF_TRUTH", "TABLET_OF_TRUTH.pages.DECIPHER_1.options.DECIPHER", "HQUHYBESLV", ["TABLET_OF_TRUTH.pages.INITIAL.options.DECIPHER_1"]),
            new("EVENT.TABLET_OF_TRUTH", "TABLET_OF_TRUTH.pages.DECIPHER_2.options.DECIPHER", "HQUHYBESLV", ["TABLET_OF_TRUTH.pages.INITIAL.options.DECIPHER_1", "TABLET_OF_TRUTH.pages.DECIPHER_1.options.DECIPHER"]),
            new("EVENT.TABLET_OF_TRUTH", "TABLET_OF_TRUTH.pages.DECIPHER_3.options.DECIPHER", "HQUHYBESLV", ["TABLET_OF_TRUTH.pages.INITIAL.options.DECIPHER_1", "TABLET_OF_TRUTH.pages.DECIPHER_1.options.DECIPHER", "TABLET_OF_TRUTH.pages.DECIPHER_2.options.DECIPHER"]),
            new("EVENT.TABLET_OF_TRUTH", "TABLET_OF_TRUTH.pages.DECIPHER_4.options.DECIPHER", "HQUHYBESLV", ["TABLET_OF_TRUTH.pages.INITIAL.options.DECIPHER_1", "TABLET_OF_TRUTH.pages.DECIPHER_1.options.DECIPHER", "TABLET_OF_TRUTH.pages.DECIPHER_2.options.DECIPHER", "TABLET_OF_TRUTH.pages.DECIPHER_3.options.DECIPHER"]),
            new("EVENT.TABLET_OF_TRUTH", "TABLET_OF_TRUTH.pages.INITIAL.options.DECIPHER_1", "HQUHYBESLV"),
            new("EVENT.TABLET_OF_TRUTH", "TABLET_OF_TRUTH.pages.INITIAL.options.SMASH", "HQUHYBESLV"),
            new("EVENT.TEA_MASTER", "TEA_MASTER.pages.INITIAL.options.BONE_TEA", "J29SM9173P", FightsFirst: 3),
            new("EVENT.TEA_MASTER", "TEA_MASTER.pages.INITIAL.options.EMBER_TEA", "J29SM9173P", FightsFirst: 3),
            new("EVENT.TEA_MASTER", "TEA_MASTER.pages.INITIAL.options.TEA_OF_DISCOURTESY", "J29SM9173P", FightsFirst: 3),
            new("EVENT.THE_FUTURE_OF_POTIONS", "THE_FUTURE_OF_POTIONS.pages.INITIAL.options.POTION", "3DKM6HUFZY"),
            new("EVENT.THE_LEGENDS_WERE_TRUE", "THE_LEGENDS_WERE_TRUE.pages.INITIAL.options.NAB_THE_MAP", "0WA4C6C2KF"),
            new("EVENT.THE_LEGENDS_WERE_TRUE", "THE_LEGENDS_WERE_TRUE.pages.INITIAL.options.SLOWLY_FIND_AN_EXIT", "0WA4C6C2KF"),
            new("EVENT.THIS_OR_THAT", "THIS_OR_THAT.pages.INITIAL.options.ORNATE", "N2E2AGFGSN"),
            new("EVENT.THIS_OR_THAT", "THIS_OR_THAT.pages.INITIAL.options.PLAIN", "N2E2AGFGSN"),
            new("EVENT.UNREST_SITE", "UNREST_SITE.pages.INITIAL.options.KILL", "88K9GG44KQ"),
            new("EVENT.UNREST_SITE", "UNREST_SITE.pages.INITIAL.options.REST", "88K9GG44KQ"),
            new("EVENT.WELLSPRING", "WELLSPRING.pages.INITIAL.options.BATHE", "8DYGVNBPVT"),
            new("EVENT.WELLSPRING", "WELLSPRING.pages.INITIAL.options.BOTTLE", "8DYGVNBPVT"),
            new("EVENT.WHISPERING_HOLLOW", "WHISPERING_HOLLOW.pages.INITIAL.options.GOLD", "HZ4GLM854W"),
            new("EVENT.WHISPERING_HOLLOW", "WHISPERING_HOLLOW.pages.INITIAL.options.HUG", "HZ4GLM854W", AlsoRetires:
                ["seam  card-prompt:CardSelectCmd.FromDeckForTransformation(player, prefs, cardToTransformation) @ EventModel.GenerateInitialOptions", "seam  reward-kind:potion @ EventModel.GenerateInitialOptions"]),
            new("EVENT.WOOD_CARVINGS", "WOOD_CARVINGS.pages.INITIAL.options.BIRD", "Y1NN8NJF3P"),
            new("EVENT.WOOD_CARVINGS", "WOOD_CARVINGS.pages.INITIAL.options.SNAKE", "Y1NN8NJF3P", AlsoRetires:
                ["seam  card-prompt:CardSelectCmd.FromDeckGeneric(player, prefs, filter, sortingOrder) @ EventModel.GenerateInitialOptions"]),
            new("EVENT.WOOD_CARVINGS", "WOOD_CARVINGS.pages.INITIAL.options.TORUS", "Y1NN8NJF3P"),
        ],
        ["ACT.UNDERDOCKS"] =
        [
            new("EVENT.ABYSSAL_BATHS", "ABYSSAL_BATHS.pages.ALL.options.EXIT_BATHS", "HZ4GLM854W", ["ABYSSAL_BATHS.pages.INITIAL.options.IMMERSE"]),
            new("EVENT.ABYSSAL_BATHS", "ABYSSAL_BATHS.pages.ALL.options.LINGER", "HZ4GLM854W", ["ABYSSAL_BATHS.pages.INITIAL.options.IMMERSE"]),
            new("EVENT.ABYSSAL_BATHS", "ABYSSAL_BATHS.pages.INITIAL.options.ABSTAIN", "HZ4GLM854W"),
            new("EVENT.ABYSSAL_BATHS", "ABYSSAL_BATHS.pages.INITIAL.options.IMMERSE", "HZ4GLM854W"),
            new("EVENT.DOORS_OF_LIGHT_AND_DARK", "DOORS_OF_LIGHT_AND_DARK.pages.INITIAL.options.DARK", "3DKM6HUFZY"),
            new("EVENT.DOORS_OF_LIGHT_AND_DARK", "DOORS_OF_LIGHT_AND_DARK.pages.INITIAL.options.LIGHT", "3DKM6HUFZY"),
            new("EVENT.DROWNING_BEACON", "DROWNING_BEACON.pages.INITIAL.options.BOTTLE", "ZBCVW5ENJ4"),
            new("EVENT.DROWNING_BEACON", "DROWNING_BEACON.pages.INITIAL.options.CLIMB", "ZBCVW5ENJ4"),
            new("EVENT.ENDLESS_CONVEYOR", "ENDLESS_CONVEYOR.pages.ALL.options.CAVIAR", "X2BN5AEZ5Q", AlsoRetires:
                ["seam  reward-kind:potion @ EventModel.CalculateVars", "seam  rewards:OfferCustom @ EventModel.CalculateVars"]),
            new("EVENT.ENDLESS_CONVEYOR", "ENDLESS_CONVEYOR.pages.ALL.options.CLAM_ROLL", "53U2DTB517"),
            new("EVENT.ENDLESS_CONVEYOR", "ENDLESS_CONVEYOR.pages.ALL.options.FRIED_EEL", "K9V81PBRLS"),
            new("EVENT.ENDLESS_CONVEYOR", "ENDLESS_CONVEYOR.pages.ALL.options.GOLDEN_FYSH", "4Y25FLUW1X", ["ENDLESS_CONVEYOR.pages.ALL.options.SPICY_SNAPPY", "ENDLESS_CONVEYOR.pages.ALL.options.CLAM_ROLL"]),
            new("EVENT.ENDLESS_CONVEYOR", "ENDLESS_CONVEYOR.pages.ALL.options.JELLY_LIVER", "41E281DU7R", AlsoRetires:
                ["seam  card-prompt:CardSelectCmd.FromHand(context, player, prefs, filter, source) @ PotionModel.OnUse"]),
            new("EVENT.ENDLESS_CONVEYOR", "ENDLESS_CONVEYOR.pages.ALL.options.SEAPUNK_SALAD", "4Y25FLUW1X", ["ENDLESS_CONVEYOR.pages.ALL.options.SPICY_SNAPPY", "ENDLESS_CONVEYOR.pages.ALL.options.CLAM_ROLL", "ENDLESS_CONVEYOR.pages.ALL.options.GOLDEN_FYSH", "ENDLESS_CONVEYOR.pages.ALL.options.CAVIAR"]),
            new("EVENT.ENDLESS_CONVEYOR", "ENDLESS_CONVEYOR.pages.ALL.options.SPICY_SNAPPY", "4Y25FLUW1X", AlsoRetires:
                ["seam  card-prompt:CardSelectCmd.FromDeckForTransformation(player, prefs, cardToTransformation) @ EventModel.CalculateVars"]),
            new("EVENT.ENDLESS_CONVEYOR", "ENDLESS_CONVEYOR.pages.ALL.options.SUSPICIOUS_CONDIMENT", "41E281DU7R", ["ENDLESS_CONVEYOR.pages.ALL.options.JELLY_LIVER", "ENDLESS_CONVEYOR.pages.ALL.options.FRIED_EEL"]),
            new("EVENT.ENDLESS_CONVEYOR", "ENDLESS_CONVEYOR.pages.GRAB_SOMETHING_OFF_THE_BELT.options.LEAVE", "41E281DU7R", ["ENDLESS_CONVEYOR.pages.ALL.options.JELLY_LIVER"]),
            new("EVENT.ENDLESS_CONVEYOR", "ENDLESS_CONVEYOR.pages.INITIAL.options.OBSERVE_CHEF", "41E281DU7R"),
            new("EVENT.PUNCH_OFF", "PUNCH_OFF.pages.INITIAL.options.I_CAN_TAKE_THEM", "Y468CL2JJF"),
            new("EVENT.PUNCH_OFF", "PUNCH_OFF.pages.INITIAL.options.NAB", "Y468CL2JJF"),
            new("EVENT.PUNCH_OFF", "PUNCH_OFF.pages.I_CAN_TAKE_THEM.options.FIGHT", "Y468CL2JJF", ["PUNCH_OFF.pages.INITIAL.options.I_CAN_TAKE_THEM"]),
            new("EVENT.SLIPPERY_BRIDGE", "SLIPPERY_BRIDGE.pages.HOLD_ON_0.options.HOLD_ON_1", "6KGKGA4S8P", ["SLIPPERY_BRIDGE.pages.INITIAL.options.HOLD_ON_0"]),
            new("EVENT.SLIPPERY_BRIDGE", "SLIPPERY_BRIDGE.pages.HOLD_ON_1.options.HOLD_ON_2", "6KGKGA4S8P", ["SLIPPERY_BRIDGE.pages.INITIAL.options.HOLD_ON_0", "SLIPPERY_BRIDGE.pages.HOLD_ON_0.options.HOLD_ON_1"]),
            new("EVENT.SLIPPERY_BRIDGE", "SLIPPERY_BRIDGE.pages.HOLD_ON_2.options.HOLD_ON_3", "6KGKGA4S8P", ["SLIPPERY_BRIDGE.pages.INITIAL.options.HOLD_ON_0", "SLIPPERY_BRIDGE.pages.HOLD_ON_0.options.HOLD_ON_1", "SLIPPERY_BRIDGE.pages.HOLD_ON_1.options.HOLD_ON_2"]),
            new("EVENT.SLIPPERY_BRIDGE", "SLIPPERY_BRIDGE.pages.HOLD_ON_3.options.HOLD_ON_4", "6KGKGA4S8P", ["SLIPPERY_BRIDGE.pages.INITIAL.options.HOLD_ON_0", "SLIPPERY_BRIDGE.pages.HOLD_ON_0.options.HOLD_ON_1", "SLIPPERY_BRIDGE.pages.HOLD_ON_1.options.HOLD_ON_2", "SLIPPERY_BRIDGE.pages.HOLD_ON_2.options.HOLD_ON_3"]),
            new("EVENT.SLIPPERY_BRIDGE", "SLIPPERY_BRIDGE.pages.HOLD_ON_4.options.HOLD_ON_5", "7744SSMSMG", ["SLIPPERY_BRIDGE.pages.INITIAL.options.HOLD_ON_0", "SLIPPERY_BRIDGE.pages.HOLD_ON_0.options.HOLD_ON_1", "SLIPPERY_BRIDGE.pages.HOLD_ON_1.options.HOLD_ON_2", "SLIPPERY_BRIDGE.pages.HOLD_ON_2.options.HOLD_ON_3", "SLIPPERY_BRIDGE.pages.HOLD_ON_3.options.HOLD_ON_4"]),
            new("EVENT.SLIPPERY_BRIDGE", "SLIPPERY_BRIDGE.pages.HOLD_ON_5.options.HOLD_ON_6", "7744SSMSMG", ["SLIPPERY_BRIDGE.pages.INITIAL.options.HOLD_ON_0", "SLIPPERY_BRIDGE.pages.HOLD_ON_0.options.HOLD_ON_1", "SLIPPERY_BRIDGE.pages.HOLD_ON_1.options.HOLD_ON_2", "SLIPPERY_BRIDGE.pages.HOLD_ON_2.options.HOLD_ON_3", "SLIPPERY_BRIDGE.pages.HOLD_ON_3.options.HOLD_ON_4", "SLIPPERY_BRIDGE.pages.HOLD_ON_4.options.HOLD_ON_5"]),
            new("EVENT.SLIPPERY_BRIDGE", "SLIPPERY_BRIDGE.pages.HOLD_ON_6.options.HOLD_ON_LOOP", "5F5F95T33D", ["SLIPPERY_BRIDGE.pages.INITIAL.options.HOLD_ON_0", "SLIPPERY_BRIDGE.pages.HOLD_ON_0.options.HOLD_ON_1", "SLIPPERY_BRIDGE.pages.HOLD_ON_1.options.HOLD_ON_2", "SLIPPERY_BRIDGE.pages.HOLD_ON_2.options.HOLD_ON_3", "SLIPPERY_BRIDGE.pages.HOLD_ON_3.options.HOLD_ON_4", "SLIPPERY_BRIDGE.pages.HOLD_ON_4.options.HOLD_ON_5", "SLIPPERY_BRIDGE.pages.HOLD_ON_5.options.HOLD_ON_6"]),
            new("EVENT.SLIPPERY_BRIDGE", "SLIPPERY_BRIDGE.pages.HOLD_ON_LOOP.options.HOLD_ON_LOOP", "5F5F95T33D", ["SLIPPERY_BRIDGE.pages.INITIAL.options.HOLD_ON_0", "SLIPPERY_BRIDGE.pages.HOLD_ON_0.options.HOLD_ON_1", "SLIPPERY_BRIDGE.pages.HOLD_ON_1.options.HOLD_ON_2", "SLIPPERY_BRIDGE.pages.HOLD_ON_2.options.HOLD_ON_3", "SLIPPERY_BRIDGE.pages.HOLD_ON_3.options.HOLD_ON_4", "SLIPPERY_BRIDGE.pages.HOLD_ON_4.options.HOLD_ON_5", "SLIPPERY_BRIDGE.pages.HOLD_ON_5.options.HOLD_ON_6", "SLIPPERY_BRIDGE.pages.HOLD_ON_6.options.HOLD_ON_LOOP"],
                ["seam  card-prompt:CardSelectCmd.FromCombatPile(context, pile, player, prefs) @ PotionModel.OnUse"]),
            new("EVENT.SLIPPERY_BRIDGE", "SLIPPERY_BRIDGE.pages.INITIAL.options.HOLD_ON_0", "6KGKGA4S8P"),
            new("EVENT.SLIPPERY_BRIDGE", "SLIPPERY_BRIDGE.pages.INITIAL.options.OVERCOME", "6KGKGA4S8P"),
            new("EVENT.SPIRALING_WHIRLPOOL", "SPIRALING_WHIRLPOOL.pages.INITIAL.options.DRINK", "RAGWB3H44W"),
            new("EVENT.SPIRALING_WHIRLPOOL", "SPIRALING_WHIRLPOOL.pages.INITIAL.options.OBSERVE", "RAGWB3H44W"),
            new("EVENT.SUNKEN_TREASURY", "SUNKEN_TREASURY.pages.INITIAL.options.FIRST_CHEST", "41MV0020T4"),
            new("EVENT.SUNKEN_TREASURY", "SUNKEN_TREASURY.pages.INITIAL.options.SECOND_CHEST", "41MV0020T4"),
            new("EVENT.TRASH_HEAP", "TRASH_HEAP.pages.INITIAL.options.DIVE_IN", "0WA4C6C2KF"),
            new("EVENT.TRASH_HEAP", "TRASH_HEAP.pages.INITIAL.options.GRAB", "0WA4C6C2KF"),
            new("EVENT.WATERLOGGED_SCRIPTORIUM", "WATERLOGGED_SCRIPTORIUM.pages.INITIAL.options.BLOODY_INK", "D72PWCFLVE"),
            new("EVENT.WATERLOGGED_SCRIPTORIUM", "WATERLOGGED_SCRIPTORIUM.pages.INITIAL.options.PRICKLY_SPONGE", "D72PWCFLVE"),
            new("EVENT.WATERLOGGED_SCRIPTORIUM", "WATERLOGGED_SCRIPTORIUM.pages.INITIAL.options.TENTACLE_QUILL", "D72PWCFLVE"),
        ],
        ["ACT.HIVE"] =
        [
            new("EVENT.AMALGAMATOR", "AMALGAMATOR.pages.INITIAL.options.COMBINE_DEFENDS", "Q3P7J5WAQ4"),
            new("EVENT.AMALGAMATOR", "AMALGAMATOR.pages.INITIAL.options.COMBINE_STRIKES", "Q3P7J5WAQ4"),
            new("EVENT.BUGSLAYER", "BUGSLAYER.pages.INITIAL.options.EXTERMINATION", "ZBCVW5ENJ4"),
            new("EVENT.BUGSLAYER", "BUGSLAYER.pages.INITIAL.options.SQUASH", "ZBCVW5ENJ4"),
            new("EVENT.COLORFUL_PHILOSOPHERS", "COLORFUL_PHILOSOPHERS.pages.INITIAL.options.DEFECT", "87NGC17B9A"),
            new("EVENT.COLORFUL_PHILOSOPHERS", "COLORFUL_PHILOSOPHERS.pages.INITIAL.options.NECROBINDER", "HHNZNPJV6W"),
            new("EVENT.COLORFUL_PHILOSOPHERS", "COLORFUL_PHILOSOPHERS.pages.INITIAL.options.REGENT", "HHNZNPJV6W"),
            new("EVENT.COLORFUL_PHILOSOPHERS", "COLORFUL_PHILOSOPHERS.pages.INITIAL.options.SILENT", "HHNZNPJV6W"),
            new("EVENT.COLOSSAL_FLOWER", "COLOSSAL_FLOWER.pages.INITIAL.options.EXTRACT_CURRENT_PRIZE_1", "XTXVMBG3WB"),
            new("EVENT.COLOSSAL_FLOWER", "COLOSSAL_FLOWER.pages.INITIAL.options.REACH_DEEPER_1", "XTXVMBG3WB"),
            new("EVENT.COLOSSAL_FLOWER", "COLOSSAL_FLOWER.pages.REACH_DEEPER_1.options.EXTRACT_CURRENT_PRIZE_2", "XTXVMBG3WB", ["COLOSSAL_FLOWER.pages.INITIAL.options.REACH_DEEPER_1"]),
            new("EVENT.COLOSSAL_FLOWER", "COLOSSAL_FLOWER.pages.REACH_DEEPER_1.options.REACH_DEEPER_2", "XTXVMBG3WB", ["COLOSSAL_FLOWER.pages.INITIAL.options.REACH_DEEPER_1"]),
            new("EVENT.COLOSSAL_FLOWER", "COLOSSAL_FLOWER.pages.REACH_DEEPER_2.options.EXTRACT_INSTEAD", "XTXVMBG3WB", ["COLOSSAL_FLOWER.pages.INITIAL.options.REACH_DEEPER_1", "COLOSSAL_FLOWER.pages.REACH_DEEPER_1.options.REACH_DEEPER_2"]),
            new("EVENT.COLOSSAL_FLOWER", "COLOSSAL_FLOWER.pages.REACH_DEEPER_2.options.POLLINOUS_CORE", "XTXVMBG3WB", ["COLOSSAL_FLOWER.pages.INITIAL.options.REACH_DEEPER_1", "COLOSSAL_FLOWER.pages.REACH_DEEPER_1.options.REACH_DEEPER_2"]),
            new("EVENT.FIELD_OF_MAN_SIZED_HOLES", "FIELD_OF_MAN_SIZED_HOLES.pages.INITIAL.options.ENTER_YOUR_HOLE", "8DYGVNBPVT"),
            new("EVENT.FIELD_OF_MAN_SIZED_HOLES", "FIELD_OF_MAN_SIZED_HOLES.pages.INITIAL.options.RESIST", "8DYGVNBPVT"),
            new("EVENT.INFESTED_AUTOMATON", "INFESTED_AUTOMATON.pages.INITIAL.options.STUDY", "X7KJSBLHQ6"),
            new("EVENT.INFESTED_AUTOMATON", "INFESTED_AUTOMATON.pages.INITIAL.options.TOUCH_CORE", "X7KJSBLHQ6"),
            new("EVENT.LOST_WISP", "LOST_WISP.pages.INITIAL.options.CLAIM", "FPS9QRR1BM"),
            new("EVENT.LOST_WISP", "LOST_WISP.pages.INITIAL.options.SEARCH", "FPS9QRR1BM"),
            new("EVENT.SPIRIT_GRAFTER", "SPIRIT_GRAFTER.pages.INITIAL.options.LET_IT_IN", "ADX2BRHFBC"),
            new("EVENT.SPIRIT_GRAFTER", "SPIRIT_GRAFTER.pages.INITIAL.options.REJECTION", "ADX2BRHFBC"),
            new("EVENT.THE_LANTERN_KEY", "THE_LANTERN_KEY.pages.INITIAL.options.KEEP_THE_KEY", "SQY9ZWLSSV"),
            new("EVENT.THE_LANTERN_KEY", "THE_LANTERN_KEY.pages.INITIAL.options.RETURN_THE_KEY", "SQY9ZWLSSV"),
            new("EVENT.THE_LANTERN_KEY", "THE_LANTERN_KEY.pages.KEEP_THE_KEY.options.FIGHT", "SQY9ZWLSSV", ["THE_LANTERN_KEY.pages.INITIAL.options.KEEP_THE_KEY"],
                ["seam  reward-kind:special_card @ EventModel.GenerateInitialOptions"], ClaimsReward: RewardKinds.SpecialCard),
            new("EVENT.ZEN_WEAVER", "ZEN_WEAVER.pages.INITIAL.options.BREATHING_TECHNIQUES", "6H5C6JPV9W", FightsFirst: 2),
            new("EVENT.ZEN_WEAVER", "ZEN_WEAVER.pages.INITIAL.options.EMOTIONAL_AWARENESS", "6H5C6JPV9W", FightsFirst: 2),
        ],
        ["ACT.GLORY"] =
        [
            new("EVENT.BATTLEWORN_DUMMY", "BATTLEWORN_DUMMY.pages.INITIAL.options.SETTING_1", "N2E2AGFGSN", AlsoRetires:
                ["seam  rewards:OfferCustom @ EventModel.Resume"]),
            new("EVENT.BATTLEWORN_DUMMY", "BATTLEWORN_DUMMY.pages.INITIAL.options.SETTING_2", "N2E2AGFGSN"),
            new("EVENT.BATTLEWORN_DUMMY", "BATTLEWORN_DUMMY.pages.INITIAL.options.SETTING_3", "N2E2AGFGSN"),
            new("EVENT.HUNGRY_FOR_MUSHROOMS", "RELIC.BIG_MUSHROOM", "8DYGVNBPVT"),
            new("EVENT.HUNGRY_FOR_MUSHROOMS", "RELIC.FRAGRANT_MUSHROOM", "8DYGVNBPVT"),
            new("EVENT.REFLECTIONS", "REFLECTIONS.pages.INITIAL.options.SHATTER", "RZX44R9DKF"),
            new("EVENT.REFLECTIONS", "REFLECTIONS.pages.INITIAL.options.TOUCH_A_MIRROR", "RZX44R9DKF"),
            new("EVENT.ROUND_TEA_PARTY", "ROUND_TEA_PARTY.pages.INITIAL.options.ENJOY_TEA", "HHNZNPJV6W"),
            new("EVENT.ROUND_TEA_PARTY", "ROUND_TEA_PARTY.pages.INITIAL.options.PICK_FIGHT", "HHNZNPJV6W"),
            new("EVENT.ROUND_TEA_PARTY", "ROUND_TEA_PARTY.pages.PICK_FIGHT.options.CONTINUE_FIGHT", "HHNZNPJV6W", ["ROUND_TEA_PARTY.pages.INITIAL.options.PICK_FIGHT"]),
            new("EVENT.TINKER_TIME", "TINKER_TIME.pages.CHOOSE_CARD_TYPE.options.ATTACK", "7744SSMSMG", ["TINKER_TIME.pages.INITIAL.options.CHOOSE_CARD_TYPE"]),
            new("EVENT.TINKER_TIME", "TINKER_TIME.pages.CHOOSE_CARD_TYPE.options.POWER", "8CKSJT78DB", ["TINKER_TIME.pages.INITIAL.options.CHOOSE_CARD_TYPE"]),
            new("EVENT.TINKER_TIME", "TINKER_TIME.pages.CHOOSE_CARD_TYPE.options.SKILL", "8CKSJT78DB", ["TINKER_TIME.pages.INITIAL.options.CHOOSE_CARD_TYPE"]),
            new("EVENT.TINKER_TIME", "TINKER_TIME.pages.CHOOSE_RIDER.options.CHAOS", "8CKSJT78DB", ["TINKER_TIME.pages.INITIAL.options.CHOOSE_CARD_TYPE", "TINKER_TIME.pages.CHOOSE_CARD_TYPE.options.SKILL"]),
            new("EVENT.TINKER_TIME", "TINKER_TIME.pages.CHOOSE_RIDER.options.CHOKING", "7744SSMSMG", ["TINKER_TIME.pages.INITIAL.options.CHOOSE_CARD_TYPE", "TINKER_TIME.pages.CHOOSE_CARD_TYPE.options.ATTACK"]),
            new("EVENT.TINKER_TIME", "TINKER_TIME.pages.CHOOSE_RIDER.options.CURIOUS", "WANXLBJPF8", ["TINKER_TIME.pages.INITIAL.options.CHOOSE_CARD_TYPE", "TINKER_TIME.pages.CHOOSE_CARD_TYPE.options.POWER"]),
            new("EVENT.TINKER_TIME", "TINKER_TIME.pages.CHOOSE_RIDER.options.ENERGIZED", "8CKSJT78DB", ["TINKER_TIME.pages.INITIAL.options.CHOOSE_CARD_TYPE", "TINKER_TIME.pages.CHOOSE_CARD_TYPE.options.SKILL"]),
            new("EVENT.TINKER_TIME", "TINKER_TIME.pages.CHOOSE_RIDER.options.EXPERTISE", "WANXLBJPF8", ["TINKER_TIME.pages.INITIAL.options.CHOOSE_CARD_TYPE", "TINKER_TIME.pages.CHOOSE_CARD_TYPE.options.POWER"]),
            new("EVENT.TINKER_TIME", "TINKER_TIME.pages.CHOOSE_RIDER.options.IMPROVEMENT", "PT927Y4HPZ", ["TINKER_TIME.pages.INITIAL.options.CHOOSE_CARD_TYPE", "TINKER_TIME.pages.CHOOSE_CARD_TYPE.options.POWER"]),
            new("EVENT.TINKER_TIME", "TINKER_TIME.pages.CHOOSE_RIDER.options.SAPPING", "7744SSMSMG", ["TINKER_TIME.pages.INITIAL.options.CHOOSE_CARD_TYPE", "TINKER_TIME.pages.CHOOSE_CARD_TYPE.options.ATTACK"]),
            new("EVENT.TINKER_TIME", "TINKER_TIME.pages.CHOOSE_RIDER.options.VIOLENCE", "Y468CL2JJF", ["TINKER_TIME.pages.INITIAL.options.CHOOSE_CARD_TYPE", "TINKER_TIME.pages.CHOOSE_CARD_TYPE.options.ATTACK"]),
            new("EVENT.TINKER_TIME", "TINKER_TIME.pages.CHOOSE_RIDER.options.WISDOM", "YRDK9DH89Q", ["TINKER_TIME.pages.INITIAL.options.CHOOSE_CARD_TYPE", "TINKER_TIME.pages.CHOOSE_CARD_TYPE.options.SKILL"]),
            new("EVENT.TINKER_TIME", "TINKER_TIME.pages.INITIAL.options.CHOOSE_CARD_TYPE", "8CKSJT78DB"),
            new("EVENT.TRIAL", "TRIAL.pages.INITIAL.options.ACCEPT", "FPS9QRR1BM"),
            new("EVENT.TRIAL", "TRIAL.pages.INITIAL.options.REJECT", "FPS9QRR1BM"),
            new("EVENT.TRIAL", "TRIAL.pages.MERCHANT.options.GUILTY", "FPS9QRR1BM", ["TRIAL.pages.INITIAL.options.REJECT", "TRIAL.pages.REJECT.options.ACCEPT"]),
            new("EVENT.TRIAL", "TRIAL.pages.MERCHANT.options.INNOCENT", "FPS9QRR1BM", ["TRIAL.pages.INITIAL.options.REJECT", "TRIAL.pages.REJECT.options.ACCEPT"]),
            new("EVENT.TRIAL", "TRIAL.pages.NOBLE.options.GUILTY", "8LSG86CEX8", ["TRIAL.pages.INITIAL.options.REJECT", "TRIAL.pages.REJECT.options.ACCEPT"]),
            new("EVENT.TRIAL", "TRIAL.pages.NOBLE.options.INNOCENT", "8LSG86CEX8", ["TRIAL.pages.INITIAL.options.REJECT", "TRIAL.pages.REJECT.options.ACCEPT"]),
            new("EVENT.TRIAL", "TRIAL.pages.NONDESCRIPT.options.GUILTY", "5U7CT06HNS", ["TRIAL.pages.INITIAL.options.REJECT", "TRIAL.pages.REJECT.options.ACCEPT"]),
            new("EVENT.TRIAL", "TRIAL.pages.NONDESCRIPT.options.INNOCENT", "5U7CT06HNS", ["TRIAL.pages.INITIAL.options.REJECT", "TRIAL.pages.REJECT.options.ACCEPT"]),
            new("EVENT.TRIAL", "TRIAL.pages.REJECT.options.ACCEPT", "FPS9QRR1BM", ["TRIAL.pages.INITIAL.options.REJECT"]),
            new("EVENT.TRIAL", "TRIAL.pages.REJECT.options.DOUBLE_DOWN", "FPS9QRR1BM", ["TRIAL.pages.INITIAL.options.REJECT"]),
        ],
    };

    public static IEnumerable<object[]> EventRowKeys() =>
        EventRows.SelectMany(pair => pair.Value.Select(row => new object[] { pair.Key, row.Event, row.Key }));

    internal static EventRow EventRowFor(string act, string eventId, string key) =>
        EventRows[act].Single(row => row.Event == eventId && row.Key == key);

    /// <summary>
    /// Each event row on its own hunted seed and acts list: the walk's first question
    /// mark opens the row's event, the walk chooses the row's option through the
    /// recorded decision naming both and meets its ask, the recording reaches the
    /// event, the option and every seam the row retires, and a fresh replay
    /// reproduces the journal decision for decision.
    /// </summary>
    [GameTheory]
    [MemberData(nameof(EventRowKeys))]
    public void AnEventRowOpensTheEventChoosesTheOptionAndReplaysToParity(string act, string eventId, string key)
    {
        var row = EventRowFor(act, eventId, key);
        using var harness = new RecordedActWalk();
        var recorded = harness.Walk(PolicyFor(row), row.Seed, visitEveryRoomType: false, EventRowActs[act]);
        var opened = recorded.Manifest.Actions
            .Where(action => action.Verb == ActionVerb.ChooseEventOption)
            .Select(action => action.Args["event_id"])
            .FirstOrDefault(id => !DecisionSurface.ActAncients().Any(pair => pair.AncientId == id) && id != "EVENT.DARV");
        Assert.True(
            opened == row.Event,
            $"seed {row.Seed} on {string.Join(", ", EventRowActs[act])} no longer opens {row.Event} at the walk's first question " +
            $"mark (it opened {opened ?? "no event"}): the game's RNG has moved, so rerun the hunt for this row");
        Assert.True(
            recorded.AskMet,
            $"the walk opened {row.Event} and finished without choosing {row.Key}; it chose " +
            string.Join(", ", recorded.Manifest.Actions.Where(action => action.Verb == ActionVerb.ChooseEventOption).Select(action => action.Args["option_key"])));
        RecordedActWalk.AssertWhole(recorded);

        var points = DecisionFacts.Of(recorded.Manifest);
        var reached = DecisionCoverage.SeamsReachedBy(
                new CoveredRecording(
                    recorded.Manifest.RunId, points,
                    RecordingStanding.Of(recorded.Manifest.Source.Native, recorded.Manifest.Environment, ThisBuild.Value, RunmobileVersion.Current),
                    DecisionFacts.ModelsMet(recorded.Manifest)),
                DecisionSurface.ProducerMap())
            .ToHashSet();
        foreach (var point in RetiredBy(row))
        {
            Assert.True(
                points.Contains(point) || reached.Contains(point),
                $"the {row.Event} {row.Key} row's recording does not reach {point}; it reaches " +
                string.Join(", ", points.Concat(reached).Select(reachedPoint => reachedPoint.ToString())));
        }

        RecordedActWalk.ReplayToParity(recorded);
    }

    /// <summary>The points an event row retires: the event, unless the committed
    /// corpus reaches it, the option, and the seams the row names beside it.</summary>
    private static IEnumerable<DecisionPoint> RetiredBy(EventRow row)
    {
        if (!DecisionExcusals.EventsTheCorpusReaches.Contains(row.Event)) yield return new DecisionPoint(DecisionKinds.Event, row.Event);
        yield return DecisionPoint.EventOption(row.Event, row.Key);
        if (row.ClaimsReward is { } kind) yield return new DecisionPoint(DecisionKinds.RewardKind, kind);
        foreach (var point in row.AlsoRetires ?? [])
        {
            yield return Point(point);
        }
    }

    internal static WalkPolicy PolicyFor(EventRow row) => new()
    {
        EventId = row.Event,
        EventOptionKey = row.Key,
        EventOptionsOnTheWay = row.Via,
        RewardKindToClaim = row.ClaimsReward,
        RouteThrough = [.. Enumerable.Repeat(MapPointType.Monster, row.FightsFirst), MapPointType.Unknown],
    };

    /// <summary>The seed the thief row walks: a run of the Hive alone whose first
    /// fight is the Thieving Hopper's, found by <c>SeedHunt</c>'s candidates on
    /// v0.111.0 as the first whose opening deals that encounter and whose walk kills
    /// the thief before it flees; twenty-nine of ninety-seven such candidates did.</summary>
    private const string ThiefSeed = "RAGWB3H44W";

    /// <summary>The thief's encounter, the one producer of the special card a
    /// monster's death puts on the loot screen.</summary>
    private const string ThiefEncounter = "ENCOUNTER.THIEVING_HOPPER_WEAK";

    /// <summary>The points the thief row retires: the special card a thief dies
    /// holding, at the seam its power produces it at.</summary>
    private static readonly string[] ThiefRetires =
    [
        "reward-kind  special_card",
        "seam  reward-kind:special_card @ AbstractModel.BeforeDeath",
    ];

    /// <summary>
    /// A thief killed holding a stolen card gives it back as a special card on the
    /// loot screen (<c>SwipePower.BeforeDeath</c>), the one producer of that reward
    /// kind outside the Lantern Key's fight: the walk fights the Hive's Thieving
    /// Hopper first, on a seed hunted so it is the act's first fight and the line
    /// kills it before it flees, claims the card by its id, and the recording replays
    /// to parity.
    /// </summary>
    [GameFact]
    public void AThiefKilledHoldingACardGivesItBackAsASpecialCardAndReplaysToParity()
    {
        var opening = SeedHunt.ReadOpening(ThiefSeed, EventRowActs["ACT.HIVE"]);
        Assert.True(
            opening.FirstEncounterId == ThiefEncounter,
            $"seed {ThiefSeed} on the Hive alone no longer opens its first fight on {ThiefEncounter} (it opens " +
            $"{opening.FirstEncounterId}): the game's RNG has moved, so rerun the hunt for this row");

        using var harness = new RecordedActWalk();
        var recorded = harness.Walk(
            new WalkPolicy { RewardKindToClaim = RewardKinds.SpecialCard, RouteThrough = [MapPointType.Monster] },
            ThiefSeed, visitEveryRoomType: false, EventRowActs["ACT.HIVE"]);
        Assert.True(
            recorded.AskMet,
            "the walk finished without a special card to claim: the thief fled, or died holding nothing; actions: " +
            string.Join(" ", recorded.Manifest.Actions.Select(action => action.Verb)));
        RecordedActWalk.AssertWhole(recorded);

        var claim = Assert.Single(recorded.Manifest.Actions, action =>
            action.Verb == ActionVerb.ClaimReward && action.Args["reward_type"] == RewardKinds.SpecialCard);
        Assert.StartsWith("CARD.", claim.Args["card_id"], StringComparison.Ordinal);

        var points = DecisionFacts.Of(recorded.Manifest);
        var reached = DecisionCoverage.SeamsReachedBy(
                new CoveredRecording(
                    recorded.Manifest.RunId, points,
                    RecordingStanding.Of(recorded.Manifest.Source.Native, recorded.Manifest.Environment, ThisBuild.Value, RunmobileVersion.Current),
                    DecisionFacts.ModelsMet(recorded.Manifest)),
                DecisionSurface.ProducerMap())
            .ToHashSet();
        foreach (var point in ThiefRetires.Select(Point))
        {
            Assert.True(points.Contains(point) || reached.Contains(point), $"the thief row's recording does not reach {point}");
        }

        RecordedActWalk.ReplayToParity(recorded);
    }

    /// <summary>The ancient rows are exactly the options of the ancients act 2 and act
    /// 3 open on - every one a relic - and cover every relic the map says one of those
    /// ancients deals: an option a game update adds is a row somebody has to hunt a
    /// seed for, and a row for one the ancient no longer offers is a walk for nothing.
    /// Neow is act 1's and its rows are the producer table's; Darv is dealt to an act
    /// after the first and waits on a walk that survives one.</summary>
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
            .Concat(EventRows.Values.SelectMany(rows => rows).SelectMany(RetiredBy).Select(point => point.ToString()))
            .Concat(ThiefRetires)
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
    /// test: the excusal has to be the line's-survival one for that relic, so it is
    /// read for what it is and retired by a line that does survive.</summary>
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
            Assert.Equal(DecisionExcusals.BeyondTheLinesSurvival(relic), excusal);
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
        "decline the first card reward" => new WalkPolicy { CardRewardAlternative = "Skip" },
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

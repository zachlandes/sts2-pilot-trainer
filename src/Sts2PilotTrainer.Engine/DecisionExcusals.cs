using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The decision points this build offers that no recording in the committed corpus
/// exercises, each with the reason it is allowed to stay that way for now.
///
/// Code rather than a data file, in the shape of <c>EngineCommands.Unmapped</c>: a
/// build is held to it by the coverage test, and a reviewer sees an excusal appear or
/// disappear in a diff. The reason is the whole of an excusal; a point without one
/// is uncovered and fails the bar. A point a recording has since reached is a stale
/// excusal, which the coverage report names so it is taken out rather than left to
/// excuse nothing.
///
/// The release bar (<c>data/recording-completeness-architecture-audit</c>, section 5)
/// does not accept "no recording reaches it" for a point a player can reach on this
/// build. The generated walks retire that sentence for every point the fixture seed's
/// route can be pointed at; what is left is excused by what its producer is and why
/// the route does not pass it, dated so its age is visible, for the retail soak.
/// </summary>
public static class DecisionExcusals
{
    /// <summary>A point whose producer the generated walk's route does not pass, each
    /// with what the producer is, so the retail soak knows what it is looking for and a
    /// second fixture seed would know what to hunt.</summary>
    private static string NotOnTheRoute(string producer) =>
        $"no committed recording reaches it and the generated walk's route does not pass its producer - {producer}; " +
        "retired by a recording that does, from the retail soak (excused 2026-09-16)";

    private const string RelicGated =
        "the chest, the elite and the shops on the fixture seed's route deal none of them, and a relic granted " +
        "outside a recorded decision is state no replay of the recording reproduces";

    /// <summary>A point no committed recording reaches and a generated walk does: a
    /// row of <c>GeneratedCoverageTests</c> plays the decision through the real
    /// recorder, replays the recording and holds it to parity, on every merge. The
    /// recording is not committed, which is why the point is still excused here
    /// rather than counted.</summary>
    private const string Generated =
        "reached by a GeneratedCoverageTests row, recorded through the real recorder and replayed to parity on " +
        "every merge; the recording is generated rather than committed";

    /// <summary>The act's transition is reached by the won-run proof rather than by a
    /// row of its own: <c>HeadlessGameplayCaptureTests</c> plays a whole run through
    /// the real recorder to the Architect's PROCEED, asserts the recording holds the
    /// verb, and replays it, on every merge.</summary>
    private const string GeneratedByTheWonRunProof =
        "reached by HeadlessGameplayCaptureTests' won run, recorded through the real recorder and replayed on " +
        "every merge; the recording is generated rather than committed";

    public static IReadOnlyDictionary<DecisionPoint, string> All { get; } = Build();

    private static IReadOnlyDictionary<DecisionPoint, string> Build()
    {
        var excusals = new Dictionary<DecisionPoint, string>();

        // No singleplayer path on v0.111.0 constructs a linked reward set, which is why
        // the format has no kind for it; the walk names it so a build that starts
        // offering one shows up here first
        excusals[new DecisionPoint(DecisionKinds.RewardKind, "LinkedRewardSet")] =
            "a container over other rewards that no singleplayer path on this build constructs; the format " +
            "has no kind for it on purpose (RewardKinds)";

        foreach (var verb in new[]
                 {
                     "TakeCardRewardAlternative", "UsePotion", "DiscardPotion", "TakeChestRelic", "SkipChestRelic",
                 })
        {
            excusals[new DecisionPoint(DecisionKinds.Verb, verb)] = Generated;
        }

        excusals[new DecisionPoint(DecisionKinds.Verb, "ProceedToNextAct")] = GeneratedByTheWonRunProof;

        // The undo needs the window the retail client leaves open before the enemy
        // turn begins, which this process runs inside the end-turn decision
        excusals[new DecisionPoint(DecisionKinds.Verb, "UndoEndTurn")] =
            "the retail client offers the undo only in the window before the enemy turn begins, which the " +
            "headless host runs inside the end-turn decision; no generated walk reaches it (excused 2026-09-16)";

        // The three screens the headless host has no screen for
        foreach (var verb in new[] { "SelectBundleFromScreen", "SelectRelicFromScreen", "RevealCrystalSphereCell" })
        {
            excusals[new DecisionPoint(DecisionKinds.Verb, verb)] =
                "a screen the headless host has no screen for (docs/headless-fidelity.md); no generated walk " +
                "reaches it (excused 2026-09-16)";
        }

        // A range prompt on this build is the choose-a-card screen, opened by the
        // Discovery family of cards, the card-choosing potions and four relics; the
        // fixture seed's route deals none of them
        excusals[new DecisionPoint(DecisionKinds.Verb, "ConfirmCardScreen")] = NotOnTheRoute(
            "a range prompt, which on this build is the choose-a-card screen the Discovery family of cards, the " +
            "card-choosing potions and four relics open, none of which the route deals");

        excusals[new DecisionPoint(DecisionKinds.RewardKind, "relic")] = Generated;

        // A card removal is put on the loot screen by Forbidden Grimoire's power, an
        // ancient card; a special card by a thief that dies holding a stolen card or by
        // the Lantern Key event. The route's one thief fight ends without a theft
        excusals[new DecisionPoint(DecisionKinds.RewardKind, "card_removal")] = NotOnTheRoute(
            "put on the loot screen by Forbidden Grimoire's power, an ancient card the route never holds");
        excusals[new DecisionPoint(DecisionKinds.RewardKind, "special_card")] = NotOnTheRoute(
            "put on the loot screen by a thief that dies holding a stolen card or by the Lantern Key event; the " +
            "route's one thief fight ends without a theft");

        excusals[new DecisionPoint(DecisionKinds.CardRewardAlternative, "Skip")] = Generated;

        // The reroll Driftwood adds keeps the reward's selection open for an answer no
        // recording carries, and the driver refuses it by name (ResidueVerbTests)
        excusals[new DecisionPoint(DecisionKinds.CardRewardAlternative, "REROLL")] =
            "keeps the reward's selection open on this build (DoNothing), which the driver refuses by name; a " +
            "recording of it cannot replay (excused 2026-09-16)";

        // Pael's Wing adds the sacrifice, and a rest option past heal and smith is one
        // a relic or a quest card adds: Girya lifts, Pael's Growth clones, Pumpkin
        // Candle kindles, Shovel digs, Meat Cleaver cooks, Byrdonis Egg hatches. The
        // chest, the elite and the shops on the fixture seed's route deal none of
        // them, and a relic granted outside a recorded decision is state a replay of
        // the recording never reproduces, so it cannot be given to the walk's player
        excusals[new DecisionPoint(DecisionKinds.CardRewardAlternative, "SACRIFICE")] = NotOnTheRoute(
            $"added by Pael's Wing; {RelicGated}");

        foreach (var kind in new[] { "colorless_card", "relic", "potion" })
        {
            excusals[new DecisionPoint(DecisionKinds.ShopKind, kind)] = Generated;
        }

        excusals[new DecisionPoint(DecisionKinds.RestOption, "HEAL")] = Generated;
        foreach (var (option, addedBy) in new[]
                 {
                     ("CLONE", "Pael's Growth"), ("COOK", "Meat Cleaver"), ("DIG", "Shovel"),
                     ("KINDLE", "Pumpkin Candle"), ("LIFT", "Girya"),
                 })
        {
            excusals[new DecisionPoint(DecisionKinds.RestOption, option)] = NotOnTheRoute($"added by {addedBy}; {RelicGated}");
        }

        excusals[new DecisionPoint(DecisionKinds.RestOption, "HATCH")] = NotOnTheRoute(
            "added by a Byrdonis Egg in the deck, which the Byrdonis Nest event deals");

        // RestSiteOption.Generate adds the mend only to a run with more than one
        // player, and the recorder records singleplayer runs only
        excusals[new DecisionPoint(DecisionKinds.RestOption, "MEND")] =
            "offered only to a run with more than one player (RestSiteOption.Generate), which the recorder " +
            "never records";

        // Every event by id rather than "every event not reached", so an event a game
        // update adds is uncovered until somebody excuses it here. An event is reached
        // only where the map rolls it, the route refuses question marks, and a row per
        // event would need a seed hunted per event
        foreach (var eventId in new[]
                 {
                     "EVENT.ABYSSAL_BATHS",
                     "EVENT.AMALGAMATOR",
                     "EVENT.AROMA_OF_CHAOS",
                     "EVENT.BATTLEWORN_DUMMY",
                     "EVENT.BUGSLAYER",
                     "EVENT.BYRDONIS_NEST",
                     "EVENT.COLORFUL_PHILOSOPHERS",
                     "EVENT.COLOSSAL_FLOWER",
                     "EVENT.CRYSTAL_SPHERE",
                     "EVENT.DENSE_VEGETATION",
                     "EVENT.DOLL_ROOM",
                     "EVENT.DOORS_OF_LIGHT_AND_DARK",
                     "EVENT.DROWNING_BEACON",
                     "EVENT.ENDLESS_CONVEYOR",
                     "EVENT.FAKE_MERCHANT",
                     "EVENT.FIELD_OF_MAN_SIZED_HOLES",
                     "EVENT.GRAVE_OF_THE_FORGOTTEN",
                     "EVENT.HUNGRY_FOR_MUSHROOMS",
                     "EVENT.INFESTED_AUTOMATON",
                     "EVENT.JUNGLE_MAZE_ADVENTURE",
                     "EVENT.LOST_WISP",
                     "EVENT.LUMINOUS_CHOIR",
                     "EVENT.MORPHIC_GROVE",
                     "EVENT.POTION_COURIER",
                     "EVENT.PUNCH_OFF",
                     "EVENT.RANWID_THE_ELDER",
                     "EVENT.REFLECTIONS",
                     "EVENT.RELIC_TRADER",
                     "EVENT.ROOM_FULL_OF_CHEESE",
                     "EVENT.ROUND_TEA_PARTY",
                     "EVENT.SAPPHIRE_SEED",
                     "EVENT.SELF_HELP_BOOK",
                     "EVENT.SLIPPERY_BRIDGE",
                     "EVENT.SPIRALING_WHIRLPOOL",
                     "EVENT.SPIRIT_GRAFTER",
                     "EVENT.STONE_OF_ALL_TIME",
                     "EVENT.SUNKEN_STATUE",
                     "EVENT.SUNKEN_TREASURY",
                     "EVENT.SYMBIOTE",
                     "EVENT.TABLET_OF_TRUTH",
                     "EVENT.TEA_MASTER",
                     "EVENT.THE_FUTURE_OF_POTIONS",
                     "EVENT.THE_LANTERN_KEY",
                     "EVENT.THE_LEGENDS_WERE_TRUE",
                     "EVENT.THIS_OR_THAT",
                     "EVENT.TINKER_TIME",
                     "EVENT.TRASH_HEAP",
                     "EVENT.TRIAL",
                     "EVENT.UNREST_SITE",
                     "EVENT.WAR_HISTORIAN_REPY",
                     "EVENT.WELCOME_TO_WONGOS",
                     "EVENT.WELLSPRING",
                     "EVENT.WHISPERING_HOLLOW",
                     "EVENT.WOOD_CARVINGS",
                     "EVENT.ZEN_WEAVER",
                 })
        {
            excusals[new DecisionPoint(DecisionKinds.Event, eventId)] = NotOnTheRoute(
                "an event is reached only where the map rolls it, the route refuses question marks, and a row " +
                "per event would need a seed hunted per event");
        }

        // An act's ancient is one of the act's own, rolled by the run's RNG at the act's
        // start, and Darv is shared by every act; the fixture seed's route meets one per
        // act and a row per ancient would need a seed hunted per ancient. Neow is not
        // here because the walk leaves it out: its decision is ChooseNeowBlessing
        foreach (var ancientId in new[]
                 {
                     "EVENT.DARV",
                     "EVENT.NONUPEIPE",
                     "EVENT.OROBAS",
                     "EVENT.PAEL",
                     "EVENT.TANX",
                     "EVENT.TEZCATARA",
                     "EVENT.VAKUU",
                 })
        {
            excusals[new DecisionPoint(DecisionKinds.Event, ancientId)] = NotOnTheRoute(
                "an act's ancient is rolled by the run's RNG from the act's own at the act's start, so a row " +
                "per ancient would need a seed hunted per ancient");
        }

        return excusals;
    }
}

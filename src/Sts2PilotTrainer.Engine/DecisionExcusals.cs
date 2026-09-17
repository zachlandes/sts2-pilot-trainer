using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The decision points this build offers that no recording in the committed corpus
/// exercises, each with the reason it is allowed to stay that way for now.
///
/// Code rather than a data file, in the shape of <c>EngineCommands.Unmapped</c>: a
/// build is held to it by the coverage test, and a reviewer sees an excusal appear or
/// disappear in a diff. The reason is the whole of an excusal; a point without one
/// is uncovered and fails the bar. An excusal naming a point no walk on this build
/// produces is stale, which the coverage report names so it is taken out rather than
/// left to excuse nothing. One a crediting recording of the corpus under test reached
/// is not stale but named as <c>excused and reached by this corpus</c>: over the
/// committed corpus its sentence has gone false, which <c>CoverageTests</c> holds to
/// none; over a copy of a player's store it is progress.
///
/// The release bar (<c>docs/release-bar.md</c>) does not accept "no recording reaches
/// it" for a point a player can reach on this build. The generated walks retire that
/// sentence for every point the fixture seed's route can be pointed at; what is left
/// is excused by what its producer is and why the route does not pass it, dated so
/// its age is visible, for the retail soak.
///
/// Every excusal carries an <see cref="ExcusalClass"/> beside its reason, and the
/// coverage map says which classes it admits for each point
/// (<c>DecisionSurface.AdmissibleExcusals</c>): a derived class the walks contradict,
/// or a placeholder on a point nothing produces, fails <c>coverage</c> by name. The
/// class is what a build is held to; the reason is still the whole of what a person
/// needs to read.
/// </summary>
public static class DecisionExcusals
{
    /// <summary>A point whose producer the generated walk's route does not pass, each
    /// with what the producer is, so the retail soak knows what it is looking for and a
    /// second fixture seed would know what to hunt.</summary>
    private static Excusal NotOnTheRoute(string producer) => new(
        ExcusalClass.NotOnTheRoute,
        $"no committed recording reaches it and the generated walk's route does not pass its producer - {producer}; " +
        "retired by a recording that does, from the retail soak (excused 2026-09-16)");

    private const string RelicGated =
        "the chest, the elite and the shops on the fixture seed's route deal none of them, and a relic granted " +
        "outside a recorded decision is state no replay of the recording reproduces";

    private const string EventOffTheRoute =
        "an event is reached only where the map rolls it, the route refuses question marks, and a row per " +
        "event would need a seed hunted per event";

    private const string AncientOffTheRoute =
        "an act's ancient is rolled by the run's RNG from the act's own at the act's start, so a row per " +
        "ancient would need a seed hunted per ancient";

    /// <summary>An option of an event the committed corpus reaches, in a recording
    /// written before the recorder named options.</summary>
    private static readonly Excusal RecordedWithoutAKey = new(
        ExcusalClass.NotOnTheRoute,
        "the committed recordings that reach this event were written before the recorder wrote option_key, " +
        "so which option they chose is not on the file; retired by a recording that carries one (excused 2026-09-17)");

    /// <summary>A seam no committed recording reaches by co-occurrence.</summary>
    private static readonly Excusal SeamOffTheRoute = new(
        ExcusalClass.NotOnTheRoute,
        "no committed recording both met one of its producers and answered its decision, and the generated " +
        "walk's route deals none of its producers (scripts/producer-map.txt lists them and what deals each); " +
        "retired by a recording that does, from a later walk or the retail soak (excused 2026-09-17)");

    /// <summary>A point no committed recording reaches and a generated walk does: a
    /// row of <c>GeneratedCoverageTests</c> plays the decision through the real
    /// recorder, replays the recording and holds it to parity, on every merge. The
    /// recording is not committed, which is why the point is still excused here
    /// rather than counted.</summary>
    private static readonly Excusal Generated = new(
        ExcusalClass.Generated,
        "reached by a GeneratedCoverageTests row, recorded through the real recorder and replayed to parity on " +
        "every merge; the recording is generated rather than committed");

    /// <summary>The act's transition is reached by the won-run proof rather than by a
    /// row of its own: <c>HeadlessGameplayCaptureTests</c> plays a whole run through
    /// the real recorder to the Architect's PROCEED, asserts the recording holds the
    /// verb, and replays it, on every merge.</summary>
    private static readonly Excusal GeneratedByTheWonRunProof = new(
        ExcusalClass.Generated,
        "reached by HeadlessGameplayCaptureTests' won run, recorded through the real recorder and replayed on " +
        "every merge; the recording is generated rather than committed");

    /// <summary>The victory room's event and its options are reached by the win and by
    /// no act's roll: the same won-run proof plays through the Architect's lines to its
    /// PROCEED, so the class is the map's own for it rather than a placeholder.</summary>
    private static readonly Excusal ReachedByTheWin = new(
        ExcusalClass.ReachedByTheWin,
        "the victory room's own event, reached by the win rather than by an act's roll; HeadlessGameplayCaptureTests' " +
        "won run plays through its lines to PROCEED through the real recorder and replays it on every merge, and " +
        "the recording is generated rather than committed");

    /// <summary>A seam answered only on screens the headless host has none for: no
    /// generated walk can draw it, whatever produces it.</summary>
    private static readonly Excusal SeamOnAScreenWithoutHeadlessHost = new(
        ExcusalClass.ScreenWithoutHeadlessHost,
        "answered only on a screen the headless host has no screen for (docs/headless-fidelity.md), so no generated " +
        "walk reaches it, and no committed recording both met one of its producers and answered it " +
        "(scripts/producer-map.txt lists them); retired by a recording from the retail soak (excused 2026-09-17)");

    /// <summary>
    /// The options of the events the committed corpus reaches - Neow's blessing and
    /// two events - each recorded before the recorder wrote <c>option_key</c>, so
    /// which option was chosen is not on the file. By id, so an option a game update
    /// adds is uncovered until somebody reads it.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> OptionsOfEventsRecordedWithoutAKey = new Dictionary<string, string[]>
    {
        ["EVENT.BRAIN_LEECH"] =
        [
            "BRAIN_LEECH.pages.INITIAL.options.RIP",
            "BRAIN_LEECH.pages.INITIAL.options.SHARE_KNOWLEDGE",
        ],
        ["EVENT.NEOW"] =
        [
            "RELIC.ARCANE_SCROLL",
            "RELIC.BOOMING_CONCH",
            "RELIC.CURSED_PEARL",
            "RELIC.DOWSING_ROD",
            "RELIC.FISHING_ROD",
            "RELIC.GOLDEN_PEARL",
            "RELIC.HEFTY_TABLET",
            "RELIC.KALEIDOSCOPE",
            "RELIC.LARGE_CAPSULE",
            "RELIC.LAVA_ROCK",
            "RELIC.LEAD_PAPERWEIGHT",
            "RELIC.LEAFY_POULTICE",
            "RELIC.LOST_COFFER",
            "RELIC.MASSIVE_SCROLL",
            "RELIC.NEOWS_BONES",
            "RELIC.NEOWS_SACRIFICE",
            "RELIC.NEOWS_TALISMAN",
            "RELIC.NEOWS_TORMENT",
            "RELIC.NEW_LEAF",
            "RELIC.NUTRITIOUS_OYSTER",
            "RELIC.PHIAL_HOLSTER",
            "RELIC.POMANDER",
            "RELIC.PRECARIOUS_SHEARS",
            "RELIC.PRECISE_SCISSORS",
            "RELIC.SCROLL_BOXES",
            "RELIC.SILKEN_TRESS",
            "RELIC.SILVER_CRUCIBLE",
            "RELIC.SMALL_CAPSULE",
            "RELIC.STONE_HUMIDIFIER",
            "RELIC.WINGED_BOOTS",
        ],
        ["EVENT.WATERLOGGED_SCRIPTORIUM"] =
        [
            "WATERLOGGED_SCRIPTORIUM.pages.INITIAL.options.BLOODY_INK",
            "WATERLOGGED_SCRIPTORIUM.pages.INITIAL.options.PRICKLY_SPONGE",
            "WATERLOGGED_SCRIPTORIUM.pages.INITIAL.options.PRICKLY_SPONGE_LOCKED",
            "WATERLOGGED_SCRIPTORIUM.pages.INITIAL.options.TENTACLE_QUILL",
            "WATERLOGGED_SCRIPTORIUM.pages.INITIAL.options.TENTACLE_QUILL_LOCKED",
        ],
    };

    /// <summary>The options of every event and ancient the route does not pass, by id
    /// under the event, excused for the event's own reason.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> OptionsOfEventsOffTheRoute = new Dictionary<string, string[]>
    {
        ["EVENT.ABYSSAL_BATHS"] =
        [
            "ABYSSAL_BATHS.pages.ALL.options.EXIT_BATHS",
            "ABYSSAL_BATHS.pages.ALL.options.LINGER",
            "ABYSSAL_BATHS.pages.INITIAL.options.ABSTAIN",
            "ABYSSAL_BATHS.pages.INITIAL.options.IMMERSE",
        ],
        ["EVENT.AMALGAMATOR"] =
        [
            "AMALGAMATOR.pages.INITIAL.options.COMBINE_DEFENDS",
            "AMALGAMATOR.pages.INITIAL.options.COMBINE_STRIKES",
        ],
        ["EVENT.AROMA_OF_CHAOS"] =
        [
            "AROMA_OF_CHAOS.pages.INITIAL.options.LET_GO",
            "AROMA_OF_CHAOS.pages.INITIAL.options.MAINTAIN_CONTROL",
        ],
        ["EVENT.BATTLEWORN_DUMMY"] =
        [
            "BATTLEWORN_DUMMY.pages.INITIAL.options.SETTING_1",
            "BATTLEWORN_DUMMY.pages.INITIAL.options.SETTING_2",
            "BATTLEWORN_DUMMY.pages.INITIAL.options.SETTING_3",
        ],
        ["EVENT.BUGSLAYER"] =
        [
            "BUGSLAYER.pages.INITIAL.options.EXTERMINATION",
            "BUGSLAYER.pages.INITIAL.options.SQUASH",
        ],
        ["EVENT.BYRDONIS_NEST"] =
        [
            "BYRDONIS_NEST.pages.INITIAL.options.EAT",
            "BYRDONIS_NEST.pages.INITIAL.options.TAKE",
        ],
        ["EVENT.COLORFUL_PHILOSOPHERS"] =
        [
            "COLORFUL_PHILOSOPHERS.pages.INITIAL.options.DEFECT",
            "COLORFUL_PHILOSOPHERS.pages.INITIAL.options.IRONCLAD",
            "COLORFUL_PHILOSOPHERS.pages.INITIAL.options.NECROBINDER",
            "COLORFUL_PHILOSOPHERS.pages.INITIAL.options.REGENT",
            "COLORFUL_PHILOSOPHERS.pages.INITIAL.options.SILENT",
        ],
        ["EVENT.COLOSSAL_FLOWER"] =
        [
            "COLOSSAL_FLOWER.pages.INITIAL.options.EXTRACT_CURRENT_PRIZE_1",
            "COLOSSAL_FLOWER.pages.INITIAL.options.REACH_DEEPER_1",
            "COLOSSAL_FLOWER.pages.REACH_DEEPER_1.options.EXTRACT_CURRENT_PRIZE_2",
            "COLOSSAL_FLOWER.pages.REACH_DEEPER_1.options.REACH_DEEPER_2",
            "COLOSSAL_FLOWER.pages.REACH_DEEPER_2.options.EXTRACT_INSTEAD",
            "COLOSSAL_FLOWER.pages.REACH_DEEPER_2.options.POLLINOUS_CORE",
        ],
        ["EVENT.CRYSTAL_SPHERE"] =
        [
            "CRYSTAL_SPHERE.pages.INITIAL.options.PAYMENT_PLAN",
            "CRYSTAL_SPHERE.pages.INITIAL.options.UNCOVER_FUTURE",
        ],
        ["EVENT.DENSE_VEGETATION"] =
        [
            "DENSE_VEGETATION.pages.INITIAL.options.REST",
            "DENSE_VEGETATION.pages.INITIAL.options.TRUDGE_ON",
            "DENSE_VEGETATION.pages.REST.options.FIGHT",
        ],
        ["EVENT.DOLL_ROOM"] =
        [
            "DOLL_ROOM.pages.INITIAL.options.EXAMINE",
            "DOLL_ROOM.pages.INITIAL.options.RANDOM",
            "DOLL_ROOM.pages.INITIAL.options.TAKE_SOME_TIME",
        ],
        ["EVENT.DOORS_OF_LIGHT_AND_DARK"] =
        [
            "DOORS_OF_LIGHT_AND_DARK.pages.INITIAL.options.DARK",
            "DOORS_OF_LIGHT_AND_DARK.pages.INITIAL.options.LIGHT",
        ],
        ["EVENT.DROWNING_BEACON"] =
        [
            "DROWNING_BEACON.pages.INITIAL.options.BOTTLE",
            "DROWNING_BEACON.pages.INITIAL.options.CLIMB",
        ],
        ["EVENT.ENDLESS_CONVEYOR"] =
        [
            "ENDLESS_CONVEYOR.pages.ALL.options.CAVIAR",
            "ENDLESS_CONVEYOR.pages.ALL.options.CLAM_ROLL",
            "ENDLESS_CONVEYOR.pages.ALL.options.FRIED_EEL",
            "ENDLESS_CONVEYOR.pages.ALL.options.GOLDEN_FYSH",
            "ENDLESS_CONVEYOR.pages.ALL.options.JELLY_LIVER",
            "ENDLESS_CONVEYOR.pages.ALL.options.LOCKED",
            "ENDLESS_CONVEYOR.pages.ALL.options.SEAPUNK_SALAD",
            "ENDLESS_CONVEYOR.pages.ALL.options.SPICY_SNAPPY",
            "ENDLESS_CONVEYOR.pages.ALL.options.SUSPICIOUS_CONDIMENT",
            "ENDLESS_CONVEYOR.pages.GRAB_SOMETHING_OFF_THE_BELT.options.LEAVE",
            "ENDLESS_CONVEYOR.pages.INITIAL.options.OBSERVE_CHEF",
        ],
        ["EVENT.FIELD_OF_MAN_SIZED_HOLES"] =
        [
            "FIELD_OF_MAN_SIZED_HOLES.pages.INITIAL.options.ENTER_YOUR_HOLE",
            "FIELD_OF_MAN_SIZED_HOLES.pages.INITIAL.options.RESIST",
        ],
        ["EVENT.GRAVE_OF_THE_FORGOTTEN"] =
        [
            "GRAVE_OF_THE_FORGOTTEN.pages.INITIAL.options.ACCEPT",
            "GRAVE_OF_THE_FORGOTTEN.pages.INITIAL.options.CONFRONT",
            "GRAVE_OF_THE_FORGOTTEN.pages.INITIAL.options.CONFRONT_LOCKED",
        ],
        ["EVENT.HUNGRY_FOR_MUSHROOMS"] =
        [
            "RELIC.BIG_MUSHROOM",
            "RELIC.FRAGRANT_MUSHROOM",
        ],
        ["EVENT.INFESTED_AUTOMATON"] =
        [
            "INFESTED_AUTOMATON.pages.INITIAL.options.STUDY",
            "INFESTED_AUTOMATON.pages.INITIAL.options.TOUCH_CORE",
        ],
        ["EVENT.JUNGLE_MAZE_ADVENTURE"] =
        [
            "JUNGLE_MAZE_ADVENTURE.pages.INITIAL.options.JOIN_FORCES",
            "JUNGLE_MAZE_ADVENTURE.pages.INITIAL.options.SOLO_QUEST",
        ],
        ["EVENT.LOST_WISP"] =
        [
            "LOST_WISP.pages.INITIAL.options.CLAIM",
            "LOST_WISP.pages.INITIAL.options.SEARCH",
        ],
        ["EVENT.LUMINOUS_CHOIR"] =
        [
            "LUMINOUS_CHOIR.pages.INITIAL.options.OFFER_TRIBUTE",
            "LUMINOUS_CHOIR.pages.INITIAL.options.OFFER_TRIBUTE_LOCKED",
            "LUMINOUS_CHOIR.pages.INITIAL.options.REACH_INTO_THE_FLESH",
        ],
        ["EVENT.MORPHIC_GROVE"] =
        [
            "MORPHIC_GROVE.pages.INITIAL.options.GROUP",
            "MORPHIC_GROVE.pages.INITIAL.options.LONER",
        ],
        ["EVENT.POTION_COURIER"] =
        [
            "POTION_COURIER.pages.INITIAL.options.GRAB_POTIONS",
            "POTION_COURIER.pages.INITIAL.options.RANSACK",
        ],
        ["EVENT.PUNCH_OFF"] =
        [
            "PUNCH_OFF.pages.INITIAL.options.I_CAN_TAKE_THEM",
            "PUNCH_OFF.pages.INITIAL.options.NAB",
            "PUNCH_OFF.pages.I_CAN_TAKE_THEM.options.FIGHT",
        ],
        ["EVENT.RANWID_THE_ELDER"] =
        [
            "RANWID_THE_ELDER.pages.INITIAL.options.GOLD",
            "RANWID_THE_ELDER.pages.INITIAL.options.POTION",
            "RANWID_THE_ELDER.pages.INITIAL.options.POTION_LOCKED",
            "RANWID_THE_ELDER.pages.INITIAL.options.RELIC",
            "RANWID_THE_ELDER.pages.INITIAL.options.RELIC_LOCKED",
        ],
        ["EVENT.REFLECTIONS"] =
        [
            "REFLECTIONS.pages.INITIAL.options.SHATTER",
            "REFLECTIONS.pages.INITIAL.options.TOUCH_A_MIRROR",
        ],
        ["EVENT.RELIC_TRADER"] =
        [
            "PROCEED",
            "RELIC_TRADER.pages.INITIAL.options.BOTTOM",
            "RELIC_TRADER.pages.INITIAL.options.MIDDLE",
            "RELIC_TRADER.pages.INITIAL.options.TOP",
        ],
        ["EVENT.ROOM_FULL_OF_CHEESE"] =
        [
            "ROOM_FULL_OF_CHEESE.pages.INITIAL.options.GORGE",
            "ROOM_FULL_OF_CHEESE.pages.INITIAL.options.SEARCH",
        ],
        ["EVENT.ROUND_TEA_PARTY"] =
        [
            "ROUND_TEA_PARTY.pages.INITIAL.options.ENJOY_TEA",
            "ROUND_TEA_PARTY.pages.INITIAL.options.PICK_FIGHT",
            "ROUND_TEA_PARTY.pages.PICK_FIGHT.options.CONTINUE_FIGHT",
        ],
        ["EVENT.SAPPHIRE_SEED"] =
        [
            "SAPPHIRE_SEED.pages.INITIAL.options.EAT",
            "SAPPHIRE_SEED.pages.INITIAL.options.PLANT",
        ],
        ["EVENT.SELF_HELP_BOOK"] =
        [
            "SELF_HELP_BOOK.pages.INITIAL.options.NO_OPTIONS",
            "SELF_HELP_BOOK.pages.INITIAL.options.READ_ENTIRE_BOOK",
            "SELF_HELP_BOOK.pages.INITIAL.options.READ_ENTIRE_BOOK_LOCKED",
            "SELF_HELP_BOOK.pages.INITIAL.options.READ_PASSAGE",
            "SELF_HELP_BOOK.pages.INITIAL.options.READ_PASSAGE_LOCKED",
            "SELF_HELP_BOOK.pages.INITIAL.options.READ_THE_BACK",
            "SELF_HELP_BOOK.pages.INITIAL.options.READ_THE_BACK_LOCKED",
        ],
        ["EVENT.SLIPPERY_BRIDGE"] =
        [
            "SLIPPERY_BRIDGE.pages.HOLD_ON_0.options.HOLD_ON_1",
            "SLIPPERY_BRIDGE.pages.HOLD_ON_1.options.HOLD_ON_2",
            "SLIPPERY_BRIDGE.pages.HOLD_ON_2.options.HOLD_ON_3",
            "SLIPPERY_BRIDGE.pages.HOLD_ON_3.options.HOLD_ON_4",
            "SLIPPERY_BRIDGE.pages.HOLD_ON_4.options.HOLD_ON_5",
            "SLIPPERY_BRIDGE.pages.HOLD_ON_5.options.HOLD_ON_6",
            "SLIPPERY_BRIDGE.pages.HOLD_ON_6.options.HOLD_ON_LOOP",
            "SLIPPERY_BRIDGE.pages.HOLD_ON_LOOP.options.HOLD_ON_LOOP",
            "SLIPPERY_BRIDGE.pages.INITIAL.options.HOLD_ON_0",
            "SLIPPERY_BRIDGE.pages.INITIAL.options.OVERCOME",
        ],
        ["EVENT.SPIRALING_WHIRLPOOL"] =
        [
            "SPIRALING_WHIRLPOOL.pages.INITIAL.options.DRINK",
            "SPIRALING_WHIRLPOOL.pages.INITIAL.options.OBSERVE",
        ],
        ["EVENT.SPIRIT_GRAFTER"] =
        [
            "SPIRIT_GRAFTER.pages.INITIAL.options.LET_IT_IN",
            "SPIRIT_GRAFTER.pages.INITIAL.options.REJECTION",
        ],
        ["EVENT.STONE_OF_ALL_TIME"] =
        [
            "STONE_OF_ALL_TIME.pages.INITIAL.options.LIFT",
            "STONE_OF_ALL_TIME.pages.INITIAL.options.LIFT_LOCKED",
            "STONE_OF_ALL_TIME.pages.INITIAL.options.PUSH",
            "STONE_OF_ALL_TIME.pages.INITIAL.options.PUSH_LOCKED",
        ],
        ["EVENT.SUNKEN_STATUE"] =
        [
            "SUNKEN_STATUE.pages.INITIAL.options.DIVE_INTO_WATER",
            "SUNKEN_STATUE.pages.INITIAL.options.GRAB_SWORD",
        ],
        ["EVENT.SUNKEN_TREASURY"] =
        [
            "SUNKEN_TREASURY.pages.INITIAL.options.FIRST_CHEST",
            "SUNKEN_TREASURY.pages.INITIAL.options.SECOND_CHEST",
        ],
        ["EVENT.SYMBIOTE"] =
        [
            "SYMBIOTE.pages.INITIAL.options.APPROACH",
            "SYMBIOTE.pages.INITIAL.options.APPROACH_LOCKED",
            "SYMBIOTE.pages.INITIAL.options.KILL_WITH_FIRE",
        ],
        ["EVENT.TABLET_OF_TRUTH"] =
        [
            "TABLET_OF_TRUTH.pages.DECIPHER.options.GIVE_UP",
            "TABLET_OF_TRUTH.pages.DECIPHER_1.options.DECIPHER",
            "TABLET_OF_TRUTH.pages.DECIPHER_2.options.DECIPHER",
            "TABLET_OF_TRUTH.pages.DECIPHER_3.options.DECIPHER",
            "TABLET_OF_TRUTH.pages.DECIPHER_4.options.DECIPHER",
            "TABLET_OF_TRUTH.pages.INITIAL.options.DECIPHER_1",
            "TABLET_OF_TRUTH.pages.INITIAL.options.SMASH",
        ],
        ["EVENT.TEA_MASTER"] =
        [
            "TEA_MASTER.pages.INITIAL.options.BONE_TEA",
            "TEA_MASTER.pages.INITIAL.options.BONE_TEA_LOCKED",
            "TEA_MASTER.pages.INITIAL.options.EMBER_TEA",
            "TEA_MASTER.pages.INITIAL.options.EMBER_TEA_LOCKED",
            "TEA_MASTER.pages.INITIAL.options.TEA_OF_DISCOURTESY",
        ],
        ["EVENT.THE_FUTURE_OF_POTIONS"] =
        [
            "THE_FUTURE_OF_POTIONS.pages.INITIAL.options.POTION",
        ],
        ["EVENT.THE_LANTERN_KEY"] =
        [
            "THE_LANTERN_KEY.pages.INITIAL.options.KEEP_THE_KEY",
            "THE_LANTERN_KEY.pages.INITIAL.options.RETURN_THE_KEY",
            "THE_LANTERN_KEY.pages.KEEP_THE_KEY.options.FIGHT",
        ],
        ["EVENT.THE_LEGENDS_WERE_TRUE"] =
        [
            "THE_LEGENDS_WERE_TRUE.pages.INITIAL.options.NAB_THE_MAP",
            "THE_LEGENDS_WERE_TRUE.pages.INITIAL.options.SLOWLY_FIND_AN_EXIT",
        ],
        ["EVENT.THIS_OR_THAT"] =
        [
            "THIS_OR_THAT.pages.INITIAL.options.ORNATE",
            "THIS_OR_THAT.pages.INITIAL.options.PLAIN",
        ],
        ["EVENT.TINKER_TIME"] =
        [
            "TINKER_TIME.pages.CHOOSE_CARD_TYPE.options.ATTACK",
            "TINKER_TIME.pages.CHOOSE_CARD_TYPE.options.POWER",
            "TINKER_TIME.pages.CHOOSE_CARD_TYPE.options.SKILL",
            "TINKER_TIME.pages.CHOOSE_RIDER.options.CHAOS",
            "TINKER_TIME.pages.CHOOSE_RIDER.options.CHOKING",
            "TINKER_TIME.pages.CHOOSE_RIDER.options.CURIOUS",
            "TINKER_TIME.pages.CHOOSE_RIDER.options.ENERGIZED",
            "TINKER_TIME.pages.CHOOSE_RIDER.options.EXPERTISE",
            "TINKER_TIME.pages.CHOOSE_RIDER.options.IMPROVEMENT",
            "TINKER_TIME.pages.CHOOSE_RIDER.options.SAPPING",
            "TINKER_TIME.pages.CHOOSE_RIDER.options.VIOLENCE",
            "TINKER_TIME.pages.CHOOSE_RIDER.options.WISDOM",
            "TINKER_TIME.pages.INITIAL.options.CHOOSE_CARD_TYPE",
        ],
        ["EVENT.TRASH_HEAP"] =
        [
            "TRASH_HEAP.pages.INITIAL.options.DIVE_IN",
            "TRASH_HEAP.pages.INITIAL.options.GRAB",
        ],
        ["EVENT.TRIAL"] =
        [
            "TRIAL.pages.INITIAL.options.ACCEPT",
            "TRIAL.pages.INITIAL.options.REJECT",
            "TRIAL.pages.MERCHANT.options.GUILTY",
            "TRIAL.pages.MERCHANT.options.INNOCENT",
            "TRIAL.pages.NOBLE.options.GUILTY",
            "TRIAL.pages.NOBLE.options.INNOCENT",
            "TRIAL.pages.NONDESCRIPT.options.GUILTY",
            "TRIAL.pages.NONDESCRIPT.options.INNOCENT",
            "TRIAL.pages.REJECT.options.ACCEPT",
            "TRIAL.pages.REJECT.options.DOUBLE_DOWN",
        ],
        ["EVENT.UNREST_SITE"] =
        [
            "UNREST_SITE.pages.INITIAL.options.KILL",
            "UNREST_SITE.pages.INITIAL.options.REST",
        ],
        ["EVENT.WAR_HISTORIAN_REPY"] =
        [
            "WAR_HISTORIAN_REPY.pages.INITIAL.options.UNLOCK_CAGE",
            "WAR_HISTORIAN_REPY.pages.INITIAL.options.UNLOCK_CHEST",
        ],
        ["EVENT.WELCOME_TO_WONGOS"] =
        [
            "WELCOME_TO_WONGOS.pages.INITIAL.options.BARGAIN_BIN",
            "WELCOME_TO_WONGOS.pages.INITIAL.options.BARGAIN_BIN_LOCKED",
            "WELCOME_TO_WONGOS.pages.INITIAL.options.FEATURED_ITEM",
            "WELCOME_TO_WONGOS.pages.INITIAL.options.FEATURED_ITEM_LOCKED",
            "WELCOME_TO_WONGOS.pages.INITIAL.options.LEAVE",
            "WELCOME_TO_WONGOS.pages.INITIAL.options.MYSTERY_BOX",
            "WELCOME_TO_WONGOS.pages.INITIAL.options.MYSTERY_BOX_LOCKED",
        ],
        ["EVENT.WELLSPRING"] =
        [
            "WELLSPRING.pages.INITIAL.options.BATHE",
            "WELLSPRING.pages.INITIAL.options.BOTTLE",
        ],
        ["EVENT.WHISPERING_HOLLOW"] =
        [
            "WHISPERING_HOLLOW.pages.INITIAL.options.GOLD",
            "WHISPERING_HOLLOW.pages.INITIAL.options.HUG",
        ],
        ["EVENT.WOOD_CARVINGS"] =
        [
            "WOOD_CARVINGS.pages.INITIAL.options.BIRD",
            "WOOD_CARVINGS.pages.INITIAL.options.SNAKE",
            "WOOD_CARVINGS.pages.INITIAL.options.SNAKE_LOCKED",
            "WOOD_CARVINGS.pages.INITIAL.options.TORUS",
        ],
        ["EVENT.ZEN_WEAVER"] =
        [
            "ZEN_WEAVER.pages.INITIAL.options.ARACHNID_ACUPUNCTURE",
            "ZEN_WEAVER.pages.INITIAL.options.BREATHING_TECHNIQUES",
            "ZEN_WEAVER.pages.INITIAL.options.EMOTIONAL_AWARENESS",
            "ZEN_WEAVER.pages.INITIAL.options.LOCKED",
        ],
    };

    private static readonly IReadOnlyDictionary<string, string[]> OptionsOfAncientsOffTheRoute = new Dictionary<string, string[]>
    {
        ["EVENT.DARV"] =
        [
            "RELIC.ASTROLABE",
            "RELIC.BLACK_STAR",
            "RELIC.CALLING_BELL",
            "RELIC.DUSTY_TOME",
            "RELIC.ECTOPLASM",
            "RELIC.EMPTY_CAGE",
            "RELIC.PANDORAS_BOX",
            "RELIC.PHILOSOPHERS_STONE",
            "RELIC.RUNIC_PYRAMID",
            "RELIC.SNECKO_EYE",
            "RELIC.SOZU",
            "RELIC.VELVET_CHOKER",
        ],
        ["EVENT.NONUPEIPE"] =
        [
            "RELIC.BEAUTIFUL_BRACELET",
            "RELIC.BLESSED_ANTLER",
            "RELIC.BRILLIANT_SCARF",
            "RELIC.DELICATE_FROND",
            "RELIC.DIAMOND_DIADEM",
            "RELIC.FUR_COAT",
            "RELIC.GLITTER",
            "RELIC.JEWELRY_BOX",
            "RELIC.LOOMING_FRUIT",
            "RELIC.SIGNET_RING",
        ],
        ["EVENT.OROBAS"] =
        [
            "RELIC.ALCHEMICAL_COFFER",
            "RELIC.ARCHAIC_TOOTH",
            "RELIC.DRIFTWOOD",
            "RELIC.ELECTRIC_SHRYMP",
            "RELIC.GLASS_EYE",
            "RELIC.PRISMATIC_GEM",
            "RELIC.RADIANT_PEARL",
            "RELIC.SAND_CASTLE",
            "RELIC.SEA_GLASS",
            "RELIC.TOUCH_OF_OROBAS",
        ],
        ["EVENT.PAEL"] =
        [
            "RELIC.PAELS_BLOOD",
            "RELIC.PAELS_CLAW",
            "RELIC.PAELS_EYE",
            "RELIC.PAELS_FLESH",
            "RELIC.PAELS_GROWTH",
            "RELIC.PAELS_HORN",
            "RELIC.PAELS_LEGION",
            "RELIC.PAELS_TEARS",
            "RELIC.PAELS_TOOTH",
            "RELIC.PAELS_WING",
        ],
        ["EVENT.TANX"] =
        [
            "RELIC.CLAWS",
            "RELIC.CROSSBOW",
            "RELIC.IRON_CLUB",
            "RELIC.MEAT_CLEAVER",
            "RELIC.SAI",
            "RELIC.SPIKED_GAUNTLETS",
            "RELIC.TANXS_WHISTLE",
            "RELIC.THROWING_AXE",
            "RELIC.TRI_BOOMERANG",
            "RELIC.WAR_HAMMER",
        ],
        ["EVENT.TEZCATARA"] =
        [
            "RELIC.BIIIG_HUG",
            "RELIC.GOLDEN_COMPASS",
            "RELIC.NUTRITIOUS_SOUP",
            "RELIC.PUMPKIN_CANDLE",
            "RELIC.SEAL_OF_GOLD",
            "RELIC.STORYBOOK",
            "RELIC.TOASTY_MITTENS",
            "RELIC.TOY_BOX",
            "RELIC.VERY_HOT_COCOA",
            "RELIC.YUMMY_COOKIE",
        ],
        ["EVENT.VAKUU"] =
        [
            "RELIC.BLOOD_SOAKED_ROSE",
            "RELIC.CHOICES_PARADOX",
            "RELIC.DISTINGUISHED_CAPE",
            "RELIC.FIDDLE",
            "RELIC.JEWELED_MASK",
            "RELIC.LORDS_PARASOL",
            "RELIC.MUSIC_BOX",
            "RELIC.PRESERVED_FOG",
            "RELIC.SERE_TALON",
            "RELIC.WHISPERING_EARRING",
        ],
    };

    /// <summary>
    /// The seams at their timing classes no committed recording reaches by
    /// co-occurrence - met one of the seam's producers and answered its decision -
    /// by id; <c>scripts/producer-map.txt</c> lists each one's producers and what deals
    /// them. The seams the committed corpus does reach are not here, because an
    /// excusal a corpus reaches is a sentence gone false.
    /// </summary>
    private static readonly string[] SeamsOffTheRoute =
    [
        "card-prompt:CardSelectCmd.FromChooseACardScreen(context, cards, player, canSkip) @ AbstractModel.BeforeHandDraw",
        "card-prompt:CardSelectCmd.FromChooseACardScreen(context, cards, player, canSkip) @ CardModel.OnPlay",
        "card-prompt:CardSelectCmd.FromChooseACardScreen(context, cards, player, canSkip) @ MonsterModel.GenerateMoveStateMachine",
        "card-prompt:CardSelectCmd.FromChooseACardScreen(context, cards, player, canSkip) @ PotionModel.OnUse",
        "card-prompt:CardSelectCmd.FromChooseACardScreen(context, cards, player, canSkip) @ RelicModel.AfterObtained",
        "card-prompt:CardSelectCmd.FromCombatPile(context, pile, player, prefs) @ AbstractModel.AfterShuffle",
        "card-prompt:CardSelectCmd.FromCombatPile(context, pile, player, prefs) @ AbstractModel.BeforeHandDraw",
        "card-prompt:CardSelectCmd.FromCombatPile(context, pile, player, prefs) @ CardModel.OnPlay",
        "card-prompt:CardSelectCmd.FromCombatPile(context, pile, player, prefs) @ PotionModel.OnUse",
        "card-prompt:CardSelectCmd.FromCombatPile(context, pile, player, prefs, filter) @ CardModel.OnPlay",
        "card-prompt:CardSelectCmd.FromDeckForEnchantment(cards, enchantment, amount, prefs) @ EventModel.GenerateInitialOptions",
        "card-prompt:CardSelectCmd.FromDeckForEnchantment(cards, enchantment, amount, prefs) @ RelicModel.AfterObtained",
        "card-prompt:CardSelectCmd.FromDeckForEnchantment(player, enchantment, amount, additionalFilter, prefs) @ EventModel.GenerateInitialOptions",
        "card-prompt:CardSelectCmd.FromDeckForEnchantment(player, enchantment, amount, prefs) @ RelicModel.AfterObtained",
        "card-prompt:CardSelectCmd.FromDeckForRemoval(player, prefs, filter) @ AbstractModel.AfterRoomEntered",
        "card-prompt:CardSelectCmd.FromDeckForRemoval(player, prefs, filter) @ EventModel.GenerateInitialOptions",
        "card-prompt:CardSelectCmd.FromDeckForRemoval(player, prefs, filter) @ RelicModel.AfterObtained",
        "card-prompt:CardSelectCmd.FromDeckForTransformation(player, prefs, cardToTransformation) @ EventModel.CalculateVars",
        "card-prompt:CardSelectCmd.FromDeckForTransformation(player, prefs, cardToTransformation) @ EventModel.GenerateInitialOptions",
        "card-prompt:CardSelectCmd.FromDeckForTransformation(player, prefs, cardToTransformation) @ RelicModel.AfterObtained",
        "card-prompt:CardSelectCmd.FromDeckForUpgrade(player, prefs) @ EventModel.GenerateInitialOptions",
        "card-prompt:CardSelectCmd.FromDeckForUpgrade(player, prefs) @ RelicModel.AfterObtained",
        "card-prompt:CardSelectCmd.FromDeckGeneric(player, prefs, filter, sortingOrder) @ EventModel.GenerateInitialOptions",
        "card-prompt:CardSelectCmd.FromDeckGeneric(player, prefs, filter, sortingOrder) @ RelicModel.AfterObtained",
        "card-prompt:CardSelectCmd.FromHand(context, player, prefs, filter, source) @ AbstractModel.AfterPlayerTurnStart",
        "card-prompt:CardSelectCmd.FromHand(context, player, prefs, filter, source) @ PotionModel.OnUse",
        "card-prompt:CardSelectCmd.FromHandForDiscard(context, player, prefs, filter, source) @ AbstractModel.AfterPlayerTurnStart",
        "card-prompt:CardSelectCmd.FromHandForDiscard(context, player, prefs, filter, source) @ CardModel.OnPlay",
        "card-prompt:CardSelectCmd.FromHandForDiscard(context, player, prefs, filter, source) @ PotionModel.OnUse",
        "card-prompt:CardSelectCmd.FromHandForUpgrade(context, player, source) @ CardModel.OnPlay",
        "card-prompt:CardSelectCmd.FromSimpleGrid(context, cardsIn, player, prefs) @ AbstractModel.AfterPlayerTurnStart",
        "card-prompt:CardSelectCmd.FromSimpleGridForRewards(context, cards, player, prefs) @ ModifierModel.GenerateNeowOption",
        "card-prompt:CardSelectCmd.FromSimpleGridForRewards(context, cards, player, prefs) @ RelicModel.AfterObtained",
        "card-reward-alternative @ AbstractModel.TryModifyCardRewardAlternatives",
        "event-option @ AncientEventModel.AllPossibleOptions",
        "rest-option:CLONE @ AbstractModel.TryModifyRestSiteOptions",
        "rest-option:COOK @ AbstractModel.TryModifyRestSiteOptions",
        "rest-option:DIG @ AbstractModel.TryModifyRestSiteOptions",
        "rest-option:HATCH @ AbstractModel.TryModifyRestSiteOptions",
        "rest-option:KINDLE @ AbstractModel.TryModifyRestSiteOptions",
        "rest-option:LIFT @ AbstractModel.TryModifyRestSiteOptions",
        "reward-kind:card @ AbstractModel.TryModifyRestSiteHealRewards",
        "reward-kind:card @ AbstractModel.TryModifyRewards",
        "reward-kind:card @ CardModel.OnPlay",
        "reward-kind:card @ ModifierModel.GenerateNeowOption",
        "reward-kind:card @ RelicModel.AfterObtained",
        "reward-kind:card_removal @ AbstractModel.AfterCombatEnd",
        "reward-kind:gold @ AbstractModel.AfterCombatEnd",
        "reward-kind:gold @ AbstractModel.BeforeDeath",
        "reward-kind:gold @ AbstractModel.TryModifyRewards",
        "reward-kind:gold @ AbstractModel.TryModifyRewardsLate",
        "reward-kind:potion @ AbstractModel.TryModifyRestSiteHealRewards",
        "reward-kind:potion @ EventModel.CalculateVars",
        "reward-kind:potion @ EventModel.GenerateInitialOptions",
        "reward-kind:potion @ EventModel.Resume",
        "reward-kind:potion @ RelicModel.AfterObtained",
        "reward-kind:relic @ ?",
        "reward-kind:relic @ AbstractModel.TryModifyRewards",
        "reward-kind:relic @ AbstractModel.TryModifyRewardsLate",
        "reward-kind:relic @ EventModel.GenerateInitialOptions",
        "reward-kind:relic @ EventModel.Resume",
        "reward-kind:relic @ RelicModel.AfterObtained",
        "reward-kind:special_card @ AbstractModel.BeforeDeath",
        "reward-kind:special_card @ EventModel.GenerateInitialOptions",
        "rewards:OfferCustom @ EventModel.CalculateVars",
        "rewards:OfferCustom @ EventModel.Resume",
        "rewards:OfferCustom @ RelicModel.AfterObtained",
        "shop-kind:relic @ EventModel.BeforeEventStarted",
    ];

    /// <summary>The seams answered only on the screens the headless host stands in for:
    /// the bundle screen Scroll Boxes opens and the Crystal Sphere's own screen.</summary>
    private static readonly string[] SeamsOnScreensWithoutHeadlessHost =
    [
        "card-prompt:CardSelectCmd.FromChooseABundleScreen(player, bundles) @ RelicModel.AfterObtained",
        "screen:NCrystalSphereScreen.ShowScreen @ EventModel.GenerateInitialOptions",
    ];

    public static IReadOnlyDictionary<DecisionPoint, Excusal> All { get; } = Build();

    private static IReadOnlyDictionary<DecisionPoint, Excusal> Build()
    {
        var excusals = new Dictionary<DecisionPoint, Excusal>();

        // No singleplayer path on v0.111.0 constructs a linked reward set, which is why
        // the format has no kind for it; the walk names it so a build that starts
        // offering one shows up here first
        excusals[new DecisionPoint(DecisionKinds.RewardKind, "LinkedRewardSet")] = new(
            ExcusalClass.NoProducerOnThisBuild,
            "a container over other rewards that no singleplayer path on this build constructs; the format " +
            "has no kind for it on purpose (RewardKinds)");

        foreach (var verb in new[]
                 {
                     "TakeCardRewardAlternative", "UsePotion", "DiscardPotion", "TakeChestRelic", "SkipChestRelic",
                 })
        {
            excusals[new DecisionPoint(DecisionKinds.Verb, verb)] = Generated;
        }

        excusals[new DecisionPoint(DecisionKinds.Verb, "ProceedToNextAct")] = GeneratedByTheWonRunProof;

        // The client offers the undo only while another player has not ended their
        // turn, and the recorder records singleplayer runs only (RunDriver.UndoEndTurn)
        excusals[new DecisionPoint(DecisionKinds.Verb, "UndoEndTurn")] = new(
            ExcusalClass.MultiplayerOnly,
            "the client offers the undo only while another player has not ended their turn, and a singleplayer " +
            "run, the one kind the recorder records, never reaches it: with one player the engine commits to the " +
            "enemy turn the moment the turn ends (RunDriver.UndoEndTurn)");

        // The three screens the headless host has no screen for
        foreach (var verb in new[] { "SelectBundleFromScreen", "SelectRelicFromScreen", "RevealCrystalSphereCell" })
        {
            excusals[new DecisionPoint(DecisionKinds.Verb, verb)] = new(
                ExcusalClass.ScreenWithoutHeadlessHost,
                "a screen the headless host has no screen for (docs/headless-fidelity.md); no generated walk " +
                "reaches it (excused 2026-09-16)");
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
        excusals[new DecisionPoint(DecisionKinds.CardRewardAlternative, "REROLL")] = new(
            ExcusalClass.NotReplayable,
            "keeps the reward's selection open on this build (DoNothing), which the driver refuses by name; a " +
            "recording of it cannot replay (excused 2026-09-16)");

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
        excusals[new DecisionPoint(DecisionKinds.RestOption, "MEND")] = new(
            ExcusalClass.MultiplayerOnly,
            "offered only to a run with more than one player (RestSiteOption.Generate), which the recorder " +
            "never records");

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
            excusals[new DecisionPoint(DecisionKinds.Event, eventId)] = NotOnTheRoute(EventOffTheRoute);
        }

        excusals[new DecisionPoint(DecisionKinds.Event, "EVENT.THE_ARCHITECT")] = ReachedByTheWin;
        foreach (var key in new[] { "PROCEED", "THE_ARCHITECT.dialogue.0", "THE_ARCHITECT.dialogue.1" })
        {
            excusals[DecisionPoint.EventOption("EVENT.THE_ARCHITECT", key)] = ReachedByTheWin;
        }

        // The Architect alone builds an option as its initial state is set and as its
        // room is entered, for the line it speaks first; the same won run answers it
        foreach (var timing in new[] { "EventModel.OnRoomEnter", "EventModel.SetInitialEventState" })
        {
            excusals[DecisionPoint.Seam("event-option", timing)] = new(
                ExcusalClass.ReachedByTheWin,
                "produced by the victory room's event alone (scripts/producer-map.txt), so reached by the win rather " +
                "than by an act's roll; HeadlessGameplayCaptureTests' won run meets it and answers its decision through " +
                "the real recorder on every merge, and the recording is generated rather than committed");
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
            excusals[new DecisionPoint(DecisionKinds.Event, ancientId)] = NotOnTheRoute(AncientOffTheRoute);
        }


        // An option is a point of its own from format v6, when the recorder began
        // writing option_key; the committed recordings predate it, so every option of
        // an event they reach is excused for that, and every option of an event they
        // do not reach for the event's own reason
        foreach (var (eventId, keys) in OptionsOfEventsRecordedWithoutAKey)
        {
            foreach (var key in keys)
            {
                excusals[DecisionPoint.EventOption(eventId, key)] = RecordedWithoutAKey;
            }
        }

        foreach (var (eventId, keys) in OptionsOfEventsOffTheRoute)
        {
            foreach (var key in keys)
            {
                excusals[DecisionPoint.EventOption(eventId, key)] = NotOnTheRoute(
                    $"an option of {eventId}, and {EventOffTheRoute}");
            }
        }

        // The three dolls are keyed by their relic's title with no relic set, so a
        // recording carries the player's localized title where this process and the
        // driver read the title's key (DecisionSurface.TitleKeyedConstructions)
        foreach (var key in new[] { "relics.BING_BONG.title", "relics.DAUGHTER_OF_THE_WIND.title", "relics.MR_STRUGGLES.title" })
        {
            excusals[DecisionPoint.EventOption("EVENT.DOLL_ROOM", key)] = new(
                ExcusalClass.NotReplayable,
                "keyed by the doll's relic title, a LocString's raw text, which the recorder writes as the player's " +
                "localized title and RunDriver.OptionKey reads as the title's key on this build, so no recording of it " +
                "can replay; stabilizing the spelling in the recorder and the driver is a later stage (excused 2026-09-17)");
        }

        foreach (var (eventId, keys) in OptionsOfAncientsOffTheRoute)
        {
            foreach (var key in keys)
            {
                excusals[DecisionPoint.EventOption(eventId, key)] = NotOnTheRoute(
                    $"an option of {eventId}, and {AncientOffTheRoute}");
            }
        }

        // A seam is reached by co-occurrence, and what the three committed recordings
        // met and answered reaches six of the seams; the rest wait on a recording that
        // holds a producer and answers the seam
        foreach (var seam in SeamsOffTheRoute)
        {
            excusals[new DecisionPoint(DecisionKinds.Seam, seam)] = SeamOffTheRoute;
        }

        foreach (var seam in SeamsOnScreensWithoutHeadlessHost)
        {
            excusals[new DecisionPoint(DecisionKinds.Seam, seam)] = SeamOnAScreenWithoutHeadlessHost;
        }

        return excusals;
    }
}

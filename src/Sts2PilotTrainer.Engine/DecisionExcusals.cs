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
/// build; that sentence is a placeholder the generated-sequence and retail-soak
/// slices retire, dated so its age is visible.
/// </summary>
public static class DecisionExcusals
{
    private const string NotYetRecorded =
        "no committed recording reaches it; retired by generated decision sequences or the retail soak " +
        "(excused 2026-09-16)";

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
        // turn begins, which this process runs inside the end-turn decision; the
        // three screens are ones the headless host has no screen for
        foreach (var verb in new[]
                 {
                     "UndoEndTurn", "SelectBundleFromScreen", "SelectRelicFromScreen", "ConfirmCardScreen",
                     "RevealCrystalSphereCell",
                 })
        {
            excusals[new DecisionPoint(DecisionKinds.Verb, verb)] = NotYetRecorded;
        }

        foreach (var kind in new[] { "relic", "card_removal", "special_card" })
        {
            excusals[new DecisionPoint(DecisionKinds.RewardKind, kind)] = NotYetRecorded;
        }

        excusals[new DecisionPoint(DecisionKinds.CardRewardAlternative, "Skip")] = Generated;
        foreach (var alternative in new[] { "REROLL", "SACRIFICE" })
        {
            excusals[new DecisionPoint(DecisionKinds.CardRewardAlternative, alternative)] = NotYetRecorded;
        }

        foreach (var kind in new[] { "colorless_card", "relic", "potion" })
        {
            excusals[new DecisionPoint(DecisionKinds.ShopKind, kind)] = Generated;
        }

        excusals[new DecisionPoint(DecisionKinds.RestOption, "HEAL")] = Generated;
        foreach (var option in new[] { "CLONE", "COOK", "DIG", "HATCH", "KINDLE", "LIFT", "MEND" })
        {
            excusals[new DecisionPoint(DecisionKinds.RestOption, option)] = NotYetRecorded;
        }

        // Every event by id rather than "every event not reached", so an event a game
        // update adds is uncovered until somebody excuses it here
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
            excusals[new DecisionPoint(DecisionKinds.Event, eventId)] = NotYetRecorded;
        }

        return excusals;
    }
}

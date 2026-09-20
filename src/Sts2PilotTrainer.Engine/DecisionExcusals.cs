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
/// sentence for every point the fixture seed's route can be pointed at, for every
/// seam a relic Neow deals or the run's own bag deals produces, each on a seed hunted
/// so the run deals that relic, for every ancient act 2 and act 3 open on, every
/// option of each and every seam the relics they deal produce, each on a run of that
/// act alone, for every event a first act's question mark or the Hive's or Glory's
/// opens, every option of each, on a seed hunted so the first question mark opens
/// that event, and for the points a character's own cards and potions reach on a
/// whole first act, on the pinned seed each character survives one on, and for what
/// only the second act deals - Darv's options, its ancients' relics' rest options,
/// the removal Dusty Tome's card earns and the events it alone allows - on a whole
/// first act carried into the second (<c>GeneratedCoverageTests</c>); what is left is
/// excused by what its producer is and why no walk yet reaches it, dated so its age
/// is visible, for a walk into a third act and the retail soak.
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
    /// second fixture seed would know what to hunt; where the sentence names the
    /// producer, its id is carried beside it and <c>coverage</c> holds it to the map.</summary>
    private static Excusal NotOnTheRoute(string producer, params string[] producerIds) => new(
        ExcusalClass.NotOnTheRoute,
        $"no committed recording reaches it and the generated walk's route does not pass its producer - {producer}; " +
        "retired by a recording that does, from the retail soak (excused 2026-09-16)",
        producerIds);

    /// <summary>Why what only a third act deals is reached by no generated walk on this
    /// build: the second-act rows carry a whole first act into the second act's
    /// opening room, its first rest site and its first question mark, and a walk on
    /// through the second act's boss into a third is the next stage's, on a line
    /// measured past its second act.</summary>
    private const string BeyondTheSecondAct =
        "the second-act rows walk a whole first act into the second act's opening room, first rest site and first " +
        "question mark on seeds hunted on 2026-09-19, and a walk on through the second act's boss into a third act " +
        "is the next stage's - a seed hunted so the third act deals it, on a line measured past its second act - " +
        "or the retail soak's";

    /// <summary>The events no generated walk reaches, each with what stands in the way:
    /// the Grave of the Forgotten is Glory's own and the third act's, War Historian
    /// Repy is dealt only in the third act of a run that kept the Lantern Key, and the
    /// Relic Trader wants five tradable relics a whole first act never holds. By id,
    /// so an event a game update adds is uncovered until somebody excuses it here.</summary>
    private static readonly IReadOnlyDictionary<string, string> EventsOffTheRoute = new Dictionary<string, string>
    {
        ["EVENT.GRAVE_OF_THE_FORGOTTEN"] =
            "Glory's own event, the third act's on the default progression, allowed only to a deck with a card its Souls " +
            "enchantment can take (GraveOfTheForgotten.IsAllowed), which a starter deck has none of and Glory's first " +
            "rewards dealt on none of 500 seeds hunted on v0.111.0 for a run of Glory alone; " + BeyondTheSecondAct +
            " (excused 2026-09-18)",
        ["EVENT.RELIC_TRADER"] =
            "allowed only from the second act on, to a run holding five tradable relics (RelicTrader.IsAllowed over " +
            "RelicModel.IsTradable: not the starter, not one with a pickup effect, not a pet's); the second-act rows " +
            "reach the second act's first question mark holding four to six relics, the starter among them, and on " +
            "none of the 1,000 seeds hunted on 2026-09-19 did that mark open the trader; retired by a walk that " +
            "claims an elite's relic and the shop's on the way, or the retail soak (excused 2026-09-18)",
        ["EVENT.WAR_HISTORIAN_REPY"] =
            "no act's roll allows it (WarHistorianRepy.IsAllowed is false); it replaces the next event only in the third " +
            "act of the acts list (LanternKey.ModifyNextEvent returns it where CurrentActIndex is 2), of a run that kept " +
            "the Lantern Key from the Hive's own event in the second, so no run of the Hive alone and no walk that stops " +
            "in the second act reaches it; " + BeyondTheSecondAct + " (excused 2026-09-18)",
    };

    /// <summary>The options of events a row reaches that no row takes, each with what
    /// stands in the way, by id: a state no row's line holds at the mark. The empty
    /// page waits on a deck no first act builds, whatever the line survives.</summary>
    private static readonly IReadOnlyDictionary<string, string> OptionsOffTheRoute = new Dictionary<string, string>
    {
        ["EVENT.SELF_HELP_BOOK SELF_HELP_BOOK.pages.INITIAL.options.NO_OPTIONS"] =
            "constructed only where the deck holds no attack Sharp can enchant, no skill Nimble can and no power Swift " +
            "can (SelfHelpBook.GenerateInitialOptions over PlayerHasCardsAvailable, each through " +
            "EnchantmentModel.CanEnchant: a playable deck card carrying no enchantment); the Ironclad starter deck holds " +
            "six attacks and four skills, every one enchantable, the book's other three options each enchant exactly one " +
            "card of one type per visit (SelectAndEnchant's prefs select one; the 2 is the enchantment's amount), and an " +
            "act's shuffled set deals the event once, so no walk of a first act or a second reaches a deck the page is " +
            "constructed for, and no class derives an option a deck can reach; retired by the retail soak, or by a " +
            "walk whose earlier acts enchant or remove every attack and skill first (excused 2026-09-18)",
    };

    /// <summary>A rest option an act's ancient's relic adds, and the seam it is
    /// produced at: the ancient row obtains the relic at the act's first room, and a
    /// rest site is fights away that the journey's mechanical line does not survive
    /// at starter strength - none of 400 seeds hunted per relic on v0.111.0 reached
    /// one - so the row retires the relic's option and its other seams and falls
    /// short of this; <c>GeneratedCoverageTests</c> holds each such seam to the
    /// excusal for the relic that adds it, so the sentence is an owned one.</summary>
    internal static Excusal BeyondTheLinesSurvival(string relicId)
    {
        var (_, _, addedBy) = RestOptionsBeyondTheLinesSurvival.SingleOrDefault(row => row.Relic == relicId);
        if (addedBy is null)
        {
            throw new ArgumentException($"{relicId} adds no rest option the ancient rows fall short of.", nameof(relicId));
        }

        return new Excusal(
            ExcusalClass.NotOnTheRoute,
            $"no committed recording reaches it; added by {addedBy}, a relic of Glory's own ancient Tanx - the third act's " +
            "on the default progression - which the act-first ancient row obtains at the act's first room on a run of " +
            "Glory alone, where the journey's mechanical line does not survive from that ancient to a rest site at " +
            "starter strength (none of 400 hunted seeds on v0.111.0); " + BeyondTheSecondAct + " (excused 2026-09-17)",
            [relicId]);
    }

    /// <summary>A point a <c>GeneratedCoverageTests</c> ancient row reaches on a run
    /// whose acts list is act 2 or act 3 alone: a generated-only list no client run
    /// has, which the engine builds the way it builds the won-run proof's one-act run
    /// and which opens on the act's ancient. Admissible as coverage evidence on the
    /// footing of that proof, never listed or shared, and said to be here so the
    /// evidence is read for what it is.</summary>
    private static readonly Excusal GeneratedByAnActFirstRow = new(
        ExcusalClass.Generated,
        "reached by a GeneratedCoverageTests ancient row on a run of act 2 or act 3 alone - a generated-only acts " +
        "list no client run has, admissible as the won-run proof's one-act run is and never listed or shared - " +
        "recorded through the real recorder and replayed to parity on every merge; the recording is generated " +
        "rather than committed");

    /// <summary>A point a <c>GeneratedCoverageTests</c> event row reaches: a walk into
    /// the first question mark of a first act - the Overgrowth's, or the Underdocks'
    /// on the variant - on a seed hunted so that mark opens the event, the option
    /// chosen through the real recorder and replayed to parity on every merge.</summary>
    private static Excusal GeneratedByAnEventRowThrough(params string[] producers) => new(
        ExcusalClass.Generated,
        "reached by a GeneratedCoverageTests event row - the first act's first question mark, on a seed hunted so " +
        "it opens this event, the page answered by key - recorded through the real recorder and replayed to parity " +
        "on every merge; the recording is generated rather than committed",
        producers);

    private static readonly Excusal GeneratedByAnEventRow = GeneratedByAnEventRowThrough();

    /// <summary>The one producer of the hatch on this build, by id: the quest card the
    /// Byrdonis Nest's take deals, which adds the option at every rest site while it
    /// is in the deck.</summary>
    private const string ByrdonisEgg = "CARD.BYRDONIS_EGG";

    /// <summary>The same, on a run of the Hive or Glory alone: the acts list the
    /// ancient rows run on, admissible on the same footing and said to be so here.</summary>
    private static readonly Excusal GeneratedByAnActFirstEventRow = new(
        ExcusalClass.Generated,
        "reached by a GeneratedCoverageTests event row on a run of the Hive or Glory alone - a generated-only acts " +
        "list no client run has, admissible as the won-run proof's one-act run is and never listed or shared - the " +
        "act's first question mark on a seed hunted so it opens this event, the page answered by key, recorded through " +
        "the real recorder and replayed to parity on every merge; the recording is generated rather than committed");

    /// <summary>The two producers of the special card on this build, by id: the
    /// thief's power, which gives the stolen card back on the thief's death, and the
    /// Lantern Key event, whose fight puts the key on the loot screen.</summary>
    private const string ThiefsPower = "POWER.SWIPE_POWER";
    private const string LanternKeyEvent = "EVENT.THE_LANTERN_KEY";

    /// <summary>A point the special card's producers reach: the thief row, a run of
    /// the Hive alone whose first fight is the Thieving Hopper's, killed holding the
    /// card it stole, and the Lantern Key row, whose fight puts the key on the loot
    /// screen; both claimed off the screen through the real recorder and replayed to
    /// parity on every merge. Named with the producers the point is excused for, so
    /// the sentence about the thief and the key is held to the map.</summary>
    private static Excusal GeneratedByTheSpecialCardRows(params string[] producers) => new(
        ExcusalClass.Generated,
        "reached by two GeneratedCoverageTests rows on a run of the Hive alone - a generated-only acts list no " +
        "client run has, admissible as the won-run proof's one-act run is and never listed or shared - the thief " +
        "killed holding the card it stole, and the Lantern Key's fight, each special card claimed off the loot " +
        "screen, recorded through the real recorder and replayed to parity on every merge; the recording is " +
        "generated rather than committed",
        producers);

    /// <summary>The one producer of gold at a death on this build, by id: the power a
    /// Fat Gremlin spawns with holding the gold its merc stole
    /// (<c>HeistPower.BeforeDeath</c>), which gives it back on the gremlin's death
    /// if it is killed in the one turn before it flees. The merc is the Underdocks'
    /// fourth fight at the earliest - three weak fights come before any normal one -
    /// and the character rows on the Underdocks variant meet it and claim gold on a
    /// whole first act, where a heist row hunted for it on the line before the
    /// measured rule reached and won that fight on none of 25 seeds.</summary>
    private const string HeistsPower = "POWER.HEIST_POWER";

    /// <summary>The events the committed corpus reaches, whose rows retire their
    /// options and nothing else; <c>GeneratedCoverageTests</c> holds its rows to that.</summary>
    internal static readonly IReadOnlySet<string> EventsTheCorpusReaches =
        new HashSet<string>(StringComparer.Ordinal) { "EVENT.BRAIN_LEECH", "EVENT.WATERLOGGED_SCRIPTORIUM" };

    /// <summary>An option an event offers locked: constructed with no work where the
    /// player cannot afford the real one, and refused by its own button.</summary>
    private static readonly Excusal NotChoosable = new(
        ExcusalClass.NotChoosable,
        "constructed with no work - the locked form an event offers where the player cannot afford the real option - " +
        "and its button refuses the press (NEventOptionButton.OnRelease), so no play chooses it " +
        "(DecisionSurface.NotChoosableOptions)");

    /// <summary>A blessing a <c>GeneratedCoverageTests</c> blessing row takes: the
    /// opening answered by the relic's id on a seed hunted so Neow offers it, on the
    /// default progression, recorded through the real recorder and replayed to parity.
    /// The committed recordings that reach Neow were written before the recorder wrote
    /// <c>option_key</c>, so which blessing they chose is not on the file and every
    /// blessing is a generated row's or excused for its own reason.</summary>
    private static readonly Excusal GeneratedByABlessingRow = new(
        ExcusalClass.Generated,
        "reached by a GeneratedCoverageTests blessing row - the opening answered by the relic's id, on a seed hunted so " +
        "Neow offers it - recorded through the real recorder and replayed to parity on every merge; the recording is " +
        "generated rather than committed, and the committed recordings that reach Neow predate option_key");

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

    /// <summary>A point a <c>GeneratedCoverageTests</c> character row reaches: a whole
    /// first act of one character on one act-one route - the default progression or
    /// its Underdocks variant - on the seed the survival measurement pinned, played to
    /// the second act's opening room by the journey's measured rule, recorded through
    /// the real recorder and replayed to parity on every merge. Named with the
    /// producers the point is excused for where the sentence names one, so it is held
    /// to the map.</summary>
    private static Excusal GeneratedByACharacterRowThrough(params string[] producers) => new(
        ExcusalClass.Generated,
        "reached by a GeneratedCoverageTests character row - a whole first act of one character on one act-one " +
        "route, on the seed the survival measurement pinned, played to the second act's opening room - recorded " +
        "through the real recorder and replayed to parity on every merge; the recording is generated rather than " +
        "committed",
        producers);

    private static readonly Excusal GeneratedByACharacterRow = GeneratedByACharacterRowThrough();

    /// <summary>A point a <c>GeneratedCoverageTests</c> second-act ancient row reaches:
    /// a whole first act on the default progression, on a seed hunted so the second
    /// act opens on Darv, Pael or Tezcatara offering the relic, the option taken by
    /// the relic's id and what the relic adds answered in the second act - a rest
    /// option at its first rest site, a reward off its fights' loot - recorded through
    /// the real recorder and replayed to parity on every merge. Named with the
    /// producers the point is excused for where the sentence names one, so it is held
    /// to the map.</summary>
    private static Excusal GeneratedByASecondActAncientRowThrough(params string[] producers) => new(
        ExcusalClass.Generated,
        "reached by a GeneratedCoverageTests second-act ancient row - a whole first act on the default progression, on " +
        "a seed hunted so the second act opens on the ancient offering the relic, the option taken by the relic's id " +
        "and what the relic adds answered in the second act - recorded through the real recorder and replayed to " +
        "parity on every merge; the recording is generated rather than committed",
        producers);

    private static readonly Excusal GeneratedByASecondActAncientRow = GeneratedByASecondActAncientRowThrough();

    /// <summary>A point a <c>GeneratedCoverageTests</c> second-act event row reaches: a
    /// whole first act on the default progression and then the second act's first
    /// question mark, on a seed hunted so it opens the event, the page answered by
    /// key with the first act's purse and deck behind it, recorded through the real
    /// recorder and replayed to parity on every merge.</summary>
    private static readonly Excusal GeneratedByASecondActEventRow = new(
        ExcusalClass.Generated,
        "reached by a GeneratedCoverageTests second-act event row - a whole first act on the default progression and " +
        "then the second act's first question mark, on a seed hunted so it opens this event, the page answered by " +
        "key - recorded through the real recorder and replayed to parity on every merge; the recording is generated " +
        "rather than committed");

    /// <summary>The one producer of the card-removal reward a generated walk reaches,
    /// by id: the power Forbidden Grimoire applies, which puts a removal on the loot
    /// screen of every fight it was played in (<c>ForbiddenGrimoirePower.AfterCombatEnd</c>).
    /// The card is an ancient of the Necrobinder's pool and Dusty Tome deals it, so the
    /// second-act Dusty Tome row walks a Necrobinder.</summary>
    private const string GrimoiresPower = "POWER.FORBIDDEN_GRIMOIRE_POWER";

    /// <summary>The one event on this build that constructs no option, by id: the Fake
    /// Merchant, whose decisions are purchases and a potion thrown.</summary>
    private const string FakeMerchantEvent = "EVENT.FAKE_MERCHANT";

    /// <summary>An event that constructs no option, and a seam only such an event
    /// produces: the format projects an event from the option chosen in it and this one
    /// offers none, so no recording projects it and no co-occurrence read names it
    /// (<c>DecisionSurface.OptionlessEvents</c>). A <c>GeneratedCoverageTests</c> row
    /// walks a whole first act into it on a seed hunted so the second act's first
    /// question mark opens it, buys from its shelf through the recorded purchase and
    /// replays to parity on every merge; what the row cannot do is be counted here.</summary>
    private static Excusal NotProjectable(params string[] producers) => new(
        ExcusalClass.NotProjectable,
        "answered through purchases and a thrown potion, which the format projects as the shop kind and the verb, and " +
        "never through an option, because the event constructs none (DecisionSurface.OptionlessEvents); an event is " +
        "projected from the option chosen in it, so no recording projects this one and no co-occurrence read names " +
        "it; the generated coverage rows' Fake Merchant row walks a whole first act into it at the second act's " +
        "first question mark, buys from its shelf through the recorded purchase and replays to parity on every merge",
        producers);

    /// <summary>
    /// The seams a character's own cards and potions open on a first act that no
    /// Ironclad walk opens - by id, for the reason above - each reached by a
    /// <c>GeneratedCoverageTests</c> character row by co-occurrence: the Silent's
    /// discard prompts from the Silent's own cards and the card reward one of them earns, and
    /// the hand prompt a potion opens on the Defect's, the Necrobinder's and the
    /// Regent's runs.
    /// </summary>
    private static readonly string[] SeamsReachedByCharacterRows =
    [
        "card-prompt:CardSelectCmd.FromHandForDiscard(context, player, prefs, filter, source) @ CardModel.OnPlay",
        "card-prompt:CardSelectCmd.FromHandForDiscard(context, player, prefs, filter, source) @ PotionModel.OnUse",
        "reward-kind:card @ CardModel.OnPlay",
    ];

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

    /// <summary>A seam answered only on a screen the headless host has none for and
    /// no generated walk answers through the stand-in.</summary>
    private static readonly Excusal SeamOnAScreenWithoutHeadlessHost = new(
        ExcusalClass.ScreenWithoutHeadlessHost,
        "answered only on a screen the headless host has no screen for (docs/headless-fidelity.md), so no generated " +
        "walk reaches it, and no committed recording both met one of its producers and answered it " +
        "(scripts/producer-map.txt lists them); retired by a recording from the retail soak (excused 2026-09-17)");

    /// <summary>The blessings a <c>GeneratedCoverageTests</c> blessing row takes, by
    /// the relic each grants: every relic Neow deals that produces no seam, so
    /// obtaining it is the whole of what the row is after. By id, so a blessing a game
    /// update adds is uncovered until somebody hunts a seed for it; the rows hold this
    /// list and the producer rows' to every blessing the build offers.</summary>
    private static readonly string[] BlessingsTakenByBlessingRows =
    [
        "RELIC.ARCANE_SCROLL",
        "RELIC.BOOMING_CONCH",
        "RELIC.CURSED_PEARL",
        "RELIC.DOWSING_ROD",
        "RELIC.FISHING_ROD",
        "RELIC.GOLDEN_PEARL",
        "RELIC.LARGE_CAPSULE",
        "RELIC.LEAFY_POULTICE",
        "RELIC.NEOWS_SACRIFICE",
        "RELIC.NEOWS_TALISMAN",
        "RELIC.NEOWS_TORMENT",
        "RELIC.NUTRITIOUS_OYSTER",
        "RELIC.PHIAL_HOLSTER",
        "RELIC.SILKEN_TRESS",
        "RELIC.SILVER_CRUCIBLE",
        "RELIC.STONE_HUMIDIFIER",
    ];

    /// <summary>The blessings a <c>GeneratedCoverageTests</c> producer row takes, by
    /// the relic each grants: every relic Neow deals that produces a seam, each on a
    /// seed hunted so Neow offers it. Winged Boots is taken by
    /// <c>ReplayRefusalRegressionTests</c>' flight row instead and excused beside
    /// these by name.</summary>
    private static readonly string[] BlessingsTakenByGeneratedWalks =
    [
        "RELIC.HEFTY_TABLET",
        "RELIC.KALEIDOSCOPE",
        "RELIC.LAVA_ROCK",
        "RELIC.LEAD_PAPERWEIGHT",
        "RELIC.LOST_COFFER",
        "RELIC.NEOWS_BONES",
        "RELIC.NEW_LEAF",
        "RELIC.POMANDER",
        "RELIC.PRECARIOUS_SHEARS",
        "RELIC.PRECISE_SCISSORS",
        "RELIC.SCROLL_BOXES",
        "RELIC.SMALL_CAPSULE",
    ];

    /// <summary>The options of every event a first act's question mark opens that a
    /// <c>GeneratedCoverageTests</c> event row takes, by id under the event; the rows
    /// hold this list to their table.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> OptionsTakenByEventRowsInTheFirstAct = new Dictionary<string, string[]>
    {
        ["EVENT.ABYSSAL_BATHS"] =
        [
            "ABYSSAL_BATHS.pages.ALL.options.EXIT_BATHS",
            "ABYSSAL_BATHS.pages.ALL.options.LINGER",
            "ABYSSAL_BATHS.pages.INITIAL.options.ABSTAIN",
            "ABYSSAL_BATHS.pages.INITIAL.options.IMMERSE",
        ],
        ["EVENT.AROMA_OF_CHAOS"] =
        [
            "AROMA_OF_CHAOS.pages.INITIAL.options.LET_GO",
            "AROMA_OF_CHAOS.pages.INITIAL.options.MAINTAIN_CONTROL",
        ],
        ["EVENT.BRAIN_LEECH"] =
        [
            "BRAIN_LEECH.pages.INITIAL.options.RIP",
            "BRAIN_LEECH.pages.INITIAL.options.SHARE_KNOWLEDGE",
        ],
        ["EVENT.BYRDONIS_NEST"] =
        [
            "BYRDONIS_NEST.pages.INITIAL.options.EAT",
            "BYRDONIS_NEST.pages.INITIAL.options.TAKE",
        ],
        ["EVENT.DENSE_VEGETATION"] =
        [
            "DENSE_VEGETATION.pages.INITIAL.options.REST",
            "DENSE_VEGETATION.pages.INITIAL.options.TRUDGE_ON",
            "DENSE_VEGETATION.pages.REST.options.FIGHT",
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
            "ENDLESS_CONVEYOR.pages.ALL.options.SEAPUNK_SALAD",
            "ENDLESS_CONVEYOR.pages.ALL.options.SPICY_SNAPPY",
            "ENDLESS_CONVEYOR.pages.ALL.options.SUSPICIOUS_CONDIMENT",
            "ENDLESS_CONVEYOR.pages.GRAB_SOMETHING_OFF_THE_BELT.options.LEAVE",
            "ENDLESS_CONVEYOR.pages.INITIAL.options.OBSERVE_CHEF",
        ],
        ["EVENT.JUNGLE_MAZE_ADVENTURE"] =
        [
            "JUNGLE_MAZE_ADVENTURE.pages.INITIAL.options.JOIN_FORCES",
            "JUNGLE_MAZE_ADVENTURE.pages.INITIAL.options.SOLO_QUEST",
        ],
        ["EVENT.LUMINOUS_CHOIR"] =
        [
            "LUMINOUS_CHOIR.pages.INITIAL.options.OFFER_TRIBUTE",
            "LUMINOUS_CHOIR.pages.INITIAL.options.REACH_INTO_THE_FLESH",
        ],
        ["EVENT.MORPHIC_GROVE"] =
        [
            "MORPHIC_GROVE.pages.INITIAL.options.GROUP",
            "MORPHIC_GROVE.pages.INITIAL.options.LONER",
        ],
        ["EVENT.PUNCH_OFF"] =
        [
            "PUNCH_OFF.pages.INITIAL.options.I_CAN_TAKE_THEM",
            "PUNCH_OFF.pages.INITIAL.options.NAB",
            "PUNCH_OFF.pages.I_CAN_TAKE_THEM.options.FIGHT",
        ],
        ["EVENT.ROOM_FULL_OF_CHEESE"] =
        [
            "ROOM_FULL_OF_CHEESE.pages.INITIAL.options.GORGE",
            "ROOM_FULL_OF_CHEESE.pages.INITIAL.options.SEARCH",
        ],
        ["EVENT.SAPPHIRE_SEED"] =
        [
            "SAPPHIRE_SEED.pages.INITIAL.options.EAT",
            "SAPPHIRE_SEED.pages.INITIAL.options.PLANT",
        ],
        ["EVENT.SELF_HELP_BOOK"] =
        [
            "SELF_HELP_BOOK.pages.INITIAL.options.READ_ENTIRE_BOOK",
            "SELF_HELP_BOOK.pages.INITIAL.options.READ_PASSAGE",
            "SELF_HELP_BOOK.pages.INITIAL.options.READ_THE_BACK",
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
            "TEA_MASTER.pages.INITIAL.options.EMBER_TEA",
            "TEA_MASTER.pages.INITIAL.options.TEA_OF_DISCOURTESY",
        ],
        ["EVENT.THE_FUTURE_OF_POTIONS"] =
        [
            "THE_FUTURE_OF_POTIONS.pages.INITIAL.options.POTION",
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
        ["EVENT.TRASH_HEAP"] =
        [
            "TRASH_HEAP.pages.INITIAL.options.DIVE_IN",
            "TRASH_HEAP.pages.INITIAL.options.GRAB",
        ],
        ["EVENT.UNREST_SITE"] =
        [
            "UNREST_SITE.pages.INITIAL.options.KILL",
            "UNREST_SITE.pages.INITIAL.options.REST",
        ],
        ["EVENT.WATERLOGGED_SCRIPTORIUM"] =
        [
            "WATERLOGGED_SCRIPTORIUM.pages.INITIAL.options.BLOODY_INK",
            "WATERLOGGED_SCRIPTORIUM.pages.INITIAL.options.PRICKLY_SPONGE",
            "WATERLOGGED_SCRIPTORIUM.pages.INITIAL.options.TENTACLE_QUILL",
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
            "WOOD_CARVINGS.pages.INITIAL.options.TORUS",
        ],
    };

    /// <summary>The same for the events the Hive's and Glory's question marks open,
    /// each on a run of that act alone.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> OptionsTakenByEventRowsOnAnActAlone = new Dictionary<string, string[]>
    {
        ["EVENT.AMALGAMATOR"] =
        [
            "AMALGAMATOR.pages.INITIAL.options.COMBINE_DEFENDS",
            "AMALGAMATOR.pages.INITIAL.options.COMBINE_STRIKES",
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
        ["EVENT.COLORFUL_PHILOSOPHERS"] =
        [
            "COLORFUL_PHILOSOPHERS.pages.INITIAL.options.DEFECT",
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
        ["EVENT.FIELD_OF_MAN_SIZED_HOLES"] =
        [
            "FIELD_OF_MAN_SIZED_HOLES.pages.INITIAL.options.ENTER_YOUR_HOLE",
            "FIELD_OF_MAN_SIZED_HOLES.pages.INITIAL.options.RESIST",
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
        ["EVENT.LOST_WISP"] =
        [
            "LOST_WISP.pages.INITIAL.options.CLAIM",
            "LOST_WISP.pages.INITIAL.options.SEARCH",
        ],
        ["EVENT.REFLECTIONS"] =
        [
            "REFLECTIONS.pages.INITIAL.options.SHATTER",
            "REFLECTIONS.pages.INITIAL.options.TOUCH_A_MIRROR",
        ],
        ["EVENT.ROUND_TEA_PARTY"] =
        [
            "ROUND_TEA_PARTY.pages.INITIAL.options.ENJOY_TEA",
            "ROUND_TEA_PARTY.pages.INITIAL.options.PICK_FIGHT",
            "ROUND_TEA_PARTY.pages.PICK_FIGHT.options.CONTINUE_FIGHT",
        ],
        ["EVENT.SPIRIT_GRAFTER"] =
        [
            "SPIRIT_GRAFTER.pages.INITIAL.options.LET_IT_IN",
            "SPIRIT_GRAFTER.pages.INITIAL.options.REJECTION",
        ],
        ["EVENT.THE_LANTERN_KEY"] =
        [
            "THE_LANTERN_KEY.pages.INITIAL.options.KEEP_THE_KEY",
            "THE_LANTERN_KEY.pages.INITIAL.options.RETURN_THE_KEY",
            "THE_LANTERN_KEY.pages.KEEP_THE_KEY.options.FIGHT",
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
        ["EVENT.ZEN_WEAVER"] =
        [
            "ZEN_WEAVER.pages.INITIAL.options.BREATHING_TECHNIQUES",
            "ZEN_WEAVER.pages.INITIAL.options.EMOTIONAL_AWARENESS",
        ],
    };

    /// <summary>The options no play can choose, by id under the event: the locked
    /// forms, each read off the IL by <c>DecisionSurface.NotChoosableOptions</c>. By
    /// id so a locked option a game update adds is uncovered until somebody reads it.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> NotChoosableOptions = new Dictionary<string, string[]>
    {
        ["EVENT.ENDLESS_CONVEYOR"] = ["ENDLESS_CONVEYOR.pages.ALL.options.LOCKED"],
        ["EVENT.GRAVE_OF_THE_FORGOTTEN"] = ["GRAVE_OF_THE_FORGOTTEN.pages.INITIAL.options.CONFRONT_LOCKED"],
        ["EVENT.LUMINOUS_CHOIR"] = ["LUMINOUS_CHOIR.pages.INITIAL.options.OFFER_TRIBUTE_LOCKED"],
        ["EVENT.RANWID_THE_ELDER"] =
        [
            "RANWID_THE_ELDER.pages.INITIAL.options.POTION_LOCKED",
            "RANWID_THE_ELDER.pages.INITIAL.options.RELIC_LOCKED",
        ],
        ["EVENT.SELF_HELP_BOOK"] =
        [
            "SELF_HELP_BOOK.pages.INITIAL.options.READ_ENTIRE_BOOK_LOCKED",
            "SELF_HELP_BOOK.pages.INITIAL.options.READ_PASSAGE_LOCKED",
            "SELF_HELP_BOOK.pages.INITIAL.options.READ_THE_BACK_LOCKED",
        ],
        ["EVENT.STONE_OF_ALL_TIME"] =
        [
            "STONE_OF_ALL_TIME.pages.INITIAL.options.LIFT_LOCKED",
            "STONE_OF_ALL_TIME.pages.INITIAL.options.PUSH_LOCKED",
        ],
        ["EVENT.SYMBIOTE"] = ["SYMBIOTE.pages.INITIAL.options.APPROACH_LOCKED"],
        ["EVENT.TEA_MASTER"] =
        [
            "TEA_MASTER.pages.INITIAL.options.BONE_TEA_LOCKED",
            "TEA_MASTER.pages.INITIAL.options.EMBER_TEA_LOCKED",
        ],
        ["EVENT.WATERLOGGED_SCRIPTORIUM"] =
        [
            "WATERLOGGED_SCRIPTORIUM.pages.INITIAL.options.PRICKLY_SPONGE_LOCKED",
            "WATERLOGGED_SCRIPTORIUM.pages.INITIAL.options.TENTACLE_QUILL_LOCKED",
        ],
        ["EVENT.WELCOME_TO_WONGOS"] =
        [
            "WELCOME_TO_WONGOS.pages.INITIAL.options.BARGAIN_BIN_LOCKED",
            "WELCOME_TO_WONGOS.pages.INITIAL.options.FEATURED_ITEM_LOCKED",
            "WELCOME_TO_WONGOS.pages.INITIAL.options.MYSTERY_BOX_LOCKED",
        ],
        ["EVENT.WOOD_CARVINGS"] = ["WOOD_CARVINGS.pages.INITIAL.options.SNAKE_LOCKED"],
        ["EVENT.ZEN_WEAVER"] = ["ZEN_WEAVER.pages.INITIAL.options.LOCKED"],
    };

    /// <summary>The options of every event no walk reaches, by id under the event,
    /// excused for the event's own reason in <see cref="EventsOffTheRoute"/>; the
    /// locked forms are above and the Doll Room's title-keyed dolls below.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> OptionsOfEventsOffTheRoute = new Dictionary<string, string[]>
    {
        ["EVENT.GRAVE_OF_THE_FORGOTTEN"] =
        [
            "GRAVE_OF_THE_FORGOTTEN.pages.INITIAL.options.ACCEPT",
            "GRAVE_OF_THE_FORGOTTEN.pages.INITIAL.options.CONFRONT",
        ],
        ["EVENT.RELIC_TRADER"] =
        [
            "PROCEED",
            "RELIC_TRADER.pages.INITIAL.options.BOTTOM",
            "RELIC_TRADER.pages.INITIAL.options.MIDDLE",
            "RELIC_TRADER.pages.INITIAL.options.TOP",
        ],
        ["EVENT.WAR_HISTORIAN_REPY"] =
        [
            "WAR_HISTORIAN_REPY.pages.INITIAL.options.UNLOCK_CAGE",
            "WAR_HISTORIAN_REPY.pages.INITIAL.options.UNLOCK_CHEST",
        ],
    };

    /// <summary>The options the second-act event rows take, by id under the event:
    /// every option of the events the game allows only from the second act on or in
    /// the second act alone, and the two a first act's line never holds the state for
    /// at its mark - Zen Weaver's acupuncture at 250 gold, and Colorful Philosophers'
    /// Ironclad, withheld from an Ironclad run and offered to the Defect's. The Doll
    /// Room's title-keyed dolls are below.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> OptionsTakenBySecondActEventRows = new Dictionary<string, string[]>
    {
        ["EVENT.COLORFUL_PHILOSOPHERS"] = ["COLORFUL_PHILOSOPHERS.pages.INITIAL.options.IRONCLAD"],
        ["EVENT.CRYSTAL_SPHERE"] =
        [
            "CRYSTAL_SPHERE.pages.INITIAL.options.PAYMENT_PLAN",
            "CRYSTAL_SPHERE.pages.INITIAL.options.UNCOVER_FUTURE",
        ],
        ["EVENT.DOLL_ROOM"] =
        [
            "DOLL_ROOM.pages.INITIAL.options.EXAMINE",
            "DOLL_ROOM.pages.INITIAL.options.RANDOM",
            "DOLL_ROOM.pages.INITIAL.options.TAKE_SOME_TIME",
        ],
        ["EVENT.POTION_COURIER"] =
        [
            "POTION_COURIER.pages.INITIAL.options.GRAB_POTIONS",
            "POTION_COURIER.pages.INITIAL.options.RANSACK",
        ],
        ["EVENT.RANWID_THE_ELDER"] =
        [
            "RANWID_THE_ELDER.pages.INITIAL.options.GOLD",
            "RANWID_THE_ELDER.pages.INITIAL.options.POTION",
            "RANWID_THE_ELDER.pages.INITIAL.options.RELIC",
        ],
        ["EVENT.STONE_OF_ALL_TIME"] =
        [
            "STONE_OF_ALL_TIME.pages.INITIAL.options.LIFT",
            "STONE_OF_ALL_TIME.pages.INITIAL.options.PUSH",
        ],
        ["EVENT.SYMBIOTE"] =
        [
            "SYMBIOTE.pages.INITIAL.options.APPROACH",
            "SYMBIOTE.pages.INITIAL.options.KILL_WITH_FIRE",
        ],
        ["EVENT.WELCOME_TO_WONGOS"] =
        [
            "WELCOME_TO_WONGOS.pages.INITIAL.options.BARGAIN_BIN",
            "WELCOME_TO_WONGOS.pages.INITIAL.options.FEATURED_ITEM",
            "WELCOME_TO_WONGOS.pages.INITIAL.options.LEAVE",
            "WELCOME_TO_WONGOS.pages.INITIAL.options.MYSTERY_BOX",
        ],
        ["EVENT.ZEN_WEAVER"] = ["ZEN_WEAVER.pages.INITIAL.options.ARACHNID_ACUPUNCTURE"],
    };

    /// <summary>The events the second-act event rows open, whose events the character
    /// rows and the event rows do not: the ones the game allows only from the second
    /// act on or in the second act alone. Colorful Philosophers and Zen Weaver are the
    /// Hive's event rows' already.</summary>
    private static readonly string[] EventsOpenedBySecondActEventRows =
    [
        "EVENT.CRYSTAL_SPHERE",
        "EVENT.DOLL_ROOM",
        "EVENT.POTION_COURIER",
        "EVENT.RANWID_THE_ELDER",
        "EVENT.STONE_OF_ALL_TIME",
        "EVENT.SYMBIOTE",
        "EVENT.WELCOME_TO_WONGOS",
    ];

    /// <summary>The option of Darv the Ironclad's default-progression character row
    /// takes: the first its page offers on KNU8ZJM21D, where that row's second act
    /// opens on Darv.</summary>
    private const string DarvsOptionTheCharacterRowTakes = "RELIC.CALLING_BELL";

    /// <summary>The options of the one ancient no act opens on alone, by id, less the
    /// one the character row takes: Darv is dealt to an act after the first as the
    /// run is generated and rolled as that act's opening ancient, so each is reached
    /// by a second-act ancient row through a whole first act on a seed whose second
    /// act rolls Darv offering it.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> OptionsTakenBySecondActAncientRows = new Dictionary<string, string[]>
    {
        ["EVENT.DARV"] =
        [
            "RELIC.ASTROLABE",
            "RELIC.BLACK_STAR",
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
    };

    /// <summary>The options of the ancients act 2 and act 3 open on, by id under the
    /// ancient - every one a relic - each taken by a <c>GeneratedCoverageTests</c>
    /// ancient row on a run of that act alone, on a seed hunted so the ancient is rolled
    /// and offers it. By id, so an option a game update adds is uncovered until a row
    /// takes it; the rows hold this list to the game's own option pools.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> OptionsTakenByAncientRows = new Dictionary<string, string[]>
    {
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
    /// and no generated walk reaches either, by id; <c>scripts/producer-map.txt</c>
    /// lists each one's producers and what deals them. The seams the committed corpus
    /// does reach are not here, because an excusal a corpus reaches is a sentence gone
    /// false; nor are the seams <see cref="SeamsReachedByGeneratedWalks"/> lists.
    /// </summary>
    private static readonly string[] SeamsOffTheRoute =
    [
        "card-prompt:CardSelectCmd.FromChooseACardScreen(context, cards, player, canSkip) @ MonsterModel.GenerateMoveStateMachine",
        "card-prompt:CardSelectCmd.FromCombatPile(context, pile, player, prefs) @ AbstractModel.AfterShuffle",
        "card-prompt:CardSelectCmd.FromCombatPile(context, pile, player, prefs) @ AbstractModel.BeforeHandDraw",
        "card-prompt:CardSelectCmd.FromCombatPile(context, pile, player, prefs, filter) @ CardModel.OnPlay",
        "card-prompt:CardSelectCmd.FromSimpleGridForRewards(context, cards, player, prefs) @ ModifierModel.GenerateNeowOption",
        "reward-kind:card @ ModifierModel.GenerateNeowOption",
        "reward-kind:gold @ AbstractModel.AfterCombatEnd",
        "reward-kind:gold @ AbstractModel.TryModifyRewardsLate",
        "reward-kind:potion @ EventModel.Resume",
        "reward-kind:relic @ AbstractModel.TryModifyRewardsLate",
        "reward-kind:relic @ EventModel.Resume",
    ];

    /// <summary>
    /// The seams a relic Neow deals, or a relic the run's own bag deals at a chest or
    /// the merchant's shelf, produces - by id, so a seam a game update adds to one of
    /// those relics is uncovered until a row reaches it. Each is reached by a
    /// <c>GeneratedCoverageTests</c> row that walks a seed hunted so the run deals the
    /// relic, obtains it through the recorded decision that deals it, and answers the
    /// seam; the rows hold this list to the map's own producers, so a seam listed here
    /// that no row reaches fails there.
    /// </summary>
    private static readonly string[] SeamsReachedByGeneratedWalks =
    [
        "card-prompt:CardSelectCmd.FromChooseABundleScreen(player, bundles) @ RelicModel.AfterObtained",
        "card-prompt:CardSelectCmd.FromChooseACardScreen(context, cards, player, canSkip) @ AbstractModel.BeforeHandDraw",
        "card-prompt:CardSelectCmd.FromChooseACardScreen(context, cards, player, canSkip) @ RelicModel.AfterObtained",
        "card-prompt:CardSelectCmd.FromDeckForEnchantment(cards, enchantment, amount, prefs) @ RelicModel.AfterObtained",
        "card-prompt:CardSelectCmd.FromDeckForEnchantment(player, enchantment, amount, prefs) @ RelicModel.AfterObtained",
        "card-prompt:CardSelectCmd.FromDeckForRemoval(player, prefs, filter) @ RelicModel.AfterObtained",
        "card-prompt:CardSelectCmd.FromDeckForTransformation(player, prefs, cardToTransformation) @ RelicModel.AfterObtained",
        "card-prompt:CardSelectCmd.FromDeckForUpgrade(player, prefs) @ RelicModel.AfterObtained",
        "card-prompt:CardSelectCmd.FromDeckGeneric(player, prefs, filter, sortingOrder) @ RelicModel.AfterObtained",
        "card-prompt:CardSelectCmd.FromHandForDiscard(context, player, prefs, filter, source) @ AbstractModel.AfterPlayerTurnStart",
        "rest-option:DIG @ AbstractModel.TryModifyRestSiteOptions",
        "rest-option:LIFT @ AbstractModel.TryModifyRestSiteOptions",
        "reward-kind:card @ AbstractModel.TryModifyRewards",
        "reward-kind:card @ RelicModel.AfterObtained",
        "reward-kind:gold @ AbstractModel.TryModifyRewards",
        "reward-kind:potion @ AbstractModel.TryModifyRestSiteHealRewards",
        "reward-kind:potion @ RelicModel.AfterObtained",
        "reward-kind:relic @ AbstractModel.TryModifyRewards",
        "reward-kind:relic @ RelicModel.AfterObtained",
        "rewards:OfferCustom @ RelicModel.AfterObtained",
    ];

    /// <summary>
    /// The seams a relic an act's ancient deals produces, and the ancients' own option
    /// seam - by id, for the reason above - each reached by a <c>GeneratedCoverageTests</c>
    /// ancient row on a run of that act alone: Lord's Parasol's removal as the merchant
    /// is entered, Toasty Mittens' exhaust and Choices Paradox's grid at the turn's
    /// start, Sea Glass's grid on being obtained, and Pael's Wing's sacrifice on the
    /// card reward. The rest options three of those relics add are the seams the rows
    /// fall short of, below.
    /// </summary>
    private static readonly string[] SeamsReachedByAncientRows =
    [
        "card-prompt:CardSelectCmd.FromDeckForRemoval(player, prefs, filter) @ AbstractModel.AfterRoomEntered",
        "card-prompt:CardSelectCmd.FromHand(context, player, prefs, filter, source) @ AbstractModel.AfterPlayerTurnStart",
        "card-prompt:CardSelectCmd.FromSimpleGrid(context, cardsIn, player, prefs) @ AbstractModel.AfterPlayerTurnStart",
        "card-prompt:CardSelectCmd.FromSimpleGridForRewards(context, cards, player, prefs) @ RelicModel.AfterObtained",
        "card-reward-alternative @ AbstractModel.TryModifyCardRewardAlternatives",
        "event-option @ AncientEventModel.AllPossibleOptions",
    ];

    /// <summary>
    /// The seams a <c>GeneratedCoverageTests</c> event row reaches - by id, for the
    /// reason above - on a first act: the prompts the events' own pages open (a
    /// removal, an upgrade, a transformation, an enchantment, the deck grid), the
    /// potion and custom rewards their pages offer, the relic Punch Off's nab offers
    /// and the card the Dream Catcher the Trash Heap dealt adds at the next heal, and
    /// the prompts the cards and potions met on the way to the mark open in its
    /// fights, reached by co-occurrence like every seam.
    /// </summary>
    private static readonly string[] SeamsReachedByEventRows =
    [
        "card-prompt:CardSelectCmd.FromChooseACardScreen(context, cards, player, canSkip) @ CardModel.OnPlay",
        "card-prompt:CardSelectCmd.FromChooseACardScreen(context, cards, player, canSkip) @ PotionModel.OnUse",
        "card-prompt:CardSelectCmd.FromCombatPile(context, pile, player, prefs) @ CardModel.OnPlay",
        "reward-kind:card @ AbstractModel.TryModifyRestSiteHealRewards",
        "card-prompt:CardSelectCmd.FromCombatPile(context, pile, player, prefs) @ PotionModel.OnUse",
        "card-prompt:CardSelectCmd.FromDeckForEnchantment(cards, enchantment, amount, prefs) @ EventModel.GenerateInitialOptions",
        "card-prompt:CardSelectCmd.FromDeckForEnchantment(player, enchantment, amount, additionalFilter, prefs) @ EventModel.GenerateInitialOptions",
        "card-prompt:CardSelectCmd.FromDeckForRemoval(player, prefs, filter) @ EventModel.GenerateInitialOptions",
        "card-prompt:CardSelectCmd.FromDeckForTransformation(player, prefs, cardToTransformation) @ EventModel.CalculateVars",
        "card-prompt:CardSelectCmd.FromDeckForTransformation(player, prefs, cardToTransformation) @ EventModel.GenerateInitialOptions",
        "card-prompt:CardSelectCmd.FromDeckForUpgrade(player, prefs) @ EventModel.GenerateInitialOptions",
        "card-prompt:CardSelectCmd.FromDeckGeneric(player, prefs, filter, sortingOrder) @ EventModel.GenerateInitialOptions",
        "card-prompt:CardSelectCmd.FromHand(context, player, prefs, filter, source) @ PotionModel.OnUse",
        "card-prompt:CardSelectCmd.FromHandForUpgrade(context, player, source) @ CardModel.OnPlay",
        "reward-kind:potion @ EventModel.CalculateVars",
        "reward-kind:potion @ EventModel.GenerateInitialOptions",
        "reward-kind:relic @ EventModel.GenerateInitialOptions",
        "rewards:OfferCustom @ EventModel.CalculateVars",
    ];

    /// <summary>The same, on a run of the Hive or Glory alone: the custom rewards
    /// the Battleworn Dummy offers as its event resumes after its fight.</summary>
    private static readonly string[] SeamsReachedByActFirstEventRows =
    [
        "rewards:OfferCustom @ EventModel.Resume",
    ];

    /// <summary>The rest options an act's ancient's relic adds that no row reaches, by
    /// option, relic and the relic's title: the one seam class the ancient rows fall
    /// short of, for the reason <see cref="BeyondTheLinesSurvival"/> gives. Meat
    /// Cleaver is Tanx's, and Tanx is Glory's, the third act's on the default
    /// progression; Pael's Growth's clone and Pumpkin Candle's kindle are act 2's
    /// ancients' and the second-act ancient rows reach them at the second act's first
    /// rest site.</summary>
    private static readonly (string Option, string Relic, string AddedBy)[] RestOptionsBeyondTheLinesSurvival =
    [
        ("COOK", "RELIC.MEAT_CLEAVER", "Meat Cleaver"),
    ];

    /// <summary>The rest options act 2's ancients' relics add, by option and relic,
    /// each reached by a second-act ancient row at the second act's first rest site.</summary>
    private static readonly (string Option, string Relic)[] RestOptionsReachedBySecondActAncientRows =
    [
        ("CLONE", "RELIC.PAELS_GROWTH"),
        ("KINDLE", "RELIC.PUMPKIN_CANDLE"),
    ];

    /// <summary>The seams answered only on the screens the headless host stands in for
    /// and no generated walk answers: the Crystal Sphere's own screen. The bundle
    /// screen is stood in for as well, and a generated walk answers it through the
    /// stand-in, so its seam is above.</summary>
    private static readonly string[] SeamsOnScreensWithoutHeadlessHost =
    [
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

        // The bundle screen is one the headless host stands in for, and the walk that
        // takes Scroll Boxes answers it through the stand-in; the range prompt's
        // confirmation is what the choose-a-card screens Hefty Tablet and Lead
        // Paperweight open and the discard Gambling Chip asks for each turn end in
        foreach (var verb in new[]
                 {
                     "TakeCardRewardAlternative", "UsePotion", "DiscardPotion", "TakeChestRelic", "SkipChestRelic",
                     "SelectBundleFromScreen", "ConfirmCardScreen",
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

        // The two screens the headless host has no screen for and no generated walk
        // answers: nothing on this build opens the relic screen, and the Crystal
        // Sphere is an event the route does not pass
        foreach (var verb in new[] { "SelectRelicFromScreen", "RevealCrystalSphereCell" })
        {
            excusals[new DecisionPoint(DecisionKinds.Verb, verb)] = new(
                ExcusalClass.ScreenWithoutHeadlessHost,
                "a screen the headless host has no screen for (docs/headless-fidelity.md); no generated walk " +
                "reaches it (excused 2026-09-16)");
        }

        excusals[new DecisionPoint(DecisionKinds.RewardKind, "relic")] = Generated;

        // A card removal is put on the loot screen by Forbidden Grimoire's power, a
        // Necrobinder ancient card that only Dusty Tome deals, which is Darv's; a
        // special card by a thief that dies holding a stolen card or by the Lantern Key
        // event, and a row claims each
        excusals[new DecisionPoint(DecisionKinds.RewardKind, "card_removal")] = GeneratedByASecondActAncientRowThrough(GrimoiresPower);
        excusals[DecisionPoint.Seam("reward-kind:card_removal", "AbstractModel.AfterCombatEnd")] = GeneratedByASecondActAncientRowThrough(GrimoiresPower);
        excusals[new DecisionPoint(DecisionKinds.RewardKind, "special_card")] = GeneratedByTheSpecialCardRows(ThiefsPower, LanternKeyEvent);
        excusals[DecisionPoint.Seam("reward-kind:special_card", "AbstractModel.BeforeDeath")] = GeneratedByTheSpecialCardRows(ThiefsPower);
        excusals[DecisionPoint.Seam("reward-kind:special_card", "EventModel.GenerateInitialOptions")] = GeneratedByTheSpecialCardRows(LanternKeyEvent);

        excusals[new DecisionPoint(DecisionKinds.CardRewardAlternative, "Skip")] = Generated;

        // The reroll Driftwood adds keeps the reward's selection open for an answer no
        // recording carries, and the driver refuses it by name (ResidueVerbTests)
        excusals[new DecisionPoint(DecisionKinds.CardRewardAlternative, "REROLL")] = new(
            ExcusalClass.NotReplayable,
            "keeps the reward's selection open on this build (DoNothing), which the driver refuses by name; a " +
            "recording of it cannot replay (excused 2026-09-16)");

        // Pael's Wing adds the sacrifice, and a rest option past heal and smith is one
        // a relic or a quest card adds: Girya lifts, Pael's Growth clones, Pumpkin
        // Candle kindles, Shovel digs, Meat Cleaver cooks, Byrdonis Egg hatches. Girya
        // and Shovel are the bag's and a walk on a seed whose bag front holds one
        // reaches its option; Pael's Wing is an ancient's and a walk on a run of that
        // ancient's act alone reaches its sacrifice at the first fight's loot; the
        // clone and the kindle are act 2's ancients' and a second-act row reaches
        // each at the second act's first rest site, where the cook is Tanx's, in the
        // third; the egg is the Byrdonis Nest's and the event row that takes it rests
        // next; a relic granted outside a recorded decision is state a replay of the
        // recording never reproduces, so none is given to the walk's player any other way
        excusals[new DecisionPoint(DecisionKinds.CardRewardAlternative, "SACRIFICE")] = GeneratedByAnActFirstRow;

        foreach (var kind in new[] { "colorless_card", "relic", "potion" })
        {
            excusals[new DecisionPoint(DecisionKinds.ShopKind, kind)] = Generated;
        }

        foreach (var option in new[] { "HEAL", "DIG", "LIFT" })
        {
            excusals[new DecisionPoint(DecisionKinds.RestOption, option)] = Generated;
        }

        foreach (var (option, relic, _) in RestOptionsBeyondTheLinesSurvival)
        {
            excusals[new DecisionPoint(DecisionKinds.RestOption, option)] = BeyondTheLinesSurvival(relic);
            excusals[new DecisionPoint(DecisionKinds.Seam, $"rest-option:{option} @ AbstractModel.TryModifyRestSiteOptions")] =
                BeyondTheLinesSurvival(relic);
        }

        foreach (var (option, relic) in RestOptionsReachedBySecondActAncientRows)
        {
            excusals[new DecisionPoint(DecisionKinds.RestOption, option)] = GeneratedByASecondActAncientRowThrough(relic);
            excusals[new DecisionPoint(DecisionKinds.Seam, $"rest-option:{option} @ AbstractModel.TryModifyRestSiteOptions")] =
                GeneratedByASecondActAncientRowThrough(relic);
        }

        excusals[new DecisionPoint(DecisionKinds.RestOption, "HATCH")] = GeneratedByAnEventRowThrough(ByrdonisEgg);
        excusals[new DecisionPoint(DecisionKinds.Seam, "rest-option:HATCH @ AbstractModel.TryModifyRestSiteOptions")] =
            GeneratedByAnEventRowThrough(ByrdonisEgg);

        // RestSiteOption.Generate adds the mend only to a run with more than one
        // player, and the recorder records singleplayer runs only
        excusals[new DecisionPoint(DecisionKinds.RestOption, "MEND")] = new(
            ExcusalClass.MultiplayerOnly,
            "offered only to a run with more than one player (RestSiteOption.Generate), which the recorder " +
            "never records");

        // Every event by id rather than "every event not reached", so an event a game
        // update adds is uncovered until somebody excuses it here: the ones an event
        // row opens, by the acts list it opens them on, and the ones no walk reaches,
        // each by what stands in the way. Brain Leech and the Scriptorium are the
        // committed corpus's own and are not excused
        foreach (var eventId in OptionsTakenByEventRowsInTheFirstAct.Keys.Where(id => !EventsTheCorpusReaches.Contains(id)))
        {
            excusals[new DecisionPoint(DecisionKinds.Event, eventId)] = GeneratedByAnEventRow;
        }

        foreach (var eventId in OptionsTakenByEventRowsOnAnActAlone.Keys)
        {
            excusals[new DecisionPoint(DecisionKinds.Event, eventId)] = GeneratedByAnActFirstEventRow;
        }

        foreach (var eventId in EventsOpenedBySecondActEventRows)
        {
            excusals[new DecisionPoint(DecisionKinds.Event, eventId)] = GeneratedByASecondActEventRow;
        }

        foreach (var (eventId, reason) in EventsOffTheRoute)
        {
            excusals[new DecisionPoint(DecisionKinds.Event, eventId)] = NotOnTheRoute(reason);
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
        // start, and a run of that act alone opens on it: the ancient rows walk from
        // there. Darv is shared by every act and opened on by none. Neow is not here
        // because the walk leaves it out: its decision is ChooseNeowBlessing
        foreach (var ancientId in OptionsTakenByAncientRows.Keys)
        {
            excusals[new DecisionPoint(DecisionKinds.Event, ancientId)] = GeneratedByAnActFirstRow;
        }

        excusals[new DecisionPoint(DecisionKinds.Event, "EVENT.DARV")] = GeneratedByACharacterRow;
        excusals[DecisionPoint.EventOption("EVENT.DARV", DarvsOptionTheCharacterRowTakes)] = GeneratedByACharacterRow;


        // An option is a point of its own from format v6, when the recorder began
        // writing option_key; the committed recordings predate it, so every blessing
        // is a generated row's - a producer row's where the relic produces a seam, a
        // blessing row's where it does not - or excused for its own reason below
        foreach (var relic in BlessingsTakenByBlessingRows)
        {
            excusals[DecisionPoint.EventOption(DecisionFacts.NeowEventId, relic)] = GeneratedByABlessingRow;
        }

        foreach (var relic in BlessingsTakenByGeneratedWalks)
        {
            excusals[DecisionPoint.EventOption(DecisionFacts.NeowEventId, relic)] = Generated;
        }

        // Massive Scroll's own rule admits it only to a run with another player in it
        // (DecisionSurface.OfferedOnlyWithAnotherPlayer), so no singleplayer run is
        // ever offered it and no row can take it
        excusals[DecisionPoint.EventOption(DecisionFacts.NeowEventId, "RELIC.MASSIVE_SCROLL")] = new(
            ExcusalClass.MultiplayerOnly,
            "Neow offers it only to a run with more than one player (MassiveScroll.IsAllowed), which the " +
            "recorder never records");

        excusals[DecisionPoint.EventOption(DecisionFacts.NeowEventId, "RELIC.WINGED_BOOTS")] = new(
            ExcusalClass.Generated,
            "reached by ReplayRefusalRegressionTests' flight row, which takes the blessing by name, recorded " +
            "through the real recorder and replayed to parity on every merge; the recording is generated rather " +
            "than committed");

        foreach (var (eventId, keys) in OptionsTakenByEventRowsInTheFirstAct)
        {
            foreach (var key in keys)
            {
                excusals[DecisionPoint.EventOption(eventId, key)] = GeneratedByAnEventRow;
            }
        }

        foreach (var (eventId, keys) in OptionsTakenByEventRowsOnAnActAlone)
        {
            foreach (var key in keys)
            {
                excusals[DecisionPoint.EventOption(eventId, key)] = GeneratedByAnActFirstEventRow;
            }
        }

        foreach (var (eventId, keys) in NotChoosableOptions)
        {
            foreach (var key in keys)
            {
                excusals[DecisionPoint.EventOption(eventId, key)] = NotChoosable;
            }
        }

        foreach (var (eventId, keys) in OptionsOfEventsOffTheRoute)
        {
            foreach (var key in keys)
            {
                excusals[DecisionPoint.EventOption(eventId, key)] = NotOnTheRoute($"an option of {eventId}, {EventsOffTheRoute[eventId]}");
            }
        }

        foreach (var (identity, reason) in OptionsOffTheRoute)
        {
            var space = identity.IndexOf(' ', StringComparison.Ordinal);
            excusals[DecisionPoint.EventOption(identity[..space], identity[(space + 1)..])] = NotOnTheRoute(reason);
        }

        foreach (var (eventId, keys) in OptionsTakenBySecondActEventRows)
        {
            foreach (var key in keys)
            {
                excusals[DecisionPoint.EventOption(eventId, key)] = GeneratedByASecondActEventRow;
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

        foreach (var (eventId, keys) in OptionsTakenBySecondActAncientRows)
        {
            foreach (var key in keys)
            {
                excusals[DecisionPoint.EventOption(eventId, key)] = GeneratedByASecondActAncientRow;
            }
        }

        // The Fake Merchant's shelf, and the rug and relics its fight's loot offers,
        // which the map lists at no hook of the event's own
        excusals[new DecisionPoint(DecisionKinds.Event, FakeMerchantEvent)] = NotProjectable();
        excusals[DecisionPoint.Seam("shop-kind:relic", "EventModel.BeforeEventStarted")] = NotProjectable(FakeMerchantEvent);
        excusals[DecisionPoint.Seam("reward-kind:relic", "?")] = NotProjectable(FakeMerchantEvent);

        foreach (var (ancientId, keys) in OptionsTakenByAncientRows)
        {
            foreach (var key in keys)
            {
                excusals[DecisionPoint.EventOption(ancientId, key)] = GeneratedByAnActFirstRow;
            }
        }

        // A seam is reached by co-occurrence, and what the three committed recordings
        // met and answered reaches six of the seams; the rest wait on a recording that
        // holds a producer and answers the seam
        foreach (var seam in SeamsOffTheRoute)
        {
            excusals[new DecisionPoint(DecisionKinds.Seam, seam)] = SeamOffTheRoute;
        }

        excusals[new DecisionPoint(DecisionKinds.Seam, "reward-kind:gold @ AbstractModel.BeforeDeath")] =
            GeneratedByACharacterRowThrough(HeistsPower);

        foreach (var seam in SeamsOnScreensWithoutHeadlessHost)
        {
            excusals[new DecisionPoint(DecisionKinds.Seam, seam)] = SeamOnAScreenWithoutHeadlessHost;
        }

        foreach (var seam in SeamsReachedByGeneratedWalks)
        {
            excusals[new DecisionPoint(DecisionKinds.Seam, seam)] = Generated;
        }

        foreach (var seam in SeamsReachedByAncientRows)
        {
            excusals[new DecisionPoint(DecisionKinds.Seam, seam)] = GeneratedByAnActFirstRow;
        }

        foreach (var seam in SeamsReachedByEventRows)
        {
            excusals[new DecisionPoint(DecisionKinds.Seam, seam)] = GeneratedByAnEventRow;
        }

        foreach (var seam in SeamsReachedByActFirstEventRows)
        {
            excusals[new DecisionPoint(DecisionKinds.Seam, seam)] = GeneratedByAnActFirstEventRow;
        }

        foreach (var seam in SeamsReachedByCharacterRows)
        {
            excusals[new DecisionPoint(DecisionKinds.Seam, seam)] = GeneratedByACharacterRow;
        }

        return excusals;
    }
}

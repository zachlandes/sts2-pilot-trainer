using System.Reflection;
using System.Runtime.CompilerServices;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The denominator of the coverage number, walked off this build and held to what
/// v0.111.0 offers.
///
/// Each walk is pinned to the count and the names the decompiled assembly shows, so
/// a walk that read half a body, or a mapping that lost a class, fails here by name
/// rather than as a coverage figure that quietly shrank. The committed record is the
/// same denominator with its excusals, held the way the choice-entry-point and
/// save-point records are held, so a game update that adds a rest option, a reward
/// kind or an event shows as a diff in the change that adopts it.
/// </summary>
public sealed class DecisionSurfaceTests
{
    /// <summary>Every verb the format names less the one the table excuses: the
    /// table's own answer, so the walk cannot disagree with <c>engine-commands</c>.</summary>
    [GameFact]
    public void TheVerbsAreEveryMappedVerbInTheFormatsOrder()
    {
        var expected = Enum.GetValues<ActionVerb>().Where(EngineCommands.Maps).Select(verb => verb.ToString()).ToList();

        Assert.Equal(expected, DecisionSurface.Verbs());
        Assert.Equal(22, expected.Count);
        Assert.DoesNotContain(nameof(ActionVerb.SelectHandCards), DecisionSurface.Verbs());
    }

    /// <summary>Six kinds the format names and one it does not: the linked set no
    /// singleplayer path constructs, named by its type so it cannot hide.</summary>
    [GameFact]
    public void TheRewardKindsAreTheSixTheFormatNamesAndTheOneItDoesNot()
    {
        Assert.Equal(
            ["card_removal", "card", "gold", "LinkedRewardSet", "potion", "relic", "special_card"],
            DecisionSurface.RewardKinds());
    }

    /// <summary>
    /// Skip and REROLL from <c>CardRewardAlternative.Generate</c> and SACRIFICE from
    /// Pael's Wing, and nothing else: the exception message that method also loads is
    /// not an alternative, and neither is any literal a relic loads for another reason.
    /// This is the card-reward Skip defect as a denominator - a point a recording has
    /// to reach or excuse, whichever answer the alternative's own AfterSelected gives.
    /// </summary>
    [GameFact]
    public void TheCardRewardAlternativesAreSkipRerollAndSacrifice()
    {
        Assert.Equal(["Skip", "REROLL", "SACRIFICE"], DecisionSurface.CardRewardAlternatives());
    }

    /// <summary>Four entry classes on five shelves: a card entry sells off either card
    /// shelf, and the format names both.</summary>
    [GameFact]
    public void TheShopKindsAreTheFiveTheFormatNames()
    {
        Assert.Equal(ShopPurchaseKinds.All.Order(StringComparer.Ordinal), DecisionSurface.ShopKinds().Order(StringComparer.Ordinal));
    }

    [GameFact]
    public void TheRestOptionsAreTheNineThisBuildDeclares()
    {
        Assert.Equal(
            ["CLONE", "COOK", "DIG", "HATCH", "HEAL", "KINDLE", "LIFT", "MEND", "SMITH"],
            DecisionSurface.RestOptions());
    }

    /// <summary>The 57 events and the seven ancients an act can roll, and the
    /// Architect, the victory room's event no act lists; Neow is the one ancient left
    /// out, because its decision is <c>ChooseNeowBlessing</c> and carries no event id.</summary>
    [GameFact]
    public void TheEventsAreEveryEventAndAncientTheModelDatabaseShips()
    {
        var events = DecisionSurface.Events();

        Assert.Equal(65, events.Count);
        Assert.All(events, id => Assert.StartsWith("EVENT.", id, StringComparison.Ordinal));
        Assert.Contains("EVENT.WATERLOGGED_SCRIPTORIUM", events);
        Assert.Contains("EVENT.OROBAS", events);
        Assert.Contains(DecisionSurface.ArchitectEventId, events);
        Assert.Equal("EVENT.THE_ARCHITECT", DecisionSurface.ArchitectEventId);
        Assert.DoesNotContain("EVENT.NEOW", events);
        Assert.Equal(events.Order(StringComparer.Ordinal), events);
    }

    [GameFact]
    public void TheCardPromptsAreTheChoiceEntryPointsByQualifiedSignature()
    {
        Assert.Equal(ChoiceEntryPoints.All().Select(ChoiceEntryPoints.QualifiedSignature), DecisionSurface.CardPrompts());
        Assert.Equal(17, DecisionSurface.CardPrompts().Count);
    }

    /// <summary>Eleven net actions, each becoming exactly one game action, read off
    /// its own <c>ToGameAction</c>; the game's twelfth game action is the hook's and
    /// no net action becomes it.</summary>
    [GameFact]
    public void TheNetActionsAreTheElevenGameActionsALocalNetActionBecomes()
    {
        Assert.Equal(
            [
                "ConsoleCmdGameAction", "DiscardPotionGameAction", "EndPlayerTurnAction", "MoveToMapCoordAction",
                "PickRelicAction", "PlayCardAction", "ReadyToBeginEnemyTurnAction", "UndoEndPlayerTurnAction",
                "UsePotionAction", "VoteForMapCoordAction", "VoteToMoveToNextActAction",
            ],
            DecisionSurface.NetActions());
    }

    [GameFact]
    public void EveryPointOfEveryKindIsInTheKindsOrder()
    {
        var all = DecisionSurface.All();

        Assert.Equal(522, all.Count);
        Assert.Equal(
            DecisionKinds.All.SelectMany(kind => DecisionSurface.Identities(kind).Select(identity => new DecisionPoint(kind, identity))),
            all);
    }

    /// <summary>Every excusal names a point this build offers; one that does not is a
    /// sentence about nothing, and the coverage report calls it stale.</summary>
    [GameFact]
    public void EveryExcusalNamesAPointThisBuildOffers()
    {
        var offered = DecisionSurface.All().ToHashSet();

        Assert.All(DecisionExcusals.All.Keys, point => Assert.Contains(point, offered));
        Assert.All(DecisionExcusals.All.Values, excusal => Assert.False(string.IsNullOrWhiteSpace(excusal.Reason)));
    }

    /// <summary>
    /// The committed denominator is what the walks produce on this build. Regenerated
    /// by the command that computes the number rather than by an environment variable,
    /// because the record is the command's own output.
    /// </summary>
    [GameFact]
    public void TheCommittedDenominatorIsWhatTheWalksProduce()
    {
        var path = Path.Combine(Arbiter.RepoRoot, DecisionSurface.RecordPath);
        var actual = DecisionSurface.Record(DecisionSurface.All());
        var recorded = File.Exists(path) ? File.ReadAllText(path) : null;

        Assert.True(
            recorded == actual,
            "The decision points this build offers, or their excusals, are not the recorded ones. If the game " +
            "build changed or an excusal moved, regenerate the record in the same change:\n\n" +
            "    ./scripts/arbiter coverage --corpus manifests --update\n\n" +
            $"Recorded in {path}:\n{recorded ?? "(no file)"}\nThis build:\n{actual}");
    }

    /// <summary>
    /// Every event's options: an ancient's from the game's own <c>AllPossibleOptions</c>
    /// keyed the way the driver keys one, every other event's off its own IL, and the
    /// events that build keys at runtime derived the way their code derives them - the
    /// dig-numbered, hold-numbered and decipher-numbered pages, the dolls keyed by their
    /// relic's title, the bare PROCEED the trader and the Architect offer, and the
    /// Architect's lines - with none of the literal halves an interpolated key leaves on
    /// the IL. Neow is here under its own id, because its blessing carries an option
    /// key, and every other option's event is one <see cref="DecisionSurface.Events"/> lists.
    /// </summary>
    [GameFact]
    public void TheEventOptionsAreEveryOptionOfEveryEventAndOfNeow()
    {
        var options = DecisionSurface.EventOptions();
        var events = DecisionSurface.Events().Append(DecisionFacts.NeowEventId).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(305, options.Count);
        Assert.Equal(options.Order(StringComparer.Ordinal), options);
        Assert.All(options, option => Assert.Contains(option[..option.IndexOf(' ', StringComparison.Ordinal)], events));
        Assert.Contains("EVENT.NEOW RELIC.WINGED_BOOTS", options);
        Assert.Contains("EVENT.BRAIN_LEECH BRAIN_LEECH.pages.INITIAL.options.RIP", options);
        Assert.Contains("EVENT.PAEL RELIC.PAELS_WING", options);
        Assert.Contains("EVENT.DARV RELIC.ASTROLABE", options);
        Assert.Contains("EVENT.HUNGRY_FOR_MUSHROOMS RELIC.BIG_MUSHROOM", options);
        Assert.Contains("EVENT.AMALGAMATOR AMALGAMATOR.pages.INITIAL.options.COMBINE_STRIKES", options);
        Assert.Contains("EVENT.COLORFUL_PHILOSOPHERS COLORFUL_PHILOSOPHERS.pages.INITIAL.options.IRONCLAD", options);
        Assert.Contains("EVENT.ENDLESS_CONVEYOR ENDLESS_CONVEYOR.pages.ALL.options.CAVIAR", options);
        Assert.Contains("EVENT.COLOSSAL_FLOWER COLOSSAL_FLOWER.pages.INITIAL.options.EXTRACT_CURRENT_PRIZE_1", options);
        Assert.Contains("EVENT.COLOSSAL_FLOWER COLOSSAL_FLOWER.pages.REACH_DEEPER_1.options.REACH_DEEPER_2", options);
        Assert.Contains("EVENT.SLIPPERY_BRIDGE SLIPPERY_BRIDGE.pages.HOLD_ON_0.options.HOLD_ON_1", options);
        Assert.Contains("EVENT.SLIPPERY_BRIDGE SLIPPERY_BRIDGE.pages.HOLD_ON_LOOP.options.HOLD_ON_LOOP", options);
        Assert.Contains("EVENT.TABLET_OF_TRUTH TABLET_OF_TRUTH.pages.DECIPHER_4.options.DECIPHER", options);
        Assert.Contains("EVENT.DOLL_ROOM relics.MR_STRUGGLES.title", options);
        Assert.Contains("EVENT.RELIC_TRADER PROCEED", options);
        Assert.Contains("EVENT.THE_ARCHITECT PROCEED", options);
        Assert.Contains("EVENT.THE_ARCHITECT THE_ARCHITECT.dialogue.1", options);
        Assert.Contains("EVENT.TINKER_TIME TINKER_TIME.pages.CHOOSE_RIDER.options.SAPPING", options);
        Assert.DoesNotContain(options, option => option.EndsWith('_'));
        Assert.Equal(30, options.Count(option => option.StartsWith("EVENT.NEOW ", StringComparison.Ordinal)));
    }

    /// <summary>
    /// The three derivations that used to write this build's numbers down are read
    /// off the IL: the flower's dig bound is the constant <c>ReachDeeper</c> compares
    /// <c>NumberOfDigs</c> with and branches past the page on, the tablet's finish is
    /// the one <c>Decipher</c> compares <c>DecipherCount</c> with and branches past
    /// the finish on, and the trader's <c>PROCEED</c> is the literal its construction
    /// loads, which needs no derivation at all. Every option those events and the
    /// Architect build by interpolation is read as a template, and every key their
    /// derivations list fits one, which is what the walk refuses on when a build
    /// moves a bound or renames a page.
    /// </summary>
    [GameFact]
    public void TheBuiltOptionKeysAreReadAsTemplatesAndTheirBoundsOffTheIl()
    {
        Assert.Equal(2, DecisionSurface.CounterBound("EVENT.COLOSSAL_FLOWER", "NumberOfDigs", "Bge"));
        Assert.Equal(5, DecisionSurface.CounterBound("EVENT.TABLET_OF_TRUTH", "DecipherCount", "Bne_Un"));
        var noSuchShape = Assert.Throws<InvalidOperationException>(
            () => DecisionSurface.CounterBound("EVENT.COLOSSAL_FLOWER", "NumberOfDigs", "Beq"));
        Assert.Contains("compares NumberOfDigs with Beq against no constant", noSuchShape.Message, StringComparison.Ordinal);

        Assert.Equal(
            [
                "COLOSSAL_FLOWER.pages.INITIAL.options.EXTRACT_CURRENT_PRIZE_{}",
                "COLOSSAL_FLOWER.pages.INITIAL.options.REACH_DEEPER_{}",
                "COLOSSAL_FLOWER.pages.REACH_DEEPER_{}.options.EXTRACT_CURRENT_PRIZE_{}",
                "COLOSSAL_FLOWER.pages.REACH_DEEPER_{}.options.REACH_DEEPER_{}",
            ],
            DecisionSurface.BuiltOptionKeyTemplates("EVENT.COLOSSAL_FLOWER"));
        Assert.Equal(
            ["TABLET_OF_TRUTH.pages.DECIPHER_{}.options.DECIPHER"],
            DecisionSurface.BuiltOptionKeyTemplates("EVENT.TABLET_OF_TRUTH"));
        Assert.Equal(["{}.dialogue.{}"], DecisionSurface.BuiltOptionKeyTemplates("EVENT.THE_ARCHITECT"));
        Assert.Empty(DecisionSurface.BuiltOptionKeyTemplates("EVENT.RELIC_TRADER"));
        Assert.Empty(DecisionSurface.BuiltOptionKeyTemplates("EVENT.PAEL"));

        var options = DecisionSurface.EventOptionKeys();
        foreach (var eventId in new[] { "EVENT.COLOSSAL_FLOWER", "EVENT.TABLET_OF_TRUTH", "EVENT.THE_ARCHITECT" })
        {
            var keys = options.Where(option => option.EventId == eventId).Select(option => option.Key).ToList();
            Assert.All(
                DecisionSurface.BuiltOptionKeyTemplates(eventId),
                template => Assert.Contains(keys, key => DecisionSurface.Fits(template, key)));
        }

        Assert.True(DecisionSurface.Fits("{}.dialogue.{}", "THE_ARCHITECT.dialogue.0"));
        Assert.False(DecisionSurface.Fits("{}.dialogue.{}", "THE_ARCHITECT.dialogue.0.x"));
        Assert.False(DecisionSurface.Fits("TABLET_OF_TRUTH.pages.DECIPHER_{}.options.DECIPHER", "TABLET_OF_TRUTH.pages.DECIPHER.options.GIVE_UP"));

        Assert.Equal(
            [
                "COLOSSAL_FLOWER.pages.INITIAL.options.EXTRACT_CURRENT_PRIZE_1",
                "COLOSSAL_FLOWER.pages.INITIAL.options.REACH_DEEPER_1",
                "COLOSSAL_FLOWER.pages.REACH_DEEPER_1.options.EXTRACT_CURRENT_PRIZE_2",
                "COLOSSAL_FLOWER.pages.REACH_DEEPER_1.options.REACH_DEEPER_2",
                "COLOSSAL_FLOWER.pages.REACH_DEEPER_2.options.EXTRACT_INSTEAD",
                "COLOSSAL_FLOWER.pages.REACH_DEEPER_2.options.POLLINOUS_CORE",
            ],
            options.Where(option => option.EventId == "EVENT.COLOSSAL_FLOWER").Select(option => option.Key));
        Assert.Equal(
            [
                "TABLET_OF_TRUTH.pages.DECIPHER.options.GIVE_UP",
                "TABLET_OF_TRUTH.pages.DECIPHER_1.options.DECIPHER",
                "TABLET_OF_TRUTH.pages.DECIPHER_2.options.DECIPHER",
                "TABLET_OF_TRUTH.pages.DECIPHER_3.options.DECIPHER",
                "TABLET_OF_TRUTH.pages.DECIPHER_4.options.DECIPHER",
                "TABLET_OF_TRUTH.pages.INITIAL.options.DECIPHER_1",
                "TABLET_OF_TRUTH.pages.INITIAL.options.SMASH",
            ],
            options.Where(option => option.EventId == "EVENT.TABLET_OF_TRUTH").Select(option => option.Key));
        Assert.Equal(
            [
                "PROCEED",
                "RELIC_TRADER.pages.INITIAL.options.BOTTOM",
                "RELIC_TRADER.pages.INITIAL.options.MIDDLE",
                "RELIC_TRADER.pages.INITIAL.options.TOP",
            ],
            options.Where(option => option.EventId == "EVENT.RELIC_TRADER").Select(option => option.Key));
    }

    /// <summary>
    /// The reading behind that, on the game's own bodies: a construction keyed by a
    /// literal carries it as its key literal, with a params default or a hover-tip
    /// call between the two leaving it in place; one keyed by an interpolation the
    /// body finishes right before it carries the template; and a counter compared
    /// with a constant is read as the constant and the branch that compares it, so
    /// the reader itself is held to this build's shapes rather than to a fixture.
    /// </summary>
    [GameFact]
    public void AConstructionsKeyAndACountersBoundAreReadOffTheGamesOwnBodies()
    {
        var trader = ChoiceEntryPoints.ConstructionsIn(GameMethod("RelicTrader", "GenerateInitialOptions"), EventOptionType());
        Assert.Equal(4, trader.Count);
        Assert.Equal("RELIC_TRADER.pages.INITIAL.options.TOP", trader[0].KeyLiteral);
        Assert.Equal("PROCEED", trader[3].KeyLiteral);
        Assert.All(trader, construction => Assert.Null(construction.Template));

        var flower = ChoiceEntryPoints.ConstructionsIn(GameMethod("ColossalFlower", "GenerateInitialOptions"), EventOptionType());
        Assert.Equal(
            ["COLOSSAL_FLOWER.pages.INITIAL.options.EXTRACT_CURRENT_PRIZE_{}", "COLOSSAL_FLOWER.pages.INITIAL.options.REACH_DEEPER_{}"],
            flower.Select(construction => construction.Template));
        Assert.All(flower, construction => Assert.Null(construction.KeyLiteral));

        var reachDeeper = StateMachineOf(GameMethod("ColossalFlower", "ReachDeeper"));
        var deeper = ChoiceEntryPoints.ConstructionsIn(reachDeeper, EventOptionType());
        Assert.Equal(
            [
                "COLOSSAL_FLOWER.pages.REACH_DEEPER_{}.options.EXTRACT_CURRENT_PRIZE_{}",
                "COLOSSAL_FLOWER.pages.REACH_DEEPER_{}.options.REACH_DEEPER_{}",
                null,
                null,
            ],
            deeper.Select(construction => construction.Template));
        Assert.Equal(
            [null, null, "COLOSSAL_FLOWER.pages.REACH_DEEPER_2.options.EXTRACT_INSTEAD", "COLOSSAL_FLOWER.pages.REACH_DEEPER_2.options.POLLINOUS_CORE"],
            deeper.Select(construction => construction.KeyLiteral));
        Assert.Equal(
            [(2, "Bge")],
            ChoiceEntryPoints.ConstantsComparedWith(reachDeeper, GameMethod("ColossalFlower", "get_NumberOfDigs")));
        Assert.Equal(
            [(5, "Bne_Un")],
            ChoiceEntryPoints.ConstantsComparedWith(
                StateMachineOf(GameMethod("TabletOfTruth", "Decipher")), GameMethod("TabletOfTruth", "get_DecipherCount")));
    }

    /// <summary>
    /// A construction two bare key literals reach with no call between - the ternary
    /// <c>done ? "PROCEED" : "DECLINE"</c>, which no event on this build writes - is
    /// refused naming both, rather than read as keyed by the last one loaded and
    /// listed as one option where the body offers two.
    /// </summary>
    [GameFact]
    public void AConstructionTwoBareKeysCouldBeTheKeyOfIsRefusedByName()
    {
        var ternary = typeof(DecisionSurfaceTests).GetMethod(nameof(KeyedByATernary), BindingFlags.NonPublic | BindingFlags.Static)!;

        var refusal = Assert.Throws<InvalidOperationException>(() => ChoiceEntryPoints.ConstructionsIn(ternary, typeof(KeyedOption)));

        Assert.Contains(nameof(KeyedByATernary), refusal.Message);
        Assert.Contains("'PROCEED'", refusal.Message);
        Assert.Contains("'DECLINE'", refusal.Message);
        Assert.Equal(
            "PROCEED",
            Assert.Single(ChoiceEntryPoints.ConstructionsIn(
                typeof(DecisionSurfaceTests).GetMethod(nameof(KeyedByOneLiteral), BindingFlags.NonPublic | BindingFlags.Static)!,
                typeof(KeyedOption))).KeyLiteral);
    }

    /// <summary>
    /// A construction keyed by a bare literal or a string read from a field -
    /// <c>flag ? "PROCEED" : storedKey</c> - is refused naming the field, rather than
    /// read as keyed by the literal with the field-keyed option dropped; one keyed by
    /// a local the literal was stored in is still that literal.
    /// </summary>
    [GameFact]
    public void AConstructionKeyedByALiteralOrAStringFromAFieldIsRefusedByName()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() =>
            ChoiceEntryPoints.ConstructionsIn(TestMethod(nameof(KeyedByALiteralOrAField)), typeof(KeyedOption)));

        Assert.Contains(nameof(KeyedByALiteralOrAField), refusal.Message);
        Assert.Contains("'PROCEED'", refusal.Message);
        Assert.Contains("field storedKey", refusal.Message);
        Assert.Equal(
            "PROCEED",
            Assert.Single(ChoiceEntryPoints.ConstructionsIn(TestMethod(nameof(KeyedByALocalTheLiteralWasStoredIn)), typeof(KeyedOption))).KeyLiteral);
    }

    /// <summary>
    /// A bare literal beside a whole key - <c>cond ? "PROCEED" : "X.pages.Y.options.Z"</c>
    /// - is refused naming both, rather than the whole key explaining the construction
    /// and the bare one going unlisted.
    /// </summary>
    [GameFact]
    public void AConstructionKeyedByABareLiteralOrAWholeKeyIsRefusedByName()
    {
        var refusal = Assert.Throws<InvalidOperationException>(() =>
            ChoiceEntryPoints.ConstructionsIn(TestMethod(nameof(KeyedByABareLiteralOrAWholeKey)), typeof(KeyedOption)));

        Assert.Contains(nameof(KeyedByABareLiteralOrAWholeKey), refusal.Message);
        Assert.Contains("'PROCEED'", refusal.Message);
        Assert.Contains("'X.pages.Y.options.Z'", refusal.Message);
    }

    private static MethodBase TestMethod(string name) =>
        typeof(DecisionSurfaceTests).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance)!;

    private sealed class KeyedOption(string key)
    {
        public string Key { get; } = key;
    }

    private readonly string storedKey = "STORED";

    private static KeyedOption KeyedByATernary(bool done) => new(done ? "PROCEED" : "DECLINE");

    private static KeyedOption KeyedByOneLiteral() => new("PROCEED");

    private KeyedOption KeyedByALiteralOrAField(bool flag)
    {
        var key = flag ? "PROCEED" : storedKey;
        return new KeyedOption(key);
    }

    private static KeyedOption KeyedByALocalTheLiteralWasStoredIn()
    {
        var key = "PROCEED";
        return new KeyedOption(key);
    }

    private static KeyedOption KeyedByABareLiteralOrAWholeKey(bool cond) => new(cond ? "PROCEED" : "X.pages.Y.options.Z");

    /// <summary>A game method by type and name, resolved at run time rather than by a
    /// <c>typeof</c> the JIT would resolve before the engine's resolver knows where
    /// the game is.</summary>
    private static MethodBase GameMethod(string eventType, string name)
    {
        const BindingFlags every = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var type = ChoiceEntryPoints.Game.GetType($"MegaCrit.Sts2.Core.Models.Events.{eventType}", throwOnError: true)!;
        return type.GetMethod(name, every) ?? throw new InvalidOperationException($"{eventType} declares no {name} on this build.");
    }

    private static Type EventOptionType() => ChoiceEntryPoints.Game.GetType("MegaCrit.Sts2.Core.Events.EventOption", throwOnError: true)!;

    /// <summary>The <c>MoveNext</c> an async method's body is compiled into.</summary>
    private static MethodBase StateMachineOf(MethodBase method) =>
        method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType
            .GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException($"{method.Name} is not async on this build.");

    /// <summary>
    /// The producer map is the scout's walk on this build: 269 edges from the models
    /// the database registers to 78 seams at their timing classes, the transitive
    /// edges among them, and the six declared seams no content reaches. Pinned by the
    /// edges that mattered: the one alternative, the one bundle screen, the second
    /// gold reward, the two events a transitive walk alone finds, the relic five calls
    /// deep, the seam a scene node reaches from no hook, and the seam the Architect
    /// reaches from two hooks, listed under both rather than under the first enumerated.
    /// </summary>
    [GameFact]
    public void TheProducerMapIsTheScoutsWalkOnThisBuild()
    {
        var map = DecisionSurface.ProducerMap();
        IReadOnlyList<string> Producers(string seam, string timing) =>
            Assert.Single(map, row => row.Seam == seam && row.Timing == timing).Producers;

        Assert.Equal(78, map.Count);
        Assert.Equal(269, map.Sum(row => row.Producers.Count));
        Assert.Equal(1613, DecisionSurface.ModelsWalked());
        Assert.Equal(map.Select(row => row.Point.Identity).Order(StringComparer.Ordinal), map.Select(row => row.Point.Identity));
        Assert.Equal(["RELIC.PAELS_WING"], Producers("card-reward-alternative", "AbstractModel.TryModifyCardRewardAlternatives"));
        Assert.Equal(["RELIC.SCROLL_BOXES"], Producers("card-prompt:CardSelectCmd.FromChooseABundleScreen(player, bundles)", "RelicModel.AfterObtained"));
        Assert.Equal(["RELIC.AMETHYST_AUBERGINE"], Producers("reward-kind:gold", "AbstractModel.TryModifyRewards"));
        Assert.Equal(
            ["EVENT.BRAIN_LEECH", "EVENT.ROOM_FULL_OF_CHEESE"],
            Producers("card-prompt:CardSelectCmd.FromSimpleGridForRewards(context, cards, player, prefs)", "EventModel.GenerateInitialOptions"));
        Assert.Equal(["RELIC.LORDS_PARASOL"], Producers("card-prompt:CardSelectCmd.FromDeckForRemoval(player, prefs, filter)", "AbstractModel.AfterRoomEntered"));
        Assert.Equal(["EVENT.FAKE_MERCHANT"], Producers("reward-kind:relic", DecisionSurface.UnrootedTiming));
        Assert.Equal(["EVENT.THE_ARCHITECT"], Producers("event-option", "EventModel.OnRoomEnter"));
        Assert.Equal(["EVENT.THE_ARCHITECT"], Producers("event-option", "EventModel.SetInitialEventState"));
        Assert.Contains("EVENT.THE_ARCHITECT", Producers("event-option", "EventModel.GenerateInitialOptions"));
        Assert.Equal(
            [
                "card-prompt:RelicSelectCmd.FromChooseARelicScreen(player, relics)", "rest-option:HEAL", "rest-option:MEND",
                "rest-option:SMITH", "reward-kind:LinkedRewardSet", "room:EnterRoom",
            ],
            DecisionSurface.UnproducedSeams());
        Assert.All(map, row => Assert.NotEmpty(row.AnsweredAt));
        Assert.Equal(map.Select(row => row.Point.Identity), DecisionSurface.Seams());
    }

    /// <summary>A relic is dealt by its rarity's mechanism and by every option that
    /// offers it: Neow's, an ancient's, an event's; a starter is a character's own.</summary>
    [GameFact]
    public void ARelicIsDealtByItsRarityAndByEveryOptionThatOffersIt()
    {
        string Dealt(string relic) => string.Join(", ", DecisionSurface.DealtBy(relic));

        Assert.Equal("neow", Dealt("RELIC.WINGED_BOOTS"));
        Assert.Equal("ancient EVENT.PAEL", Dealt("RELIC.PAELS_WING"));
        Assert.Equal("ancient EVENT.DARV", Dealt("RELIC.ASTROLABE"));
        Assert.Equal("grab-bag Common", Dealt("RELIC.AMETHYST_AUBERGINE"));
        Assert.Equal("shop", Dealt("RELIC.CAULDRON"));
        Assert.Equal("starter", Dealt("RELIC.BURNING_BLOOD"));
        Assert.Equal("event EVENT.HUNGRY_FOR_MUSHROOMS", Dealt("RELIC.BIG_MUSHROOM"));
        Assert.Throws<ArgumentException>(() => DecisionSurface.DealtBy("RELIC.NO_SUCH_RELIC"));
    }

    /// <summary>An event is reachable in the acts whose own lists hold it, every act
    /// for a shared event or ancient, and an act's ancient in that act alone.</summary>
    [GameFact]
    public void AnEventIsReachableInTheActsThatListIt()
    {
        string[] acts = ["ACT.OVERGROWTH", "ACT.UNDERDOCKS", "ACT.HIVE", "ACT.GLORY"];

        Assert.Equal(acts, DecisionSurface.ActsReaching("EVENT.BRAIN_LEECH"));
        Assert.Equal(acts, DecisionSurface.ActsReaching("EVENT.DARV"));
        Assert.Equal(["ACT.HIVE"], DecisionSurface.ActsReaching("EVENT.PAEL"));
        Assert.Equal(["ACT.OVERGROWTH"], DecisionSurface.ActsReaching("EVENT.WELLSPRING"));
        Assert.Empty(DecisionSurface.ActsReaching("EVENT.NO_SUCH_EVENT"));
        Assert.Empty(DecisionSurface.ActsReaching(DecisionSurface.ArchitectEventId));
        Assert.Contains("EVENT.PAEL", DecisionSurface.ReachableIn("ACT.HIVE"));
        Assert.Contains("EVENT.DARV", DecisionSurface.ReachableIn("ACT.HIVE"));
        Assert.DoesNotContain("EVENT.PAEL", DecisionSurface.ReachableIn("ACT.OVERGROWTH"));
        Assert.Throws<ArgumentException>(() => DecisionSurface.ReachableIn("ACT.NO_SUCH_ACT"));
    }

    /// <summary>
    /// The map derives an excusal class where it reads one - the three screens from
    /// the stand-in table, the relic screen and the linked set from nothing reaching
    /// them, the mend from the game's own player-count branch and the undo from the
    /// driver's list of what the client offers only with another player, the reroll
    /// from the after-action its construction passes and the dolls from the title
    /// their options are keyed by, a locked option from the null it is constructed
    /// with, the Architect and its options and the seams it alone produces from the
    /// win, a seam from the screens its answers are all on - and derives none for a
    /// point content produces. A placeholder is admissible only where nothing is
    /// derived; a generated row is admissible everywhere; retail-only timing is
    /// derived for nothing on this build.
    /// </summary>
    [GameFact]
    public void TheMapDerivesAClassWhereItReadsOneAndAdmitsAPlaceholderOnlyElsewhere()
    {
        IReadOnlyList<ExcusalClass> Derived(string kind, string identity) =>
            DecisionSurface.DerivedExcusals(new DecisionPoint(kind, identity)).Order().ToList();

        Assert.Equal([ExcusalClass.ScreenWithoutHeadlessHost], Derived(DecisionKinds.Verb, "SelectBundleFromScreen"));
        Assert.Equal([ExcusalClass.ScreenWithoutHeadlessHost], Derived(DecisionKinds.Verb, "RevealCrystalSphereCell"));
        Assert.Equal(
            [ExcusalClass.NoProducerOnThisBuild, ExcusalClass.ScreenWithoutHeadlessHost],
            Derived(DecisionKinds.Verb, "SelectRelicFromScreen"));
        Assert.Equal([ExcusalClass.MultiplayerOnly], Derived(DecisionKinds.Verb, "UndoEndTurn"));
        Assert.Empty(Derived(DecisionKinds.Verb, "ConfirmCardScreen"));
        Assert.Equal([ExcusalClass.NoProducerOnThisBuild], Derived(DecisionKinds.RewardKind, "LinkedRewardSet"));
        Assert.Empty(Derived(DecisionKinds.RewardKind, "card_removal"));
        Assert.Equal([ExcusalClass.MultiplayerOnly], Derived(DecisionKinds.RestOption, "MEND"));
        Assert.Empty(Derived(DecisionKinds.RestOption, "HEAL"));
        Assert.Empty(Derived(DecisionKinds.RestOption, "LIFT"));
        Assert.Equal([ExcusalClass.NotReplayable], Derived(DecisionKinds.CardRewardAlternative, "REROLL"));
        Assert.Empty(Derived(DecisionKinds.CardRewardAlternative, "Skip"));
        Assert.Empty(Derived(DecisionKinds.CardRewardAlternative, "SACRIFICE"));
        Assert.Equal(
            [ExcusalClass.NoProducerOnThisBuild],
            Derived(DecisionKinds.CardPrompt, "RelicSelectCmd.FromChooseARelicScreen(player, relics)"));
        Assert.Empty(Derived(DecisionKinds.CardPrompt, "CardSelectCmd.FromChooseABundleScreen(player, bundles)"));
        Assert.Empty(Derived(DecisionKinds.Event, "EVENT.PAEL"));
        Assert.Equal([ExcusalClass.ReachedByTheWin], Derived(DecisionKinds.Event, "EVENT.THE_ARCHITECT"));
        Assert.Equal([ExcusalClass.ReachedByTheWin], Derived(DecisionKinds.EventOption, "EVENT.THE_ARCHITECT PROCEED"));
        Assert.Empty(Derived(DecisionKinds.EventOption, "EVENT.NEOW RELIC.WINGED_BOOTS"));
        Assert.Empty(Derived(DecisionKinds.EventOption, "EVENT.RELIC_TRADER PROCEED"));
        Assert.Equal([ExcusalClass.NotReplayable], Derived(DecisionKinds.EventOption, "EVENT.DOLL_ROOM relics.MR_STRUGGLES.title"));
        Assert.Empty(Derived(DecisionKinds.EventOption, "EVENT.DOLL_ROOM DOLL_ROOM.pages.INITIAL.options.EXAMINE"));
        Assert.Equal([ExcusalClass.NotChoosable], Derived(DecisionKinds.EventOption, "EVENT.WATERLOGGED_SCRIPTORIUM WATERLOGGED_SCRIPTORIUM.pages.INITIAL.options.PRICKLY_SPONGE_LOCKED"));
        Assert.Equal([ExcusalClass.NotChoosable], Derived(DecisionKinds.EventOption, "EVENT.ZEN_WEAVER ZEN_WEAVER.pages.INITIAL.options.LOCKED"));
        Assert.Empty(Derived(DecisionKinds.EventOption, "EVENT.WATERLOGGED_SCRIPTORIUM WATERLOGGED_SCRIPTORIUM.pages.INITIAL.options.PRICKLY_SPONGE"));
        Assert.Empty(Derived(DecisionKinds.EventOption, "EVENT.SELF_HELP_BOOK SELF_HELP_BOOK.pages.INITIAL.options.NO_OPTIONS"));

        // Every option the build offers locked is named so - twenty on this build,
        // every one the locked form of an option the player cannot afford - and
        // nothing else is: the reading is the null the option is constructed with
        var locked = DecisionSurface.EventOptionKeys()
            .Where(option => DecisionSurface.NotChoosableOptions(option.EventId).Contains(option.Key, StringComparer.Ordinal))
            .Select(option => option.Key)
            .ToList();
        Assert.Equal(20, locked.Count);
        Assert.All(locked, key => Assert.EndsWith("LOCKED", key, StringComparison.Ordinal));
        Assert.Empty(DecisionSurface.NotChoosableOptions("EVENT.PAEL"));
        Assert.Equal(["relics.DAUGHTER_OF_THE_WIND.title", "relics.MR_STRUGGLES.title", "relics.BING_BONG.title"], DecisionSurface.TitleKeyedOptions("EVENT.DOLL_ROOM"));
        Assert.Empty(DecisionSurface.TitleKeyedOptions("EVENT.BRAIN_LEECH"));
        Assert.Equal(
            [ExcusalClass.ScreenWithoutHeadlessHost],
            Derived(DecisionKinds.Seam, "card-prompt:CardSelectCmd.FromChooseABundleScreen(player, bundles) @ RelicModel.AfterObtained"));
        Assert.Equal(
            [ExcusalClass.ScreenWithoutHeadlessHost],
            Derived(DecisionKinds.Seam, "screen:NCrystalSphereScreen.ShowScreen @ EventModel.GenerateInitialOptions"));
        Assert.Equal([ExcusalClass.ReachedByTheWin], Derived(DecisionKinds.Seam, "event-option @ EventModel.OnRoomEnter"));
        Assert.Empty(Derived(DecisionKinds.Seam, "event-option @ EventModel.GenerateInitialOptions"));
        Assert.Empty(Derived(DecisionKinds.Seam, "rewards:OfferCustom @ RelicModel.AfterObtained"));
        Assert.All(DecisionSurface.All(), point => Assert.DoesNotContain(ExcusalClass.RetailOnlyTiming, DecisionSurface.DerivedExcusals(point)));

        var admissible = DecisionSurface.AdmissibleExcusals(DecisionSurface.All());
        Assert.Equal(
            [ExcusalClass.MultiplayerOnly, ExcusalClass.Generated],
            admissible[new DecisionPoint(DecisionKinds.RestOption, "MEND")].Order());
        Assert.Equal(
            [ExcusalClass.Generated, ExcusalClass.NotOnTheRoute],
            admissible[new DecisionPoint(DecisionKinds.RestOption, "HEAL")].Order());
        Assert.Equal(DecisionSurface.All().Count, admissible.Count);
    }

    /// <summary>Every excusal is in a class the map admits for its point, which is
    /// what lets <c>coverage</c> hold a build to the classes rather than to prose.</summary>
    [GameFact]
    public void EveryExcusalIsInAClassTheMapAdmits()
    {
        var admissible = DecisionSurface.AdmissibleExcusals(DecisionSurface.All());

        Assert.All(
            DecisionExcusals.All,
            excusal => Assert.Contains(excusal.Value.Class, admissible[excusal.Key]));
    }

    /// <summary>Every producer an excusal names is one the map lists for its point,
    /// which is what lets <c>coverage</c> hold a sentence about who produces a point
    /// to the build rather than to the person who wrote it; the three the audit found
    /// naming their producer in prose alone name it by id now, beside the rest
    /// options the ancient rows fall short of.</summary>
    [GameFact]
    public void EveryProducerAnExcusalNamesIsOneTheMapListsForItsPoint()
    {
        var map = DecisionSurface.ProducerMap();
        var named = DecisionExcusals.All.Where(excusal => excusal.Value.NamedProducers.Count > 0).ToList();

        Assert.All(
            named,
            excusal => Assert.All(
                excusal.Value.NamedProducers,
                producer => Assert.Contains(producer, DecisionCoverage.ProducersListedAt(excusal.Key, map))));
        Assert.Equal(
            ["CARD.BYRDONIS_EGG"],
            DecisionExcusals.All[new DecisionPoint(DecisionKinds.RestOption, "HATCH")].NamedProducers);
        Assert.Equal(
            ["POWER.FORBIDDEN_GRIMOIRE_POWER"],
            DecisionExcusals.All[new DecisionPoint(DecisionKinds.RewardKind, "card_removal")].NamedProducers);
        Assert.Equal(
            ["POWER.SWIPE_POWER", "EVENT.THE_LANTERN_KEY"],
            DecisionExcusals.All[new DecisionPoint(DecisionKinds.RewardKind, "special_card")].NamedProducers);
        Assert.Equal(
            ["POWER.SWIPE_POWER"],
            DecisionExcusals.All[DecisionPoint.Seam("reward-kind:special_card", "AbstractModel.BeforeDeath")].NamedProducers);
        Assert.Equal(
            ["EVENT.THE_LANTERN_KEY"],
            DecisionExcusals.All[DecisionPoint.Seam("reward-kind:special_card", "EventModel.GenerateInitialOptions")].NamedProducers);
        Assert.Equal(
            ["RELIC.PAELS_GROWTH"],
            DecisionExcusals.All[new DecisionPoint(DecisionKinds.RestOption, "CLONE")].NamedProducers);
        Assert.Equal(11, named.Count);
    }

    /// <summary>The committed producer map is what the walk produces on this build,
    /// regenerated by the same command as the denominator.</summary>
    [GameFact]
    public void TheCommittedProducerMapIsWhatTheWalkProduces()
    {
        var path = Path.Combine(Arbiter.RepoRoot, DecisionSurface.ProducerMapRecordPath);
        var actual = DecisionSurface.ProducerMapRecord();
        var recorded = File.Exists(path) ? File.ReadAllText(path) : null;

        Assert.True(
            recorded == actual,
            "The producer map on this build is not the recorded one. If the game build changed, regenerate the " +
            "record in the same change:\n\n" +
            "    ./scripts/arbiter coverage --corpus manifests --update\n\n" +
            $"Recorded in {path}:\n{recorded ?? "(no file)"}\nThis build:\n{actual}");
    }
}

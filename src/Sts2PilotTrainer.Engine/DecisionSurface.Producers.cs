using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The seam-centric half of the coverage map: every event's options, the producer
/// walk that lists, per seam and timing class, the content that reaches it, what
/// deals each relic and which act reaches each event, and the excusal classes the
/// map admits for every point.
///
/// What breaks a normal player's recording is never a card effect; every replay
/// refusal this project has recorded was a seam - a driver rule, a format that cannot
/// name a thing, a screen the host stands in for, a sample taken at the wrong instant.
/// So the map is keyed by seam and by the game hook the producing content runs in,
/// and the models are listed under each seam as the routes to it: two producers of one
/// seam at one timing exercise the same recorder and driver path, and a seed hunter
/// reads the list to know what to look for. The walk is the scout's producer walk on
/// the engine's own IL reader: from every hook a model overrides, through its own
/// helpers, its lambdas and its state machines and the game members they call, to
/// the first member that is a seam, bounded in depth so the map stays about the
/// model's own code rather than the whole engine behind it. It stops at other models,
/// at the hook broadcaster and at the scene tree for the same reason.
/// </summary>
public static partial class DecisionSurface
{
    private const BindingFlags Every = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                       BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    /// <summary>The namespace the game's model families live in exactly; content
    /// lives in the namespaces under it.</summary>
    private const string ModelsNamespace = "MegaCrit.Sts2.Core.Models";

    /// <summary>How far from a model's own hook the walk follows game members before
    /// it stops: the scout measured every edge converging within six on v0.111.0, and
    /// past it a model's call into the run manager would reach every seam the run has.</summary>
    private const int WalkDepth = 6;

    /// <summary>The timing class of a seam a model reaches from no hook it overrides:
    /// the walk from every one of its members found it and the walk from its hooks
    /// did not, so the map says so rather than dropping the edge.</summary>
    public const string UnrootedTiming = "?";

    // ── Event options ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Every option of every event this build ships, as <c>event_id option_key</c>:
    /// the events, the ancients and the Architect of <see cref="Events"/>, and Neow,
    /// whose blessing carries an option key under its own verb. An ancient's options
    /// are the game's own <c>AllPossibleOptions</c> - every option its pools can roll -
    /// keyed the way the driver keys one, by the relic dealt or the option's text key.
    /// Any other event's are read off its own IL: every option key literal its bodies
    /// load, every <c>InitialOptionKey</c> it builds one with, and every relic its
    /// generic <c>RelicOption</c> deals. Every <c>EventOption</c> the event constructs
    /// is then held to that reading - the key it is constructed with has to be one of
    /// those literals - and an event that constructs one with a key built at runtime
    /// is refused by name rather than listed short, except where
    /// <see cref="RuntimeBuiltOptionKeys"/> derives its keys the way the event's own
    /// code builds them.
    /// </summary>
    public static IReadOnlyList<string> EventOptions() =>
        EventOptionKeys().Select(option => DecisionPoint.EventOption(option.EventId, option.Key).Identity).ToList();

    /// <summary>The same, as (event id, option key) pairs.</summary>
    public static IReadOnlyList<(string EventId, string Key)> EventOptionKeys() => EventOptionWalk.Value;

    private static readonly Lazy<IReadOnlyList<(string EventId, string Key)>> EventOptionWalk = new(() =>
    {
        EngineHost.Start();
        return EventModels().Concat(ModelDb.AllAncients.Where(model => model is Neow))
            .SelectMany(model => OptionKeysOf(model).Select(key => (EventId: model.Id.ToString(), Key: key)))
            .Distinct()
            .OrderBy(option => option.EventId, StringComparer.Ordinal)
            .ThenBy(option => option.Key, StringComparer.Ordinal)
            .ToList();
    });

    /// <summary>A whole option key as an event's code writes one: an event, a page and
    /// an option, the option's last character never the underscore an interpolated
    /// key's literal half ends in.</summary>
    private static readonly Regex OptionKeyLiteral = new(
        @"^[A-Z0-9_]+\.pages\.[A-Za-z0-9_]+\.options\.[A-Za-z0-9_]*[A-Za-z0-9]$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The events whose option keys the game builds at runtime, each with the same
    /// derivation the event's own code makes, so the walk lists the keys a recorder
    /// writes rather than the literal halves the IL holds. Colorful Philosophers
    /// offers one option per character card pool, keyed by the pool's energy colour;
    /// Endless Conveyor keys each dish by the id its <c>Dish</c> record is constructed
    /// with; Colossal Flower numbers its extract and dig options by the digs so far,
    /// on its first page and on each deeper one; Slippery Bridge numbers each hold-on
    /// by the holds so far, on the page the last one opened, and reads <c>LOOP</c> past
    /// the seventh; Tablet of Truth offers its decipher on a page per decipher so far;
    /// Doll Room keys each doll by the title of the relic in it, the way the driver
    /// keys an option with no relic set; Tinker Time keys each rider through a helper
    /// whose literals are the keys; Relic Trader offers a bare <c>PROCEED</c> to a
    /// player with nothing to trade; and the Architect offers a line per line of the
    /// dialogue the win rolls and then a bare <c>PROCEED</c>. An event that builds a
    /// key and is not here is refused by name, so a game update that adds one fails the
    /// denominator rather than thinning it.
    ///
    /// Doll Room's keys are the one place the recorded key is a title rather than an
    /// id: the retail client writes the player's localized title where this process,
    /// whose localization returns every key as itself, writes the title's key. The
    /// walk lists what this process reads, and <see cref="TitleKeyedConstructions"/>
    /// is the reading that says no recording of those points can replay on this build.
    /// </summary>
    private static IReadOnlyList<string>? RuntimeBuiltOptionKeys(EventModel model) => model switch
    {
        ColorfulPhilosophers => ModelDb.AllCharacterCardPools
            .Select(pool => $"{model.Id.Entry}.pages.INITIAL.options.{pool.EnergyColorName.ToUpperInvariant()}")
            .ToList(),
        EndlessConveyor => DishKeysOf(model),
        ColossalFlower => ColossalFlowerKeys(model),
        SlipperyBridge => SlipperyBridgeKeys(model),
        TabletOfTruth => TabletOfTruthKeys(model),
        DollRoom => DollRoomKeys(model),
        TinkerTime => LiteralsOf(model, "GetRiderLocKey"),
        RelicTrader => ["PROCEED"],
        TheArchitect architect => ArchitectKeys(architect),
        _ => null,
    };

    private static IReadOnlyList<string> DishKeysOf(EventModel conveyor)
    {
        var dish = conveyor.GetType().GetNestedType("Dish", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"{conveyor.Id} declares no nested Dish on this build.");
        return OwnMembersOf(conveyor.GetType())
            .SelectMany(member => ChoiceEntryPoints.ConstructionsIn(member, dish))
            .Select(construction => construction.Literal
                ?? throw new InvalidOperationException($"{conveyor.Id} constructs a dish with no id literal before it."))
            .Select(id => $"{conveyor.Id.Entry}.pages.ALL.options.{id}")
            .ToList();
    }

    /// <summary>The digs are counted from zero, the first page numbers its options by
    /// the dig they would be, and each dig short of the second opens a page named for
    /// it; the second dig's page carries literal keys the IL walk reads itself.</summary>
    private static IReadOnlyList<string> ColossalFlowerKeys(EventModel flower)
    {
        var entry = flower.Id.Entry;
        return
        [
            $"{entry}.pages.INITIAL.options.EXTRACT_CURRENT_PRIZE_1",
            $"{entry}.pages.INITIAL.options.REACH_DEEPER_1",
            $"{entry}.pages.REACH_DEEPER_1.options.EXTRACT_CURRENT_PRIZE_2",
            $"{entry}.pages.REACH_DEEPER_1.options.REACH_DEEPER_2",
        ];
    }

    /// <summary>Each hold-on opens the page of the hold before it and offers the next,
    /// both numbered by <c>GetHoldOnSuffix</c>, which reads <c>LOOP</c> from the
    /// seventh; from there the page and the option both read <c>LOOP</c> and the set
    /// is closed.</summary>
    private static IReadOnlyList<string> SlipperyBridgeKeys(EventModel bridge)
    {
        var entry = bridge.Id.Entry;
        var suffix = bridge.GetType().GetMethod("GetHoldOnSuffix", Every)
            ?? throw new InvalidOperationException($"{bridge.Id} declares no GetHoldOnSuffix on this build.");
        string Suffix(int holdOns) => (string)suffix.Invoke(bridge, [holdOns])!;
        var keys = new List<string>();
        for (var holdOns = 1; ; holdOns++)
        {
            var key = $"{entry}.pages.HOLD_ON_{Suffix(holdOns - 1)}.options.HOLD_ON_{Suffix(holdOns)}";
            if (keys.Contains(key)) break;
            keys.Add(key);
        }

        return keys;
    }

    /// <summary>Each decipher short of the fifth opens a page named for the deciphers
    /// so far and offers the next; the fifth finishes the event.</summary>
    private static IReadOnlyList<string> TabletOfTruthKeys(EventModel tablet) =>
        Enumerable.Range(1, 4).Select(deciphers => $"{tablet.Id.Entry}.pages.DECIPHER_{deciphers}.options.DECIPHER").ToList();

    /// <summary>The dolls are the event's own static table of relics, and each doll's
    /// option is keyed by its relic's title with no relic set on the option.</summary>
    private static IReadOnlyList<string> DollRoomKeys(EventModel room)
    {
        var dolls = room.GetType().GetField("_dolls", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null) as Array
            ?? throw new InvalidOperationException($"{room.Id} declares no static _dolls table on this build.");
        return dolls.Cast<object>()
            .Select(doll => doll.GetType().GetField("relic")?.GetValue(doll) as RelicModel
                ?? throw new InvalidOperationException($"{room.Id}'s doll table carries no relic on this build."))
            .Select(relic => relic.Title.GetRawText())
            .ToList();
    }

    /// <summary>
    /// The events that key an option by a <c>LocString</c>'s raw text rather than by a
    /// literal, each with the member that constructs it and the keys that member
    /// produces: the reading behind <see cref="ExcusalClass.NotReplayable"/> for an
    /// event option. A recorder writes such a key as the player's localized title and
    /// this process reads the title's key, so <c>RunDriver.OptionKey</c> never matches
    /// the recorded key on this build and no recording of the point can replay;
    /// stabilizing the spelling in the recorder and the driver is a later stage. The
    /// table is held to the IL both ways in <see cref="OptionKeysOf"/>: a construction
    /// keyed through <c>GetRawText</c> in a member not named here is refused, and a
    /// member named here that no longer constructs one is stale and refused.
    /// </summary>
    private static readonly IReadOnlyDictionary<Type, (string Member, Func<EventModel, IReadOnlyList<string>> Keys)> TitleKeyedConstructions =
        new Dictionary<Type, (string, Func<EventModel, IReadOnlyList<string>>)>
        {
            [typeof(DollRoom)] = ("OptionFromChoice", DollRoomKeys),
        };

    /// <summary>The option keys of an event that a recording carries as a localized
    /// title, so none it carries can replay: every key the event's title-keyed
    /// constructions produce, none for an event with no such construction.</summary>
    public static IReadOnlyList<string> TitleKeyedOptions(string eventId)
    {
        EngineHost.Start();
        var model = EventModels().FirstOrDefault(candidate => candidate.Id.ToString() == eventId);
        return model is not null && TitleKeyedConstructions.TryGetValue(model.GetType(), out var construction)
            ? construction.Keys(model)
            : [];
    }

    private static bool ReadsATitle(MethodBase member) =>
        ChoiceEntryPoints.Callees(member).Any(callee => callee.DeclaringType == typeof(LocString) && callee.Name == nameof(LocString.GetRawText));

    /// <summary>The option keys a helper of the event's own returns: every literal its
    /// body loads.</summary>
    private static IReadOnlyList<string> LiteralsOf(EventModel model, string helper)
    {
        var method = model.GetType().GetMethod(helper, Every)
            ?? throw new InvalidOperationException($"{model.Id} declares no {helper} on this build.");
        return ChoiceEntryPoints.StringLiterals(method).Where(literal => OptionKeyLiteral.IsMatch(literal)).ToList();
    }

    /// <summary>One option per line of the longest dialogue the Architect's own set
    /// holds but the last, keyed by the line's index, then the PROCEED its last line
    /// and an empty dialogue both offer.</summary>
    private static IReadOnlyList<string> ArchitectKeys(TheArchitect architect)
    {
        var longest = architect.DialogueSet.GetAllDialogues().Max(dialogue => dialogue.Lines.Count);
        return Enumerable.Range(0, Math.Max(longest - 1, 0))
            .Select(line => $"{architect.Id.Entry}.dialogue.{line.ToString(CultureInfo.InvariantCulture)}")
            .Append("PROCEED")
            .ToList();
    }

    private static IReadOnlyList<string> OptionKeysOf(EventModel model)
    {
        if (model is AncientEventModel ancient)
        {
            return ancient.AllPossibleOptions.Select(RunDriver.OptionKey).Distinct(StringComparer.Ordinal).ToList();
        }

        var initialOptionKey = typeof(EventModel).GetMethod("InitialOptionKey", Every)
            ?? throw new InvalidOperationException("EventModel.InitialOptionKey is not declared on this build.");
        var keys = new List<string>();
        var runtimeBuilt = RuntimeBuiltOptionKeys(model);
        TitleKeyedConstructions.TryGetValue(model.GetType(), out var titleKeyed);
        var titleKeyedMemberMet = false;
        foreach (var member in OwnMembersOf(model.GetType()))
        {
            keys.AddRange(ChoiceEntryPoints.StringLiterals(member).Where(literal => OptionKeyLiteral.IsMatch(literal)));

            var initialKeys = ChoiceEntryPoints.LiteralsBefore(member, callee => callee == initialOptionKey);
            keys.AddRange(initialKeys.Select(literal => $"{model.Id.Entry}.pages.INITIAL.options.{literal}"));

            // Every option the member constructs is held to the literals read above:
            // among the strings loaded on the way to it is a whole key, or the name an
            // InitialOptionKey call completes; a construction with neither carries a
            // key the event built at runtime, which only a derivation here can list
            foreach (var construction in ChoiceEntryPoints.ConstructionsIn(member, typeof(EventOption)))
            {
                var explained = construction.Literals.Any(literal =>
                    OptionKeyLiteral.IsMatch(literal) || initialKeys.Contains(literal, StringComparer.Ordinal));
                if (!explained && ReadsATitle(member))
                {
                    if (titleKeyed.Member != member.Name)
                    {
                        throw new InvalidOperationException(
                            $"{model.Id} constructs an option in {member.DeclaringType!.Name}.{member.Name} keyed by a " +
                            "LocString's raw text, which a recording carries as the player's localized title; add the " +
                            "member and its keys to DecisionSurface.TitleKeyedConstructions.");
                    }

                    titleKeyedMemberMet = true;
                    continue;
                }

                if (!explained && runtimeBuilt is null)
                {
                    throw new InvalidOperationException(
                        $"{model.Id} constructs an option in {member.DeclaringType!.Name}.{member.Name} with no whole " +
                        $"option key literal on the way to it ({(construction.Literals.Count == 0 ? "no string loaded" : string.Join(", ", construction.Literals.Select(literal => $"'{literal}'")))}), " +
                        "so the key is built at runtime; add its derivation to DecisionSurface.RuntimeBuiltOptionKeys.");
                }
            }

            foreach (var callee in ChoiceEntryPoints.Callees(member))
            {
                if (callee.Name != "RelicOption" || callee.DeclaringType != typeof(EventModel)) continue;
                if (callee is MethodInfo { IsGenericMethod: true } relicOption)
                {
                    keys.Add(IdOf(relicOption.GetGenericArguments()[0]));
                }
                else
                {
                    throw new InvalidOperationException(
                        $"{model.Id} deals a relic through the non-generic RelicOption in " +
                        $"{member.DeclaringType!.Name}.{member.Name}, so which relic is not on the IL and " +
                        "this walk cannot name the option.");
                }
            }
        }

        if (titleKeyed.Member is not null && !titleKeyedMemberMet)
        {
            throw new InvalidOperationException(
                $"DecisionSurface.TitleKeyedConstructions names {model.GetType().Name}.{titleKeyed.Member}, and no such " +
                "member constructs an option keyed by a LocString's raw text on this build; the entry is stale.");
        }

        return keys.Concat(runtimeBuilt ?? []).Distinct(StringComparer.Ordinal).ToList();
    }

    // ── The producer map ──────────────────────────────────────────────────────────

    /// <summary>Every seam at every timing class some content reaches, with its
    /// producers by id, in seam order then timing order. Walked once per process.</summary>
    public static IReadOnlyList<ProducerSeam> ProducerMap() => ProducerWalk.Value.Seams;

    /// <summary>The seam points, as <see cref="Identities"/> lists them.</summary>
    public static IReadOnlyList<string> Seams() => ProducerMap().Select(seam => seam.Point.Identity).ToList();

    /// <summary>The seams this build declares that no content reaches: the prompt entry
    /// points, reward kinds and rest options nothing produces, listed so the record
    /// says which are dead on this build.</summary>
    public static IReadOnlyList<string> UnproducedSeams() => ProducerWalk.Value.Unproduced;

    /// <summary>The producers no seam lists: every model the walk reached no seam from,
    /// counted so the map's coverage of the content is a number rather than a claim.</summary>
    public static int ModelsWalked() => ProducerWalk.Value.ModelsWalked;

    private sealed record ProducerMapReading(
        IReadOnlyList<ProducerSeam> Seams, IReadOnlyList<string> Unproduced, int ModelsWalked);

    private static readonly Lazy<ProducerMapReading> ProducerWalk = new(WalkProducers);

    private static ProducerMapReading WalkProducers()
    {
        EngineHost.Start();
        var models = ModelDb.All
            .Where(model => !IsMock(model.GetType()))
            .OrderBy(model => model.Id.ToString(), StringComparer.Ordinal)
            .ToList();
        var edges = new Dictionary<(string Seam, string Timing), List<string>>();
        foreach (var model in models)
        {
            var type = model.GetType();
            var fromHooks = new HashSet<(string Seam, string Timing)>();
            foreach (var root in HookRoots(type))
            {
                foreach (var seam in SeamsReachedFrom(type, [root]))
                {
                    fromHooks.Add((seam, TimingOf(root)));
                }
            }

            // The check that the hooks explain every edge: a seam the model's whole
            // member set reaches and no hook does is listed under the unrooted timing
            var rooted = fromHooks.Select(edge => edge.Seam).ToHashSet(StringComparer.Ordinal);
            foreach (var seam in SeamsReachedFrom(type, OwnMembersOf(type)))
            {
                if (!rooted.Contains(seam)) fromHooks.Add((seam, UnrootedTiming));
            }

            foreach (var (seam, timing) in fromHooks)
            {
                if (!edges.TryGetValue((seam, timing), out var producers)) edges[(seam, timing)] = producers = [];
                producers.Add(model.Id.ToString());
            }
        }

        var seams = edges
            .OrderBy(entry => entry.Key.Seam, StringComparer.Ordinal)
            .ThenBy(entry => entry.Key.Timing, StringComparer.Ordinal)
            .Select(entry => new ProducerSeam(entry.Key.Seam, entry.Key.Timing, entry.Value, AnsweredAt(entry.Key.Seam)))
            .ToList();
        var produced = seams.Select(seam => seam.Seam).ToHashSet(StringComparer.Ordinal);
        var unproduced = DeclaredSeams().Where(seam => !produced.Contains(seam)).Order(StringComparer.Ordinal).ToList();
        return new ProducerMapReading(seams, unproduced, models.Count);
    }

    /// <summary>The seams this build declares whether or not content reaches them.</summary>
    private static IEnumerable<string> DeclaredSeams() =>
        CardPrompts().Select(prompt => $"card-prompt:{prompt}")
            .Concat(RewardKinds().Select(kind => $"reward-kind:{kind}"))
            .Concat(RestOptions().Select(option => $"rest-option:{option}"))
            .Concat(["event-option", "card-reward-alternative", "room:EnterRoom"]);

    /// <summary>
    /// The seam a callee is, or null: the way a decision reaches the game, spelled so
    /// it maps onto the point a recording answers it at. A prompt entry point, a screen
    /// shown or created, a reward set offered through <c>RewardsCmd</c>, a reward
    /// constructed by kind, a rest option by its id, an event option, a card-reward
    /// alternative, a merchant entry by shelf, a room entered.
    /// </summary>
    private static string? SeamOf(MethodBase callee)
    {
        var type = callee.DeclaringType;
        if (type is null) return null;
        if (EntryPoints.Value.Contains(callee)) return $"card-prompt:{ChoiceEntryPoints.QualifiedSignature(callee)}";
        if (ChoiceEntryPoints.OpensAPrompt(callee)) return $"screen:{type.Name}.{callee.Name}";
        if (type == typeof(RewardsCmd) && callee.IsStatic) return $"rewards:{callee.Name}";
        if (callee.IsConstructor && type.IsSubclassOf(typeof(Reward))) return $"reward-kind:{LootRewards.KindOf(type)}";
        if (callee.IsConstructor && type.IsSubclassOf(typeof(RestSiteOption))) return $"rest-option:{RestOptionIdOf(type)}";
        if (type == typeof(EventOption) && (callee.IsConstructor || callee.Name == nameof(EventOption.FromRelic))) return "event-option";
        if (callee.IsConstructor && type == typeof(CardRewardAlternative)) return "card-reward-alternative";
        if (callee.IsConstructor && type.IsSubclassOf(typeof(MerchantEntry))) return $"shop-kind:{ShopKindOf(type)}";
        if (type == typeof(RunManager) && callee.Name is "EnterRoom" or "EnterRoomDebug") return $"room:{callee.Name}";
        if (type.Name == "NChooseARelicSelection" && callee.Name == "ShowScreen") return "screen:NChooseARelicSelection.ShowScreen";
        if (type.Name == "NCrystalSphereScreen" && callee.IsStatic) return $"screen:NCrystalSphereScreen.{callee.Name}";
        return null;
    }

    private static readonly Lazy<HashSet<MethodBase>> EntryPoints = new(() => ChoiceEntryPoints.All().ToHashSet<MethodBase>());

    private static string ShopKindOf(Type entry) =>
        typeof(MerchantCardEntry).IsAssignableFrom(entry) ? "card"
        : typeof(MerchantRelicEntry).IsAssignableFrom(entry) ? ShopPurchaseKinds.Relic
        : typeof(MerchantPotionEntry).IsAssignableFrom(entry) ? ShopPurchaseKinds.Potion
        : typeof(MerchantCardRemovalEntry).IsAssignableFrom(entry) ? ShopPurchaseKinds.CardRemoval
        : entry.Name;

    /// <summary>The points a seam is answered at in this format: what a recording that
    /// exercised the seam projects to, so a seam can be reached by co-occurrence.</summary>
    private static IReadOnlyList<DecisionPoint> AnsweredAt(string seam)
    {
        var (family, identity) = seam.IndexOf(':', StringComparison.Ordinal) is var colon && colon >= 0
            ? (seam[..colon], seam[(colon + 1)..])
            : (seam, "");
        DecisionPoint Verb(ActionVerb verb) => new(DecisionKinds.Verb, verb.ToString());
        DecisionPoint[] cardScreen = [Verb(ActionVerb.SelectCardFromScreen), Verb(ActionVerb.ConfirmCardScreen)];
        return family switch
        {
            "card-prompt" when identity.StartsWith("CardSelectCmd.FromChooseABundleScreen", StringComparison.Ordinal) =>
                [Verb(ActionVerb.SelectBundleFromScreen)],
            "card-prompt" when identity.StartsWith("RelicSelectCmd.", StringComparison.Ordinal) =>
                [Verb(ActionVerb.SelectRelicFromScreen)],
            "card-prompt" => cardScreen,
            "screen" when identity.StartsWith("NCrystalSphereScreen.", StringComparison.Ordinal) =>
                [Verb(ActionVerb.RevealCrystalSphereCell)],
            "screen" when identity.StartsWith("NChooseARelicSelection.", StringComparison.Ordinal) =>
                [Verb(ActionVerb.SelectRelicFromScreen)],
            "screen" => cardScreen,
            "rewards" =>
                [Verb(ActionVerb.ClaimReward), Verb(ActionVerb.TakeCard), Verb(ActionVerb.SkipRewards), Verb(ActionVerb.TakeCardRewardAlternative)],
            "reward-kind" => [new DecisionPoint(DecisionKinds.RewardKind, identity)],
            "rest-option" => [new DecisionPoint(DecisionKinds.RestOption, identity)],
            "event-option" => [Verb(ActionVerb.ChooseEventOption), Verb(ActionVerb.ChooseNeowBlessing)],
            "card-reward-alternative" => [Verb(ActionVerb.TakeCardRewardAlternative)],
            "shop-kind" when identity == "card" =>
                [new DecisionPoint(DecisionKinds.ShopKind, ShopPurchaseKinds.CharacterCard), new DecisionPoint(DecisionKinds.ShopKind, ShopPurchaseKinds.ColorlessCard)],
            "shop-kind" => [new DecisionPoint(DecisionKinds.ShopKind, identity)],
            "room" => [Verb(ActionVerb.MapMove)],
            _ => [],
        };
    }

    /// <summary>
    /// The seams reachable from a set of a model's members: through the model's own
    /// code, its bases, and any game member that is not another model's, not the hook
    /// broadcaster and not the scene tree, to the walk's depth.
    /// </summary>
    private static IReadOnlySet<string> SeamsReachedFrom(Type model, IEnumerable<MethodBase> roots)
    {
        var seams = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<MethodBase>();
        var frontier = new Queue<(MethodBase Member, int Depth)>();
        foreach (var root in roots)
        {
            if (seen.Add(root)) frontier.Enqueue((root, 0));
        }

        while (frontier.Count > 0)
        {
            var (member, depth) = frontier.Dequeue();
            foreach (var callee in Expanded(member))
            {
                if (SeamOf(callee) is { } seam)
                {
                    seams.Add(seam);
                    continue;
                }

                if (depth + 1 > WalkDepth) continue;
                var declaring = callee.DeclaringType;
                if (declaring is null || declaring.Assembly != ChoiceEntryPoints.Game) continue;
                var outer = Outermost(declaring);
                if (outer == typeof(Hook)) continue;
                if (outer.IsSubclassOf(typeof(AbstractModel)) && outer != model && !model.IsSubclassOf(outer)) continue;
                if (outer.Namespace?.Contains(".Nodes", StringComparison.Ordinal) == true) continue;
                if (seen.Add(callee)) frontier.Enqueue((callee, depth + 1));
            }
        }

        return seams;
    }

    private static readonly Dictionary<MethodBase, IReadOnlyList<MethodBase>> Expansions = [];

    private static IReadOnlyList<MethodBase> Expanded(MethodBase member)
    {
        lock (Expansions)
        {
            if (!Expansions.TryGetValue(member, out var callees))
            {
                Expansions[member] = callees = ChoiceEntryPoints.OwnCalleesOf(member);
            }

            return callees;
        }
    }

    /// <summary>
    /// The hooks a model overrides: every member declared on it or on a content base
    /// it extends whose base definition is a family base's or the abstract model's -
    /// the game's own entry into the model's code, and so the timing class of what
    /// the code reaches. A member whose base definition is another content type's is
    /// reached through that type's hook and is not a root of its own.
    /// </summary>
    private static IEnumerable<MethodInfo> HookRoots(Type model) =>
        ContentChain(model)
            .SelectMany(type => type.GetMethods(Every))
            .Where(method => method.GetBaseDefinition() is { } baseDefinition
                             && baseDefinition.DeclaringType != method.DeclaringType
                             && !IsContent(baseDefinition.DeclaringType!));

    /// <summary>The name of the hook a root is: the family base's member it overrides,
    /// a property read by its property's name.</summary>
    private static string TimingOf(MethodInfo root)
    {
        var baseDefinition = root.GetBaseDefinition();
        var name = baseDefinition.Name;
        if (baseDefinition.IsSpecialName && (name.StartsWith("get_", StringComparison.Ordinal) || name.StartsWith("set_", StringComparison.Ordinal)))
        {
            name = name[4..];
        }

        return $"{baseDefinition.DeclaringType!.Name}.{name}";
    }

    /// <summary>The model's own type and every content base it extends, innermost first.</summary>
    private static IEnumerable<Type> ContentChain(Type model)
    {
        for (var type = model; type is not null && IsContent(type); type = type.BaseType)
        {
            yield return type;
        }
    }

    private static bool IsContent(Type type) =>
        type.Namespace?.StartsWith(ModelsNamespace + ".", StringComparison.Ordinal) == true;

    /// <summary>Every member of a model's content chain, the compiler-written types
    /// nested in each included.</summary>
    private static IReadOnlyList<MethodBase> OwnMembersOf(Type model) =>
        ContentChain(model)
            .SelectMany(type => MembersByOuter.Value.TryGetValue(type, out var members) ? members : [])
            .ToList();

    private static readonly Lazy<Dictionary<Type, List<MethodBase>>> MembersByOuter = new(() =>
        ChoiceEntryPoints.AllLoadedTypes
            .SelectMany(type => type.GetConstructors(Every).Concat<MethodBase>(type.GetMethods(Every)))
            .GroupBy(member => Outermost(member.DeclaringType!))
            .ToDictionary(group => group.Key, group => group.ToList()));

    private static Type Outermost(Type type)
    {
        while (type.DeclaringType is not null) type = type.DeclaringType;
        return type;
    }

    private static bool IsMock(Type type) =>
        type.Namespace?.Split('.').Any(segment => segment is "Mocks" or "Mock") == true;

    private static string RestOptionIdOf(Type option)
    {
        var getter = option.GetProperty(nameof(RestSiteOption.OptionId), Declared)?.GetGetMethod(nonPublic: true)
            ?? throw new InvalidOperationException(
                $"{option.FullName} declares no {nameof(RestSiteOption.OptionId)} of its own on this build.");
        var literals = ChoiceEntryPoints.StringLiterals(getter);
        return literals.Count == 1
            ? literals[0]
            : throw new InvalidOperationException(
                $"{option.FullName}.{nameof(RestSiteOption.OptionId)} loads " +
                $"{literals.Count.ToString(CultureInfo.InvariantCulture)} literal(s), " +
                "so it is not the constant this walk reads an option's id off.");
    }

    private static readonly Lazy<Dictionary<Type, string>> IdsByType = new(() =>
    {
        EngineHost.Start();
        return ModelDb.All.GroupBy(model => model.GetType()).ToDictionary(group => group.Key, group => group.First().Id.ToString());
    });

    private static string IdOf(Type model) =>
        IdsByType.Value.TryGetValue(model, out var id)
            ? id
            : throw new InvalidOperationException($"{model.FullName} is not a model the database registers on this build.");

    // ── What deals a relic, and which act reaches an event ────────────────────────

    /// <summary>One way a relic can be dealt to a player in normal play.</summary>
    /// <param name="Mechanism">The dealer: <c>starter</c>, <c>neow</c>, <c>grab-bag</c>
    /// (a chest, an elite's reward or a shop's relic shelf draws it by rarity),
    /// <c>shop</c>, <c>ancient</c>, <c>event</c>.</param>
    /// <param name="By">Who: the rarity bag, the ancient or the event that offers it, or
    /// null for the dealers that need no name.</param>
    public sealed record RelicDealing(string Mechanism, string? By)
    {
        public override string ToString() => By is null ? Mechanism : $"{Mechanism} {By}";
    }

    /// <summary>
    /// How a relic reaches a player, read from its rarity and from every event option
    /// that offers it: a common, uncommon or rare relic is drawn from the grab bag its
    /// rarity fills, a shop relic from a merchant's shelf, a starter is a character's
    /// own, and an ancient's or an event's relic is dealt by the ancient or the event
    /// whose options name it. A relic of event or ancient rarity no option names is
    /// dealt by that rarity's mechanism in a way the option walk cannot see, and is
    /// said to be.
    /// </summary>
    public static IReadOnlyList<RelicDealing> DealtBy(string relicId)
    {
        EngineHost.Start();
        var relic = ModelDb.AllRelics.FirstOrDefault(model => model.Id.ToString() == relicId)
            ?? throw new ArgumentException($"{relicId} is not a relic the database registers on this build.", nameof(relicId));
        var dealings = new List<RelicDealing>();
        switch (relic.Rarity)
        {
            case RelicRarity.Starter:
                dealings.Add(new RelicDealing("starter", null));
                break;
            case RelicRarity.Common or RelicRarity.Uncommon or RelicRarity.Rare:
                dealings.Add(new RelicDealing("grab-bag", relic.Rarity.ToString()));
                break;
            case RelicRarity.Shop:
                dealings.Add(new RelicDealing("shop", null));
                break;
        }

        foreach (var (eventId, key) in EventOptionKeys().Where(option => option.Key == relicId))
        {
            dealings.Add(new RelicDealing(
                eventId == DecisionFacts.NeowEventId ? "neow" : IsAncient(eventId) ? "ancient" : "event",
                eventId == DecisionFacts.NeowEventId ? null : eventId));
        }

        if (dealings.Count == 0)
        {
            dealings.Add(new RelicDealing(relic.Rarity.ToString().ToLowerInvariant(), "no option offers it"));
        }

        return dealings;
    }

    private static bool IsAncient(string eventId) =>
        ModelDb.AllAncients.Any(ancient => ancient.Id.ToString() == eventId);

    /// <summary>
    /// Whether a relic is allowed only in a run with another player in it: its own
    /// <c>IsAllowed</c> or <c>IsAllowedAtNeow</c> override, on its type or a base short
    /// of <c>RelicModel</c>, compares the run's player count as more than one - Massive
    /// Scroll's rule on this build, which is why Neow never offers it to the run the
    /// recorder records. Read off the IL the way a rest option's player-count branch
    /// is, so a build that lifts the rule shows here before a hunt for it fails.
    /// </summary>
    public static bool OfferedOnlyWithAnotherPlayer(string relicId)
    {
        EngineHost.Start();
        var relic = ModelDb.AllRelics.FirstOrDefault(model => model.Id.ToString() == relicId)
            ?? throw new ArgumentException($"{relicId} is not a relic the database registers on this build.", nameof(relicId));
        for (var type = relic.GetType(); type is not null && type != typeof(RelicModel); type = type.BaseType)
        {
            foreach (var name in new[] { nameof(RelicModel.IsAllowed), nameof(RelicModel.IsAllowedAtNeow) })
            {
                if (type.GetMethod(name, Declared) is { } rule && ChoiceEntryPoints.ComparesPlayerCountAsMoreThanOne(rule))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Every event and ancient an act can reach, by id: the act's own lists and the
    /// shared pool every act draws from, in the act's own order. An act id no act
    /// ships is refused by name.
    /// </summary>
    public static IReadOnlyList<string> ReachableIn(string actId)
    {
        EngineHost.Start();
        var act = ModelDb.Acts.FirstOrDefault(model => model.Id.ToString() == actId)
            ?? throw new ArgumentException($"{actId} is not an act the database ships on this build.", nameof(actId));
        return act.AllEvents.Concat(ModelDb.AllSharedEvents).Select(model => model.Id.ToString())
            .Concat(act.AllAncients.Concat(ModelDb.AllSharedAncients).Select(model => model.Id.ToString()))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The acts that reach an event, by id, in index order: every act for a
    /// shared event or ancient, none for an event no act lists.</summary>
    public static IReadOnlyList<string> ActsReaching(string eventId)
    {
        EngineHost.Start();
        return ModelDb.Acts
            .OrderBy(act => act.Index)
            .ThenBy(act => act.Id.ToString(), StringComparer.Ordinal)
            .Select(act => act.Id.ToString())
            .Where(actId => ReachableIn(actId).Contains(eventId, StringComparer.Ordinal))
            .ToList();
    }

    /// <summary>What the record says of a producer beside its id: how a relic is dealt,
    /// which acts reach an event; nothing for the rest.</summary>
    private static string ProducerNote(string producerId)
    {
        if (producerId.StartsWith("RELIC.", StringComparison.Ordinal))
        {
            return "dealt by " + string.Join(", ", DealtBy(producerId));
        }

        if (producerId == ArchitectEventId) return "reached by the win";
        if (producerId.StartsWith("EVENT.", StringComparison.Ordinal))
        {
            var acts = ActsReaching(producerId);
            return acts.Count == 0 ? "reached in no act" : "reached in " + string.Join(", ", acts);
        }

        return "";
    }

    // ── The committed record ──────────────────────────────────────────────────────

    /// <summary>Where the producer map on this build is committed, relative to the
    /// repository root.</summary>
    public const string ProducerMapRecordPath = "scripts/producer-map.txt";

    /// <summary>
    /// The committed producer map: every seam at every timing class, with the content
    /// that reaches it and what deals or reaches each producer, then the seams no
    /// content reaches. A claim about code, like the ledger, and a separate file from
    /// the coverage record for the reason <c>AGENTS.md</c> gives: which recordings
    /// reach a point and which code produces it are different facts.
    /// </summary>
    public static string ProducerMapRecord()
    {
        var text = new StringBuilder();
        text.AppendLine("# Every seam a decision reaches the game through, at the game hook the producing");
        text.AppendLine("# content runs in, with the models that reach it: walked off the game assembly by");
        text.AppendLine("# DecisionSurface and held to this file by DecisionSurfaceTests. A relic carries what");
        text.AppendLine("# deals it and an event which acts reach it, so a seed hunter knows what to look for.");
        text.AppendLine($"# The walk follows a model's own code {WalkDepth.ToString(CultureInfo.InvariantCulture)} calls deep; " +
                        $"'{UnrootedTiming}' is a seam reached from no hook the model overrides. Regenerate with");
        text.AppendLine("#   ./scripts/arbiter coverage --corpus manifests --update");
        text.AppendLine();
        text.AppendLine(
            $"# models walked: {ModelsWalked().ToString(CultureInfo.InvariantCulture)}; " +
            $"seams produced: {ProducerMap().Count.ToString(CultureInfo.InvariantCulture)}; " +
            $"producer edges: {ProducerMap().Sum(seam => seam.Producers.Count).ToString(CultureInfo.InvariantCulture)}");
        foreach (var seam in ProducerMap())
        {
            text.AppendLine();
            text.AppendLine($"{seam.Point.Identity}  [{seam.Producers.Count.ToString(CultureInfo.InvariantCulture)}]");
            foreach (var producer in seam.Producers)
            {
                var note = ProducerNote(producer);
                text.AppendLine(note.Length == 0 ? $"    {producer}" : $"    {producer}  {note}");
            }
        }

        text.AppendLine();
        text.AppendLine($"# seams no content reaches ({UnproducedSeams().Count.ToString(CultureInfo.InvariantCulture)})");
        foreach (var seam in UnproducedSeams())
        {
            text.AppendLine(seam);
        }

        return text.ToString();
    }

    // ── Which excusal is admissible where ─────────────────────────────────────────

    /// <summary>
    /// For every point of the denominator, the excusal classes the map admits: the
    /// derived classes the walks establish for it, <see cref="ExcusalClass.Generated"/>
    /// everywhere because a merge-gate row outranks any reason a point is unreached,
    /// and <see cref="ExcusalClass.NotOnTheRoute"/> only where nothing is derived,
    /// because a placeholder on a point nothing produces, nobody can replay or only a
    /// second player is offered would be a sentence about the wrong thing.
    /// </summary>
    public static IReadOnlyDictionary<DecisionPoint, IReadOnlySet<ExcusalClass>> AdmissibleExcusals(
        IReadOnlyList<DecisionPoint> denominator)
    {
        var admissible = new Dictionary<DecisionPoint, IReadOnlySet<ExcusalClass>>();
        foreach (var point in denominator)
        {
            var derived = DerivedExcusals(point).ToHashSet();
            var classes = new HashSet<ExcusalClass>(derived) { ExcusalClass.Generated };
            if (derived.Count == 0) classes.Add(ExcusalClass.NotOnTheRoute);
            admissible[point] = classes;
        }

        return admissible;
    }

    /// <summary>The derived classes for one point, each read off the build.</summary>
    public static IEnumerable<ExcusalClass> DerivedExcusals(DecisionPoint point)
    {
        switch (point.Kind)
        {
            case DecisionKinds.Verb:
                if (Enum.TryParse<ActionVerb>(point.Identity, out var verb))
                {
                    if (ScreenStandIns.StoodInFor.Any(standIn => standIn.Verb == verb)) yield return ExcusalClass.ScreenWithoutHeadlessHost;
                    if (RunDriver.OfferedOnlyWithAnotherPlayer.Contains(verb)) yield return ExcusalClass.MultiplayerOnly;
                    if (ScreenStandIns.StoodInFor.FirstOrDefault(standIn => standIn.Verb == verb) is { } stoodIn
                        && NothingReaches(stoodIn.Type, stoodIn.Member))
                    {
                        yield return ExcusalClass.NoProducerOnThisBuild;
                    }
                }

                break;
            case DecisionKinds.RewardKind:
                if (NothingConstructsARewardOfKind(point.Identity)) yield return ExcusalClass.NoProducerOnThisBuild;
                break;
            case DecisionKinds.RestOption:
                if (GeneratedForMoreThanOnePlayer(point.Identity)) yield return ExcusalClass.MultiplayerOnly;
                else if (NothingConstructsARestOption(point.Identity)) yield return ExcusalClass.NoProducerOnThisBuild;
                break;
            case DecisionKinds.CardRewardAlternative:
                if (!AlternativeEndsTheSelection(point.Identity)) yield return ExcusalClass.NotReplayable;
                break;
            case DecisionKinds.Event:
                if (point.Identity == ArchitectEventId) yield return ExcusalClass.ReachedByTheWin;
                else if (ActsReaching(point.Identity).Count == 0) yield return ExcusalClass.NoProducerOnThisBuild;
                break;
            case DecisionKinds.EventOption:
                var space = point.Identity.IndexOf(' ', StringComparison.Ordinal);
                var eventId = point.Identity[..space];
                var key = point.Identity[(space + 1)..];
                if (eventId == ArchitectEventId) yield return ExcusalClass.ReachedByTheWin;
                else if (eventId != DecisionFacts.NeowEventId && ActsReaching(eventId).Count == 0) yield return ExcusalClass.NoProducerOnThisBuild;
                if (TitleKeyedOptions(eventId).Contains(key, StringComparer.Ordinal)) yield return ExcusalClass.NotReplayable;

                // A blessing granting a relic the game allows only with another player
                // is never offered to the run the recorder records
                if (eventId == DecisionFacts.NeowEventId && key.StartsWith("RELIC.", StringComparison.Ordinal)
                    && OfferedOnlyWithAnotherPlayer(key))
                {
                    yield return ExcusalClass.MultiplayerOnly;
                }

                break;
            case DecisionKinds.Seam:
                if (ProducerMap().FirstOrDefault(row => row.Point == point) is not { } seam) break;

                // A seam answered nowhere but on screens the headless host stands in
                // for is one no generated walk can draw, whatever produces it
                if (seam.AnsweredAt.Count > 0
                    && seam.AnsweredAt.All(answer => answer.Kind == DecisionKinds.Verb
                                                     && Enum.TryParse<ActionVerb>(answer.Identity, out var answered)
                                                     && ScreenStandIns.StoodInFor.Any(standIn => standIn.Verb == answered)))
                {
                    yield return ExcusalClass.ScreenWithoutHeadlessHost;
                }

                if (seam.Producers.All(producer => producer == ArchitectEventId)) yield return ExcusalClass.ReachedByTheWin;
                break;
            case DecisionKinds.CardPrompt:
                var entryPoint = ChoiceEntryPoints.All().FirstOrDefault(method => ChoiceEntryPoints.QualifiedSignature(method) == point.Identity);
                if (entryPoint is not null && NothingReaches(entryPoint)) yield return ExcusalClass.NoProducerOnThisBuild;
                break;
        }
    }

    /// <summary>Whether nothing on this build reaches a prompt or screen member: no
    /// content produces its seam and no game code outside the command types and the
    /// mocks calls it.</summary>
    private static bool NothingReaches(Type type, string member)
    {
        var target = type.GetMethod(member, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            ?? throw new InvalidOperationException($"{type.Name}.{member} is not declared on this build.");
        return NothingReaches(target);
    }

    private static bool NothingReaches(MethodBase target)
    {
        var seam = SeamOf(target);
        var produced = seam is not null && ProducerMap().Any(row => row.Seam == seam);
        return !produced && ChoiceEntryPoints.ReachedBy(target).Count == 0;
    }

    private static bool NothingConstructsARewardOfKind(string kind) =>
        !ProducerMap().Any(row => row.Seam == $"reward-kind:{kind}")
        && ChoiceEntryPoints.AllLoadedTypes
            .Where(type => !type.IsAbstract && type.IsSubclassOf(typeof(Reward)) && LootRewards.KindOf(type) == kind)
            .All(type => !EngineConstructs(type));

    private static bool NothingConstructsARestOption(string optionId) =>
        !ProducerMap().Any(row => row.Seam == $"rest-option:{optionId}")
        && ChoiceEntryPoints.AllLoadedTypes
            .Where(type => !type.IsAbstract && type.IsSubclassOf(typeof(RestSiteOption)) && RestOptionIdOf(type) == optionId)
            .All(type => !EngineConstructs(type));

    /// <summary>Whether any game member outside the scene tree, the mocks and the
    /// content models constructs a type: the engine's own paths to it.</summary>
    private static bool EngineConstructs(Type type) =>
        ChoiceEntryPoints.GameMembersConstructing(type)
            .Select(method => Outermost(method.DeclaringType!))
            .Any(outer => !IsMock(outer)
                          && outer.Namespace?.Contains(".Nodes", StringComparison.Ordinal) != true
                          && !outer.IsSubclassOf(typeof(AbstractModel)));

    /// <summary>
    /// Whether <c>RestSiteOption.Generate</c> constructs a rest option only after it has
    /// read the run's player count: the game's own branch for the mend, read off the
    /// body's order - the options constructed before the count is read are every run's,
    /// and one constructed after it is the branch's.
    /// </summary>
    private static bool GeneratedForMoreThanOnePlayer(string optionId)
    {
        var generate = typeof(RestSiteOption).GetMethod(nameof(RestSiteOption.Generate), BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("RestSiteOption.Generate is not declared on this build.");
        var readThePlayers = false;
        foreach (var callee in ChoiceEntryPoints.Callees(generate))
        {
            if (callee.Name == "get_Players") readThePlayers = true;
            if (callee.IsConstructor && callee.DeclaringType is { } option && option.IsSubclassOf(typeof(RestSiteOption))
                && RestOptionIdOf(option) == optionId)
            {
                return readThePlayers;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether an alternative's after-action ends the selection, by the driver's own
    /// rule, read off the enum each construction of a <c>CardRewardAlternative</c>
    /// passes: the constant loaded last before the construction, in
    /// <c>CardRewardAlternative.Generate</c> and in every override that adds one.
    /// </summary>
    private static bool AlternativeEndsTheSelection(string optionId)
    {
        foreach (var method in AlternativeConstructors())
        {
            foreach (var construction in ChoiceEntryPoints.ConstructionsIn(method, typeof(CardRewardAlternative)))
            {
                if (construction.Literal == optionId && construction.Constant is { } constant)
                {
                    return ManifestCardSelector.EndsTheSelection((PostAlternateCardRewardAction)constant);
                }
            }
        }

        throw new InvalidOperationException($"No construction of card-reward alternative '{optionId}' carries an after-action on this build.");
    }

    private static IEnumerable<MethodBase> AlternativeConstructors()
    {
        var generate = typeof(CardRewardAlternative).GetMethod(nameof(CardRewardAlternative.Generate), BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("CardRewardAlternative.Generate is not declared on this build.");
        var modify = typeof(AbstractModel).GetMethod(nameof(AbstractModel.TryModifyCardRewardAlternatives), Declared)
            ?? throw new InvalidOperationException("AbstractModel.TryModifyCardRewardAlternatives is not declared on this build.");
        yield return generate;
        foreach (var method in ChoiceEntryPoints.AllLoadedTypes
                     .Where(type => type.IsSubclassOf(typeof(AbstractModel)))
                     .Select(type => type.GetMethod(modify.Name, Declared))
                     .OfType<MethodInfo>()
                     .Where(method => method.GetBaseDefinition() == modify.GetBaseDefinition())
                     .OrderBy(method => method.DeclaringType!.FullName, StringComparer.Ordinal))
        {
            yield return method;
        }
    }
}

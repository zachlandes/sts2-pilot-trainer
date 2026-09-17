using System.Globalization;
using System.Reflection;
using System.Text;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Models;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Every decision point this build can offer, walked off the game assembly: the
/// denominator of the coverage number, produced by reading the game rather than
/// written by anybody.
///
/// One walk per kind, each returning identities in a stable order, and each reading
/// the game the way the recorder or the driver reads the thing it enumerates - a
/// reward class through <see cref="LootRewards.KindOf(Type)"/>, a shelf the way the
/// recorder's purchase patch maps a merchant entry to one, an alternative through
/// the literal <c>CardRewardAlternative.Generate</c> and the one relic that adds to
/// it construct one with, a rest option through the constant its <c>OptionId</c>
/// getter returns.
/// A class the mapping does not claim comes back under its own type name, which is
/// the walk saying the format lacks a kind rather than hiding one.
///
/// The IL is read the way <see cref="ChoiceEntryPoints"/> reads it - reflection over
/// the loaded assembly, against the vendored stubs - rather than through a second IL
/// reader; a body the stubs cannot resolve refuses by name rather than thinning the
/// count. Every walk here loads the engine's assembly first, for the reason
/// <see cref="ChoiceEntryPoints"/> gives, and the event walk starts the engine,
/// because event ids live in the model database and nowhere in the type table.
/// </summary>
public static partial class DecisionSurface
{
    private const BindingFlags Declared =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    static DecisionSurface()
    {
        _ = EngineHost.StartupPhase();
    }

    /// <summary>Every point of every kind, in <see cref="DecisionKinds.All"/>'s order.</summary>
    public static IReadOnlyList<DecisionPoint> All() =>
        DecisionKinds.All
            .SelectMany(kind => Identities(kind).Select(identity => new DecisionPoint(kind, identity)))
            .ToList();

    /// <summary>The identities one kind offers on this build.</summary>
    public static IReadOnlyList<string> Identities(string kind) => kind switch
    {
        DecisionKinds.Verb => Verbs(),
        DecisionKinds.RewardKind => RewardKinds(),
        DecisionKinds.CardRewardAlternative => CardRewardAlternatives(),
        DecisionKinds.ShopKind => ShopKinds(),
        DecisionKinds.RestOption => RestOptions(),
        DecisionKinds.Event => Events(),
        DecisionKinds.EventOption => EventOptions(),
        DecisionKinds.Seam => Seams(),
        DecisionKinds.CardPrompt => CardPrompts(),
        DecisionKinds.NetAction => NetActions(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a decision kind"),
    };

    /// <summary>Every verb the format names that this build maps, in the enum's order;
    /// a verb the table excuses is not a decision anybody can make here.</summary>
    public static IReadOnlyList<string> Verbs() =>
        Enum.GetValues<ActionVerb>()
            .Where(EngineCommands.Maps)
            .Select(verb => verb.ToString())
            .ToList();

    /// <summary>Every concrete reward class, named the way the loot screen names it.</summary>
    public static IReadOnlyList<string> RewardKinds() =>
        ConcreteSubclassesOf(typeof(Reward)).Select(LootRewards.KindOf).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// Every alternative a card reward can offer past its cards: the literals
    /// <c>CardRewardAlternative.Generate</c> constructs, then the literals every
    /// <c>TryModifyCardRewardAlternatives</c> override adds, in that order. That is
    /// the list the recorder's <c>option_id</c> is drawn from, and a build that adds
    /// a relic with a fourth answer shows here before a recording ever meets it.
    /// </summary>
    public static IReadOnlyList<string> CardRewardAlternatives()
    {
        var generate = typeof(CardRewardAlternative).GetMethod(
            nameof(CardRewardAlternative.Generate), BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("CardRewardAlternative.Generate is not declared on this build.");
        var modify = typeof(AbstractModel).GetMethod(nameof(AbstractModel.TryModifyCardRewardAlternatives), Declared)
            ?? throw new InvalidOperationException(
                "AbstractModel.TryModifyCardRewardAlternatives is not declared on this build.");

        var overrides = ChoiceEntryPoints.AllLoadedTypes
            .Where(type => type.IsSubclassOf(typeof(AbstractModel)))
            .Select(type => type.GetMethod(modify.Name, Declared))
            .OfType<MethodInfo>()
            .Where(method => method.GetBaseDefinition() == modify.GetBaseDefinition())
            .OrderBy(method => method.DeclaringType!.FullName, StringComparer.Ordinal);

        return ChoiceEntryPoints.LiteralsConstructing(generate, typeof(CardRewardAlternative))
            .Concat(overrides.SelectMany(method =>
                ChoiceEntryPoints.LiteralsConstructing(method, typeof(CardRewardAlternative))))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Every shelf a merchant entry can be bought off, from the entry classes this
    /// build declares, mapped the way the recorder's purchase patch maps one: a card
    /// entry is on either card shelf, the format's two kinds for it; a relic, a potion
    /// and the removal service are one each. Any other entry class is named by its
    /// type, a shelf the format lacks.
    /// </summary>
    public static IReadOnlyList<string> ShopKinds() =>
        ConcreteSubclassesOf(typeof(MerchantEntry))
            .SelectMany(type =>
                typeof(MerchantCardEntry).IsAssignableFrom(type)
                    ? [ShopPurchaseKinds.CharacterCard, ShopPurchaseKinds.ColorlessCard]
                    : typeof(MerchantRelicEntry).IsAssignableFrom(type) ? [ShopPurchaseKinds.Relic]
                    : typeof(MerchantPotionEntry).IsAssignableFrom(type) ? [ShopPurchaseKinds.Potion]
                    : typeof(MerchantCardRemovalEntry).IsAssignableFrom(type) ? [ShopPurchaseKinds.CardRemoval]
                    : new[] { type.Name })
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Every rest-site option, by the id its own <c>OptionId</c> returns.</summary>
    public static IReadOnlyList<string> RestOptions() =>
        ConcreteSubclassesOf(typeof(RestSiteOption))
            .Select(type =>
            {
                var getter = type.GetProperty(nameof(RestSiteOption.OptionId), Declared)?.GetGetMethod(nonPublic: true)
                    ?? throw new InvalidOperationException(
                        $"{type.FullName} declares no {nameof(RestSiteOption.OptionId)} of its own on this build.");
                var literals = ChoiceEntryPoints.StringLiterals(getter);
                return literals.Count == 1
                    ? literals[0]
                    : throw new InvalidOperationException(
                        $"{type.FullName}.{nameof(RestSiteOption.OptionId)} loads " +
                        $"{literals.Count.ToString(CultureInfo.InvariantCulture)} literal(s), " +
                        "so it is not the constant this walk reads an option's id off.");
            })
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Every event this build ships, by the id a recording names it with: the events
    /// the acts and the shared pool deal, and the ancients each act rolls one of, which
    /// the model database keeps apart from its events and a recording does not - an
    /// ancient's page is answered through the same <c>ChooseEventOption</c>, naming
    /// the ancient's id. The first release measurement found Orobas in a store
    /// recording and in no walk, which is how the ancients came to be here. Neow is
    /// the one ancient left out, by name: every Neow decision is the recorder's
    /// <c>ChooseNeowBlessing</c>, a verb of its own carrying no event id, so the point
    /// it is counted under is that verb and an event point for it would be one no
    /// recording can ever project. The two lists are concatenated and not
    /// de-duplicated: an event and an ancient shipped under one id on a later build
    /// are two points with one name, which fails <c>DecisionSurfaceTests</c> by count
    /// rather than being folded into one and thinning the denominator silently.
    /// </summary>
    public static IReadOnlyList<string> Events()
    {
        EngineHost.Start();
        return ModelDb.AllEvents.Select(model => model.Id.ToString())
            .Concat(ModelDb.AllAncients.Where(model => model is not Neow).Select(model => model.Id.ToString()))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Every card or relic prompt entry point, by its qualified signature.</summary>
    public static IReadOnlyList<string> CardPrompts() =>
        ChoiceEntryPoints.All().Select(ChoiceEntryPoints.QualifiedSignature).ToList();

    /// <summary>
    /// Every game action a locally issued net action becomes, read off each net
    /// action's own <c>ToGameAction</c>: the type it constructs there. A net action
    /// whose body constructs no game action, or more than one, is refused by name.
    /// </summary>
    public static IReadOnlyList<string> NetActions() =>
        INetActionSubtypes.All
            .Select(netAction =>
            {
                var toGameAction = netAction.GetMethod("ToGameAction", BindingFlags.Instance | BindingFlags.Public)
                    ?? throw new InvalidOperationException(
                        $"{netAction.FullName} declares no ToGameAction on this build.");
                var constructed = ChoiceEntryPoints.Callees(toGameAction)
                    .Where(callee => callee.IsConstructor && callee.DeclaringType?.IsSubclassOf(typeof(GameAction)) == true)
                    .Select(callee => callee.DeclaringType!.Name)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                return constructed.Count == 1
                    ? constructed[0]
                    : throw new InvalidOperationException(
                        $"{netAction.FullName}.ToGameAction constructs " +
                        $"{constructed.Count.ToString(CultureInfo.InvariantCulture)} game " +
                        "action type(s), so this walk cannot say which one it becomes.");
            })
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>Where the denominator and the excusals on this build are committed,
    /// relative to the repository root.</summary>
    public const string RecordPath = "scripts/decision-coverage.txt";

    /// <summary>
    /// The committed record: every point the walks produce on this build, one per line
    /// under its kind, with its excusal and the excusal's class where it has one. No
    /// counts, because the record is about what the build offers and a count is about
    /// which recordings happen to be committed. Regenerated by
    /// <c>./scripts/arbiter coverage --update</c> beside <see cref="ProducerMapRecord"/>
    /// and held by <c>DecisionSurfaceTests</c>.
    /// </summary>
    public static string Record(IReadOnlyList<DecisionPoint> denominator)
    {
        var text = new StringBuilder();
        text.AppendLine("# Every decision point this build can offer, walked off the game assembly by");
        text.AppendLine("# DecisionSurface and held to this file by DecisionSurfaceTests: the denominator of");
        text.AppendLine("# ./scripts/arbiter coverage, with the excusal DecisionExcusals gives a point no");
        text.AppendLine("# committed recording reaches, in the class the map admits for it. A game update that");
        text.AppendLine("# adds or removes a point changes this file in the change that adopts it. Regenerate with");
        text.AppendLine("#   ./scripts/arbiter coverage --corpus manifests --update");
        foreach (var kind in DecisionKinds.All)
        {
            var points = denominator.Where(point => point.Kind == kind).ToList();
            text.AppendLine();
            text.AppendLine(
                $"# {kind} ({points.Count.ToString(CultureInfo.InvariantCulture)})" +
                (DecisionKinds.NotProjectableBecause(kind) is { } why ? $": {why}" : ""));
            foreach (var point in points)
            {
                text.AppendLine(
                    DecisionExcusals.All.TryGetValue(point, out var excuse)
                        ? $"{point.Identity}  {excuse.Describe()}"
                        : point.Identity);
            }
        }

        return text.ToString();
    }

    // ── The ledger's kinds: what the recorder has to watch, beyond what a recording counts ──

    /// <summary>The kinds <see cref="DecisionLedger"/> holds the recorder's account to,
    /// in the order the ledger lists them: each is a way a decision reaches the game
    /// that a build can add to without touching a member the recorder patches.</summary>
    public static readonly string[] LedgerKinds = ["net-action", "player-choice", "message", "overlay-screen", "room"];

    /// <summary>
    /// Every choice the client syncs, as (kind, member): each member whose body calls
    /// <c>PlayerChoiceSynchronizer.SyncLocalChoice</c>, crossed with the kinds the
    /// results that body constructs carry - read off which <c>PlayerChoiceResult.From*</c>
    /// factory it calls, since the kind is the factory's and never a runtime value on
    /// this build. A caller that constructs through a factory this reading does not
    /// know - the kind-taking <c>FromCards</c> among them - is listed under that factory's name, and one whose
    /// own body constructs no result at all under <see cref="UnreadChoiceKind"/>, so
    /// that neither is dropped: each is a candidate no row can claim until somebody
    /// has read it. Walked once per process, since the assembly does not change under it.
    /// </summary>
    public static IReadOnlyList<(string Kind, MethodBase Member)> PlayerChoiceSites() => PlayerChoiceSiteWalk.Value;

    /// <summary>The kind a synced choice is listed under when the member syncing it
    /// constructs its result nowhere this walk can read.</summary>
    public const string UnreadChoiceKind = "?";

    private static readonly Lazy<IReadOnlyList<(string Kind, MethodBase Member)>> PlayerChoiceSiteWalk = new(() =>
    {
        var sync = typeof(PlayerChoiceSynchronizer).GetMethod(
            nameof(PlayerChoiceSynchronizer.SyncLocalChoice), BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException("PlayerChoiceSynchronizer.SyncLocalChoice is not declared on this build.");
        return ChoiceEntryPoints.CallSites(callee => callee == sync)
            .Select(site => site.Caller)
            .Distinct()
            .SelectMany(caller => ChoiceKindsConstructedBy(caller).Select(kind => (Kind: kind, Member: caller)))
            .OrderBy(site => PlayerChoiceIdentity(site.Kind, site.Member), StringComparer.Ordinal)
            .ToList();
    });

    /// <summary>How a synced choice is named in the ledger: its kind at the member that syncs it.</summary>
    public static string PlayerChoiceIdentity(string kind, MethodBase member) =>
        $"{kind} @ {member.DeclaringType!.Name}.{EntryPointSignature.Of(member)}";

    private static IEnumerable<string> ChoiceKindsConstructedBy(MethodBase caller)
    {
        var factories = ChoiceEntryPoints.OwnCalleesOf(caller)
            .Where(callee => callee.DeclaringType == typeof(PlayerChoiceResult) && callee.IsStatic)
            .Select(callee => callee.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (factories.Count == 0)
        {
            yield return UnreadChoiceKind;
            yield break;
        }

        foreach (var factory in factories)
        {
            switch (factory)
            {
                case "FromIndex" or "FromIndexes":
                    yield return nameof(PlayerChoiceType.Index);
                    break;
                case "FromPlayerId":
                    yield return nameof(PlayerChoiceType.Player);
                    break;
                case "FromCanonicalCard" or "FromCanonicalCards":
                    yield return nameof(PlayerChoiceType.CanonicalCard);
                    break;
                case "FromMutableCombatCard" or "FromMutableCombatCards":
                    yield return nameof(PlayerChoiceType.CombatCard);
                    break;
                case "FromMutableDeckCard" or "FromMutableDeckCards":
                    yield return nameof(PlayerChoiceType.DeckCard);
                    break;
                case "FromMutableCard" or "FromMutableCards":
                    yield return nameof(PlayerChoiceType.MutableCard);
                    break;
                default:
                    yield return factory;
                    break;
            }
        }
    }

    /// <summary>
    /// Every message the client sends, as (message, sender): each member whose body
    /// calls <c>SendMessage</c> on the <c>INetGameService</c> interface or on a service
    /// that implements it - a lobby member holding the host service by its own type
    /// calls the class's method, and the IL names that one - with the message type it
    /// sends read off the generic argument of the call. A call whose argument is still
    /// open - a service forwarding one overload to another as <c>T</c> - sends nothing
    /// of its own and is left out; a service sending a named message is a sender like
    /// any other. Walked once per process.
    /// </summary>
    public static IReadOnlyList<(Type Message, MethodBase Sender)> MessageSites() => MessageSiteWalk.Value;

    private static readonly Lazy<IReadOnlyList<(Type Message, MethodBase Sender)>> MessageSiteWalk = new(() =>
        ChoiceEntryPoints.CallSites(callee =>
                callee.Name == nameof(INetGameService.SendMessage) && callee.IsGenericMethod &&
                typeof(INetGameService).IsAssignableFrom(callee.DeclaringType))
            .Select(site => (Message: site.Callee.GetGenericArguments()[0], Sender: site.Caller))
            .Where(site => !site.Message.IsGenericParameter)
            .Distinct()
            .OrderBy(site => MessageIdentity(site.Message, site.Sender), StringComparer.Ordinal)
            .ToList());

    /// <summary>How a sent message is named in the ledger: its type and the member that sends it.</summary>
    public static string MessageIdentity(Type message, MethodBase sender) =>
        $"{message.Name} <- {sender.DeclaringType!.Name}.{EntryPointSignature.Of(sender)}";

    /// <summary>
    /// Every type the message and synced-choice walks produced a caller from that has
    /// a body the IL reader could not read whole, with how many, by full name: a send
    /// or a sync inside one of those bodies is a candidate neither walk can produce, so
    /// the ledger carries the set and excuses each in writing. It is the part of the
    /// whole unreadable set the walks can point at; every other type on
    /// <see cref="UnreadableBodiesRecord"/> is a place a send or sync could hide too,
    /// and that record's diff is the guard for those.
    /// </summary>
    public static IReadOnlyList<(string Type, int Bodies)> UnreadableLedgerBodies() =>
        ChoiceEntryPoints.UnreadableBodiesAmong(
                PlayerChoiceSites().Select(site => site.Member).Concat(MessageSites().Select(site => site.Sender)))
            .Select(entry => (entry.Type.FullName!, entry.Bodies))
            .ToList();

    /// <summary>Every type of the game assembly the runtime could not load against the
    /// stubs, by full name: none of its bodies was walked, so none of its sends or syncs
    /// can be a candidate, and the ledger carries the set for the same reason.</summary>
    public static IReadOnlyList<string> UnloadableTypes() => ChoiceEntryPoints.UnloadableTypes();

    /// <summary>How many types on this build have a body the IL reader could not read whole.</summary>
    public static int UnreadableTypeCount() => ChoiceEntryPoints.UnreadableBodies().Count;

    /// <summary>The whole unreadable set on this build - every type with a body the IL
    /// reader could not read whole, and every type the runtime could not load - as the
    /// text committed at <see cref="UnreadableBodiesRecordPath"/>.</summary>
    public static string UnreadableBodiesRecord() => ChoiceEntryPoints.UnreadableBodiesRecord();

    /// <summary>Where that record is committed, relative to the repository root.</summary>
    public const string UnreadableBodiesRecordPath = ChoiceEntryPoints.UnreadableBodiesRecordPath;

    /// <summary>Every overlay screen this build draws. Walked once per process.</summary>
    public static IReadOnlyList<Type> OverlayScreenTypes() => OverlayScreenWalk.Value;

    private static readonly Lazy<IReadOnlyList<Type>> OverlayScreenWalk = new(() =>
        ChoiceEntryPoints.AllLoadedTypes
            .Where(type => !type.IsAbstract && !type.IsInterface && typeof(IOverlayScreen).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList());

    /// <summary>Every room type the map can deal and every room class the run can
    /// stand in, each by name. Walked once per process.</summary>
    public static IReadOnlyList<string> Rooms() => RoomWalk.Value;

    private static readonly Lazy<IReadOnlyList<string>> RoomWalk = new(() =>
        Enum.GetNames<RoomType>().Select(name => $"RoomType.{name}")
            .Concat(ConcreteSubclassesOf(typeof(AbstractRoom)).Select(type => type.Name))
            .ToList());

    private static IReadOnlyList<Type> ConcreteSubclassesOf(Type baseType) =>
        ChoiceEntryPoints.AllLoadedTypes
            .Where(type => !type.IsAbstract && type.IsSubclassOf(baseType))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
}

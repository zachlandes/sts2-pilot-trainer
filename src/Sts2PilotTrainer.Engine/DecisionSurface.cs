using System.Globalization;
using System.Reflection;
using System.Text;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
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
public static class DecisionSurface
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

    /// <summary>Every event this build ships, by the id a recording names it with.</summary>
    public static IReadOnlyList<string> Events()
    {
        EngineHost.Start();
        return ModelDb.AllEvents.Select(model => model.Id.ToString()).Order(StringComparer.Ordinal).ToList();
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
    /// under its kind, with its excusal where it has one. No counts, because the record
    /// is about what the build offers and a count is about which recordings happen to
    /// be committed. Regenerated by <c>./scripts/arbiter coverage --update</c> and held
    /// by <c>DecisionSurfaceTests</c>.
    /// </summary>
    public static string Record(IReadOnlyList<DecisionPoint> denominator)
    {
        var text = new StringBuilder();
        text.AppendLine("# Every decision point this build can offer, walked off the game assembly by");
        text.AppendLine("# DecisionSurface and held to this file by DecisionSurfaceTests: the denominator of");
        text.AppendLine("# ./scripts/arbiter coverage, with the excusal DecisionExcusals gives a point no");
        text.AppendLine("# committed recording reaches. A game update that adds or removes a point changes");
        text.AppendLine("# this file in the change that adopts it. Regenerate with");
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
                        ? $"{point.Identity}  excused: {excuse}"
                        : point.Identity);
            }
        }

        return text.ToString();
    }

    private static IReadOnlyList<Type> ConcreteSubclassesOf(Type baseType) =>
        ChoiceEntryPoints.AllLoadedTypes
            .Where(type => !type.IsAbstract && type.IsSubclassOf(baseType))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
}

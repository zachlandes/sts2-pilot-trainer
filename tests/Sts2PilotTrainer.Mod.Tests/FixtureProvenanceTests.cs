using System.Reflection;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Rewards;
using Sts2PilotTrainer.Engine;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// A test that asserts how an answer is handled obtains the list it answers from the
/// engine's own producer, never from a literal written in the test.
///
/// The card-reward Skip defect was covered by a test: it wrote the alternative list
/// by hand as the one alternative the handler happened to expect, so the handler was
/// held to a list the game never offers and passed for as long as the defect lived.
/// The rule is in AGENTS.md's card-prompt paragraph; this is the lint that keeps it,
/// over this suite's compiled IL through the engine's one reader rather than over
/// its source, because a construction has more than one spelling in C# - the fixture
/// the defect lived behind was a target-typed <c>new(...)</c> inside a list
/// initializer - and every spelling is one <c>newobj</c>. What it proves is exactly
/// that: no test in this assembly constructs an alternative or a creation result,
/// except the types named below with their reason.
///
/// An alternative is constructed by <c>CardRewardAlternative.Generate</c> and the
/// relics that add to it, and by nothing in this repository's tests. A creation
/// result is what the engine hands its seam; a test that builds one is allowed only
/// where the list is the engine entry point's input rather than the list a handler
/// is held to.
/// </summary>
public sealed class FixtureProvenanceTests
{
    /// <summary>Test types allowed to construct a creation result, each with the reason
    /// it is the engine's input and not an offered list somebody wrote.</summary>
    private static readonly IReadOnlyDictionary<string, string> CreationResultTypesAllowed =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CardPromptOfferTests"] =
                "the results are the argument handed to CardSelectCmd.FromSimpleGridForRewards, the way the " +
                "game's own callers hand it one, and the test asserts what the entry point then offers",
        };

    [GameFact]
    public void NoTestConstructsACardRewardAlternative()
    {
        var offenders = Offenders(() => typeof(CardRewardAlternative)).ToList();

        Assert.True(
            offenders.Count == 0,
            "An alternative is produced by CardRewardAlternative.Generate and the relics that add to it, never " +
            "written in a test; obtain the list from the engine at a real card reward (CardRewardAlternativeTests " +
            "shows how):\n" + string.Join("\n", offenders));
    }

    [GameFact]
    public void ACreationResultIsConstructedOnlyWhereItIsTheEnginesInput()
    {
        var offenders = Offenders(() => typeof(CardCreationResult))
            .Where(site => !CreationResultTypesAllowed.ContainsKey(site.Owner.Name))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A creation result is what the engine hands its seam; a test that answers a list of them obtains it " +
            "from the reward the engine put on the loot screen. A type that is the entry point's own input is " +
            "named in CreationResultTypesAllowed with its reason:\n" + string.Join("\n", offenders));
    }

    /// <summary>An allow-list entry that no site needs any more is a stale excuse.</summary>
    [GameFact]
    public void EveryAllowedTypeStillConstructsOne()
    {
        var owners = Offenders(() => typeof(CardCreationResult)).Select(site => site.Owner.Name).ToHashSet(StringComparer.Ordinal);

        Assert.All(CreationResultTypesAllowed.Keys, type => Assert.Contains(type, owners));
    }

    /// <summary>
    /// The spelling the defect's fixture used, held to be seen: the reader finds
    /// exactly the site below, so a reader that stopped seeing a target-typed
    /// <c>new(...)</c> in a list initializer fails here rather than as a lint that
    /// passes on the fixture it exists to refuse.
    /// </summary>
    [GameFact]
    public void TheReaderSeesATargetTypedConstructionInAListInitializer()
    {
        var sites = Sites(() => typeof(CardRewardAlternative))
            .Where(site => site.Owner == typeof(FixtureProvenanceTests))
            .ToList();

        Assert.Equal([$"{nameof(FixtureProvenanceTests)}.{nameof(AHandWrittenAlternativeList)}"], sites.Select(site => site.ToString()));
    }

    private static List<CardRewardAlternative> AHandWrittenAlternativeList() =>
        [new("SACRIFICE", PostAlternateCardRewardAction.EndSelectionAndCompleteReward)];

    private static IEnumerable<Site> Offenders(Func<Type> constructed) =>
        Sites(constructed).Where(site => site.Owner != typeof(FixtureProvenanceTests));

    /// <summary>The type is named in a function rather than passed, because the
    /// engine's resolver is what says where the game is and is installed only once
    /// the engine has been touched; a test body that names a game type before that
    /// cannot be prepared.</summary>
    private static IEnumerable<Site> Sites(Func<Type> constructed)
    {
        _ = ChoiceEntryPoints.Game;
        return ChoiceEntryPoints.MethodsConstructing(typeof(FixtureProvenanceTests).Assembly, constructed())
            .Select(method => new Site(Owner(method), method))
            .OrderBy(site => site.ToString(), StringComparer.Ordinal);
    }

    /// <summary>The test type a method belongs to: a compiler-written closure or state
    /// machine is attributed to the type the test that wrote it is declared in.</summary>
    private static Type Owner(MethodBase method)
    {
        var type = method.DeclaringType ?? throw new InvalidOperationException($"{method.Name} is declared in no type.");
        while (type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) && type.DeclaringType is { } outer) type = outer;
        return type;
    }

    private sealed record Site(Type Owner, MethodBase Method)
    {
        public override string ToString() => $"{Owner.Name}.{Method.Name}";
    }
}

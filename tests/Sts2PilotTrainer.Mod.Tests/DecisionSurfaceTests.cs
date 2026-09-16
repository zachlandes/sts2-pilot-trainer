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

    [GameFact]
    public void TheEventsAreEveryEventTheModelDatabaseShips()
    {
        var events = DecisionSurface.Events();

        Assert.Equal(57, events.Count);
        Assert.All(events, id => Assert.StartsWith("EVENT.", id, StringComparison.Ordinal));
        Assert.Contains("EVENT.WATERLOGGED_SCRIPTORIUM", events);
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

        Assert.Equal(131, all.Count);
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
        Assert.All(DecisionExcusals.All.Values, reason => Assert.False(string.IsNullOrWhiteSpace(reason)));
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
}

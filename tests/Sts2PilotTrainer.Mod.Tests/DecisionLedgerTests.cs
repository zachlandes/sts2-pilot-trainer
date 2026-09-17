using Sts2PilotTrainer.Engine;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The recorder's account of every way a decision can reach the game on this build,
/// held to the walks and to the committed ledger.
///
/// The walks are the candidates; the command table's observations and the written
/// excusals are the claims; a candidate accounted for neither way is what a game
/// update leaves behind, and <c>EngineCommands.Verify</c> refuses it. This holds the
/// counts to v0.111.0, the ledger to the file, and the classification to its two
/// answers on a candidate nothing claims.
/// </summary>
public sealed class DecisionLedgerTests
{
    [GameFact]
    public void EveryCandidateOnThisBuildIsClaimedOrExcusedAndTheTableVerifies()
    {
        var entries = DecisionLedger.Entries();

        Assert.All(entries, entry => Assert.True(entry.IsClassified, entry.Describe()));
        Assert.Empty(DecisionLedger.StaleExcusals());
        Assert.Empty(EngineCommands.Verify());
    }

    [GameFact]
    public void TheCandidatesAreTheOnesTheDecompiledBuildShows()
    {
        var counts = DecisionSurface.LedgerKinds.ToDictionary(kind => kind, kind => DecisionLedger.Candidates(kind).Count);

        Assert.Equal(11, counts["net-action"]);
        Assert.Equal(14, counts["player-choice"]);
        Assert.Equal(61, counts["message"]);
        Assert.Equal(13, counts["overlay-screen"]);
        Assert.Equal(15, counts["room"]);
    }

    /// <summary>A candidate no row observes and no excusal names is UNCLASSIFIED, which
    /// is the answer a game update's new screen or message gets before anybody has
    /// looked at it; a claimed one names its rows.</summary>
    [GameFact]
    public void ACandidateNothingClaimsIsUnclassifiedAndAClaimedOneNamesItsRows()
    {
        var stranger = DecisionLedger.Classify(new LedgerCandidate("overlay-screen", "NSomethingNew", typeof(object)));
        Assert.False(stranger.IsClassified);
        Assert.Equal(DecisionLedger.Unclassified, stranger.Status);

        var rewards = DecisionLedger.Candidates("overlay-screen").Single(candidate => candidate.Identity == "NRewardsScreen");
        var claimed = DecisionLedger.Classify(rewards);
        Assert.Equal(DecisionLedger.Claimed, claimed.Status);
        Assert.Equal("ClaimReward, TakeCard, SkipRewards", claimed.Account);

        var ending = DecisionLedger.Classify(DecisionLedger.Candidates("overlay-screen").Single(candidate => candidate.Identity == "NGameOverScreen"));
        Assert.Equal(DecisionLedger.ExcusedStatus, ending.Status);
        Assert.StartsWith("ending: ", ending.Account, StringComparison.Ordinal);
    }

    /// <summary>Every synced choice on this build is a card kind, an index or a
    /// player, and every card prompt entry point syncs exactly the kind its own
    /// factory constructs.</summary>
    [GameFact]
    public void EverySyncedChoiceIsNamedByTheKindItsFactoryConstructs()
    {
        var choices = DecisionSurface.PlayerChoices();

        Assert.Contains("CombatCard @ CardSelectCmd.FromHand(context, player, prefs, filter, source)", choices);
        Assert.Contains("DeckCard @ CardSelectCmd.FromDeckForUpgrade(player, prefs)", choices);
        Assert.Contains("Index @ CardReward.OnSelect()", choices);
        Assert.Contains("Player @ MendRestSiteOption.OnSelect()", choices);
        Assert.DoesNotContain(choices, choice => choice.StartsWith("None @", StringComparison.Ordinal));
    }

    [GameFact]
    public void TheCommittedLedgerIsWhatTheWalksAndTheTableProduce()
    {
        var path = Path.Combine(Arbiter.RepoRoot, DecisionLedger.RecordPath);
        var actual = DecisionLedger.Record();
        var recorded = File.Exists(path) ? File.ReadAllText(path) : null;

        Assert.True(
            recorded == actual,
            "The ways a decision can reach the game on this build, or how each is accounted for, are not the " +
            "recorded ones. If the game build changed or a row's observations moved, regenerate the ledger in the " +
            "same change:\n\n    ./scripts/arbiter engine-commands --update\n\n" +
            $"Recorded in {path}:\n{recorded ?? "(no file)"}\nThis build:\n{actual}");
    }
}

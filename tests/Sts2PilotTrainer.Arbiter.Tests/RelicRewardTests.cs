using System.Text.RegularExpressions;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// A relic claimed off a loot screen, driven against the real engine.
///
/// The whole-act fixture beats an elite and declines the relic its loot offers, so
/// the loot screen that fight puts up is the one committed history with a relic on
/// it. The claim is found by kind and checked by id, as every other pick is: a wrong
/// id names what was on offer instead, and the right one leaves the relic on the run.
/// </summary>
public sealed class RelicRewardTests
{
    /// <summary>The elite's loot is declined at this action of the whole-act fixture,
    /// after its gold and card were taken.</summary>
    private const int EliteLootSkip = 129;

    [GameFact]
    public void ARelicClaimIsCheckedByIdAgainstWhatTheLootScreenOffers()
    {
        var wrong = Arbiter.Run("replay", Variant(("reward_type", RewardKinds.Relic), ("relic_id", "RELIC.NOT_ON_OFFER")));

        Assert.False(wrong.Verified, wrong.All);
        Assert.Contains("claims the 'relic' reward RELIC.NOT_ON_OFFER, but this loot screen offers", wrong.All, StringComparison.Ordinal);

        // The refusal names what the screen does offer, and claiming that is a
        // replay that verifies with the relic on the run.
        var offered = Regex.Match(wrong.All, @"but this loot screen offers (RELIC\.[A-Z0-9_]+)").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(offered), wrong.All);

        var right = Arbiter.Run("replay", Variant(("reward_type", RewardKinds.Relic), ("relic_id", offered)));

        Assert.True(right.Verified, right.All);
        Assert.Contains("VERIFIED", right.Output, StringComparison.Ordinal);
    }

    /// <summary>The whole-act history up to the elite's loot, with the decline
    /// replaced by a claim. Checkpoints and boundaries past the cut are dropped, as
    /// every variant drops them, because the history no longer reaches them.</summary>
    private static string Variant(params (string Key, string Value)[] claim)
    {
        var manifest = ManifestJson.Load(Arbiter.WholeAct);
        var skip = manifest.Actions[EliteLootSkip];
        Assert.Equal(ActionVerb.SkipRewards, skip.Verb);

        var actions = manifest.Actions.Take(EliteLootSkip).ToList();
        actions.Add(skip with
        {
            Verb = ActionVerb.ClaimReward,
            Args = new SortedDictionary<string, string>(
                claim.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                StringComparer.Ordinal),
        });

        var path = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", $"relic-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        ManifestJson.Save(manifest with
        {
            Actions = actions,
            Checkpoints = [.. manifest.Checkpoints.Where(checkpoint => checkpoint.AfterSeq < EliteLootSkip)],
            Boundaries = [.. manifest.Boundaries.Where(boundary => boundary.AfterSeq < EliteLootSkip)],
        }, path);
        return path;
    }
}

using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The verbs that reach the loot screen, the event and the card screens, driven
/// against the real engine.
///
/// Each one is a mapping onto a game command that did not exist in this repository
/// before, so each needs its refusal demonstrated as well as its success. The shipped
/// manifest is the success case and it is verified elsewhere; these are the ways of
/// being wrong. Every case damages the shipped history in one place and asks what the
/// arbiter says, because a driver that improvises is exactly the failure this project
/// exists to prevent and improvisation is invisible in a passing replay.
/// </summary>
public class RewardAndScreenVerbTests
{
    [GameFact]
    public void AWonFightPutsItsLootOnOfferWithoutBeingAsked()
    {
        // The engine has no command for the loot screen appearing - the retail UI
        // calls it - so the host stands in. If it stopped, the gold, the potion and
        // the card reward would simply never be generated and every reward action
        // after a victory would refuse.
        var result = Replay(Actions(manifest => manifest.Actions.Take(11).ToList()), stopAfter: null);

        Assert.Contains("VERIFIED", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void ClaimingARewardNoOneIsOfferingIsRefused()
    {
        var result = Replay(Actions(manifest =>
        [
            .. manifest.Actions.Take(2),
            manifest.Actions.First(a => a.Verb == ActionVerb.ClaimReward) with { Seq = 2 },
        ]));

        Assert.Contains("no rewards are on offer", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void ClaimingARewardOutsideThisHistoryIsRefusedAtIngestion()
    {
        var result = Replay(Retype(ActionVerb.ClaimReward, ("reward_type", "relic")));

        Assert.False(result.Verified, result.All);
        Assert.Contains("'reward_type' is 'relic'", result.All, StringComparison.Ordinal);
        Assert.Contains("Known kinds: gold, potion", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void TakingACardTheRewardDidNotOfferAtThatPositionIsRefused()
    {
        var result = Replay(Retype(ActionVerb.TakeCard, ("card_id", "CARD.BASH")));

        Assert.Contains("expects CARD.BASH at card reward option 0", result.All, StringComparison.Ordinal);
        Assert.Contains("CARD.POMMEL_STRIKE", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void TakingACardFromAPositionTheRewardDoesNotHaveIsRefused()
    {
        var result = Replay(Retype(ActionVerb.TakeCard, ("option_index", "7")));

        Assert.Contains("takes card reward option 7", result.All, StringComparison.Ordinal);
        Assert.Contains("this reward offers 3", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void SkippingALootScreenNobodyIsShowingIsRefused()
    {
        var result = Replay(Actions(manifest =>
        [
            .. manifest.Actions.Take(2),
            new ActionRecord
            {
                Seq = 2,
                Verb = ActionVerb.SkipRewards,
                Source = FactSource.Observed,
                Evidence = FactEvidence.AtVideoTime(80_000, "test"),
            },
        ]));

        Assert.Contains("no rewards are on offer", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void LeavingARoomWithLootStillOnOfferIsRefused()
    {
        // The engine skips a leftover reward set on the way out and says nothing, so
        // without this refusal a history that forgot a reward would replay exactly
        // like one that declined it. This is the whole reason SkipRewards exists.
        var result = Replay(Actions(manifest =>
            manifest.Actions
                .Where(action => action.Verb != ActionVerb.ClaimReward &&
                                 action.Verb != ActionVerb.TakeCard &&
                                 action.Verb != ActionVerb.SkipRewards)
                .Select((action, index) => action with { Seq = index })
                .Take(13)
                .ToList()));

        Assert.Contains("loot screen is still open", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void ChoosingAnOptionInADifferentEventIsRefused()
    {
        var result = Replay(Retype(ActionVerb.ChooseEventOption, ("event_id", "EVENT.TEA_MASTER")));

        Assert.Contains("expects event EVENT.TEA_MASTER", result.All, StringComparison.Ordinal);
        Assert.Contains("EVENT.WATERLOGGED_SCRIPTORIUM", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void ChoosingAnEventOptionOutsideAnEventRoomIsRefused()
    {
        // The event synchronizer keeps the last event it ran, so asking it would have
        // answered with Neow's from two floors back. The room is asked instead.
        var result = Replay(Actions(manifest =>
        [
            .. manifest.Actions.Take(2),
            manifest.Actions.First(a => a.Verb == ActionVerb.ChooseEventOption) with { Seq = 2 },
        ]));

        Assert.Contains("not an event", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void PickingACardTheScreenDoesNotOfferAtThatPositionIsRefused()
    {
        var result = Replay(Retype(ActionVerb.SelectCardFromScreen, ("card_id", "CARD.ASCENDERS_BANE")));

        Assert.Contains("expects CARD.ASCENDERS_BANE at screen option", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void ASelectionNoScreenAskedForIsRefused()
    {
        // A card selection has to follow the action that opens its screen. One that
        // does not is an action recorded against a screen this run never opened, and
        // it has to fail rather than sit in the history doing nothing.
        var result = Replay(Actions(manifest =>
        {
            var actions = manifest.Actions.Take(11).ToList();
            actions.Add(manifest.Actions.First(a => a.Verb == ActionVerb.SelectCardFromScreen) with { Seq = 11 });
            return actions;
        }));

        Assert.Contains("no screen consumed it", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void AScreenThatWantsMoreCardsThanTheManifestSuppliesIsRefused()
    {
        // The Waterlogged Scriptorium's Prickly Sponge enchants two cards. Supplying
        // one would let the engine choose the second, which is a decision nobody made.
        var result = Replay(Actions(manifest =>
        {
            var actions = manifest.Actions.ToList();
            var second = actions.Last(a => a.Verb == ActionVerb.SelectCardFromScreen);
            actions.Remove(second);
            return actions.Select((action, index) => action with { Seq = index }).ToList();
        }));

        Assert.Contains("asked for 2 card(s)", result.All, StringComparison.Ordinal);
        Assert.Contains("the manifest supplies 1", result.All, StringComparison.Ordinal);
    }

    [GameTheory]
    [InlineData(15, 0)]
    [InlineData(16, 1)]
    public void APartialReplayCannotConsumeSelectionsOutsideItsHistory(
        int stopAfter, int suppliedSelections)
    {
        var result = Replay(Arbiter.Manifest, stopAfter);

        Assert.False(result.Verified, result.All);
        Assert.Contains("asked for 2 card(s)", result.All, StringComparison.Ordinal);
        Assert.Contains(
            $"the manifest supplies {suppliedSelections}", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void FreshProcessesReachTheSameTwoEnemyBoundary()
    {
        // Determinism over the whole extended history, not only the part that was
        // already there. Everything the six new verbs reach draws from the run's own
        // random streams - the gold amount, which potion the fight rolled, which three
        // cards the reward offered - and a digest taken in one process would say
        // nothing about whether any of it is reproducible.
        var outDir = TempDir();

        var result = Arbiter.Run("determinism", Arbiter.Manifest, "--runs", "2", "--out", outDir);

        Assert.True(result.Verified, result.All);
        Assert.Contains("byte-identical canonical state", result.Output, StringComparison.Ordinal);

        // And the state they agree on is the one the recording shows at the boundary.
        var state = File.ReadAllText(Path.Combine(outDir, "determinism-run0.state"));
        Assert.Contains("combat.encounter=ENCOUNTER.CORPSE_SLUGS_WEAK", state, StringComparison.Ordinal);
        Assert.Contains("run.total_floor=5", state, StringComparison.Ordinal);
        Assert.Contains("player.deck_count=12", state, StringComparison.Ordinal);
        Assert.Contains(
            "combat.hand=CARD.BASH@ENCHANTMENT.STEADY|", state, StringComparison.Ordinal);
    }

    /// <summary>
    /// The shipped history with its actions rewritten, saved where a child process can
    /// replay it.
    ///
    /// The evidence timestamps are rebuilt onto an even ladder rather than carried
    /// over. These variants drop and reorder actions, and the validator - rightly -
    /// insists a manifest's timestamps run in the order its actions do, so keeping the
    /// original times would make every one of these fail at ingestion for a reason
    /// that has nothing to do with the verb under test.
    /// </summary>
    private static string Actions(Func<ReplayManifest, IReadOnlyList<ActionRecord>> rewrite)
    {
        var manifest = ManifestJson.Load(Arbiter.Manifest);
        var rewritten = rewrite(manifest);
        var actions = rewritten
            .Select((action, index) => action with
            {
                Evidence = FactEvidence.AtVideoTime(
                    ActionTime(index), action.Evidence?.Method ?? "test variant"),
            })
            .ToList();
        var position = actions.Select((action, index) => (action.Seq, index))
            .ToDictionary(pair => pair.Seq, pair => pair.index);

        var checkpoints = manifest.Checkpoints
            .Where(checkpoint => position.ContainsKey(checkpoint.AfterSeq))
            .Select(checkpoint => checkpoint with
            {
                Expect = checkpoint.Expect.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value with
                    {
                        Evidence = FactEvidence.AtVideoTime(
                            ActionTime(position[checkpoint.AfterSeq]) + 100, "test variant"),
                    },
                    StringComparer.Ordinal),
            })
            .ToList();

        var path = Path.Combine(TempDir(), "verbs.json");
        ManifestJson.Save(manifest with { Actions = actions, Checkpoints = checkpoints }, path);
        return path;
    }

    /// <summary>Well after the manifest's run-start evidence at 9,000ms, and well
    /// inside the recording.</summary>
    private static int ActionTime(int index) => 20_000 + index * 1_000;

    /// <summary>The shipped history with one argument of the first action of a given
    /// verb replaced.</summary>
    private static string Retype(ActionVerb verb, (string Name, string Value) argument) =>
        Actions(manifest =>
        {
            var actions = manifest.Actions.ToList();
            var target = actions.First(action => action.Verb == verb);
            var args = target.Args.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            args[argument.Name] = argument.Value;
            actions[actions.IndexOf(target)] = target with { Args = args };
            return actions;
        });

    private static Arbiter.Result Replay(string manifestPath, int? stopAfter = null) =>
        stopAfter is { } seq
            ? Arbiter.Run("replay", manifestPath, "--stop-after", seq.ToString())
            : Arbiter.Run("replay", manifestPath);

    private static string TempDir()
    {
        var dir = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}

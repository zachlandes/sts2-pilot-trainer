namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The recording's own line of its fight, as shipped beside the manifest: cut from
/// a replay trace at the fight's end and bound to the exact history it replayed.
/// </summary>
public sealed class RecordedFightTests
{
    private const string Digest = "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void KeepsTheTraceThroughTheEndOfTheFirstFightAndBindsToThatHistory()
    {
        var manifest = Manifest();
        var fight = RecordedFight.From(manifest, FullTrace(), Digest);

        Assert.Equal(RecordedFight.Schema, fight.SchemaId);
        Assert.Equal("test-run", fight.RunId);
        Assert.Equal(3, fight.CoveredThroughSeq);
        Assert.Equal([-1, 0, 1, 2, 3], fight.Trace.Steps.Select(step => step.Seq));
        Assert.Equal(
            SnapshotCacheKey.HashActions(manifest.Actions.Where(action => action.Seq <= 3)), fight.ActionHistoryHash);
        Assert.Equal(Digest, fight.CombatStartSnapshotDigest);

        fight.Bind(manifest);
        Assert.Equal("victory", fight.Projection().Summary.Outcome);
    }

    [Fact]
    public void RefusesATraceWhoseFightNeverFinishes()
    {
        var unfinished = new ReplayTrace { Steps = FullTrace().Steps.Take(4).ToList() };
        var thrown = Assert.Throws<ManifestException>(() => RecordedFight.From(Manifest(), unfinished, Digest));
        Assert.Contains("still in progress", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesToBindToAnotherRecording()
    {
        var fight = RecordedFight.From(Manifest(), FullTrace(), Digest);
        var other = Manifest() with { RunId = "someone-else" };

        var thrown = Assert.Throws<ManifestException>(() => fight.Bind(other));
        Assert.Contains("from run 'test-run'", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesToBindToAHistoryThatChangedUnderneathIt()
    {
        var manifest = Manifest();
        var fight = RecordedFight.From(manifest, FullTrace(), Digest);
        var retranscribed = manifest with
        {
            Actions = manifest.Actions
                .Select(action => action.Seq == 2
                    ? Fixtures.Action(2, ActionVerb.PlayCard, ("card_id", "CARD.STRIKE_IRONCLAD"), ("hand_index", "0"))
                    : action)
                .ToList(),
        };

        var thrown = Assert.Throws<ManifestException>(() => fight.Bind(retranscribed));
        Assert.Contains("not this recording's through action 3", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesToBindToARecordingWithADifferentBoundaryDigest()
    {
        var fight = RecordedFight.From(Manifest(), FullTrace(), "sha256:" + new string('f', 64));

        var thrown = Assert.Throws<ManifestException>(() => fight.Bind(Manifest()));
        Assert.Contains("not the same fight from the same boundary", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesToBindWhenTheRecordingHasNoDigest()
    {
        var fight = RecordedFight.From(Manifest(), FullTrace(), Digest);
        var undigested = Manifest() with { Source = Manifest().Source with { CombatStartSnapshotDigest = null } };

        var thrown = Assert.Throws<ManifestException>(() => fight.Bind(undigested));
        Assert.Contains("no engine-produced combat-start snapshot digest", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SurvivesAJsonRoundTrip()
    {
        var fight = RecordedFight.From(Manifest(), FullTrace(), Digest);
        var read = RecordedFight.Deserialize(fight.Serialize());

        Assert.Equal(fight.Serialize(), read.Serialize());
        read.Bind(Manifest());
    }

    [Fact]
    public void RefusesAFileFromAnotherSchema()
    {
        var fight = RecordedFight.From(Manifest(), FullTrace(), Digest) with { SchemaId = "something/else/v9" };
        var thrown = Assert.Throws<ManifestException>(() => fight.Bind(Manifest()));
        Assert.Contains("schema", thrown.Message, StringComparison.Ordinal);
    }

    private static ReplayManifest Manifest()
    {
        var manifest = Fixtures.ValidManifest();
        return manifest with
        {
            Source = manifest.Source with { CombatStartSnapshotDigest = Fact<string>.Engine(Digest) },
            Actions =
            [
                .. manifest.Actions,
                Fixtures.Action(2, ActionVerb.PlayCard, ("card_id", "CARD.BASH"), ("hand_index", "3")),
                Fixtures.Action(3, ActionVerb.PlayCard, ("card_id", "CARD.STRIKE_IRONCLAD"), ("hand_index", "0")),
                Fixtures.Action(4, ActionVerb.ClaimReward, ("reward_type", "gold")),
            ],
        };
    }

    /// <summary>Run start, the blessing, the move into the fight, two plays of which
    /// the second kills, and a reward claimed afterwards.</summary>
    private static ReplayTrace FullTrace() => new()
    {
        Steps =
        [
            Step(-1, "run_start", Outside(64), Outside(64)),
            Step(0, "ChooseNeowBlessing", Outside(64), Outside(64)),
            Step(1, "MapMove", Outside(64), InCombat(1, 64, 42)),
            Step(2, "PlayCard", InCombat(1, 64, 42), InCombat(1, 64, 34)),
            Step(3, "PlayCard", InCombat(1, 64, 34), Won(64)),
            Step(4, "ClaimReward", Won(64), Won(64)),
        ],
    };

    private static ReplayStep Step(
        int seq, string verb, IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after) =>
        new() { Seq = seq, Verb = verb, Before = before, After = after };

    private static Dictionary<string, string> Outside(int hp) => Common(hp, "none");

    private static Dictionary<string, string> Won(int hp)
    {
        var sample = Common(hp, "victory");
        sample["combat.turn"] = "1";
        sample["combat.enemy_count"] = "0";
        return sample;
    }

    private static Dictionary<string, string> InCombat(int turn, int hp, int enemyHp)
    {
        var sample = Common(hp, "in_progress");
        sample["combat.turn"] = turn.ToString(System.Globalization.CultureInfo.InvariantCulture);
        sample["combat.encounter"] = "ENCOUNTER.TEST";
        sample["combat.hand"] = "CARD.BASH|CARD.STRIKE_IRONCLAD";
        sample["combat.enemy_count"] = "1";
        sample["combat.enemy.0.model"] = "MONSTER.TEST";
        sample["combat.enemy.0.hp"] = enemyHp.ToString(System.Globalization.CultureInfo.InvariantCulture);
        sample["combat.enemy.0.max_hp"] = "42";
        return sample;
    }

    private static Dictionary<string, string> Common(int hp, string outcome) => new(StringComparer.Ordinal)
    {
        ["combat.in_progress"] = outcome == "in_progress" ? "true" : "false",
        ["combat.outcome"] = outcome,
        ["player.hp"] = hp.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["player.max_hp"] = "68",
        ["player.deck"] = "CARD.BASH|CARD.STRIKE_IRONCLAD",
        ["player.relics"] = "RELIC.BURNING_BLOOD",
        ["player.potions"] = "empty|empty",
    };
}

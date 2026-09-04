namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The recording's own line of each of its fights, as shipped beside the manifest:
/// cut from a replay trace at each fight's end and bound to the exact history it
/// replayed and to the manifest boundary of the same ordinal.
/// </summary>
public sealed class RecordedFightTests
{
    private const string FirstDigest = "sha256:" + "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string SecondDigest = "sha256:" + "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    [Fact]
    public void KeepsTheTraceThroughTheEndOfEachFightAndBindsToThatHistory()
    {
        var manifest = Manifest();
        var fights = RecordedFights.From(manifest, FullTrace(), Digests());

        Assert.Equal(RecordedFights.Schema, fights.SchemaId);
        Assert.Equal("test-run", fights.RunId);
        Assert.Equal([1, 2], fights.Fights.Select(fight => fight.Fight));

        var first = fights.Fight(1);
        Assert.Equal(1, first.CombatStartSeq);
        Assert.Equal(3, first.CoveredThroughSeq);
        Assert.Equal([1, 2, 3], first.Trace.Steps.Select(step => step.Seq));
        Assert.Equal(
            SnapshotCacheKey.HashActions(manifest.Actions.Where(action => action.Seq <= 3)),
            first.ActionHistoryHash);
        Assert.Equal(FirstDigest, first.CombatStartSnapshotDigest);

        var second = fights.Fight(2);
        Assert.Equal(5, second.CombatStartSeq);
        Assert.Equal(6, second.CoveredThroughSeq);

        fights.Bind(manifest);
        Assert.Equal("victory", fights.Projection().Summary.Outcome);
        Assert.Equal("victory", fights.Projection(2).Summary.Outcome);
    }

    /// <summary>
    /// A fight nothing derived a digest for is not cut. Its line could never be bound
    /// to the recording, so writing one would put a comparison in the file that no
    /// reader is allowed to make.
    /// </summary>
    [Fact]
    public void CutsExactlyTheFightsADigestWasDerivedFor()
    {
        var fights = RecordedFights.From(
            Manifest(), FullTrace(), new Dictionary<int, string> { [1] = FirstDigest });

        Assert.Equal([1], fights.Fights.Select(fight => fight.Fight));
    }

    [Fact]
    public void RefusesADigestForAFightTheReplayNeverFinished()
    {
        var thrown = Assert.Throws<ManifestException>(() => RecordedFights.From(
            Manifest(), FullTrace(), new Dictionary<int, string> { [3] = FirstDigest }));

        Assert.Contains("no completed fight with that ordinal", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesATraceWhoseFightNeverFinishes()
    {
        var unfinished = new ReplayTrace { Steps = FullTrace().Steps.Take(4).ToList() };
        var thrown = Assert.Throws<ManifestException>(
            () => RecordedFights.From(Manifest(), unfinished, Digests()));
        Assert.Contains("still in progress", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesToBindToAnotherRecording()
    {
        var fights = RecordedFights.From(Manifest(), FullTrace(), Digests());
        var other = Manifest() with { RunId = "someone-else" };

        var thrown = Assert.Throws<ManifestException>(() => fights.Bind(other));
        Assert.Contains("from run 'test-run'", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesToBindToAHistoryThatChangedUnderneathIt()
    {
        var manifest = Manifest();
        var fights = RecordedFights.From(manifest, FullTrace(), Digests());
        var retranscribed = manifest with
        {
            Actions = manifest.Actions
                .Select(action => action.Seq == 2
                    ? Fixtures.Action(2, ActionVerb.PlayCard, ("card_id", "CARD.STRIKE_IRONCLAD"), ("hand_index", "0"))
                    : action)
                .ToList(),
        };

        var thrown = Assert.Throws<ManifestException>(() => fights.Bind(retranscribed));
        Assert.Contains("not this recording's through action 3", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file whose first fight still agrees and whose second does not is refused on
    /// the second. Binding per fight is what makes that possible; binding the set as
    /// one claim would say only that something in it had drifted.
    /// </summary>
    [Fact]
    public void RefusesOnTheFightThatDrifted()
    {
        var fights = RecordedFights.From(Manifest(), FullTrace(), Digests());
        var moved = Manifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(FirstDigest)),
                ReplayBoundary.CombatStart(2, 5, Fact<string>.Engine("sha256:" + new string('f', 64))),
            ],
        };

        var thrown = Assert.Throws<ManifestException>(() => fights.Bind(moved));
        Assert.Contains("Fight 2's combat-start snapshot digest", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesToBindWhenTheRecordingDeclaresNoBoundaryForAFight()
    {
        var fights = RecordedFights.From(Manifest(), FullTrace(), Digests());
        var undeclared = Manifest() with
        {
            Boundaries = [ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(FirstDigest))],
        };

        var thrown = Assert.Throws<ManifestException>(() => fights.Bind(undeclared));
        Assert.Contains("no combat-start boundary for fight 2", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SurvivesAJsonRoundTrip()
    {
        var fights = RecordedFights.From(Manifest(), FullTrace(), Digests());
        var read = RecordedFights.Deserialize(fights.Serialize());

        Assert.Equal(fights.Serialize(), read.Serialize());
        read.Bind(Manifest());
    }

    [Fact]
    public void RefusesAFileFromAnotherSchema()
    {
        var fights = RecordedFights.From(Manifest(), FullTrace(), Digests()) with
        {
            SchemaId = "something/else/v9",
        };
        var thrown = Assert.Throws<ManifestException>(() => fights.Bind(Manifest()));
        Assert.Contains("schema", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>The single-fight file this replaced is named rather than called
    /// unrecognisable, so somebody holding one is told what to do about it.</summary>
    [Fact]
    public void RefusesTheRetiredSingleFightFileByName()
    {
        var fights = RecordedFights.From(Manifest(), FullTrace(), Digests()) with
        {
            SchemaId = RecordedFights.RetiredSchema,
        };

        var thrown = Assert.Throws<ManifestException>(() => fights.Bind(Manifest()));
        Assert.Contains("single-fight recorded-fight file", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NamesWhatItHoldsWhenAskedForAFightItDoesNot()
    {
        var fights = RecordedFights.From(Manifest(), FullTrace(), Digests());
        var thrown = Assert.Throws<ManifestException>(() => fights.Fight(7));
        Assert.Contains("fight 7 is not among them", thrown.Message, StringComparison.Ordinal);
    }

    private static Dictionary<int, string> Digests() =>
        new() { [1] = FirstDigest, [2] = SecondDigest };

    private static ReplayManifest Manifest()
    {
        var manifest = Fixtures.ValidManifest();
        return manifest with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(FirstDigest)),
                ReplayBoundary.CombatStart(2, 5, Fact<string>.Engine(SecondDigest)),
            ],
            Actions =
            [
                .. manifest.Actions,
                Fixtures.Action(2, ActionVerb.PlayCard, ("card_id", "CARD.BASH"), ("hand_index", "3")),
                Fixtures.Action(3, ActionVerb.PlayCard, ("card_id", "CARD.STRIKE_IRONCLAD"), ("hand_index", "0")),
                Fixtures.Action(4, ActionVerb.ClaimReward, ("reward_type", "gold")),
                Fixtures.Action(5, ActionVerb.MapMove, ("act", "0"), ("row", "2"), ("column", "3")),
                Fixtures.Action(6, ActionVerb.PlayCard, ("card_id", "CARD.BASH"), ("hand_index", "0")),
            ],
        };
    }

    /// <summary>Run start, the blessing, the move into the first fight, two plays of
    /// which the second kills, a reward claimed afterwards, then a second floor and a
    /// second fight won in one action.</summary>
    private static ReplayTrace FullTrace() => new()
    {
        Steps =
        [
            Step(-1, "run_start", Outside(64), Outside(64)),
            Step(0, "ChooseNeowBlessing", Outside(64), Outside(64)),
            Step(1, "MapMove", Outside(64), InCombat(1, 64, 42)),
            Step(2, "PlayCard", InCombat(1, 64, 42), InCombat(1, 64, 34)),
            Step(3, "PlayCard", InCombat(1, 64, 34), Won(64)),
            Step(4, "ClaimReward", Won(64), Outside(64)),
            Step(5, "MapMove", Outside(64), InCombat(1, 64, 20)),
            Step(6, "PlayCard", InCombat(1, 64, 20), Won(64)),
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

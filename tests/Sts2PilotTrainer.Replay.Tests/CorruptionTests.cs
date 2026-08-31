namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The corruptions are the arbiter's negative controls, so they need controls of
/// their own: each must actually change the history, and must still be a history the
/// validator accepts. A "corruption" that produced an invalid manifest would be
/// rejected by the validator instead of by the engine, which proves nothing about
/// the engine.
/// </summary>
public class CorruptionTests
{
    private static ReplayManifest Playable()
    {
        var manifest = Fixtures.ValidManifest();
        return manifest with
        {
            Actions =
            [
                Fixtures.Action(0, ActionVerb.ChooseNeowBlessing, ("option_index", "2")),
                Fixtures.Action(1, ActionVerb.MapMove, ("act", "0"), ("row", "1"), ("column", "3")),
                Fixtures.Action(2, ActionVerb.PlayCard, ("card_id", "CARD.HELLRAISER"), ("hand_index", "1")),
                Fixtures.Action(3, ActionVerb.PlayCard, ("card_id", "CARD.DEFEND_IRONCLAD"), ("hand_index", "3")),
                Fixtures.Action(4, ActionVerb.EndTurn),
            ],
        };
    }

    [Fact]
    public void EveryCorruptionChangesTheActionHistory()
    {
        var original = SnapshotCacheKey.HashActions(Playable().Actions);

        foreach (var corruption in Corruption.All)
        {
            var corrupted = corruption.Apply(Playable());
            Assert.True(
                SnapshotCacheKey.HashActions(corrupted.Actions) != original,
                $"corruption '{corruption.Name}' left the action history unchanged");
        }
    }

    [Fact]
    public void EveryCorruptionStillProducesAStructurallyValidManifest()
    {
        foreach (var corruption in Corruption.All)
        {
            var result = ManifestValidator.Validate(corruption.Apply(Playable()));
            Assert.True(result.IsValid, $"corruption '{corruption.Name}' produced an invalid manifest:\n{result.Describe()}");
        }
    }

    [Fact]
    public void CoversBothCorruptionsVideoOnlyChecksCannotSee()
    {
        // These two are the reason for owning an engine at all. If the set ever stops
        // including them, the negative controls only demonstrate what arithmetic on
        // the footage already caught.
        var undetectable = Corruption.All
            .Where(c => c.VideoOnly == Corruption.VideoOnlyVerdict.Undetected)
            .Select(c => c.Name)
            .ToList();

        Assert.Contains("reorder-plays", undetectable);
        Assert.Contains("substitute-same-cost", undetectable);
    }

    [Fact]
    public void KeepsAtLeastOneCorruptionVideoOnlyChecksWouldCatch()
    {
        // A control on the controls: an arbiter that caught only the subtle
        // corruptions would be broken in a way the subtle ones cannot reveal.
        Assert.Contains(Corruption.All, c => c.VideoOnly == Corruption.VideoOnlyVerdict.Detected);
    }

    [Fact]
    public void ReorderingKeepsEvidenceTimestampsInSequenceOrder()
    {
        var reordered = Corruption.All.Single(c => c.Name == "reorder-plays").Apply(Playable());
        var plays = reordered.Actions.Where(action => action.Verb == ActionVerb.PlayCard).ToList();

        Assert.Equal(3_000, plays[0].Evidence!.VideoTimeMs);
        Assert.Equal(4_000, plays[1].Evidence!.VideoTimeMs);
        Assert.True(ManifestValidator.Validate(reordered).IsValid);
    }

    [Fact]
    public void ReorderingKeepsBothPlaysLegalAtTheirOriginalHandPositions()
    {
        // The reorder has to be caught by state, not by the driver noticing a bad hand
        // index - otherwise it would prove nothing about whether order matters.
        var reordered = Corruption.All.Single(c => c.Name == "reorder-plays").Apply(Playable());
        var plays = reordered.Actions.Where(a => a.Verb == ActionVerb.PlayCard).ToList();

        Assert.Equal("CARD.DEFEND_IRONCLAD", plays[0].Args["card_id"]);
        Assert.Equal("4", plays[0].Args["hand_index"]);
        Assert.Equal("CARD.HELLRAISER", plays[1].Args["card_id"]);
        Assert.Equal("1", plays[1].Args["hand_index"]);
    }
}

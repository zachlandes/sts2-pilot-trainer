namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The cache key decides when a materialised snapshot may be reused. Its two jobs
/// pull in opposite directions, so both are tested: it must change whenever the run
/// would come out differently, and it must not change when only the annotations did.
/// </summary>
public class SnapshotCacheKeyTests
{
    [Fact]
    public void IsStableForTheSameHistory()
    {
        var manifest = Fixtures.ValidManifest();
        Assert.Equal(SnapshotCacheKey.For(manifest, 1), SnapshotCacheKey.For(manifest, 1));
    }

    [Fact]
    public void ChangesWhenAnActionsArgumentsChange()
    {
        var manifest = Fixtures.ValidManifest();
        var moved = manifest with
        {
            Actions =
            [
                manifest.Actions[0],
                Fixtures.Action(1, ActionVerb.MapMove, ("row", "1"), ("column", "0")),
            ],
        };

        Assert.NotEqual(SnapshotCacheKey.For(manifest, 1), SnapshotCacheKey.For(moved, 1));
    }

    [Fact]
    public void ChangesWhenTwoActionsAreReordered()
    {
        // Order is the point. A key that survived a reordering would hand back a
        // snapshot for a run that would not have produced it.
        var actions = new[]
        {
            Fixtures.Action(0, ActionVerb.PlayCard, ("card_id", "CARD.BASH"), ("hand_index", "0")),
            Fixtures.Action(1, ActionVerb.PlayCard, ("card_id", "CARD.DEFEND_IRONCLAD"), ("hand_index", "1")),
        };
        var swapped = new[] { actions[1] with { Seq = 0 }, actions[0] with { Seq = 1 } };

        Assert.NotEqual(SnapshotCacheKey.HashActions(actions), SnapshotCacheKey.HashActions(swapped));
    }

    [Fact]
    public void DoesNotChangeWhenOnlyProvenanceChanges()
    {
        // Evidence, notes and the RNG classification describe how we came to believe
        // an action happened; they do not change what the engine does. If they were in
        // the key, improving an annotation would throw away a correctly verified
        // snapshot, and the pressure would be to stop improving annotations.
        var original = Fixtures.Action(0, ActionVerb.EndTurn);
        var reannotated = original with
        {
            Evidence = FactEvidence.AtVideoTime(999_999, "a much better description"),
            Note = "revisited during review",
            ConsumesRng = ["Shuffle", "MonsterAi"],
        };

        Assert.Equal(
            SnapshotCacheKey.HashActions([original]),
            SnapshotCacheKey.HashActions([reannotated]));
    }

    [Theory]
    [InlineData("build_version")]
    [InlineData("seed")]
    [InlineData("content_hash")]
    [InlineData("game_mode")]
    public void ChangesWhenAnyEnvironmentIdentityFieldChanges(string field)
    {
        var manifest = Fixtures.ValidManifest();
        var changed = field switch
        {
            "build_version" => WithEnvironment(manifest, e => e with
            {
                BuildVersion = Fact<string>.Observed("v0.112.0", FactEvidence.AtVideoTime(1, "t")),
            }),
            "seed" => WithEnvironment(manifest, e => e with
            {
                Seed = Fact<string>.Observed("MMWN3B7J2JL3", FactEvidence.AtVideoTime(1, "t")),
            }),
            "content_hash" => WithEnvironment(manifest, e => e with
            {
                ContentHash = Fact<string>.Observed("999", FactEvidence.AtVideoTime(1, "t")),
            }),
            _ => WithEnvironment(manifest, e => e with
            {
                GameMode = Fact<string>.Inferred("custom", FactEvidence.Reasoning("t")),
            }),
        };

        Assert.NotEqual(SnapshotCacheKey.For(manifest, 1), SnapshotCacheKey.For(changed, 1));
    }

    [Fact]
    public void ChangesWithThePointItWasTakenAt()
    {
        var manifest = Fixtures.ValidManifest();
        Assert.NotEqual(SnapshotCacheKey.For(manifest, 0), SnapshotCacheKey.For(manifest, 1));
    }

    [Fact]
    public void RendersADirectoryNameAPersonCanRead()
    {
        var name = SnapshotCacheKey.For(Fixtures.ValidManifest(), 1).ToCacheDirectoryName();

        Assert.Contains("v0.111.0", name, StringComparison.Ordinal);
        Assert.Contains("SFXT47K77RFK", name, StringComparison.Ordinal);
        Assert.Contains("standard", name, StringComparison.Ordinal);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, name);
    }

    private static ReplayManifest WithEnvironment(
        ReplayManifest manifest, Func<EnvironmentIdentity, EnvironmentIdentity> change) =>
        manifest with { Environment = change(manifest.Environment) };
}

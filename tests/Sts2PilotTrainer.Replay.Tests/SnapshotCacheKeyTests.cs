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
                Fixtures.Action(1, ActionVerb.MapMove, ("act", "0"), ("row", "1"), ("column", "0")),
            ],
        };

        Assert.NotEqual(SnapshotCacheKey.For(manifest, 1), SnapshotCacheKey.For(moved, 1));
    }

    /// <summary>
    /// A boundary whose own action opens a card screen is answered from the selections
    /// recorded after it, so those selections determine the state at the boundary and
    /// have to be in the key. Two recordings that agree up to the end of the turn and
    /// disagree about which card the screen took are two different states, and serving
    /// one from the other's cache directory is exactly what the key exists to prevent.
    /// </summary>
    [Fact]
    public void ChangesWhenTheSelectionAnsweringTheBoundarysOwnScreenChanges()
    {
        var manifest = Fixtures.ValidManifest() with
        {
            Actions =
            [
                Fixtures.Action(0, ActionVerb.ChooseNeowBlessing, ("option_index", "2")),
                Fixtures.Action(1, ActionVerb.EndTurn),
                Fixtures.Action(2, ActionVerb.SelectCardFromScreen,
                    ("card_id", "CARD.BASH"), ("option_index", "0")),
            ],
        };
        var otherPick = manifest with
        {
            Actions =
            [
                manifest.Actions[0],
                manifest.Actions[1],
                Fixtures.Action(2, ActionVerb.SelectCardFromScreen,
                    ("card_id", "CARD.DEFEND_IRONCLAD"), ("option_index", "1")),
            ],
        };

        Assert.NotEqual(SnapshotCacheKey.For(manifest, 1), SnapshotCacheKey.For(otherPick, 1));
    }

    /// <summary>
    /// The action-history hash is not a cache detail: it is written into every
    /// recorded-fights file as <c>action_history_hash</c> and read back by
    /// <see cref="RecordedFight.Bind"/>, so its value is a serialized contract. Pinned
    /// to a literal here so that an edit made for the cache's benefit cannot move run
    /// identity without somebody deciding to.
    /// </summary>
    [Fact]
    public void HashesAKnownHistoryToItsPublishedValue()
    {
        var history = new[]
        {
            Fixtures.Action(0, ActionVerb.ChooseNeowBlessing, ("option_index", "2")),
            Fixtures.Action(1, ActionVerb.MapMove, ("act", "0"), ("row", "1"), ("column", "3")),
            Fixtures.Action(2, ActionVerb.SelectCardFromScreen,
                ("card_id", "CARD.BASH"), ("option_index", "0")),
        };

        Assert.Equal(
            "sha256:579a215792fa30eaea5a4fd8bbb673e6f7200d61b69a6788a4c2432494ead75b",
            SnapshotCacheKey.HashActions(history));
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
    [InlineData("character")]
    [InlineData("ascension")]
    [InlineData("acts")]
    [InlineData("mods")]
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
            "game_mode" => WithEnvironment(manifest, e => e with
            {
                GameMode = Fact<string>.Inferred("custom", FactEvidence.Reasoning("t")),
            }),
            "character" => WithEnvironment(manifest, e => e with
            {
                Character = Fact<string>.Observed("CHARACTER.NECROBINDER", FactEvidence.AtVideoTime(1, "t")),
            }),
            "ascension" => WithEnvironment(manifest, e => e with
            {
                Ascension = Fact<int>.Observed(11, FactEvidence.AtVideoTime(1, "t")),
            }),
            "acts" => WithEnvironment(manifest, e => e with
            {
                Acts = Fact<IReadOnlyList<string>>.Declared(["ACT.OVERGROWTH"]),
            }),
            _ => WithEnvironment(manifest, e => e with
            {
                Mods = Fact<ModEnvironment>.Declared(new ModEnvironment
                {
                    Name = "different-environment",
                    ReportedCount = 0,
                    Mods = [],
                }),
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

    [Fact]
    public void KeepsManifestDerivedDirectoryNamesInsideTheCacheRoot()
    {
        var manifest = Fixtures.ValidManifest();
        manifest = manifest with
        {
            Environment = manifest.Environment with
            {
                Character = Fact<string>.Observed(
                    "CHARACTER.X/../../../../target", FactEvidence.AtVideoTime(1, "test")),
            },
        };
        var root = Path.GetFullPath(Path.Combine("build", "snapshot-cache-test"));

        var resolved = SnapshotCacheKey.For(manifest, 1).ResolveCacheDirectory(root);

        Assert.StartsWith(root + Path.DirectorySeparatorChar, resolved, StringComparison.Ordinal);
        Assert.Equal(Path.GetFileName(resolved), Path.GetRelativePath(root, resolved));
    }

    [Fact]
    public void RejectsACacheDirectorySymlinkOutsideTheCacheRoot()
    {
        var baseDirectory = Path.GetFullPath(Path.Combine(
            "build", "test-scratch", Guid.NewGuid().ToString("N")));
        var root = Path.Combine(baseDirectory, "cache");
        var outside = Path.Combine(baseDirectory, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        var key = SnapshotCacheKey.For(Fixtures.ValidManifest(), 1);
        Directory.CreateSymbolicLink(Path.Combine(root, key.ToCacheDirectoryName()), outside);

        var error = Assert.Throws<InvalidOperationException>(() => key.ResolveCacheDirectory(root));

        Assert.Contains("symbolic link", error.Message, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    [Fact]
    public void RejectsASymlinkedCacheArtifactBeforeWriting()
    {
        var baseDirectory = Path.GetFullPath(Path.Combine(
            "build", "test-scratch", Guid.NewGuid().ToString("N")));
        var cacheDirectory = Path.Combine(baseDirectory, "cache");
        var outside = Path.Combine(baseDirectory, "outside-state.canonical");
        Directory.CreateDirectory(cacheDirectory);
        File.WriteAllText(outside, "outside");
        File.CreateSymbolicLink(Path.Combine(cacheDirectory, "state.canonical"), outside);

        var error = Assert.Throws<InvalidOperationException>(() =>
            SnapshotCacheKey.ResolveCacheArtifact(cacheDirectory, "state.canonical"));

        Assert.Contains("symbolic link", error.Message, StringComparison.Ordinal);
        Assert.Equal("outside", File.ReadAllText(outside));
    }

    private static ReplayManifest WithEnvironment(
        ReplayManifest manifest, Func<EnvironmentIdentity, EnvironmentIdentity> change) =>
        manifest with { Environment = change(manifest.Environment) };
}

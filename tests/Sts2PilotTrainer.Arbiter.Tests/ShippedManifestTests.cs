using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// Checks on the manifest this repository actually ships, as opposed to a fixture.
/// These need no game: they read the file and apply the same rules the arbiter does.
/// </summary>
public class ShippedManifestTests
{
    private static ReplayManifest Manifest => ManifestJson.Load(Arbiter.Manifest);

    [Fact]
    public void PassesEveryStructuralRule()
    {
        var result = ManifestValidator.Validate(Manifest);
        Assert.True(result.IsValid, result.Describe());
    }

    /// <summary>
    /// Every manifest this repository ships is already in the current format on disk.
    ///
    /// A migrated file must not migrate again: the reader would accept a version-5
    /// file forever, and the moment a file changes has to be a moment a person chose,
    /// in the change that moved the format. The two native recordings carry the
    /// migration's own note, because their event choices name no option key and a
    /// version-6 recorder would have named one.
    /// </summary>
    [Theory]
    [InlineData("navegreed-OJ-6QXhNgdg.replay.json", false)]
    [InlineData("native-9F8CY60C5BK7-20260906-005737.replay.json", true)]
    [InlineData("native-3LACFJ5NJ371-20260906-015901.replay.json", true)]
    public void EveryShippedManifestIsAlreadyInTheCurrentFormat(string fileName, bool migratedNative)
    {
        var path = Path.Combine(Arbiter.RepoRoot, "manifests", fileName);
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));

        Assert.Equal(
            ReplayManifest.CurrentManifestVersion,
            document.RootElement.GetProperty("manifest_version").GetInt32());

        var manifest = ManifestJson.Load(path);
        var result = ManifestValidator.Validate(manifest);
        Assert.True(result.IsValid, result.Describe());

        if (!migratedNative) return;

        var native = document.RootElement.GetProperty("source").GetProperty("native");
        Assert.Equal(NativeSource.CompleteIntegrity, native.GetProperty("integrity").GetString());
        Assert.Equal(5, native.GetProperty("migrated_from_version").GetInt32());
        Assert.All(
            manifest.Actions.Where(action => action.Verb is ActionVerb.ChooseNeowBlessing or ActionVerb.ChooseEventOption),
            action => Assert.False(action.Args.ContainsKey("option_key")));
    }

    [Fact]
    public void ShippedMapObservationPassesIngestion()
    {
        var observation = MapObservation.Load(Arbiter.MapObservation);

        Assert.NotEmpty(observation.Nodes);
    }

    [Fact]
    public void ShowsTheRecordingBeginsAtTheRunsStart()
    {
        // The specific defence against a run resumed from history. One of the three
        // mods in this creator's environment does exactly that, and a resumed run
        // matches on seed, build, content hash and acts.
        var start = Manifest.Source.RunStart;
        Assert.NotNull(start);
        Assert.False(start.EnteredFromRunHistory.Value);
        Assert.False(start.ResumeModalSeen.Value);
        Assert.Equal(1, start.FirstObservedFloor.Value);
        Assert.InRange(start.FirstObservedRunTimeSeconds.Value, 0, RunStartEvidence.MaxRunTimeSecondsAtStart);
    }

    [Fact]
    public void ReadsTheEnvironmentAgainFromTheOtherEndOfTheRecording()
    {
        var summary = Manifest.Source.RunSummary;
        Assert.NotNull(summary);

        // Far apart on purpose: two readings that agree across most of an hour of
        // footage cannot both be one drifted glance, and a recording spliced from two
        // runs could not agree at both ends.
        var firstReadingMs = Manifest.Environment.Seed.Evidence!.VideoTimeMs!.Value;
        Assert.True(
            summary.VideoTimeMs - firstReadingMs > 1_000_000,
            "the two environment readings are too close together to corroborate each other");

        Assert.Equal(Manifest.Environment.Seed.Value, summary.Seed.Value);
        Assert.Equal(Manifest.Environment.ContentHash.Value, summary.ContentHash.Value);
        Assert.NotEmpty(summary.NotShown);
    }

    /// <summary>
    /// The comparison contract computes over a finished fight, so a manifest whose
    /// history stopped mid-combat could never be one side of a comparison. The gate
    /// computes this from a real replay; this reads it off the manifest, needs no
    /// game, and so runs everywhere.
    /// </summary>
    [Fact]
    public void CoversTheFirstCombatThroughItsEnd()
    {
        var manifest = Manifest;
        var ends = manifest.Checkpoints.Where(c => c.Kind == "combat_end").ToList();

        Assert.NotEmpty(ends);
        Assert.All(ends, final =>
        {
            Assert.Equal("victory", final.Expect["combat.outcome"].Value);
            Assert.Equal("false", final.Expect["combat.in_progress"].Value);
            Assert.All(final.Expect.Values, fact => Assert.Equal(FactSource.Observed, fact.Source));
        });

        // The first fight the history enters is the one the comparison contract reads,
        // and it has to have finished. The history may run past it - this one goes on
        // to a second fight and into the opening turns of a third - but a first fight
        // that never ended would leave the projection with nothing it can compute.
        var firstCombatStart = manifest.Checkpoints
            .Where(c => c.Kind == "combat_start")
            .Min(c => c.AfterSeq);
        Assert.Contains(ends, final => final.AfterSeq > firstCombatStart);
    }

    /// <summary>
    /// Every negative control has something in this history to damage.
    ///
    /// A control that finds nothing to corrupt is reported as inapplicable rather than
    /// as a pass, which is honest and would also let the publication manifest quietly
    /// stop exercising one. This is what keeps that from happening silently.
    /// </summary>
    [Fact]
    public void GivesEveryNegativeControlSomethingToDamage()
    {
        var manifest = Manifest;
        var inapplicable = Corruption.All
            .Where(control => !control.AppliesTo(manifest))
            .Select(control => $"{control.Name} (needs {control.Requires})")
            .ToList();

        Assert.True(inapplicable.Count == 0, string.Join("; ", inapplicable));
    }

    /// <summary>
    /// The history reaches the fight the 209-215 second window is inside, and stops at
    /// that window's own opening rather than replaying into it.
    /// </summary>
    [Fact]
    public void ReachesTheTwoEnemyWindowsCombatAndStopsAtItsBoundary()
    {
        var manifest = Manifest;
        var boundary = Assert.Single(manifest.Checkpoints, c => c.Id == "floor5-window-boundary");

        Assert.Equal(manifest.Actions[^1].Seq, boundary.AfterSeq);
        Assert.Equal("5", boundary.Expect["run.total_floor"].Value);
        Assert.Equal("3", boundary.Expect["combat.turn"].Value);
        Assert.Contains("CARD.BASH@ENCHANTMENT.STEADY", boundary.Expect["combat.hand"].Value);
        Assert.All(boundary.Expect.Values, fact =>
        {
            Assert.Equal(FactSource.Observed, fact.Source);
            Assert.InRange(fact.Evidence!.VideoTimeMs!.Value, 209_000, 215_000);
        });
    }

    /// <summary>
    /// The negative controls damage a nominated play. Without a nomination they take
    /// the last one, which in a fight replayed to its end is the killing blow - and
    /// omitting the killing blow leaves a shorter history that is self-consistent, so
    /// the control would stop being a control.
    /// </summary>
    [Fact]
    public void NominatesAPlayForTheNegativeControlsThatIsNotTheKillingBlow()
    {
        var manifest = Manifest;
        var nominated = Corruption.NominatedPlay(manifest.Actions);

        Assert.True(nominated.Args.ContainsKey("negative_control_substitute_card_id"));
        Assert.NotEqual(manifest.Actions[^1].Seq, nominated.Seq);
        Assert.NotEqual(
            nominated.Args["card_id"],
            nominated.Args["negative_control_substitute_card_id"]);
        Assert.Contains(
            manifest.Checkpoints,
            checkpoint => checkpoint.AfterSeq == nominated.Seq);
    }

    [Fact]
    public void IdentifiesEveryModTheOverlayReported()
    {
        var mods = Manifest.Environment.Mods.Value;

        Assert.Equal(mods.ReportedCount, mods.Mods.Count);
        Assert.All(mods.Mods, m => Assert.False(string.IsNullOrWhiteSpace(m.ReplayRisk)));

        // The identities are not readable from the video, which names no mod. Marking
        // them observed would claim a source that does not exist.
        Assert.Equal(FactSource.Inferred, Manifest.Environment.Mods.Source);
    }

}

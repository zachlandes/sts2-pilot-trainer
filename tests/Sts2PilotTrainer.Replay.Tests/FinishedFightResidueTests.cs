namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// Which boundaries of a recording written before format 7 carry a digest this
/// projection cannot reproduce, and which do not. No game: the rule is a rule over
/// the recording's own declarations.
/// </summary>
public sealed class FinishedFightResidueTests
{
    private static readonly Fact<string> Digest = Fact<string>.Captured(Fixtures.Digest, FactEvidence.AtActionOrdinal(0));

    private static ReplayManifest Recording(int? migratedFrom)
    {
        var native = Fixtures.NativeManifest();
        return native with
        {
            Source = native.Source with
            {
                Native = native.Source.Native! with { MigratedFromVersion = migratedFrom },
            },
            Boundaries =
            [
                ReplayBoundary.FloorEntry(2, 1, Digest),
                ReplayBoundary.CombatStart(1, 3, Digest),
                ReplayBoundary.FloorEntry(3, 3, Digest),
                ReplayBoundary.TurnStart(1, 2, 6, Digest),
                ReplayBoundary.FloorEntry(4, 9, Digest),
                ReplayBoundary.CombatStart(2, 12, Digest),
                ReplayBoundary.FloorEntry(5, 12, Digest),
                ReplayBoundary.FloorEntry(6, 20, Digest),
                ReplayBoundary.FloorEntry(7, 25, Digest),
            ],
            // The history stops inside the fight floor 7 dealt, so no combat start is
            // declared for it; the checkpoint there is what says a fight is live.
            Checkpoints =
            [
                .. native.Checkpoints,
                new Checkpoint
                {
                    Id = "floor-7-fight-opens",
                    AfterSeq = 25,
                    Kind = "combat_start",
                    Expect = new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
                    {
                        ["combat.in_progress"] = Fact<string>.Captured("true", FactEvidence.AtActionOrdinal(25)),
                    },
                },
            ],
        };
    }

    /// <summary>Of a format-6 recording, exactly the floor arrivals after the first
    /// fight that opened no fight of their own: a fight's own boundaries are read
    /// inside a live fight, whose projection did not change, an arrival a checkpoint
    /// reads as live is the last fight's own, and before the first fight there was
    /// nothing to carry.</summary>
    [Fact]
    public void OnlyANonFightArrivalAfterTheFirstFightPredatesThisProjection()
    {
        var recording = Recording(migratedFrom: 6);

        var predating = recording.Boundaries
            .Where(boundary => FinishedFightResidue.PredatesThisProjection(recording, boundary))
            .Select(boundary => boundary.Describe())
            .ToList();

        Assert.Equal(
            [ReplayBoundary.FloorEntry(4, 9, Digest).Describe(), ReplayBoundary.FloorEntry(6, 20, Digest).Describe()],
            predating);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void ARecordingWrittenBeforeFormatSevenSaysSoWhicheverOlderFormatItBeganIn(int writtenIn)
    {
        var recording = Recording(writtenIn);
        Assert.True(FinishedFightResidue.PredatesThisProjection(recording, recording.Boundaries[4]));
    }

    /// <summary>A recording a current recorder wrote carries no residue anywhere,
    /// and says nothing about migration.</summary>
    [Fact]
    public void ACurrentRecordingPredatesNothing()
    {
        var recording = Recording(migratedFrom: null);
        Assert.All(recording.Boundaries, boundary =>
            Assert.False(FinishedFightResidue.PredatesThisProjection(recording, boundary)));
    }

    /// <summary>A video reconstruction records no migration note of its own, so what
    /// it predates is the version its file declared when it was read: engine-derived
    /// under format 6, its non-fight arrivals after the first fight are re-derived;
    /// in the current format nothing is.</summary>
    [Fact]
    public void AVideoReconstructionIsExcusedByTheVersionItWasReadFrom()
    {
        var video = Fixtures.ValidManifest() with
        {
            Boundaries = [ReplayBoundary.CombatStart(1, 1, Digest), ReplayBoundary.FloorEntry(3, 5, Digest)],
        };
        Assert.All(video.Boundaries, boundary =>
            Assert.False(FinishedFightResidue.PredatesThisProjection(video, boundary)));

        var readFromSix = video with { ReadFromVersion = 6 };
        Assert.False(FinishedFightResidue.PredatesThisProjection(readFromSix, readFromSix.Boundaries[0]));
        Assert.True(FinishedFightResidue.PredatesThisProjection(readFromSix, readFromSix.Boundaries[1]));
    }

    [Theory]
    [InlineData("combat.turn", true)]
    [InlineData("combat.outcome", true)]
    [InlineData("combat.enemy.0.hp", true)]
    [InlineData("combat.in_progress", false)]
    [InlineData("player.hp", false)]
    [InlineData("run.total_floor", false)]
    public void EveryCombatFieldButTheLiveFlagIsResidue(string field, bool residue)
    {
        Assert.Equal(residue, FinishedFightResidue.IsResidueField(field));
    }
}

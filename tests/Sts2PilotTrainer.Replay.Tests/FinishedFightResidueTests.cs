namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// Which boundaries carrying a digest hashed before format 7 are ones this
/// projection cannot reproduce, and which are not. No game: the rule is a rule over
/// the recording's own declarations, and which projection a digest is a claim in is
/// the boundary's own.
/// </summary>
public sealed class FinishedFightResidueTests
{
    private static readonly Fact<string> Digest = Fact<string>.Captured(Fixtures.Digest, FactEvidence.AtActionOrdinal(0));

    private static ReplayManifest Recording(int? migratedFrom, int? projection = null)
    {
        var native = Fixtures.NativeManifest();
        return native with
        {
            Source = native.Source with
            {
                Native = native.Source.Native! with { MigratedFromVersion = migratedFrom },
            },
            Boundaries = new List<ReplayBoundary>
            {
                ReplayBoundary.FloorEntry(2, 1, Digest),
                ReplayBoundary.CombatStart(1, 3, Digest),
                ReplayBoundary.FloorEntry(3, 3, Digest),
                ReplayBoundary.TurnStart(1, 2, 6, Digest),
                ReplayBoundary.FloorEntry(4, 9, Digest),
                ReplayBoundary.CombatStart(2, 12, Digest),
                ReplayBoundary.FloorEntry(5, 12, Digest),
                ReplayBoundary.FloorEntry(6, 20, Digest),
                ReplayBoundary.FloorEntry(7, 25, Digest),
            }.Select(boundary => boundary with { Projection = projection ?? migratedFrom ?? CanonicalState.Projection }).ToList(),
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

    /// <summary>A native recording's note of where its file began outlives the
    /// replay that re-derived its digests, so it cannot be what says a digest is an
    /// older claim: a file migrated from 5 whose boundaries a verified replay stamped
    /// as this projection's predates nothing, and a disagreement at any of them is the
    /// finding rather than a digest to re-derive.</summary>
    [Fact]
    public void ARederivedRecordingPredatesNothingWhateverFormatItsFileBeganIn()
    {
        var rederived = Recording(migratedFrom: 5, projection: CanonicalState.Projection);
        Assert.All(rederived.Boundaries, boundary =>
            Assert.False(FinishedFightResidue.PredatesThisProjection(rederived, boundary)));
    }

    /// <summary>A file rewritten in the current format without a replay says nothing
    /// of the older digests it carries in its version, so the boundary says it: a
    /// current-format manifest whose arrival digest was hashed under format 6 is
    /// still that older claim, and its combat start still is not.</summary>
    [Fact]
    public void AnOlderDigestInACurrentFormatFileIsStillTheOlderClaim()
    {
        var video = Fixtures.ValidManifest() with
        {
            Boundaries =
            [
                ReplayBoundary.CombatStart(1, 1, Digest) with { Projection = 6 },
                ReplayBoundary.FloorEntry(3, 5, Digest) with { Projection = 6 },
            ],
        };
        Assert.Equal(ReplayManifest.CurrentManifestVersion, video.WrittenIn);

        Assert.False(FinishedFightResidue.PredatesThisProjection(video, video.Boundaries[0]));
        Assert.True(FinishedFightResidue.PredatesThisProjection(video, video.Boundaries[1]));
    }

    /// <summary>Reading a file written in an older format marks every boundary with
    /// that format, beside taking the residue expectations away, so the mark travels
    /// with the digest through whatever the file becomes.</summary>
    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void ReadingAnOlderFormatMarksEveryBoundaryWithIt(int writtenIn)
    {
        var recording = Recording(migratedFrom: null);

        var read = FinishedFightResidue.ReadFromOlderFormat(recording, writtenIn);

        Assert.All(read.Boundaries, boundary => Assert.Equal(writtenIn, boundary.Projection));
        Assert.Equal(recording.Boundaries.Select(boundary => boundary.Digest), read.Boundaries.Select(boundary => boundary.Digest));
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

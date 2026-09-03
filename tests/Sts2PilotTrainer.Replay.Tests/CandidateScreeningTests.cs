namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// Screening exists to make refusal cheap, so the tests that matter are the ones where it
/// refuses. A screen that passed everything would hand the expensive half of ingestion a
/// recording with no seed in it, and the cost of finding that out would be a download, a
/// segmentation pass and a transcription.
///
/// The paired positive cases are here for the same reason the replay controls have one: a
/// gate nobody has fed a good input to has not been shown to accept anything.
/// </summary>
public class CandidateScreeningTests
{
    private static PatchCalendar Calendar() => new(
    [
        new GameRelease("v0.110.0", new DateOnly(2026, 7, 31)),
        new GameRelease("v0.111.0", new DateOnly(2026, 8, 13)),
    ]);

    private static CreatorProfile DescriptionCreator() => new()
    {
        ChannelName = "JapaneseExport",
        ChannelId = "UCYZwLfdwKJjIm_JFYCEWULw",
        SeedSource = SeedSource.Description,
        SeedPattern = @"Run Seed:\s*([0-9A-Z_]+)",
        BuildPattern = @"Patch:\s*(v\d+\.\d+\.\d+)",
    };

    private static CreatorProfile OverlayCreator(params string[] occlusions) => new()
    {
        ChannelName = "NaveGreed",
        ChannelId = "UCuuDxwofGcur0Lt6iP-aDww",
        SeedSource = SeedSource.VersionOverlay,
        Occlusions = occlusions,
    };

    private static VideoMetadata Video(
        CreatorProfile creator, string? description, DateOnly? uploaded = null,
        IReadOnlyList<VideoChapter>? chapters = null) => new()
    {
        VideoId = "LKBhc87lAT0",
        ChannelId = creator.ChannelId,
        ChannelName = creator.ChannelName,
        Title = "Ascension 10 Defect",
        Description = description,
        DurationSeconds = 3597,
        UploadedUtc = uploaded ?? new DateOnly(2026, 8, 25),
        Chapters = chapters ?? [],
    };

    [Fact]
    public void AcceptsASeedAndBuildBothStatedInTheDescription()
    {
        var creator = DescriptionCreator();
        var video = Video(creator, "Run Seed: Y5BC6Y7SZPSU\nPatch: v0.111.0");

        var screening = CandidateScreening.Screen(video, creator, Calendar());

        Assert.Equal(ScreeningVerdict.Eligible, screening.Verdict);
        Assert.Equal("Y5BC6Y7SZPSU", screening.CandidateSeed);
        Assert.Equal("v0.111.0", screening.CandidateBuild);
        Assert.Equal(CandidateScreening.BuildStatedByCreator, screening.BuildBasis);
        Assert.Empty(screening.Blockers);
    }

    [Fact]
    public void DatesTheBuildFromTheUploadWhenTheCreatorDoesNotStateOne()
    {
        var creator = DescriptionCreator();
        var video = Video(creator, "Run Seed: Y5BC6Y7SZPSU", new DateOnly(2026, 8, 25));

        var screening = CandidateScreening.Screen(video, creator, Calendar());

        Assert.Equal(ScreeningVerdict.Eligible, screening.Verdict);
        Assert.Equal("v0.111.0", screening.CandidateBuild);
        Assert.Equal(CandidateScreening.BuildDatedFromUpload, screening.BuildBasis);
    }

    [Fact]
    public void RefusesWhenTheBuildCannotBeDatedAndTheCreatorDidNotStateIt()
    {
        var creator = DescriptionCreator();
        var video = Video(creator, "Run Seed: Y5BC6Y7SZPSU", new DateOnly(2026, 8, 13));

        var screening = CandidateScreening.Screen(video, creator, Calendar());

        Assert.Equal(ScreeningVerdict.Refused, screening.Verdict);
        Assert.Equal(CandidateScreening.BuildUnknown, screening.BuildBasis);
        Assert.Contains(screening.Blockers, b => b.Contains("could be on either", StringComparison.Ordinal));
    }

    [Fact]
    public void AStatedBuildBeatsAnUploadDateThatWouldHaveBeenAmbiguous()
    {
        var creator = DescriptionCreator();
        var video = Video(creator, "Run Seed: Y5BC6Y7SZPSU\nPatch: v0.111.0", new DateOnly(2026, 8, 13));

        var screening = CandidateScreening.Screen(video, creator, Calendar());

        Assert.Equal(ScreeningVerdict.Eligible, screening.Verdict);
        Assert.Equal("v0.111.0", screening.CandidateBuild);
    }

    [Fact]
    public void RefusesADescriptionCreatorWhoPublishedNoSeed()
    {
        var creator = DescriptionCreator();
        var video = Video(creator, "Great run today, subscribe!");

        var screening = CandidateScreening.Screen(video, creator, Calendar());

        Assert.Equal(ScreeningVerdict.Refused, screening.Verdict);
        Assert.Contains(screening.Blockers, b => b.Contains("too large to search", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Run Seed: Y5BC6Y7SZPSO")]
    [InlineData("Run Seed: Y5BC6Y7SZPSI")]
    public void RefusesASeedContainingCharactersTheGameNeverGenerates(string description)
    {
        var creator = DescriptionCreator();

        var screening = CandidateScreening.Screen(Video(creator, description), creator, Calendar());

        Assert.Equal(ScreeningVerdict.Refused, screening.Verdict);
        Assert.Contains(screening.Blockers, b => b.Contains("never produces", StringComparison.Ordinal));
    }

    [Fact]
    public void SendsAnOverlayCreatorForAFrameProbeRatherThanGuessing()
    {
        var creator = OverlayCreator();

        var screening = CandidateScreening.Screen(Video(creator, "no seed here"), creator, Calendar());

        Assert.Equal(ScreeningVerdict.NeedsFrameProbe, screening.Verdict);
        Assert.Null(screening.CandidateSeed);
        Assert.Empty(screening.Blockers);
    }

    [Fact]
    public void RefusesAnOverlayCreatorWhoseLayoutCoversTheOverlay()
    {
        var creator = OverlayCreator("a webcam sits over the top-right corner");

        var screening = CandidateScreening.Screen(Video(creator, null), creator, Calendar());

        Assert.Equal(ScreeningVerdict.Refused, screening.Verdict);
        Assert.Contains(screening.Blockers, b => b.Contains("absent value", StringComparison.Ordinal));
    }

    [Fact]
    public void RefusesAProfileAppliedToSomebodyElsesChannel()
    {
        var creator = DescriptionCreator();
        var video = Video(creator, "Run Seed: Y5BC6Y7SZPSU") with { ChannelId = "UCsomeoneelse" };

        var screening = CandidateScreening.Screen(video, creator, Calendar());

        Assert.Equal(ScreeningVerdict.Refused, screening.Verdict);
        Assert.Contains(screening.Blockers, b => b.Contains("wrong screen", StringComparison.Ordinal));
    }

    [Fact]
    public void ReportsTheCreatorsOwnIntroChapterSoReadingStartsAfterIt()
    {
        var creator = DescriptionCreator();
        var video = Video(creator, "Run Seed: Y5BC6Y7SZPSU", chapters:
        [
            new VideoChapter("Intro", 0, 46),
            new VideoChapter("Act 1", 46, 1425),
        ]);

        var screening = CandidateScreening.Screen(video, creator, Calendar());

        Assert.Equal(ScreeningVerdict.Eligible, screening.Verdict);
        Assert.Contains(screening.Notes, n => n.Contains("'Intro' over the first 46 seconds", StringComparison.Ordinal));
    }
}

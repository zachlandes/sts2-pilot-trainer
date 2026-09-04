namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// Dating a recording from its upload is a guess, and the only thing that makes a guess
/// safe is that it says when it cannot be made. These tests are mostly about the refusals:
/// a calendar that confidently returned a build for every date would key artifacts to the
/// wrong game and nothing downstream would notice until a replay diverged.
/// </summary>
public class PatchCalendarTests
{
    private static PatchCalendar Calendar() => new(
    [
        new GameRelease("v0.108.0", new DateOnly(2026, 7, 3)),
        new GameRelease("v0.109.0", new DateOnly(2026, 7, 17)),
        new GameRelease("v0.110.0", new DateOnly(2026, 7, 31)),
        new GameRelease("v0.111.0", new DateOnly(2026, 8, 13)),
    ]);

    [Fact]
    public void DatesAnUploadWellInsideAReleaseWindow()
    {
        var inference = Calendar().InferForUpload(new DateOnly(2026, 8, 25));

        Assert.True(inference.IsResolved);
        Assert.Equal("v0.111.0", inference.Version);
        Assert.False(inference.Ambiguous);
    }

    [Fact]
    public void DatesAnUploadOnTheDayBeforeTheNextRelease()
    {
        var inference = Calendar().InferForUpload(new DateOnly(2026, 7, 30));

        Assert.True(inference.IsResolved);
        Assert.Equal("v0.109.0", inference.Version);
    }

    [Theory]
    [InlineData(2026, 8, 13)]
    [InlineData(2026, 8, 14)]
    public void RefusesToPickWhenAReleaseLandedTheSameDayOrTheDayBefore(int year, int month, int day)
    {
        var inference = Calendar().InferForUpload(new DateOnly(year, month, day));

        Assert.True(inference.Ambiguous);
        Assert.False(inference.IsResolved);
        Assert.Null(inference.Version);
        Assert.Equal(["v0.110.0", "v0.111.0"], inference.Candidates);
    }

    [Fact]
    public void ResolvesTheDayAfterTheAmbiguityWindowCloses()
    {
        var inference = Calendar().InferForUpload(new DateOnly(2026, 8, 15));

        Assert.True(inference.IsResolved);
        Assert.Equal("v0.111.0", inference.Version);
    }

    [Fact]
    public void RefusesAnUploadOlderThanEverythingItKnows()
    {
        var inference = Calendar().InferForUpload(new DateOnly(2026, 6, 1));

        Assert.False(inference.IsResolved);
        Assert.False(inference.Ambiguous);
        Assert.Null(inference.Version);
        Assert.Empty(inference.Candidates);
        Assert.Contains("cannot be inferred", inference.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void IsNotAmbiguousWhenThereIsNoEarlierReleaseToConfuseItWith()
    {
        var calendar = new PatchCalendar([new GameRelease("v0.108.0", new DateOnly(2026, 7, 3))]);

        var inference = calendar.InferForUpload(new DateOnly(2026, 7, 3));

        Assert.True(inference.IsResolved);
        Assert.Equal("v0.108.0", inference.Version);
    }

    [Fact]
    public void OrdersReleasesGivenOutOfOrder()
    {
        var calendar = new PatchCalendar(
        [
            new GameRelease("v0.111.0", new DateOnly(2026, 8, 13)),
            new GameRelease("v0.108.0", new DateOnly(2026, 7, 3)),
        ]);

        Assert.Equal("v0.111.0", calendar.Latest.Version);
        Assert.Equal("v0.108.0", calendar.Releases[0].Version);
    }

    [Fact]
    public void RefusesAnEmptyCalendar()
    {
        Assert.Throws<ManifestException>(() => new PatchCalendar([]));
    }

    [Fact]
    public void RefusesAVersionListedTwice()
    {
        Assert.Throws<ManifestException>(() => new PatchCalendar(
        [
            new GameRelease("v0.111.0", new DateOnly(2026, 8, 13)),
            new GameRelease("v0.111.0", new DateOnly(2026, 8, 14)),
        ]));
    }

    [Fact]
    public void KnowsOnlyTheVersionsItLists()
    {
        Assert.True(Calendar().Knows("v0.110.0"));
        Assert.False(Calendar().Knows("v0.112.0"));
    }
}

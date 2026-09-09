using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Trainer.Tests;

public sealed class CommunityAccessTests
{
    [Fact]
    public void AServiceAndTheSettingOnIsAnOrdinaryTab()
    {
        Assert.Null(CommunityLock.For(sharingAvailable: true, showCommunityRuns: true));
    }

    [Fact]
    public void TheSettingOffLocksTheTabAndPointsAtTheSetting()
    {
        var locked = CommunityLock.For(sharingAvailable: true, showCommunityRuns: false);

        Assert.NotNull(locked);
        Assert.Contains(LibraryCopy.ShowCommunityRuns, locked.Tooltip);
        Assert.Contains("Settings", locked.Tooltip);
        Assert.Contains(LibraryCopy.ShowCommunityRuns, locked.Notice);
        Assert.Contains("run code", locked.Notice);
    }

    /// <summary>A switch that changes nothing is not what to point a player at: with no
    /// service the lock names the service, whatever the setting says.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NoServiceOutranksTheSetting(bool showCommunityRuns)
    {
        var locked = CommunityLock.For(sharingAvailable: false, showCommunityRuns);

        Assert.NotNull(locked);
        Assert.DoesNotContain(LibraryCopy.ShowCommunityRuns, locked.Tooltip);
        Assert.Contains("sharing service", locked.Tooltip);
        Assert.DoesNotContain(LibraryCopy.ShowCommunityRuns, locked.Notice);
    }

    [Fact]
    public void ThePlayerFacingTabIsCalledCommunity()
    {
        Assert.Equal("Community", LibraryCopy.CommunityTab);
        Assert.Contains("Community", LibraryCopy.LookupNotFound);
        Assert.DoesNotContain("Others", LibraryCopy.LookupNotFound);
    }
}

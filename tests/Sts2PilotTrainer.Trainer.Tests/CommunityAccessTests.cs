using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Trainer.Tests;

public sealed class CommunityAccessTests
{
    [Fact]
    public void AServiceAndTheSettingOnIsAnOrdinaryTab()
    {
        Assert.Null(CommunityLock.For(sharingAvailable: true, settingsReadable: true, showCommunityRuns: true));
    }

    [Fact]
    public void TheSettingOffLocksTheTabAndPointsAtTheSetting()
    {
        var locked = CommunityLock.For(sharingAvailable: true, settingsReadable: true, showCommunityRuns: false);

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
        var locked = CommunityLock.For(sharingAvailable: false, settingsReadable: true, showCommunityRuns);

        Assert.NotNull(locked);
        Assert.DoesNotContain(LibraryCopy.ShowCommunityRuns, locked.Tooltip);
        Assert.Contains("sharing service", locked.Tooltip);
        Assert.DoesNotContain(LibraryCopy.ShowCommunityRuns, locked.Notice);
    }

    /// <summary>A settings file this build cannot read is one no control can write
    /// into, so the lock says that rather than sending a player to a toggle that snaps
    /// straight back.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnUnreadableSettingsFilePointsAtNoSwitch(bool showCommunityRuns)
    {
        var locked = CommunityLock.For(
            sharingAvailable: true, settingsReadable: false, showCommunityRuns);

        Assert.NotNull(locked);
        Assert.DoesNotContain(LibraryCopy.ShowCommunityRuns, locked.Tooltip);
        Assert.DoesNotContain(LibraryCopy.ShowCommunityRuns, locked.Notice);
        Assert.DoesNotContain("in Settings", locked.Notice);
        Assert.DoesNotContain("open Settings", locked.Notice);
        Assert.Contains("settings.json", locked.Tooltip);
        Assert.Contains("run code", locked.Notice);
    }

    /// <summary>No service outranks an unreadable file too: the tab names the service.</summary>
    [Fact]
    public void NoServiceOutranksAnUnreadableSettingsFile()
    {
        var locked = CommunityLock.For(
            sharingAvailable: false, settingsReadable: false, showCommunityRuns: false);

        Assert.NotNull(locked);
        Assert.Equal(LibraryCopy.CommunityUnavailableTooltip, locked.Tooltip);
    }

    [Fact]
    public void ThePlayerFacingTabIsCalledCommunity()
    {
        Assert.Equal("Community", LibraryCopy.CommunityTab);
        Assert.Contains("Community", LibraryCopy.LookupNotFound);
        Assert.DoesNotContain("Others", LibraryCopy.LookupNotFound);
    }
}

using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Trainer.Tests;

public sealed class CommunityAccessTests
{
    [Fact]
    public void AServiceAndTheSettingOnIsAnOrdinaryTab()
    {
        Assert.Null(CommunityLock.For(sharingAvailable: true, showCommunityRuns: true));
    }

    /// <summary>The setting-off lock is the captain's sentence, on the tab and over it:
    /// it names the setting by its own label and the path to it.</summary>
    [Fact]
    public void TheSettingOffLocksTheTabAndPointsAtTheSetting()
    {
        var locked = CommunityLock.For(sharingAvailable: true, showCommunityRuns: false);

        Assert.NotNull(locked);
        Assert.Equal(LibraryCopy.CommunityOff, locked.Tooltip);
        Assert.Equal(locked.Tooltip, locked.Body);
        Assert.StartsWith("Community runs are off.", locked.Body);
        Assert.Contains("Settings > General > Runmobile", locked.Body);
        Assert.EndsWith(LibraryCopy.ShowCommunityRuns, locked.Body);
    }

    /// <summary>No service is the other cause and gets the other sentence: the
    /// setting-off wording would send a player to a switch that adds no service, so
    /// the line names the service and never the switch.</summary>
    [Fact]
    public void NoServiceNamesTheServiceAndNeverTheSwitch()
    {
        var locked = CommunityLock.For(sharingAvailable: false, showCommunityRuns: true);

        Assert.NotNull(locked);
        Assert.Equal(LibraryCopy.CommunityUnavailable, locked.Body);
        Assert.Equal(locked.Tooltip, locked.Body);
        Assert.DoesNotContain("Settings", locked.Body);
        Assert.DoesNotContain("Toggle", locked.Body);
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
        Assert.DoesNotContain(LibraryCopy.ShowCommunityRuns, locked.Body);
    }

    /// <summary>Every lock says its reason in one sentence: the tooltip behind the
    /// lock icon, and the body line the screen puts over the tabs for a player who
    /// never hovers. A lock short of either would be a Community list with nothing to
    /// account for it.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void EveryLockCarriesItsReasonInOneSentence(bool sharingAvailable, bool showCommunityRuns)
    {
        var locked = CommunityLock.For(sharingAvailable, showCommunityRuns);

        Assert.NotNull(locked);
        Assert.NotEmpty(locked.Tooltip);
        Assert.DoesNotContain('\n', locked.Tooltip);
        Assert.NotEmpty(locked.Body);
        Assert.DoesNotContain('\n', locked.Body);
    }

    [Fact]
    public void ThePlayerFacingTabIsCalledCommunity()
    {
        Assert.Equal("Community", LibraryCopy.CommunityTab);
        Assert.Contains("Community", LibraryCopy.LookupNotFound);
        Assert.DoesNotContain("Others", LibraryCopy.LookupNotFound);
    }
}

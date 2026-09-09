namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// Why the Community tab is locked, or null when it is not.
///
/// The lock is the game's own: a locked achievement wears a lock over its icon and
/// says what it is short of, and the stats screen's own Achievements tab carries the
/// same lock while it is disabled. This is the one derivation of whether Runmobile's
/// Community tab wears it and what it says, so the tab's tooltip and the line over the
/// list cannot disagree about the reason.
///
/// Nothing is laid over the list: the runs included with Runmobile and a run looked up
/// by code are still there and still pressable, and the lock accounts for what is
/// missing rather than covering what is not.
/// </summary>
/// <param name="Tooltip">The one sentence behind the lock on the tab.</param>
/// <param name="Body">The same reason over the tabs, where a player who never hovers
/// still reads it. One line, drawn whole.</param>
public sealed record CommunityLock(string Tooltip, string Body)
{
    /// <summary>
    /// The lock for these facts.
    ///
    /// No service outranks the setting: a switch that cannot change anything is not
    /// the thing to point a player at. There is no third case for a settings file this
    /// build could not read, because such a file names no sharing service either, so it
    /// arrives here as no service and gets that lock. With a service and the setting on
    /// there is nothing to say and the tab is an ordinary tab.
    /// </summary>
    public static CommunityLock? For(bool sharingAvailable, bool showCommunityRuns)
    {
        if (!sharingAvailable)
        {
            return new CommunityLock(
                LibraryCopy.CommunityUnavailableTooltip, LibraryCopy.SharingServiceUnavailable);
        }

        return showCommunityRuns
            ? null
            : new CommunityLock(LibraryCopy.CommunityOffTooltip, LibraryCopy.CommunityOffBody);
    }
}

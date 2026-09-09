namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// Why the Community tab is locked, or null when it is not.
///
/// The lock is the game's own: a locked achievement wears a lock over its icon and
/// says what it is short of, and the stats screen's own Achievements tab carries the
/// same lock while it is disabled. This is the one derivation of whether Runmobile's
/// Community tab wears it and what it says, so the tab's tooltip and the plate over
/// the list cannot disagree about the reason.
/// </summary>
/// <param name="Tooltip">The one sentence behind the lock on the tab.</param>
/// <param name="Notice">The plate over the list: what a player can do about it.</param>
public sealed record CommunityLock(string Tooltip, string Notice)
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
                LibraryCopy.CommunityUnavailableTooltip, LibraryCopy.CommunityUnavailableNotice);
        }

        return showCommunityRuns
            ? null
            : new CommunityLock(LibraryCopy.CommunityOffTooltip, LibraryCopy.CommunityOffNotice);
    }
}

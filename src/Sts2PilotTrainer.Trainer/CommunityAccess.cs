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
/// <param name="Sentence">The one sentence the lock says: behind the lock on the tab
/// on hover, and drawn whole over the tabs where a player who never hovers still
/// reads it. One sentence, one field: the two places used to carry two wordings of
/// one cause, and the line's wording was the one a player quoted back as wrong.</param>
public sealed record CommunityLock(string Sentence)
{
    /// <summary>
    /// The lock for these facts, and the cause it names.
    ///
    /// Two causes, two sentences, never one for both: the setting being off is cured
    /// by the setting, and the sentence says where it is; no service is cured by
    /// nothing a player can toggle, and a sentence pointing them at the switch would
    /// send them to a control that changes nothing. No service outranks the setting for
    /// that reason. There is no third case for a settings file this build could not
    /// read, because such a file names no sharing service either, so it arrives here as
    /// no service and gets that lock. With a service and the setting on there is
    /// nothing to say and the tab is an ordinary tab.
    /// </summary>
    public static CommunityLock? For(bool sharingAvailable, bool showCommunityRuns)
    {
        if (!sharingAvailable)
        {
            return new CommunityLock(LibraryCopy.CommunityUnavailable);
        }

        return showCommunityRuns
            ? null
            : new CommunityLock(LibraryCopy.CommunityOff);
    }
}

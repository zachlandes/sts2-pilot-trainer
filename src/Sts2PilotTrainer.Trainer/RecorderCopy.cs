namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// Every fixed word the recorder shows a player, in one place.
///
/// Three are rows of the game's own version overlay rather than sentences: the overlay
/// is a column of short capitals at the corner of the screen, and a row in it reads
/// like its neighbours or reads as a stranger. None carries a reason. Why a
/// recording stopped is written into the journal and said afterwards, on the run
/// history's plate; the overlay is not a place for it. The rest are the bookmark tag's
/// tooltip, because its one control is icon only and the sentence is a hover away.
///
/// These strings are approved wording. Changing one is a product decision, not a
/// refactor.
/// </summary>
public static class RecorderCopy
{
    /// <summary>The row under MODDED while this run is being recorded.</summary>
    public const string Recording = "RECORDING";

    /// <summary>The same row once the recorder can no longer account for the run
    /// continuously. Nothing recorded so far is discarded; the run is still the
    /// player's and goes on.</summary>
    public const string RecordingStopped = "RECORDING STOPPED";

    /// <summary>The same row while the recorder is still watching a run it can no longer
    /// account for from its start. The run goes on being recorded and is the player's;
    /// what it can never be is shared, and that is the whole of what the row says - the
    /// cause is in the journal and not on the overlay, the way the stopped row's is
    /// not.</summary>
    public const string RecordingNotShareable = "RECORDING · NOT SHAREABLE";

    /// <summary>The bookmark tag's tooltip while the fight that just ended is not
    /// bookmarked.</summary>
    public const string BookmarkThisFight = "Bookmark this fight";

    /// <summary>The same tooltip's title once it is.</summary>
    public const string Bookmarked = "Bookmarked";

    /// <summary>And its second line then, because pressing again is the undo.</summary>
    public const string PressToRemoveBookmark = "Press to remove";
}

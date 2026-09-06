namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// Every fixed word the recorder shows a player, in one place.
///
/// Two, and both are rows of the game's own version overlay rather than sentences: the
/// overlay is a column of short capitals at the corner of the screen, and a row in it
/// reads like its neighbours or reads as a stranger. Neither carries a reason. Why a
/// recording stopped is written into the journal and said afterwards, on the run
/// history's plate; the overlay is not a place for it.
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
}

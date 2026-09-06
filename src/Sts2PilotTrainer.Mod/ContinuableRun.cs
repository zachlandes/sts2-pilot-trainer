using MegaCrit.Sts2.Core.Saves;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// Which run this game can currently Continue, as the moment it began.
///
/// One question, asked of the game rather than worked out from what is on disk: the
/// save is the game's to interpret, and a recording is matched to it by the run's own
/// start time, which is what <see cref="Sts2PilotTrainer.Replay.RecordingLibrary.Name"/>
/// writes into a recording's name and reads back out of it.
///
/// It refuses rather than approximates. A game with no <c>SaveManager</c>, or one
/// holding a run save it could not read, gets an exception rather than a null: the
/// caller is <see cref="RecordingRetention"/> deciding what to delete, and "there is
/// no run to continue" and "this game cannot say" must not answer the same way.
/// </summary>
internal static class ContinuableRun
{
    private static Func<DateTime?>? _readerForTesting;

    /// <summary>When the continuable run began, or null when there is no run to
    /// continue.</summary>
    internal static DateTime? StartedUtc()
    {
        if (_readerForTesting is not null) return _readerForTesting();

        var saves = SaveManager.Instance
            ?? throw new InvalidOperationException(
                "This game has no SaveManager, so Runmobile cannot tell which run it can continue.");

        if (!saves.HasRunSave) return null;

        var save = saves.LoadRunSave();
        if (!save.Success || save.SaveData is not { } run)
        {
            throw new InvalidOperationException(
                $"This game has a saved run it could not read ({save.Status}), so Runmobile cannot tell " +
                "which run it can continue.");
        }

        return DateTimeOffset.FromUnixTimeSeconds(run.StartTime).UtcDateTime;
    }

    /// <summary>Answers for a game that is not there. For tests only: nothing in the
    /// mod calls it, and a player's process has the game's own answer.</summary>
    internal static void UseReaderForTesting(Func<DateTime?>? reader) => _readerForTesting = reader;
}

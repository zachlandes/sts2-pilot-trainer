using MegaCrit.Sts2.Core.Saves;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// Whether this save profile has finished a run, asked of the game rather than counted
/// off anything of ours.
///
/// One fact, and it is the game's own. <c>ProgressState.NumberOfRuns</c> is wins plus
/// losses, and it is the number the retail client itself reads to decide whether to
/// open its singleplayer submenu and whether the Compendium exists at all. Reading the
/// same number is what makes Runmobile's main-menu row appear exactly where the game's
/// own route to the library does not - a mod that counted something of its own would
/// eventually be on screen beside a Compendium button, or absent beside no route at all.
///
/// A run in progress has not been finished and does not count, which is the game's
/// meaning and the right one here: a player on their first run still cannot reach the
/// Compendium.
///
/// It refuses rather than approximates, for <see cref="ContinuableRun"/>'s reason: a
/// game with no <c>SaveManager</c> and a profile with no finished runs must not answer
/// the same way. Callers that draw a control they have already refused catch it and say
/// nothing; the one caller that draws the row itself has a running main menu, which the
/// game builds out of this same object.
/// </summary>
internal static class RunsFinished
{
    private static Func<bool>? _readerForTesting;

    /// <summary>Whether this profile has finished at least one run.</summary>
    internal static bool Any()
    {
        if (_readerForTesting is not null) return _readerForTesting();

        var saves = SaveManager.Instance
            ?? throw new InvalidOperationException(
                "This game has no SaveManager, so Runmobile cannot tell whether you have finished a run.");

        var progress = saves.Progress
            ?? throw new InvalidOperationException(
                "This game has no progress, so Runmobile cannot tell whether you have finished a run.");

        return progress.NumberOfRuns > 0;
    }

    /// <summary>Answers for a game that is not there. For tests only: nothing in the mod
    /// calls it, and a player's process has the game's own answer.</summary>
    internal static void UseReaderForTesting(Func<bool>? reader) => _readerForTesting = reader;
}

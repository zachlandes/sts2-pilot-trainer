using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// Makes the trainer's run unable to persist anything, by any path, for as long as
/// it exists.
///
/// Setting a run up with saving off is the first defence and not a sufficient one.
/// <c>RunManager.ShouldSave</c> gates the run save and everything at the end of a
/// run, but two writes in this build sit outside it: winning a fight calls
/// <c>SaveManager.UpdateProgressAfterCombatWon</c> and then
/// <c>SaveProgressFile</c>, and an event room saves the run with
/// <c>saveProgress</c> defaulting to true. The trainer's run wins a fight and
/// stands in an event room, so both of those are on its path. A player's progress
/// file would then be rewritten from a run that was never theirs.
///
/// A third kind sits on the same list without being a write at all: marking a card,
/// a relic or a potion as seen changes only the progress the game holds in memory,
/// which the trainer's run then leaves behind for the game to write out later by an
/// ordinary path the barrier must not stop. State that will be written is a write
/// that has not happened yet.
///
/// One more kind, on the list for the same reason but reached by a different route:
/// marking a tutorial complete writes the progress file itself rather than through
/// <c>SaveManager.SaveProgressFile</c>, so suppressing that method does not cover it.
/// The barrier names <c>MarkFtueAsComplete</c> and its two siblings directly.
///
/// Known and deliberately not covered: <c>NGameOverScreen</c> mutates
/// <c>Progress.CurrentScore</c> and the badge state in memory and then calls
/// <c>SaveProgressFile</c>. The write is suppressed here; the mutation is not, so
/// the dirtied score outlives the trainer run and the next ordinary write persists
/// it - the same shape as the seen-marks above, and it would be answered the same
/// way. Nothing reaches it today, because a fight-scoped trainer never opens the
/// game-over screen; a whole-run replay would, by construction, and this list is
/// where that is answered when it does.
///
/// So the writes themselves are stopped rather than the flags that usually reach
/// them. Every patch here is installed once, at mod start, and every one of them
/// does nothing at all unless a trainer run is live. That order matters: a barrier
/// raised when the run starts would have a window before it, and a barrier lowered
/// by a crash would be no barrier. Installed always and conditional on the run, a
/// crash, a forced exit and a quit are all covered, because the write never
/// happens rather than being undone afterwards.
///
/// It is deliberately narrow. It suppresses persistence and outward reporting for
/// one run and touches nothing else: with no trainer run live, every one of these
/// methods behaves exactly as the game wrote it, which is what keeps a player's own
/// runs saving normally.
/// </summary>
internal static class ProfileWriteBarrier
{
    /// <summary>
    /// Whether a trainer run is live, and so whether any of this applies.
    ///
    /// Owned here rather than read from <see cref="RecordedFightRun"/> so that the
    /// barrier can be raised before a run exists and lowered after it is gone -
    /// there is no moment where a trainer run is live and the barrier is not.
    /// </summary>
    internal static bool IsActive { get; private set; }

    /// <summary>The writes a trainer run must not make, by type and method name.
    /// Named rather than discovered, so a build that moved one fails loudly at
    /// install time instead of quietly persisting a run nobody played.</summary>
    private static readonly (string Type, string Method)[] SuppressedWrites =
    [
        // The player's progress file: what a won fight would rewrite.
        ("MegaCrit.Sts2.Core.Saves.SaveManager", "SaveProgressFile"),
        ("MegaCrit.Sts2.Core.Saves.SaveManager", "UpdateProgressAfterCombatWon"),
        ("MegaCrit.Sts2.Core.Saves.SaveManager", "UpdateProgressWithRunData"),
        ("MegaCrit.Sts2.Core.Saves.SaveManager", "SaveProfile"),

        // The combat replay the engine writes at the end of every fight. It lands in
        // the player's own profile directory, where it is the replay of the last
        // combat they fought - and a fight in somebody else's run is not one of
        // theirs. Measured: this is the one file a trainer fight changed once
        // everything else was byte identical. Patched at the writer rather than at
        // RunManager.WriteReplay, which only hands it the path: suppressing that
        // wrapper left the file changed anyway, so the write reaches the writer by a
        // path this process cannot intercept there.
        ("MegaCrit.Sts2.Core.Multiplayer.Replay.CombatReplayWriter", "WriteReplay"),

        // The run save and the run history: this fight is not a run anybody keeps.
        ("MegaCrit.Sts2.Core.Saves.SaveManager", "SaveRun"),
        ("MegaCrit.Sts2.Core.Saves.SaveManager", "SaveRunHistory"),
        ("MegaCrit.Sts2.Core.Saves.SaveManager", "IncrementNumReloads"),

        // Not writes, and on this list for exactly that reason. A run marks its own
        // relics seen as it starts and its rewards seen as they are offered, and those
        // calls only mutate the progress the game holds in memory - so the barrier
        // never saw them, and the mutation outlived the run it came from. The game
        // then wrote it out itself: measured in the retail client, NGame.Quit calls
        // SaveProgressFile with no trainer run live, which is an ordinary write the
        // barrier must not stop, of a progress state the trainer had already dirtied.
        // Seen once as a rotated progress backup whose content happened to match,
        // because the profile had already seen the trainer's relic; on a profile that
        // had not, the same path writes a discovery the player never made.
        ("MegaCrit.Sts2.Core.Saves.SaveManager", "MarkCardAsSeen"),
        ("MegaCrit.Sts2.Core.Saves.SaveManager", "MarkRelicAsSeen"),
        ("MegaCrit.Sts2.Core.Saves.SaveManager", "MarkPotionAsSeen"),

        // The tutorial marks, which reach the progress file by a path the entries
        // above do not cover. ProgressSaveManager.MarkFtueAsComplete calls
        // SaveProgress() itself rather than SaveManager.SaveProgressFile, so
        // suppressing that method leaves this one writing; the barrier has to name
        // these three. Suppressing them here also stops the in-memory mark, which is
        // the same reasoning as the seen-marks above.
        //
        // The trainer's own path reaches map_select_ftue from NMapScreen and
        // can_play_cards_ftue from NEndTurnButton, with more from NCardPlay,
        // CardPileCmd, NRewardsScreen, NCardRewardSelectionScreen and
        // NPotionContainer. Nothing measured it because SeenFtue short-circuits on
        // !EnableFtues and every profile it was measured on had tutorials off; a
        // player who left them on is the case that was never run.
        //
        // The two siblings write the same way and are here for the same reason. One
        // consequence is deliberate: the settings screen's reset-tutorials does
        // nothing while a trainer run is live, because doing something would mean
        // writing the player's progress file from inside somebody else's run.
        ("MegaCrit.Sts2.Core.Saves.SaveManager", "MarkFtueAsComplete"),
        ("MegaCrit.Sts2.Core.Saves.SaveManager", "SetFtuesEnabled"),
        ("MegaCrit.Sts2.Core.Saves.SaveManager", "ResetFtues"),

        // Outward-facing, and so worse than a local write: an achievement earned in
        // somebody else's fight is not the player's, and it cannot be taken back.
        ("MegaCrit.Sts2.Core.Achievements.AchievementsHelper", "CheckForDefeatedAllEnemiesAchievement"),
        ("MegaCrit.Sts2.Core.Achievements.AchievementsHelper", "AfterBossDefeated"),
        ("MegaCrit.Sts2.Core.Achievements.AchievementsHelper", "AfterRunEnded"),
    ];

    /// <summary>
    /// Installs the barrier. Called once, from mod start, before any trainer run can
    /// exist.
    /// </summary>
    internal static void Install(Harmony harmony)
    {
        var installed = Install(
            harmony,
            typeof(MegaCrit.Sts2.Core.Saves.SaveManager).Assembly,
            SuppressedWrites);

        Log.Info(
            $"[{RunmobileMod.ModId}] profile write barrier installed over " +
            $"{installed.ToString(System.Globalization.CultureInfo.InvariantCulture)} write(s); " +
            "inactive until a trainer run exists", 2);
    }

    internal static int Install(
        Harmony harmony,
        Assembly targetAssembly,
        IReadOnlyList<(string Type, string Method)> suppressedWrites)
    {
        // Two prefixes rather than one, because Harmony's __result is only valid on a
        // method that returns something. A single prefix declaring it would refuse to
        // patch every void write here, and a barrier that failed to install over half
        // its list is worse than no barrier: it would look installed.
        var skipVoid = new HarmonyMethod(typeof(ProfileWriteBarrier)
            .GetMethod(nameof(SkipVoidWrite), BindingFlags.NonPublic | BindingFlags.Static)!);
        var skipTask = new HarmonyMethod(typeof(ProfileWriteBarrier)
            .GetMethod(nameof(SkipTaskWrite), BindingFlags.NonPublic | BindingFlags.Static)!);
        var installed = 0;

        foreach (var (typeName, methodName) in suppressedWrites)
        {
            var type = targetAssembly.GetType(typeName)
                ?? throw new InvalidOperationException(
                    $"This build has no {typeName}, so the trainer cannot guarantee it writes nothing.");

            var methods = type
                .GetMethods(BindingFlags.Instance | BindingFlags.Static |
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(method => method.Name == methodName)
                .ToList();

            if (methods.Count == 0)
            {
                throw new InvalidOperationException(
                    $"This build's {typeName} has no '{methodName}', so the trainer cannot guarantee it " +
                    "writes nothing.");
            }

            foreach (var method in methods)
            {
                if (method.ReturnType == typeof(void))
                {
                    harmony.Patch(method, prefix: skipVoid);
                }
                else if (typeof(Task).IsAssignableFrom(method.ReturnType))
                {
                    harmony.Patch(method, prefix: skipTask);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"{typeName}.{methodName} returns {method.ReturnType.Name} on this build, and the " +
                        "barrier has no way to answer its callers without inventing a value.");
                }

                installed++;
            }
        }

        return installed;
    }

    /// <summary>
    /// Raises the barrier. Called before the trainer's run is constructed, so there
    /// is no window in which the run exists unprotected.
    /// </summary>
    internal static void Raise() => IsActive = true;

    /// <summary>
    /// Lowers it, once the trainer's run is gone. Everything the game writes for the
    /// player's own runs works normally again from here.
    /// </summary>
    internal static void Lower() => IsActive = false;

    /// <summary>The prefix on a write that returns nothing. Returning false skips the
    /// original, which is the whole of it.</summary>
    private static bool SkipVoidWrite() => !IsActive;

    /// <summary>
    /// The prefix on a write that returns a task.
    ///
    /// The task has to be answered as well as skipped: its callers await it, and a
    /// null there would take the game down in place of the write it was preventing.
    /// </summary>
    private static bool SkipTaskWrite(ref Task __result)
    {
        if (IsActive) __result = Task.CompletedTask;
        return !IsActive;
    }
}

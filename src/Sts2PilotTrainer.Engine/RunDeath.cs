using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The run's end on the player's death, ended headlessly where the retail client ends it.
///
/// <c>CreatureCmd.Kill</c> is the one place the game notices that every player is
/// dead, in a fight or out of one: it processes the loss where a fight is live and
/// then, under <c>TestMode.IsOff</c>, stops the music, calls
/// <c>RunManager.OnEnded(false)</c> and shows the game-over screen. The headless flag
/// is on here, so the whole block is skipped - the presentation with the run's end -
/// and a run whose player died outside a fight went on standing in its room with the
/// engine never told it was over. The recorder finishes a run at <c>OnEnded</c>
/// (<c>RunRecorder.RunEnded</c>) and the arbiter reads the decision a run ended on
/// there (<see cref="RunEnding"/>), so an end the engine never announced is a decision
/// neither host reads the way the retail client does: the recorder read the killing
/// event option once its page had moved on, and a replay held it to nothing.
///
/// This puts the engine half of that block back, at the instant the block runs: a
/// postfix on the collection form of <c>Kill</c>, which every kill funnels into,
/// runs once the kill's own task has completed - which is right after the block, the
/// last thing the method does - and calls <c>OnEnded(false)</c> under the block's own
/// conditions, the run in progress, not being cleaned up, and every player of the
/// killed creature's run dead. Nothing of the presentation is stood in for, and no
/// random stream is touched, so this is not one of <c>RestoreRetailBranches</c>'
/// four, which flip the flag and let the retail branch run whole; the branch here
/// cannot run whole because it dereferences the scene tree. The kill that follows a
/// win reaches <c>OnEnded</c> a second time this way, as it does in the client, and
/// <c>RunManager</c>'s own guard writes the history once. <c>docs/headless-fidelity.md</c>
/// owns it beside the others.
/// </summary>
internal static class RunDeath
{
    internal static void Install(Harmony harmony, List<string> failures)
    {
        var kill = typeof(CreatureCmd).GetMethod(
            nameof(CreatureCmd.Kill), BindingFlags.Public | BindingFlags.Static,
            [typeof(IReadOnlyCollection<Creature>), typeof(bool)]);
        if (kill is null)
        {
            failures.Add(
                "run death: CreatureCmd.Kill(IReadOnlyCollection<Creature>, bool) not found in this build, so a run " +
                "whose player dies is never ended headlessly the way the client ends it.");
            return;
        }

        try
        {
            harmony.Patch(kill, postfix: new HarmonyMethod(typeof(RunDeath).GetMethod(nameof(After), BindingFlags.NonPublic | BindingFlags.Static)!));
        }
        catch (Exception ex)
        {
            failures.Add($"run death CreatureCmd.Kill: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void After(IReadOnlyCollection<Creature> creatures, Task __result)
    {
        // The block reads the run off the first player among the killed, and only a
        // kill that took a player can have emptied the run
        var runState = creatures.FirstOrDefault(creature => creature.IsPlayer)?.Player?.RunState;
        if (runState is null) return;

        if (__result.IsCompleted)
        {
            EndTheRunIfEveryPlayerIsDead(runState);
            return;
        }

        // A kill still awaiting something ends the run where the block would have,
        // once its own task completes and on the thread that completes it
        __result.ContinueWith(
            _ => EndTheRunIfEveryPlayerIsDead(runState),
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static void EndTheRunIfEveryPlayerIsDead(IRunState runState)
    {
        var manager = RunManager.Instance;
        if (manager is not { IsInProgress: true, IsCleaningUp: false }) return;
        if (!runState.Players.All(player => player.Creature.IsDead)) return;

        manager.OnEnded(isVictory: false);
    }
}

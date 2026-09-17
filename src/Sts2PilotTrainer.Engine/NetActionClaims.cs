using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Which of the game actions a locally issued net action becomes is watched by what,
/// so a game action nothing here claims can be refused at the seam it enters by.
///
/// Every game action a player issues in a singleplayer game passes through
/// <c>ActionQueueSynchronizer.RequestEnqueue</c>, so that one member is where a
/// stranger - a type a mod added, or a game update introduced - is met once and by
/// name. This table is the recorder's account of the actions this build has: each is
/// claimed by the observer or patch that records the decision it carries, excused as
/// the engine's own bookkeeping, or named as the console's, which marks a run
/// non-standard. It is held in both directions against <see cref="DecisionSurface.NetActions"/>,
/// the walk off the assembly, so a build that adds a twelfth action fails a test
/// before a recorder meets it.
///
/// The claims name game members and observer roles rather than the recorder's own
/// patch classes, because the engine cannot see the mod; the recorder's tests hold
/// the two together.
/// </summary>
public static class NetActionClaims
{
    public enum Disposition
    {
        /// <summary>A decision something records.</summary>
        Claimed,

        /// <summary>The engine's own bookkeeping, never a decision.</summary>
        EngineDriven,

        /// <summary>The console's, which no recorded run may carry; met here only in a
        /// networked game, where nothing is recorded.</summary>
        NonStandard,
    }

    /// <summary>What watches one game action.</summary>
    /// <param name="Disposition">Whether it is recorded, excused or the console's.</param>
    /// <param name="WatchedBy">The observer or the game member the recorder watches it at.</param>
    public sealed record Claim(Disposition Disposition, string WatchedBy);

    private const string FightObserver = "the fight observer, inside a fight";

    /// <summary>Every game action this build's net actions become, by type.</summary>
    public static IReadOnlyDictionary<Type, Claim> All { get; } = new Dictionary<Type, Claim>
    {
        [typeof(PlayCardAction)] = new(Disposition.Claimed, FightObserver),
        [typeof(EndPlayerTurnAction)] = new(Disposition.Claimed, FightObserver),
        [typeof(UndoEndPlayerTurnAction)] = new(Disposition.Claimed, FightObserver),
        [typeof(UsePotionAction)] = new(
            Disposition.Claimed, $"{FightObserver}; {nameof(PotionModel)}.{nameof(PotionModel.EnqueueManualUse)} outside one"),
        [typeof(DiscardPotionGameAction)] = new(
            Disposition.Claimed, $"{FightObserver}; {nameof(DiscardPotionGameAction)}..ctor outside one"),
        [typeof(VoteForMapCoordAction)] = new(
            Disposition.Claimed, $"{nameof(RunManager)}.{nameof(RunManager.EnterMapCoord)}, which the move's execution reaches"),
        [typeof(MoveToMapCoordAction)] = new(
            Disposition.Claimed, $"{nameof(RunManager)}.{nameof(RunManager.EnterMapCoord)}, which the move's execution reaches"),
        [typeof(PickRelicAction)] = new(
            Disposition.Claimed,
            $"{nameof(TreasureRoomRelicSynchronizer)}.{nameof(TreasureRoomRelicSynchronizer.PickRelicLocally)}, which enqueues it"),
        [typeof(VoteToMoveToNextActAction)] = new(
            Disposition.Claimed,
            $"{nameof(ActChangeSynchronizer)}.{nameof(ActChangeSynchronizer.SetLocalPlayerReady)}, which enqueues it"),
        [typeof(ReadyToBeginEnemyTurnAction)] = new(
            Disposition.EngineDriven, "the engine, when the player's turn has ended; nobody decides it"),
        [typeof(ConsoleCmdGameAction)] = new(
            Disposition.NonStandard,
            $"{nameof(DevConsole)}, whose singleplayer branch builds no action; a run it is met in is non-standard"),
    };

    /// <summary>The claim on a game action's type, or null for a stranger.</summary>
    public static Claim? For(Type gameAction) =>
        All.TryGetValue(gameAction, out var claim) ? claim : null;

    /// <summary>
    /// Every way the table and the build disagree: a game action the build's net
    /// actions become that no row claims, and a row naming an action no net action
    /// becomes. Empty on the build the table was written for.
    /// </summary>
    public static IReadOnlyList<string> Verify()
    {
        var walked = DecisionSurface.NetActions();
        var claimed = All.Keys.Select(type => type.Name).ToList();
        return walked.Except(claimed, StringComparer.Ordinal)
            .Select(name => $"{name} is a game action a net action becomes on this build, and nothing claims it.")
            .Concat(claimed.Except(walked, StringComparer.Ordinal)
                .Select(name => $"{name} is claimed, and no net action on this build becomes it."))
            .ToList();
    }
}

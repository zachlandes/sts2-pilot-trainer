using System.Reflection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>How a recorded decision and the game's own command are related.</summary>
public enum EngineCommandKind
{
    /// <summary>The driver calls this member to make the decision. Most verbs are
    /// this: the retail client calls the same member when a player clicks.</summary>
    Issued,

    /// <summary>The engine calls out and the decision is the answer handed back.
    /// A card screen suspends inside the call that opened it and pulls the player's
    /// answer through a seam, so there is nothing for a driver to send.</summary>
    Answered,
}

/// <summary>
/// Which of the game's own members each decision in <see cref="ActionVerb"/> maps
/// onto, in one table.
///
/// <c>AGENTS.md</c> says to find the engine's own command before writing one, and
/// until now that rule lived only in <see cref="RunDriver"/>'s handlers, where it
/// could only be read by reading every handler. Two readers need it in one place: the
/// driver, whose refusal for an unimplemented verb is derived from this table rather
/// than restated beside it, and the recorder, which observes the same members from
/// the other end - a decision the driver issues is a decision a running game
/// announces.
///
/// It is a description, not a dispatch table. Each handler takes different arguments
/// and does different checking, and collapsing them into one signature would hide
/// exactly the per-verb refusals that make a replay trustworthy. What this fixes is
/// that the set of verbs this build implements is written down once.
///
/// Every row is checkable: <see cref="Verify"/> asks the loaded game assembly whether
/// the named member still exists, so a patch that renames one is a failing check here
/// rather than a refusal in the middle of somebody's replay.
/// </summary>
public static class EngineCommands
{
    private static readonly EngineCommand[] Table =
    [
        new()
        {
            Verb = ActionVerb.ChooseNeowBlessing,
            Type = typeof(EventSynchronizer),
            Member = nameof(EventSynchronizer.ChooseLocalOption),
            Kind = EngineCommandKind.Issued,
            Note = "The opening blessing is an event like any other; only its option list is special.",
        },
        new()
        {
            Verb = ActionVerb.ChooseEventOption,
            Type = typeof(EventSynchronizer),
            Member = nameof(EventSynchronizer.ChooseLocalOption),
            Kind = EngineCommandKind.Issued,
            Note = "The same member, with the event's own id checked first.",
        },
        new()
        {
            Verb = ActionVerb.MapMove,
            Type = typeof(RunManager),
            Member = nameof(RunManager.EnterMapCoord),
            Kind = EngineCommandKind.Issued,
            Note =
                "Headlessly this is the whole move. Inside the retail client it is the middle of one, and " +
                "the host supplies the screen's own travel; see docs/in-game-host.md.",
        },
        new()
        {
            Verb = ActionVerb.PlayCard,
            Type = typeof(PlayCardAction),
            Member = ConstructorMember,
            Kind = EngineCommandKind.Issued,
            Note = "Enqueued on the run's own action queue, which is what a clicked card does.",
        },
        new()
        {
            Verb = ActionVerb.EndTurn,
            Type = typeof(PlayerCmd),
            Member = nameof(PlayerCmd.EndTurn),
            Kind = EngineCommandKind.Issued,
            Note = "canBackOut is false: a recorded turn end was not taken back.",
        },
        new()
        {
            Verb = ActionVerb.ClaimReward,
            Type = typeof(RewardsSetSynchronizer),
            Member = nameof(RewardsSetSynchronizer.SelectLocalReward),
            Kind = EngineCommandKind.Issued,
            Note = "The reward is found by the kind the loot screen names, never by position.",
        },
        new()
        {
            Verb = ActionVerb.TakeCard,
            Type = typeof(RewardsSetSynchronizer),
            Member = nameof(RewardsSetSynchronizer.SelectLocalReward),
            Kind = EngineCommandKind.Issued,
            Note =
                "The same member for the card reward, whose own screen is then answered through " +
                "ICardSelector.",
        },
        new()
        {
            Verb = ActionVerb.SkipRewards,
            Type = typeof(RewardsSetSynchronizer),
            Member = nameof(RewardsSetSynchronizer.SkipLocalRewardsSet),
            Kind = EngineCommandKind.Issued,
            Note = "Dismissing a loot screen with something still on it is a decision, so it has a verb.",
        },
        new()
        {
            Verb = ActionVerb.SelectCardFromScreen,
            Type = typeof(ICardSelector),
            Member = nameof(ICardSelector.GetSelectedCards),
            Kind = EngineCommandKind.Answered,
            Note =
                "The engine asks. The driver queues the manifest's picks before the action that opens the " +
                "screen and confirms afterwards that a screen consumed each one.",
        },
        new()
        {
            Verb = ActionVerb.ChooseRestSiteOption,
            Type = typeof(RestSiteSynchronizer),
            Member = nameof(RestSiteSynchronizer.ChooseLocalOption),
            Kind = EngineCommandKind.Issued,
            Note =
                "The option is found by the id it declares, never by position: which options a rest site " +
                "offers depends on the run that reached it.",
        },
        new()
        {
            Verb = ActionVerb.TakeChestRelic,
            Type = typeof(TreasureRoomRelicSynchronizer),
            Member = nameof(TreasureRoomRelicSynchronizer.PickRelicLocally),
            Kind = EngineCommandKind.Issued,
            Note = "The chest's relics were rolled by the engine when the room was entered.",
        },
        new()
        {
            Verb = ActionVerb.SkipChestRelic,
            Type = typeof(TreasureRoomRelicSynchronizer),
            Member = nameof(TreasureRoomRelicSynchronizer.SkipRelicLocally),
            Kind = EngineCommandKind.Issued,
            Note = "The engine's own name for leaving the relic, and its own way of recording that.",
        },
        new()
        {
            Verb = ActionVerb.ProceedToNextAct,
            Type = typeof(ActChangeSynchronizer),
            Member = nameof(ActChangeSynchronizer.SetLocalPlayerReady),
            Kind = EngineCommandKind.Issued,
            Note =
                "A vote rather than a call. RunManager.EnterNextAct is what it leads to, and calling that " +
                "directly would skip the act floor the vote advances.",
        },
        new()
        {
            Verb = ActionVerb.ShopPurchase,
            Type = typeof(MerchantEntry),
            Member = nameof(MerchantEntry.OnTryPurchaseWrapper),
            Kind = EngineCommandKind.Issued,
            Note =
                "One member for all five kinds, because the merchant's own entries are what differ. A card " +
                "removal reaches OneOffSynchronizer.DoLocalMerchantCardRemoval through it, and its screen " +
                "is answered as any other card screen is.",
        },
        new()
        {
            Verb = ActionVerb.UsePotion,
            Type = typeof(PotionModel),
            Member = nameof(PotionModel.EnqueueManualUse),
            Kind = EngineCommandKind.Issued,
            Note = "What the potion holder in the retail client calls when a potion is dragged onto a target.",
        },
        new()
        {
            Verb = ActionVerb.DiscardPotion,
            Type = typeof(DiscardPotionGameAction),
            Member = ConstructorMember,
            Kind = EngineCommandKind.Issued,
            Note = "Enqueued on the run's own queue, which is what the potion popup's discard button does.",
        },
    ];

    /// <summary>
    /// Verbs the format names and this build deliberately does not map, with the
    /// reason. Kept beside the table rather than as an absence, because "we have not
    /// got to it" and "there is nothing to map" are different answers and a reader
    /// deserves to be told which.
    /// </summary>
    private static readonly IReadOnlyDictionary<ActionVerb, string> Unmapped =
        new SortedDictionary<ActionVerb, string>
        {
            [ActionVerb.CloseShop] =
                "Nothing to map. The merchant is a room the run leaves by moving on the map, and the shop " +
                "screen's own proceed button only hides the screen - MerchantRoom.Exit returns immediately " +
                "under the headless flag. Closing a shop is presentation, like ProceedToMap.",
            [ActionVerb.SelectHandCards] =
                "Nothing of its own to map. Every card screen this build opens - over the hand, the deck or " +
                "a pile - is answered through the one ICardSelector seam, by position in the list that " +
                "screen offered, which SelectCardFromScreen already names. A prompt over the hand offers a " +
                "filtered subset of it, so a hand position would not even be the right coordinate. A second " +
                "verb here would be a second name for one thing.",
            [ActionVerb.ProceedToMap] =
                "Returning to the map is presentation, not a decision: the engine is already standing " +
                "wherever the previous action left it. See docs/proof-of-concept-path.md.",
        };

    /// <summary>How a constructor is named in a row, since it has no name of its own.</summary>
    public const string ConstructorMember = ".ctor";

    /// <summary>Every mapped verb, in the order the table declares them.</summary>
    public static IReadOnlyList<EngineCommand> All => Table;

    /// <summary>The game's command for this decision, or null when this build maps none.</summary>
    public static EngineCommand? For(ActionVerb verb) =>
        Table.FirstOrDefault(command => command.Verb == verb);

    /// <summary>Whether this build maps a game command onto this decision.</summary>
    public static bool Maps(ActionVerb verb) => For(verb) is not null;

    /// <summary>Why this build maps nothing onto this decision, or null when it does.</summary>
    public static string? UnmappedReason(ActionVerb verb) =>
        Unmapped.TryGetValue(verb, out var reason) ? reason : null;

    /// <summary>
    /// Everything wrong with the table, as sentences, or empty when it is sound.
    ///
    /// Two questions, both of which a game update can change the answer to: does the
    /// loaded assembly still have every member a row names, and is every verb in the
    /// format accounted for exactly once. A verb in neither list is a decision nobody
    /// decided about, which is the quiet failure this table exists to prevent.
    /// </summary>
    public static IReadOnlyList<string> Verify()
    {
        var problems = new List<string>();

        foreach (var command in Table.Where(command => !Exists(command)))
        {
            problems.Add(
                $"{command.Verb} is mapped onto {command.Describe()}, which the loaded game assembly does " +
                "not have. Either the game renamed it or the row is wrong; replaying a history that uses " +
                "this verb would fail somewhere less obvious.");
        }

        foreach (var duplicate in Table.GroupBy(command => command.Verb).Where(group => group.Count() > 1))
        {
            problems.Add($"{duplicate.Key} is mapped more than once; a verb has one command.");
        }

        foreach (var verb in Enum.GetValues<ActionVerb>())
        {
            var mapped = Maps(verb);
            var excused = Unmapped.ContainsKey(verb);
            if (mapped && excused)
            {
                problems.Add(
                    $"{verb} is both mapped and listed as unmapped. One of the two is stale.");
            }
            else if (!mapped && !excused)
            {
                problems.Add(
                    $"{verb} is in the format's alphabet and this table says nothing about it. Every verb " +
                    "is either mapped onto the game's own command or has a written reason it is not.");
            }
        }

        return problems;
    }

    /// <summary>
    /// Pushes one action per mapped verb through a real driver, and keeps only the
    /// complaint that the switch has no case for it.
    ///
    /// <see cref="Verify"/> asks the game whether a member still exists; only this
    /// asks whether the driver still knows what to do with the verb that names it.
    /// Every refusal but one is discarded: a decision made in the wrong place is
    /// supposed to be refused, and which refusal it earns is the verb's own business.
    /// The single outcome this rejects is the driver saying the table names a command
    /// it cannot handle.
    ///
    /// One run for all of them, because the engine's RunManager holds one run per
    /// process and refuses a second. So a verb that succeeds moves this run on and
    /// the verbs after it are asked about a later moment - which changes which
    /// refusal each one earns, and not whether the driver has a case for it, which is
    /// the only thing being asked.
    ///
    /// Needs the game, so it is a command rather than a unit test; see the
    /// <c>engine-commands --probe</c> verb.
    /// </summary>
    public static IReadOnlyList<string> ProbeDriver()
    {
        var problems = new List<string>();
        var session = new GameSession();
        session.StartRun(
            "PR0BE", "CHARACTER.IRONCLAD", 0, "standard",
            ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"]);
        using var driver = new RunDriver(session);
        driver.EnterFirstRoom();

        foreach (var command in Table)
        {
            try
            {
                driver.Apply(new ActionRecord
                {
                    Seq = 0,
                    Verb = command.Verb,
                    Args = ProbeArguments,
                    Source = FactSource.Declared,
                });
            }
            catch (Exception exception)
            {
                // Everything else is expected: the verb's own refusal, or the
                // engine's complaint about a decision made at the wrong moment.
                // Which one it earns is that verb's business, not this probe's.
                if (exception is EngineException &&
                    exception.Message.Contains(SwitchDriftMarker, StringComparison.Ordinal))
                {
                    problems.Add(exception.Message);
                }
            }
        }

        return problems;
    }

    /// <summary>The phrase <see cref="RunDriver"/> uses when the table names a
    /// command it has no case for, named once so the probe cannot look for a
    /// sentence that has since been reworded.</summary>
    internal const string SwitchDriftMarker = "has no case for";

    /// <summary>
    /// Enough arguments for any verb's handler to be reached. Not enough for one to
    /// succeed, and deliberately so: the probe applies each to a run standing in its
    /// opening event, where almost nothing is the right moment. The driver reads only
    /// the names its own handler needs, so one set serves every verb.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ProbeArguments =
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["act"] = "0",
            ["card_id"] = "CARD.STRIKE_IRONCLAD",
            ["column"] = "0",
            ["event_id"] = "EVENT.PROBE",
            ["hand_index"] = "0",
            ["option_index"] = "0",
            ["reward_type"] = "gold",
            ["row"] = "0",
        };

    private static bool Exists(EngineCommand command) =>
        command.Member == ConstructorMember
            ? command.Type.GetConstructors(Everything).Length > 0
            : command.Type.GetMember(command.Member, Everything).Length > 0;

    private const BindingFlags Everything =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
}

/// <summary>One recorded decision and the game member it corresponds to.</summary>
public sealed record EngineCommand
{
    public required ActionVerb Verb { get; init; }

    /// <summary>The game type that declares the member.</summary>
    public required Type Type { get; init; }

    /// <summary>The member's name, or <see cref="EngineCommands.ConstructorMember"/>.</summary>
    public required string Member { get; init; }

    public required EngineCommandKind Kind { get; init; }

    /// <summary>Why this member and not another, in one or two sentences.</summary>
    public required string Note { get; init; }

    /// <summary>How a diagnostic names this command to a person.</summary>
    public string Describe() => $"{Type.Name}.{Member}";
}

using System.Reflection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Mod;

/// <summary>How the retail client is made to issue a recorded decision.</summary>
internal enum ClientIssue
{
    /// <summary>The driver calls the engine member <c>EngineCommands</c> names, and the
    /// screen follows through the synchronizer's own events. The retail button's
    /// handler does nothing but call that member.</summary>
    EngineCall,

    /// <summary>The engine member is the middle of what the button does, so the host
    /// supplies the screen's own command through <c>RunningGameCommands</c> and the
    /// driver issues that instead. The map move is the precedent.</summary>
    ScreenCommand,
}

/// <summary>
/// The retail client's own command for each decision it issues, in one table beside
/// <see cref="EngineCommands"/>.
///
/// The engine table says which game member a decision is. This says how a running
/// client is made to issue it: the screen handler the retail button reaches, whether
/// the driver calls the engine member or the host supplies the screen's command, and
/// the members the deviation lock holds so that nobody but the recording can make
/// the decision while it is being shown. It is the client half of the same mechanical
/// guarantee the engine half has had: a patch that renames a screen handler or a lock
/// target fails <see cref="Verify"/> the day the assemblies are bootstrapped, rather
/// than greying a row or aborting a journey in front of a player.
///
/// It is not a second alphabet. Every row's verb is <see cref="ActionVerb"/>'s, every
/// row names the <see cref="EngineCommands"/> row it reaches, and the set of verbs
/// here is held equal to <see cref="RetailPlayback.Verbs"/> - the one declaration the
/// driver enforces and the library reads - in both directions. Adding a verb to the
/// client means a row here, a member of <c>RunningGameCommands</c> where the issue is
/// a screen's, and the lock that keeps it the recording's; this table is where that is
/// written down once.
///
/// It lives in the mod because it names Godot node types, which the engine owner must
/// not; <c>./scripts/arbiter engine-commands</c> therefore cannot print it, and
/// <c>ClientCommandTableTests</c> is its check.
/// </summary>
internal static class ClientCommands
{
    private static readonly ClientCommand[] Table =
    [
        new()
        {
            Verb = ActionVerb.ChooseNeowBlessing,
            HandlerType = typeof(NEventRoom),
            Handler = nameof(NEventRoom.OptionButtonClicked),
            Issue = ClientIssue.EngineCall,
            Locks = [(typeof(EventSynchronizer), nameof(EventSynchronizer.ChooseLocalOption))],
            Note =
                "The option button's handler calls ChooseLocalOption and nothing else, so the driver calls " +
                "the engine member and the event screen follows. The reveal lights the option row first.",
        },
        new()
        {
            Verb = ActionVerb.ChooseEventOption,
            HandlerType = typeof(NEventRoom),
            Handler = nameof(NEventRoom.OptionButtonClicked),
            Issue = ClientIssue.EngineCall,
            Locks = [(typeof(EventSynchronizer), nameof(EventSynchronizer.ChooseLocalOption))],
            Note = "The same handler and the same lock as the opening blessing; only the option list differs.",
        },
        new()
        {
            Verb = ActionVerb.MapMove,
            HandlerType = typeof(NMapScreen),
            Handler = nameof(NMapScreen.TravelToMapCoord),
            Issue = ClientIssue.ScreenCommand,
            Locks = [(typeof(RunManager), nameof(RunManager.EnterMapCoord))],
            Note =
                "EnterMapCoord is the middle of what a clicked node does: measured, entering the coordinate " +
                "alone leaves the client on the map with the room built behind it and its combat never dealt. " +
                "The host supplies RunningGameCommands.Travel, which is the map screen's own travel.",
        },
        new()
        {
            Verb = ActionVerb.SelectCardFromScreen,
            HandlerType = typeof(NCardGridSelectionScreen),
            Handler = "OnCardClicked",
            Issue = ClientIssue.ScreenCommand,
            Locks =
            [
                (typeof(NDeckCardSelectScreen), "OnCardClicked"),
                (typeof(NDeckTransformSelectScreen), "OnCardClicked"),
            ],
            Note =
                "There is no engine command at all: the screen is the game's own, and the handler is the " +
                "grid screen's abstract OnCardClicked, which the removal and transform screens each " +
                "override - so the lock hangs on both overrides. The host supplies RunningGameCommands." +
                "SelectCard, which presses the card's holder the way a click does.",
        },
    ];

    /// <summary>
    /// Members the recorded-fight journey patches that are not any decision's, with
    /// the reason. Kept beside the table so that a patch nobody accounted for is a
    /// failing check rather than an absence.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Excused =
        new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            [Name(typeof(NRewardButton), "OnRelease")] =
                "The loot lock: after the player's own fight, nothing is taken off the loot screen until the " +
                "post-fight choice is made. Not a decision the recording makes.",
            [Name(typeof(NRewardsScreen), "OnProceedButtonPressed")] =
                "The loot lock's other half: the post-fight choice is the only way off the loot.",
            [Name(typeof(RunManager), nameof(RunManager.CleanUp))] =
                "The teardown hook: the game has cleaned the run up and the journey has to know.",
            [Name(typeof(NGame), nameof(NGame.ReturnToMainMenu))] =
                "The return hook: a result owed after the run is gone is shown on the menu it returns to.",
        };

    /// <summary>Every row, in the order the table declares them.</summary>
    internal static IReadOnlyList<ClientCommand> All => Table;

    /// <summary>The client's command for this decision, or null when it issues none.
    /// A loop rather than a lambda: a closure over a sibling assembly's enum is a class
    /// with a field of that type, which stops the mod loading.</summary>
    internal static ClientCommand? For(ActionVerb verb)
    {
        foreach (var command in Table)
        {
            if (command.Verb == verb) return command;
        }

        return null;
    }

    /// <summary>
    /// Everything wrong with the table on this build, as sentences, or empty when it is
    /// sound.
    ///
    /// Four questions. Does the loaded game still have every handler and every lock
    /// target a row names. Is every row's verb one <see cref="EngineCommands"/> maps.
    /// Is the set of verbs here exactly <see cref="RetailPlayback.Verbs"/>, so the
    /// client issues what the library offers and nothing else. And is every member the
    /// journey patches either a row's lock or excused by name - a lock hung on a member
    /// a build renamed does not refuse loudly, it is simply not there, and a player can
    /// then take a decision the recording owns.
    /// </summary>
    internal static IReadOnlyList<string> Verify()
    {
        var problems = new List<string>();

        foreach (var command in Table)
        {
            if (!Exists(command.HandlerType, command.Handler))
            {
                problems.Add(
                    $"{command.Verb} is issued through {command.DescribeHandler()}, which the loaded game " +
                    "assembly does not have. Either the game renamed it or the row is wrong.");
            }

            foreach (var (type, member) in command.Locks.Where(lockTarget => !Exists(lockTarget.Type, lockTarget.Member)))
            {
                problems.Add(
                    $"{command.Verb} is kept the recording's by a lock on {Name(type, member)}, which the " +
                    "loaded game assembly does not have. A lock on a renamed member is no lock at all.");
            }

            if (!EngineCommands.Maps(command.Verb))
            {
                problems.Add(
                    $"{command.Verb} has a client command and no engine command. The client issues the game's " +
                    "own member, and EngineCommands is where that member is named.");
            }
        }

        foreach (var duplicate in Table.GroupBy(command => command.Verb).Where(group => group.Count() > 1))
        {
            problems.Add($"{duplicate.Key} has more than one client command; a verb has one.");
        }

        var declared = RetailPlayback.Verbs.ToHashSet();
        var tabled = Table.Select(command => command.Verb).ToHashSet();
        foreach (var verb in declared.Except(tabled))
        {
            problems.Add(
                $"{verb} is in RetailPlayback.Verbs and has no row here, so the library offers a decision this " +
                "table says nothing about how the client issues.");
        }

        foreach (var verb in tabled.Except(declared))
        {
            problems.Add(
                $"{verb} has a row here and is not in RetailPlayback.Verbs, so the client knows how to issue " +
                "a decision the driver refuses and the library never offers.");
        }

        var locks = Table.SelectMany(command => command.Locks).Select(target => Name(target.Type, target.Member))
            .ToHashSet(StringComparer.Ordinal);
        var patched = PatchTargets.Targets(RecordedFightModule.PatchClasses);
        foreach (var target in patched.Where(target => !locks.Contains(target) && !Excused.ContainsKey(target)))
        {
            problems.Add(
                $"The recorded-fight journey patches {target}, which is neither a row's lock nor excused " +
                "here. Every member it hangs on is accounted for by name.");
        }

        foreach (var excused in Excused.Keys.Where(name => !patched.Contains(name, StringComparer.Ordinal)))
        {
            problems.Add($"{excused} is excused here and nothing patches it. The excuse is stale.");
        }

        return problems;
    }

    private static string Name(Type type, string member) => $"{type.Name}.{member}";

    private static bool Exists(Type type, string member) => type.GetMember(member, Everything).Length > 0;

    private const BindingFlags Everything =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static |
        BindingFlags.FlattenHierarchy;
}

/// <summary>One recorded decision, and how a running retail client issues it.</summary>
internal sealed record ClientCommand
{
    /// <summary>The verb, held as an int: a field of a sibling assembly's value type
    /// stops the whole mod loading, one startup phase before its siblings can be
    /// resolved. The same reason <c>RecordedFightRun._phase</c> is an int.</summary>
    private readonly int _verb;

    public required ActionVerb Verb
    {
        get => (ActionVerb)_verb;
        init => _verb = (int)value;
    }

    /// <summary>The screen type whose handler the retail button reaches.</summary>
    public required Type HandlerType { get; init; }

    /// <summary>That handler's name. A string where the member is protected.</summary>
    public required string Handler { get; init; }

    public required ClientIssue Issue { get; init; }

    /// <summary>The members the deviation lock holds while the recording is deciding,
    /// so that only the journey can make this decision.</summary>
    public required IReadOnlyList<(Type Type, string Member)> Locks { get; init; }

    /// <summary>Why this handler and this way of issuing it, in a sentence or two.</summary>
    public required string Note { get; init; }

    /// <summary>The engine command this reaches: the same row <c>EngineCommands</c>
    /// maps the verb onto, never a second one.</summary>
    public EngineCommand Engine => EngineCommands.For(Verb)
        ?? throw new InvalidOperationException($"{Verb} has no engine command; Verify says so.");

    public string DescribeHandler() => $"{HandlerType.Name}.{Handler}";
}

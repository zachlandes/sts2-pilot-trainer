using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Saves;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The retail soak plays what a click plays and nothing else, held over its compiled
/// IL through the engine's one reader rather than over its source.
///
/// The game's own autoplayer makes three kinds of run no replay reproduces - powers
/// applied through <c>PowerCmd</c>, an event's enemies killed through
/// <c>CreatureCmd.Kill</c>, every prompt answered engine-side through
/// <c>CardSelectCmd.UseSelector</c> and cards played through <c>CardCmd.AutoPlay</c>,
/// which spends no energy and reaches no executor - and makes four kinds of profile
/// write the mod must not: a seed override, the fast-mode preference, the FTUE flags
/// and epoch overrides. The soak probe measured each on the retail client; this is
/// what keeps the soak off all of them, every merge, by the same walk
/// <see cref="FixtureProvenanceTests"/> reads a construction with. Every method of the
/// soak's own types is read - closures and async state machines included, because a
/// call inside a lambda is one <c>call</c> like any other - and the game's own
/// handlers the soak reuses are read one level deep beside them, so a game update
/// that put a cheat into a handler the soak trusts is a red test rather than a
/// recording nobody can replay.
///
/// The module also draws nothing, and that is read the same way: no node is added,
/// no scene instantiated, and the shell's drawing gate is never even asked.
/// </summary>
public sealed class RetailSoakGuardTests
{
    /// <summary>The members a soak call to would make a run no replay reproduces or
    /// a write to the player's profile, each with why.</summary>
    private static IReadOnlyList<(Func<MethodBase, bool> Matches, string Because)> Refused =>
    [
        (member => member.DeclaringType == typeof(PowerCmd),
            "PowerCmd applies a power no decision records; the game's combat handler cheats with it"),
        (member => member.DeclaringType == typeof(CreatureCmd) && member.Name == nameof(CreatureCmd.Kill),
            "CreatureCmd.Kill ends an enemy no decision records; the game's event handler cheats with it"),
        (member => member.DeclaringType == typeof(CardSelectCmd) && member.Name == nameof(CardSelectCmd.UseSelector),
            "CardSelectCmd.UseSelector answers every prompt engine-side, so the recorder sees no answer"),
        (member => member.DeclaringType == typeof(CardCmd) && member.Name == nameof(CardCmd.AutoPlay),
            "CardCmd.AutoPlay spends no energy, constructs no PlayCardAction and wedges the fight capture"),
        (member => member.DeclaringType == typeof(NGame) && member.Name == $"set_{nameof(NGame.DebugSeedOverride)}",
            "the seed override is a debug path the game's own lobby reads ahead of the player's seed"),
        (member => member.DeclaringType == typeof(SaveManager) && member.Name is nameof(SaveManager.MarkFtueAsComplete)
            or nameof(SaveManager.SetFtuesEnabled) or nameof(SaveManager.ResetFtues),
            "an FTUE flag is a profile write"),
        (member => member.DeclaringType == typeof(SaveManager) && member.Name is nameof(SaveManager.ObtainEpoch)
            or nameof(SaveManager.ObtainEpochOverride) or nameof(SaveManager.RevealEpoch) or nameof(SaveManager.GrantNextUnlock),
            "an epoch or unlock is a profile write"),
        (member => member.DeclaringType == typeof(PrefsSave) && member.Name.StartsWith("set_", StringComparison.Ordinal),
            "a preference is the player's, written once by a person; fast mode included"),
        (member => member.DeclaringType == typeof(SettingsSave) && member.Name.StartsWith("set_", StringComparison.Ordinal),
            "a setting is the player's"),
    ];

    /// <summary>What drawing looks like in IL: a node added or a scene instantiated,
    /// and the gate a surface asks before it draws.</summary>
    private static IReadOnlyList<(Func<MethodBase, bool> Matches, string Because)> Drawing =>
    [
        (member => member.DeclaringType == typeof(Node) && member.Name is nameof(Node.AddChild) or "AddSibling",
            "the soak adds nothing to the scene tree"),
        (member => member.Name == "AddChildSafely", "the soak adds nothing to the scene tree"),
        (member => member.DeclaringType == typeof(PackedScene) && member.Name == nameof(PackedScene.Instantiate),
            "the soak instantiates no scene"),
        (member => member.DeclaringType == typeof(RunmobileMod) && member.Name == $"get_{nameof(RunmobileMod.MayDraw)}",
            "a module that draws nothing never asks whether it may"),
    ];

    private static IReadOnlyList<Type> SoakTypes => [typeof(RetailSoak), typeof(RetailSoakModule)];

    [GameFact]
    public void TheSoakCallsNothingThatCheatsOrWritesTheProfile()
    {
        var offenders = Offenders(SoakMethods(), Refused).ToList();

        Assert.True(offenders.Count == 0, "The retail soak calls a member it refuses:\n" + string.Join("\n", offenders));
    }

    [GameFact]
    public void TheSoakDrawsNothing()
    {
        var offenders = Offenders(SoakMethods(), Drawing).ToList();

        Assert.True(offenders.Count == 0, "The retail soak draws:\n" + string.Join("\n", offenders));
    }

    /// <summary>The game's handlers the soak reuses unchanged, one level deep: the two
    /// it replaces are the ones that cheat, and these must not have started.</summary>
    [GameFact]
    public void EveryReusedGameHandlerIsCleanOneLevelDeep()
    {
        _ = ChoiceEntryPoints.Game;
        var methods = RetailSoak.ReusedGameHandlers.SelectMany(MethodsOf).ToList();
        Assert.NotEmpty(methods);

        var offenders = Offenders(methods, Refused).ToList();

        Assert.True(
            offenders.Count == 0,
            "A game handler the soak reuses calls a member the soak refuses; replace it the way the combat and " +
            "event handlers were replaced:\n" + string.Join("\n", offenders));
    }

    /// <summary>The two handlers the soak replaces are refused by the same walk, so
    /// the walk is known to see the calls it exists to refuse.</summary>
    [GameFact]
    public void TheWalkSeesTheCheatsInTheHandlersTheSoakReplaces()
    {
        _ = ChoiceEntryPoints.Game;
        var combat = ChoiceEntryPoints.Game.GetType("MegaCrit.Sts2.Core.AutoSlay.Handlers.Rooms.CombatRoomHandler")!;
        var @event = ChoiceEntryPoints.Game.GetType("MegaCrit.Sts2.Core.AutoSlay.Handlers.Rooms.EventRoomHandler")!;

        var offenders = Offenders(MethodsOf(combat).Concat(MethodsOf(@event)).ToList(), Refused).ToList();

        Assert.Contains(offenders, line => line.Contains("PowerCmd", StringComparison.Ordinal));
        Assert.Contains(offenders, line => line.Contains("CreatureCmd.Kill", StringComparison.Ordinal));
        Assert.Contains(offenders, line => line.Contains("CardCmd.AutoPlay", StringComparison.Ordinal));
        Assert.DoesNotContain(combat, RetailSoak.ReusedGameHandlers);
        Assert.DoesNotContain(@event, RetailSoak.ReusedGameHandlers);
    }

    /// <summary>The walk reads a call inside a lambda and inside an async method's
    /// state machine, which is where a cheat would hide from a walk over declared
    /// methods alone.</summary>
    [GameFact]
    public void TheWalkReadsClosuresAndStateMachines()
    {
        _ = ChoiceEntryPoints.Game;
        var sites = Offenders(MethodsOf(typeof(RetailSoakGuardTests)).ToList(), Refused).ToList();

        Assert.Equal(2, sites.Count);
        Assert.All(sites, line => Assert.Contains(nameof(RetailSoakGuardTests), line, StringComparison.Ordinal));
        Assert.Contains(sites, line => line.Contains("PowerCmd", StringComparison.Ordinal));
        Assert.Contains(sites, line => line.Contains("CardCmd.AutoPlay", StringComparison.Ordinal));
    }

    // The two spellings the walk above has to see, never called
    private static Func<Task> ACheatInALambda() => () => PowerCmd.Apply(null!, null!, null!, 0, null, null);

    private static async Task ACheatInAStateMachine()
    {
        await Task.Yield();
        await CardCmd.AutoPlay(null!, null!, null);
    }

    private static List<MethodBase> SoakMethods()
    {
        _ = ChoiceEntryPoints.Game;
        return SoakTypes.SelectMany(MethodsOf).ToList();
    }

    /// <summary>Every method and constructor declared by a type or by any type nested
    /// in it, which is where the compiler puts closures and state machines.</summary>
    private static IEnumerable<MethodBase> MethodsOf(Type type)
    {
        const BindingFlags every = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var method in type.GetConstructors(every).Concat<MethodBase>(type.GetMethods(every))) yield return method;
        foreach (var nested in type.GetNestedTypes(every))
        {
            foreach (var method in MethodsOf(nested)) yield return method;
        }
    }

    private static IEnumerable<string> Offenders(
        IReadOnlyList<MethodBase> methods, IReadOnlyList<(Func<MethodBase, bool> Matches, string Because)> rules) =>
        methods
            .SelectMany(method => ChoiceEntryPoints.Callees(method).Select(callee => (Method: method, Callee: callee)))
            .SelectMany(site => rules
                .Where(rule => rule.Matches(site.Callee))
                .Select(rule => $"{Outermost(site.Method.DeclaringType!).Name}.{site.Method.Name} calls " +
                                $"{site.Callee.DeclaringType!.Name}.{site.Callee.Name}: {rule.Because}"))
            .Distinct()
            .OrderBy(line => line, StringComparer.Ordinal);

    private static Type Outermost(Type type)
    {
        while (type.DeclaringType is { } outer) type = outer;
        return type;
    }
}

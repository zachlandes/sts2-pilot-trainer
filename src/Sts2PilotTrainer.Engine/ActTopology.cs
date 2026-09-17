using System.Reflection;
using System.Text;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Unlocks;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The game's own act topology, read from the assembly rather than assumed: every act
/// the model database ships at each index, whether it is that index's default, the
/// epochs its own <c>IsUnlocked</c> names, its room and floor counts, and every type
/// this build subclasses <c>ActModel</c> with.
///
/// The recorder's and the replay's fixtures name one progression - the default act at
/// each index - and one alternative at index 0, and the whole-act walk's survival rules
/// were tuned to the floor count of the act it plays. <see cref="SavePoints"/> holds the
/// save contract, and a new or alternative act keeps every <c>SaveManager.SaveRun</c>
/// call site and every ancient-event subclass exactly where they were, so that record
/// stays green while the fixtures' progression is no longer the game's. This is the
/// reading that changes instead, held to a committed record the same way.
///
/// Reuses <see cref="ChoiceEntryPoints"/>'s IL scan and loaded-type set rather than
/// loading the game assembly a second time, and the model database the engine has
/// already started rather than a second construction of it.
/// </summary>
internal static class ActTopology
{
    /// <summary>
    /// Loads the engine assembly before any member here names a game type, the same
    /// way <see cref="SavePoints"/> does and for the same reason.
    /// </summary>
    static ActTopology()
    {
        _ = EngineHost.StartupPhase();
    }

    /// <summary>One act the database ships, as the record lists it.</summary>
    internal sealed record ShippedAct(
        int Index, string Id, bool IsDefault, int Rooms, int Floors, IReadOnlyList<Type> Epochs, Type Model);

    /// <summary>
    /// Every act <c>ModelDb.Acts</c> lists, in index order and then by id: the acts a run
    /// can be made of, one per index in index order. Read off the started engine's
    /// database because the list is hand-written in the game and a subclass it leaves
    /// out - <c>DeprecatedAct</c> on this build - ships no act.
    /// </summary>
    internal static IReadOnlyList<ShippedAct> ShippedActs()
    {
        EngineHost.Start();
        return ModelDb.Acts
            .OrderBy(act => act.Index)
            .ThenBy(act => act.Id.ToString(), StringComparer.Ordinal)
            .Select(act => new ShippedAct(
                act.Index, act.Id.ToString(), act.IsDefault,
                act.GetNumberOfRooms(isMultiplayer: false), act.GetNumberOfFloors(isMultiplayer: false),
                EpochsNamedBy(act.GetType()), act.GetType()))
            .ToList();
    }

    /// <summary>
    /// The default act at each index, in index order: the progression the game's own
    /// <c>ActModel.GetDefaultList</c> builds and the one every fixture here names.
    /// </summary>
    internal static IReadOnlyList<string> DefaultProgression() =>
        ShippedActs().Where(act => act.IsDefault).Select(act => act.Id).ToList();

    /// <summary>
    /// The epochs an act's own <c>IsUnlocked</c> names, read off its IL as the type
    /// arguments of every <c>UnlockState.IsEpochRevealed&lt;T&gt;</c> it calls, so which
    /// unlock reveals an alternative act is recorded without a state to ask. An act
    /// whose body names none is unlocked by some other rule or by none, and the record
    /// says which epochs it names rather than "always".
    /// </summary>
    internal static IReadOnlyList<Type> EpochsNamedBy(Type act)
    {
        const BindingFlags declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                                      BindingFlags.DeclaredOnly;
        var isUnlocked = act.GetMethod(nameof(ActModel.IsUnlocked), declared, [typeof(UnlockState)])
            ?? throw new InvalidOperationException(
                $"{act.FullName} does not declare {nameof(ActModel.IsUnlocked)}({nameof(UnlockState)}); " +
                "every act on this build overrides it, and this reading names the epochs its body reveals.");
        return ChoiceEntryPoints.OwnCalleesOf(isUnlocked)
            .Where(callee => callee is MethodInfo { IsGenericMethod: true, Name: "IsEpochRevealed" } &&
                             callee.DeclaringType == typeof(UnlockState))
            .SelectMany(callee => ((MethodInfo)callee).GetGenericArguments())
            .Distinct()
            .OrderBy(epoch => epoch.FullName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Every type this build subclasses <c>ActModel</c> with and the database does not
    /// ship. A new act begins as a subclass, and one the database has not listed yet
    /// is still a diff here rather than nothing.
    /// </summary>
    internal static IReadOnlyList<Type> UnshippedActSubclasses()
    {
        var shipped = ShippedActs().Select(act => act.Model).ToHashSet();
        return ChoiceEntryPoints.AllLoadedTypes
            .Where(type => type.IsSubclassOf(typeof(ActModel)) && !shipped.Contains(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The committed record: every <see cref="ShippedActs"/> line and every
    /// <see cref="UnshippedActSubclasses"/> name, one per line under its own heading.
    /// Regenerate with <c>./scripts/act-topology.sh --update</c> so a game
    /// update that adds an act, moves one, or changes what unlocks one shows as a diff
    /// in the change that adopts the build.
    /// </summary>
    internal static string Enumeration()
    {
        var text = new StringBuilder();
        text.AppendLine("# The game's act topology on this build, read by RunRecorderTests from the");
        text.AppendLine("# assembly's own model database, IL and type table rather than assumed: every");
        text.AppendLine("# act the database ships at each index, whether it is that index's default, the");
        text.AppendLine("# epochs its IsUnlocked names and its room and floor counts; and every type that");
        text.AppendLine("# subclasses ActModel without shipping. The recorder's and the replay's fixtures");
        text.AppendLine("# name the default act at each index as their progression, so a build that adds");
        text.AppendLine("# an act, moves one to another index, or changes which is the default or what");
        text.AppendLine("# unlocks one, changes this file in the change that adopts it.");
        text.AppendLine("# Regenerate with");
        text.AppendLine("#   ./scripts/act-topology.sh --update");
        text.AppendLine();
        text.AppendLine("# Acts the database ships, one per index in index order:");
        text.AppendLine("# index id default|alternative rooms floors epochs model");
        foreach (var act in ShippedActs())
        {
            var epochs = act.Epochs.Count == 0 ? "-" : string.Join(",", act.Epochs.Select(epoch => epoch.FullName));
            text.AppendLine(
                $"{act.Index} {act.Id} {(act.IsDefault ? "default" : "alternative")} rooms={act.Rooms} " +
                $"floors={act.Floors} epochs={epochs} {act.Model.FullName}");
        }

        text.AppendLine();
        text.AppendLine("# Types that subclass ActModel and the database does not ship:");
        foreach (var type in UnshippedActSubclasses())
        {
            text.AppendLine(type.FullName);
        }

        return text.ToString();
    }
}

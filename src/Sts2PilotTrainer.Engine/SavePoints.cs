using System.Reflection;
using System.Text;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The game's own save contract, read from the assembly rather than assumed: every
/// <c>SaveManager.SaveRun</c> overload - the recorder's own <c>RunSaved</c> patch watches
/// exactly one - which members call each, and which types this build subclasses
/// <c>AncientEventModel</c> with, the closed set an ancient event finishing can be.
///
/// The resume logic (<c>RunCapture.Resume</c>, <c>IsObservedSaveRollback</c>) and
/// <c>AGENTS.md</c>'s save contract both assume this is a closed, known pair of sets.
/// A game update that moves where the run saves, or adds or removes an ancient event,
/// changes what a continued run has to account for without changing a single line the
/// recorder patches, because <c>RunSaved</c> sits on <c>SaveManager.SaveRun</c> itself
/// and fires for a new caller exactly as it does for an old one - so drift here is
/// silent until a recording it produced fails resume, unless it is read and held to a
/// committed record the way <c>RunRecorderTests</c> already holds the choice
/// entry points.
///
/// Reuses <see cref="ChoiceEntryPoints"/>'s IL scan and loaded-type set rather than
/// loading the game assembly a second time.
/// </summary>
internal static class SavePoints
{
    /// <summary>
    /// Loads the engine assembly before any member here names a game type, the same
    /// way <see cref="ChoiceEntryPoints"/> does and for the same reason: its module
    /// initializer is what teaches the runtime where the prepared game lives, and a
    /// static member typed over the game would resolve before it otherwise.
    /// </summary>
    static SavePoints()
    {
        _ = EngineHost.StartupPhase();
    }

    /// <summary>
    /// Every <c>SaveManager.SaveRun</c> this build declares, by signature. The recorder's
    /// <c>RunSaved</c> patch watches exactly one of them, so a second one is a save site
    /// the recorder does not see, and it is enumerated here so that it shows as a diff
    /// rather than as the callers of the watched one staying exactly what they were.
    /// </summary>
    internal static IReadOnlyList<MethodInfo> SaveRunOverloads()
    {
        const BindingFlags every = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var overloads = typeof(SaveManager).GetMethods(every)
            .Where(method => method.Name == nameof(SaveManager.SaveRun))
            .OrderBy(Signature, StringComparer.Ordinal)
            .ToList();
        return overloads.Count > 0
            ? overloads
            : throw new InvalidOperationException(
                $"{typeof(SaveManager).FullName}.{nameof(SaveManager.SaveRun)} is not declared on this build; " +
                "the recorder's RunSaved patch and this reading both name it.");
    }

    /// <summary>
    /// Every member whose own body calls the given <see cref="SaveRunOverloads"/> member,
    /// read by its declared name: an async method's compiler-generated state machine
    /// resolves back to the method that declared it, so a caller reads the way a person
    /// would name it.
    /// </summary>
    internal static IReadOnlyList<MethodBase> CallersOf(MethodBase saveRun) =>
        ChoiceEntryPoints.MethodsNaming(saveRun)
            .Select(ChoiceEntryPoints.DeclaredMember)
            .Distinct()
            .OrderBy(method => method.DeclaringType!.FullName, StringComparer.Ordinal)
            .ThenBy(method => method.Name, StringComparer.Ordinal)
            .ToList();

    private static string Signature(MethodInfo method) =>
        $"{method.Name}({string.Join(", ", method.GetParameters().Select(parameter => parameter.ParameterType.Name))})";

    /// <summary>Every type this build subclasses <c>AncientEventModel</c> with.</summary>
    internal static IReadOnlyList<Type> AncientEventSubclasses() =>
        ChoiceEntryPoints.AllLoadedTypes
            .Where(type => type.IsSubclassOf(typeof(AncientEventModel)))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The committed record: every <see cref="SaveRunOverloads"/> member with every
    /// <see cref="CallersOf"/> member under it, and every <see cref="AncientEventSubclasses"/>
    /// name, one per line under its own heading.
    /// Regenerate with <c>./scripts/save-points.sh --update</c> so a game update that
    /// adds or removes either shows as a diff in the change that adopts the build.
    /// </summary>
    internal static string Enumeration()
    {
        var text = new StringBuilder();
        text.AppendLine("# The game's save contract on this build, read by RunRecorderTests from the");
        text.AppendLine("# assembly's own IL and type table rather than assumed: every SaveManager.SaveRun");
        text.AppendLine("# overload, every member that calls each, and every type that subclasses");
        text.AppendLine("# AncientEventModel. A build that adds a way to save, moves where the run saves,");
        text.AppendLine("# or adds or removes an ancient event, changes this file in the change that");
        text.AppendLine("# adopts it. Regenerate with");
        text.AppendLine("#   ./scripts/save-points.sh --update");
        text.AppendLine();
        foreach (var saveRun in SaveRunOverloads())
        {
            text.AppendLine($"# Members that call SaveManager.{Signature(saveRun)}:");
            foreach (var method in CallersOf(saveRun))
            {
                text.AppendLine($"{method.DeclaringType!.FullName}.{method.Name}");
            }

            text.AppendLine();
        }

        text.AppendLine("# Types that subclass AncientEventModel:");
        foreach (var type in AncientEventSubclasses())
        {
            text.AppendLine(type.FullName);
        }

        return text.ToString();
    }
}

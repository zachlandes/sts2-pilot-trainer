using System.Reflection;
using System.Text;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using Sts2PilotTrainer.Engine;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The game's own save contract, read from the assembly rather than assumed: which
/// members call <c>SaveManager.SaveRun</c> - the one member the recorder's own
/// <c>RunSaved</c> patch watches - and which types this build subclasses
/// <c>AncientEventModel</c> with, the closed set an ancient event finishing can be.
///
/// The resume logic (<c>RunCapture.Resume</c>, <c>IsObservedSaveRollback</c>) and
/// <c>AGENTS.md</c>'s save contract both assume this is a closed, known pair of sets.
/// A game update that moves where the run saves, or adds or removes an ancient event,
/// changes what a continued run has to account for without changing a single line the
/// recorder patches, because <c>RunSaved</c> sits on <c>SaveManager.SaveRun</c> itself
/// and fires for a new caller exactly as it does for an old one - so drift here is
/// silent until a recording it produced fails resume, unless it is read and held to a
/// committed record the way <see cref="RunRecorderTests"/> already holds the choice
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

    /// <summary>The one member the recorder's <c>RunSaved</c> patch watches. Every
    /// caller reaches it whatever its own call site wrote, because the compiler fills
    /// in <c>saveProgress</c>'s default.</summary>
    private static MethodBase SaveRun => AccessTools.Method(
        typeof(SaveManager), nameof(SaveManager.SaveRun), [typeof(AbstractRoom), typeof(bool)]);

    /// <summary>
    /// Every member whose own body calls <see cref="SaveRun"/>, read by its declared
    /// name: an async method's compiler-generated state machine resolves back to the
    /// method that declared it, so a caller reads the way a person would name it.
    /// </summary>
    internal static IReadOnlyList<MethodBase> SaveRunCallers() =>
        ChoiceEntryPoints.MethodsNaming(SaveRun)
            .Select(ChoiceEntryPoints.DeclaredMember)
            .Distinct()
            .OrderBy(method => method.DeclaringType!.FullName, StringComparer.Ordinal)
            .ThenBy(method => method.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>Every type this build subclasses <c>AncientEventModel</c> with.</summary>
    internal static IReadOnlyList<Type> AncientEventSubclasses() =>
        ChoiceEntryPoints.AllLoadedTypes
            .Where(type => type.IsSubclassOf(typeof(AncientEventModel)))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The committed record: every <see cref="SaveRunCallers"/> member and every
    /// <see cref="AncientEventSubclasses"/> name, one per line under its own heading.
    /// Regenerate with <c>./scripts/save-points.sh --update</c> so a game update that
    /// adds or removes either shows as a diff in the change that adopts the build.
    /// </summary>
    internal static string Enumeration()
    {
        var text = new StringBuilder();
        text.AppendLine("# The game's save contract on this build, read by RunRecorderTests from the");
        text.AppendLine("# assembly's own IL and type table rather than assumed: every member that calls");
        text.AppendLine("# SaveManager.SaveRun, and every type that subclasses AncientEventModel. A build");
        text.AppendLine("# that moves where the run saves, or adds or removes an ancient event, changes");
        text.AppendLine("# this file in the change that adopts it. Regenerate with");
        text.AppendLine("#   ./scripts/save-points.sh --update");
        text.AppendLine();
        text.AppendLine("# Members that call SaveManager.SaveRun:");
        foreach (var method in SaveRunCallers())
        {
            text.AppendLine($"{method.DeclaringType!.FullName}.{method.Name}");
        }

        text.AppendLine();
        text.AppendLine("# Types that subclass AncientEventModel:");
        foreach (var type in AncientEventSubclasses())
        {
            text.AppendLine(type.FullName);
        }

        return text.ToString();
    }
}

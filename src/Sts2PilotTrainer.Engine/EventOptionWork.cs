using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The task an event option's work runs as, read on its way past.
///
/// <c>EventSynchronizer.ChooseLocalOption</c> returns nothing: it starts the option's
/// <c>Chosen</c> task and keeps it to itself, wrapped by the game's own
/// <c>TaskHelper.RunSafely</c>, which logs a fault and swallows it. A driver that only
/// drained the action queue after choosing never learned that the work had faulted,
/// which headlessly it did wherever an event reached the scene tree
/// (<see cref="PresentationStandIns"/>): the event stood on the same page, the next
/// recorded decision named an option it no longer offered, and the refusal blamed the
/// recording. The recorder reads the same task the same way, in
/// <c>RunRecorder.OptionChosen</c>.
///
/// Headless only, and only the raw task: <c>Chosen</c>'s own, before the wrapper, so
/// a fault is a fault here. Nothing decides anything; the driver drains what it can
/// and refuses a fault by name.
/// </summary>
internal static class EventOptionWork
{
    /// <summary>The task the last option chosen started, or null where the engine
    /// started none - a locked option, or one already chosen.</summary>
    internal static Task? Last { get; private set; }

    internal static void Forget() => Last = null;

    internal static void Install(Harmony harmony, List<string> failures)
    {
        var chosen = typeof(EventOption).GetMethod(nameof(EventOption.Chosen), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (chosen is null)
        {
            failures.Add(
                "event option work: EventOption.Chosen not found in this build, so the driver cannot wait for an " +
                "option's work or refuse a fault in it.");
            return;
        }

        try
        {
            harmony.Patch(chosen, postfix: new HarmonyMethod(typeof(EventOptionWork).GetMethod(nameof(After), BindingFlags.NonPublic | BindingFlags.Static)!));
        }
        catch (Exception ex)
        {
            failures.Add($"event option work EventOption.Chosen: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void After(Task __result) => Last = __result;
}

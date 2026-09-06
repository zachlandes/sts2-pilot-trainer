using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The run library: browse the runs this game can play, open one, and play from any
/// place its recording proves.
///
/// The third module in the shell, and the one with the most surface. It owns four
/// things and they are one screen rather than four features - a card in the
/// Compendium, the browser behind it, one run opened, and the plate under the game's
/// own run-history pane. All four speak the same vocabulary and obey the same two
/// settled rules: a player plays <em>from</em> a run, and a run this game cannot play
/// is not in the list at all.
///
/// <para><b>After the fact, always.</b> Nothing here is reachable while a run is being
/// played. The Compendium is a main-menu surface, and the run-history plate says so in
/// a sentence for the case where the game ever lets its history open mid-run. There is
/// no in-run affordance and adding one is not a smaller version of this feature; it is
/// a different one.</para>
///
/// <para>What it establishes before installing anything is that this build still has
/// the two members it hangs on. A library that silently failed to add its card would
/// be a feature a player cannot find and cannot be told about, which is worse than a
/// line in the log saying it is not there.</para>
/// </summary>
internal sealed class RunLibraryModule : IRunmobileModule
{
    internal static RunLibraryModule Instance { get; } = new();

    /// <summary>
    /// The patch classes this module owns, listed rather than discovered, for the
    /// reason the Combat Trainer lists its own: <c>PatchAll</c> would install another
    /// module's patches and would install these for a library that had refused.
    /// </summary>
    internal static IReadOnlyList<Type> PatchClasses { get; } =
    [
        typeof(CompendiumCard),
        typeof(RunHistoryPlateHost.HistoryEntry),
    ];

    private readonly Lock _gate = new();

    private string? _refusal;
    private bool _examined;

    private RunLibraryModule()
    {
    }

    /// <summary>This module's name in the mod's own log lines. Not player-facing: what
    /// a player reads lives in <see cref="LibraryCopy"/>.</summary>
    public string Name => "Run library";

    public bool Enabled
    {
        get
        {
            Examine();
            return _refusal is null;
        }
    }

    public string? Refusal
    {
        get
        {
            Examine();
            return _refusal;
        }
    }

    /// <summary>
    /// The library has no singleplayer-menu card.
    ///
    /// Its way in is the Compendium, which is where the game already keeps the things
    /// you look at rather than play - the card library, the bestiary, the run history.
    /// A second card in the singleplayer menu would offer to browse from the screen
    /// that starts runs, which is the one place browsing does not belong.
    /// </summary>
    public IReadOnlyList<MenuCard> MenuCards => [];

    public void Install(Harmony harmony)
    {
        foreach (var patchClass in PatchClasses)
        {
            harmony.CreateClassProcessor(patchClass).Patch();
        }
    }

    private void Examine()
    {
        lock (_gate)
        {
            if (_examined) return;
            _examined = true;

            var problems = PatchTargets.Unresolvable(PatchClasses).ToList();
            if (problems.Count > 0) _refusal = string.Join(" ", problems);
        }
    }
}

/// <summary>
/// Whether a run reproduces on the build in front of us.
///
/// Design section 9.4 in one place. Two answers come from two different kinds of
/// evidence, and keeping them apart is the point:
///
/// <list type="bullet">
/// <item>A recording made on <em>another</em> build has no verdict here at all. Only a
/// verdict keyed to this build says anything about this build, and nothing local can
/// produce one - the run would have to be replayed on it. That is
/// <see cref="RunVerdict.Absent"/>, which is why the list's own numeral says such runs
/// "come back when a verdict for {build} arrives".</item>
/// <item>A recording made on <em>this</em> build is checked here, against the same
/// preflight the entry itself will apply, over the same supplied progress model. A
/// pass is <see cref="RunVerdict.Passed"/> and anything else is
/// <see cref="RunVerdict.Failed"/>.</item>
/// </list>
///
/// It computes nothing of its own. Every field it reads is
/// <c>EnvironmentPreflight</c>'s, asked through <see cref="Preflight"/>, so a run the
/// list offers is a run the eligibility gate has already agreed to.
/// </summary>
internal static class RunVerdicts
{
    /// <summary>
    /// This build's verdict on one recording.
    ///
    /// A game this process cannot read is not a game that can approve anything, so a
    /// failure to read is <see cref="RunVerdict.Absent"/> rather than a refusal: the
    /// library then lists nothing and says how many it did not list, which is honest
    /// and leaves the run findable by its code.
    /// </summary>
    internal static RunVerdict For(ReplayManifest recording, string thisBuild)
    {
        if (!string.Equals(recording.Environment.BuildVersion.Value, thisBuild, StringComparison.Ordinal))
        {
            return RunVerdict.Absent;
        }

        try
        {
            return Preflight.Evaluate(
                recording.Environment,
                RecordedFightEntry.SuppliedProgressFor(recording),
                recording.Source.Kind).Matches
                ? RunVerdict.Passed
                : RunVerdict.Failed;
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not judge {recording.RunId} against this game, so it has no " +
                $"verdict here: {ex.GetType().Name}: {ex.Message}", 2);
            return RunVerdict.Absent;
        }
    }
}

/// <summary>
/// Which of a module's patches this build cannot attach.
///
/// Asked of Harmony's own resolution rather than of a name list, because what has to
/// be true is exactly that the patch will attach. The recorder asks the same question
/// about its own targets for a different reason - it would miss decisions - and this
/// module asks it because a surface that silently failed to appear is a feature a
/// player cannot find. One implementation, so the two answers cannot drift.
/// </summary>
internal static class PatchTargets
{
    internal static IEnumerable<string> Unresolvable(IReadOnlyList<Type> patchClasses)
    {
        foreach (var patchClass in patchClasses)
        {
            foreach (var patch in patchClass.GetCustomAttributes(typeof(HarmonyPatch), inherit: false)
                         .OfType<HarmonyPatch>()
                         .Select(attribute => attribute.info)
                         .Where(info => info.declaringType is not null && info.methodName is not null))
            {
                if (Resolves(patch)) continue;
                yield return
                    $"{patch.declaringType!.Name}.{patch.methodName} is absent from this build, so the " +
                    "surface that hangs on it would not appear.";
            }
        }
    }

    private static bool Resolves(HarmonyMethod patch)
    {
        try
        {
            return AccessTools.Method(patch.declaringType!, patch.methodName, patch.argumentTypes) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

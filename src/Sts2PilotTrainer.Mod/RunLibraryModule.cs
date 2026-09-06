using System.Reflection;
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
/// pass is <see cref="RunVerdict.Passed"/>, a mismatch is
/// <see cref="RunVerdict.Failed"/>, and a reading that could not be taken at all is
/// <see cref="RunVerdict.Unjudged"/>.</item>
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
    /// failure to read is <see cref="RunVerdict.Unjudged"/> rather than a refusal: the
    /// library then lists nothing and says how many it did not list, which is honest
    /// and leaves the run findable by its code. Its own answer rather than
    /// <see cref="RunVerdict.Absent"/>, because a verdict nobody could reach and a
    /// verdict that does not exist are different facts, and the run code says which.
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
            return RunVerdict.Unjudged;
        }
    }
}

/// <summary>
/// Which of a module's patches this build cannot attach.
///
/// Asked of Harmony's own resolution rather than of a name list, because what has to
/// be true is exactly that the patch will attach. This module asks it because a
/// surface that silently failed to appear is a feature a player cannot find.
///
/// <para>There are two readers, not one. <c>RecorderModule.UnresolvableTargets</c> asks
/// the same question of the recorder's own patch classes for a different reason - it
/// would miss decisions rather than fail to draw - and the two are not yet the same
/// code because neither reader answers for the other's classes. That one reads only
/// class-level attributes, which is the narrowness this one had to grow out of, and it
/// resolves a constructor patch, which this one has no branch for. Every recorder patch
/// names its method on the class attribute today, so nothing is unchecked; unifying them
/// means one reader that covers both declaration styles and constructors, and one
/// refusal sentence that reads right for a missing surface and a missed decision
/// alike.</para>
/// </summary>
internal static class PatchTargets
{
    /// <summary>
    /// Every member these patch classes hang on, as Harmony would resolve it, named
    /// <c>Type.Method</c>.
    ///
    /// Both declaration styles are read here rather than one: a class attribute may
    /// carry the type and the method name together, the way the recorder writes them,
    /// or carry the type alone with the method name on each patched method, the way the
    /// library's two classes are written. Reading only the first said nothing at all
    /// about the second, on every build.
    /// </summary>
    internal static IReadOnlyList<string> Targets(IReadOnlyList<Type> patchClasses)
    {
        var targets = new List<string>();
        foreach (var patch in Named(patchClasses))
        {
            var name = $"{patch.DeclaringType.Name}.{patch.Info.methodName}";
            if (!targets.Contains(name, StringComparer.Ordinal)) targets.Add(name);
        }

        return targets;
    }

    internal static IEnumerable<string> Unresolvable(IReadOnlyList<Type> patchClasses)
    {
        foreach (var patch in Named(patchClasses))
        {
            if (Resolves(patch.DeclaringType, patch.Info)) continue;

            yield return
                $"{patch.DeclaringType.Name}.{patch.Info.methodName} is absent from this build, so the " +
                "surface that hangs on it would not appear.";
        }
    }

    /// <summary>
    /// Every member these patch classes name, whichever half of the attribute pair named
    /// it.
    ///
    /// One traversal for both questions, because "which declaration styles are checked"
    /// is the thing that has already been got wrong once here: a reader that saw only
    /// class attributes reported the library's two patch classes clean on every build,
    /// and a second copy of this walk is a second place for that to happen.
    /// </summary>
    private static IEnumerable<(Type DeclaringType, HarmonyMethod Info)> Named(
        IReadOnlyList<Type> patchClasses)
    {
        foreach (var patchClass in patchClasses)
        {
            var onClass = Infos(patchClass);
            var declaring = onClass.Select(info => info.declaringType).FirstOrDefault(type => type is not null);

            foreach (var info in onClass.Concat(patchClass
                         .GetMethods(BindingFlags.Static | BindingFlags.Instance |
                                     BindingFlags.Public | BindingFlags.NonPublic)
                         .SelectMany(Infos)))
            {
                var type = info.declaringType ?? declaring;
                if (type is null || info.methodName is null) continue;

                yield return (type, info);
            }
        }
    }

    private static IReadOnlyList<HarmonyMethod> Infos(MemberInfo member) =>
        [
            .. member.GetCustomAttributes(typeof(HarmonyPatch), inherit: false)
                .OfType<HarmonyPatch>()
                .Select(attribute => attribute.info),
        ];

    private static bool Resolves(Type declaringType, HarmonyMethod patch)
    {
        try
        {
            return AccessTools.Method(declaringType, patch.methodName, patch.argumentTypes) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

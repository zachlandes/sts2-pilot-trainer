using HarmonyLib;
using Sts2PilotTrainer.Engine;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// The recorder: every run the player plays becomes a recording of their own.
///
/// The second module in the shell, and the one with no surface at all. It contributes
/// no menu card and draws nothing; what it does is watch, and the only thing a player
/// sees is that their runs turn up in the store afterwards. The settings row that
/// turns it off arrives with the rest of Runmobile's own drawing; until then the
/// toggle is a line in <see cref="RunmobileSettings"/>.
///
/// What it establishes before installing anything is that this build still has every
/// member it means to watch. A recorder attached to a game that renamed one would
/// quietly miss those decisions, and a history missing decisions replays perfectly
/// into a different run - so a build it cannot watch completely is one it declines to
/// watch at all, and says so in the log.
/// </summary>
internal sealed class RecorderModule : IRunmobileModule
{
    internal static RecorderModule Instance { get; } = new();

    private readonly Lock _gate = new();

    private string? _refusal;
    private bool _examined;

    private RecorderModule()
    {
    }

    /// <summary>This module's name in the mod's own log lines. Not player-facing:
    /// the words a player reads live in <c>Sts2PilotTrainer.Trainer</c>, and this
    /// module puts none in front of anybody.</summary>
    public string Name => "Recorder";

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

    /// <summary>The recorder has no surface in this release.</summary>
    public IReadOnlyList<MenuCard> MenuCards => [];

    public void Install(Harmony harmony)
    {
        foreach (var patchClass in RunRecorder.PatchClasses)
        {
            harmony.CreateClassProcessor(patchClass).Patch();
        }

        // The screens themselves are the shell's, because a screen being up is a fact
        // about the game that the Combat Trainer's settle reads too. What was answered
        // is this feature's, so it subscribes rather than patching them a second time.
        RunRecorder.ReadTheAnswers();
    }

    /// <summary>
    /// Whether this build is one the recorder can watch completely.
    ///
    /// Two questions, and both are about the game rather than about this mod.
    /// <see cref="EngineCommands.Verify"/> asks whether every decision the format
    /// names still maps onto a member the loaded assembly has - the same table the
    /// driver replays through, read from the other end. The patch list asks whether
    /// Harmony can resolve each method this module means to attach to, which is the
    /// part <see cref="EngineCommands"/> cannot see: the run's own lifecycle. The two
    /// card screens are not in it - they are the shell's, installed however this
    /// module answers, and this module only subscribes to what they answered.
    /// </summary>
    private void Examine()
    {
        lock (_gate)
        {
            if (_examined) return;
            _examined = true;

            var problems = new List<string>(EngineCommands.Verify());
            problems.AddRange(UnresolvableTargets());
            if (problems.Count > 0) _refusal = string.Join(" ", problems);
        }
    }

    /// <summary>
    /// Every method this module would patch that this build does not have.
    ///
    /// Asked of Harmony's own resolution rather than of a name list, because what has
    /// to be true is exactly that the patch will attach - and a method that resolves to
    /// nothing is where a recorder silently stops seeing a kind of decision.
    /// </summary>
    private static IEnumerable<string> UnresolvableTargets()
    {
        foreach (var patchClass in RunRecorder.PatchClasses)
        {
            foreach (var patch in patchClass.GetCustomAttributes(typeof(HarmonyPatch), inherit: false)
                         .OfType<HarmonyPatch>()
                         .Select(attribute => attribute.info)
                         .Where(info => info.declaringType is not null))
            {
                if (Resolves(patch)) continue;
                yield return
                    $"{patch.declaringType!.Name}.{patch.methodName ?? "the constructor this recorder watches"} " +
                    "is absent from this build, so the recorder would not see the decisions that go through it.";
            }
        }
    }

    /// <summary>
    /// Whether this build has the method a patch names.
    ///
    /// Harmony's own lookup, so the question asked is exactly the one that matters -
    /// will the patch attach - rather than a second reading of the assembly that could
    /// disagree with it. It returns null rather than throwing for a member it cannot
    /// find, and anything it does throw is the same answer.
    /// </summary>
    private static bool Resolves(HarmonyMethod patch)
    {
        try
        {
            // A constructor is a member this build can drop like any other - the discard
            // a player makes outside a fight is watched through one - and it names no
            // method, so asking for one by a null name would answer "absent" for every
            // build.
            return patch.methodType == MethodType.Constructor || patch.methodName is null
                ? AccessTools.Constructor(patch.declaringType!, patch.argumentTypes) is not null
                : AccessTools.Method(patch.declaringType!, patch.methodName, patch.argumentTypes) is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

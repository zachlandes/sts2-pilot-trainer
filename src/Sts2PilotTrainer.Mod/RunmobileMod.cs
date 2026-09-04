using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using Sts2PilotTrainer.Engine;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// Runmobile's entry point, and the only place that decides whether this process is
/// one the mod may speak about at all.
///
/// The game finds this class because it carries <see cref="ModInitializerAttribute"/>,
/// calls <see cref="Initialize"/>, and does nothing else on our behalf - in
/// particular it does not call <c>Harmony.PatchAll</c> for a mod that declares an
/// initializer, so the shell installs its own patches and each enabled module
/// installs the patches for its feature.
///
/// It is a shell: what is true of the mod however it is configured lives here, and
/// each feature lives behind <see cref="IRunmobileModule"/>. Today there is one
/// module, the Combat Trainer.
///
/// Mod initialization deliberately reads nothing about the game. It runs inside the
/// game's "very early" startup phase, one phase before the game builds its model
/// database and id-serialization cache, so there is no game to read yet: asking then
/// took the process down with a segmentation fault rather than an error. Everything
/// that reads the running game happens from <see cref="ModeCard"/>, off a surface a
/// player has reached. See docs/in-game-host.md.
///
/// It refuses rather than degrades. A module that cannot establish what it needs
/// says so in the game's log, installs no patch and contributes no surface.
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class RunmobileMod
{
    internal const string ModId = "Runmobile";

    private const string HarmonyId = "sts2-pilot-trainer.runmobile";

    internal static IReadOnlyList<Type> ShellPatchClasses { get; } = [typeof(ModeCard)];

    private static readonly Lock AdoptionGate = new();

    private static bool _adoptionAttempted;
    private static bool _adopted;

    /// <summary>Whether the mod started without refusing. Distinct from any module
    /// being enabled: the shell starting is about this process, and a module being
    /// enabled is about what that module could establish.</summary>
    internal static bool Started { get; private set; }

    /// <summary>
    /// Every feature this build carries, in the order they are installed.
    ///
    /// The recorder and the run library are the other two and are not built yet;
    /// when they are, they are added here and nothing else about the shell changes.
    /// </summary>
    internal static IReadOnlyList<IRunmobileModule> Modules { get; } = [CombatTrainerModule.Instance];

    /// <summary>The modules that could establish what they need in this process.</summary>
    internal static IEnumerable<IRunmobileModule> EnabledModules => Modules.Where(module => module.Enabled);

    /// <summary>Every singleplayer-menu card the enabled modules contribute.</summary>
    internal static IReadOnlyList<MenuCard> MenuCards => MenuCardsFrom(Modules);

    internal static IReadOnlyList<MenuCard> MenuCardsFrom(IReadOnlyList<IRunmobileModule> modules) =>
        modules.Where(module => module.Enabled).SelectMany(module => module.MenuCards).ToList();

    public static void Initialize()
    {
        SiblingAssemblies.Install();
        try
        {
            Start();
        }
        catch (Exception ex)
        {
            // A mod that throws out of its initializer is reported by the game as a
            // failed mod, which is the right outcome and a worse message than this
            // one. Say what could not be established, then stay out of the way.
            Log.Error($"[{ModId}] refusing to run: {ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    /// <summary>
    /// Kept apart from <see cref="Initialize"/> and never inlined, because preparing
    /// this method resolves the assemblies it mentions and those live beside this one
    /// rather than beside the game. <see cref="SiblingAssemblies"/> has to have run
    /// first, and "first" here means before the JIT looks, not before the first
    /// statement executes.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Start()
    {
        var harmony = new Harmony(HarmonyId);

        // Installed here, at mod start, rather than when a trainer run begins. A
        // barrier raised with the run would have a window before it and would be gone
        // after a crash; installed always and conditional on the run, there is no
        // moment where a trainer run exists and its writes are not stopped. It is the
        // shell's rather than a module's: it is about what this mod may do to a
        // player's profile, which no feature gets to decide for itself.
        ProfileWriteBarrier.Install(harmony);
        InstallShellPatches(harmony);
        InstallModules(harmony, Modules);
        Started = true;
        Log.Info($"[{ModId}] loaded; its cards are added when the singleplayer menu opens", 2);
    }

    internal static void InstallShellPatches(Harmony harmony)
    {
        foreach (var patchClass in ShellPatchClasses)
        {
            harmony.CreateClassProcessor(patchClass).Patch();
        }
    }

    /// <summary>
    /// Installs each enabled module's patches, in order, and says in the game's log
    /// which ones were installed and why any were not.
    ///
    /// A module that cannot establish what it needs is skipped rather than thrown
    /// out of: the feature is gone for this session and the rest of the mod is not.
    /// </summary>
    internal static IReadOnlyList<string> InstallModules(
        Harmony harmony, IReadOnlyList<IRunmobileModule> modules)
    {
        var installed = new List<string>();
        foreach (var module in modules)
        {
            if (!module.Enabled)
            {
                Log.Error($"[{ModId}] {module.Name} is unavailable: {module.Refusal}", 2);
                continue;
            }

            module.Install(harmony);
            installed.Add(module.Name);
            Log.Info($"[{ModId}] {module.Name} installed", 2);
        }

        return installed;
    }

    /// <summary>
    /// Takes the running game, once, at a moment when there is one.
    ///
    /// Called from the singleplayer menu rather than from mod loading, because that
    /// menu cannot exist before the game has finished starting up.
    /// <see cref="EngineHost.AdoptRunningGame"/> refuses anything it cannot read
    /// honestly, and a refusal here means no mode card at all: a card that opened
    /// onto a screen which could not answer its own question would be worse than no
    /// card. The outcome is remembered, so a refusal is reported once rather than on
    /// every visit to the menu.
    /// </summary>
    internal static bool EnsureAdopted()
    {
        lock (AdoptionGate)
        {
            if (_adoptionAttempted) return _adopted;
            _adoptionAttempted = true;

            if (!Started)
            {
                Log.Error($"[{ModId}] the mod refused to start; not adding the mode card.", 2);
                return false;
            }

            try
            {
                var startup = EngineHost.AdoptRunningGame();
                _adopted = true;
                Log.Info(
                    $"[{ModId}] adopted the running game: {startup.ModelsRegistered} models registered", 2);
            }
            catch (Exception ex)
            {
                Log.Error(
                    $"[{ModId}] refusing to report on this game: {ex.GetType().Name}: {ex.Message}", 2);
            }

            return _adopted;
        }
    }
}

/// <summary>
/// Teaches the runtime that this mod's own assemblies sit beside it, in the load
/// context the mod itself was loaded into.
///
/// The game resolves exactly two names for a mod - its own assembly and Harmony -
/// and loads the mod's DLL by path, so nothing would find
/// <c>Sts2PilotTrainer.Engine.dll</c> and the rest shipped in the same directory.
///
/// Which context they land in is the whole point, and getting it wrong is not a
/// tidiness question. Godot loads the game into its own load context, not the
/// default one. A resolver installed on the default context satisfies the lookup
/// there, so <c>Sts2PilotTrainer.Engine</c> loads into the default context, and its
/// reference to <c>sts2</c> is then resolved by the runtime's own probing - which
/// finds the file again and loads a <em>second</em> copy of the game assembly.
/// Everything about that copy looks right, including its path, and everything in it
/// is uninitialised: the model database is empty, the startup phase reads
/// <c>None</c>, and touching its statics ends the process rather than returning a
/// wrong answer. So the resolver goes on this assembly's own context, where
/// <c>sts2</c> is already loaded and binds to the game's copy.
///
/// Installed as the first statement of the mod initializer, which is early enough
/// because every method that mentions a sibling assembly is behind a call the JIT
/// only prepares afterwards.
/// </summary>
internal static class SiblingAssemblies
{
    private static int _installed;

    internal static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1) return;

        var self = typeof(SiblingAssemblies).Assembly;
        var directory = Path.GetDirectoryName(self.Location);
        if (string.IsNullOrEmpty(directory)) return;

        var context = AssemblyLoadContext.GetLoadContext(self) ?? AssemblyLoadContext.Default;
        context.Resolving += (resolving, name) =>
        {
            if (name.Name is null) return null;
            var path = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(path) ? resolving.LoadFromAssemblyPath(path) : null;
        };
    }
}

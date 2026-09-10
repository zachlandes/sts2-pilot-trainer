using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

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
/// each feature lives behind <see cref="IRunmobileModule"/>. Today there are three
/// modules: recorded fights, the recorder and the run library.
///
/// Mod initialization deliberately reads nothing about the game. It runs inside the
/// game's "very early" startup phase, one phase before the game builds its model
/// database and id-serialization cache, so there is no game to read yet: asking then
/// took the process down with a segmentation fault rather than an error. Everything
/// that reads the running game happens from a surface a player has reached. See
/// docs/in-game-host.md.
///
/// It refuses rather than degrades. A module that cannot establish what it needs
/// says so in the game's log, installs no patch and contributes no surface.
/// </summary>
[ModInitializer(nameof(Initialize))]
public static class RunmobileMod
{
    internal const string ModId = "Runmobile";

    /// <summary>The id every patch this mod installs is owned by. Taken from the
    /// rules rather than written twice: <c>EnvironmentPreflight</c> tells our patches
    /// from somebody else's in a recording's roster by this exact string, and a mod
    /// that could rename itself out of its own rule would pass it by accident.</summary>
    private const string HarmonyId = PatchRoster.HostOwnerId;

    internal static IReadOnlyList<Type> ShellPatchClasses { get; } =
        [typeof(SingleplayerMenuRetention), .. CardScreensUp.PatchClasses, .. GameSessionWatch.PatchClasses];

    private static readonly Lock AdoptionGate = new();

    private static bool _adoptionAttempted;
    private static bool _adopted;
    private static bool _nativeTextVerified;

    /// <summary>Whether the mod started without refusing. Distinct from any module
    /// being enabled: the shell starting is about this process, and a module being
    /// enabled is about what that module could establish.</summary>
    internal static bool Started { get; private set; }

    /// <summary>
    /// Every feature this build carries, in the order they are installed.
    ///
    /// Recorded fights, the recorder, and the run library.
    /// </summary>
    internal static IReadOnlyList<IRunmobileModule> Modules { get; } =
        [RecordedFightModule.Instance, RecorderModule.Instance, RunLibraryModule.Instance];

    /// <summary>The modules that could establish what they need in this process.</summary>
    internal static IEnumerable<IRunmobileModule> EnabledModules => Modules.Where(module => module.Enabled);

    /// <summary>
    /// Whether a module may put anything in front of the player right now.
    ///
    /// The gate is here rather than in each module for the same reason the write
    /// barrier is the shell's: what this mod may do to somebody else's session is not
    /// a decision a feature gets to make for itself, and a module that has not been
    /// written yet would be a module that had not been told.
    /// <see cref="GameSessionWatch"/> owns the reading, and a module knows only that
    /// the shell said no - it never learns what the answer depended on.
    ///
    /// Every surface goes through this, not only the cards. A module's own Harmony
    /// patches draw where the shell never looks - the run library's Compendium button
    /// and its run-history plate are both of those - so this is what those entry points
    /// ask before they draw. A silent shell is silent rather than degraded: nothing is
    /// drawn, including a refusal saying why, because a refusal is itself this mod
    /// speaking in somebody else's session.
    /// </summary>
    internal static bool MayDraw => GameSessionWatch.MaySpeak;

    internal static bool AdoptionRefused
    {
        get
        {
            lock (AdoptionGate) return _adoptionAttempted && !_adopted;
        }
    }

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
        LogThePatchRoster();
        Started = true;
        Log.Info($"[{ModId}] loaded", 2);
    }

    /// <summary>
    /// Writes what is patched in this process into the game's own log, one line per
    /// member, the moment this mod's patches are installed.
    ///
    /// It is a fingerprint of what actually attached, which is not what the mod
    /// intended to attach. A patch whose target this build renamed resolves to
    /// nothing and applies silently; the module that owns it refuses first
    /// (<see cref="RecorderModule"/>), and this is what says so from the other end,
    /// in a file a player can attach to a bug report without rebuilding anything.
    /// It also names anybody else patching this game, which is the question a
    /// content hash cannot answer.
    ///
    /// A diagnostic never takes the mod down with it. Failing to describe the patches
    /// is not failing to have installed them, and refusing to run over a log line
    /// would be a worse outcome than the missing line.
    /// </summary>
    private static void LogThePatchRoster()
    {
        try
        {
            var roster = HarmonyRoster.Read();
            Log.Info(
                $"[{ModId}] patch roster: {roster.Members.Count} patched member(s) in this process", 2);
            foreach (var member in roster.Members)
            {
                Log.Info($"[{ModId}]   {member.Describe()}", 2);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[{ModId}] could not read the patch roster: {ex.GetType().Name}: {ex.Message}", 2);
        }
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
    ///
    /// That promise is about a module which declares itself disabled. A module whose
    /// <see cref="IRunmobileModule.Install"/> throws propagates out of this loop,
    /// aborts <see cref="Start"/> before <see cref="Started"/> is set, and may leave
    /// the patches it had already applied in place. That is a broken build rather than
    /// a runtime condition, and the failure-isolation lifecycle that would contain it
    /// is still not built.
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
    /// Not from mod loading, because the game has no model database yet then. Every
    /// feature that reads the engine asks this for itself at the first moment it has
    /// demonstrably got a running game - the recorder when a run has entered its first
    /// room - so no feature's correctness rests on another
    /// having asked first. It is the mod's one adoption entry and answers the same way
    /// however many ask.
    ///
    /// <see cref="EngineHost.AdoptRunningGame"/> refuses anything it cannot read
    /// honestly, and a refusal here is the caller's to act on: no recording. A
    /// recording that named a build nobody read off this client would be worse than not
    /// being there. The outcome is remembered, so a refusal is
    /// reported once rather than on every visit to the menu.
    ///
    /// Applying the player's retention policy happens here too and is not one of the
    /// things a refusal stops. Nothing it removes can race a journal being appended
    /// to: on the path where adoption succeeded it has run before the recorder is told
    /// a run exists, and where adoption failed the recorder opens no journal at all.
    /// </summary>
    internal static bool EnsureAdopted()
    {
        VerifyNativeText();
        var adopted = Adopt();

        // What this mod leaves on a player's disk is the shell's, and it does not
        // depend on this mod being able to read the game. Retention has its own
        // readiness condition and it is the store's own: a chosen save profile, which
        // is what says whose files these are. A game the engine layer refuses to adopt
        // still answers a player who asked for their runs to be removed, and a visit
        // where the profile was not resolved yet is retried at the next one, because
        // ApplyOnce latches only once it has actually run.
        RecordingRetention.ApplyOnce();

        return adopted;
    }

    /// <summary>
    /// Reads this build's own text roles once, here, and says in the log which ones it
    /// could not answer.
    ///
    /// Here rather than at mod start because the mod reads nothing at initialization,
    /// and a scene is the game. This is the mod's first moment with a running game. It
    /// is ahead of anything this mod draws from a menu, and not ahead of everything: the
    /// recorder asks for adoption at the first room of a run, so on a player who opens
    /// no Runmobile surface the sweep runs during that transition instead. Either way a
    /// role this build renamed is named in the log before the surface that asks for it
    /// refuses, rather than first being noticed by a player already entering a recorded
    /// fight - which is what a wrong node path cost once.
    ///
    /// Like the patch roster, a diagnostic never takes the mod down with it: failing to
    /// describe the typography is not failing to have it.
    /// </summary>
    private static void VerifyNativeText()
    {
        // Every surface asks for adoption, so this is reached at every menu press. The
        // roles themselves are read once and cached; the latch is so the summary is
        // said once too.
        if (_nativeTextVerified) return;
        _nativeTextVerified = true;

        try
        {
            var refused = GameText.Verify();
            if (refused.Count == 0) return;

            Log.Error(
                $"[{ModId}] {refused.Count} text role(s) this build does not have; every surface " +
                "that asks for one refuses: " + string.Join(", ", refused), 2);
        }
        catch (Exception ex)
        {
            Log.Error($"[{ModId}] could not read this build's text roles: {ex.GetType().Name}: {ex.Message}", 2);
        }
    }

    private static bool Adopt()
    {
        lock (AdoptionGate)
        {
            if (_adoptionAttempted) return _adopted;
            _adoptionAttempted = true;

            if (!Started)
            {
                Log.Error($"[{ModId}] the mod refused to start, so it will not take this game.", 2);
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

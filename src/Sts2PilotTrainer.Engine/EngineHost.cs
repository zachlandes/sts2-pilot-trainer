using MegaCrit.Sts2.Core.Models;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Brings the real game assembly up inside a plain console process, once.
///
/// The game expects a Godot scene tree, a frame loop, and a platform layer, and
/// has none of them here. Everything this class does is about supplying the
/// minimum those subsystems need in order to stay out of the way - never about
/// changing what the game decides. Anything that would change a decision belongs
/// in a manifest caveat, not in this file.
/// </summary>
public static class EngineHost
{
    private static bool _started;
    private static readonly Lock Gate = new();

    /// <summary>Set once initialisation succeeds, so callers can report what the
    /// engine actually said rather than what we hoped it would say.</summary>
    public static EngineStartupReport? Startup { get; private set; }

    /// <summary>How the engine in this process came to be up. Everything downstream
    /// that reads the game has to know, because a headless copy and a retail client
    /// keep the same values in different places.</summary>
    public static EngineOrigin Origin { get; private set; } = EngineOrigin.None;

    /// <summary>
    /// Takes the engine this process already has, instead of building one.
    ///
    /// This is the entry point for code running inside the retail game: the mod host.
    /// <see cref="Start"/> must never run there. It sets the engine's test-mode flag,
    /// neutralises the save subsystem and declares the mod loader finished with
    /// nothing loaded - defensible in a console process that owns everything, and a
    /// corruption of a player's session in the client that owns nothing.
    ///
    /// Adoption is a refusal-first check rather than an assumption. Every condition
    /// below is something a caller would otherwise read a plausible wrong answer
    /// from: an empty model database hashes to a stable, meaningless number, and a
    /// process with test mode on is not a retail client whatever else it looks like.
    /// It writes nothing, patches nothing and initialises nothing.
    /// </summary>
    public static EngineStartupReport AdoptRunningGame()
    {
        lock (Gate)
        {
            if (Origin == EngineOrigin.RunningGame) return Startup!;
            if (_started)
            {
                throw new EngineException(
                    "This process already started its own headless engine, so there is no running game to " +
                    "adopt. The two hosts are mutually exclusive by design.");
            }

            var refusals = RunningGameRefusals();
            if (refusals.Count > 0)
            {
                throw new EngineException(
                    "This process is not a game whose state can be read honestly; refusing to report on it:\n" +
                    string.Join("\n", refusals.Select(refusal => $"  - {refusal}")));
            }

            Startup = new EngineStartupReport(
                ModelsRegistered: MegaCrit.Sts2.Core.Models.ModelDb.All.Count(),
                ModelsFailed: 0,
                Warnings: [],
                Failures: []);
            Origin = EngineOrigin.RunningGame;
            _started = true;
            return Startup;
        }
    }

    /// <summary>
    /// Everything that would make a reading of this process untrustworthy. Empty
    /// means the game is up and answering for itself.
    /// </summary>
    private static List<string> RunningGameRefusals()
    {
        var refusals = new List<string>();

        // First, and alone: a game that has not finished its essential
        // initialization has no model database and no id-serialization cache, and
        // every reading below would be taken against a store that does not exist
        // yet. This is not a hypothetical - the game runs mod initializers in its
        // "very early" phase, one phase before it builds either - and reading
        // through it does not fail, it takes the process down. So this returns
        // rather than accumulating, and nothing after it is touched.
        var phase = StartupPhase();
        if (phase is not ("Essential" or "Done"))
        {
            refusals.Add(
                $"the game's startup phase is '{phase ?? "unreadable"}', not one where it has a model " +
                "database and an id-serialization cache to read. Adopt it from a surface the player can " +
                $"reach, not from mod loading, which the game runs before either exists. Read from " +
                $"{GameAssemblyProvenance()}.");
            return refusals;
        }

        if (MegaCrit.Sts2.Core.TestSupport.TestMode.IsOn)
        {
            refusals.Add(
                "the engine's test mode is on, which means this is a headless or test process rather than a " +
                "retail client, and several gameplay paths behave differently under it");
        }

        var models = 0;
        try
        {
            models = MegaCrit.Sts2.Core.Models.ModelDb.All.Count();
        }
        catch (Exception ex)
        {
            refusals.Add($"the model database could not be enumerated: {ex.GetType().Name}: {ex.Message}");
        }

        if (models == 0) refusals.Add("the model database is empty, so there is no content to compare against");

        try
        {
            if (EngineInitialization.ContentHash() == "0")
            {
                refusals.Add(
                    "the game reports content hash 0, which means its id database never initialised - a hash " +
                    "over nothing is stable and meaningless");
            }
        }
        catch (Exception ex)
        {
            refusals.Add($"the game's content hash could not be read: {ex.GetType().Name}: {ex.Message}");
        }

        if (RunningGameReleaseInfo() is null)
        {
            refusals.Add("the game has no release info loaded, so the build it is running cannot be identified");
        }

        if (MegaCrit.Sts2.Core.Saves.SaveManager.Instance?.Progress is null)
        {
            refusals.Add(
                "no save progress is loaded, so there is no profile whose unlocks could be checked");
        }

        return refusals;
    }

    /// <summary>
    /// The game's own startup phase, by name, or null when this build no longer
    /// publishes one.
    ///
    /// The game keeps it in one private field and moves it through None, VeryEarly,
    /// Essential and Done. Essential is the step that calls <c>ModelDb.Init</c> and
    /// <c>ModelIdSerializationCache.Init</c>, so anything at or past it has the
    /// content this host reads. Read reflectively because the field is the game's
    /// own; nothing here writes it, and a build that no longer publishes it reads as
    /// unreadable rather than as ready.
    /// </summary>
    public static string? StartupPhase()
    {
        var assembly = typeof(MegaCrit.Sts2.Core.Models.ModelDb).Assembly;
        var type = assembly.GetType("MegaCrit.Sts2.Core.Helpers.OneTimeInitialization");
        var field = type?.GetField(
            "_state", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        return field?.GetValue(null)?.ToString();
    }

    /// <summary>
    /// Which game assembly these readings came from, and whether it is the only one.
    ///
    /// Named in every refusal because "the game says no" is only meaningful once it
    /// is clear which game was asked. A host that had bound to a second copy of the
    /// assembly would report an empty, unstarted world with total confidence, and
    /// the report would look exactly like a game that had not started yet.
    /// </summary>
    internal static string GameAssemblyProvenance()
    {
        var assembly = typeof(MegaCrit.Sts2.Core.Models.ModelDb).Assembly;
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .Count(candidate => candidate.GetName().Name == assembly.GetName().Name);
        var location = string.IsNullOrEmpty(assembly.Location) ? "an assembly with no file" : assembly.Location;
        return loaded == 1
            ? location
            : $"{location} ({loaded.ToString(System.Globalization.CultureInfo.InvariantCulture)} assemblies " +
              "named sts2 are loaded, which is one too many)";
    }

    /// <summary>The release information the running game published about itself, or
    /// null when it has none. Read-only, and never substituted for.</summary>
    internal static object? RunningGameReleaseInfo()
    {
        var assembly = typeof(MegaCrit.Sts2.Core.Models.ModelDb).Assembly;
        var managerType = assembly.GetType("MegaCrit.Sts2.Core.Debug.ReleaseInfoManager");
        var instance = managerType
            ?.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            ?.GetValue(null);
        return managerType
            ?.GetProperty("ReleaseInfo", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            ?.GetValue(instance);
    }

    public static EngineStartupReport Start()
    {
        lock (Gate)
        {
            // An adopted retail process is already started, and this returns its
            // report rather than initialising over it. That is what makes every
            // reader below safe to call from inside the game.
            if (_started) return Startup!;
            if (Startup is { Ready: false } failed)
            {
                throw StartupFailure(failed);
            }

            // The game's async code posts continuations to the current context and
            // then waits for them. With no frame loop to pump a real context, those
            // waits never resume, so the context here runs them inline. This changes
            // when continuations run, never which ones run or in what order relative
            // to each other.
            SynchronizationContext.SetSynchronizationContext(new InlineSynchronizationContext());

            var report = EngineInitialization.InitializeOnce();
            Startup = report;
            if (!report.Ready) throw StartupFailure(report);

            Origin = EngineOrigin.HeadlessHost;
            _started = true;
            return report;
        }
    }

    private static EngineException StartupFailure(EngineStartupReport report) =>
        new(
            "Required engine initialization failed; refusing to run:\n" +
            string.Join("\n", report.Failures.Select(failure => $"  - {failure}")));

    /// <summary>
    /// The game's own content-database hash - the value its multiplayer layer
    /// compares between peers, and the value its version overlay renders as
    /// <c>HASH [...]</c>. Computed from the model database this process actually
    /// loaded, so it answers "what content is here", not "what content did someone
    /// claim was here".
    /// </summary>
    public static string ContentHash()
    {
        Start();
        return EngineInitialization.ContentHash();
    }

    /// <summary>Where the engine itself looks for its release-info file. Reported in
    /// preflight diagnostics so a version mismatch says where to put the file rather
    /// than only that something was missing.</summary>
    public static IReadOnlyList<string> ReleaseInfoSearchPaths()
    {
        Start();
        var type = typeof(ModelDb).Assembly.GetType("MegaCrit.Sts2.Core.Debug.ReleaseInfoManager");
        var method = type?.GetMethod(
            "GetPossibleReleaseInfoPaths",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return method?.Invoke(null, null) as string[] ?? [];
    }

    /// <summary>
    /// Every act this build ships, with its index and whether it is the default for
    /// that index. Reported by preflight because the game ships more than one act per
    /// index and picking the wrong variant produces a valid-looking run of entirely
    /// different content.
    /// </summary>
    public static IReadOnlyList<string> AvailableActs()
    {
        Start();
        return ModelDb.Acts
            .OrderBy(a => a.Index)
            .ThenBy(a => a.Id.ToString(), StringComparer.Ordinal)
            .Select(a => $"{a.Index}:{a.Id}{(a.IsDefault ? " (default)" : "")}")
            .ToList();
    }

    /// <summary>Number of models registered, useful as a sanity signal: a hash over
    /// an empty database is a perfectly stable, perfectly meaningless number.</summary>
    public static int RegisteredModelCount()
    {
        Start();
        return ModelDb.All.Count();
    }
}

/// <summary>
/// Where the engine in this process came from. Not a detail: the two origins keep
/// the same facts in different places, and one of them must never be initialised.
/// </summary>
public enum EngineOrigin
{
    /// <summary>Nothing has brought the engine up here yet.</summary>
    None,

    /// <summary>This process built its own headless engine out of the prepared copy
    /// under <c>build/lib</c>, with the patches <c>docs/headless-fidelity.md</c>
    /// records.</summary>
    HeadlessHost,

    /// <summary>The retail client brought itself up and this code was loaded into
    /// it as a mod. Nothing here initialised or patched anything.</summary>
    RunningGame,
}

/// <summary>What initialising the engine produced, including anything it refused.</summary>
public sealed record EngineStartupReport(
    int ModelsRegistered,
    int ModelsFailed,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Failures)
{
    public bool Ready => ModelsRegistered > 0 && ModelsFailed == 0 && Failures.Count == 0;
}

/// <summary>
/// Runs posted continuations immediately on the calling thread, draining anything
/// they post in turn. Ordering is preserved: a continuation posted while another is
/// running is queued and drained after it, never interleaved.
/// </summary>
internal sealed class InlineSynchronizationContext : SynchronizationContext
{
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _pending = new();
    private bool _draining;

    public override void Post(SendOrPostCallback d, object? state)
    {
        if (_draining)
        {
            _pending.Enqueue((d, state));
            return;
        }

        _draining = true;
        try
        {
            d(state);
            while (_pending.Count > 0)
            {
                var (callback, callbackState) = _pending.Dequeue();
                callback(callbackState);
            }
        }
        finally
        {
            _draining = false;
        }
    }

    public override void Send(SendOrPostCallback d, object? state) => d(state);

    /// <summary>Runs anything queued while another callback was executing. Called
    /// between actions so the engine's work finishes before the next decision.</summary>
    internal void DrainPending()
    {
        while (_pending.Count > 0)
        {
            var (callback, state) = _pending.Dequeue();
            _draining = true;
            try
            {
                callback(state);
            }
            finally
            {
                _draining = false;
            }
        }
    }
}

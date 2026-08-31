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

    public static EngineStartupReport Start()
    {
        lock (Gate)
        {
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

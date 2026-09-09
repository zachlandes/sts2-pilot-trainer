namespace Sts2PilotTrainer.Replay;

/// <summary>
/// How a running retail client reaches one boundary of a recording, or the decision
/// that stops it.
///
/// Four answers and nothing in between. <see cref="Walk"/> is the journey as it has
/// always been: the recording's own decisions, one at a time, on the game's own
/// screens. <see cref="Restore"/> stands the run at a floor arrival by continuing the
/// game's own save from there, which is the one way past a fight the client cannot
/// play. <see cref="RestoreThenWalk"/> does the first and then the second for the
/// decisions after the arrival. <see cref="Unreachable"/> names the first decision no
/// route gets past, so a surface can say what stopped it rather than that something
/// did.
///
/// A closed set, because a host dispatches on it: every route is executed by one
/// journey and a fifth answer would be a fifth path. <see cref="RetailPlayback.RouteTo"/>
/// is the one thing that produces one.
/// </summary>
public abstract record PlaybackRoute
{
    private PlaybackRoute()
    {
    }

    /// <summary>Whether this route gets to the boundary at all.</summary>
    public bool Reachable => this is not Unreachable;

    /// <summary>Every decision on the way is one the client issues, so the recording
    /// is walked from its start.</summary>
    public sealed record Walk : PlaybackRoute;

    /// <summary>
    /// The boundary is a floor arrival with a fight live at it, and the game's own
    /// save from that arrival is continued through the retail continue path.
    ///
    /// The floor and the action are the arrival's own coordinates, which is what a
    /// consumer needs to find or materialise the snapshot; whether one is cached is not
    /// this route's business, because a route that depended on a file being present
    /// would grey a row over a missing cache rather than over the history.
    /// </summary>
    public sealed record Restore(int Floor, int AfterSeq) : PlaybackRoute;

    /// <summary>
    /// The nearest restorable arrival before the boundary, then the recording's own
    /// decisions from there.
    ///
    /// Representable and, on v0.111.0, never produced: a fight is live at every
    /// restorable arrival, so the first decision after one is the fight's own, which
    /// the client does not issue. It exists because the walk through a fight is the
    /// next thing this shape gains, and the route it gains is this one.
    /// </summary>
    public sealed record RestoreThenWalk(int Floor, int AfterSeq) : PlaybackRoute;

    /// <summary>
    /// No route gets past this decision: the first the client cannot issue after the
    /// best restore point, or the first of all where there is none.
    /// </summary>
    public sealed record Unreachable(ActionRecord Refused) : PlaybackRoute;
}


namespace Sts2PilotTrainer.Replay;

/// <summary>Where a capture of a fight has got to.</summary>
public enum FightCaptureState
{
    /// <summary>The fight is live and every action so far has been sampled either side.</summary>
    Live,

    /// <summary>The fight ended inside a captured action. The trace holds a whole fight.</summary>
    Completed,

    /// <summary>The fight was left before it ended. Nothing about it is comparable.</summary>
    Abandoned,

    /// <summary>Something happened that the capture did not sample either side of, so
    /// the trace is not a continuous record of the fight. <see cref="FightCapture.Refusal"/>
    /// says what.</summary>
    Incomplete,
}

/// <summary>
/// A fight somebody is playing, sampled either side of every action into a
/// <see cref="ReplayTrace"/>.
///
/// The headless arbiter samples the canonical state before and after each action it
/// applies, and that trace is what <see cref="CombatProjection"/> derives from. A
/// fight played by a person in the retail client has no arbiter applying anything:
/// the game's own commands do the work, and the host can only observe them. This is
/// that observation, kept as the same trace, so the person's fight goes through the
/// same projection and the same comparison as the recording's rather than through a
/// second reading that would have to be reconciled with the first.
///
/// It is a lifecycle, and it refuses to be read early. A projection is defined over a
/// finished fight, so a capture only hands one over once the fight ended <em>inside</em>
/// an action it sampled. A fight that was left, or whose state moved between two
/// samples without an action in between, has a trace that is not a record of the
/// whole fight - and a comparison over it would state differences that mean nothing.
/// Both are refused with a sentence, and the trace is kept either way so what was
/// seen can still be read.
///
/// Nothing here reads the game. Every sample arrives from the caller, already filtered
/// to <see cref="ReplayTrace.SampledFields"/>, which is what keeps every rule in this
/// class testable on a machine that does not own the game.
/// </summary>
public sealed class FightCapture : IFightSampleSink
{
    /// <summary>The verb of the step that marks where the capture began. It is the
    /// same moment the headless trace's combat start is: the sample after the action
    /// that entered the fight.</summary>
    public const string CombatStartVerb = "combat_start";

    private readonly List<ReplayStep> _steps = [];
    private OpenStep? _open;
    private int _nextSeq;

    private FightCapture(string sourceId, string combatStartSnapshotDigest, ReplayStep boundary)
    {
        SourceId = sourceId;
        CombatStartSnapshotDigest = combatStartSnapshotDigest;
        _steps.Add(boundary);
    }

    /// <summary>Which line this is, for the comparison to name.</summary>
    public string SourceId { get; }

    /// <summary>Digest of the complete canonical state at the boundary the capture
    /// began from. Carried into the projection, where the comparison requires it to
    /// be the recording's.</summary>
    public string CombatStartSnapshotDigest { get; }

    public FightCaptureState State { get; private set; } = FightCaptureState.Live;

    /// <summary>Why this capture holds no comparable fight, or null while it might.</summary>
    public string? Refusal { get; private set; }

    /// <summary>Whether an action has been sampled before and not yet after.</summary>
    public bool HasOpenStep => _open is not null;

    /// <summary>
    /// Whether the player has taken an action of their own yet.
    ///
    /// The combat-start boundary is a step in the trace and is nobody's action, so the
    /// number of steps is one from the moment a capture exists. This counts only what
    /// was played.
    /// </summary>
    public bool AnythingPlayed => _nextSeq > 0;

    /// <summary>
    /// Everything sampled so far, in order. Available in every state: a trace of a
    /// fight that was abandoned is still what was seen of it, and it is the refusal
    /// that says it is not comparable, not the absence of data.
    /// </summary>
    public ReplayTrace Trace => new() { Steps = _steps.ToList() };

    /// <summary>
    /// Starts capturing at a combat-start boundary.
    /// </summary>
    /// <param name="sourceId">Which line this is.</param>
    /// <param name="boundary">The sampled canonical state at combat start.</param>
    /// <param name="combatStartSnapshotDigest">Digest of the complete canonical state
    /// at that same moment.</param>
    /// <exception cref="ManifestException">When the boundary is not a live fight, or
    /// no digest is supplied. A capture that began anywhere else would produce a trace
    /// whose first sample is not a combat start, and every derivation downstream
    /// would be over the wrong fight.</exception>
    public static FightCapture Begin(
        string sourceId, IReadOnlyDictionary<string, string> boundary, string combatStartSnapshotDigest)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new ManifestException("A captured fight needs a source id for the comparison to name it by.");
        }

        if (string.IsNullOrWhiteSpace(combatStartSnapshotDigest))
        {
            throw new ManifestException(
                "A fight cannot be captured for comparison without the complete combat-start snapshot digest " +
                "of the boundary it begins from.");
        }

        if (Outcome(boundary) != InProgress)
        {
            throw new ManifestException(
                $"Capture can only begin inside a live fight, and this boundary reads combat.outcome " +
                $"'{Outcome(boundary)}'.");
        }

        var sample = ReplayTrace.Sample(boundary);
        return new FightCapture(sourceId, combatStartSnapshotDigest, new ReplayStep
        {
            Seq = -1,
            Verb = CombatStartVerb,
            Before = sample,
            After = sample,
        });
    }

    /// <summary>
    /// Records that an action is about to happen, with the state it happens from.
    ///
    /// Continuity is checked here rather than assumed: the state an action begins
    /// from has to be the state the previous one left. A gap between them is a change
    /// no action accounts for, and a trace with a gap in it is not a record of the
    /// fight. It is refused rather than bridged, because bridging it would attribute
    /// the gap's damage to nothing and the projection would quietly under-count.
    ///
    /// The same rule is what lets an action that begins while another is still open be
    /// recorded rather than refused, but only where the engine had nothing pending:
    /// then this action's before-sample <em>is</em> the previous action's after-sample,
    /// and closing the previous one with it invents nothing.
    /// </summary>
    /// <param name="previousActionFinished">
    /// Whether the action still open had already finished executing when this one
    /// began. Only consulted when an action is still open, and then it is the
    /// difference between a sample that is exact and one that would be a guess.
    /// </param>
    public void BeginStep(
        string verb,
        IReadOnlyDictionary<string, string> args,
        IReadOnlyDictionary<string, string> before,
        bool previousActionFinished = false)
    {
        if (State != FightCaptureState.Live) return;

        if (_open is not null)
        {
            // Two actions can begin one after the other with nothing between them:
            // measured in the retail client, where one click played a held card and
            // ended the turn, so the card's after-sample had not been taken when the
            // ended turn began.
            //
            // Where the open action had already finished executing, this sample is
            // exactly its after-state - the state an action begins from is the state
            // the one before it left - so it is closed with it and nothing is guessed.
            // Where it had not, the two actions genuinely overlap, sampling now would
            // attribute one's effects to the other, and the capture refuses as it
            // always has.
            if (!previousActionFinished)
            {
                Refuse(
                    $"A '{verb}' began while the '{_open.Verb}' before it had not been sampled afterwards, so the " +
                    "capture cannot say what each of them did.");
                return;
            }

            CompleteStep(before);
            if (State != FightCaptureState.Live) return;
        }

        var sample = ReplayTrace.Sample(before);
        var last = _steps[^1].After;
        if (!ReplayTrace.SameSample(last, sample))
        {
            var differences = last.Keys.Union(sample.Keys, StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Where(field => !string.Equals(
                    last.GetValueOrDefault(field), sample.GetValueOrDefault(field), StringComparison.Ordinal))
                .ToList();
            Refuse(
                $"The fight changed between the previous action and this '{verb}' with no action in between " +
                $"({string.Join(", ", differences)}), so the capture is not a continuous record of the fight.");
            return;
        }

        _open = new OpenStep(verb, new SortedDictionary<string, string>(
            args.ToDictionary(arg => arg.Key, arg => arg.Value, StringComparer.Ordinal), StringComparer.Ordinal), sample);
    }

    /// <summary>
    /// Records the state the open action left.
    ///
    /// If the fight is no longer in progress afterwards, the fight ended inside this
    /// action and the capture is complete - which is the only way it completes.
    /// </summary>
    public void CompleteStep(IReadOnlyDictionary<string, string> after)
    {
        if (State != FightCaptureState.Live) return;

        if (_open is null)
        {
            Refuse("An action was sampled afterwards with none open, so the capture cannot say what it followed.");
            return;
        }

        var sample = ReplayTrace.Sample(after);
        _steps.Add(new ReplayStep
        {
            Seq = _nextSeq++,
            Verb = _open.Verb,
            Args = _open.Args,
            Before = _open.Before,
            After = sample,
        });
        _open = null;

        if (Outcome(sample) != InProgress) State = FightCaptureState.Completed;
    }

    /// <summary>
    /// Forgets the open action without recording it, for an action the game itself
    /// took back before it took effect - an ended turn un-ended before the enemy
    /// turn began. The state it returns to is checked by the next
    /// <see cref="BeginStep"/> like any other.
    /// </summary>
    public void DiscardOpenStep()
    {
        if (State != FightCaptureState.Live) return;
        _open = null;
    }

    /// <summary>
    /// The fight has ended. Closes the open action with this final state if there is
    /// one; refuses if the fight ended with no action open, because an end nothing
    /// was sampled around is a change the trace does not account for.
    /// </summary>
    public void Finish(IReadOnlyDictionary<string, string> final)
    {
        if (State != FightCaptureState.Live) return;

        if (_open is not null)
        {
            CompleteStep(final);
            if (State == FightCaptureState.Live)
            {
                Refuse(
                    "The fight was reported over, but the state sampled after its last action still reads " +
                    "as in progress, so the capture cannot say how it ended.");
            }
            return;
        }

        Refuse(
            "The fight ended with no action being sampled, so its end belongs to nothing the capture " +
            "recorded. The trace is not a continuous record of the fight.");
    }

    /// <summary>The observer could not account for the fight continuously.</summary>
    public void MarkIncomplete(string reason)
    {
        if (State != FightCaptureState.Live) return;
        Refuse(reason);
    }

    /// <summary>The fight was left before it ended: the run was quit, or torn down.</summary>
    public void Abandon()
    {
        if (State != FightCaptureState.Live) return;
        State = FightCaptureState.Abandoned;
        Refusal = "The fight was abandoned before it ended, so it has no completed line to project.";
        _open = null;
    }

    /// <summary>
    /// The captured fight, as the comparison contract reads it.
    /// </summary>
    /// <exception cref="ManifestException">Unless the fight ended inside a sampled
    /// action. The refusal is the capture's own sentence about why not.</exception>
    public CombatProjection Project()
    {
        if (State != FightCaptureState.Completed)
        {
            throw new ManifestException(Refusal ??
                "This fight has not ended, so it has no completed line to project. Total turns, net health " +
                "change and final health are all defined at the end of a fight.");
        }

        return CombatProjection.FromTrace(SourceId, Trace, CombatStartSnapshotDigest);
    }

    private void Refuse(string reason)
    {
        State = FightCaptureState.Incomplete;
        Refusal = "Your fight could not be captured completely, so it is not compared. " + reason;
        _open = null;
    }

    private const string InProgress = "in_progress";

    private static string Outcome(IReadOnlyDictionary<string, string> sample) =>
        sample.GetValueOrDefault("combat.outcome", "none");

    private sealed record OpenStep(
        string Verb, IReadOnlyDictionary<string, string> Args, IReadOnlyDictionary<string, string> Before);
}

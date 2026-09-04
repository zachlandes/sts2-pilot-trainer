using System.Globalization;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Constructs the recording's run and walks it to the start of the recording's
/// fight, one recorded decision at a time.
///
/// The owner of "stand somebody in that fight". It adds no rules: the environment
/// gate is <see cref="Preflight"/>'s, the run is <see cref="GameSession"/>'s, each
/// decision is <see cref="RunDriver"/>'s, the ordered plan is
/// <see cref="RecordedFightPlan"/>'s and the proof at the boundary is
/// <see cref="CombatStartEquality"/>'s. What it owns is the sequence, and the
/// insistence that nothing skips a step.
///
/// Three things it will not do. It will not construct a run over one that already
/// exists. It will not accept a decision the plan does not authorise at this point,
/// wherever that decision came from. And it will not report a fight as entered
/// unless the live state at the boundary is the recorded one - a player put into a
/// fight that drifted would be playing a fight nothing could compare, which is the
/// failure this whole project is built to prevent.
///
/// The progress the run is generated against is supplied, not read. That is not a
/// shortcut around the player's profile: the run being constructed is the
/// recording's, and it has to be generated against the state its content came from.
/// Where the recording carries that state, it is what is supplied, which is what
/// makes this symmetric - a viewer with fewer unlocks and a viewer with more both get
/// the recorded player's state, because neither one's own ever enters the run.
/// Nothing here writes to a profile, and the run is set up with saving off; see
/// <c>GameSession.PrepareRunInRunningGame</c> and docs/environment-identity.md.
/// </summary>
public sealed class RecordedFightEntry : IDisposable
{
    /// <summary>
    /// The progress model this recording's run is generated against, decided once.
    ///
    /// A host has to construct the run against the state the recording's content came
    /// from, and it has to ask its own eligibility question against the same one -
    /// otherwise a screen reports a requirement that nothing consults. One rule so the
    /// two can never be asked different questions.
    ///
    /// A recording that carries the state itself gets that state. A recording that
    /// does not - which is every recording read off a video, because no video shows an
    /// unlock state - asks for completeness instead, and the complete state is what is
    /// supplied. Neither is a reading of the person in front of the game.
    /// </summary>
    public static PlayerProgress SuppliedProgressFor(ReplayManifest recording) =>
        recording.Environment.Unlocks.Value.Inventory is { } inventory
            ? PlayerProgress.Exact(inventory)
            : PlayerProgress.AllUnlocked;

    private readonly GameSession _session;
    private readonly RunDriver _driver;
    private readonly PlayerProgress _progress;

    private RecordedFightEntry(
        ReplayManifest manifest, IBoundaryPlan plan, GameSession session, PlayerProgress progress,
        Func<MapCoord, Task>? travelInRunningGame)
    {
        Manifest = manifest;
        Plan = plan;
        _session = session;
        _progress = progress;
        _driver = new RunDriver(session, travelInRunningGame);
    }

    public ReplayManifest Manifest { get; }

    /// <summary>The recording's decisions before the boundary, and the boundary.</summary>
    public IBoundaryPlan Plan { get; }

    /// <summary>Which fight of the recording this entry stands in, or a refusal when
    /// its boundary is not a fight's.</summary>
    public int Fight => Plan.Fight ?? throw new EngineException(
        $"{Plan.Describe()} is not the start of a fight, so there is no fight of the recording to compare " +
        "a played line against.");

    /// <summary>How many of the plan's steps have been executed.</summary>
    public int StepsTaken { get; private set; }

    /// <summary>The decision the recording made next, or null once they are all
    /// made.</summary>
    public ActionRecord? NextStep =>
        StepsTaken < Plan.PrefixActions.Count ? Plan.PrefixActions[StepsTaken] : null;

    /// <summary>Whether every recorded decision has been made and the run should now
    /// be standing at the boundary. Whether it actually is, is
    /// <see cref="VerifyBoundary"/>'s question.</summary>
    public bool AtBoundary => StepsTaken == Plan.PrefixActions.Count;

    /// <summary>The run this entry constructed, for a host that has to finish
    /// launching it through the game's own continuation.</summary>
    public RunState PreparedRun => _session.RunState;

    /// <summary>Engine work the last step started that a host has to let finish on
    /// the game's own frames before the next one. Always null headlessly, where the
    /// driver waits for it itself.</summary>
    public Task? Pending => _driver.Pending;

    /// <summary>
    /// Whether this game can construct the recording's run, asked before offering to.
    ///
    /// The same rules as everywhere else, over the progress model the run will
    /// actually be generated against. The eligibility screen asks the same question,
    /// so no saved-profile shortfall is presented as blocking a run that does not
    /// consult it.
    /// </summary>
    public static bool CanConstruct(
        ReplayManifest recording, out PreflightResult gate,
        PlayerProgress? progress = null)
    {
        gate = Preflight.Evaluate(
            recording.Environment, progress ?? SuppliedProgressFor(recording), recording.Source.Kind);
        return gate.Matches && LocalEnvironment.ReadStartedRun() is null;
    }

    /// <summary>
    /// Builds the recording's run in this headless process and enters its first
    /// room, ready for the first recorded decision.
    /// </summary>
    public static RecordedFightEntry StartHeadless(
        ReplayManifest manifest, PlayerProgress? progress = null) =>
        StartHeadless(manifest, RecordedFightPlan.For(manifest), progress);

    /// <inheritdoc cref="StartHeadless(ReplayManifest, PlayerProgress)"/>
    /// <param name="plan">Which boundary of the recording to walk to. A fight's or a
    /// floor's; the journey is the same and only the proof at the end differs.</param>
    public static RecordedFightEntry StartHeadless(
        ReplayManifest manifest, IBoundaryPlan plan, PlayerProgress? supplied = null)
    {
        var progress = supplied ?? SuppliedProgressFor(manifest);
        var entry = Prepare(manifest, plan, progress, travelInRunningGame: null, session => session.StartRun(
            manifest.Environment.Seed.Value,
            manifest.Environment.Character.Value,
            manifest.Environment.Ascension.Value,
            manifest.Environment.GameMode.Value,
            manifest.Environment.Acts.Value,
            progress));

        entry._driver.EnterFirstRoom();
        return entry;
    }

    /// <summary>
    /// Builds the recording's run inside the retail client and stops where
    /// presentation begins.
    ///
    /// The caller drives the game's own start-run continuation with
    /// <see cref="PreparedRun"/>, which is what loads the scene and enters the first
    /// act, and then steps this entry through the plan.
    /// </summary>
    /// <param name="travelInRunningGame">
    /// How the host issues a map move on the game's own map screen. Required here: a
    /// map move in the client is a screen's command, and the engine's own coordinate
    /// entry is only the middle of it.
    /// </param>
    public static RecordedFightEntry PrepareInRunningGame(
        ReplayManifest manifest, Func<MapCoord, Task> travelInRunningGame,
        PlayerProgress? progress = null) =>
        PrepareInRunningGame(manifest, RecordedFightPlan.For(manifest), travelInRunningGame, progress);

    /// <inheritdoc cref="PrepareInRunningGame(ReplayManifest, Func{MapCoord, Task}, PlayerProgress)"/>
    public static RecordedFightEntry PrepareInRunningGame(
        ReplayManifest manifest, IBoundaryPlan plan, Func<MapCoord, Task> travelInRunningGame,
        PlayerProgress? supplied = null) =>
        PrepareAgainst(manifest, plan, travelInRunningGame, supplied ?? SuppliedProgressFor(manifest));

    /// <summary>The same, with the progress model already decided, so that the model
    /// the run is built against and the model handed to the session are the one
    /// value.</summary>
    private static RecordedFightEntry PrepareAgainst(
        ReplayManifest manifest, IBoundaryPlan plan, Func<MapCoord, Task> travelInRunningGame,
        PlayerProgress progress) =>
        Prepare(manifest, plan, progress, travelInRunningGame, session => session.PrepareRunInRunningGame(
            manifest.Environment.Seed.Value,
            manifest.Environment.Character.Value,
            manifest.Environment.Ascension.Value,
            manifest.Environment.GameMode.Value,
            manifest.Environment.Acts.Value,
            progress));

    private static RecordedFightEntry Prepare(
        ReplayManifest manifest, IBoundaryPlan plan, PlayerProgress progress,
        Func<MapCoord, Task>? travelInRunningGame, Action<GameSession> construct)
    {
        var validation = ManifestValidator.Validate(manifest);
        if (!validation.IsValid)
        {
            throw new ManifestException("Manifest is not valid:\n" + validation.Describe());
        }

        // The prerequisites, asked of the progress model this run will actually be
        // generated against. The same question the arbiter asks before it constructs
        // a run, and the same rules; what differs between a host and a person is
        // which reading the rules are asked about, not the rules.
        var prerequisites = Preflight.Evaluate(manifest.Environment, progress, manifest.Source.Kind);
        if (!prerequisites.Matches)
        {
            throw new EngineException(Refusal(
                "This game cannot construct the recording's run:", prerequisites, "this machine has"));
        }

        var session = new GameSession();
        construct(session);

        // What the engine actually built, read back before a single decision is made.
        // A seed it normalised differently or an act that quietly defaulted would
        // otherwise replay perfectly into a different fight.
        var runIdentity = Preflight.EvaluateStartedRun(manifest.Environment);
        if (!runIdentity.Matches)
        {
            throw new EngineException(Refusal(
                "The run this game built is not the run the recording describes:", runIdentity,
                "the started run has"));
        }

        return new RecordedFightEntry(manifest, plan, session, progress, travelInRunningGame);
    }

    /// <summary>
    /// Makes the recording's next decision.
    ///
    /// The plan is the only authority on what that is. A host cannot pass one in, and
    /// a decision arriving from anywhere else - a screen the player reached, a key
    /// they pressed - is not this call and does not become authorised by happening.
    /// </summary>
    public void AdvanceOneStep()
    {
        var action = NextStep ?? throw new EngineException(
            $"Every decision the recording made before {Plan.Describe()} has already been made. There is " +
            "nothing further to execute.");

        if (!Plan.Authorises(StepsTaken, action))
        {
            // Unreachable through NextStep, and checked anyway: the plan is the whole
            // authority on what happens before the boundary, and a host that stopped
            // asking it would be a host that had learned one recording by heart.
            throw new EngineException(
                $"Action {action.Seq} ({action.Verb}) is not the decision this recording made at step " +
                $"{StepsTaken.ToString(CultureInfo.InvariantCulture)} on the way to {Plan.Describe()}.");
        }

        if (InCombat() && !AllowedWhileFighting.Contains(action.Verb))
        {
            throw new EngineException(
                $"The run is in a fight and the recording's next decision is a '{action.Verb}', which is " +
                "not one a fight accepts. The run entered a fight the recording did not, so this journey is " +
                "not the recording's. Refusing to keep going.");
        }

        _driver.Apply(action, RemainingPrefix());
        StepsTaken++;
    }

    /// <summary>
    /// The recording's decisions after the next one, which the driver needs because a
    /// card screen is answered from inside the call that opens it.
    ///
    /// The plan stops at the boundary's own action, so the last step would otherwise be
    /// handed nothing at all; the screen it opens is answered from the recording's own
    /// selections immediately after it, exactly as a whole replay answers it.
    ///
    /// No history on v0.111.0 reaches that last case, and the reason is worth writing
    /// down because it is a fact about the game rather than about these fixtures.
    /// <see cref="BoundarySelector.PlanFor"/> refuses a turn boundary, so a plan only
    /// ever ends at a combat start or a floor arrival. A floor arrival's action is
    /// always a map move, and a map move is not one of the seven verbs
    /// <c>RunDriver.Apply</c> hands the upcoming actions to - an opening blessing, an
    /// event option, a potion, a shop purchase, a rest site option, a card played and
    /// an end of turn - so nothing can follow it. A combat start's
    /// action is that same map move unless an event option began the fight - and of the
    /// five events that call <c>EventModel.EnterCombatWithoutExitingEvent</c> on this
    /// build (Punch Off, Fake Merchant, Battleworn Dummy, Dense Vegetation, The Lantern
    /// Key), not one opens a card-selection screen. The two sets do not intersect. An
    /// event that both opens a screen and starts its room's fight would disprove this
    /// and would be the history to generate; the branch is here because the rule is the
    /// arbiter's, not because this build happens to exercise it.
    /// </summary>
    private IReadOnlyList<ActionRecord> RemainingPrefix() =>
        StepsTaken + 1 < Plan.PrefixActions.Count
            ? Plan.PrefixActions.Skip(StepsTaken + 1).ToList()
            : CardScreenAnswers.After(Manifest.Actions, Plan.PrefixActions[StepsTaken].Seq);

    /// <summary>Makes every remaining recorded decision, in order. The same steps,
    /// without stopping between them.</summary>
    public void AdvanceToBoundary()
    {
        while (!AtBoundary) AdvanceOneStep();
    }

    /// <summary>
    /// What the recording's next decision is, in the terms a screen needs to say it.
    ///
    /// Read from the run the decision is about to act on, never from a table: the
    /// relic an opening blessing grants is the event's own answer, and what kind of
    /// node a move enters is the generated map's. A host that wrote either down would
    /// be a host that had learned one recording by heart.
    /// </summary>
    public PrefightChoice DescribeNextStep() =>
        DescribeNextStepOrNull()
        ?? throw new EngineException(
            $"Action {NextStep!.Seq} is a '{NextStep.Verb}', which this trainer cannot show the recording " +
            "making. Only an opening blessing and a map move are supported before a fight.");

    /// <summary>
    /// The same, and null rather than a refusal when the decision is not one this
    /// trainer has words for.
    ///
    /// Both exist because the two callers want different things from the same
    /// question. A screen that is showing the recording make its decisions has to fail
    /// loudly on one it cannot show. A host walking to a later fight passes through
    /// that fight's predecessors - cards played, turns ended, loot taken - and an
    /// uncaptioned step there is an ordinary thing rather than a defect.
    /// </summary>
    public PrefightChoice? DescribeNextStepOrNull()
    {
        var action = NextStep ?? throw new EngineException(
            $"Every decision the recording made before {Plan.Describe()} has already been made; there is no " +
            "next one to describe.");

        return action.Verb switch
        {
            ActionVerb.ChooseNeowBlessing => new PrefightChoice.Blessing(action.Seq, BlessingRelic(action)),
            ActionVerb.MapMove => DescribeMapMove(action),
            _ => null,
        };
    }

    /// <summary>
    /// The relic the blessing this action takes would grant.
    ///
    /// Asked of the option itself rather than of the relics the player ends up with,
    /// so the caption can be shown before the decision is made rather than explained
    /// afterwards.
    /// </summary>
    private static string BlessingRelic(ActionRecord action)
    {
        var index = ArgumentInt(action, "option_index");
        var localEvent = RunManager.Instance.EventSynchronizer?.GetLocalEvent()
            ?? throw new EngineException(
                $"Action {action.Seq} takes an opening blessing, but this run is not standing in an event.");

        var options = localEvent.CurrentOptions;
        if (index < 0 || index >= options.Count)
        {
            throw new EngineException(
                $"Action {action.Seq} takes option {index.ToString(CultureInfo.InvariantCulture)} and this " +
                $"event offers {options.Count.ToString(CultureInfo.InvariantCulture)}.");
        }

        return options[index].Relic?.Id.ToString()
            ?? throw new EngineException(
                $"Action {action.Seq} takes an opening blessing that grants no relic, which this trainer has " +
                "no way to name.");
    }

    private PrefightChoice DescribeMapMove(ActionRecord action)
    {
        var row = ArgumentInt(action, "row");
        var column = ArgumentInt(action, "column");
        var map = _session.RunState.Map
            ?? throw new EngineException(
                $"Action {action.Seq} moves on a map this act has not generated.");

        var point = map.GetPoint(column, row)
            ?? throw new EngineException(
                $"Action {action.Seq} moves to (row {row.ToString(CultureInfo.InvariantCulture)}, column " +
                $"{column.ToString(CultureInfo.InvariantCulture)}), which does not exist in this act.");

        return new PrefightChoice.MapMove(
            action.Seq, point.PointType.ToString(), column, map.GetColumnCount());
    }

    private static int ArgumentInt(ActionRecord action, string name) =>
        action.Args.TryGetValue(name, out var raw)
            ? int.Parse(raw, CultureInfo.InvariantCulture)
            : throw new EngineException(
                $"Action {action.Seq} ({action.Verb}) is missing required argument '{name}'.");

    /// <summary>
    /// Whether the fight has finished opening and is the player's to act in.
    ///
    /// Entering the room is not the same moment as the fight being ready, and the
    /// retail client is where that stops being a distinction without a difference:
    /// the room is built as soon as the map move's task completes, and the opening
    /// hand is dealt over the frames after it. Asked in between, the boundary reads
    /// an empty hand and no energy - a real state, and not the one the recording
    /// describes. The engine's own turn phase is the signal that the player may act,
    /// which is exactly what "before any card is played" means.
    /// </summary>
    public bool IsReadyForThePlayer =>
        CombatManager.Instance is { IsInProgress: true } &&
        _session.RunState.Players[0].PlayerCombatState is { Phase: PlayerTurnPhase.Play };

    /// <summary>
    /// What the run says about its combat, for a refusal that has to explain itself.
    ///
    /// A host that gave up waiting for a fight to open should say what it was looking
    /// at, or the next person reads the same sentence and learns nothing.
    /// </summary>
    public string DescribeCombatReadiness()
    {
        var room = _session.RunState.CurrentRoom?.RoomType.ToString() ?? "none";
        var manager = CombatManager.Instance;
        var combat = _session.RunState.Players[0].PlayerCombatState;
        return $"room={room}, combat manager={(manager is null ? "none" : manager.IsInProgress ? "in progress" : "not in progress")}, " +
               $"player combat state={(combat is null ? "none" : combat.Phase.ToString())}, " +
               $"turn={(combat is null ? "-" : combat.TurnNumber.ToString(CultureInfo.InvariantCulture))}";
    }

    /// <summary>The live run's canonical state, as the arbiter reads it.</summary>
    public CanonicalState LiveState() => CanonicalStateProjection.Project(_session.RunState);

    /// <summary>
    /// Whether the fight now live is the fight the recording starts.
    ///
    /// Asked after the last recorded decision and before a player is given the
    /// controls. It refuses two different ways of being wrong: the run is not in a
    /// fight at all, or it is in one that does not match what the recording observed
    /// and the engine-produced snapshot digest. Both are drift, and neither is entered.
    /// </summary>
    public BoundaryEquality VerifyBoundary()
    {
        if (!AtBoundary)
        {
            throw new EngineException(
                $"{Plan.PrefixActions.Count - StepsTaken} of the recording's decisions before " +
                $"{Plan.Describe()} have not been made yet, so there is nothing to compare against.");
        }

        var expectedDigest = Manifest.BoundaryAt(Plan.Kind, fight: Plan.Fight, floor: Plan.Floor)?.Digest.Value
            ?? throw new ManifestException(
                $"The recording declares no boundary for {Plan.Describe()}, so there is no digest to compare " +
                "the live state against.");
        var state = LiveState();

        if (Liveness() is { } refusal)
        {
            return new BoundaryEquality
            {
                Kind = Plan.Kind,
                Matches = false,
                Comparisons = [],
                ExpectedDigest = expectedDigest,
                ActualDigest = state.Digest(),
                Refusal = refusal,
            };
        }

        return BoundaryEquality.Compare(Plan.Kind, Plan.Boundary, state.Fields, state.Digest(), expectedDigest);
    }

    /// <summary>
    /// Why the run is not standing where this plan's boundary is, or null when it is.
    ///
    /// Only a fight's boundary has one. A combat start with no combat in progress is a
    /// run that did not enter the fight the recording entered, and every field a
    /// combat checkpoint names would read as absent rather than as wrong - a refusal
    /// worth writing in words.
    ///
    /// A floor arrival deliberately has none. Arriving on a floor is entering its
    /// room, and this engine deals the room's fight in the same call, so "arrived and
    /// nothing decided yet" is not a state that exists. What proves a floor arrival is
    /// where the run stands and the digest of everything else, which is what the
    /// comparison below already asks.
    /// </summary>
    private string? Liveness() =>
        Plan.Kind == ReplayBoundary.CombatStartKind && !InCombat()
            ? "Every decision the recording made before its fight has been made and this run is not in a " +
              "fight. The recording enters one at this point, so the run this game generated is not the " +
              "recording's, and there is nothing here to hand over."
            : null;

    /// <summary>
    /// The source id the player's own line carries into a comparison. One constant,
    /// so the comparison names the same side the same way headlessly and in the
    /// client.
    /// </summary>
    public const string PlayerSourceId = "player";

    /// <summary>The capture of the fight after it was handed over, or null before.</summary>
    public FightCapture? Capture { get; private set; }

    /// <summary>
    /// Starts capturing the fight that has just been proved to be the recorded one.
    ///
    /// Only from a boundary that matched: the capture carries the digest the
    /// comparison will later require to be the recording's, and beginning one from a
    /// boundary that was not would produce a line that looks comparable and is not.
    /// </summary>
    /// <exception cref="EngineException">When the boundary did not match, or a
    /// capture already exists.</exception>
    public FightCapture BeginCapture(BoundaryEquality boundary)
    {
        if (Plan.Kind != ReplayBoundary.CombatStartKind)
        {
            throw new EngineException(
                $"A capture is a fight's, and this entry stands at {Plan.Describe()}. The supported unit of " +
                "comparison is a whole fight; see docs/comparison-direction.md.");
        }

        if (!boundary.Matches)
        {
            throw new EngineException(
                "The fight cannot be captured for comparison: it is not the recorded one. " +
                (boundary.Refusal ?? string.Empty));
        }

        if (Capture is not null)
        {
            throw new EngineException("This fight is already being captured.");
        }

        Capture = FightCapture.Begin(PlayerSourceId, LiveState().Fields, boundary.ActualDigest);
        return Capture;
    }

    /// <summary>The live canonical state, cut down to what a trace keeps.</summary>
    public IReadOnlyDictionary<string, string> SampleLiveState() => ReplayTrace.Sample(LiveState().Fields);

    /// <summary>
    /// Plays the recording's own fight to its end through the capture, headlessly.
    ///
    /// The command line's stand-in for a person: the recording's own actions after
    /// the boundary, applied by the same driver the replay uses, with the canonical
    /// state sampled either side of each one by <see cref="FightCapture"/> exactly as
    /// the in-game host samples a player's. That is what lets the capture, the
    /// projection and the comparison be exercised end to end with no scene tree, and
    /// it decides nothing: every action is the recording's.
    /// </summary>
    /// <exception cref="EngineException">Inside a running game, where the fight is
    /// the player's and nothing may play it for them; or when no capture has begun.</exception>
    public FightCapture PlayRecordedFightHeadless()
    {
        var capture = Capture
            ?? throw new EngineException("No capture has begun, so there is nothing to play the fight into.");

        var actions = Manifest.Actions
            .Where(action => action.Seq > Plan.BoundarySeq)
            .OrderBy(action => action.Seq)
            .ToList();

        for (var index = 0; index < actions.Count && capture.State == FightCaptureState.Live; index++)
        {
            var action = actions[index];
            capture.BeginStep(action.Verb.ToString(), action.Args, SampleLiveState());
            _driver.Apply(action, actions.Skip(index + 1).ToList());
            capture.CompleteStep(SampleLiveState());
        }

        if (capture.State == FightCaptureState.Live)
        {
            throw new EngineException(
                "The recording's actions ran out before its fight ended, so there is no completed fight to " +
                "capture. The supported unit is a whole fight.");
        }

        return capture;
    }

    /// <summary>Which progress model this run was generated against, named so a
    /// report can say it rather than imply a reading of somebody's profile.</summary>
    public string ProgressOrigin => LocalEnvironment.OriginOf(_progress);

    /// <summary>
    /// The decisions a player can make while a fight is live.
    ///
    /// The guard this serves is what catches a run that entered a fight the recording
    /// did not. It used to be "in a fight at all", which was right while the only
    /// boundary was the first fight's; a journey to a later one walks through earlier
    /// fights on purpose, so the question is whether the decision in front of the run
    /// is one a live fight accepts.
    /// </summary>
    private static readonly ActionVerb[] AllowedWhileFighting =
    [
        ActionVerb.PlayCard,
        ActionVerb.EndTurn,
        ActionVerb.UsePotion,
        ActionVerb.DiscardPotion,
        ActionVerb.SelectCardFromScreen,
        ActionVerb.SelectHandCards,
    ];

    private bool InCombat() =>
        LiveState().Fields.GetValueOrDefault("combat.in_progress") == "true";

    private static string Refusal(string headline, PreflightResult result, string actualPhrase) =>
        headline + "\n" + string.Join("\n", result.Fields
            .Where(field => !field.Matches)
            .Select(field =>
                $"  - {field.Field}: the recording needs '{field.Expected}', {actualPhrase} " +
                $"'{field.Actual}'. {field.Diagnostic}"));

    public void Dispose()
    {
        Capture?.Abandon();
        _driver.Dispose();
    }
}

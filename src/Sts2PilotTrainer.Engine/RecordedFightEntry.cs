using System.Globalization;
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
/// recording's, and the recording requires the complete unlock state its content
/// came from. Nothing here writes to a profile, and the run is set up with saving
/// off; see <c>GameSession.PrepareRunInRunningGame</c> and docs/environment-identity.md.
/// </summary>
public sealed class RecordedFightEntry : IDisposable
{
    private readonly GameSession _session;
    private readonly RunDriver _driver;
    private readonly PlayerProgress _progress;

    private RecordedFightEntry(
        ReplayManifest manifest, RecordedFightPlan plan, GameSession session, PlayerProgress progress)
    {
        Manifest = manifest;
        Plan = plan;
        _session = session;
        _progress = progress;
        _driver = new RunDriver(session);
    }

    public ReplayManifest Manifest { get; }

    /// <summary>The recording's decisions before its fight, and the boundary.</summary>
    public RecordedFightPlan Plan { get; }

    /// <summary>How many of the plan's steps have been executed.</summary>
    public int StepsTaken { get; private set; }

    /// <summary>The decision the recording made next, or null once they are all
    /// made.</summary>
    public ActionRecord? NextStep =>
        StepsTaken < Plan.PrefightActions.Count ? Plan.PrefightActions[StepsTaken] : null;

    /// <summary>Whether every recorded decision has been made and the fight should
    /// now be live. Whether it actually is, is <see cref="VerifyCombatStart"/>'s
    /// question.</summary>
    public bool AtCombatStart => StepsTaken == Plan.PrefightActions.Count;

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
    /// actually be generated against. That is what separates this from the
    /// eligibility screen's own verdict, which reads the player's profile: a player
    /// starting this run by hand would need the unlocks and the ascension ceiling
    /// themselves, and nobody starts it by hand.
    /// </summary>
    public static bool CanConstruct(
        EnvironmentIdentity expected, out PreflightResult gate,
        PlayerProgress progress = PlayerProgress.AllUnlocked)
    {
        gate = Preflight.Evaluate(expected, progress);
        return gate.Matches && LocalEnvironment.ReadStartedRun() is null;
    }

    /// <summary>
    /// Builds the recording's run in this headless process and enters its first
    /// room, ready for the first recorded decision.
    /// </summary>
    public static RecordedFightEntry StartHeadless(
        ReplayManifest manifest, PlayerProgress progress = PlayerProgress.AllUnlocked)
    {
        var entry = Prepare(manifest, progress, session => session.StartRun(
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
    public static RecordedFightEntry PrepareInRunningGame(
        ReplayManifest manifest, PlayerProgress progress = PlayerProgress.AllUnlocked) =>
        Prepare(manifest, progress, session => session.PrepareRunInRunningGame(
            manifest.Environment.Seed.Value,
            manifest.Environment.Character.Value,
            manifest.Environment.Ascension.Value,
            manifest.Environment.GameMode.Value,
            manifest.Environment.Acts.Value,
            progress));

    private static RecordedFightEntry Prepare(
        ReplayManifest manifest, PlayerProgress progress, Action<GameSession> construct)
    {
        var validation = ManifestValidator.Validate(manifest);
        if (!validation.IsValid)
        {
            throw new ManifestException("Manifest is not valid:\n" + validation.Describe());
        }

        var plan = RecordedFightPlan.For(manifest);

        // The prerequisites, asked of the progress model this run will actually be
        // generated against. The same question the arbiter asks before it constructs
        // a run, and the same rules; what differs between a host and a person is
        // which reading the rules are asked about, not the rules.
        var prerequisites = Preflight.Evaluate(manifest.Environment, progress);
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

        return new RecordedFightEntry(manifest, plan, session, progress);
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
            "Every decision the recording made before its fight has already been made. There is nothing " +
            "further to execute before the fight starts.");

        var wasInCombat = InCombat();
        if (wasInCombat)
        {
            throw new EngineException(
                $"The run is already in a fight with {Plan.PrefightActions.Count - StepsTaken} recorded " +
                "decision(s) still unmade, so this is not the recording's fight. Refusing to keep going.");
        }

        _driver.Apply(action, []);
        StepsTaken++;
    }

    /// <summary>Makes every remaining recorded decision, in order. The same steps,
    /// without stopping between them.</summary>
    public void AdvanceToCombatStart()
    {
        while (!AtCombatStart) AdvanceOneStep();
    }

    /// <summary>
    /// What the recording's next decision is, in the terms a screen needs to say it.
    ///
    /// Read from the run the decision is about to act on, never from a table: the
    /// relic an opening blessing grants is the event's own answer, and what kind of
    /// node a move enters is the generated map's. A host that wrote either down would
    /// be a host that had learned one recording by heart.
    /// </summary>
    public PrefightChoice DescribeNextStep()
    {
        var action = NextStep ?? throw new EngineException(
            "Every decision the recording made before its fight has already been made; there is no next one " +
            "to describe.");

        return action.Verb switch
        {
            ActionVerb.ChooseNeowBlessing => new PrefightChoice.Blessing(action.Seq, BlessingRelic(action)),
            ActionVerb.MapMove => DescribeMapMove(action),
            _ => throw new EngineException(
                $"Action {action.Seq} is a '{action.Verb}', which this trainer cannot show the recording " +
                "making. Only an opening blessing and a map move are supported before a fight."),
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

    /// <summary>The live run's canonical state, as the arbiter reads it.</summary>
    public CanonicalState LiveState() => CanonicalStateProjection.Project(_session.RunState);

    /// <summary>
    /// Whether the fight now live is the fight the recording starts.
    ///
    /// Asked after the last recorded decision and before a player is given the
    /// controls. It refuses two different ways of being wrong: the run is not in a
    /// fight at all, or it is in one that does not match what the recording observed
    /// and cached. Both are drift, and neither is entered.
    /// </summary>
    /// <param name="expectedDigest">
    /// The cached combat-start snapshot's digest, when the caller has one. The
    /// recording's observed fields are compared either way; the digest is what also
    /// compares the state no video can show.
    /// </param>
    public CombatStartEquality VerifyCombatStart(string? expectedDigest = null)
    {
        if (!AtCombatStart)
        {
            throw new EngineException(
                $"{Plan.PrefightActions.Count - StepsTaken} of the recording's decisions before the fight " +
                "have not been made yet, so there is no combat start to compare against.");
        }

        var state = LiveState();
        if (!InCombat())
        {
            return new CombatStartEquality
            {
                Matches = false,
                Comparisons = [],
                ExpectedDigest = expectedDigest,
                ActualDigest = state.Digest(),
                Refusal =
                    "Every decision the recording made before its fight has been made and this run is not in a " +
                    "fight. The recording enters one at this point, so the run this game generated is not the " +
                    "recording's, and there is nothing here to hand over.",
            };
        }

        return CombatStartEquality.Compare(Plan.Boundary, state.Fields, state.Digest(), expectedDigest);
    }

    /// <summary>Which progress model this run was generated against, named so a
    /// report can say it rather than imply a reading of somebody's profile.</summary>
    public string ProgressOrigin => LocalEnvironment.OriginOf(_progress);

    private bool InCombat() =>
        LiveState().Fields.GetValueOrDefault("combat.in_progress") == "true";

    private static string Refusal(string headline, PreflightResult result, string actualPhrase) =>
        headline + "\n" + string.Join("\n", result.Fields
            .Where(field => !field.Matches)
            .Select(field =>
                $"  - {field.Field}: the recording needs '{field.Expected}', {actualPhrase} " +
                $"'{field.Actual}'. {field.Diagnostic}"));

    public void Dispose() => _driver.Dispose();
}

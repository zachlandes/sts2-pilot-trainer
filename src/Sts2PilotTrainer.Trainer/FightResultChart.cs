using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// The post-fight chart: what each turn cost either side, turn by turn, for both
/// lines at once.
///
/// This is the required interface <c>docs/comparison-direction.md</c> first wrote
/// down and kept the data for - a turn-indexed reading of enemy health lost and
/// player health lost for the player's line and the recording's, with the potions
/// each side used marked at the turn they were used. It is presentation, so it
/// lives here rather than in the comparison contract:
/// <see cref="CombatComparison"/> states differences and commits to no shape for
/// them, and a chart baked into it would be an interface decision nothing could
/// revisit.
///
/// It derives and never infers. Every number is a value the comparison already
/// carries; a turn one side never reached is absent on that side
/// (<see cref="FightResultPoint.EnemyHealthLost"/> and
/// <see cref="FightResultPoint.HealthLost"/> are null) rather than drawn as a zero,
/// because a zero there would be a claim that the side fought that turn and lost
/// nothing. Nothing here scores, ranks, or says which line was better.
/// </summary>
public sealed record FightResultChart(
    /// <summary>What the chart is called, over it.</summary>
    string Heading,
    /// <summary>The x axis: the turn.</summary>
    string TurnLabel,
    /// <summary>The upper plot: enemy health lost, both lines.</summary>
    string EnemyMeasureLabel,
    /// <summary>The lower plot: the fighter's own health lost, both lines.</summary>
    string PlayerMeasureLabel,
    /// <summary>Every turn either side reached, in order. The chart's x axis.</summary>
    IReadOnlyList<int> Turns,
    /// <summary>The player's line.</summary>
    FightResultSeries Yours,
    /// <summary>The recording's line.</summary>
    FightResultSeries Theirs,
    /// <summary>The largest single value any point carries, which both measures and
    /// both lines are drawn against. One scale rather than four: two plots with
    /// different scales would make an eight look like a thirty.</summary>
    int Ceiling)
{
    /// <summary>A chart with nothing to draw. What a screen carrying a notice rather
    /// than a comparison has.</summary>
    public static FightResultChart Empty { get; } = new(
        TrainerCopy.ChartHeading,
        TrainerCopy.TurnLabel,
        TrainerCopy.EnemyMeasureLabel,
        TrainerCopy.PlayerMeasureLabel,
        [],
        new FightResultSeries(string.Empty, IsPlayer: true, []),
        new FightResultSeries(string.Empty, IsPlayer: false, []),
        0);

    /// <summary>Whether there is a fight to draw at all.</summary>
    public bool HasTurns => Turns.Count > 0;

    /// <summary>
    /// Reads the chart out of a comparison.
    /// </summary>
    /// <param name="creator">Whose recording the second line is, from the manifest.</param>
    /// <param name="comparison">The player's line on the left, the recording's on the right.</param>
    public static FightResultChart From(string creator, CombatComparison comparison)
    {
        var turns = comparison.Turns.Select(turn => turn.Turn).ToList();
        var yours = new FightResultSeries(
            TrainerCopy.YouColumn, IsPlayer: true, comparison.Turns.Select(Point(turn => turn.Left)).ToList());
        var theirs = new FightResultSeries(
            creator, IsPlayer: false, comparison.Turns.Select(Point(turn => turn.Right)).ToList());

        return new FightResultChart(
            TrainerCopy.ChartHeading,
            TrainerCopy.TurnLabel,
            TrainerCopy.EnemyMeasureLabel,
            TrainerCopy.PlayerMeasureLabel,
            turns,
            yours,
            theirs,
            Ceiling: yours.Points.Concat(theirs.Points).SelectMany(Values).DefaultIfEmpty(0).Max());
    }

    private static Func<ComparedTurn, FightResultPoint> Point(Func<ComparedTurn, CombatTurn?> side) =>
        compared => side(compared) is { } turn
            ? new FightResultPoint(compared.Turn, turn.EnemyHealthLost, turn.HealthLost, turn.ConsumablesUsed)
            // Absent rather than zero: this side's fight was already over, and a point
            // on the axis would say it fought the turn and lost nothing.
            : new FightResultPoint(compared.Turn, null, null, []);

    private static IEnumerable<int> Values(FightResultPoint point)
    {
        if (point.EnemyHealthLost is { } enemy) yield return enemy;
        if (point.HealthLost is { } health) yield return health;
    }
}

/// <summary>
/// One line on the chart, point by point.
///
/// <see cref="IsPlayer"/> is what keeps the two lines apart everywhere they are
/// drawn. Which line is which is a fact about the comparison's two sides, decided
/// once here rather than by whichever renderer happens to be drawing them.
/// </summary>
public sealed record FightResultSeries(
    string Label,
    bool IsPlayer,
    /// <summary>One point per turn of <see cref="FightResultChart.Turns"/>, in the
    /// same order. A turn this side never reached is present and empty.</summary>
    IReadOnlyList<FightResultPoint> Points);

/// <summary>
/// One turn of one line: what it took off the enemy, what it cost, and the potions
/// it spent.
///
/// Both measurements are null together, and only when this side's fight was already
/// over by this turn. Everything else is a number the trace sampled either side of
/// an action.
/// </summary>
public sealed record FightResultPoint(
    int Turn,
    /// <summary>Enemy health that actually came off this turn, or null where this
    /// side did not reach the turn. Damage a block absorbed is not counted.</summary>
    int? EnemyHealthLost,
    /// <summary>Health that actually came off this side this turn, or null where this
    /// side did not reach the turn.</summary>
    int? HealthLost,
    /// <summary>The potions spent on this turn, as stable model ids. The id is what a
    /// renderer looks artwork up by; no art is named here.</summary>
    IReadOnlyList<string> PotionModelIds)
{
    /// <summary>Whether this side fought this turn at all.</summary>
    public bool Reached => EnemyHealthLost is not null;
}

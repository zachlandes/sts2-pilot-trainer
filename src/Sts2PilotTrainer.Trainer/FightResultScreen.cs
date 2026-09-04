using System.Globalization;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// What the player reads once their fight has ended: their line beside the
/// recording's, or the one sentence that says why there is no comparison.
///
/// Computed from a <see cref="CombatComparison"/> and nothing else. The two
/// projections stay apart here as they do in the contract - the summary figures
/// first, then the turn chronology and the chart of the same turns - and every
/// number is the comparison's. Nothing is scored, ranked or judged: a figure whose
/// two sides differ is a figure whose two sides differ, and the notes say so in the
/// approved words.
///
/// It is a model of pictures rather than of sentences: the cards each turn, the
/// potions and the two lines of the chart are model ids and numerals that a renderer
/// draws. What text remains is furniture and the two caveats, each a rule rather
/// than a caption. Every sentence is a template over the creator's name and the
/// comparison's values, and nothing here names a recording.
/// </summary>
public sealed record FightResultScreen(
    string Title,
    /// <summary>Why the two lines can be set beside each other at all.</summary>
    string SameBoundaryNote,
    /// <summary>The player's column header, then the recording's.</summary>
    IReadOnlyList<string> Columns,
    /// <summary>The summary, one row per compared field, in the contract's order.</summary>
    IReadOnlyList<FightResultRow> Rows,
    string TurnDetailHeading,
    /// <summary>One entry per turn either side reached, in turn order, with each
    /// side's cards, potions and health-loss numerals.</summary>
    IReadOnlyList<FightResultTurn> Turns,
    /// <summary>What stands where a side has no turn to draw.</summary>
    string FightOverLabel,
    /// <summary>The same turns as a chart: both lines' enemy health lost and player
    /// health lost against the turn, with the potions marked where they were
    /// spent.</summary>
    FightResultChart Chart,
    IReadOnlyList<string> Notes,
    /// <summary>The one sentence shown instead of the rows when there is no
    /// comparison, or empty when there is one.</summary>
    string Notice,
    string DoneButton)
{
    /// <summary>Whether this screen carries a comparison, as opposed to a notice.</summary>
    public bool HasComparison => Rows.Count > 0;

    /// <summary>
    /// The player's completed fight beside the recording's.
    /// </summary>
    /// <param name="creator">Whose recording it is, from the manifest.</param>
    /// <param name="comparison">The player's line on the left, the recording's on the right.</param>
    public static FightResultScreen For(string creator, CombatComparison comparison)
    {
        var rows = new List<FightResultRow>();
        foreach (var field in comparison.Summary)
        {
            rows.Add(new FightResultRow(
                Label(field.Field), Display(field.Field, field.Left), Display(field.Field, field.Right), field.Matches));
        }

        var turns = comparison.Turns.Select(turn => new FightResultTurn(
            turn.Turn,
            turn.Left is { } yours ? FightResultTurnSide.Of(yours) : null,
            turn.Right is { } theirs ? FightResultTurnSide.Of(theirs) : null)).ToList();

        return new FightResultScreen(
            Title: TrainerCopy.ComparisonTitle(creator),
            SameBoundaryNote: TrainerCopy.SameBoundaryNote,
            Columns: [TrainerCopy.YouColumn, creator],
            Rows: rows,
            TurnDetailHeading: TrainerCopy.TurnDetailHeading,
            Turns: turns,
            FightOverLabel: TrainerCopy.FightOverLabel,
            Chart: FightResultChart.From(creator, comparison),
            Notes: [TrainerCopy.NoVerdictNote, TrainerCopy.BlockNote],
            Notice: string.Empty,
            DoneButton: TrainerCopy.DoneButton);
    }

    /// <summary>
    /// What to show for a capture, whatever state it ended in.
    ///
    /// One place decides, so the four outcomes cannot drift apart: a fight left
    /// before it ended, a capture that could not be completed, a fight that was not
    /// won, and a completed win compared with the recording's. A comparison that
    /// refuses - a boundary that is not the recording's - is shown in its own words.
    /// </summary>
    public static FightResultScreen Of(string creator, FightCapture capture, CombatProjection recording)
    {
        switch (capture.State)
        {
            case FightCaptureState.Abandoned:
                return Left();
            case FightCaptureState.Incomplete:
                return Refused(capture.Refusal ?? string.Empty);
        }

        try
        {
            var yours = capture.Project();
            if (!string.Equals(yours.Summary.Outcome, "victory", StringComparison.Ordinal))
            {
                return Lost(creator);
            }

            return For(creator, CombatComparison.Between(yours, recording));
        }
        catch (ManifestException refusal)
        {
            return Refused(refusal.Message);
        }
    }

    /// <summary>The player did not win. The recording's line was a won fight, and a
    /// lost one has no completed line to set beside it.</summary>
    public static FightResultScreen Lost(string creator) => NoticeOf(TrainerCopy.LostNote(creator));

    /// <summary>The fight was left before it ended.</summary>
    public static FightResultScreen Left() => NoticeOf(TrainerCopy.LeftNote);

    /// <summary>The capture, the projection or the comparison refused, in its own
    /// words. Shown verbatim: each of those already explains itself, and a second
    /// account of the same refusal would be a sentence nobody approved.</summary>
    public static FightResultScreen Refused(string refusal) => NoticeOf(refusal);

    private static FightResultScreen NoticeOf(string sentence) => new(
        Title: TrainerCopy.Name,
        SameBoundaryNote: string.Empty,
        Columns: [],
        Rows: [],
        TurnDetailHeading: string.Empty,
        Turns: [],
        FightOverLabel: string.Empty,
        Chart: FightResultChart.Empty,
        Notes: [],
        Notice: sentence,
        DoneButton: TrainerCopy.DoneButton);

    private static string Label(string field) => field switch
    {
        "outcome" => TrainerCopy.OutcomeRow,
        "total_turns" => TrainerCopy.TurnsRow,
        "starting_health" => TrainerCopy.StartingHealthRow,
        "final_health" => TrainerCopy.FinalHealthRow,
        "net_health_change" => TrainerCopy.NetHealthChangeRow,
        "consumables_used" => TrainerCopy.PotionsUsedRow,
        "cards_removed" => TrainerCopy.CardsRemovedRow,
        _ => throw new ManifestException(
            $"The comparison carries a summary field '{field}' this screen has no row for. A row it invented " +
            "would be a label nobody approved."),
    };

    private static string Display(string field, string value) => field switch
    {
        "outcome" => value switch
        {
            "victory" => TrainerCopy.WonOutcome,
            "defeat" => TrainerCopy.LostOutcome,
            "ended" => TrainerCopy.EndedOutcome,
            _ => throw new ManifestException(
                $"The comparison reports an outcome '{value}' this screen has no word for."),
        },
        "net_health_change" => Signed(value),
        "consumables_used" or "cards_removed" => value.Length == 0
            ? TrainerCopy.None
            : string.Join(", ", value.Split('|').Select(ModelIdNames.Display)),
        _ => value,
    };

    private static string Signed(string value)
    {
        var number = int.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return number > 0
            ? "+" + number.ToString(CultureInfo.InvariantCulture)
            : number.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>One summary row: the label, the player's value, the recording's, and
/// whether they agree. Agreement is a fact about two values, not a verdict.</summary>
public sealed record FightResultRow(string Label, string Yours, string Theirs, bool Matches);

/// <summary>
/// One turn as the result draws it: the turn's number, and each side's cards,
/// potions and health-loss numerals. A side that did not reach the turn is null,
/// which is the difference rather than a row of zeroes.
/// </summary>
public sealed record FightResultTurn(int Turn, FightResultTurnSide? Yours, FightResultTurnSide? Theirs);

/// <summary>
/// What one line did on one turn.
///
/// The cards are the model ids of the cards played, in the order they were played,
/// which is what a renderer looks card art up by. The potions are the potions that
/// left a slot that turn without being discarded, from the same reading the summary's
/// potion row uses.
/// </summary>
public sealed record FightResultTurnSide(
    IReadOnlyList<string> CardModelIds,
    IReadOnlyList<string> PotionModelIds,
    int EnemyHealthLost,
    int HealthLost)
{
    /// <summary>
    /// Reads one side's turn out of the comparison's turn detail.
    /// </summary>
    /// <exception cref="ManifestException">When a card play carries no card id. The
    /// icon for it would have to be invented, and a card nobody can name is not a
    /// card this screen will draw a blank for.</exception>
    public static FightResultTurnSide Of(CombatTurn turn) => new(
        turn.Actions
            .Where(action => string.Equals(action.Verb, nameof(ActionVerb.PlayCard), StringComparison.Ordinal))
            .Select(CardModelId)
            .ToList(),
        turn.ConsumablesUsed,
        turn.EnemyHealthLost,
        turn.HealthLost);

    private static string CardModelId(TurnAction action) =>
        action.Args.TryGetValue("card_id", out var card) && card.Length > 0
            ? card
            : throw new ManifestException(
                $"Step {action.Seq} plays a card and carries no 'card_id', so the card cannot be named. " +
                "Refusing rather than drawing an icon for a card nobody can identify.");
}

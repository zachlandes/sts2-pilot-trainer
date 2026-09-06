using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>What one row of the post-fight choice does. Named rather than matched on
/// its label, so the host never reads a sentence to know what a press means, and so
/// a row that is absent on this outcome cannot be reached by counting.</summary>
public enum PostFightAction
{
    /// <summary>Draws the comparison panel. Only looks.</summary>
    ShowTheComparison,

    /// <summary>Runs the recording's fight through the engine on the transport. Moves
    /// a run, the recording's.</summary>
    WatchTheirFight,

    /// <summary>Back to the proven combat start, the way the chip's jump to the
    /// beginning already goes.</summary>
    FightItAgain,

    /// <summary>The run goes on and the hold is the player's.</summary>
    ContinueAsYou,

    /// <summary>Ends it: the run is discarded and the fight is left.</summary>
    Leave,
}

/// <summary>
/// Everything the post-fight choice is derived from.
/// </summary>
/// <param name="Won">Whether the player's fight was won. It decides one row's
/// presence and where the menu hangs; it is not a verdict.</param>
/// <param name="ComparisonShown">Whether the comparison has been shown for this
/// fight this sitting. Draws the dot on that row and gates nothing.</param>
/// <param name="FightWatched">Whether the recording's fight has been watched for
/// this fight this sitting. The same dot, on the other reveal row.</param>
/// <param name="CanWatch">Whether this build can run the recording's fight through
/// the engine inside the client. False until in-combat playback exists, and the
/// row is then absent rather than refused.</param>
/// <param name="CanContinueAsYou">Whether this build can hand the run to the player
/// after their fight. False until run-level continuation exists; absent likewise.</param>
public sealed record PostFightFacts(
    bool Won,
    bool ComparisonShown,
    bool FightWatched,
    bool CanWatch,
    bool CanContinueAsYou);

/// <summary>One row of the choice: what pressing it does, and the row as the menu
/// draws it.</summary>
public sealed record PostFightRow(PostFightAction Action, MenuRow Row);

/// <summary>
/// The choice a player is offered once their fight has ended, in place of a result
/// drawn unbidden.
///
/// One rule carries it: the mod never volunteers the recording's answer, and never
/// withholds it from somebody who asks. So the fight ends, the chip stays, and after
/// the same two seconds the game takes to draw its own ending a menu hangs under it
/// in the order of the teaching - look, then act. Nothing about the recording's line
/// is on screen until a row is pressed.
///
/// Rows are present or absent, never disabled. A refused menu row says nothing on
/// this surface, and a row for something this build cannot do would say less than
/// that. The count of rows therefore differs by outcome and by what the build can
/// do; the thing that never changes is the tag's own controls, and this menu is not
/// one of them.
///
/// The session state it reads is a dot and nothing more: a row already taken this
/// sitting carries the teal dot the speed menu uses for "the one you are in", and
/// every row stays offered whether or not it has been taken. Pure, and derived from
/// <see cref="PostFightFacts"/> alone, so every column of the table can be asserted
/// without a game.
/// </summary>
public sealed record PostFightChoice(IReadOnlyList<PostFightRow> Rows)
{
    /// <summary>The rows as the menu draws them, in order.</summary>
    public IReadOnlyList<MenuRow> Menu => [.. Rows.Select(row => row.Row)];

    /// <summary>What the row at this position does.</summary>
    /// <exception cref="ManifestException">When there is no such row. A press on a
    /// row that is not offered is refused rather than mapped onto a neighbour.</exception>
    public PostFightAction ActionAt(int index) =>
        index >= 0 && index < Rows.Count
            ? Rows[index].Action
            : throw new ManifestException(
                $"The post-fight choice offers {Rows.Count} row(s), so there is no row {index} to press.");

    /// <summary>
    /// The choice for one finished fight.
    /// </summary>
    /// <param name="creator">Whose recording it is, from the manifest. The one row
    /// that names anybody names them.</param>
    public static PostFightChoice For(string creator, PostFightFacts facts)
    {
        var rows = new List<PostFightRow>
        {
            // Hollow, because it only looks: the reveal glyph is the one shape this
            // surface adds to the family, and it obeys the family's rule.
            new(PostFightAction.ShowTheComparison, new MenuRow(
                TransportGlyph.Reveal, TrainerCopy.ShowTheComparison, IsCurrent: facts.ComparisonShown)),
        };

        if (facts.CanWatch)
        {
            rows.Add(new PostFightRow(PostFightAction.WatchTheirFight, new MenuRow(
                TransportGlyph.Play, TrainerCopy.WatchTheirFight(creator), IsCurrent: facts.FightWatched)));
        }

        rows.Add(new PostFightRow(
            PostFightAction.FightItAgain, new MenuRow(TransportGlyph.Again, TrainerCopy.FightItAgain)));

        // Absent on a loss whatever the build can do: there is no run to go on with.
        if (facts.CanContinueAsYou && facts.Won)
        {
            rows.Add(new PostFightRow(
                PostFightAction.ContinueAsYou, new MenuRow(TransportGlyph.Continue, TrainerCopy.ContinueAsYou)));
        }

        // No glyph: leaving is the game's own back language, not a transport verb.
        rows.Add(new PostFightRow(PostFightAction.Leave, new MenuRow(null, TrainerCopy.Leave)));
        return new PostFightChoice(rows);
    }
}

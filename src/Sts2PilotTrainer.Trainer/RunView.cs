using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// One place in a run the strip can select: a floor, what it held, and what the
/// recording proves about it.
/// </summary>
/// <param name="Fight">The ordinal of the fight this floor's combat starts, or null
/// when the recording proves none there. Read by the entry rather than shown: nothing
/// on this surface refers to a fight by number.</param>
/// <param name="Kind">What the floor held, derived from the recording's own decisions.
/// It is the second line of the play-from row and the strip's own glyph.</param>
/// <param name="Unfinished">Whether the recording fought on this floor and stops
/// before the fight ends. Derived rather than assumed: the recording's own combat
/// actions say a fight happened and the absence of a combat-start boundary says it
/// never finished, which is the difference between "there was no fight here" and
/// "there is no finished line to compare yours against".</param>
/// <param name="IsRunStart">Whether this is where the run begins. It is a place to
/// stand and it is the same place "Start the run over" already puts a player, which
/// is why the play-from row is refused rather than offered twice.</param>
/// <param name="AfterSeq">The action the recording's own boundary for this floor
/// follows, or -1 for the floor the run started on, which no boundary names. What the
/// recording says at this place is read at that action.</param>
public sealed record RunViewPosition(
    int Floor, int? Fight, FloorKind Kind, bool Unfinished, bool IsRunStart, int AfterSeq)
{
    /// <summary>
    /// Whether the play-from row will stand a player here.
    ///
    /// The rule in one place, read by the row and by every strip that draws this
    /// position - the run view's own and the browser pane's - so a cell drawn as
    /// playable is a cell the row offers. The run's own start is refused because
    /// "Start the run over" already puts a player there, and an unfinished fight
    /// because there is no finished recorded line to set one against.
    /// </summary>
    public bool Playable => !IsRunStart && !Unfinished;
}

/// <summary>
/// One cell of the run strip: a floor of the run, and what is true of it for this
/// player.
/// </summary>
/// <param name="Played">Whether this player has stood in this floor's fight. The
/// strip's tick, and the one thing on it that is about the person rather than the
/// run.</param>
/// <param name="Selected">Whether this is the position the view is showing. The
/// strip's ring.</param>
public sealed record RunStripCell(
    int Floor, FloorKind Kind, bool Played, bool Selected, bool Playable);

/// <summary>Which offer a row is. Named rather than matched on its label, so the
/// drawing never has to read a sentence to know what pressing it does.</summary>
public enum RunViewRowKind
{
    /// <summary>The one play-from row. Where it stands a player is the selected floor's
    /// own kind: a combat floor's fight start, and any other floor's entry.</summary>
    PlayFrom,

    Continue,
    StartOver,
}

/// <summary>
/// One row of the run view: what it offers, where it stands a player, and - when it
/// is refused - the reason, in place of the second line.
/// </summary>
/// <param name="Note">The row's second line. On the play-from row it is dynamic and
/// names the selected floor and its kind; on Continue it is the floor the next
/// unplayed fight is on; Start the run over has none, because a second line there said
/// nothing its label did not.</param>
/// <param name="Enemy">Which enemy the fight on this row's floor is against, as the
/// enemy's own model id, or null where the recording names none. The fight pane names
/// it; no row's label does.</param>
/// <param name="Held">What the row's floor held, so the drawing can put that kind's
/// mark beside the row. <see cref="FloorKind.Unknown"/> on a row that is not about one
/// floor - Start the run over is the only one - and on a floor nothing established.</param>
/// <param name="ShownThisSitting">Whether the recording's line for this row's fight
/// has been shown this sitting - the comparison drawn, or the attempt finished by
/// name. Draws the hollow-eye mark beside the row and gates nothing: a rehearsal of
/// a line just seen is a teaching move of its own, and the mark is there so the
/// player knows which kind of attempt this is.</param>
public sealed record RunViewRow(
    RunViewRowKind Kind,
    string Label,
    string? Note,
    bool Enabled,
    string? Reason = null,
    int? Fight = null,
    int? Floor = null,
    string? Enemy = null,
    FloorKind Held = FloorKind.Unknown,
    bool ShownThisSitting = false);

/// <summary>
/// One run, opened: the strip of every floor it reached, what the recording says at
/// the selected one, and the ways in.
///
/// Reached one way. The browser's pane has an "Open the run" ribbon in either tab and
/// that is the only thing that opens this; run history's plate goes straight to a
/// floor, and its "Choose another floor" row drills in here about the run it was
/// already showing. Back returns to whichever of those opened it, with the selection
/// kept.
///
/// It computes nothing about the run and judges nothing about how it was played.
/// Which places exist is <see cref="ReplayManifest.Boundaries"/>' answer - the same
/// list <c>RecordedFightEntry</c> walks to and the same list the validator enforces -
/// so a row here can never offer a boundary the entry would refuse. Which of them this
/// player has stood in is <see cref="RunProgress"/>' answer, and the only thing on this
/// screen that is about the person rather than the run.
///
/// <para><b>One play-from row, not two.</b> A fight is one thing a floor can hold
/// rather than a thing beside it, so the label is fixed and the second line names the
/// selected floor and its kind. On a combat floor the entry is the fight's start; on
/// any other floor it is the floor's own entry, from where the run is the player's.
/// </para>
///
/// <para><b>No fight is named by number anywhere on this surface.</b> No player has
/// that concept, the strip enumerates floors, and the game's own screens count floors.
/// A fight ordinal is carried on a row for the entry to use and is never drawn.</para>
/// </summary>
public sealed record RunView(
    string RunId,
    IReadOnlyList<RunViewPosition> Positions,
    RunViewPosition? Selected,
    IReadOnlyList<RunStripCell> Strip,
    IReadOnlyList<RunViewRow> Rows,
    IReadOnlyList<RunRelic> Relics,
    RunReading Reading,
    string FloorNote,
    string? NotSaved)
{
    /// <summary>The deck at the selected position's start, as the tiles the pane draws,
    /// or null where the recording says nothing there. A gap, never a zero.</summary>
    public IReadOnlyList<DeckTile>? Deck => Reading.Deck;

    /// <summary>How many cards that deck holds, beside the relics at the top right.</summary>
    public int? DeckCount => Reading.DeckCount;

    /// <summary>
    /// The run view for one recording, with one floor selected.
    ///
    /// <paramref name="selectedFloor"/> null selects the first place the strip offers,
    /// which is where a player who has just opened the run is standing.
    /// </summary>
    /// <param name="shownThisSitting">The fights of this recording whose line has been
    /// shown this sitting. Held in memory by whoever draws the comparison and never
    /// written, so a later launch passes nothing and every fight is cold again.</param>
    public static RunView For(
        ReplayManifest recording, RunProgress progress, int? selectedFloor = null,
        IReadOnlyCollection<int>? shownThisSitting = null)
    {
        var positions = PositionsIn(recording);
        var selected = selectedFloor is { } floor
            ? positions.FirstOrDefault(position => position.Floor == floor)
            : positions.FirstOrDefault();
        var fights = LibraryRun.ProvedFights(recording);
        var played = progress.PlayedFrom(recording.RunId).Where(fights.Contains).ToList();

        // Read once and handed to the rows, so the pane and the play-from row's own
        // enemy cannot be two readings of the same place.
        var reading = selected is null
            ? RunReading.Nothing
            : RunReading.At(recording, selected.AfterSeq);
        var rows = RowsFor(recording, progress, positions, selected, fights, reading, shownThisSitting ?? []);

        return new RunView(
            recording.RunId,
            positions,
            selected,
            [
                .. positions.Select(position => new RunStripCell(
                    position.Floor,
                    position.Kind,
                    position.Fight is { } fight && played.Contains(fight),
                    selected is not null && position.Floor == selected.Floor,
                    position.Playable)),
            ],
            rows,
            RunReading.RelicsFound(recording),
            reading,
            LibraryCopy.FloorIsYours,
            // Said once, beside the rows, and only where one of them would actually
            // stand a player somewhere. A screen whose rows are all refused has nothing
            // to warn anybody about.
            rows.Any(row => row.Enabled) ? LibraryCopy.NotSaved : null);
    }

    /// <summary>
    /// Every floor of this recording, and what it proves about each.
    ///
    /// The run's first floor is here and is not a boundary: a run is not "arrived at"
    /// where it begins, so the recording proves no floor entry for it, and leaving it
    /// out would leave the strip starting at floor two. It is derived from the first
    /// floor the recording did arrive at rather than written down as 1, so a recording
    /// that starts somewhere else says so.
    /// </summary>
    public static IReadOnlyList<RunViewPosition> PositionsIn(ReplayManifest recording)
    {
        var floorEntries = recording.Boundaries
            .Where(boundary => boundary.IsFloorEntry && boundary.Floor is not null)
            .GroupBy(boundary => boundary.Floor!.Value)
            .Select(group => (Floor: group.Key, AfterSeq: group.Min(boundary => boundary.AfterSeq)))
            .OrderBy(entry => entry.Floor)
            .ToList();

        var start = floorEntries.Count > 0 ? Math.Max(1, floorEntries[0].Floor - 1) : 1;
        var floors = new List<(int Floor, int AfterSeq)>();
        if (floorEntries.All(entry => entry.Floor != start)) floors.Add((start, -1));
        floors.AddRange(floorEntries);

        return
        [
            .. floors.Select((entry, index) =>
            {
                var until = index + 1 < floors.Count ? floors[index + 1].AfterSeq : int.MaxValue;
                var fight = recording.Boundaries
                    .Where(boundary =>
                        boundary.IsCombatStart &&
                        boundary.AfterSeq >= entry.AfterSeq && boundary.AfterSeq < until)
                    .Select(boundary => boundary.Fight)
                    .OfType<int>()
                    .Order()
                    .Cast<int?>()
                    .FirstOrDefault();
                var fought = FoughtBetween(recording, entry.AfterSeq, until);
                return new RunViewPosition(
                    entry.Floor,
                    fight,
                    // A floor whose fight the recording never finished is still a combat
                    // floor: what it held is one question and whether it can be played
                    // from is another, and collapsing them would leave the row's second
                    // line naming no kind at all.
                    fight is not null || fought
                        ? FloorKind.Combat
                        : FloorKinds.Between(recording, entry.AfterSeq, until),
                    fight is null && fought,
                    index == 0,
                    entry.AfterSeq);
            }),
        ];
    }

    /// <summary>
    /// Whether the recording fought between two points in its own history without ever
    /// reaching a boundary there.
    ///
    /// The window is the floor's own, half-open at its far end: a floor entry and the
    /// combat it opens carry the same <c>after_seq</c>, so a floor holds what happens
    /// from its own entry up to the next floor's.
    ///
    /// The combat verbs are <c>RecordedFightPlan</c>'s list rather than a second one:
    /// which actions happen inside a fight is one question with one owner, and a copy
    /// of it here would be a copy to keep in step.
    /// </summary>
    private static bool FoughtBetween(ReplayManifest recording, int afterSeq, int until) =>
        recording.Actions.Any(action =>
            action.Seq >= afterSeq && action.Seq < until &&
            RecordedFightPlan.IsCombatVerb(action.Verb));

    /// <summary>
    /// The rows, in the design's own order: the play-from row, Continue, and Start the
    /// run over.
    ///
    /// The play-from row is always drawn, refused where the selected position is not a
    /// place to stand, because its position is how a player learns where the offer
    /// lives and it moves with the strip's selection. Continue and Start the run over
    /// are drawn only where the recording proves the boundary behind them: a boundary
    /// <c>RunCoverage.Boundaries()</c> does not contain is not drawn at all, so a
    /// truncated recording offers fewer rows rather than a row that refuses.
    /// </summary>
    private static IReadOnlyList<RunViewRow> RowsFor(
        ReplayManifest recording, RunProgress progress, IReadOnlyList<RunViewPosition> positions,
        RunViewPosition? selected, IReadOnlyList<int> fights, RunReading reading,
        IReadOnlyCollection<int> shownThisSitting)
    {
        var rows = new List<RunViewRow> { PlayFromRow(selected, reading, shownThisSitting) };

        if (progress.ContinueAt(recording.RunId, fights) is { } next)
        {
            var at = positions.FirstOrDefault(position => position.Fight == next);
            rows.Add(new RunViewRow(
                RunViewRowKind.Continue,
                LibraryCopy.ContinueFromNextUnplayed,
                at is null ? null : LibraryCopy.FloorLine(at.Floor),
                Enabled: true,
                Fight: next,
                Floor: at?.Floor,
                Enemy: at is null ? null : RunReading.At(recording, at.AfterSeq).Enemies.FirstOrDefault()?.Model,
                Held: at?.Kind ?? FloorKind.Unknown));
        }

        // Absent for the same reason Continue is: it walks to fight 1's combat start,
        // and a recording that proves no fight 1 has nowhere for it to come to rest.
        if (fights.Contains(1))
        {
            var start = positions.FirstOrDefault(position => position.Fight == 1);
            rows.Add(new RunViewRow(
                RunViewRowKind.StartOver, LibraryCopy.StartTheRunOver, null,
                Enabled: true, Fight: 1, Floor: start?.Floor));
        }

        return rows;
    }

    /// <summary>
    /// The one play-from row, over the four cases the design gives it.
    ///
    /// The label never changes; the second line does, and it is what tells a player
    /// where pressing goes. Two of the four cases are refusals and the second line is
    /// then the reason, because a row that gave both would be saying where it goes and
    /// that it does not go there.
    /// </summary>
    private static RunViewRow PlayFromRow(
        RunViewPosition? selected, RunReading reading, IReadOnlyCollection<int> shownThisSitting)
    {
        if (selected is null)
        {
            return new RunViewRow(
                RunViewRowKind.PlayFrom, LibraryCopy.PlayFromThisFloor, null, Enabled: false,
                Reason: LibraryCopy.RunStartsHere);
        }

        if (selected.IsRunStart)
        {
            return new RunViewRow(
                RunViewRowKind.PlayFrom, LibraryCopy.PlayFromThisFloor, null, Enabled: false,
                Reason: LibraryCopy.RunStartsHere, Floor: selected.Floor);
        }

        if (selected.Unfinished)
        {
            return new RunViewRow(
                RunViewRowKind.PlayFrom, LibraryCopy.PlayFromThisFloor, null, Enabled: false,
                Reason: LibraryCopy.FightNotFinished, Floor: selected.Floor);
        }

        return new RunViewRow(
            RunViewRowKind.PlayFrom,
            LibraryCopy.PlayFromThisFloor,
            LibraryCopy.FloorLine(selected.Floor, selected.Kind),
            Enabled: true,
            Fight: selected.Fight,
            Floor: selected.Floor,
            Enemy: reading.Enemies.FirstOrDefault()?.Model,
            Held: selected.Kind,
            ShownThisSitting: selected.Fight is { } fight && shownThisSitting.Contains(fight));
    }
}

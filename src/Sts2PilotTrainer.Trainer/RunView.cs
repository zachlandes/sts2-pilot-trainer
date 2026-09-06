using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// One place in a run the strip can select: a floor, and what the recording proves
/// about the fight on it.
/// </summary>
/// <param name="Fight">The ordinal of the fight this floor's combat starts, or null
/// when the recording proves none there.</param>
/// <param name="Unfinished">Whether the recording fought on this floor and stops
/// before the fight ends. Derived rather than assumed: the recording's own combat
/// actions say a fight happened and the absence of a combat-start boundary says it
/// never finished, which is the difference between "there was no fight here" and
/// "there is no finished line to compare yours against".</param>
/// <param name="IsRunStart">Whether this is where the run begins. It is a place to
/// stand and it is the same place "Start the run over" already puts a player, which
/// is why the floor row is refused rather than offered twice.</param>
public sealed record RunViewPosition(int Floor, int? Fight, bool Unfinished, bool IsRunStart);

/// <summary>Which offer a row is. Named rather than matched on its label, so the
/// drawing never has to read a sentence to know what pressing it does.</summary>
public enum RunViewRowKind
{
    PlayFromFight,
    PlayFromFloor,
    Continue,
    StartOver,
}

/// <summary>
/// One row of the run view: what it offers, where it stands a player, and - when it
/// is refused - the reason, on the row.
///
/// A refused row keeps its place. The affordance's position is how a player learns it
/// exists, and a row that vanished on some floors and reappeared on others would read
/// as the screen changing shape rather than as this floor having no fight.
/// </summary>
/// <param name="ShownThisSitting">Whether the recording's line for this row's fight
/// has been shown this sitting - the comparison drawn, or the attempt finished by
/// name. Draws the hollow-eye mark beside the row and gates nothing: a rehearsal of
/// a line just seen is a teaching move of its own, and the mark is there so the
/// player knows which kind of attempt this is.</param>
public sealed record RunViewRow(
    RunViewRowKind Kind,
    string Label,
    string Note,
    bool Enabled,
    string? Reason = null,
    int? Fight = null,
    int? Floor = null,
    bool ShownThisSitting = false);

/// <summary>
/// One run, opened: every place the recording proves a player can be stood, and the
/// ways in.
///
/// <see cref="FightsPlayed"/> is counted against the ordinals the recording proves,
/// the way <see cref="LibraryRun.PlayedCount"/> is, so the row in the list and this
/// screen never print two numbers for the same fact.
///
/// It computes nothing about the run and judges nothing about how it was played.
/// Which places exist is <see cref="ReplayManifest.Boundaries"/>' answer - the same
/// list <c>RecordedFightEntry</c> walks to and the same list the validator enforces -
/// so a row here can never offer a boundary the entry would refuse. Which fights this
/// player has already played from is <see cref="RunProgress"/>' answer, and the only
/// thing on this screen that is about the person rather than the run.
///
/// <para>Every way in is the one entering verb. "Play from this fight", "Play from
/// this floor", "Continue: play from fight N" and "Start the run over" are four
/// boundaries of the same journey, not four features.</para>
/// </summary>
public sealed record RunView(
    string RunId,
    IReadOnlyList<RunViewPosition> Positions,
    RunViewPosition? Selected,
    IReadOnlyList<RunViewRow> Rows,
    IReadOnlyList<int> FightsPlayed,
    int FightCount,
    string FloorNote)
{
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

        return new RunView(
            recording.RunId,
            positions,
            selected,
            RowsFor(recording, progress, selected, fights, shownThisSitting ?? []),
            played,
            fights.Count,
            LibraryCopy.FloorIsYours);
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
                return new RunViewPosition(
                    entry.Floor,
                    fight,
                    fight is null && FoughtBetween(recording, entry.AfterSeq, until),
                    index == 0);
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

    private static IReadOnlyList<RunViewRow> RowsFor(
        ReplayManifest recording, RunProgress progress, RunViewPosition? selected,
        IReadOnlyList<int> fights, IReadOnlyCollection<int> shownThisSitting)
    {
        var rows = new List<RunViewRow>
        {
            FightRow(selected, shownThisSitting),
            FloorRow(selected),
        };

        // Absent rather than refused when the recording has nothing left to continue
        // to. There is no boundary behind it, and a row offering a fight the recording
        // does not have would be an offer nothing could honour.
        if (progress.ContinueAt(recording.RunId, fights) is { } next)
        {
            rows.Add(new RunViewRow(
                RunViewRowKind.Continue,
                LibraryCopy.ContinueAtFight(next),
                LibraryCopy.ContinueNote,
                Enabled: true,
                Fight: next));
        }

        // Absent for the same reason Continue is: it walks to fight 1's combat start,
        // and a recording that proves no fight 1 has nowhere for it to come to rest.
        if (fights.Contains(1))
        {
            rows.Add(new RunViewRow(
                RunViewRowKind.StartOver,
                LibraryCopy.StartTheRunOver,
                LibraryCopy.StartTheRunOverNote,
                Enabled: true,
                Fight: 1));
        }

        return rows;
    }

    private static RunViewRow FightRow(RunViewPosition? selected, IReadOnlyCollection<int> shownThisSitting) =>
        selected?.Fight is { } fight
            ? new RunViewRow(
                RunViewRowKind.PlayFromFight,
                LibraryCopy.PlayFromThisFight,
                LibraryCopy.PlayFromThisFightNote,
                Enabled: true,
                Fight: fight,
                Floor: selected.Floor,
                ShownThisSitting: shownThisSitting.Contains(fight))
            : new RunViewRow(
                RunViewRowKind.PlayFromFight,
                LibraryCopy.PlayFromThisFight,
                LibraryCopy.PlayFromThisFightNote,
                Enabled: false,
                Reason: selected?.Unfinished == true
                    ? LibraryCopy.FightNotFinished
                    : LibraryCopy.NoFightOnThisFloor,
                Floor: selected?.Floor);

    private static RunViewRow FloorRow(RunViewPosition? selected) =>
        selected is { IsRunStart: false }
            ? new RunViewRow(
                RunViewRowKind.PlayFromFloor,
                LibraryCopy.PlayFromThisFloor,
                LibraryCopy.PlayFromThisFloorNote,
                Enabled: true,
                Floor: selected.Floor)
            : new RunViewRow(
                RunViewRowKind.PlayFromFloor,
                LibraryCopy.PlayFromThisFloor,
                LibraryCopy.PlayFromThisFloorNote,
                Enabled: false,
                Reason: LibraryCopy.RunStartsHere,
                Floor: selected?.Floor);
}

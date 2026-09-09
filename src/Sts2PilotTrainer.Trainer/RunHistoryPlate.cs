namespace Sts2PilotTrainer.Trainer;

/// <summary>The mark at the head of the plate, where it has a head at all. Drawn,
/// never written: a state's name is a heading and the mark is what a player picks out
/// at a glance.</summary>
public enum PlateMark
{
    /// <summary>Something about the recording stops it being used.</summary>
    Warning,

    /// <summary>The same, in the eligibility screen's red: a recording this build is
    /// not the one for.</summary>
    OtherVersion,
}

/// <summary>Which offer a plate row is. The plate has three and gains none: the way
/// straight in, the way to the rest of the run, and the way out to the submit flow.</summary>
public enum PlateRowKind
{
    /// <summary>Stands the player at the run's last floor. Named with its kind, the
    /// way the run view's own row names a floor.</summary>
    PlayFromFloor,

    /// <summary>Opens this run in the run view, a deeper screen about the same run, so
    /// the game's own disclosure chevron is its glyph.</summary>
    ChooseAnotherFloor,

    Submit,
}

/// <summary>One row of the run-history plate.</summary>
public sealed record PlateRow(
    PlateRowKind Kind, string Label, bool Enabled, int? Floor = null, FloorKind FloorKind = FloorKind.Unknown);

/// <summary>
/// What the recorder knows about the run the run-history screen is showing.
///
/// Every field is a reading somebody took, and the type says so by having no
/// defaults: a plate that reported "continuous" because nothing had said otherwise
/// would be making an integrity claim it never established, which is the failure
/// <c>AGENTS.md</c> names by name.
/// </summary>
/// <param name="HasRecording">Whether a recording of this run exists at all. The plate
/// is absent when it does not - not empty, absent.</param>
/// <param name="Continuous">Whether the recorder watched the whole run. False is the
/// recording saying so about itself.</param>
/// <param name="ConsoleUsed">Whether a console command was used during the run, or
/// null when nothing established it. Null is not "no": it gates the submit row alone,
/// and a plate claiming a clean run it never checked would be evidence nobody can
/// check.</param>
/// <param name="RecordedBuild">The build the recording was made on.</param>
/// <param name="ThisBuild">The build this game is.</param>
/// <param name="RunInProgress">Whether a run is being played right now. Play-from is
/// after the fact, and this is the one state where the plate says so.</param>
/// <param name="SubmitAvailable">Whether there is a submit flow to lead to. Supplied
/// like every other fact here rather than decided inside the derivation, because
/// whether the flow exists is a fact about the build and not about the run - and a
/// host that drew the row refused on its own would put the enabled decision in two
/// owners.</param>
/// <param name="LastFloor">The furthest floor of this run a player can be stood at, or
/// null when there is none. The furthest the row can offer rather than the furthest the
/// run reached: the row is an offer to play from somewhere, and
/// <see cref="RunViewPosition.Playable"/> is the one rule that says where those places
/// are - which excludes the run's own start, a fight the recording stops inside, and a
/// place this client cannot be walked to.</param>
/// <param name="LastFloorKind">What that floor held. The row names it beside the floor
/// number, and <see cref="FloorKind.Unknown"/> leaves it unnamed rather than
/// guessed.</param>
/// <param name="HasOtherFloors">Whether the run holds a floor other than that one. The
/// "Choose another floor" row is absent where there is no other floor to choose, which
/// is the boundaries rule applied to this plate: a row nothing is behind is not
/// drawn.</param>
public sealed record RunHistoryFacts(
    bool HasRecording,
    bool Continuous,
    bool? ConsoleUsed,
    string RecordedBuild,
    string ThisBuild,
    bool RunInProgress,
    bool SubmitAvailable,
    int? LastFloor,
    FloorKind LastFloorKind,
    bool HasOtherFloors);

/// <summary>
/// The plate under the game's own run-history pane: what this run's recording is, and
/// where it will stand you.
///
/// <para><b>No head line in the ordinary state.</b> The history row's own record mark
/// already says the run is recorded, and a head line repeating it would be the plate
/// introducing itself. A head exists only where there is a status to state, and it is
/// then in the eligibility screen's language.</para>
///
/// <para><b>The sentence about a played-from run not being saved is said once, at the
/// press.</b> The shipped trainer already says it beside its Enter button, and a head
/// line saying it here would be warning a player about something they have not asked
/// for yet.</para>
///
/// <para><b>Rows name floors, never fights.</b> No player has the fight-number concept,
/// and the game's own screens count floors.</para>
///
/// <para><b>The derivation is total.</b> Every combination of facts has an answer,
/// including the absent one, and there is no other way to build a plate - which is the
/// same rule <see cref="PlaybackTransport"/> holds for the transport, adopted here for
/// the same reason it was adopted there.</para>

/// </summary>
/// <param name="Mark">The head's mark, or null in the ordinary state, which has no
/// head.</param>
/// <param name="Head">The head line, or null in the ordinary state.</param>
/// <param name="NotSaved">Said once, beside the rows, where one of them would actually
/// stand a player somewhere - the way the shipped trainer says it beside its Enter
/// button. Never a head line, and never on a plate whose rows are all refused, where it
/// would read as the reason they are.</param>
public sealed record RunHistoryPlate(
    PlateMark? Mark,
    string? Head,
    IReadOnlyList<PlateRow> Rows,
    string? Reason,
    string? NotSaved)
{
    /// <summary>
    /// The plate for one run of the player's history, or null when there is nothing to
    /// draw.
    ///
    /// Null exactly when the run has no recording. Every other state is a plate,
    /// because every other state is something a player is owed a sentence about.
    /// </summary>
    public static RunHistoryPlate? For(RunHistoryFacts facts)
    {
        if (!facts.HasRecording) return null;

        if (!facts.Continuous)
        {
            return Refused(
                PlateMark.Warning, LibraryCopy.PlateCantBeReplayed, facts,
                LibraryCopy.PlateContinuityBroken);
        }

        if (!string.Equals(facts.RecordedBuild, facts.ThisBuild, StringComparison.Ordinal))
        {
            // Described, never entered - the same rule the browser follows for a run
            // this game cannot play. Both versions are in the head line, so a player
            // reading it can see which of the two is theirs, and there is no reason
            // line underneath because the head line is the whole reason.
            return Refused(
                PlateMark.OtherVersion,
                LibraryCopy.PlateRecordedOn(facts.RecordedBuild, facts.ThisBuild),
                facts,
                reason: null);
        }

        if (facts.RunInProgress) return Refused(mark: null, head: null, facts, LibraryCopy.PlateDuringARun);

        // A console command changes what the run was, so it stops the run being
        // published and stops nothing else: the fights really were fought and playing
        // from one is still playing from what happened. It is named ahead of the
        // missing flow because it is the more particular thing true of this run - the
        // flow's absence is true of every run on this build.
        var consoleUsed = facts.ConsoleUsed == true;
        var submittable = !consoleUsed && facts.SubmitAvailable;
        return new RunHistoryPlate(
            Mark: null,
            Head: null,
            RowsFor(facts, floorEnabled: facts.LastFloor is not null, submitEnabled: submittable),
            consoleUsed ? LibraryCopy.PlateConsoleUsed
                : submittable ? null : LibraryCopy.PlateSubmitComing,
            facts.LastFloor is not null ? LibraryCopy.NotSaved : null);
    }

    private static RunHistoryPlate Refused(
        PlateMark? mark, string? head, RunHistoryFacts facts, string? reason) =>
        new(mark, head, RowsFor(facts, floorEnabled: false, submitEnabled: false), reason, NotSaved: null);

    /// <summary>
    /// The plate's rows.
    ///
    /// The play-from row keeps its place in every state, refused where the run cannot
    /// be played from, because its position is how a player learns the feature exists
    /// and a plate that collapsed to nothing would teach them it does not. "Choose
    /// another floor" is absent where the run holds no other floor, which is the rule a
    /// boundary nothing proves already follows: a row nothing is behind is not drawn.
    /// </summary>
    private static IReadOnlyList<PlateRow> RowsFor(
        RunHistoryFacts facts, bool floorEnabled, bool submitEnabled)
    {
        var rows = new List<PlateRow> { FloorRow(facts, floorEnabled) };
        if (facts.HasOtherFloors)
        {
            rows.Add(new PlateRow(
                PlateRowKind.ChooseAnotherFloor, LibraryCopy.ChooseAnotherFloor, floorEnabled));
        }

        rows.Add(new PlateRow(PlateRowKind.Submit, LibraryCopy.SubmitThisRun, submitEnabled));
        return rows;
    }

    /// <summary>
    /// The play-from row, named for the floor the run ended at where the recording
    /// proves one and left unnamed where it does not.
    ///
    /// A row cannot say "Play from floor 12" about a recording with no floor 12, so the
    /// label drops the number rather than inventing one, and the row is refused. The
    /// place is still held: what a player learns from the row is that this is where a
    /// run is played from.
    /// </summary>
    private static PlateRow FloorRow(RunHistoryFacts facts, bool enabled) =>
        facts.LastFloor is { } floor
            ? new PlateRow(
                PlateRowKind.PlayFromFloor,
                LibraryCopy.PlayFromFloor(floor, facts.LastFloorKind),
                enabled,
                Floor: floor,
                FloorKind: facts.LastFloorKind)
            : new PlateRow(PlateRowKind.PlayFromFloor, LibraryCopy.PlayFromAFloor, Enabled: false);
}

namespace Sts2PilotTrainer.Trainer;

/// <summary>The mark at the head of the plate. Drawn, never written: a state's name
/// is a heading and the mark is what a player picks out at a glance.</summary>
public enum PlateMark
{
    /// <summary>A recording exists and nothing is wrong with it.</summary>
    Recorded,

    /// <summary>The same, dimmed: a recording this game is not the one for.</summary>
    RecordedMuted,

    /// <summary>Something about the recording stops it being used.</summary>
    Warning,

    /// <summary>A run the recorder was never on.</summary>
    Multiplayer,
}

/// <summary>Which offer a plate row is. The plate has three and gains none: two ways
/// in, and the way out to the submit flow.</summary>
public enum PlateRowKind
{
    PlayFromFight,
    PlayFromFloor,
    Submit,
}

/// <summary>One row of the run-history plate.</summary>
public sealed record PlateRow(PlateRowKind Kind, string Label, bool Enabled, int? Fight = null, int? Floor = null);

/// <summary>
/// What the recorder knows about the run the run-history screen is showing.
///
/// Every field is a reading somebody took, and the type says so by having no
/// defaults: a plate that reported "continuous" or "single-player" because nothing
/// had said otherwise would be making an integrity claim it never established, which
/// is the failure <c>AGENTS.md</c> names by name.
/// </summary>
/// <param name="HasRecording">Whether a recording of this run exists at all. The plate
/// is absent when it does not - not empty, absent.</param>
/// <param name="Multiplayer">Whether the game's own history says more than one player
/// played it. Read from the history rather than from the recording, because there is
/// no recording of a multiplayer run to read.</param>
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
/// <param name="LastFight">The run's own last fight, or null when the recording proves
/// none.</param>
/// <param name="LastFloor">The last floor the run arrived at, or null when the
/// recording proves none.</param>
public sealed record RunHistoryFacts(
    bool HasRecording,
    bool Multiplayer,
    bool Continuous,
    bool? ConsoleUsed,
    string RecordedBuild,
    string ThisBuild,
    bool RunInProgress,
    bool SubmitAvailable,
    int? LastFight,
    int? LastFloor);

/// <summary>
/// The plate under the game's own run-history pane: what this run's recording is, and
/// the two places it will stand you.
///
/// <para><b>Every state keeps every row.</b> A run that cannot be played from is drawn
/// with its rows in place and refused, and one line underneath says why. That is the
/// settled rule and it is not decoration: the affordance's position is how a player
/// learns the feature exists, and a plate that collapsed to nothing on a multiplayer
/// run would teach them it does not.</para>
///
/// <para><b>The derivation is total.</b> Every combination of facts has an answer,
/// including the absent one, and there is no other way to build a plate - which is the
/// same rule <see cref="PlaybackTransport"/> holds for the transport, adopted here for
/// the same reason it was adopted there.</para>
/// </summary>
public sealed record RunHistoryPlate(
    PlateMark Mark,
    string Head,
    IReadOnlyList<PlateRow> Rows,
    string? Reason)
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
        if (facts.Multiplayer)
        {
            // Kept even though the recorder records nothing for a multiplayer run. The
            // history row is the game's, so the affordance's place is held and stated,
            // which is how a player learns when it does work.
            return Refused(
                PlateMark.Multiplayer, LibraryCopy.PlateMultiplayer, facts,
                LibraryCopy.PlateMultiplayerReason);
        }

        if (!facts.HasRecording) return null;

        if (!facts.Continuous)
        {
            return Refused(
                PlateMark.Warning, LibraryCopy.PlateRecordedWithAGap, facts,
                LibraryCopy.PlateContinuityBroken);
        }

        if (!string.Equals(facts.RecordedBuild, facts.ThisBuild, StringComparison.Ordinal))
        {
            return Refused(
                PlateMark.RecordedMuted, LibraryCopy.PlateRecordedOn(facts.RecordedBuild), facts,
                LibraryCopy.PlateOtherBuild(facts.ThisBuild));
        }

        if (facts.RunInProgress)
        {
            return Refused(
                PlateMark.RecordedMuted, LibraryCopy.PlateRecordedPlain, facts,
                LibraryCopy.PlateDuringARun);
        }

        // A console command changes what the run was, so it stops the run being
        // published and stops nothing else: the fights really were fought and playing
        // from one is still playing from what happened. It is named ahead of the
        // missing flow because it is the more particular thing true of this run - the
        // flow's absence is true of every run on this build.
        var consoleUsed = facts.ConsoleUsed == true;
        var submittable = !consoleUsed && facts.SubmitAvailable;
        return new RunHistoryPlate(
            PlateMark.Recorded,
            LibraryCopy.PlateRecorded,
            [
                FightRow(facts, enabled: facts.LastFight is not null),
                FloorRow(facts, enabled: facts.LastFloor is not null),
                new PlateRow(PlateRowKind.Submit, LibraryCopy.SubmitThisRun, submittable),
            ],
            consoleUsed ? LibraryCopy.PlateConsoleUsed
                : submittable ? null : LibraryCopy.PlateSubmitComing);
    }

    private static RunHistoryPlate Refused(
        PlateMark mark, string head, RunHistoryFacts facts, string reason) =>
        new(
            mark,
            head,
            [
                FightRow(facts, enabled: false),
                FloorRow(facts, enabled: false),
                new PlateRow(PlateRowKind.Submit, LibraryCopy.SubmitThisRun, Enabled: false),
            ],
            reason);

    /// <summary>
    /// The fight row, named for the run's own last fight where the recording proves
    /// one and left unnamed where it does not.
    ///
    /// A row cannot say "play from fight 4" about a recording with no fight 4, so the
    /// label drops the number rather than inventing one, and the row is refused. The
    /// place is still held: what a player learns from the row is that this is where
    /// fights are entered from.
    /// </summary>
    private static PlateRow FightRow(RunHistoryFacts facts, bool enabled) =>
        facts.LastFight is { } fight
            ? new PlateRow(PlateRowKind.PlayFromFight, LibraryCopy.PlayFromFight(fight), enabled, Fight: fight)
            : new PlateRow(PlateRowKind.PlayFromFight, LibraryCopy.PlayFromAFight, Enabled: false);

    /// <inheritdoc cref="FightRow"/>
    private static PlateRow FloorRow(RunHistoryFacts facts, bool enabled) =>
        facts.LastFloor is { } floor
            ? new PlateRow(PlateRowKind.PlayFromFloor, LibraryCopy.PlayFromFloor(floor), enabled, Floor: floor)
            : new PlateRow(PlateRowKind.PlayFromFloor, LibraryCopy.PlayFromAFloor, Enabled: false);
}

using System.Text.Json.Serialization;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>Where a run in the library came from. It decides which list it is in and
/// which heading it sits under, and nothing else - no row is drawn differently for
/// it, and no control names it.</summary>
public enum RunOrigin
{
    /// <summary>Travels inside the mod. Present with no network and no index.</summary>
    Included,

    /// <summary>Curated, in the curator's own order.</summary>
    Featured,

    /// <summary>Everything else the index holds.</summary>
    Recent,

    /// <summary>Written by the recorder, on this computer.</summary>
    Mine,
}

/// <summary>
/// Whether a run reproduces on the build being asked about.
///
/// Four answers rather than two, because "nobody has checked", "somebody checked and
/// it did not reproduce" and "this game could not be read to check" are different
/// facts and a surface that collapsed them would be claiming a check it never saw.
/// Every answer but a pass is hidden from the list, and each of the three can change
/// on its own terms - a verdict arriving, the game matching again, the reading
/// succeeding.
/// </summary>
public enum RunVerdict
{
    /// <summary>No verdict exists for this build.</summary>
    Absent,

    /// <summary>A verdict exists for this build and the run did not reproduce.</summary>
    Failed,

    /// <summary>Recorded on this build, and this game could not be read to judge it.
    /// Not the same as no verdict existing: nothing was asked of an index, a reading
    /// this game takes for itself failed.</summary>
    Unjudged,

    /// <summary>A verdict exists for this build and the run reproduced.</summary>
    Passed,
}

/// <summary>
/// One run as the library's surfaces read it: what the row says, and the one fact
/// that decides whether it is in the list at all.
///
/// It is a reading of a recording plus what only a host can supply - which list the
/// run is in, what this build's verdict on it is, and which floor of it this player
/// last loaded. Nothing here is computed from a recording twice: the fights and floors
/// come from the boundaries the recording actually proves, which is the same list the
/// run view's rows come from.
///
/// <para><see cref="Verdict"/> is three-valued past a pass on purpose: a run this
/// game could not be read to judge is not a run that failed, and a surface reporting
/// one as the other would be claiming a check it never saw.</para>
/// </summary>
/// <param name="Recorded">When the run was played, for ordering and for the row's
/// clock. Null when whoever built this could not read it, in which case the run sorts
/// after everything that carries one rather than being given a made-up time.</param>
/// <param name="Relics">Every relic the run carried, in the order it found them. The
/// strip on the row shows the first few by rarity and the pane shows them all.</param>
/// <param name="DeckCount">How many cards the run's deck held where it ended, or null
/// where the recording says nothing about it.</param>
/// <param name="LastFloorReplayed">The last floor of this run this player loaded, or
/// null when they have loaded none. Blank until there is one; saving a run does not
/// set it.</param>
/// <param name="Positions">Every floor the run reached and what it held, which is what
/// the pane's run strip is drawn from. The same list the run view opens on, so a strip
/// in the pane and a strip in the run view are one reading.</param>
/// <param name="Deck">The deck where the run ended, as the pane's card tiles, or null
/// where the recording says nothing about it. A gap, never a zero.</param>
[method: JsonConstructor]
public sealed record LibraryRun(
    string RunId,
    RunOrigin Origin,
    string? Creator,
    string Character,
    int Ascension,
    string RecordedBuild,
    IReadOnlyList<int> Fights,
    IReadOnlyList<int> Floors,
    string? Outcome,
    RunVerdict Verdict,
    IReadOnlyList<RunRelic> Relics,
    int? DeckCount,
    int? LastFloorReplayed,
    IReadOnlyList<RunViewPosition> Positions,
    IReadOnlyList<DeckTile>? Deck,
    DateTimeOffset? Recorded = null)
{
    /// <summary>Whether the recording established that this was multiplayer.</summary>
    public bool? Multiplayer { get; init; }

    /// <summary>The last act the recording reached, in player-facing numbering.</summary>
    public int? ActReached { get; init; }

    /// <summary>The fight ordinals this player has already played from.</summary>
    public IReadOnlyList<int> FightsPlayed { get; init; } = [];

    /// <summary>The sharing service's identity for this submission.</summary>
    [JsonIgnore]
    public string? ShareId { get; init; }

    /// <summary>The sharing service's short lookup code for this submission.</summary>
    [JsonIgnore]
    public string? ShareCode { get; init; }

    /// <summary>The row identity. Separate submissions of one recording remain separate rows.</summary>
    [JsonIgnore]
    public string EntryId => ShareId ?? RunId;

    /// <summary>Compatibility constructor for callers that have not read pane details.</summary>
    public LibraryRun(
        string runId,
        RunOrigin origin,
        string? creator,
        string character,
        int ascension,
        string recordedBuild,
        IReadOnlyList<int> fights,
        string? outcome,
        bool? multiplayer,
        RunVerdict verdict,
        IReadOnlyList<int> fightsPlayed,
        DateTimeOffset? recorded = null,
        string? ShareId = null,
        string? ShareCode = null)
        : this(
            runId, origin, creator, character, ascension, recordedBuild, fights, [], outcome,
            verdict, [], null, null, [], null, recorded)
    {
        Multiplayer = multiplayer;
        FightsPlayed = fightsPlayed;
        this.ShareId = ShareId;
        this.ShareCode = ShareCode;
    }

    /// <summary>The outcome a recording of a won run carries.</summary>
    public const string WonOutcome = "won";

    /// <summary>Whether the run was won, as the row's crown reads it. False for a run
    /// whose recording says nothing about how it ended.</summary>
    public bool Won => string.Equals(Outcome, WonOutcome, StringComparison.Ordinal);

    /// <summary>
    /// Whether this run is in the list.
    ///
    /// The settled hidden rule, in one place. A run this build has no passing verdict
    /// for is not listed, with no tickbox and no greyed row. A run code still finds
    /// one, which is what <see cref="RunBrowser.Lookup"/> is for.
    /// </summary>
    public bool Listed => Verdict == RunVerdict.Passed && Multiplayer != true;

    /// <summary>The last floor the run itself reached, or null where the recording
    /// proves none. The row's own reach, as distinct from how far this player has
    /// replayed.</summary>
    public int? LastFloor => Floors.Count == 0 ? null : Floors[^1];

    /// <summary>
    /// One run, read out of its recording.
    ///
    /// The fights and floors come from <see cref="ReplayManifest.Boundaries"/> rather
    /// than from the action list, because a boundary is what a player can actually be
    /// stood at: a fight the recording stops inside is a fight that happened and is not
    /// a place to stand.
    /// </summary>
    public static LibraryRun From(
        ReplayManifest recording,
        RunOrigin origin,
        RunVerdict verdict,
        int? lastFloorReplayed = null,
        DateTimeOffset? recorded = null)
    {
        var floors = ProvedFloors(recording);
        var last = RunReading.Latest(recording);

        return new LibraryRun(
            recording.RunId,
            origin,
            RecordingIdentity.CreatorOrNull(recording),
            recording.Environment.Character.Value,
            recording.Environment.Ascension.Value,
            recording.Environment.BuildVersion.Value,
            ProvedFights(recording),
            floors,
            recording.Source.Native?.Outcome,
            verdict,
            RunReading.RelicsFound(recording),
            last.DeckCount,
            lastFloorReplayed,
            RunView.PositionsIn(recording),
            last.Deck,
            recorded)
        {
            ActReached = RunReading.ActReached(recording),
        };
    }

    /// <summary>Compatibility reader for callers carrying fight progress and session kind.</summary>
    public static LibraryRun From(
        ReplayManifest recording,
        RunOrigin origin,
        RunVerdict verdict,
        IReadOnlyList<int> fightsPlayed,
        bool? multiplayer = null,
        DateTimeOffset? recorded = null) =>
        From(recording, origin, verdict, LastReplayedFloor(recording, fightsPlayed), recorded) with
        {
            Multiplayer = multiplayer,
            FightsPlayed = fightsPlayed,
        };

    private static int? LastReplayedFloor(
        ReplayManifest recording, IReadOnlyList<int> fightsPlayed) =>
        RunView.PositionsIn(recording)
            .Where(position => position.Fight is { } fight && fightsPlayed.Contains(fight))
            .Select(position => (int?)position.Floor)
            .LastOrDefault();

    /// <summary>Every fight of this recording a player could be stood at the start
    /// of.</summary>
    public static IReadOnlyList<int> ProvedFights(ReplayManifest recording) =>
    [
        .. recording.Boundaries
            .Where(boundary =>
                string.Equals(boundary.Kind, ReplayBoundary.CombatStartKind, StringComparison.Ordinal))
            .Select(boundary => boundary.Fight)
            .OfType<int>()
            .Distinct()
            .Order(),
    ];

    /// <summary>Every floor of this recording a player could be stood at the entry
    /// of.</summary>
    public static IReadOnlyList<int> ProvedFloors(ReplayManifest recording) =>
    [
        .. recording.Boundaries
            .Where(boundary =>
                string.Equals(boundary.Kind, ReplayBoundary.FloorEntryKind, StringComparison.Ordinal))
            .Select(boundary => boundary.Floor)
            .OfType<int>()
            .Distinct()
            .Order(),
    ];
}

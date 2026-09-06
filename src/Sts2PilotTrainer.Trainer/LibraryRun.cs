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
/// One run as the library's surfaces read it: what the row says, and the two facts
/// that decide whether it is in the list at all.
///
/// It is a reading of a recording plus what only a host can supply - which list the
/// run is in, what this build's verdict on it is, and which of its fights this player
/// has played from. Nothing here is computed from a recording twice: the fights come
/// from the boundaries the recording actually proves, which is the same list the run
/// view's rows come from.
///
/// <para><b>Two fields are three-valued on purpose.</b> <see cref="Multiplayer"/> is
/// null when the recording does not say, because a recording written before the
/// recorder could tell has no answer and a host reporting "single-player" it never
/// established would be exactly the claim <c>AGENTS.md</c> forbids. The hidden rule
/// hides an established multiplayer run and no other; it never hides a run for a
/// question nobody asked. <see cref="Verdict"/> carries its own third answer for the
/// same reason.</para>
/// </summary>
/// <param name="Recorded">When the run was played, for ordering. Null when whoever
/// built this could not read it, in which case the run sorts after everything that
/// carries one rather than being given a made-up time.</param>
public sealed record LibraryRun(
    string RunId,
    RunOrigin Origin,
    string? Creator,
    string Character,
    int Ascension,
    string RecordedBuild,
    IReadOnlyList<int> Fights,
    string? Outcome,
    bool? Multiplayer,
    RunVerdict Verdict,
    IReadOnlyList<int> FightsPlayed,
    DateTimeOffset? Recorded = null)
{
    /// <summary>The outcome a recording of a won run carries.</summary>
    public const string WonOutcome = "won";

    /// <summary>Whether the run was won, as the row's crown reads it. False for a run
    /// whose recording says nothing about how it ended.</summary>
    public bool Won => string.Equals(Outcome, WonOutcome, StringComparison.Ordinal);

    /// <summary>
    /// Whether this run is in the list.
    ///
    /// The settled hidden rule, in one place. A run this build has no passing verdict
    /// for is not listed, and an established multiplayer run is not listed - with no
    /// tickbox and no greyed row either way. A run code still finds both, which is what
    /// <see cref="RunBrowser.Lookup"/> is for.
    /// </summary>
    public bool Listed => Verdict == RunVerdict.Passed && Multiplayer != true;

    /// <summary>How many fights this run's recording proves a player can be stood at
    /// the start of. The denominator under the row.</summary>
    public int FightCount => Fights.Count;

    /// <summary>
    /// How many of this run's fights this player has already played from. The pips
    /// under the fight count.
    ///
    /// Counted against the ordinals the recording proves rather than against how many
    /// there are: a fight the recording stopped inside spends an ordinal and proves no
    /// boundary, so the proved set can have a hole in it and a range test would put a
    /// pip under a fight nothing offers.
    /// </summary>
    public int PlayedCount => FightsPlayed.Count(Fights.Contains);

    /// <summary>
    /// One run, read out of its recording.
    ///
    /// The fights come from <see cref="ReplayManifest.Boundaries"/> rather than from
    /// the action list, because a boundary is what a player can actually be stood at:
    /// a fight the recording stops inside is a fight that happened and is not a place
    /// to stand, and counting it would put a pip under a row nothing offers.
    /// </summary>
    public static LibraryRun From(
        ReplayManifest recording,
        RunOrigin origin,
        RunVerdict verdict,
        IReadOnlyList<int>? fightsPlayed = null,
        bool? multiplayer = null,
        DateTimeOffset? recorded = null) =>
        new(
            recording.RunId,
            origin,
            RecordingIdentity.CreatorOrNull(recording),
            recording.Environment.Character.Value,
            recording.Environment.Ascension.Value,
            recording.Environment.BuildVersion.Value,
            ProvedFights(recording),
            recording.Source.Native?.Outcome,
            multiplayer,
            verdict,
            fightsPlayed ?? [],
            recorded);

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

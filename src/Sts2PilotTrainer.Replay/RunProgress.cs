using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// How far a player has got through each recording they have played from: which
/// fights they have stood in, the last floor of it they loaded, and nothing else.
///
/// It exists because three things on the library's surface need it and none can be
/// derived from a recording - the strip's ticks in the run view, the floor in
/// "Continue from the next unplayed fight", and the browser's Last floor replayed
/// column. All three are facts about the person in front of the game rather than about
/// the run, so they live beside the recordings rather than in one.
///
/// <para><b>Progress, never resumable state.</b> A fight ordinal and a floor number are
/// the whole of what is stored. There is no snapshot here, no deck, no seed position, no partial run -
/// storing one would be the cache <c>docs/native-replay-format.md</c> refuses, and a
/// player who resumed from it would be resuming a run nobody re-derived. Standing in
/// a fight is always <c>RecordedFightEntry</c> replaying the recording's own decisions
/// to that boundary; this file only says which ones have been reached before.</para>
///
/// <para>It carries a schema string and refuses an unrecognised one, like every other
/// file under the store. The direction it fails in is the opposite of the settings
/// file's: a progress record this build cannot read is a record it forgets rather than
/// one it guesses at, because the cost is a blank column and a Continue row pointing
/// at the first fight, and the cost of guessing is a surface that lies about what they
/// have played. The one shape it does read back is <see cref="SchemaV1"/>, which
/// carries the same fight ordinals and no floors.</para>
/// </summary>
public sealed record RunProgress
{
    public const string Schema = "sts2-pilot-trainer/run-progress/v2";

    /// <summary>
    /// The shape before the last floor loaded was recorded.
    ///
    /// Read rather than refused, and this is the one direction that is worth the
    /// exception: a v1 file's fight ordinals mean exactly what they mean now, so
    /// refusing it would throw away a player's real progress to avoid inventing a
    /// floor - and no floor is invented, because a v1 file simply names none and the
    /// column is blank until they load one. It is written back as v2.
    /// </summary>
    public const string SchemaV1 = "sts2-pilot-trainer/run-progress/v1";

    /// <summary>What this file is called under the store. Held here rather than in the
    /// mod because the shape and its name are one contract.</summary>
    public const string FileName = "progress.json";

    [JsonPropertyName("schema")]
    public required string SchemaId { get; init; }

    /// <summary>
    /// The fights played from, per recording, keyed by the recording's own run id.
    ///
    /// Ordinals rather than boundaries: a fight's ordinal is what the run view's rows
    /// and the recording's own <c>combat_start</c> boundaries are both numbered by, so
    /// one number means the same thing on both sides. A run nobody has played from has
    /// no entry rather than an empty list.
    /// </summary>
    [JsonPropertyName("fights_played")]
    public IReadOnlyDictionary<string, IReadOnlyList<int>> FightsPlayed { get; init; } =
        new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);

    /// <summary>
    /// The last floor of each recording this player loaded, keyed by run id.
    ///
    /// The floor they were last standing on in that run, whether they played from it
    /// or reached it by playing on - not a place to resume from and not a save. It is
    /// the browser's Last floor replayed column and nothing else reads it, so losing
    /// this file blanks a column and costs nothing that can be replayed.
    ///
    /// <para>Saving a run does not set it, because saving is not loading. A run nobody
    /// has loaded a floor of has no entry rather than a floor of zero.</para>
    /// </summary>
    [JsonPropertyName("last_floor")]
    public IReadOnlyDictionary<string, int> LastFloor { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>What a player who has never played from anything has.</summary>
    public static RunProgress Empty => new() { SchemaId = Schema };

    /// <summary>The fights played from this recording, ascending, or nothing.</summary>
    public IReadOnlyList<int> PlayedFrom(string runId) =>
        FightsPlayed.TryGetValue(runId, out var fights) ? [.. fights.Distinct().Order()] : [];

    /// <summary>Whether this fight of this recording has been played from.</summary>
    public bool HasPlayed(string runId, int fight) => PlayedFrom(runId).Contains(fight);

    /// <summary>
    /// The last floor of this recording the player loaded, or null when they have
    /// loaded none.
    ///
    /// <paramref name="floors"/> is the floors the recording holds, and a floor outside
    /// it is not one of this run's: a record written against a longer recording would
    /// otherwise put a floor in the column that this run does not have. Null rather
    /// than the first floor, because the column is blank until there is one.
    /// </summary>
    public int? LastFloorLoaded(string runId, IReadOnlyList<int> floors) =>
        LastFloor.TryGetValue(runId, out var floor) && floors.Contains(floor) ? floor : null;

    /// <summary>
    /// The fight "Continue" offers, or null when the recording has none left.
    ///
    /// The first fight after the last one played, which is the design's own
    /// definition, and the recording's first fight for one nobody has played from. It
    /// deliberately does not fill gaps: a player who skipped ahead to fight 5 asked to
    /// be at fight 5, and offering them fight 2 next would be the surface disagreeing
    /// with what they did.
    ///
    /// <para><paramref name="fights"/> is the recording's proved fight ordinals rather
    /// than how many of them there are. A fight the recording stopped inside has no
    /// combat-start boundary and still spends an ordinal, so the proved set can have a
    /// hole in it - and counting instead of reading would let this name a fight nothing
    /// proves, which the entry would then refuse. Past the last proved fight there is
    /// nothing to continue to and this answers null.</para>
    ///
    /// <para>Both ends are read through that list. "The last one played" means the last
    /// one played <em>from this run</em>, so an ordinal the recording no longer proves
    /// is not where a player is: a record written against a longer recording would
    /// otherwise anchor this past everything and take the row off the screen.</para>
    /// </summary>
    public int? ContinueAt(string runId, IReadOnlyList<int> fights)
    {
        var played = PlayedFrom(runId).Where(fights.Contains).ToList();
        var after = played.Count == 0 ? 0 : played[^1];
        return fights.Where(fight => fight > after).Cast<int?>().FirstOrDefault();
    }

    /// <summary>
    /// The same record with one more fight played from, or this one when it already
    /// said so.
    ///
    /// Returning a new record rather than mutating is what lets a caller decide
    /// whether the change is worth a write: the store is on a player's disk and a
    /// fight replayed twice should not rewrite it.
    /// </summary>
    public RunProgress WithFightPlayed(string runId, int fight)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new ArgumentException(
                "Progress is recorded against a recording's run id.", nameof(runId));
        }

        if (fight < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(fight), fight, "Fights are numbered from 1, the way a recording's boundaries are.");
        }

        if (HasPlayed(runId, fight)) return this;

        var fights = new Dictionary<string, IReadOnlyList<int>>(FightsPlayed, StringComparer.Ordinal)
        {
            [runId] = [.. PlayedFrom(runId).Append(fight).Order()],
        };
        return this with { FightsPlayed = fights };
    }

    /// <summary>
    /// The same record with the last floor loaded of one recording set, or this one
    /// when it already said so.
    ///
    /// The floor a player was last standing on, so a later load of an earlier floor
    /// replaces a later one: the column says where they were, not how far they ever
    /// got.
    /// </summary>
    public RunProgress WithFloorLoaded(string runId, int floor)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new ArgumentException(
                "Progress is recorded against a recording's run id.", nameof(runId));
        }

        if (floor < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(floor), floor, "Floors are numbered from 1, the way a recording's boundaries are.");
        }

        if (LastFloor.TryGetValue(runId, out var already) && already == floor) return this;

        var floors = new Dictionary<string, int>(LastFloor, StringComparer.Ordinal) { [runId] = floor };
        return this with { LastFloor = floors };
    }

    /// <summary>
    /// Reads a progress record, refusing a schema this build does not know.
    ///
    /// A caller with no file at all passes null and gets <see cref="Empty"/>, which is
    /// the honest answer for a player who has not played from anything yet.
    /// </summary>
    public static RunProgress Read(string? json)
    {
        if (json is null || string.IsNullOrWhiteSpace(json)) return Empty;

        var progress = ManifestJson.DeserializeRequired<RunProgress>(json, "Runmobile progress");
        if (string.Equals(progress.SchemaId, SchemaV1, StringComparison.Ordinal))
        {
            // A v1 file names no floor, so it reads as a player who has loaded none.
            // Nothing is filled in for them: the column is blank until they load one.
            return progress with { SchemaId = Schema, LastFloor = new Dictionary<string, int>(StringComparer.Ordinal) };
        }

        if (!string.Equals(progress.SchemaId, Schema, StringComparison.Ordinal))
        {
            throw new ManifestException(
                $"This progress file declares schema '{progress.SchemaId}', and this build reads " +
                $"'{Schema}' or '{SchemaV1}'.");
        }

        return progress;
    }

    /// <summary>The record as it goes on disk.</summary>
    public string Write() => System.Text.Json.JsonSerializer.Serialize(this, ManifestJson.Options);
}

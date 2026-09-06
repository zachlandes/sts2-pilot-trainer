using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// How far a player has got through each recording they have played from: which
/// fights they have stood in, and nothing else.
///
/// It exists because two things on the library's surface need it and neither can be
/// derived from a recording - the pips under a run's fight count, and the number in
/// "Continue: play from fight N". Both are facts about the person in front of the
/// game rather than about the run, so they live beside the recordings rather than in
/// one.
///
/// <para><b>Progress, never resumable state.</b> A fight ordinal is the whole of what
/// is stored. There is no snapshot here, no deck, no seed position, no partial run -
/// storing one would be the cache <c>docs/native-replay-format.md</c> refuses, and a
/// player who resumed from it would be resuming a run nobody re-derived. Standing in
/// a fight is always <c>RecordedFightEntry</c> replaying the recording's own decisions
/// to that boundary; this file only says which ones have been reached before.</para>
///
/// <para>It carries a schema string and refuses an unrecognised one, like every other
/// file under the store. The direction it fails in is the opposite of the settings
/// file's: a progress record this build cannot read is a record it forgets rather than
/// one it guesses at, because the cost is a player seeing "Continue: play from
/// fight 1" and the cost of guessing is a surface that lies about what they have
/// played.</para>
/// </summary>
public sealed record RunProgress
{
    public const string Schema = "sts2-pilot-trainer/run-progress/v1";

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

    /// <summary>What a player who has never played from anything has.</summary>
    public static RunProgress Empty => new() { SchemaId = Schema };

    /// <summary>The fights played from this recording, ascending, or nothing.</summary>
    public IReadOnlyList<int> PlayedFrom(string runId) =>
        FightsPlayed.TryGetValue(runId, out var fights) ? [.. fights.Distinct().Order()] : [];

    /// <summary>Whether this fight of this recording has been played from.</summary>
    public bool HasPlayed(string runId, int fight) => PlayedFrom(runId).Contains(fight);

    /// <summary>
    /// The fight "Continue" offers, or null when the recording has none left.
    ///
    /// The first fight after the last one played, which is the design's own
    /// definition, and fight 1 for a recording nobody has played from. It deliberately
    /// does not fill gaps: a player who skipped ahead to fight 5 asked to be at
    /// fight 5, and offering them fight 2 next would be the surface disagreeing with
    /// what they did. Past the last fight there is nothing to continue to and this
    /// answers null rather than naming a fight the recording does not have.
    /// </summary>
    public int? ContinueAt(string runId, int fightCount)
    {
        if (fightCount <= 0) return null;
        var played = PlayedFrom(runId);
        var next = played.Count == 0 ? 1 : played[^1] + 1;
        return next <= fightCount ? next : null;
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
    /// Reads a progress record, refusing a schema this build does not know.
    ///
    /// A caller with no file at all passes null and gets <see cref="Empty"/>, which is
    /// the honest answer for a player who has not played from anything yet.
    /// </summary>
    public static RunProgress Read(string? json)
    {
        if (json is null || string.IsNullOrWhiteSpace(json)) return Empty;

        var progress = ManifestJson.DeserializeRequired<RunProgress>(json, "Runmobile progress");
        if (!string.Equals(progress.SchemaId, Schema, StringComparison.Ordinal))
        {
            throw new ManifestException(
                $"This progress file declares schema '{progress.SchemaId}', and this build reads " +
                $"'{Schema}'.");
        }

        return progress;
    }

    /// <summary>The record as it goes on disk.</summary>
    public string Write() => System.Text.Json.JsonSerializer.Serialize(this, ManifestJson.Options);
}

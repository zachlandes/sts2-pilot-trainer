using System.Text.Json.Serialization;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// What verdict this game reached for a recording on a build, remembered so one cheap
/// question can be answered without reading the library.
///
/// It exists for the Compendium card and for nothing else. Whether that button appears
/// is asked on every menu open, and answering it honestly used to mean deserializing
/// every recording on the player's disk and running a preflight over each - fifty files
/// of hundreds of kilobytes, on the game's own thread, precisely in the case where the
/// answer turns out to be no. This is the note that lets that question be answered from
/// a list of run ids and one small file.
///
/// <para><b>It is a hint, and never evidence about a run.</b> Nothing a player is told
/// about whether a run plays may come from here: <c>RunBrowser</c>'s list and the
/// run-code lookup judge live, every time they are opened. The reason is that a verdict
/// is a reading of the whole environment - content hash, mod set, supplied unlocks, act
/// variant - and not of the build alone, so an entry keyed by build is stale the moment
/// a mod is installed. What a stale entry can do is bounded by that: at worst it shows a
/// button onto a list that turns out empty. That is not a claim about a run, and that
/// containment is the only reason this file is allowed to exist.</para>
///
/// <para><b>Unknown means go and look.</b> A run this game has never judged on this
/// build is a reason to show the button, not a reason to hide it: only a remembered
/// <see cref="RunVerdict.Failed"/> or <see cref="RunVerdict.Absent"/> takes a run out of
/// the reckoning, because those are the two answers that say the run is not listed. That
/// direction is chosen deliberately. Hiding on unknown closed a loop with no way out -
/// the cache is written only when the browser is opened, and the browser is reached only
/// through the button, so a player who updated the game past every remembered verdict
/// lost the feature permanently. Erring the other way costs one browser open onto a list
/// that turns out empty, and the same open judges every run and writes the answers, so it
/// corrects itself.</para>
///
/// <para>What that buys is an accelerator rather than a second opinion: where the
/// remembered verdicts are in step with what the preflight would say now, this answers
/// exactly what building the list would have answered. Where they are stale it can be
/// wrong in one direction only, the one above.</para>
///
/// <para><b>An entry is a record that a judgement happened.</b> Only a judgement
/// actually taken is written. Nothing seeds this, nothing backfills it, and a recording
/// the browser did not evaluate has no entry - which is the unknown the rule above sends
/// somebody to look at rather than an answer this file made up.</para>
///
/// <para>It carries a schema string and refuses an unrecognised one, like every other
/// file under the store. A verdict name this build does not know reads as no entry
/// rather than as a refusal, because forgetting a hint costs a menu button and
/// guessing at one costs the rule the button stands for.</para>
/// </summary>
public sealed record RunVerdictCache
{
    public const string Schema = "sts2-pilot-trainer/run-verdicts/v1";

    /// <summary>What this file is called under the store. Held beside the shape,
    /// because the shape and its name are one contract.</summary>
    public const string FileName = "verdicts.json";

    [JsonPropertyName("schema")]
    public required string SchemaId { get; init; }

    /// <summary>
    /// The verdict reached for each recording, keyed by the build it was reached on and
    /// then by the recording's own run id.
    ///
    /// Keyed by build first because a verdict says nothing about any other build, and a
    /// player who moves between builds should not have one build's answers quietly
    /// answering for another's. The verdict is its own name as text, so a value written
    /// by a build that knows more answers than this one reads as no entry.
    /// </summary>
    [JsonPropertyName("verdicts")]
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Verdicts { get; init; } =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

    /// <summary>What a player whose browser has never judged anything has.</summary>
    public static RunVerdictCache Empty => new() { SchemaId = Schema };

    /// <summary>
    /// The verdict remembered for this recording on this build, or null when none was
    /// ever taken - which is not the same as a verdict of <see cref="RunVerdict.Absent"/>
    /// and is why this answers null rather than choosing one.
    /// </summary>
    public RunVerdict? For(string runId, string build) =>
        Verdicts.TryGetValue(build, out var onBuild) &&
        onBuild.TryGetValue(runId, out var verdict) &&
        Enum.TryParse<RunVerdict>(verdict, ignoreCase: false, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// The same record with one build's judgements recorded, or this one when it already
    /// said all of them.
    ///
    /// Returning a new record rather than mutating is what lets a caller decide whether
    /// the change is worth a write: this is on a player's disk and a browser opened
    /// twice without anything changing should not rewrite it.
    /// </summary>
    public RunVerdictCache WithJudged(string build, IReadOnlyDictionary<string, RunVerdict> judged)
    {
        if (string.IsNullOrWhiteSpace(build))
        {
            throw new ArgumentException("A verdict is recorded against a build.", nameof(build));
        }

        var onBuild = new Dictionary<string, string>(StringComparer.Ordinal);
        if (Verdicts.TryGetValue(build, out var existing))
        {
            foreach (var entry in existing) onBuild[entry.Key] = entry.Value;
        }

        var changed = false;
        foreach (var entry in judged)
        {
            var name = entry.Value.ToString();
            if (onBuild.TryGetValue(entry.Key, out var was) &&
                string.Equals(was, name, StringComparison.Ordinal))
            {
                continue;
            }

            onBuild[entry.Key] = name;
            changed = true;
        }

        if (!changed) return this;

        var verdicts =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(Verdicts, StringComparer.Ordinal)
            {
                [build] = onBuild,
            };
        return this with { Verdicts = verdicts };
    }

    /// <summary>
    /// Reads a cache, refusing a schema this build does not know.
    ///
    /// A caller with no file at all passes null and gets <see cref="Empty"/>, which is
    /// the honest answer for a player whose browser has not judged anything yet.
    /// </summary>
    public static RunVerdictCache Read(string? json)
    {
        if (json is null || string.IsNullOrWhiteSpace(json)) return Empty;

        var cache = ManifestJson.DeserializeRequired<RunVerdictCache>(json, "Runmobile verdicts");
        if (!string.Equals(cache.SchemaId, Schema, StringComparison.Ordinal))
        {
            throw new ManifestException(
                $"This verdict file declares schema '{cache.SchemaId}', and this build reads '{Schema}'.");
        }

        return cache;
    }

    /// <summary>
    /// Whether any of these runs could be in the list on this build, as far as what has
    /// been judged says.
    ///
    /// The Compendium card's whole question about the player's own runs, and the one
    /// place the unknown-means-look rule is written. A run with a remembered
    /// <see cref="RunVerdict.Failed"/> or <see cref="RunVerdict.Absent"/> is out of the
    /// reckoning; everything else - judged and passed, judged and unreadable, never
    /// judged at all - is a reason to open the browser and find out.
    /// </summary>
    public bool CouldListAny(IEnumerable<string> runIds, string build) =>
        runIds.Any(runId => For(runId, build) is not (RunVerdict.Failed or RunVerdict.Absent));

    /// <summary>The record as it goes on disk.</summary>
    public string Write() => System.Text.Json.JsonSerializer.Serialize(this, ManifestJson.Options);
}

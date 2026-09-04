using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Every build one recording has ever been asked about, and what each answered.
///
/// A catalogue rather than a field on the manifest, for the reason
/// <see cref="Revalidation"/> writes down at length: the manifest says what the
/// recording was made on and never changes, and whether it still reproduces is a
/// claim about a build, which needs somewhere of its own to live. So this is indexed
/// by (build, content hash) - two builds can share a version string across a hotfix,
/// and the content hash is what tells them apart.
///
/// It accumulates rather than replaces. A build asked again overwrites its own entry
/// and nothing else, because the question "does it still reproduce on v0.112.0" has
/// one current answer and the answer for v0.111.0 is not it.
/// </summary>
public sealed record ReproductionVerdicts
{
    public const string Schema = "sts2-pilot-trainer/reproduction-verdicts/v1";

    [JsonPropertyName("schema")]
    public string SchemaId { get; init; } = Schema;

    /// <summary>Which recording every verdict here is about.</summary>
    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    /// <summary>The verdicts, oldest build first as they were asked.</summary>
    [JsonPropertyName("verdicts")]
    public required IReadOnlyList<ReproductionVerdict> Verdicts { get; init; }

    /// <summary>
    /// This catalogue with one build's answer brought up to date.
    ///
    /// Replaces the entry for the same build and content hash and leaves the rest
    /// alone. An older answer for a different build is not stale - it is the answer
    /// for that build, which is the whole point of keying by one.
    /// </summary>
    public ReproductionVerdicts With(ReproductionVerdict verdict) => this with
    {
        Verdicts =
        [
            ..Verdicts.Where(existing =>
                !string.Equals(existing.VerifiedBuild, verdict.VerifiedBuild, StringComparison.Ordinal) ||
                !string.Equals(
                    existing.VerifiedContentHash, verdict.VerifiedContentHash, StringComparison.Ordinal)),
            verdict,
        ],
    };

    /// <summary>An empty catalogue for a recording nobody has re-keyed yet.</summary>
    public static ReproductionVerdicts For(ReplayManifest manifest) =>
        new() { RunId = manifest.RunId, Verdicts = [] };

    /// <summary>
    /// Refuses a catalogue that is not this recording's, or that says two things about
    /// one build.
    ///
    /// The same shape of check the recorded fights get, and for the same reason: a
    /// file that travels beside a manifest has to be shown to be about it, or it is a
    /// second opinion nobody can attribute.
    /// </summary>
    public void Bind(ReplayManifest manifest)
    {
        if (!string.Equals(SchemaId, Schema, StringComparison.Ordinal))
        {
            throw new ManifestException(
                $"The verdicts' schema is '{SchemaId}', and this build reads '{Schema}'. Re-run gate --rekey " +
                "to produce one this build can read.");
        }

        if (!string.Equals(RunId, manifest.RunId, StringComparison.Ordinal))
        {
            throw new ManifestException(
                $"These verdicts are about run '{RunId}', and the recording is '{manifest.RunId}'.");
        }

        var duplicate = Verdicts
            .GroupBy(verdict => (verdict.VerifiedBuild, verdict.VerifiedContentHash))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ManifestException(
                $"These verdicts answer for build {duplicate.Key.VerifiedBuild} (content " +
                $"{duplicate.Key.VerifiedContentHash}) more than once. A build has one current answer, and a " +
                "file holding two leaves the reader to pick.");
        }

        foreach (var verdict in Verdicts)
        {
            if (!string.Equals(verdict.RunId, RunId, StringComparison.Ordinal))
            {
                throw new ManifestException(
                    $"A verdict in this catalogue is about run '{verdict.RunId}' and the catalogue is " +
                    $"'{RunId}'.");
            }

            if (!string.Equals(
                    verdict.RecordedBuild, manifest.Environment.BuildVersion.Value, StringComparison.Ordinal))
            {
                throw new ManifestException(
                    $"A verdict records this recording as made on {verdict.RecordedBuild} and the manifest " +
                    $"says {manifest.Environment.BuildVersion.Value}. The recorded build is what the manifest " +
                    "says it is; a verdict that disagrees was measured against a different recording.");
            }

            if (verdict.Schema != ReproductionVerdict.CurrentSchema)
            {
                throw new ManifestException(
                    $"A verdict names schema '{verdict.Schema}' and this build reads " +
                    $"'{ReproductionVerdict.CurrentSchema}'. A catalogue holding an entry this build cannot " +
                    "read cannot be repaired by re-keying, because a re-key leaves every other build's entry " +
                    "where it is: delete this catalogue and run gate --rekey again for each build you want a " +
                    "verdict on.");
            }
        }
    }

    public string Serialize() => JsonSerializer.Serialize(this, ManifestJson.Options);

    public static ReproductionVerdicts Deserialize(string json) =>
        ManifestJson.RefuseInvalidJson("Reproduction verdicts", () =>
        {
            var verdicts = JsonSerializer.Deserialize<ReproductionVerdicts>(json, ManifestJson.Options)
                ?? throw new ManifestException("Reproduction verdicts deserialized to null.");
            ManifestJson.ValidateRequiredMembers(verdicts, "Reproduction verdicts");
            return verdicts;
        });

    /// <summary>The catalogue beside a manifest, or an empty one where none exists
    /// yet. A recording nobody has re-keyed is the ordinary case and not a defect.</summary>
    public static ReproductionVerdicts LoadOrEmpty(string path, ReplayManifest manifest) =>
        File.Exists(path) ? Deserialize(File.ReadAllText(path)) : For(manifest);

    /// <summary>Where a recording's verdicts live: beside the manifest, under the same
    /// name. One rule, so nothing has to be told where to look.</summary>
    public static string PathFor(string manifestPath) =>
        manifestPath.EndsWith(".replay.json", StringComparison.Ordinal)
            ? manifestPath[..^".replay.json".Length] + ".verdicts.json"
            : manifestPath + ".verdicts.json";
}

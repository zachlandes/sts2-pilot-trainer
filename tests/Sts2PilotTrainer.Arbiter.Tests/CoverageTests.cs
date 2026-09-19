using System.Text.Json;
using System.Text.Json.Nodes;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The coverage number, computed by the command over the committed corpus: the
/// merge-gate condition that no point this build offers is uncovered by a recording
/// without a written excusal.
///
/// Today that condition holds by excusal for most of the denominator - the committed
/// corpus reaches nineteen points and six seams by co-occurrence - and this holds the
/// command to saying so by name, with the card-reward alternative the audit found
/// unrecorded listed under its own verb as excused rather than absent, and every
/// excusal in a class the map admits.
/// </summary>
public sealed class CoverageTests
{
    [GameFact]
    public void TheCommittedCorpusCoversOrExcusesEveryPointThisBuildOffers()
    {
        InScratch(outDir =>
        {
            var result = Arbiter.Run("coverage", "--corpus", "manifests", "--out", outDir);

            Assert.True(result.Verified, result.All);
            Assert.Contains("COVERED - every point", result.Output, StringComparison.Ordinal);
            Assert.Contains("verb  ChooseNeowBlessing  3 recording(s)", result.Output, StringComparison.Ordinal);
            Assert.Contains("verb  TakeCardRewardAlternative  excused [generated]:", result.Output, StringComparison.Ordinal);
            Assert.Contains("card-reward-alternative  Skip  excused [generated]:", result.Output, StringComparison.Ordinal);
            Assert.Contains("card-prompt  CardSelectCmd.FromHand(context, player, prefs, filter, source)  not projectable", result.Output, StringComparison.Ordinal);
            Assert.Contains("event-option  EVENT.NEOW RELIC.WINGED_BOOTS  excused [generated]:", result.Output, StringComparison.Ordinal);
            Assert.Contains("event-option  EVENT.NEOW RELIC.ARCANE_SCROLL  excused [generated]:", result.Output, StringComparison.Ordinal);
            Assert.Contains("event-option  EVENT.COLORFUL_PHILOSOPHERS COLORFUL_PHILOSOPHERS.pages.INITIAL.options.IRONCLAD  excused [offered-only-to-another-character]:", result.Output, StringComparison.Ordinal);
            Assert.Contains("seam  reward-kind:gold @ AbstractModel.BeforeDeath  excused [generated; names POWER.HEIST_POWER]:", result.Output, StringComparison.Ordinal);
            Assert.Contains("event  EVENT.DARV  excused [generated]:", result.Output, StringComparison.Ordinal);
            Assert.Contains("event-option  EVENT.DARV RELIC.ASTROLABE  excused [not-on-the-route]:", result.Output, StringComparison.Ordinal);
            Assert.Contains("event-option  EVENT.NEOW RELIC.MASSIVE_SCROLL  excused [multiplayer-only]:", result.Output, StringComparison.Ordinal);
            Assert.Contains("seam  event-option @ EventModel.GenerateInitialOptions  co-occurrence in 2 recording(s)", result.Output, StringComparison.Ordinal);
            Assert.Contains("seam  reward-kind:gold @ AbstractModel.TryModifyRewards  excused [generated]:", result.Output, StringComparison.Ordinal);
            Assert.Contains("seam  reward-kind:special_card @ EventModel.GenerateInitialOptions  excused [generated; names EVENT.THE_LANTERN_KEY]:", result.Output, StringComparison.Ordinal);
            Assert.Contains("seam  reward-kind:relic @ AbstractModel.TryModifyRewardsLate  excused [not-on-the-route]:", result.Output, StringComparison.Ordinal);
            Assert.Contains("event  EVENT.AROMA_OF_CHAOS  excused [generated]:", result.Output, StringComparison.Ordinal);
            Assert.Contains("event-option  EVENT.ENDLESS_CONVEYOR ENDLESS_CONVEYOR.pages.ALL.options.LOCKED  excused [not-choosable]:", result.Output, StringComparison.Ordinal);
            Assert.Contains("rest-option  MEND  excused [multiplayer-only]:", result.Output, StringComparison.Ordinal);
            Assert.Contains("uncovered: 0", result.Output, StringComparison.Ordinal);
            // What the number is evidence about: three Ironclad runs, two natives at
            // ascension 0 on the default progression and the video reconstruction at
            // ascension 10 on the Underdocks, and no other character
            Assert.Contains("  characters: CHARACTER.IRONCLAD (3 recording(s))", result.Output, StringComparison.Ordinal);
            Assert.Contains("  variants: ACT.OVERGROWTH,ACT.HIVE,ACT.GLORY (2 recording(s))  ACT.UNDERDOCKS,ACT.HIVE,ACT.GLORY (1 recording(s))", result.Output, StringComparison.Ordinal);
            Assert.Contains("  ascensions: 0 (2 recording(s))  10 (1 recording(s))", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("inadmissible excusals", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("misnamed producers", result.Output, StringComparison.Ordinal);
            // The excusals describe the committed corpus, so none of them is reached by
            // it: an excusal this corpus reaches has a sentence that has gone false
            Assert.DoesNotContain("excused and reached by this corpus", result.Output, StringComparison.Ordinal);

            var artifact = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "coverage.json"))).RootElement;
            Assert.True(artifact.GetProperty("covered").GetBoolean());
            Assert.Equal("v0.111.0", artifact.GetProperty("build").GetProperty("build_version").GetString());
            var axes = artifact.GetProperty("axes");
            var characters = Assert.Single(axes.GetProperty("characters").EnumerateArray());
            Assert.Equal("CHARACTER.IRONCLAD", characters.GetProperty("value").GetString());
            Assert.Equal(3, characters.GetProperty("recordings").GetInt32());
            Assert.Equal(
                [("ACT.OVERGROWTH,ACT.HIVE,ACT.GLORY", 2), ("ACT.UNDERDOCKS,ACT.HIVE,ACT.GLORY", 1)],
                axes.GetProperty("variants").EnumerateArray().Select(value => (value.GetProperty("value").GetString(), value.GetProperty("recordings").GetInt32())));
            Assert.Equal(
                [("0", 2), ("10", 1)],
                axes.GetProperty("ascensions").EnumerateArray().Select(value => (value.GetProperty("value").GetString(), value.GetProperty("recordings").GetInt32())));
            var totals = artifact.GetProperty("totals");
            Assert.Equal(522, totals.GetProperty("points").GetInt32());
            Assert.Equal(0, totals.GetProperty("uncovered").GetInt32());
            Assert.Equal(6, totals.GetProperty("co_occurrence").GetInt32());
            Assert.Equal(0, totals.GetProperty("inadmissible_excusals").GetInt32());
            Assert.Equal(0, totals.GetProperty("misnamed_producers").GetInt32());
            Assert.Equal(3, totals.GetProperty("recordings").GetInt32());
            // Both natives were written by the unstamped recorder, and still credit:
            // the manifest replays on this build whatever the journal got wrong
            Assert.Equal(3, totals.GetProperty("credited_recordings").GetInt32());
            Assert.Equal(0, totals.GetProperty("unverified_recordings").GetInt32());
            Assert.Equal(
                totals.GetProperty("points").GetInt32(),
                totals.GetProperty("covered").GetInt32() + totals.GetProperty("co_occurrence").GetInt32() +
                totals.GetProperty("excused").GetInt32() + totals.GetProperty("not_projectable").GetInt32());
            var seam = artifact.GetProperty("points").EnumerateArray()
                .Single(point => point.GetProperty("identity").GetString() == "event-option @ EventModel.GenerateInitialOptions");
            Assert.Equal("CoOccurrence", seam.GetProperty("state").GetString());
            var mend = artifact.GetProperty("points").EnumerateArray()
                .Single(point => point.GetProperty("identity").GetString() == "MEND" && point.GetProperty("kind").GetString() == "rest-option");
            Assert.Equal("multiplayer-only", mend.GetProperty("excuse_class").GetString());
            Assert.False(mend.TryGetProperty("excuse_producers", out _), "an excusal naming no producer writes none");
            var hatch = artifact.GetProperty("points").EnumerateArray()
                .Single(point => point.GetProperty("identity").GetString() == "HATCH" && point.GetProperty("kind").GetString() == "rest-option");
            Assert.Equal(["CARD.BYRDONIS_EGG"], hatch.GetProperty("excuse_producers").EnumerateArray().Select(producer => producer.GetString()));
        });
    }

    /// <summary>A corpus that reaches nothing leaves every projectable point without an
    /// excusal uncovered, and the command says which and exits non-zero.</summary>
    [GameFact]
    public void AnEmptyCorpusLeavesTheUnexcusedPointsUncoveredByName()
    {
        InScratch(directory =>
        {
            var corpus = Path.Combine(directory, "corpus");
            Directory.CreateDirectory(corpus);

            var result = Arbiter.Run("coverage", "--corpus", corpus, "--out", Path.Combine(directory, "evidence"));

            Assert.False(result.Verified, result.All);
            Assert.Contains("NOT COVERED", result.Output, StringComparison.Ordinal);
            Assert.Contains("verb  ChooseNeowBlessing  uncovered", result.Output, StringComparison.Ordinal);
            Assert.Contains("event  EVENT.WATERLOGGED_SCRIPTORIUM  uncovered", result.Output, StringComparison.Ordinal);
            Assert.Contains("seam  event-option @ EventModel.GenerateInitialOptions  uncovered", result.Output, StringComparison.Ordinal);
            Assert.Contains("recordings: 0", result.Output, StringComparison.Ordinal);
            Assert.Contains("  characters: none - no crediting recording", result.Output, StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// A corpus is counted recording by recording, the way parity classifies one: a
    /// manifest this build cannot read is named with the parser's words and the rest
    /// of the corpus is still counted, and a recording the recorder marked broken
    /// credits nothing - what it reached is tallied beside the row, the row stays
    /// uncovered or excused, and the excusal it reached is not stale.
    /// </summary>
    [GameFact]
    public void AnUnreadableManifestIsNamedAndABrokenRecordingCreditsNothing()
    {
        InScratch(directory =>
        {
            var corpus = Path.Combine(directory, "corpus");
            Directory.CreateDirectory(corpus);
            const string brokenRun = "native-3LACFJ5NJ371-20260906-015902";
            var brokenManifest = Path.Combine(corpus, $"{brokenRun}{RecordingLibrary.ManifestExtension}");
            var broken = JsonNode.Parse(File.ReadAllText(
                Path.Combine(Arbiter.RepoRoot, "manifests", "native-3LACFJ5NJ371-20260906-015901.replay.json")))!;
            broken["run_id"] = brokenRun;
            broken["source"]!["native"]!["continuity"] = NativeSource.BrokenContinuity;
            File.WriteAllText(brokenManifest, broken.ToJsonString());
            File.WriteAllText(Path.Combine(corpus, $"junk{RecordingLibrary.ManifestExtension}"), "{ not a manifest");
            File.Copy(Arbiter.Manifest, Path.Combine(corpus, Path.GetFileName(Arbiter.Manifest)));

            var outDir = Path.Combine(directory, "evidence");
            var result = Arbiter.Run("coverage", "--corpus", corpus, "--out", outDir);

            Assert.False(result.Verified, result.All);
            Assert.Contains("NOT COVERED", result.Output, StringComparison.Ordinal);
            Assert.Contains("verb  ChooseNeowBlessing  1 recording(s); reached by 1 unverified recording(s), not credited", result.Output, StringComparison.Ordinal);
            Assert.Contains("verb  ChooseRestSiteOption  uncovered; reached by 1 unverified recording(s), not credited", result.Output, StringComparison.Ordinal);
            Assert.Contains($"{brokenRun}  continuity is 'broken'", result.Output, StringComparison.Ordinal);
            Assert.Contains("junk.replay.json  this build cannot read the manifest:", result.Output, StringComparison.Ordinal);
            Assert.Contains("recordings: 3", result.Output, StringComparison.Ordinal);
            Assert.Contains("recordings credited: 1  unverified: 1  unreadable: 1", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain("stale excusals", result.Output, StringComparison.Ordinal);

            var artifact = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "coverage.json"))).RootElement;
            var totals = artifact.GetProperty("totals");
            Assert.Equal(3, totals.GetProperty("recordings").GetInt32());
            Assert.Equal(1, totals.GetProperty("credited_recordings").GetInt32());
            Assert.Equal(1, totals.GetProperty("unverified_recordings").GetInt32());
            Assert.Equal(1, totals.GetProperty("unreadable_recordings").GetInt32());
            var unverified = Assert.Single(artifact.GetProperty("unverified_recordings").EnumerateArray());
            Assert.Equal(brokenRun, unverified.GetProperty("run_id").GetString());
            Assert.Equal("ContinuityBroken", unverified.GetProperty("standing").GetString());
            var unreadable = Assert.Single(artifact.GetProperty("unreadable_recordings").EnumerateArray());
            Assert.Equal("junk.replay.json", unreadable.GetProperty("manifest").GetString());
            var restSite = artifact.GetProperty("points").EnumerateArray()
                .Single(point => point.GetProperty("identity").GetString() == "ChooseRestSiteOption");
            Assert.Equal(0, restSite.GetProperty("recordings").GetInt32());
            Assert.Equal(1, restSite.GetProperty("unverified_recordings").GetInt32());
            Assert.Equal("Uncovered", restSite.GetProperty("state").GetString());
        });
    }

    /// <summary>
    /// A recording made on another build credits nothing, the way <c>replay</c>
    /// refuses the same file and <c>parity</c> names it: the audit's reproduction, a
    /// committed manifest relabelled to another version and content hash in a scratch
    /// corpus, which credited 17 points and 5 seams while the replay refused it on
    /// <c>build_version</c>. Every point it reached is tallied beside the row as
    /// unverified, the recording is named with the preflight's own three fields in the
    /// preflight's own words, and the artifact's header says which build the number
    /// is about.
    /// </summary>
    [GameFact]
    public void ARecordingOfAnotherBuildCreditsNothingAndIsNamedInThePreflightsWords()
    {
        InScratch(directory =>
        {
            var corpus = Path.Combine(directory, "corpus");
            Directory.CreateDirectory(corpus);
            const string otherBuildRun = "native-3LACFJ5NJ371-otherbuild";
            var relabelled = JsonNode.Parse(File.ReadAllText(
                Path.Combine(Arbiter.RepoRoot, "manifests", "native-3LACFJ5NJ371-20260906-015901.replay.json")))!;
            relabelled["run_id"] = otherBuildRun;
            relabelled["environment"]!["build_version"]!["Value"] = "v0.112.0";
            relabelled["environment"]!["content_hash"]!["Value"] = "999999999";
            File.WriteAllText(Path.Combine(corpus, $"{otherBuildRun}{RecordingLibrary.ManifestExtension}"), relabelled.ToJsonString());

            var outDir = Path.Combine(directory, "evidence");
            var result = Arbiter.Run("coverage", "--corpus", corpus, "--out", outDir);

            Assert.False(result.Verified, result.All);
            Assert.Contains("NOT COVERED", result.Output, StringComparison.Ordinal);
            Assert.Contains("verb  ChooseNeowBlessing  uncovered; reached by 1 unverified recording(s), not credited", result.Output, StringComparison.Ordinal);
            Assert.Contains("event  EVENT.BRAIN_LEECH  uncovered; reached by 1 unverified recording(s), not credited", result.Output, StringComparison.Ordinal);
            Assert.Contains("seam  event-option @ EventModel.GenerateInitialOptions  uncovered; reached by 1 unverified recording(s), not credited", result.Output, StringComparison.Ordinal);
            Assert.Contains(
                $"{otherBuildRun}  build_version: manifest says 'v0.112.0', this machine has 'v0.111.0'. Replaying on a different build",
                result.Output, StringComparison.Ordinal);
            Assert.Contains(
                "content_hash: manifest says '999999999', this machine has '1568834832'. The content hash is a checksum",
                result.Output, StringComparison.Ordinal);
            Assert.Contains("covered: 0  co-occurrence: 0", result.Output, StringComparison.Ordinal);
            Assert.Contains("recordings credited: 0  unverified: 1  unreadable: 0", result.Output, StringComparison.Ordinal);

            var artifact = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "coverage.json"))).RootElement;
            var build = artifact.GetProperty("build");
            Assert.Equal("v0.111.0", build.GetProperty("build_version").GetString());
            Assert.Equal("2026.08.14", build.GetProperty("build_date_utc").GetString());
            Assert.Equal("1568834832", build.GetProperty("content_hash").GetString());
            var totals = artifact.GetProperty("totals");
            Assert.Equal(0, totals.GetProperty("covered").GetInt32());
            Assert.Equal(0, totals.GetProperty("co_occurrence").GetInt32());
            Assert.Equal(0, totals.GetProperty("credited_recordings").GetInt32());
            Assert.Equal(1, totals.GetProperty("unverified_recordings").GetInt32());
            var unverified = Assert.Single(artifact.GetProperty("unverified_recordings").EnumerateArray());
            Assert.Equal(otherBuildRun, unverified.GetProperty("run_id").GetString());
            Assert.Equal("AnotherBuild", unverified.GetProperty("standing").GetString());
            Assert.Equal(
                [("build_version", false), ("build_date_utc", true), ("content_hash", false)],
                unverified.GetProperty("build").EnumerateArray()
                    .Select(field => (field.GetProperty("field").GetString(), field.GetProperty("matches").GetBoolean())));
            var neow = artifact.GetProperty("points").EnumerateArray()
                .Single(point => point.GetProperty("identity").GetString() == "ChooseNeowBlessing");
            Assert.Equal(0, neow.GetProperty("recordings").GetInt32());
            Assert.Equal(1, neow.GetProperty("unverified_recordings").GetInt32());
            Assert.Equal("Uncovered", neow.GetProperty("state").GetString());
        });
    }

    /// <summary>
    /// A recording an older recorder wrote credits what it reached, exactly as the
    /// same recording written by this build's recorder does, while <c>parity</c>
    /// holds nothing on it: the manifest replays on this build and a replayed
    /// history is a witness that every point on it is reachable; the journal is what
    /// the older recorder got wrong, and the manifest carries none of it. The two
    /// artifacts differ in nothing but the corpus they name.
    /// </summary>
    [GameFact]
    public void ARecordingOfAnOlderRecorderCreditsWhatThisBuildsRecorderWould()
    {
        InScratch(directory =>
        {
            var source = JsonNode.Parse(File.ReadAllText(
                Path.Combine(Arbiter.RepoRoot, "manifests", "native-3LACFJ5NJ371-20260906-015901.replay.json")))!;
            var outputs = new List<string>();
            var artifacts = new List<string>();
            foreach (var (corpusName, recorder) in new[] { ("corpus-a", "runmobile-recorder/0.0.1"), ("corpus-b", RunmobileVersion.Recorder) })
            {
                var corpus = Path.Combine(directory, corpusName);
                Directory.CreateDirectory(corpus);
                source["source"]!["native"]!["recorder_version"] = recorder;
                File.WriteAllText(
                    Path.Combine(corpus, "native-3LACFJ5NJ371-20260906-015901.replay.json"), source.ToJsonString());

                var outDir = Path.Combine(directory, $"evidence-{corpusName}");
                var result = Arbiter.Run("coverage", "--corpus", corpus, "--out", outDir);
                Assert.Contains("recordings: 1", result.Output, StringComparison.Ordinal);
                Assert.Contains("verb  ChooseNeowBlessing  1 recording(s)", result.Output, StringComparison.Ordinal);
                Assert.DoesNotContain("not credited", result.Output, StringComparison.Ordinal);
                Assert.DoesNotContain("credited to nothing", result.Output, StringComparison.Ordinal);
                Assert.DoesNotContain("older recorder", result.Output, StringComparison.Ordinal);
                outputs.Add(result.Output.Replace(corpusName, "<corpus>", StringComparison.Ordinal));
                artifacts.Add(File.ReadAllText(Path.Combine(outDir, "coverage.json")).Replace(corpusName, "<corpus>", StringComparison.Ordinal));
            }

            Assert.Equal(outputs[1], outputs[0]);
            Assert.Equal(artifacts[1], artifacts[0]);
            var totals = JsonDocument.Parse(artifacts[0]).RootElement.GetProperty("totals");
            Assert.Equal(1, totals.GetProperty("credited_recordings").GetInt32());
            Assert.Equal(0, totals.GetProperty("unverified_recordings").GetInt32());
        });
    }

    private static void InScratch(Action<string> body)
    {
        var directory = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", $"coverage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            body(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

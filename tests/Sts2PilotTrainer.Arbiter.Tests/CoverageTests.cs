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
/// corpus reaches nineteen points - and this holds the command to saying so by name,
/// with the card-reward alternative the audit found unrecorded listed under its own
/// verb as excused rather than absent.
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
            Assert.Contains("verb  TakeCardRewardAlternative  excused:", result.Output, StringComparison.Ordinal);
            Assert.Contains("card-reward-alternative  Skip  excused:", result.Output, StringComparison.Ordinal);
            Assert.Contains("card-prompt  CardSelectCmd.FromHand(context, player, prefs, filter, source)  not projectable", result.Output, StringComparison.Ordinal);
            Assert.Contains("uncovered: 0", result.Output, StringComparison.Ordinal);

            var artifact = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "coverage.json"))).RootElement;
            Assert.True(artifact.GetProperty("covered").GetBoolean());
            var totals = artifact.GetProperty("totals");
            Assert.Equal(131, totals.GetProperty("points").GetInt32());
            Assert.Equal(0, totals.GetProperty("uncovered").GetInt32());
            Assert.Equal(3, totals.GetProperty("recordings").GetInt32());
            Assert.Equal(
                totals.GetProperty("points").GetInt32(),
                totals.GetProperty("covered").GetInt32() + totals.GetProperty("excused").GetInt32() +
                totals.GetProperty("not_projectable").GetInt32());
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
            Assert.Contains("recordings: 0", result.Output, StringComparison.Ordinal);
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

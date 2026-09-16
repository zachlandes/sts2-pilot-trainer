using System.Text.Json;

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

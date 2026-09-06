using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// The note the Compendium card reads, and the two things that keep it from becoming
/// evidence.
///
/// It is keyed by build, so one build's answers never answer for another's, and an
/// entry this build cannot read is forgotten rather than interpreted. Both are asserted
/// through the written file rather than only through a round trip, because what is on
/// disk is the contract a later build reads.
/// </summary>
public sealed class RunVerdictCacheTests
{
    private const string Build = "v0.111.0";

    private static IReadOnlyDictionary<string, RunVerdict> Judged(
        string runId, RunVerdict verdict) =>
        new Dictionary<string, RunVerdict>(StringComparer.Ordinal) { [runId] = verdict };

    [Fact]
    public void ARecordingNobodyJudgedHasNoAnswerRatherThanAnAbsentVerdict()
    {
        Assert.Null(RunVerdictCache.Empty.For("native-a", Build));
    }

    [Fact]
    public void AJudgementSurvivesTheRoundTripAndAnswersOnlyForItsOwnBuild()
    {
        var written = RunVerdictCache.Empty.WithJudged(Build, Judged("native-a", RunVerdict.Passed)).Write();

        var read = RunVerdictCache.Read(written);

        Assert.Equal(RunVerdict.Passed, read.For("native-a", Build));
        Assert.Null(read.For("native-a", "v0.112.0"));
        Assert.Null(read.For("native-b", Build));
    }

    [Fact]
    public void JudgementsOnTwoBuildsAreBothKept()
    {
        var cache = RunVerdictCache.Empty
            .WithJudged(Build, Judged("native-a", RunVerdict.Passed))
            .WithJudged("v0.112.0", Judged("native-a", RunVerdict.Failed));

        Assert.Equal(RunVerdict.Passed, cache.For("native-a", Build));
        Assert.Equal(RunVerdict.Failed, cache.For("native-a", "v0.112.0"));
    }

    /// <summary>Recording the same answer again is not a change, which is what lets a
    /// caller decide whether a write is worth making.</summary>
    [Fact]
    public void RecordingTheSameAnswerAgainReturnsTheSameRecord()
    {
        var cache = RunVerdictCache.Empty.WithJudged(Build, Judged("native-a", RunVerdict.Passed));

        Assert.Same(cache, cache.WithJudged(Build, Judged("native-a", RunVerdict.Passed)));
        Assert.NotSame(cache, cache.WithJudged(Build, Judged("native-a", RunVerdict.Failed)));
    }

    /// <summary>
    /// A verdict name a later build wrote reads as no entry. Forgetting a hint costs a
    /// menu button one browser open; guessing at one would have this file answering a
    /// question it was never allowed to answer.
    /// </summary>
    [Fact]
    public void AVerdictNameThisBuildDoesNotKnowReadsAsNoEntry()
    {
        var cache = RunVerdictCache.Read(
            "{\"schema\":\"" + RunVerdictCache.Schema + "\",\"verdicts\":{\"" + Build +
            "\":{\"native-a\":\"Marvellous\"}}}");

        Assert.Null(cache.For("native-a", Build));
    }

    [Fact]
    public void ACacheFromAnotherSchemaIsRefused()
    {
        var refusal = Assert.Throws<ManifestException>(() =>
            RunVerdictCache.Read("""{"schema":"sts2-pilot-trainer/run-verdicts/v9","verdicts":{}}"""));

        Assert.Contains(RunVerdictCache.Schema, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoFileAtAllIsAPlayerWhoseBrowserHasJudgedNothing()
    {
        Assert.Empty(RunVerdictCache.Read(null).Verdicts);
        Assert.Empty(RunVerdictCache.Read("   ").Verdicts);
    }

    [Fact]
    public void AVerdictIsRecordedAgainstABuild()
    {
        Assert.Throws<ArgumentException>(() =>
            RunVerdictCache.Empty.WithJudged(" ", Judged("native-a", RunVerdict.Passed)));
    }
}

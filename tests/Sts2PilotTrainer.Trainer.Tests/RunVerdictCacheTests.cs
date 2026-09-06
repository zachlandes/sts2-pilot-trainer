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

    /// <summary>
    /// The equivalence the cache exists to keep: where what has been judged is in step
    /// with what the preflight would say now, the cheap question answers exactly what
    /// building the list would answer. Asserted against the list's own rule - a run is in
    /// the list when its verdict passed - rather than against a second copy of it.
    /// </summary>
    [Theory]
    [MemberData(nameof(CachesInStep))]
    public void AnUpToDateCacheAnswersWhatBuildingTheListWouldAnswer(
        IReadOnlyDictionary<string, RunVerdict> live)
    {
        var cache = live.Count == 0 ? RunVerdictCache.Empty : RunVerdictCache.Empty.WithJudged(Build, live);

        Assert.Equal(
            live.Values.Any(verdict => verdict == RunVerdict.Passed),
            cache.CouldListAny(live.Keys, Build));
    }

    public static TheoryData<IReadOnlyDictionary<string, RunVerdict>> CachesInStep() => new()
    {
        new Dictionary<string, RunVerdict>(StringComparer.Ordinal),
        new Dictionary<string, RunVerdict>(StringComparer.Ordinal)
        {
            ["native-a"] = RunVerdict.Failed,
            ["native-b"] = RunVerdict.Absent,
        },
        new Dictionary<string, RunVerdict>(StringComparer.Ordinal)
        {
            ["native-a"] = RunVerdict.Failed,
            ["native-b"] = RunVerdict.Passed,
        },
        new Dictionary<string, RunVerdict>(StringComparer.Ordinal)
        {
            ["native-a"] = RunVerdict.Passed,
        },
    };

    /// <summary>
    /// A run nobody has judged on this build is a reason to look, so it shows the button
    /// even where judging it live would list nothing.
    ///
    /// That is the one direction a cold or stale cache can be wrong in, and it is the
    /// safe one: it costs one browser open, and the same open judges every run and writes
    /// the answers, so it corrects itself. The opposite direction was a lockout with no
    /// way out - the browser is the only thing that judges and the button is the only way
    /// to the browser, so a player who updated the game past every remembered verdict
    /// never got the feature back.
    /// </summary>
    [Fact]
    public void ARunNobodyHasJudgedOnThisBuildIsAReasonToLook()
    {
        Assert.True(RunVerdictCache.Empty.CouldListAny(["native-a"], Build));

        var otherBuild = RunVerdictCache.Empty.WithJudged(
            "v0.110.0", Judged("native-a", RunVerdict.Passed));
        Assert.True(otherBuild.CouldListAny(["native-a"], Build));

        var judgedHere = otherBuild.WithJudged(Build, Judged("native-a", RunVerdict.Failed));
        Assert.False(judgedHere.CouldListAny(["native-a"], Build));
    }

    /// <summary>A verdict nobody could reach is not a run that is out: the reading is
    /// taken again, so it stays a reason to look.</summary>
    [Fact]
    public void ARunThisGameCouldNotBeReadToJudgeIsStillAReasonToLook()
    {
        var cache = RunVerdictCache.Empty.WithJudged(Build, Judged("native-a", RunVerdict.Unjudged));

        Assert.True(cache.CouldListAny(["native-a"], Build));
    }

    [Fact]
    public void NoRunsAtAllIsNothingToLookAt()
    {
        Assert.False(RunVerdictCache.Empty.CouldListAny([], Build));
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

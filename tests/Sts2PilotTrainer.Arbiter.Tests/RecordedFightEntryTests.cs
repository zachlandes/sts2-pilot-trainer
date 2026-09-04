using System.Text.Json;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// Standing in the recording's fight, driven through the real engine.
///
/// Every test here runs <c>enter-fight</c> as a command, in its own process, for the
/// reason the rest of this suite does: the engine keeps static state, and a claim
/// about what a fresh host does has to be made by a fresh host. What is exercised is
/// the same <c>RecordedFightEntry</c> the in-game host runs - construction at the
/// recording's identity, the recording's own decisions in order, and the proof at
/// the combat-start boundary.
/// </summary>
public sealed class RecordedFightEntryTests
{
    /// <summary>
    /// The whole journey, end to end: the run is constructed at the recording's
    /// identity, the recording's two decisions are made in order, and the fight that
    /// opens is the one the recording observed - on every value it observed and on
    /// the complete canonical state the cached snapshot holds.
    /// </summary>
    [GameFact]
    public void ConstructsTheRecordingsRunAndStandsInItsFightAtTheRecordedBoundary()
    {
        var report = EnterFight(out var result);

        Assert.True(result.Verified, result.All);
        Assert.True(report.GetProperty("boundary_matches").GetBoolean());
        Assert.Equal("floor2-combat-start", report.GetProperty("boundary_checkpoint").GetString());
        Assert.Equal(1, report.GetProperty("boundary_seq").GetInt32());
        Assert.Equal("NaveGreed", report.GetProperty("creator").GetString());
        Assert.All(
            report.GetProperty("comparisons").EnumerateArray(),
            comparison => Assert.True(comparison.GetProperty("matches").GetBoolean()));
    }

    /// <summary>
    /// The digest covers what the recording could not: every run-persistent random
    /// stream's position and the order of the draw pile. Its agreement with the
    /// cached combat-start snapshot is the part of this claim no video could support.
    /// </summary>
    [GameFact]
    public void TheLiveStateAtTheBoundaryIsTheCachedCombatStartSnapshot()
    {
        Arbiter.Run("combat-snapshot", Arbiter.Manifest);
        var report = EnterFight(out var result);

        Assert.True(result.Verified, result.All);
        Assert.Equal(
            report.GetProperty("recorded_snapshot_digest").GetString(),
            report.GetProperty("this_game_digest").GetString());
        Assert.Contains("cache hit", report.GetProperty("snapshot_source").GetString()!, StringComparison.Ordinal);
    }

    /// <summary>
    /// The recording's decisions are executed in its order, and each one is captioned
    /// from what the run is standing in front of rather than from anything written
    /// down about this recording.
    /// </summary>
    [GameFact]
    public void TheRecordingsDecisionsAreExecutedInOrderAndCaptionedFromTheRun()
    {
        var report = EnterFight(out _);
        var steps = report.GetProperty("steps").EnumerateArray().ToList();

        Assert.Equal(2, steps.Count);
        Assert.Equal(0, steps[0].GetProperty("seq").GetInt32());
        Assert.Equal("ChooseNeowBlessing", steps[0].GetProperty("verb").GetString());
        Assert.Equal("1 of 2", steps[0].GetProperty("counter").GetString());
        Assert.Equal("NaveGreed took Leafy Poultice", steps[0].GetProperty("caption").GetString());

        Assert.Equal(1, steps[1].GetProperty("seq").GetInt32());
        Assert.Equal("MapMove", steps[1].GetProperty("verb").GetString());
        Assert.Equal("2 of 2", steps[1].GetProperty("counter").GetString());
        Assert.Equal(
            "NaveGreed moved to the Monster node, centre column",
            steps[1].GetProperty("caption").GetString());
    }

    /// <summary>
    /// Each decision also has a target: which object on the game's own screen the
    /// transport lights before committing it.
    ///
    /// Read from the same live run as the caption, which is why it is asserted from
    /// the same report. The client resolves these against the screen's own nodes and
    /// refuses when it cannot find them; what is pinned here is that the engine names
    /// the right one, because a reveal pointing somewhere else would light one row and
    /// commit another.
    /// </summary>
    [GameFact]
    public void EachDecisionNamesWhereItLandsOnTheGamesOwnScreen()
    {
        var report = EnterFight(out _);
        var steps = report.GetProperty("steps").EnumerateArray().ToList();

        Assert.Equal(
            "event option 2 granting RELIC.LEAFY_POULTICE", steps[0].GetProperty("reveals").GetString());
        Assert.Equal("map node (row 1, column 3)", steps[1].GetProperty("reveals").GetString());
    }

    /// <summary>
    /// The transport's own controls, as the journey offers them. Looking back is
    /// refused on the first decision because there is nothing behind it.
    ///
    /// A terminal cannot draw the glyphs the client does, so what is printed is the
    /// tooltip's title. That is the honest stand-in: naming the control rather than
    /// inventing a label the client does not have.
    /// </summary>
    [GameFact]
    public void TheTransportOffersLookingBackOnlyOnceThereIsSomethingBehind()
    {
        var report = EnterFight(out _);
        var steps = report.GetProperty("steps").EnumerateArray().ToList();

        Assert.Equal("(Look back)", steps[0].GetProperty("controls").GetProperty("back").GetString());
        Assert.Equal("[Play]", steps[0].GetProperty("controls").GetProperty("play").GetString());
        Assert.Equal("[Step]", steps[0].GetProperty("controls").GetProperty("step").GetString());
        Assert.Equal("[Look back]", steps[1].GetProperty("controls").GetProperty("back").GetString());
    }

    /// <summary>
    /// The unlock state the run is generated against is supplied for that run and
    /// written nowhere. Measured rather than asserted: the profile reading and every
    /// byte of the profile store are compared either side of the entry.
    /// </summary>
    [GameFact]
    public void TheSuppliedProgressReachesTheRunAndNeverTheProfile()
    {
        var report = EnterFight(out var result);

        Assert.True(result.Verified, result.All);
        Assert.True(report.GetProperty("profile_unchanged").GetBoolean());
        Assert.Equal(
            report.GetProperty("profile_before").GetString(),
            report.GetProperty("profile_after").GetString());

        // The reading is named as a supplied model rather than as anybody's save, and
        // the run it produced is the recording's ascension whatever the profile allows.
        Assert.Contains(
            "supplied by the host",
            report.GetProperty("progress_origin").GetString()!,
            StringComparison.Ordinal);
        Assert.Contains("ascension ceiling 0", report.GetProperty("profile_before").GetString()!,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The drift refusal, exercised by damaging one of the recording's own decisions
    /// before the fight with the project's own negative control. The fight that opens
    /// is a real, valid fight; it is simply not the recorded one, and it is refused.
    /// </summary>
    [GameFact]
    public void RefusesAFightThatOpenedDifferentlyFromTheRecordings()
    {
        var report = EnterFight(out var result, "--control", "wrong-opening-choice");

        Assert.False(result.Verified);
        Assert.False(report.GetProperty("boundary_matches").GetBoolean());
        Assert.Contains(
            "did not open the way the recording's did",
            report.GetProperty("refusal").GetString()!,
            StringComparison.Ordinal);
        Assert.Contains(
            report.GetProperty("comparisons").EnumerateArray(),
            comparison => !comparison.GetProperty("matches").GetBoolean());
    }

    /// <summary>
    /// A control aimed after the boundary would leave it untouched, so entering the
    /// damaged history would look like evidence that drift is caught when nothing had
    /// drifted. It is refused before the run is built.
    /// </summary>
    [GameFact]
    public void RefusesAControlThatDoesNotReachTheDecisionsBeforeTheFight()
    {
        var result = Arbiter.Run(
            "enter-fight", Arbiter.Manifest, "--control", "move-to-a-different-node");

        Assert.False(result.Verified);
        Assert.Contains(
            "changes nothing the recording decides before the start of fight 1",
            result.All,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The boundary is not a place a host may arrive at early. With a recorded
    /// decision still unmade the question is refused rather than answered, because a
    /// player handed the controls there would be playing a different fight.
    /// </summary>
    [GameFact]
    public void RefusesToJudgeTheBoundaryBeforeEveryRecordedDecisionIsMade()
    {
        var result = Arbiter.Run("enter-fight", Arbiter.Manifest, "--step");

        Assert.False(result.Verified);
        Assert.Contains("1 of 2", result.All, StringComparison.Ordinal);
        Assert.Contains(
            "before the start of fight 1 have not been made yet, so there is nothing to compare against",
            result.All,
            StringComparison.Ordinal);
    }

    private static JsonElement EnterFight(out Arbiter.Result result, params string[] extra)
    {
        var outDir = Path.Combine("build", "test-scratch", $"enter-fight-{Guid.NewGuid():N}");
        result = Arbiter.Run(["enter-fight", Arbiter.Manifest, "--out", outDir, .. extra]);
        var path = Path.Combine(Arbiter.RepoRoot, outDir, "enter-fight.json");
        Assert.True(File.Exists(path), result.All);
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement;
    }
}

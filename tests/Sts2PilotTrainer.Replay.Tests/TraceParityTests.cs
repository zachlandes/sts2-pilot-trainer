using System.Globalization;

namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The per-decision oracle, held on traces written by hand.
///
/// No game: the oracle is a comparison over two traces and has to give the right
/// answer, in the right words, on every shape of disagreement - which is exactly the
/// set of inputs a real replay cannot be made to produce on demand. What a real
/// recorder and a real replay produce is <c>HeadlessGameplayCaptureTests</c>'s
/// question, asked through this same oracle.
/// </summary>
public sealed class TraceParityTests
{
    [Fact]
    public void TwoTracesThatAgreeEverywhereAreAtParity()
    {
        var result = TraceParity.Compare(Recorded(), Recorded());

        Assert.True(result.AtParity);
        Assert.Equal("PARITY", result.Describe());
        Assert.Equal(3, result.Decisions);
        Assert.Equal(3, result.ReplayedDecisions);
        Assert.Empty(result.OpeningDifferences);
    }

    [Fact]
    public void ASampledFieldThatDiffersIsNamedWithItsDecisionAndBothValues()
    {
        var replayed = With(Recorded(), seq: 1, step => step with { Before = Changed(step.Before, "player.hp", "58") });

        var result = TraceParity.Compare(Recorded(), replayed);

        Assert.False(result.AtParity);
        Assert.Equal(ParityDivergenceKind.BeforeSampleDiffers, result.Divergence!.Kind);
        Assert.Equal(1, result.Divergence.Seq);
        Assert.Equal("MapMove", result.Divergence.Verb);
        Assert.Equal("decision 1 (MapMove) before: player.hp: 60 -> 58", result.Describe());
    }

    [Fact]
    public void ASettledFieldThatDiffersIsNamedTheSameWay()
    {
        var replayed = With(Recorded(), seq: 2, step => step with { After = Changed(step.After, "player.gold", "0") });

        var result = TraceParity.Compare(Recorded(), replayed);

        Assert.Equal(ParityDivergenceKind.AfterSampleDiffers, result.Divergence!.Kind);
        Assert.Equal("decision 2 (PlayCard) after: player.gold: 99 -> 0", result.Describe());
    }

    /// <summary>A field the projection keeps outside the digest is not one the two
    /// hosts have to agree on, so it does not divide them here either.</summary>
    [Fact]
    public void AFieldOutsideTheDigestDoesNotDivideTheTraces()
    {
        var replayed = With(
            Recorded(), seq: 2, step => step with { After = Changed(step.After, ReplayTrace.EndedOnSideField, "enemy") });

        Assert.True(TraceParity.Compare(Recorded(), replayed).AtParity);
    }

    [Fact]
    public void ADigestThatDiffersWithEverySampledFieldEqualIsHiddenState()
    {
        var replayed = With(Recorded(), seq: 1, step => step with { BeforeDigest = Digest(99) });

        var result = TraceParity.Compare(Recorded(), replayed);

        Assert.False(result.AtParity);
        Assert.Equal(ParityDivergenceKind.HiddenStateDiffersBefore, result.Divergence!.Kind);
        Assert.True(result.Divergence.HiddenStateOnly);
        Assert.StartsWith(
            "hidden state differs at decision 1 (MapMove); every sampled field agrees (before: ",
            result.Describe(), StringComparison.Ordinal);
        Assert.Contains($"{Digest(0)} -> {Digest(99)}", result.Describe(), StringComparison.Ordinal);
    }

    [Fact]
    public void ASettledDigestThatDiffersIsHiddenStateToo()
    {
        var replayed = With(Recorded(), seq: 0, step => step with { AfterDigest = Digest(99) });

        var result = TraceParity.Compare(Recorded(), replayed);

        Assert.Equal(ParityDivergenceKind.HiddenStateDiffersAfter, result.Divergence!.Kind);
        Assert.Equal(0, result.Divergence.Seq);
    }

    /// <summary>The sample is held before the digest, so a divergence a field shows is
    /// never reported as hidden state.</summary>
    [Fact]
    public void AFieldDifferenceOutranksTheDigest()
    {
        var replayed = With(
            Recorded(), seq: 1,
            step => step with { Before = Changed(step.Before, "player.hp", "58"), BeforeDigest = Digest(99) });

        var result = TraceParity.Compare(Recorded(), replayed);

        Assert.Equal(ParityDivergenceKind.BeforeSampleDiffers, result.Divergence!.Kind);
        Assert.False(result.Divergence.HiddenStateOnly);
    }

    /// <summary>A trace written before the digests were kept says nothing about hidden
    /// state, and is not read as disagreeing about it.</summary>
    [Fact]
    public void ASideWithoutDigestsIsHeldOnItsSamplesAlone()
    {
        var undigested = new ReplayTrace
        {
            Steps = Recorded().Steps.Select(step => step with { BeforeDigest = null, AfterDigest = null }).ToList(),
        };

        Assert.True(TraceParity.Compare(undigested, Recorded()).AtParity);
        Assert.True(TraceParity.Compare(Recorded(), undigested).AtParity);
    }

    [Fact]
    public void AReplayThatEndsEarlyIsNamedAtTheFirstDecisionItLacks()
    {
        var replayed = new ReplayTrace { Steps = Recorded().Steps.Take(3).ToList() };

        var result = TraceParity.Compare(Recorded(), replayed);

        Assert.Equal(ParityDivergenceKind.MissingStep, result.Divergence!.Kind);
        Assert.Equal(2, result.Divergence.Seq);
        Assert.Equal(2, result.ReplayedDecisions);
        Assert.Equal("decision 2 (PlayCard): the replay has no step here; it ended before this decision", result.Describe());
    }

    [Fact]
    public void AReplayThatGoesOnPastTheRecordingIsNamedAtItsFirstExtraStep()
    {
        var recorded = new ReplayTrace { Steps = Recorded().Steps.Take(3).ToList() };

        var result = TraceParity.Compare(recorded, Recorded());

        Assert.Equal(ParityDivergenceKind.ExtraStep, result.Divergence!.Kind);
        Assert.Equal(2, result.Divergence.Seq);
        Assert.Equal(2, result.Decisions);
        Assert.Equal(3, result.ReplayedDecisions);
    }

    [Fact]
    public void AStepOfAnotherVerbIsNamedAsSuch()
    {
        var replayed = With(Recorded(), seq: 1, step => step with { Verb = "ShopPurchase" });

        var result = TraceParity.Compare(Recorded(), replayed);

        Assert.Equal(ParityDivergenceKind.StepDiffers, result.Divergence!.Kind);
        Assert.Equal("decision 1 (MapMove): the replay's step here is decision 1 (ShopPurchase)", result.Describe());
    }

    /// <summary>
    /// The retail client rolls a won fight's rewards after the engine has settled, on
    /// its own clock, so the reading the killing play settled into is the one digest
    /// the recorder's own resume never holds a run to; the oracle holds the same rule.
    /// The sample of that reading is still held, and so is the next decision's own
    /// before-digest, which is where the two hosts read the same state.
    /// </summary>
    [Fact]
    public void TheDigestAFightEndingDecisionSettledIntoIsNotHeld()
    {
        var replayed = With(Recorded(), seq: 2, step => step with { AfterDigest = Digest(99) });

        Assert.True(Recorded().Steps[3].EndsAFight);
        Assert.True(TraceParity.Compare(Recorded(), replayed).AtParity);
    }

    [Fact]
    public void AFightEndingDecisionsSettledSampleIsStillHeld()
    {
        var replayed = With(Recorded(), seq: 2, step => step with { After = Changed(step.After, "player.hp", "1") });

        Assert.Equal(ParityDivergenceKind.AfterSampleDiffers, TraceParity.Compare(Recorded(), replayed).Divergence!.Kind);
    }

    /// <summary>The opening reading is not a decision's reading and no decision is
    /// held to it: its hidden state is reported beside the verdict, not in it.</summary>
    [Fact]
    public void TheOpeningReadingsHiddenStateIsReportedBesideTheVerdictAndNotCounted()
    {
        var replayed = With(Recorded(), seq: -1, step => step with { BeforeDigest = Digest(99), AfterDigest = Digest(99) });

        var result = TraceParity.Compare(Recorded(), replayed);

        Assert.True(result.AtParity);
        Assert.Equal([$"hidden state: {Digest(-1)} -> {Digest(99)}"], result.OpeningDifferences);
        Assert.Equal(
            "PARITY\nopening reading: differs before any decision; every decision is held from its own reading " +
            $"(hidden state: {Digest(-1)} -> {Digest(99)})",
            result.Describe());
    }

    /// <summary>A sampled field that differs at the opening is reported the same way
    /// and counted no more than the digest is: an ascension that starts the run
    /// damaged takes the health after the retail recorder's opening reading and
    /// before the replay's, and the first decision's own before-reading is where
    /// both hosts are held. The first release measurement met exactly that.</summary>
    [Fact]
    public void TheOpeningReadingsSampleIsReportedBesideTheVerdictAndNotCounted()
    {
        var replayed = With(Recorded(), seq: -1, step => step with { After = Changed(step.After, "player.hp", "1") });

        var result = TraceParity.Compare(Recorded(), replayed);

        Assert.True(result.AtParity);
        Assert.Equal(["player.hp: 60 -> 1"], result.OpeningDifferences);
    }

    /// <summary>What the opening reading does not hold, the first decision does: the
    /// same field differing at decision 0's before-reading is the divergence.</summary>
    [Fact]
    public void TheFirstDecisionsBeforeReadingIsHeld()
    {
        var replayed = With(Recorded(), seq: 0, step => step with { Before = Changed(step.Before, "player.hp", "1") });

        var divergence = TraceParity.Compare(Recorded(), replayed).Divergence!;

        Assert.Equal(0, divergence.Seq);
        Assert.Equal(ParityDivergenceKind.BeforeSampleDiffers, divergence.Kind);
    }

    /// <summary>The opening's differences are carried on a diverged result as well,
    /// so the note stands beside either verdict: a recording whose opening differs
    /// and whose first decision diverges reports both, and the artifact loses
    /// neither.</summary>
    [Fact]
    public void TheOpeningReadingIsReportedBesideADivergenceToo()
    {
        var replayed = With(Recorded(), seq: -1, step => step with { After = Changed(step.After, "player.hp", "1") });
        replayed = With(replayed, seq: 0, step => step with { Before = Changed(step.Before, "player.hp", "1") });

        var result = TraceParity.Compare(Recorded(), replayed);

        Assert.False(result.AtParity);
        Assert.Equal(0, result.Divergence!.Seq);
        Assert.Equal(["player.hp: 60 -> 1"], result.OpeningDifferences);
        Assert.Equal(
            "decision 0 (ChooseNeowBlessing) before: player.hp: 60 -> 1\n" +
            "opening reading: differs before any decision; every decision is held from its own reading " +
            "(player.hp: 60 -> 1)",
            result.Describe());
    }

    /// <summary>A journal reads back as the trace its capture kept, digests and all,
    /// through one conversion: what the CLI compares is what the test compared.</summary>
    [Fact]
    public void AJournalReadsBackAsTheTraceItsCaptureKept()
    {
        var capture = RunCapture.Begin(new RunRecordingStart
        {
            RunId = "native-SFXT47K77RFK-20260905-030000",
            RecorderVersion = "runmobile-recorder/0.1.0",
            Identity = RecordedRun.Identity(),
            State = Recorded().Steps[0].After,
            Digest = Digest(-1),
            RunClockMs = 0,
        });
        foreach (var step in Recorded().Steps.Skip(1))
        {
            capture.Record(
                Enum.Parse<ActionVerb>(step.Verb), step.Args,
                new StateReading(step.Before, step.BeforeDigest!), new StateReading(step.After, step.AfterDigest!));
        }

        var read = RunJournal.Parse(capture.Journal.Render());

        Assert.True(TraceParity.Compare(capture.Trace, read.Trace).AtParity);
        Assert.True(TraceParity.Compare(Recorded(), read.Trace).AtParity);
        Assert.Equal(Digest(-1), read.Trace.Steps[0].BeforeDigest);
        Assert.Equal(Digest(-1), read.Trace.Steps[0].AfterDigest);
        Assert.Equal(Digest(0), read.Trace.Steps[2].BeforeDigest);
        Assert.Equal(Digest(1), read.Trace.Steps[2].AfterDigest);
    }

    // ── A three-decision recording: a blessing, a move into a fight, the killing play ──

    private static ReplayTrace Recorded() => new()
    {
        Steps =
        [
            Step(-1, "run_start", Floor(1), Floor(1), Digest(-1), Digest(-1)),
            Step(0, "ChooseNeowBlessing", Floor(1), Floor(1), Digest(-1), Digest(0)),
            Step(1, "MapMove", Floor(1), InFight(2), Digest(0), Digest(1)),
            Step(2, "PlayCard", InFight(2), Floor(2), Digest(1), Digest(2)),
        ],
    };

    private static ReplayStep Step(
        int seq, string verb, IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after,
        string beforeDigest, string afterDigest) => new()
        {
            Seq = seq,
            Verb = verb,
            Args = new SortedDictionary<string, string>(StringComparer.Ordinal),
            Before = before,
            After = after,
            BeforeDigest = beforeDigest,
            AfterDigest = afterDigest,
        };

    private static ReplayTrace With(ReplayTrace trace, int seq, Func<ReplayStep, ReplayStep> change) => new()
    {
        Steps = trace.Steps.Select(step => step.Seq == seq ? change(step) : step).ToList(),
    };

    private static IReadOnlyDictionary<string, string> Changed(
        IReadOnlyDictionary<string, string> sample, string field, string value) =>
        new SortedDictionary<string, string>(sample.ToDictionary(f => f.Key, f => f.Value), StringComparer.Ordinal)
        {
            [field] = value,
        };

    private static IReadOnlyDictionary<string, string> Floor(int floor) => new SortedDictionary<string, string>(
        StringComparer.Ordinal)
    {
        ["combat.in_progress"] = "false",
        ["combat.outcome"] = "none",
        ["player.deck"] = "CARD.STRIKE_IRONCLAD|CARD.BASH",
        ["player.gold"] = "99",
        ["player.hp"] = "60",
        ["player.max_hp"] = "80",
        ["run.is_game_over"] = "false",
        ["run.map_coord"] = $"r{floor.ToString(CultureInfo.InvariantCulture)}c3",
        ["run.total_floor"] = floor.ToString(CultureInfo.InvariantCulture),
    };

    private static IReadOnlyDictionary<string, string> InFight(int floor) => new SortedDictionary<string, string>(
        Floor(floor).ToDictionary(f => f.Key, f => f.Value), StringComparer.Ordinal)
    {
        ["combat.in_progress"] = "true",
        ["combat.outcome"] = "in_progress",
        ["combat.turn"] = "1",
    };

    private static string Digest(int seq) =>
        "sha256:" + (seq + 1).ToString("x2", CultureInfo.InvariantCulture).PadLeft(64, 'a');
}

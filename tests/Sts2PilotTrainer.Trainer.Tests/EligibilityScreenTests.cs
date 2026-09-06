using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// What the Combat Trainer's one screen says, and why.
///
/// Every claim on that screen is a claim about somebody's game, so each one is
/// checked here against a preflight verdict built from a known reading. These run
/// without the game installed, which is the point of keeping the wording and the row
/// rules out of the mod host.
/// </summary>
public sealed class EligibilityScreenTests
{
    [Fact]
    public void PassingGameGetsThePassHeadline()
    {
        var screen = Fixtures.Screen();

        Assert.True(screen.Eligible);
        Assert.Equal(TrainerCopy.PassHeadline, screen.Headline);
        Assert.All(screen.Rows, row => Assert.True(row.Met));
        Assert.Empty(screen.Refusals);
    }

    [Fact]
    public void AnAdditionalLoadedModGetsTheFailHeadlineAndARefusal()
    {
        var prerequisites = Fixtures.Prerequisites() with
        {
            Mods =
            [
                new LocalMod("Runmobile", "Runmobile", "0.1.0", false, "Loaded"),
                new LocalMod("patcher", "Behavior Patcher", "1.0.0", false, "Failed"),
            ],
        };

        var screen = Fixtures.Screen(prerequisites);

        Assert.False(screen.Eligible);
        Assert.Equal(TrainerCopy.FailHeadline, screen.Headline);
        Assert.Contains(screen.Refusals, refusal => refusal.Contains("Disable every mod except Runmobile"));
    }

    [Fact]
    public void FailingGameGetsTheFailHeadlineAndKeepsEveryOtherRow()
    {
        var screen = Fixtures.Screen(Fixtures.Prerequisites(relicsAvailable: 141));

        Assert.False(screen.Eligible);
        Assert.Equal(TrainerCopy.FailHeadline, screen.Headline);
        Assert.False(screen.Row("Relics").Met);
        Assert.True(screen.Row("Build").Met);
    }

    [Fact]
    public void RowValuesComeFromTheManifestAndTheReading()
    {
        var screen = Fixtures.Screen(Fixtures.Prerequisites(relicsAvailable: 141));

        Assert.Equal("Build v0.111.0", screen.Row("Build").Label);
        Assert.Equal("Content hash 1568834832", screen.Row("Content hash").Label);
        Assert.Equal("Relics: 141 of 143", screen.Row("Relics").Label);
        Assert.Equal("Act: Underdocks unlocked", screen.Row("Act: Underdocks").Label);
        Assert.Equal("Ascension 10 available on Ironclad", screen.Row("Ascension").Label);
    }

    [Fact]
    public void EveryActTheManifestClimbsGetsItsOwnRow()
    {
        var screen = Fixtures.Screen();

        Assert.Equal(
            ["Act: Underdocks unlocked", "Act: Hive unlocked", "Act: Glory unlocked"],
            screen.Rows.Where(row => row.Label.StartsWith("Act:", StringComparison.Ordinal))
                .Select(row => row.Label));
    }

    /// <summary>
    /// A shortfall of one act is invisible in a total, and it is the one shortfall
    /// that changes every fight in the run. The row that names it has to be the one
    /// that goes red.
    /// </summary>
    [Fact]
    public void OnlyTheLockedActGoesRed()
    {
        var screen = Fixtures.Screen(Fixtures.Prerequisites(lockedActs: ["ACT.HIVE"]));

        Assert.True(screen.Row("Act: Underdocks").Met);
        Assert.False(screen.Row("Act: Hive").Met);
        Assert.True(screen.Row("Act: Glory").Met);
        Assert.Contains("cannot climb", screen.Row("Act: Hive").Note);
    }

    [Fact]
    public void AFailingRowCarriesTheEnginesOwnRemediationUnchanged()
    {
        var screen = Fixtures.Screen(Fixtures.Prerequisites(ascensionCeiling: 9));

        var row = screen.Row("Ascension");
        Assert.False(row.Met);
        Assert.Contains(EnvironmentPreflight.UnlockRemediation, row.Note);
    }

    /// <summary>
    /// The hash is a necessary gate and never proof of parity, and a green row that
    /// said nothing would invite exactly the reading the engine's sentence forbids.
    /// </summary>
    [Fact]
    public void AGreenContentHashRowStillCarriesItsScope()
    {
        var screen = Fixtures.Screen();

        var row = screen.Row("Content hash");
        Assert.True(row.Met);
        Assert.Equal(EnvironmentPreflight.ContentHashScope, row.Note);
    }

    [Fact]
    public void UnmetRowsComeFirst()
    {
        var screen = Fixtures.Screen(Fixtures.Prerequisites(relicsAvailable: 141, ascensionCeiling: 9));

        var met = screen.Rows.Select(row => row.Met).ToList();
        Assert.Equal(2, met.Count(value => !value));
        Assert.False(met[0]);
        Assert.False(met[1]);
        Assert.DoesNotContain(false, met.Skip(2));
    }

    /// <summary>
    /// A gate that failed and had no row would be a requirement the screen quietly
    /// dropped. There is no row shape for a mismatched build date, so its sentence is
    /// shown instead - the whole sentence, as the engine wrote it.
    /// </summary>
    [Fact]
    public void AFailingFieldWithNoRowIsShownAsItsOwnSentence()
    {
        var reading = Fixtures.Prerequisites() with { BuildDateUtc = "2026.08.13" };
        var recording = Fixtures.Recording();
        var screen = EligibilityScreen.For(
            recording, EnvironmentPreflight.LiveGame(recording.Environment, reading, run: null));

        Assert.False(screen.Eligible);
        Assert.Contains(screen.Refusals, refusal => refusal.Contains("compared in local time"));
    }

    // ── A shortfall nobody can fix ─────────────────────────────────────────
    //
    // The rows above are errands: unlock the relics, finish a run to raise the
    // ascension ceiling, unlock the act. A recording made by the recorder names the
    // unlock state it was generated under, and this build either has those ids or
    // does not - and if it does not, no amount of playing changes it. Everything
    // below is that difference being drawn rather than collapsed.

    /// <summary>
    /// A build that does not ship an id the recording names is a statement, not an
    /// errand, and the row says which.
    /// </summary>
    [Fact]
    public void AnIdThisBuildDoesNotShipIsUnavailableRatherThanUnmet()
    {
        var screen = Fixtures.Screen(
            Fixtures.UnbuildablePrerequisites(), identity: Fixtures.ExactIdentity());

        var row = screen.Row("Epochs");
        Assert.Equal(PreflightOutcome.Unavailable, row.State);
        Assert.False(row.Met);
        Assert.False(row.Actionable);
        Assert.DoesNotContain(EnvironmentPreflight.UnlockRemediation, row.Note);
        Assert.Contains(EnvironmentPreflight.ContentNotShipped, row.Note);
    }

    /// <summary>
    /// The defect this screen was rewritten to remove: a state that could not be built
    /// leaves the act question unasked, and the absence of an answer used to be drawn
    /// as a locked act - telling a player to go and unlock content that is not in
    /// their game and never will be.
    /// </summary>
    [Fact]
    public void AnActQuestionThatWasNeverAskedIsNotDrawnAsALockedAct()
    {
        var screen = Fixtures.Screen(
            Fixtures.UnbuildablePrerequisites(), identity: Fixtures.ExactIdentity());

        var acts = screen.Rows.Where(row => row.Label.StartsWith("Act:", StringComparison.Ordinal)).ToList();

        Assert.Equal(3, acts.Count);
        Assert.All(acts, row => Assert.Equal(PreflightOutcome.Unavailable, row.State));
        Assert.All(acts, row => Assert.False(row.Actionable));
        Assert.All(acts, row => Assert.DoesNotContain(EnvironmentPreflight.UnlockRemediation, row.Note));
    }

    /// <summary>The reading's own sentence about why the state could not be built
    /// reaches the player, rather than a row that says only that something is
    /// missing.</summary>
    [Fact]
    public void TheUnaskedActRowCarriesTheReadingsOwnShortfall()
    {
        var screen = Fixtures.Screen(
            Fixtures.UnbuildablePrerequisites(), identity: Fixtures.ExactIdentity());

        Assert.Contains("does not ship 1 of the 2 epoch id(s)", screen.Row("Act: Underdocks").Note);
    }

    /// <summary>
    /// And it is said once. A question that was never asked is one fact about the
    /// reading rather than a fact about each act, so repeating it under all three
    /// would print the same paragraph three times to say it.
    /// </summary>
    [Fact]
    public void TheUnaskedActSentenceIsSaidOnceRatherThanUnderEveryAct()
    {
        var screen = Fixtures.Screen(
            Fixtures.UnbuildablePrerequisites(), identity: Fixtures.ExactIdentity());

        var acts = screen.Rows.Where(row => row.Label.StartsWith("Act:", StringComparison.Ordinal)).ToList();

        Assert.Equal(3, acts.Count);
        Assert.Equal(1, acts.Count(row => row.Note is { Length: > 0 }));
    }

    /// <summary>
    /// A locked act is a fact about that act, so each locked row keeps its own
    /// sentence.
    /// </summary>
    [Fact]
    public void EveryLockedActKeepsItsOwnSentence()
    {
        var screen = Fixtures.Screen(Fixtures.Prerequisites(lockedActs: ["ACT.HIVE", "ACT.GLORY"]));

        Assert.Contains("cannot climb", screen.Row("Act: Hive").Note);
        Assert.Contains("cannot climb", screen.Row("Act: Glory").Note);
    }

    /// <summary>
    /// Under an exact requirement the locked act is still one act, and the acts the
    /// build does have are still met.
    ///
    /// The state was built here, so the reading names which act it leaves locked; only
    /// what that answer is worth changes, and a screen that read the gate's outcome as
    /// "the question was never asked" drew all three acts as content this build does
    /// not ship and put the sentence under the wrong one.
    /// </summary>
    [Fact]
    public void UnderAnExactRequirementOnlyTheLockedActRefusesAndKeepsItsOwnSentence()
    {
        var unbuildable = Fixtures.UnbuildablePrerequisites();
        var reading = unbuildable with
        {
            Unlocks = unbuildable.Unlocks with
            {
                ShippedIds = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["epochs"] = ["EPOCH.ONE", "EPOCH.TWO"],
                    ["encounters_seen"] = ["ENCOUNTER.ONE"],
                },
            },
            LockedActs = ["ACT.HIVE"],
            UnlockStateShortfall = null,
        };

        var screen = Fixtures.Screen(reading, identity: Fixtures.ExactIdentity());
        var hive = screen.Row("Act: Hive");

        Assert.Equal(PreflightOutcome.Met, screen.Row("Act: Underdocks").State);
        Assert.Null(screen.Row("Act: Underdocks").Note);
        Assert.Equal(PreflightOutcome.Met, screen.Row("Act: Glory").State);
        Assert.Null(screen.Row("Act: Glory").Note);

        Assert.Equal(PreflightOutcome.Unavailable, hive.State);
        Assert.Contains("cannot climb", hive.Note);
        Assert.Contains(EnvironmentPreflight.ContentNotShipped, hive.Note);
        Assert.DoesNotContain(EnvironmentPreflight.UnlockRemediation, hive.Note);
        Assert.Equal(
            1,
            screen.Rows.Count(row =>
                row.Label.StartsWith("Act:", StringComparison.Ordinal) && row.Note is { Length: > 0 }));
    }

    /// <summary>
    /// The run count an exact state is built from is not a requirement of anybody's
    /// game, so it is not a row.
    ///
    /// It is reported rather than compared - nothing about this installation has to
    /// match it - and a row for it read "Runs: supplied to the run of 11", which is
    /// neither a requirement nor a sentence. The report keeps it.
    /// </summary>
    [Fact]
    public void TheRunCountAnExactStateWasBuiltFromIsNotARow()
    {
        var reading = Fixtures.UnbuildablePrerequisites();
        var recording = Fixtures.Recording(Fixtures.ExactIdentity());
        var preflight = EnvironmentPreflight.LiveGame(recording.Environment, reading, run: null);
        var screen = EligibilityScreen.For(recording, preflight);

        Assert.Contains(preflight.Fields, field => field.Field == "unlocks_runs");
        Assert.DoesNotContain(screen.Rows, row => row.Label.StartsWith("Runs", StringComparison.Ordinal));
    }

    /// <summary>
    /// "Yet" is a promise, and this is the case where nothing can keep it.
    /// </summary>
    [Fact]
    public void AShortfallNobodyCanFixDropsThePromiseFromTheHeadline()
    {
        var screen = Fixtures.Screen(
            Fixtures.UnbuildablePrerequisites(), identity: Fixtures.ExactIdentity());

        Assert.False(screen.Eligible);
        Assert.Equal(TrainerCopy.UnavailableHeadline, screen.Headline);
        Assert.DoesNotContain("yet", screen.Headline, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One unavailable field decides the headline even beside errands, because running
    /// every errand would leave the answer exactly where it is.
    /// </summary>
    [Fact]
    public void AnErrandBesideAnUnavailableRowDoesNotRestoreThePromise()
    {
        var reading = Fixtures.UnbuildablePrerequisites() with { BuildVersion = "v0.110.0" };
        var screen = Fixtures.Screen(reading, identity: Fixtures.ExactIdentity());

        Assert.Equal(TrainerCopy.UnavailableHeadline, screen.Headline);
        Assert.Contains(screen.Rows, row => row.Actionable);
    }

    /// <summary>What this build cannot supply is read before the errands, because it
    /// is what decides the answer.</summary>
    [Fact]
    public void WhatThisBuildCannotSupplyIsReadFirst()
    {
        var reading = Fixtures.UnbuildablePrerequisites() with { BuildVersion = "v0.110.0" };
        var screen = Fixtures.Screen(reading, identity: Fixtures.ExactIdentity());

        var states = screen.Rows.Select(row => row.State).ToList();
        var lastUnavailable = states.LastIndexOf(PreflightOutcome.Unavailable);
        var firstErrand = states.IndexOf(PreflightOutcome.NotMet);

        Assert.True(firstErrand > lastUnavailable, string.Join(", ", screen.Rows.Select(row => row.Label)));
        Assert.DoesNotContain(PreflightOutcome.Met, states.Take(firstErrand));
    }

    /// <summary>And an ordinary shortfall keeps it: every row on that screen is an
    /// errand, so the headline that says so is true.</summary>
    [Fact]
    public void AnEnvironmentThatIsOnlyBehindKeepsTheFailHeadline()
    {
        var screen = Fixtures.Screen(Fixtures.Prerequisites(relicsAvailable: 141));

        Assert.Equal(TrainerCopy.FailHeadline, screen.Headline);
        Assert.All(screen.Rows.Where(row => !row.Met), row => Assert.True(row.Actionable));
    }

    /// <summary>
    /// A recording this build can reproduce exactly says so on every row, which is the
    /// other half of the same path: the epochs and encounters it names are counted
    /// against what it named, not against this build's whole catalogue.
    /// </summary>
    [Fact]
    public void ARecordingThisBuildCanReproduceExactlyPasses()
    {
        var reading = Fixtures.Prerequisites() with
        {
            Unlocks = Fixtures.Prerequisites().Unlocks with
            {
                Categories = [],
                ShippedIds = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["epochs"] = ["EPOCH.ONE", "EPOCH.TWO", "EPOCH.THREE"],
                    ["encounters_seen"] = ["ENCOUNTER.ONE", "ENCOUNTER.TWO"],
                },
            },
        };

        var screen = Fixtures.Screen(reading, identity: Fixtures.ExactIdentity());

        Assert.True(screen.Eligible);
        Assert.Equal("Epochs: 2 of 2", screen.Row("Epochs").Label);
        Assert.Equal("Encounters seen: 1 of 1", screen.Row("Encounters seen").Label);
    }

    [Fact]
    public void TheRecordingLineNamesTheBuildTheManifestRecords()
    {
        var screen = Fixtures.Screen();

        Assert.Equal("Recorded on v0.111.0 (2026.08.14)", screen.RecordingLine);
        Assert.Equal("NaveGreed · Ironclad · Ascension 10 · Floor 2 · Sludge Spinner", screen.Subtitle);
        Assert.Equal(TrainerCopy.Name, screen.Title);
        Assert.Equal(TrainerCopy.BackButton, screen.BackButton);
        Assert.Equal(TrainerCopy.ProfileNote, screen.ProfileNote);
    }
}

/// <summary>
/// Model ids read as a player reads them, which is what the two approved rows that
/// name content depend on.
/// </summary>
public sealed class ModelIdNameTests
{
    [Theory]
    [InlineData("ACT.UNDERDOCKS", "Underdocks")]
    [InlineData("CHARACTER.IRONCLAD", "Ironclad")]
    [InlineData("ENCOUNTER.SLUDGE_SPINNER_WEAK", "Sludge Spinner Weak")]
    [InlineData("Underdocks", "Underdocks")]
    public void ModelIdsReadAsNames(string modelId, string expected) =>
        Assert.Equal(expected, ModelIdNames.Display(modelId));
}

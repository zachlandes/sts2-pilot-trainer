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

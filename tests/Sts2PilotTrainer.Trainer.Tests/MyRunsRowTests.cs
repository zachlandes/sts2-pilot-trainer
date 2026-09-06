namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// What the settings row says about the player's own runs.
///
/// The derivation is pure, so everything the row can ever read is reachable here: each
/// of the three second lines, both ends of the size figure, the refusal with nothing to
/// remove, and the receipt that follows a purge which could not take everything.
///
/// Two of these are about the vocabulary rather than about arithmetic, and they are
/// here because a wrong word on this row is the defect nothing else would catch. The
/// row says <em>runs</em>: journal, manifest and recording are this project's internal
/// names, and a player has never heard any of them.
/// </summary>
public sealed class MyRunsRowTests
{
    private const long Mb = 1024L * 1024L;

    [Fact]
    public void TheReadingIsHowManyRunsAndWhatTheyTake()
    {
        var row = MyRunsRow.For(new MyRunsFacts(Runs: 12, Bytes: 6 * Mb, Keep: 50));

        Assert.Equal("12 runs · 6 MB", row.Reading);
        Assert.Equal("on this computer, in user://Runmobile/recordings", row.Detail);
    }

    /// <summary>
    /// A count of one reads as one. The design writes every count as a template, and a
    /// row reading "1 runs" is the template showing through to the player.
    /// </summary>
    [Fact]
    public void OneRunIsSingular()
    {
        var row = MyRunsRow.For(new MyRunsFacts(Runs: 1, Bytes: 40 * 1024, Keep: 50));

        Assert.Equal("1 run · 40 KB", row.Reading);
    }

    [Fact]
    public void AnEmptyLibraryReadsAsNothingAndRefusesTheControl()
    {
        var row = MyRunsRow.For(new MyRunsFacts(Runs: 0, Bytes: 0, Keep: 50));

        Assert.Equal("0 runs · 0 MB", row.Reading);
        Assert.False(row.RemovePressable);
    }

    [Fact]
    public void TheControlIsOfferedAsSoonAsThereIsSomethingToRemove()
    {
        Assert.True(MyRunsRow.For(new MyRunsFacts(Runs: 1, Bytes: 1024, Keep: 50)).RemovePressable);
    }

    /// <summary>
    /// A policy standing below what is on the disk says so on the row, which is the
    /// design's rule that the standing act is as visible as the immediate one.
    /// </summary>
    [Fact]
    public void APolicyBelowTheCountSaysHowManyWillGo()
    {
        var row = MyRunsRow.For(new MyRunsFacts(Runs: 12, Bytes: 6 * Mb, Keep: 5));

        Assert.Equal(
            "7 older runs will be removed at the main menu · user://Runmobile/recordings", row.Detail);
    }

    [Fact]
    public void APolicyAboveTheCountSaysNothingAboutRemoving()
    {
        var row = MyRunsRow.For(new MyRunsFacts(Runs: 3, Bytes: Mb, Keep: 50));

        Assert.Equal("on this computer, in user://Runmobile/recordings", row.Detail);
    }

    /// <summary>
    /// The receipt beats the warning: the removal already happened, and what the policy
    /// will do at the next main menu is the smaller news.
    /// </summary>
    [Fact]
    public void AReceiptIsWhatTheRowSaysAfterAPurge()
    {
        var row = MyRunsRow.For(new MyRunsFacts(Runs: 0, Bytes: 0, Keep: 5, RemovedJustNow: 12));

        Assert.Equal("0 runs · 0 MB", row.Reading);
        Assert.Equal("12 runs removed just now · user://Runmobile/recordings", row.Detail);
    }

    /// <summary>
    /// A purge leaves the run the game can still continue, so the reading afterwards is
    /// the one run that is still there rather than the zero that was asked for. The
    /// receipt and the reading are two different facts and the row states both.
    /// </summary>
    [Fact]
    public void APurgeThatCouldNotTakeEverythingStillReadsAsWhatIsLeft()
    {
        var row = MyRunsRow.For(new MyRunsFacts(Runs: 1, Bytes: 90 * 1024, Keep: 50, RemovedJustNow: 11));

        Assert.Equal("1 run · 90 KB", row.Reading);
        Assert.Equal("11 runs removed just now · user://Runmobile/recordings", row.Detail);
    }

    /// <summary>Zero removed is a real answer: somebody pressed Remove and is owed the
    /// receipt whether or not there was anything to take.</summary>
    [Fact]
    public void APurgeThatFoundNothingStillSaysSo()
    {
        var row = MyRunsRow.For(new MyRunsFacts(Runs: 0, Bytes: 0, Keep: 50, RemovedJustNow: 0));

        Assert.Equal("0 runs removed just now · user://Runmobile/recordings", row.Detail);
    }

    /// <summary>
    /// The figure follows the magnitude. A single run in megabytes rounds to nothing
    /// while the row says there is one, and a full library in kilobytes is a number
    /// nobody can weigh.
    ///
    /// The unit follows the rounded figure rather than the byte count, so nothing reads
    /// as a thousand and twenty-four of the smaller unit: the last four rows are the
    /// band either side of each boundary, where rounding is what carries the figure
    /// into the next one.
    /// </summary>
    [Theory]
    [InlineData(0L, "0 MB")]
    [InlineData(1L, "1 KB")]
    [InlineData(40L * 1024, "40 KB")]
    [InlineData(1024L * 1024, "1 MB")]
    [InlineData((long)(1.55 * 1024 * 1024), "1.5 MB")]
    [InlineData(120L * 1024 * 1024, "120 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3 GB")]
    [InlineData(1023L * 1024, "1023 KB")]
    [InlineData(1_048_000L, "1 MB")]
    [InlineData((1024L * 1024) - 1, "1 MB")]
    [InlineData((1024L * 1024 * 1024) - 1, "1 GB")]
    public void TheSizeIsReadInTheUnitThatCarriesIt(long bytes, string expected)
    {
        Assert.Equal(expected, MyRunsRow.Size(bytes));
    }

    /// <summary>
    /// A settings file this build could not read says what is actually true under it:
    /// an unreadable file leaves no policy in force, so nothing is removed of this mod's
    /// own accord until somebody puts the file right. The numeral beside it is the
    /// default shown in place of the player's sentence, and no warning is issued from
    /// it - runs it named would not be going anywhere.
    /// </summary>
    [Fact]
    public void AnUnreadableSettingsFileSaysNothingIsBeingRemoved()
    {
        var row = MyRunsRow.For(
            new MyRunsFacts(Runs: 12, Bytes: 6 * Mb, Keep: 50, SettingsReadable: false));

        Assert.Equal("50", row.KeepNumeral);
        Assert.Equal(
            "settings.json could not be read, so no runs are removed automatically until it is · " +
            "user://Runmobile/recordings",
            row.Detail);
    }

    /// <summary>
    /// Every control that would write into the settings file is refused over one this
    /// build cannot read - the stepper, and the removal too, because the removal records
    /// the request in that same file before it takes anything. An act that cannot be
    /// recorded is one that must not be offered.
    /// </summary>
    [Fact]
    public void AnUnreadableSettingsFileRefusesEveryControlThatWouldWriteToIt()
    {
        var refused = MyRunsRow.For(new MyRunsFacts(Runs: 12, Bytes: Mb, Keep: 50, SettingsReadable: false));
        Assert.False(refused.KeepPressable);
        Assert.False(refused.RemovePressable);

        var offered = MyRunsRow.For(new MyRunsFacts(Runs: 12, Bytes: Mb, Keep: 50));
        Assert.True(offered.KeepPressable);
        Assert.True(offered.RemovePressable);
    }

    /// <summary>
    /// The prediction is what retention will do, not what the subtraction says.
    ///
    /// The run the game can currently Continue is never removed, whatever the policy
    /// asks for, so a standing purge over three runs takes two.
    /// </summary>
    [Fact]
    public void TheRunTheGameCanContinueIsNotCountedAmongTheOnesGoing()
    {
        var row = MyRunsRow.For(
            new MyRunsFacts(Runs: 3, Bytes: Mb, Keep: 0, ContinuableRunWouldBeLeft: true));

        Assert.Equal(
            "2 older runs will be removed at the main menu · user://Runmobile/recordings", row.Detail);
    }

    /// <summary>The only run there is being the continuable one leaves nothing to
    /// predict, so the row says where they are instead of promising a removal that
    /// cannot happen.</summary>
    [Fact]
    public void AStandingPurgeOverTheContinuableRunAlonePromisesNothing()
    {
        var row = MyRunsRow.For(
            new MyRunsFacts(Runs: 1, Bytes: Mb, Keep: 0, ContinuableRunWouldBeLeft: true));

        Assert.Equal("on this computer, in user://Runmobile/recordings", row.Detail);
    }

    /// <summary>
    /// A disk that refused for a reason this row did not establish names none of them.
    /// The save-profile sentence is the one cause it may state, because it is the one a
    /// player resolves by choosing a profile.
    /// </summary>
    [Fact]
    public void ADiskThatRefusedNamesNoCauseAndOffersNothing()
    {
        var row = MyRunsRow.For(
            new MyRunsFacts(Runs: 0, Bytes: 0, Keep: 50, SettingsReadable: null, Disk: MyRunsDisk.Refused));

        Assert.Equal("Your runs could not be read; the game's log says why", row.Reading);
        Assert.Equal(string.Empty, row.Detail);
        Assert.False(row.KeepPressable);
        Assert.False(row.RemovePressable);
    }

    /// <summary>A policy nobody got as far as reading is not a policy anybody refused,
    /// and the controls over it are refused either way.</summary>
    [Fact]
    public void APolicyNobodyReadIsNotAPolicyRefused()
    {
        var row = MyRunsRow.For(
            new MyRunsFacts(
                Runs: 0, Bytes: 0, Keep: 50, SettingsReadable: null, Disk: MyRunsDisk.NoSaveProfileYet));

        Assert.Equal("Your runs are read once you have chosen a save profile", row.Reading);
        Assert.False(row.KeepPressable);
    }

    /// <summary>
    /// The row is written in the player's noun. Every internal name for the same thing
    /// stays internal.
    ///
    /// The directory is the one place an internal name is on the surface, and it is
    /// there on purpose: it is a path a player can open, not a word for a run. It is
    /// taken out before the sentence is judged rather than left to weaken the rule.
    /// </summary>
    [Theory]
    [InlineData("recording")]
    [InlineData("journal")]
    [InlineData("manifest")]
    public void NoInternalNameForARunReachesTheRow(string internalName)
    {
        var row = MyRunsRow.For(new MyRunsFacts(Runs: 12, Bytes: 6 * Mb, Keep: 5, RemovedJustNow: 3));

        foreach (var line in new[]
                 {
                     row.Reading, row.Detail, row.KeepLabel, row.RemoveLabel,
                     row.Confirm.Title, row.Confirm.Body, row.Confirm.Remove, row.Confirm.Keep,
                 })
        {
            var prose = line.Replace("user://Runmobile/recordings", string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(internalName, prose, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ThePolicyCarriesTheSettledLabel()
    {
        var row = MyRunsRow.For(new MyRunsFacts(Runs: 12, Bytes: 6 * Mb, Keep: 5));

        Assert.Equal("Keep my runs", row.KeepLabel);
        Assert.Equal("5", row.KeepNumeral);
        Assert.True(row.KeepPressable);
    }

    /// <summary>
    /// The confirmation names what goes and what does not. The second half is the claim
    /// the store's containment rule is what keeps: a save, a profile and run history are
    /// not files this removal can name.
    /// </summary>
    [Fact]
    public void TheConfirmationNamesWhatGoesAndWhatIsLeftAlone()
    {
        var confirm = MyRunsRow.For(new MyRunsFacts(Runs: 12, Bytes: 6 * Mb, Keep: 5)).Confirm;

        Assert.Equal("Remove all your runs?", confirm.Title);
        Assert.Equal(
            "12 runs, 6 MB, recorded by Runmobile. Your saves, profile and run history are not touched.",
            confirm.Body);
        Assert.Equal("Remove", confirm.Remove);
        Assert.Equal("Keep them", confirm.Keep);
    }
}

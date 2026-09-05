using System.Reflection;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What the recorder reads out of a live game, checked against the game rather than
/// against a copy of what it is expected to say.
///
/// The recorder itself needs a person playing and none of these do. What they pin is
/// the reading half - the unlock state a run was generated against, the run's own
/// clock and start time, the mod list, and the members each of those goes through.
/// Every one is a place a game update could quietly turn a recording into a lie
/// rather than into an error, which is why they are asked of this build each time
/// rather than written down once.
/// </summary>
public sealed class RecordedRunReadingTests
{
    /// <summary>
    /// The readings that give a recording its name and its clock, and the members they
    /// go through.
    ///
    /// A recording is keyed by its seed and the moment the run began, and both have to
    /// survive a reload or a session continued tomorrow would open a second recording
    /// of the same run. Two of these are private on this build, so they are pinned here
    /// rather than discovered missing in the middle of somebody's session.
    /// </summary>
    [GameFact]
    public void TheRunsOwnClockAndStartTimeAreStillWhereTheRecorderReadsThem()
    {
        var runManager = GameType("MegaCrit.Sts2.Core.Runs.RunManager");
        Assert.NotNull(runManager.GetField("_startTime", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.Equal(typeof(long), runManager.GetProperty("RunTime")!.PropertyType);
        Assert.Equal(typeof(bool), runManager.GetProperty("IsAbandoned")!.PropertyType);
        Assert.NotNull(runManager.GetMethod("OnEnded"));
        Assert.NotNull(runManager.GetMethod("SetUpNewSingleplayer"));
        Assert.NotNull(runManager.GetMethod("SetUpSavedSingleplayer"));

        var unlockState = GameType("MegaCrit.Sts2.Core.Unlocks.UnlockState");
        Assert.NotNull(unlockState.GetField("_encountersSeen", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.NotNull(unlockState.GetField("_unlockedEpochIds", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.Equal(typeof(int), unlockState.GetProperty("NumberOfRuns")!.PropertyType);

        var runState = GameType("MegaCrit.Sts2.Core.Runs.RunState");
        Assert.Equal(typeof(int), runState.GetProperty("TotalFloor")!.PropertyType);
        Assert.NotNull(runState.GetProperty("UnlockState"));
    }

    /// <summary>
    /// The two screens the recorder reads an answer out of, and the members that carry
    /// both halves of it.
    ///
    /// A card screen suspends inside the call that opened it and hands its answer back
    /// through a seam the player's own client fills, so these are the only places a
    /// recorder can see which option was taken. A build that renamed one would leave
    /// the recorder unable to say which card came off a reward, which is a decision
    /// every fight makes.
    /// </summary>
    [GameFact]
    public void TheCardScreensStillCarryWhatTheyOfferedAndWhatCameBack()
    {
        var grid = GameType("MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardGridSelectionScreen");
        Assert.NotNull(grid.GetField("_cards", BindingFlags.Instance | BindingFlags.NonPublic));
        Assert.True(typeof(Task).IsAssignableFrom(grid.GetMethod("CardsSelected")!.ReturnType));

        var reward = GameType("MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardRewardSelectionScreen");
        Assert.NotNull(reward.GetMethod("ShowScreen"));
        Assert.True(typeof(Task).IsAssignableFrom(reward.GetMethod("OptionSelected")!.ReturnType));
    }

    /// <summary>A recording is named by nothing that says whose game it was.</summary>
    [Fact]
    public void ARecordingsNameCarriesOnlyItsSeedAndWhenItBegan()
    {
        var name = LiveRun.NameRecording(
            "SFXT47K77RFK", new DateTimeOffset(2026, 9, 5, 3, 14, 15, TimeSpan.Zero));

        Assert.Equal("native-SFXT47K77RFK-20260905-031415", name);
    }

    /// <summary>
    /// The mod list a recorder writes is the declaration each mod made, and says so.
    ///
    /// Nothing running inside a player's game is in a position to audit the mods beside
    /// it, which is exactly why <c>EnvironmentPreflight</c> judges a native recording by
    /// a rule over those declarations rather than against a list of audited names. A
    /// risk line that read as an assessment would be a list that looks like diligence
    /// and carries none.
    /// </summary>
    [Fact]
    public void TheRecordedModListReportsWhatEachModDeclaredAndAssessesNothing()
    {
        var mods = ModEnvironment.AsRecorded(
        [
            new LocalMod("Runmobile", "Runmobile", "0.1.0", AffectsGameplay: false, "Loaded"),
            new LocalMod("Thing", "Some Thing", "2.0", AffectsGameplay: true, "Loaded"),
        ]);

        Assert.Equal(2, mods.ReportedCount);
        Assert.Equal(mods.ReportedCount, mods.Mods.Count);
        Assert.Equal(["Runmobile", "Some Thing"], mods.Mods.Select(mod => mod.Name));
        Assert.Equal<bool?[]>([false, true], [.. mods.Mods.Select(mod => mod.AffectsGameplay)]);
        Assert.All(mods.Mods, mod =>
            Assert.Contains("Read rather than judged", mod.ReplayRisk, StringComparison.Ordinal));
    }

    /// <summary>
    /// And the preflight then judges that list by its rule: a mod that declares itself
    /// gameplay-affecting is refused.
    /// </summary>
    [Fact]
    public void APreflightRefusesARecordedModThatSaysItChangesTheGame()
    {
        var refused = EnvironmentPreflight.Prerequisites(
            Identity(ModEnvironment.AsRecorded(
                [new LocalMod("Thing", "Some Thing", "2.0", AffectsGameplay: true, "Loaded")])),
            LocalReading(),
            "native");

        var field = Assert.Single(refused.Fields, entry => entry.Field == "mod_environment");
        Assert.False(field.Matches);
        Assert.Contains("declare themselves gameplay-affecting", field.Diagnostic!, StringComparison.Ordinal);
    }

    private static Type GameType(string name)
    {
        _ = EngineHost.StartupPhase();
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Single(assembly => assembly.GetName().Name == "sts2")
            .GetType(name);
        Assert.True(type is not null, $"This build has no {name}.");
        return type!;
    }

    private static EnvironmentIdentity Identity(ModEnvironment mods)
    {
        var evidence = FactEvidence.AtActionOrdinal(-1);
        return new EnvironmentIdentity
        {
            BuildVersion = Fact<string>.Captured("v0.111.0", evidence),
            BuildDateUtc = Fact<string>.Captured("2026.08.14", evidence),
            GameMode = Fact<string>.Captured("standard", evidence),
            Seed = Fact<string>.Captured("SFXT47K77RFK", evidence),
            ContentHash = Fact<string>.Captured("1568834832", evidence),
            Ascension = Fact<int>.Captured(0, evidence),
            Unlocks = Fact<UnlockRequirement>.Captured(
                UnlockRequirement.Complete("no unlock requirement is under test here"), evidence),
            Character = Fact<string>.Captured("CHARACTER.IRONCLAD", evidence),
            Acts = Fact<IReadOnlyList<string>>.Captured(["ACT.UNDERDOCKS"], evidence),
            Mods = Fact<ModEnvironment>.Captured(mods, evidence),
        };
    }

    private static LocalPrerequisites LocalReading() => new()
    {
        BuildVersion = "v0.111.0",
        BuildDateUtc = "2026.08.14",
        ContentHash = "1568834832",
        Mods = [],
        Unlocks = new UnlockInventory
        {
            Origin = "a reading written by this test",
            FromPlayerProfile = false,
            Categories = [],
        },
        LockedActs = [],
    };
}

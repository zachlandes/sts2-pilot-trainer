namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The sequencing an in-game host has to get right: which gate is asked when, and
/// what "no run yet" is allowed to mean.
///
/// This is the boundary the mod calls across. It is checked here rather than in the
/// host because the rule is about the gates, not about Godot, and because a host
/// that got it wrong would tell a player their install was broken when they had
/// simply not started a run.
/// </summary>
public sealed class LivePreflightTests
{
    [Fact]
    public void WithNoRunOnlyThePrerequisitesAreReported()
    {
        var identity = Identity();

        var live = EnvironmentPreflight.LiveGame(identity, Prerequisites(), run: null);

        Assert.False(live.RunPresent);
        Assert.DoesNotContain(live.Fields, field => field.Field.StartsWith("run_", StringComparison.Ordinal));
        Assert.True(live.Matches, Why(live));
    }

    /// <summary>
    /// The run-identity gate still refuses a null reading - it is asked, and it
    /// answers - but a host that has not been given a run is not told its install is
    /// wrong. Both facts have to hold at once.
    /// </summary>
    [Fact]
    public void TheRunIdentityGateStillRefusesWhenThereIsNoRun()
    {
        var identity = Identity();

        var live = EnvironmentPreflight.LiveGame(identity, Prerequisites(), run: null);

        Assert.False(live.RunIdentity.Matches);
        Assert.Contains(live.RunIdentity.Fields, field => field.Field == "run_present");
    }

    [Fact]
    public void AFailingPrerequisiteFailsTheWholeVerdict()
    {
        var identity = Identity();

        var live = EnvironmentPreflight.LiveGame(
            identity, Prerequisites() with { ContentHash = "999" }, run: null);

        Assert.False(live.Matches);
    }

    [Fact]
    public void AWrongRunFailsTheVerdictEvenWhenEveryPrerequisitePasses()
    {
        var identity = Identity();

        var live = EnvironmentPreflight.LiveGame(identity, Prerequisites(), Run(seed: "SOMETHINGELSE"));

        Assert.True(live.RunPresent);
        Assert.True(live.Prerequisites.Matches, Why(live));
        Assert.False(live.RunIdentity.Matches);
        Assert.False(live.Matches);
        Assert.Contains(live.Fields, field => field.Field == "run_seed" && !field.Matches);
    }

    [Fact]
    public void TheRightRunPassesAndItsFieldsAreReported()
    {
        var identity = Identity();

        var live = EnvironmentPreflight.LiveGame(identity, Prerequisites(), Run());

        Assert.True(live.Matches, Why(live));
        Assert.Contains(live.Fields, field => field.Field == "run_seed");
    }

    /// <summary>
    /// The shipped fixture records the source creator's three mods, which the mod
    /// gate judges separately. These tests are about sequencing, so they use a
    /// vanilla source environment and leave that gate to its own tests.
    /// </summary>
    private static EnvironmentIdentity Identity() =>
        Fixtures.ValidManifest().Environment with
        {
            Mods = Fact<ModEnvironment>.Inferred(
                new ModEnvironment { Name = "vanilla", ReportedCount = 0, Mods = [] },
                FactEvidence.Reasoning("no mods in the source environment")),
        };

    /// <summary>Names the fields that refused, so a failure here reads as a fact
    /// about a gate rather than as a bare false.</summary>
    private static string Why(LivePreflight live) =>
        string.Join("; ", live.Fields.Where(field => !field.Matches)
            .Select(field => $"{field.Field}: expected '{field.Expected}', got '{field.Actual}'"));

    private static LocalPrerequisites Prerequisites() => new()
    {
        BuildVersion = "v0.111.0",
        BuildDateUtc = "2026.08.14",
        ContentHash = "1568834832",
        Mods = [],
        Unlocks = new UnlockInventory
        {
            Origin = "test reading",
            FromPlayerProfile = true,
            Categories = [new UnlockCategory("relics", 143, 143, [])],
        },
        LockedActs = [],
        ProfileAscensionCeiling = 10,
    };

    private static LocalRunReading Run(string? seed = null) => new()
    {
        Origin = "run in progress",
        Seed = seed ?? "SFXT47K77RFK",
        GameMode = "standard",
        Ascension = 10,
        Character = "CHARACTER.IRONCLAD",
        Acts = ["ACT.UNDERDOCKS"],
    };
}

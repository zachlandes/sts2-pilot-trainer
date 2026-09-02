using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// Every preflight dimension, in both directions.
///
/// The rules are pure so that this file can exist at all: it runs on a machine with
/// no game installed, which is where the checks most need to be provable, because a
/// checker whose negative side is never exercised is a checker that reports a pass.
/// Each test names one field and shows the input that makes it refuse.
/// </summary>
public class EnvironmentPreflightTests
{
    // ---- build and content -------------------------------------------------

    [Fact]
    public void MatchingEnvironmentPasses()
    {
        var result = EnvironmentPreflight.Prerequisites(Environment(), Local());

        Assert.True(result.Matches, Describe(result));
    }

    [Fact]
    public void ADifferentBuildVersionRefuses()
    {
        var result = EnvironmentPreflight.Prerequisites(Environment(), Local() with { BuildVersion = "v0.103.2" });

        Assert.False(result.Matches);
        Assert.Contains("no migration path", Diagnostic(result, "build_version"), StringComparison.Ordinal);
    }

    [Fact]
    public void ADifferentBuildDateRefuses()
    {
        var result = EnvironmentPreflight.Prerequisites(
            Environment(), Local() with { BuildDateUtc = "2026.08.13" });

        Assert.False(result.Matches);
        Assert.False(Field(result, "build_date_utc").Matches);
    }

    [Fact]
    public void ADifferentContentHashRefuses()
    {
        var result = EnvironmentPreflight.Prerequisites(Environment(), Local() with { ContentHash = "1234567890" });

        Assert.False(result.Matches);
        Assert.Contains("necessary gate", Diagnostic(result, "content_hash"), StringComparison.Ordinal);
    }

    // ---- manifest-side gates -----------------------------------------------

    [Fact]
    public void ASeedOutsideTheGamesAlphabetRefuses()
    {
        // O and I are exactly the characters the game never emits and an OCR reader
        // invents, so this is the shape a misread seed arrives in.
        var environment = Environment() with
        {
            Seed = Fact<string>.Observed("SEXT47KIIREK", FactEvidence.AtVideoTime(1, "overlay")),
        };

        var result = EnvironmentPreflight.Prerequisites(environment, Local());

        Assert.False(result.Matches);
        Assert.Contains("misread rather than observed", Diagnostic(result, "seed_alphabet"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("daily")]
    [InlineData("custom")]
    public void AModeThisMilestoneCannotReplayRefuses(string mode)
    {
        var environment = Environment() with
        {
            GameMode = Fact<string>.Inferred(mode, FactEvidence.Reasoning("test")),
        };

        var result = EnvironmentPreflight.Prerequisites(environment, Local());

        Assert.False(result.Matches);
        Assert.False(Field(result, "game_mode_supported").Matches);
    }

    [Fact]
    public void AnUnrecognisedSourceModEnvironmentRefuses()
    {
        var environment = Environment() with
        {
            Mods = Fact<ModEnvironment>.Inferred(
                new ModEnvironment
                {
                    Name = "someone-elses-2026-08",
                    ReportedCount = 1,
                    Mods = [new InstalledMod("Unknown Mod", "unknown", "unassessed")],
                },
                FactEvidence.Reasoning("test")),
        };

        var result = EnvironmentPreflight.Prerequisites(environment, Local());

        Assert.False(result.Matches);
        Assert.Contains("has not been bounded", Diagnostic(result, "mod_environment"), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAdditionalLoadedModRefusesEvenWhenTheContentHashMatches()
    {
        var result = EnvironmentPreflight.Prerequisites(
            Environment(),
            Local() with
            {
                Mods = [new LocalMod("patcher", "Behavior Patcher", "1.0.0", false, "Loaded")],
            });

        Assert.False(result.Matches);
        Assert.Contains("does not cover behaviour patches", Diagnostic(result, "loaded_mod_environment"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AFailedModRefusesBecauseItsResourcesMayRemainLoaded()
    {
        var result = EnvironmentPreflight.Prerequisites(
            Environment(),
            Local() with
            {
                Mods =
                [
                    new LocalMod("CombatTrainer", "Combat Trainer", "0.1.0", false, "Loaded"),
                    new LocalMod("broken", "Broken Resource Mod", "1.0.0", false, "Failed"),
                ],
            });

        Assert.False(result.Matches);
        Assert.Contains("failed mod can leave resources loaded", Diagnostic(result, "loaded_mod_environment"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// The host failing on its own is our defect, not a compatibility problem.
    ///
    /// Every nonempty failure used to be reported as another active or failed mod,
    /// which on a clean install with only Combat Trainer present sent the player off
    /// to disable mods they do not have and blamed their game for ours.
    /// </summary>
    [Fact]
    public void TheHostFailingAloneIsReportedAsItsOwnFailure()
    {
        var result = EnvironmentPreflight.LiveGame(
            Environment(),
            Local() with
            {
                Mods = [new LocalMod("CombatTrainer", "Combat Trainer", "0.1.0", false, "Failed")],
            },
            run: null);

        Assert.False(result.Prerequisites.Matches);
        Assert.Equal(
            "Combat Trainer failed to load. Restart the game and check again.",
            Diagnostic(result.Prerequisites, "loaded_mod_environment"));
    }

    /// <summary>
    /// The same failure with somebody else's mod beside it is a compatibility
    /// problem, and keeps the explanation that says why a hash cannot settle it.
    /// </summary>
    [Fact]
    public void TheHostFailingBesideAnotherModIsStillReportedAsContamination()
    {
        var result = EnvironmentPreflight.LiveGame(
            Environment(),
            Local() with
            {
                Mods =
                [
                    new LocalMod("CombatTrainer", "Combat Trainer", "0.1.0", false, "Failed"),
                    new LocalMod("baselib", "BaseLib", "3.4.5", false, "Loaded"),
                ],
            },
            run: null);

        Assert.False(result.Prerequisites.Matches);
        Assert.Contains(
            "another active or failed mod",
            Diagnostic(result.Prerequisites, "loaded_mod_environment"),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A disabled mod beside a failed host is not another mod being there, so the
    /// host's own failure is still what gets reported. Disabled is the state a player
    /// reaches by doing exactly what the other sentence would have told them to do.
    /// </summary>
    [Fact]
    public void ADisabledModBesideAFailedHostDoesNotBecomeContamination()
    {
        var result = EnvironmentPreflight.LiveGame(
            Environment(),
            Local() with
            {
                Mods =
                [
                    new LocalMod("CombatTrainer", "Combat Trainer", "0.1.0", false, "Failed"),
                    new LocalMod("baselib", "BaseLib", "3.4.5", false, "Disabled"),
                ],
            },
            run: null);

        Assert.Equal(
            "Combat Trainer failed to load. Restart the game and check again.",
            Diagnostic(result.Prerequisites, "loaded_mod_environment"));
    }

    /// <summary>
    /// A game reporting no mod at all is neither: nothing failed and nothing else is
    /// there, so the host simply is not loaded and the sentence says so.
    /// </summary>
    [Fact]
    public void AGameThatReportsNoHostAtAllIsNotReportedAsAFailure()
    {
        var result = EnvironmentPreflight.LiveGame(Environment(), Local() with { Mods = [] }, run: null);

        Assert.Contains(
            "did not report Combat Trainer as loaded",
            Diagnostic(result.Prerequisites, "loaded_mod_environment"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DisabledModsDoNotPreventParity()
    {
        var result = EnvironmentPreflight.Prerequisites(
            Environment(),
            Local() with
            {
                Mods =
                [
                    new LocalMod("CombatTrainer", "Combat Trainer", "0.1.0", false, "Loaded"),
                    new LocalMod("disabled", "Disabled Mod", "1.0.0", true, "Disabled"),
                ],
            });

        Assert.True(result.Matches, Describe(result));
    }

    [Fact]
    public void TheKnownNonGameplayHostIsTheOnlyPermittedLoadedMod()
    {
        var result = EnvironmentPreflight.Prerequisites(
            Environment(),
            Local() with
            {
                Mods = [new LocalMod("CombatTrainer", "Combat Trainer", "0.1.0", false, "Loaded")],
            });

        Assert.True(result.Matches, Describe(result));
    }

    [Fact]
    public void AGameplayClaimForTheHostRefuses()
    {
        var result = EnvironmentPreflight.Prerequisites(
            Environment(),
            Local() with
            {
                Mods = [new LocalMod("CombatTrainer", "Combat Trainer", "0.1.0", true, "Loaded")],
            });

        Assert.False(result.Matches);
    }

    [Fact]
    public void TheAuditedSourceToolingEnvironmentPasses()
    {
        var environment = Environment() with
        {
            Mods = Fact<ModEnvironment>.Inferred(
                new ModEnvironment
                {
                    Name = "navegreed-2026-08",
                    ReportedCount = 3,
                    Mods =
                    [
                        new InstalledMod("Slay the Relics Exporter", "overlay export", "reads only"),
                        new InstalledMod("BaseLib", "shared library", "audited"),
                        new InstalledMod("Hindsight", "run review", "audited"),
                    ],
                },
                FactEvidence.Reasoning("test")),
        };

        Assert.True(EnvironmentPreflight.Prerequisites(environment, Local()).Matches);
    }

    // ---- unlocks -----------------------------------------------------------

    [Fact]
    public void AnUnlockRequirementThisArbiterCannotCheckRefuses()
    {
        var environment = Environment() with
        {
            Unlocks = Fact<UnlockRequirement>.Declared(
                new UnlockRequirement { Completeness = "partial", Basis = "test" }),
        };

        var result = EnvironmentPreflight.Prerequisites(environment, Local());

        Assert.False(result.Matches);
        Assert.Contains("Only 'complete' is expressible", Diagnostic(result, "unlocks_requirement"),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("characters", "CHARACTER.SILENT")]
    [InlineData("cards", "CARD.ACCELERANT")]
    [InlineData("card_pools", "CARD_POOL.SILENT_CARD_POOL")]
    [InlineData("relics", "RELIC.BIG_HAT")]
    [InlineData("potions", "POTION.BLOOD_POTION")]
    [InlineData("shared_ancients", "EVENT.DARV")]
    [InlineData("epochs", "UNDERDOCKS_EPOCH")]
    public void AShortfallInAnyUnlockCategoryRefuses(string category, string missing)
    {
        var reduced = Complete().Categories
            .Select(entry => entry.Name == category
                ? entry with { Available = entry.Required - 1, MissingSample = [missing] }
                : entry)
            .ToList();

        var result = EnvironmentPreflight.Prerequisites(
            Environment(), Local() with { Unlocks = Complete() with { Categories = reduced } });

        Assert.False(result.Matches);
        var diagnostic = Diagnostic(result, $"unlocks_{category}");
        Assert.Contains(missing, diagnostic, StringComparison.Ordinal);
        Assert.Contains("the same seed produces a different run", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void NoUnlockDiagnosticEverOffersToEditTheSave()
    {
        // The remediation is the product boundary, not a nicety: a tool that edited a
        // player's progress to make a replay possible would have destroyed the thing
        // the replay was evidence about.
        var reduced = Complete().Categories.Select(entry => entry with { Available = 0 }).ToList();

        var result = EnvironmentPreflight.Prerequisites(
            Environment(),
            Local() with
            {
                Unlocks = Complete() with { Categories = reduced, FromPlayerProfile = true },
                ProfileAscensionCeiling = 0,
                LockedActs = ["ACT.UNDERDOCKS"],
            });

        Assert.False(result.Matches);
        foreach (var field in result.Fields.Where(f => !f.Matches))
        {
            Assert.Contains("never writes to your save", field.Diagnostic ?? string.Empty, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ALockedActRefusesEvenWhenEveryCategoryTotalIsComplete()
    {
        // The one shortfall a total cannot show: this build ships two acts at index 0,
        // and taking the other one generates different content behind an identical map.
        var result = EnvironmentPreflight.Prerequisites(
            Environment(), Local() with { LockedActs = ["ACT.UNDERDOCKS"] });

        Assert.False(result.Matches);
        Assert.Contains("would take the other variant", Diagnostic(result, "acts_unlocked"), StringComparison.Ordinal);
    }

    [Fact]
    public void AProfileBelowTheManifestsAscensionRefuses()
    {
        var result = EnvironmentPreflight.Prerequisites(
            Environment(),
            Local() with
            {
                Unlocks = Complete() with { FromPlayerProfile = true },
                ProfileAscensionCeiling = 9,
            });

        Assert.False(result.Matches);
        Assert.Contains("highest available ascension", Diagnostic(result, "ascension_unlocked"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void AProfileAtTheManifestsAscensionPasses()
    {
        var result = EnvironmentPreflight.Prerequisites(
            Environment(),
            Local() with
            {
                Unlocks = Complete() with { FromPlayerProfile = true },
                ProfileAscensionCeiling = 10,
            });

        Assert.True(result.Matches, Describe(result));
    }

    [Fact]
    public void AHostSuppliedUnlockStateReportsTheAscensionGateAsUnmeasured()
    {
        // Reporting "not gated" is the honest answer for a host that constructs runs
        // directly. A pass here would be a measurement nobody took.
        var field = Field(EnvironmentPreflight.Prerequisites(Environment(), Local()), "ascension_unlocked");

        Assert.True(field.Matches);
        Assert.Contains("not gated", field.Actual, StringComparison.Ordinal);
    }

    // ---- run identity ------------------------------------------------------

    [Fact]
    public void AMatchingRunPasses()
    {
        Assert.True(EnvironmentPreflight.RunIdentity(Environment(), Run()).Matches);
    }

    [Fact]
    public void NoRunInProgressRefusesWithWhatToStart()
    {
        var result = EnvironmentPreflight.RunIdentity(Environment(), null);

        Assert.False(result.Matches);
        var diagnostic = Diagnostic(result, "run_present");
        Assert.Contains("SFXT47K77RFK", diagnostic, StringComparison.Ordinal);
        Assert.Contains("ascension 10", diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunOnADifferentSeedRefuses()
    {
        var result = EnvironmentPreflight.RunIdentity(Environment(), Run() with { Seed = "SFXT47K77RFX" });

        Assert.False(result.Matches);
        Assert.Contains("different run from the first floor", Diagnostic(result, "run_seed"), StringComparison.Ordinal);
    }

    [Fact]
    public void ARunInADifferentModeRefuses()
    {
        var result = EnvironmentPreflight.RunIdentity(Environment(), Run() with { GameMode = "custom" });

        Assert.False(result.Matches);
        Assert.False(Field(result, "run_game_mode").Matches);
    }

    [Fact]
    public void ARunAtADifferentAscensionRefuses()
    {
        var result = EnvironmentPreflight.RunIdentity(Environment(), Run() with { Ascension = 9 });

        Assert.False(result.Matches);
        Assert.Contains("fixed when it begins", Diagnostic(result, "run_ascension"), StringComparison.Ordinal);
    }

    [Fact]
    public void ARunOnADifferentCharacterRefuses()
    {
        var result = EnvironmentPreflight.RunIdentity(Environment(), Run() with { Character = "CHARACTER.SILENT" });

        Assert.False(result.Matches);
        Assert.False(Field(result, "run_character").Matches);
    }

    [Fact]
    public void ARunThroughADifferentActVariantRefuses()
    {
        var result = EnvironmentPreflight.RunIdentity(
            Environment(), Run() with { Acts = ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"] });

        Assert.False(result.Matches);
        Assert.Contains("producing the same map", Diagnostic(result, "run_acts"), StringComparison.Ordinal);
    }

    [Fact]
    public void CombiningKeepsEveryFieldAndFailsIfEitherGateFailed()
    {
        var combined = EnvironmentPreflight.Combine(
            EnvironmentPreflight.Prerequisites(Environment(), Local()),
            EnvironmentPreflight.RunIdentity(Environment(), null));

        Assert.False(combined.Matches);
        Assert.Contains(combined.Fields, field => field.Field == "build_version");
        Assert.Contains(combined.Fields, field => field.Field == "run_present");
    }

    // ---- fixtures ----------------------------------------------------------

    private static EnvironmentIdentity Environment() => Fixtures.ValidManifest().Environment with
    {
        Acts = Fact<IReadOnlyList<string>>.Inferred(
            ["ACT.UNDERDOCKS", "ACT.HIVE", "ACT.GLORY"], FactEvidence.Reasoning("map screen title")),
        Mods = Fact<ModEnvironment>.Inferred(
            new ModEnvironment { Name = "vanilla", ReportedCount = 0, Mods = [] },
            FactEvidence.Reasoning("test")),
    };

    private static LocalPrerequisites Local() => new()
    {
        BuildVersion = "v0.111.0",
        BuildDateUtc = "2026.08.14",
        ContentHash = "1568834832",
        Mods = [],
        Unlocks = Complete(),
        LockedActs = [],
        ProfileAscensionCeiling = null,
    };

    private static UnlockInventory Complete() => new()
    {
        Origin = "UnlockState.all, supplied by the host in place of the source player's profile",
        FromPlayerProfile = false,
        Categories =
        [
            new UnlockCategory("characters", 5, 5, []),
            new UnlockCategory("cards", 596, 596, []),
            new UnlockCategory("card_pools", 12, 12, []),
            new UnlockCategory("character_card_pools", 5, 5, []),
            new UnlockCategory("relics", 299, 299, []),
            new UnlockCategory("potions", 66, 66, []),
            new UnlockCategory("shared_ancients", 1, 1, []),
            new UnlockCategory("epochs", 57, 57, []),
        ],
    };

    private static LocalRunReading Run() => new()
    {
        Origin = "run in progress, read from RunManager.State",
        Seed = "SFXT47K77RFK",
        GameMode = "standard",
        Ascension = 10,
        Character = "CHARACTER.IRONCLAD",
        Acts = ["ACT.UNDERDOCKS", "ACT.HIVE", "ACT.GLORY"],
    };

    private static PreflightField Field(PreflightResult result, string field) =>
        result.Fields.SingleOrDefault(entry => entry.Field == field)
        ?? throw new InvalidOperationException(
            $"No preflight field '{field}'. Present: {string.Join(", ", result.Fields.Select(f => f.Field))}");

    private static string Diagnostic(PreflightResult result, string field) =>
        Field(result, field).Diagnostic
        ?? throw new InvalidOperationException($"Preflight field '{field}' passed and carries no diagnostic.");

    private static string Describe(PreflightResult result) =>
        string.Join("\n", result.Fields
            .Where(field => !field.Matches)
            .Select(field => $"{field.Field}: expected '{field.Expected}', got '{field.Actual}'"));
}

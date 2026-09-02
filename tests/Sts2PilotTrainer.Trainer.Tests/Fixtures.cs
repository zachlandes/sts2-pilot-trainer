using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// The identity and readings the screen is computed from.
///
/// Built here rather than reused from a manifest file so that each test changes
/// exactly one thing: what the screen says about a failing relic count is a claim
/// about one field, and a fixture that failed three gates would prove none of them.
/// </summary>
internal static class Fixtures
{
    internal static EnvironmentIdentity Identity() => new()
    {
        BuildVersion = Fact<string>.Observed("v0.111.0", FactEvidence.AtVideoTime(9000, "overlay")),
        BuildDateUtc = Fact<string>.Observed("2026.08.14", FactEvidence.AtVideoTime(9000, "overlay")),
        GameMode = Fact<string>.Inferred("standard", FactEvidence.Reasoning("not date-formatted")),
        Seed = Fact<string>.Observed("SFXT47K77RFK", FactEvidence.AtVideoTime(9000, "overlay")),
        ContentHash = Fact<string>.Observed("1568834832", FactEvidence.AtVideoTime(9000, "overlay")),
        Ascension = Fact<int>.Observed(10, FactEvidence.AtVideoTime(9000, "badge")),
        Unlocks = Fact<UnlockRequirement>.Inferred(
            UnlockRequirement.Complete("experienced creator"), FactEvidence.Reasoning("never on screen")),
        Character = Fact<string>.Observed("CHARACTER.IRONCLAD", FactEvidence.AtVideoTime(75600, "sprite")),
        Acts = Fact<IReadOnlyList<string>>.Inferred(
            ["ACT.UNDERDOCKS", "ACT.HIVE", "ACT.GLORY"], FactEvidence.Reasoning("map screen title")),
        Mods = Fact<ModEnvironment>.Inferred(
            new ModEnvironment
            {
                Name = "navegreed-2026-08",
                ReportedCount = 0,
                Mods = [],
            },
            FactEvidence.Reasoning("count observed")),
    };

    /// <summary>A machine that can play the recording: every category complete, every
    /// act unlocked, the ascension available.</summary>
    internal static LocalPrerequisites Prerequisites(
        int relicsAvailable = 143,
        IReadOnlyList<string>? lockedActs = null,
        int ascensionCeiling = 10,
        string? contentHash = null,
        string? buildVersion = null)
    {
        var missingRelics = relicsAvailable < 143
            ? new[] { "RELIC.BURNING_BLOOD", "RELIC.RING_OF_THE_SNAKE" }
            : [];

        return new LocalPrerequisites
        {
            BuildVersion = buildVersion ?? "v0.111.0",
            BuildDateUtc = "2026.08.14",
            ContentHash = contentHash ?? "1568834832",
            Mods = [new LocalMod("CombatTrainer", "Combat Trainer", "0.1.0", false, "Loaded")],
            Unlocks = new UnlockInventory
            {
                Origin = "the save progress of whichever profile this process has",
                FromPlayerProfile = true,
                Categories =
                [
                    new UnlockCategory("characters", 5, 5, []),
                    new UnlockCategory("relics", relicsAvailable, 143, missingRelics),
                ],
            },
            LockedActs = lockedActs ?? [],
            ProfileAscensionCeiling = ascensionCeiling,
        };
    }

    internal static LocalRunReading Run(string? seed = null, int ascension = 10, string? character = null) =>
        new()
        {
            Origin = "run in progress, read from RunManager.State",
            Seed = seed ?? "SFXT47K77RFK",
            GameMode = "standard",
            Ascension = ascension,
            Character = character ?? "CHARACTER.IRONCLAD",
            Acts = ["ACT.UNDERDOCKS", "ACT.HIVE", "ACT.GLORY"],
        };

    /// <summary>
    /// The recording the screen is about. Only the parts a screen reads are filled
    /// in: who it is by, and the identity every row is measured against.
    /// </summary>
    internal static ReplayManifest Recording(EnvironmentIdentity? identity = null, string? creator = "NaveGreed") =>
        new()
        {
            RunId = "navegreed-OJ-6QXhNgdg",
            Environment = identity ?? Identity(),
            Source = new SourceProvenance
            {
                Kind = "vod",
                ExtractionMethod = "manual",
                Coverage = "run start through the first fight",
                Video = creator is null ? null : new VideoSource
                {
                    Platform = "youtube",
                    VideoId = "OJ-6QXhNgdg",
                    ChannelId = "UCuuDxwofGcur0Lt6iP-aDww",
                    ChannelName = creator,
                    DurationSeconds = 2049,
                },
            },
            Actions = [],
            Checkpoints = [],
        };

    internal static EligibilityScreen Screen(
        LocalPrerequisites? prerequisites = null,
        LocalRunReading? run = null,
        bool fightOffered = false)
    {
        var recording = Recording();
        return EligibilityScreen.For(
            recording,
            EnvironmentPreflight.LiveGame(recording.Environment, prerequisites ?? Prerequisites(), run),
            fightOffered);
    }

    internal static EligibilityRow Row(this EligibilityScreen screen, string startsWith) =>
        screen.Rows.Single(row => row.Label.StartsWith(startsWith, StringComparison.Ordinal));
}

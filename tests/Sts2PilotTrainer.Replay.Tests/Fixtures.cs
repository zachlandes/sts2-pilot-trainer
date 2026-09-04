using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// A minimal valid manifest, and helpers for breaking one rule at a time.
///
/// Every negative test starts from something the validator accepts and changes
/// exactly one thing. That is what makes a failure attributable: a fixture built
/// wrong in three ways proves only that the validator dislikes it.
/// </summary>
internal static class Fixtures
{
    internal static ReplayManifest ValidManifest() => new()
    {
        RunId = "test-run",
        Environment = new EnvironmentIdentity
        {
            BuildVersion = Fact<string>.Observed("v0.111.0", FactEvidence.AtVideoTime(1000, "overlay")),
            BuildDateUtc = Fact<string>.Observed("2026.08.14", FactEvidence.AtVideoTime(1000, "overlay")),
            GameMode = Fact<string>.Inferred("standard", FactEvidence.Reasoning("not date-formatted")),
            Seed = Fact<string>.Observed("SFXT47K77RFK", FactEvidence.AtVideoTime(1000, "overlay")),
            ContentHash = Fact<string>.Observed("1568834832", FactEvidence.AtVideoTime(1000, "overlay")),
            Ascension = Fact<int>.Observed(10, FactEvidence.AtVideoTime(1000, "badge")),
            Unlocks = Fact<UnlockRequirement>.Inferred(
                UnlockRequirement.Complete("experienced creator"), FactEvidence.Reasoning("not shown on screen")),
            Character = Fact<string>.Observed("CHARACTER.IRONCLAD", FactEvidence.AtVideoTime(1000, "sprite")),
            Acts = Fact<IReadOnlyList<string>>.Inferred(
                ["ACT.UNDERDOCKS"], FactEvidence.Reasoning("map screen title")),
            Mods = Fact<ModEnvironment>.Inferred(ModEnvironment(), FactEvidence.Reasoning("count observed, identities established elsewhere")),
        },
        Source = new SourceProvenance
        {
            Kind = "vod",
            Video = new VideoSource
            {
                Platform = "youtube",
                VideoId = "OJ-6QXhNgdg",
                ChannelId = "UCuuDxwofGcur0Lt6iP-aDww",
                ChannelName = "NaveGreed",
                DurationSeconds = 2049,
            },
            ExtractionMethod = "manual",
            Coverage = "opening turn only",
            RunStart = RunStart(),
            RunSummary = RunSummary(),
        },
        Actions =
        [
            new ActionRecord
            {
                Seq = 0,
                Verb = ActionVerb.ChooseNeowBlessing,
                Args = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["option_index"] = "2" },
                Source = FactSource.Observed,
                Evidence = FactEvidence.AtVideoTime(26000, "effect on max health"),
            },
            new ActionRecord
            {
                Seq = 1,
                Verb = ActionVerb.MapMove,
                Args = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["act"] = "0",
                    ["row"] = "1",
                    ["column"] = "3",
                },
                Source = FactSource.Observed,
                Evidence = FactEvidence.AtVideoTime(73500, "ringed node on the map"),
            },
        ],
        Checkpoints =
        [
            new Checkpoint
            {
                Id = "combat-start",
                AfterSeq = 1,
                Kind = "combat_start",
                Expect = new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
                {
                    ["combat.energy"] = Fact<string>.Observed("3", FactEvidence.AtVideoTime(75600, "energy orb")),
                },
            },
        ],
        Boundaries = [ReplayBoundary.CombatStart(1, 1, Fact<string>.Engine(Digest))],
    };

    /// <summary>A well-formed engine digest, so a test that is not about digest shape
    /// does not have to spell one out.</summary>
    internal const string Digest =
        "sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    internal static ReplayManifest SyntheticManifest() => SyntheticReplayFixture.Create();

    /// <summary>
    /// The engine-generated history of a whole act: nine fights, seventeen floor
    /// arrivals and the turns within them, all of it produced by the real game.
    ///
    /// Read through the shipped accessor rather than off disk, because a rule about
    /// later fights and later floors wants a history that really has them, and this is
    /// the one such history that owes nothing to anybody's video.
    /// </summary>
    internal static ReplayManifest WholeActManifest() => SyntheticReplayFixture.CreateWholeAct();

    /// <summary>
    /// A minimal valid recording made by this project's own recorder.
    ///
    /// The same run as <see cref="ValidManifest"/> and a different kind of claim about
    /// it: every value is what a recorder read out of the live game, so the evidence
    /// coordinate is the run's own ordered history rather than a public timestamp.
    /// </summary>
    internal static ReplayManifest NativeManifest()
    {
        var vod = ValidManifest();
        return vod with
        {
            RunId = "test-native-run",
            Environment = vod.Environment with
            {
                BuildVersion = Captured("v0.111.0", 0),
                BuildDateUtc = Captured("2026.08.14", 0),
                GameMode = Captured("standard", 0),
                Seed = Captured("SFXT47K77RFK", 0),
                ContentHash = Captured("1568834832", 0),
                Ascension = Fact<int>.Captured(10, FactEvidence.AtActionOrdinal(0)),
                Unlocks = Fact<UnlockRequirement>.Captured(
                    UnlockRequirement.Exact("read from the player's own profile", UnlockInventory()),
                    FactEvidence.AtActionOrdinal(0)),
                Character = Captured("CHARACTER.IRONCLAD", 0),
                Acts = Fact<IReadOnlyList<string>>.Captured(
                    ["ACT.UNDERDOCKS"], FactEvidence.AtActionOrdinal(0)),
                Mods = Fact<ModEnvironment>.Captured(NativeModEnvironment(), FactEvidence.AtActionOrdinal(0)),
            },
            Source = new SourceProvenance
            {
                Kind = "native",
                ExtractionMethod = "captured",
                Coverage = "the whole run, as it was played",
                Native = NativeSourceBlock(),
            },
            Actions = [.. vod.Actions.Select(action => action with
            {
                Source = FactSource.Captured,
                Evidence = FactEvidence.AtActionOrdinal(action.Seq, runClockMs: 1000 * (action.Seq + 1)),
            })],
            Checkpoints = [.. vod.Checkpoints.Select(checkpoint => checkpoint with
            {
                Expect = checkpoint.Expect.ToDictionary(
                    entry => entry.Key,
                    entry => Captured(entry.Value.Value, checkpoint.AfterSeq),
                    StringComparer.Ordinal),
            })],
        };
    }

    internal static NativeSource NativeSourceBlock(
        bool witnessedStart = true,
        string continuity = NativeSource.ContinuousContinuity,
        string outcome = "won") => new()
        {
            RecorderVersion = "runmobile-recorder/0.1.0",
            WitnessedRunStart = Fact<bool>.Captured(witnessedStart, FactEvidence.AtActionOrdinal(0)),
            Continuity = continuity,
            Outcome = outcome,
        };

    internal static UnlockStateInventory UnlockInventory() => new()
    {
        Epochs = ["EPOCH.ONE", "EPOCH.TWO"],
        EncountersSeen = ["ENCOUNTER.SLUDGE_SPINNER_WEAK", "ENCOUNTER.TEST"],
        Runs = 137,
    };

    internal static ModEnvironment NativeModEnvironment() => new()
    {
        Name = "the player's own game",
        ReportedCount = 1,
        Mods = [new InstalledMod("Runmobile", "the recorder itself", "reads only", AffectsGameplay: false)],
    };

    private static Fact<string> Captured(string value, int actionOrdinal) =>
        Fact<string>.Captured(value, FactEvidence.AtActionOrdinal(actionOrdinal));

    internal static ModEnvironment ModEnvironment(int reportedCount = 1) => new()
    {
        Name = "test-environment",
        ReportedCount = reportedCount,
        Mods = [new InstalledMod("Some Mod", "does a thing", "assessed as harmless for this test")],
    };

    internal static RunStartEvidence RunStart(
        int runTimeSeconds = 4, int floor = 1, bool fromHistory = false, bool modal = false) => new()
        {
            FirstObservedRunTimeSeconds = Fact<int>.Observed(runTimeSeconds, FactEvidence.AtVideoTime(9000, "run timer")),
            FirstObservedFloor = Fact<int>.Observed(floor, FactEvidence.AtVideoTime(9000, "floor counter")),
            EnteredFromRunHistory = Fact<bool>.Observed(fromHistory, FactEvidence.AtVideoTime(9000, "no history screen")),
            ResumeModalSeen = Fact<bool>.Observed(modal, FactEvidence.AtVideoTime(9000, "no resume dialog")),
        };

    internal static RunSummaryObservation RunSummary(
        string seed = "SFXT47K77RFK", string build = "v0.111.0", string date = "2026.08.14",
        string hash = "1568834832", int ascension = 10) => new()
        {
            VideoTimeMs = 2047000,
            Seed = Fact<string>.Observed(seed, FactEvidence.AtVideoTime(2047000, "overlay")),
            BuildVersion = Fact<string>.Observed(build, FactEvidence.AtVideoTime(2047000, "overlay")),
            BuildDateUtc = Fact<string>.Observed(date, FactEvidence.AtVideoTime(2047000, "overlay")),
            ContentHash = Fact<string>.Observed(hash, FactEvidence.AtVideoTime(2047000, "overlay")),
            Ascension = Fact<int>.Observed(ascension, FactEvidence.AtVideoTime(2047000, "summary line")),
            FloorsClimbed = Fact<int>.Observed(49, FactEvidence.AtVideoTime(2047000, "summary line")),
            PlayerMaxHp = Fact<int>.Observed(68, FactEvidence.AtVideoTime(2047000, "top bar")),
            DeckSize = Fact<int>.Observed(18, FactEvidence.AtVideoTime(2047000, "deck badge")),
            RelicCount = Fact<int>.Observed(12, FactEvidence.AtVideoTime(2047000, "relic bar")),
            NotShown = ["the game mode"],
        };

    internal static ActionRecord Action(int seq, ActionVerb verb, params (string Key, string Value)[] args)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in args) map[key] = value;
        return new ActionRecord
        {
            Seq = seq,
            Verb = verb,
            Args = map,
            Source = FactSource.Observed,
            Evidence = FactEvidence.AtVideoTime(1000 * (seq + 1), "test fixture"),
        };
    }
}

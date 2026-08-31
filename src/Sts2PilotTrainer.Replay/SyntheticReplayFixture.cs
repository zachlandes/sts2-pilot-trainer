namespace Sts2PilotTrainer.Replay;

public static class SyntheticReplayFixture
{
    public static ReplayManifest Create() => new()
    {
        RunId = "synthetic-v0111-first-combat",
        Environment = new EnvironmentIdentity
        {
            BuildVersion = Fact<string>.Declared("v0.111.0"),
            BuildDateUtc = Fact<string>.Declared("2026.08.14"),
            GameMode = Fact<string>.Declared("standard"),
            Seed = Fact<string>.Declared("SFXT47K77RFK"),
            ContentHash = Fact<string>.Declared("1568834832"),
            Ascension = Fact<int>.Declared(10),
            Character = Fact<string>.Declared("CHARACTER.IRONCLAD"),
            Acts = Fact<IReadOnlyList<string>>.Declared(
                ["ACT.UNDERDOCKS", "ACT.HIVE", "ACT.GLORY"]),
            Mods = Fact<ModEnvironment>.Declared(new ModEnvironment
            {
                Name = "vanilla-headless-v0.111.0",
                ReportedCount = 0,
                Mods = [],
            }),
        },
        Source = new SourceProvenance
        {
            Kind = "synthetic-engine",
            Synthetic = new SyntheticSource
            {
                FixtureId = "v0111-first-combat",
                FixtureVersion = 1,
                Generator = "sts2-pilot-trainer",
                GeneratedBuild = "v0.111.0",
            },
            ExtractionMethod = "engine-generated",
            Coverage = "Pinned first-combat engine fixture through turn two.",
        },
        Actions =
        [
            DeclaredAction(0, ActionVerb.ChooseNeowBlessing, ("option_index", "2")),
            DeclaredAction(1, ActionVerb.MapMove, ("row", "1"), ("column", "3")),
            DeclaredAction(2, ActionVerb.PlayCard,
                ("card_id", "CARD.HELLRAISER"), ("hand_index", "1")),
            DeclaredAction(3, ActionVerb.PlayCard,
                ("card_id", "CARD.DEFEND_IRONCLAD"), ("hand_index", "3")),
            DeclaredAction(4, ActionVerb.EndTurn),
        ],
        Checkpoints =
        [
            EngineCheckpoint("combat-start", 1,
                ("combat.turn", "1"),
                ("combat.energy", "3"),
                ("combat.max_energy", "3"),
                ("combat.block", "0"),
                ("combat.player_hp", "64"),
                ("player.max_hp", "68"),
                ("combat.hand", "CARD.STRIKE_IRONCLAD|CARD.HELLRAISER|CARD.STRIKE_IRONCLAD|CARD.BASH|CARD.DEFEND_IRONCLAD"),
                ("combat.draw_pile_count", "6"),
                ("combat.discard_pile_count", "0"),
                ("combat.enemy_count", "1"),
                ("combat.enemy.0.hp", "42"),
                ("combat.enemy.0.max_hp", "42"),
                ("combat.enemy.0.intent", "Attack:9+Debuff")),
            EngineCheckpoint("after-hellraiser", 2,
                ("combat.energy", "1"), ("combat.hand_count", "4")),
            EngineCheckpoint("after-defend", 3,
                ("combat.energy", "0"), ("combat.block", "5"), ("combat.hand_count", "3")),
            EngineCheckpoint("turn-two", 4,
                ("combat.turn", "2"), ("combat.player_hp", "60")),
        ],
    };

    private static ActionRecord DeclaredAction(
        int seq, ActionVerb verb, params (string Key, string Value)[] args) => new()
        {
            Seq = seq,
            Verb = verb,
            Args = new SortedDictionary<string, string>(
                args.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                StringComparer.Ordinal),
            Source = FactSource.Declared,
        };

    private static Checkpoint EngineCheckpoint(
        string id, int afterSeq, params (string Field, string Value)[] expected) => new()
        {
            Id = id,
            AfterSeq = afterSeq,
            Kind = "synthetic-engine",
            Expect = expected.ToDictionary(
                pair => pair.Field, pair => Fact<string>.Engine(pair.Value), StringComparer.Ordinal),
        };
}

using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

public static class SyntheticFixtureGenerator
{
    public static ReplayManifest Generate()
    {
        var identity = GameIdentity.Read();
        if (identity.BuildVersion != "v0.111.0")
        {
            throw new EngineException(
                $"Synthetic fixture generation supports v0.111.0, not {identity.BuildVersion}.");
        }
        const string seed = "P1L0TTRA1NER";
        string[] acts = ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"];
        var session = new GameSession();
        session.StartRun(seed, "CHARACTER.IRONCLAD", 0, "standard", acts);
        var driver = new RunDriver(session);
        driver.EnterFirstRoom();

        var actions = new List<ActionRecord>();
        var checkpoints = new List<Checkpoint>();
        Apply(driver, actions, ActionVerb.ChooseNeowBlessing, ("option_index", "0"));

        var state = CanonicalStateProjection.Project(session.RunState);
        var current = ParseCoord(state.Fields["run.map_coord"]);
        var edge = session.CurrentMapTopology().Edges
            .Where(candidate => candidate.FromRow == current.Row && candidate.FromColumn == current.Column)
            .OrderBy(candidate => candidate.ToColumn)
            .First();
        Apply(driver, actions, ActionVerb.MapMove,
            ("act", session.RunState.CurrentActIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("row", edge.ToRow.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("column", edge.ToColumn.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        checkpoints.Add(Capture("combat-start", actions[^1].Seq, session,
            "combat.turn", "combat.energy", "combat.player_hp", "combat.hand",
            "combat.enemy.0.model", "combat.enemy.0.hp", "combat.enemy.0.intent"));

        var setupCard = PlayPreservingAlternatives(driver, session, actions);
        checkpoints.Add(Capture("after-" + setupCard.ToLowerInvariant(), actions[^1].Seq, session,
            "combat.energy", "combat.block", "combat.hand_count", "combat.enemy.0.hp"));

        PlayWithSubstitute(
            driver, session, actions, "CARD.STRIKE_IRONCLAD", "CARD.DEFEND_IRONCLAD");
        checkpoints.Add(Capture("after-strike", actions[^1].Seq, session,
            "combat.energy", "combat.enemy.0.hp", "combat.hand_count"));

        Apply(driver, actions, ActionVerb.EndTurn);
        checkpoints.Add(Capture("turn-two", actions[^1].Seq, session,
            "combat.turn", "combat.player_hp", "combat.hand", "combat.draw_pile_count",
            "combat.discard_pile", "combat.enemy.0.hp"));

        return new ReplayManifest
        {
            RunId = "synthetic-v0111-pilot-trainer",
            Environment = new EnvironmentIdentity
            {
                BuildVersion = Fact<string>.Declared(identity.BuildVersion),
                BuildDateUtc = Fact<string>.Declared(identity.BuildDateUtc),
                GameMode = Fact<string>.Declared("standard"),
                Seed = Fact<string>.Declared(seed),
                ContentHash = Fact<string>.Declared(identity.ContentHash),
                Ascension = Fact<int>.Declared(0),
                Character = Fact<string>.Declared("CHARACTER.IRONCLAD"),
                Acts = Fact<IReadOnlyList<string>>.Declared(acts),
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
                    FixtureId = "v0111-pilot-trainer",
                    FixtureVersion = 1,
                    Generator = "sts2-pilot-trainer",
                    GeneratedBuild = identity.BuildVersion,
                },
                ExtractionMethod = "engine-generated",
                Coverage = "Mechanically generated first-combat fixture through turn two.",
            },
            Actions = actions,
            Checkpoints = checkpoints,
        };
    }

    private static string PlayPreservingAlternatives(
        RunDriver driver, GameSession session, List<ActionRecord> actions)
    {
        var hand = CanonicalStateProjection.Project(session.RunState).Fields["combat.hand"].Split('|');
        var strikeCount = hand.Count(card => card == "CARD.STRIKE_IRONCLAD");
        var defendCount = hand.Count(card => card == "CARD.DEFEND_IRONCLAD");
        var cardId = defendCount > 1
            ? "CARD.DEFEND_IRONCLAD"
            : strikeCount > 1
                ? "CARD.STRIKE_IRONCLAD"
                : throw new EngineException(
                    "Synthetic fixture hand cannot preserve a Strike/Defend substitution pair.");
        var index = FindCard(hand, cardId);
        Apply(driver, actions, ActionVerb.PlayCard,
            ("card_id", cardId),
            ("hand_index", index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        return cardId[5..].Replace("_IRONCLAD", "", StringComparison.Ordinal);
    }

    private static void PlayWithSubstitute(
        RunDriver driver, GameSession session, List<ActionRecord> actions,
        string cardId, string substituteCardId)
    {
        var hand = CanonicalStateProjection.Project(session.RunState).Fields["combat.hand"].Split('|');
        var index = FindCard(hand, cardId);
        var substituteIndex = FindCard(hand, substituteCardId);
        Apply(driver, actions, ActionVerb.PlayCard,
            ("card_id", cardId),
            ("hand_index", index.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            ("negative_control_substitute_card_id", substituteCardId),
            ("negative_control_substitute_hand_index",
                substituteIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static int FindCard(string[] hand, string cardId)
    {
        var index = Array.FindIndex(hand, card => card == cardId);
        return index >= 0
            ? index
            : throw new EngineException($"Synthetic fixture hand has no {cardId}.");
    }

    private static void Apply(
        RunDriver driver, List<ActionRecord> actions, ActionVerb verb,
        params (string Key, string Value)[] args)
    {
        var action = new ActionRecord
        {
            Seq = actions.Count,
            Verb = verb,
            Args = new SortedDictionary<string, string>(
                args.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                StringComparer.Ordinal),
            Source = FactSource.Declared,
        };
        driver.Apply(action);
        actions.Add(action);
    }

    private static Checkpoint Capture(
        string id, int afterSeq, GameSession session, params string[] fields)
    {
        var state = CanonicalStateProjection.Project(session.RunState);
        return new Checkpoint
        {
            Id = id,
            AfterSeq = afterSeq,
            Kind = "synthetic-engine",
            Expect = fields.ToDictionary(
                field => field,
                field => Fact<string>.Engine(state.Fields[field]),
                StringComparer.Ordinal),
        };
    }

    private static (int Row, int Column) ParseCoord(string value)
    {
        var separator = value.IndexOf('c');
        return (
            int.Parse(value.AsSpan(1, separator - 1), System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(value.AsSpan(separator + 1), System.Globalization.CultureInfo.InvariantCulture));
    }
}

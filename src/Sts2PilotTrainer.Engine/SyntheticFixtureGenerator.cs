using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Which line the generated fixture plays once the fight starts.
///
/// Two lines exist because a comparison contract with only one fight to compute over
/// has not been exercised at all. Both are mechanical rules over the hand the engine
/// deals, not judgements about play: neither is better and the fixture says nothing
/// about which is.
/// </summary>
public enum CombatLine
{
    /// <summary>The reference line: the first playable card in hand order, every
    /// turn. Its opening two plays are fixed so that a Strike/Defend substitution
    /// pair survives for the negative controls.</summary>
    Reference,

    /// <summary>The same fight played the other way round the hand: the last playable
    /// card in hand order, from the first turn.</summary>
    Alternate,
}

public static class SyntheticFixtureGenerator
{
    /// <summary>How many turns a generated fight may take before the generator gives
    /// up. A fixture that never resolves is a defect in the generator or the host, and
    /// silently emitting an unfinished fight would hide it.</summary>
    private const int TurnLimit = 40;

    public static ReplayManifest Generate() => Generate(CombatLine.Reference);

    public static ReplayManifest Generate(CombatLine line)
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

        if (line == CombatLine.Reference)
        {
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
        }

        PlayToTheEndOfTheFight(driver, session, actions, line);
        checkpoints.Add(Capture("combat-complete", actions[^1].Seq, session,
            "combat.outcome", "combat.in_progress", "combat.turn", "player.hp",
            "combat.enemy_count", "run.act_floor"));

        return new ReplayManifest
        {
            RunId = line == CombatLine.Reference
                ? "synthetic-v0111-pilot-trainer"
                : "synthetic-v0111-pilot-trainer-alternate",
            Environment = new EnvironmentIdentity
            {
                BuildVersion = Fact<string>.Declared(identity.BuildVersion),
                BuildDateUtc = Fact<string>.Declared(identity.BuildDateUtc),
                GameMode = Fact<string>.Declared("standard"),
                Seed = Fact<string>.Declared(seed),
                ContentHash = Fact<string>.Declared(identity.ContentHash),
                Ascension = Fact<int>.Declared(0),
                Unlocks = Fact<UnlockRequirement>.Declared(UnlockRequirement.Complete(
                    "Generated by this arbiter against UnlockState.all, so the requirement is a property of " +
                    "how the fixture was produced rather than a claim about any player.")),
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
                    FixtureId = line == CombatLine.Reference
                        ? "v0111-pilot-trainer"
                        : "v0111-pilot-trainer-alternate",
                    // Bumped when the fixture stopped after the opening turn and
                    // started playing the fight to its end.
                    FixtureVersion = 2,
                    Generator = "sts2-pilot-trainer",
                    GeneratedBuild = identity.BuildVersion,
                },
                ExtractionMethod = "engine-generated",
                Coverage = line == CombatLine.Reference
                    ? "Mechanically generated first combat, opened to preserve a substitution pair and " +
                      "then played to the end of the fight."
                    : "Mechanically generated first combat, played to the end of the fight from the other " +
                      "end of the hand.",
            },
            Actions = actions,
            Checkpoints = checkpoints,
        };
    }

    /// <summary>
    /// Plays the fight out until the engine says it is over.
    ///
    /// The unit of work is the whole fight, so a fixture that stops while the combat is
    /// still running proves the boundary and not the thing the comparison is computed
    /// over. Which card gets played is a mechanical rule over the hand the engine
    /// dealt - deliberately not a judgement, because the fixture is here to exercise
    /// the machinery and must not read as a claim about how to play.
    ///
    /// The loop reads the engine's own combat lifecycle through
    /// <c>combat.outcome</c> rather than counting turns: the fight ends when the
    /// engine says it has, and the turn limit below is a guard against a host that
    /// never gets there, not the exit condition.
    /// </summary>
    private static void PlayToTheEndOfTheFight(
        RunDriver driver, GameSession session, List<ActionRecord> actions, CombatLine line)
    {
        for (var turn = 0; turn < TurnLimit; turn++)
        {
            while (Outcome(session) == "in_progress")
            {
                var hand = CanonicalStateProjection.Project(session.RunState).Fields["combat.hand"];
                var index = PlayableIndex(session, line);
                if (index < 0) break;

                Apply(driver, actions, ActionVerb.PlayCard,
                    ("card_id", hand.Split('|')[index]),
                    ("hand_index", index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }

            if (Outcome(session) != "in_progress") return;
            Apply(driver, actions, ActionVerb.EndTurn);
        }

        throw new EngineException(
            $"The generated fight was still running after {TurnLimit} turns. Refusing to emit a fixture " +
            "whose combat never finishes: every quantity the comparison computes is defined over a " +
            "completed fight.");
    }

    /// <summary>
    /// The hand index this line would play, or -1 when nothing in hand can be played.
    ///
    /// Asks the engine whether each card is playable rather than reasoning about energy
    /// here, so the rule cannot drift away from what the game would actually allow.
    /// </summary>
    private static int PlayableIndex(GameSession session, CombatLine line)
    {
        var hand = session.RunState.Players[0].PlayerCombatState?.Hand.Cards;
        if (hand is null) return -1;

        var order = line == CombatLine.Reference
            ? Enumerable.Range(0, hand.Count)
            : Enumerable.Range(0, hand.Count).Reverse();
        foreach (var index in order)
        {
            if (hand[index].CanPlay(out _, out _)) return index;
        }
        return -1;
    }

    private static string Outcome(GameSession session) =>
        CanonicalStateProjection.Project(session.RunState).Fields["combat.outcome"];

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

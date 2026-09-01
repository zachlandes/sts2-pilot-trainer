using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Projects a live run into the canonical state the arbiter compares.
///
/// An explicit allowlist, field by field. Nothing is serialised wholesale and
/// filtered afterwards, so a field that appears in a future build cannot slip into
/// the digest unnoticed - it simply will not be here until someone decides it
/// belongs. See <see cref="CanonicalState.ExcludedByDesign"/> for what is kept out
/// and why.
///
/// The most important entries are the ones a video can never show: the position of
/// every run-persistent RNG stream, and the order of the draw pile. Those are the
/// state that makes exact replay necessary in the first place, and a digest that
/// omitted them could agree while the runs had already diverged.
/// </summary>
public static class CanonicalStateProjection
{
    private static readonly BindingFlags NonPublicInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>The run's RNG streams, in a fixed order so the digest is stable.</summary>
    private static readonly string[] RunStreams =
    [
        "UpFront", "Shuffle", "MonsterAi", "CombatCardGeneration", "CombatCardSelection",
        "CombatEnergyCosts", "CombatOrbGeneration", "CombatPotionGeneration", "CombatTargets",
        "UnknownMapPoint", "TreasureRoomRelics", "Niche",
    ];

    private static readonly string[] PlayerStreams = ["Rewards", "Shops", "Transformations"];

    public static CanonicalState Project(RunState run)
    {
        var builder = CanonicalState.Build();
        var player = run.Players[0];

        ProjectRun(builder, run);
        ProjectRunRng(builder, run);
        ProjectActContent(builder, run);
        ProjectPlayer(builder, player);
        ProjectCombat(builder, player);

        return builder.ToState();
    }

    private static void ProjectRun(CanonicalState.Builder builder, RunState run)
    {
        builder.Add("run.game_mode", run.GameMode.ToString());
        builder.Add("run.ascension", run.AscensionLevel);
        builder.Add("run.act_index", run.CurrentActIndex);
        builder.Add("run.act_floor", run.ActFloor);
        builder.Add("run.total_floor", run.TotalFloor);
        builder.Add("run.seed", run.Rng.StringSeed);
        builder.Add("run.map_coord", run.CurrentMapCoord is { } coord ? $"r{coord.row}c{coord.col}" : "none");
        builder.Add("run.is_game_over", run.IsGameOver);
        // Enemy health is scaled by this model. It is run identity, not presentation:
        // the same encounter under a different scaling model is a different fight.
        // Which acts this run is made of. The game ships more than one act per index,
        // and two runs on the same seed through different act variants generate
        // entirely different content while producing the same map - so the act list
        // is identity, not configuration.
        builder.AddSequence("run.acts", run.Acts.Select(a => $"{a.Index}:{a.Id}"));
        builder.Add("run.multiplayer_scaling", run.MultiplayerScalingModel?.Id.ToString() ?? "none");
    }

    /// <summary>
    /// Every run-persistent stream's position.
    ///
    /// This is the hidden state the whole project exists to reproduce. It is not
    /// observable from any video at any resolution, it persists across the whole run,
    /// and it is what makes "the same seed" insufficient. A canonical state without
    /// it would let two genuinely different runs produce the same digest.
    /// </summary>
    private static void ProjectRunRng(CanonicalState.Builder builder, RunState run)
    {
        foreach (var name in RunStreams)
        {
            var stream = typeof(RunRngSet).GetProperty(name)?.GetValue(run.Rng) as Rng;
            builder.Add($"run.rng.{name}", Counter(stream));
        }
    }

    /// <summary>
    /// The act's generated room set: the ordered encounters and events this run will
    /// meet, plus how many have been consumed.
    ///
    /// Worth its place in the canonical state because it is where a generation
    /// divergence becomes visible first. Without it, two runs that generated
    /// different content look identical until the player walks into a fight, and the
    /// report then says "the enemy is wrong" rather than "the content list is wrong".
    /// </summary>
    private static void ProjectActContent(CanonicalState.Builder builder, RunState run)
    {
        var act = run.Acts[run.CurrentActIndex];
        var rooms = act.GetType().GetField("_rooms", NonPublicInstance)?.GetValue(act);
        if (rooms is null)
        {
            builder.Add("act.room_set", "unavailable");
            return;
        }

        foreach (var (field, label) in new[]
                 {
                     ("normalEncounters", "normal_encounters"),
                     ("eliteEncounters", "elite_encounters"),
                     ("events", "events"),
                 })
        {
            var list = rooms.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(rooms) as System.Collections.IEnumerable;
            builder.AddSequence(
                $"act.{label}",
                list?.Cast<object>().Select(Identify) ?? []);
        }

        foreach (var (field, label) in new[]
                 {
                     ("normalEncountersVisited", "normal_encounters_visited"),
                     ("eliteEncountersVisited", "elite_encounters_visited"),
                     ("eventsVisited", "events_visited"),
                 })
        {
            var value = rooms.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(rooms);
            builder.Add($"act.{label}", value?.ToString() ?? "unknown");
        }
    }

    private static string Identify(object model) =>
        model.GetType().GetProperty("Id")?.GetValue(model)?.ToString() ?? model.GetType().Name;

    private static void ProjectPlayer(CanonicalState.Builder builder, Player player)
    {
        builder.Add("player.character", player.Character.Id.ToString());
        builder.Add("player.gold", player.Gold);
        builder.Add("player.max_energy", player.MaxEnergy);
        builder.Add("player.hp", player.Creature?.CurrentHp ?? -1);
        builder.Add("player.max_hp", player.Creature?.MaxHp ?? -1);

        // Deck order is part of the state, not a presentation detail: the shuffle
        // stream turns it into draw order.
        builder.AddSequence("player.deck", player.Deck.Cards.Select(Describe));

        // The same deck counted rather than listed. Redundant against the list and
        // here anyway, because it is the one deck fact a recording actually shows:
        // the badge in the top bar carries it on every frame, while the ordered deck
        // is not readable from the deck screen, which sorts. A checkpoint that could
        // only compare the ordered list would have nothing to say about the deck at
        // any moment the video did not open a screen.
        builder.Add("player.deck_count", player.Deck.Cards.Count);
        builder.AddSequence("player.relics", player.Relics.Select(r => r.Id.ToString()));
        builder.AddSequence("player.potions", player.PotionSlots.Select(slot => slot?.Id.ToString() ?? "empty"));

        foreach (var name in PlayerStreams)
        {
            var stream = player.PlayerRng.GetType().GetProperty(name)?.GetValue(player.PlayerRng) as Rng;
            builder.Add($"player.rng.{name}", Counter(stream));
        }
    }

    private static void ProjectCombat(CanonicalState.Builder builder, Player player)
    {
        var combat = player.PlayerCombatState;
        if (combat is null)
        {
            builder.Add("combat.in_progress", false);
            builder.Add("combat.outcome", "none");
            return;
        }

        var outcome = CombatOutcome(player);
        builder.Add("combat.in_progress", outcome == "in_progress");
        builder.Add("combat.outcome", outcome);
        builder.Add("combat.turn", combat.TurnNumber);
        builder.Add("combat.phase", combat.Phase.ToString());
        builder.Add("combat.energy", combat.Energy);
        builder.Add("combat.max_energy", combat.MaxEnergy);

        // Ordered, all of them. Draw-pile order in particular is the single most
        // consequential thing a video cannot show.
        builder.AddSequence("combat.hand", combat.Hand.Cards.Select(Describe));
        builder.AddSequence("combat.draw_pile", combat.DrawPile.Cards.Select(Describe));
        builder.AddSequence("combat.discard_pile", combat.DiscardPile.Cards.Select(Describe));
        builder.AddSequence("combat.exhaust_pile", combat.ExhaustPile.Cards.Select(Describe));
        builder.AddSequence("combat.play_pile", combat.PlayPile.Cards.Select(Describe));
        builder.Add("combat.hand_count", combat.Hand.Cards.Count);
        builder.Add("combat.draw_pile_count", combat.DrawPile.Cards.Count);
        builder.Add("combat.discard_pile_count", combat.DiscardPile.Cards.Count);

        var creature = player.Creature;
        builder.Add("combat.block", creature?.Block ?? -1);
        builder.Add("combat.player_hp", creature?.CurrentHp ?? -1);
        builder.AddSequence("combat.player_powers", Powers(creature));

        var state = CombatManager.Instance.DebugOnlyGetState();
        if (state is null)
        {
            builder.Add("combat.enemy_count", 0);
            return;
        }

        builder.Add("combat.round", state.RoundNumber);
        builder.Add("combat.encounter", state.Encounter?.Id.ToString() ?? "none");

        var enemies = state.Enemies.Where(e => e is not null).ToList();
        builder.Add("combat.enemy_count", enemies.Count);
        for (var i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            builder.Add($"combat.enemy.{i}.model", enemy.ModelId.ToString());
            builder.Add($"combat.enemy.{i}.hp", enemy.CurrentHp);
            builder.Add($"combat.enemy.{i}.max_hp", enemy.MaxHp);
            // What the monster rolled before scaling was applied. Recorded separately
            // so a health mismatch says whether the roll or the scaling differed.
            builder.Add($"combat.enemy.{i}.max_hp_unscaled",
                enemy.MonsterMaxHpBeforeModification?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none");
            builder.Add($"combat.enemy.{i}.block", enemy.Block);
            builder.Add($"combat.enemy.{i}.alive", enemy.IsAlive);
            builder.AddSequence($"combat.enemy.{i}.powers", Powers(enemy));
            builder.Add($"combat.enemy.{i}.next_move", enemy.Monster?.NextMove?.StateId ?? "none");
            builder.Add($"combat.enemy.{i}.intent", DescribeIntent(enemy, creature));
        }
    }

    /// <summary>
    /// Whether the fight is still running, and if it is not, how it ended.
    ///
    /// Read from the combat manager rather than from the player's combat state,
    /// because that state outlives the fight: once the last enemy dies it is still
    /// there, holding the final hand and pile order, with its turn phase set to None.
    /// Asking it whether a combat is in progress therefore reports a finished fight as
    /// an active one - which is exactly the reading that would let a whole-combat
    /// comparison compute total turns, net health change and final health over a fight
    /// that had not finished.
    ///
    /// The finished fight's other combat fields are still projected, on purpose. The
    /// last frame of a fight is part of its result, and dropping it the moment the
    /// fight ended would throw away the end of every quantity the comparison needs.
    /// </summary>
    private static string CombatOutcome(Player player)
    {
        var manager = CombatManager.Instance
            ?? throw new EngineException(
                "The player is in a combat state but this build exposes no CombatManager, so whether the " +
                "fight is still running cannot be read. Refusing: a finished fight reported as an active " +
                "one is precisely the error this field exists to prevent.");

        if (manager.IsInProgress) return "in_progress";

        if (player.Creature is { IsAlive: false }) return "defeat";

        // The engine takes a dead enemy out of the combat state rather than leaving it
        // there at zero health, so a won fight ends with no enemies at all. "No living
        // enemy" therefore has to cover the empty list, and it is reached only after
        // the combat manager has already said the fight is over.
        var enemies = manager.DebugOnlyGetState()?.Enemies.Where(enemy => enemy is not null).ToList() ?? [];
        if (enemies.TrueForAll(enemy => !enemy.IsAlive)) return "victory";

        // A fight that stopped with the player and an enemy both alive is a real
        // engine state and not one this milestone has seen. It is named rather than
        // folded into victory, because a comparison computed over a fight nobody can
        // characterise should say so rather than pick the flattering reading.
        return "ended";
    }

    /// <summary>
    /// The enemy's telegraphed intent, in the form the player sees it: the intent
    /// kind and, for an attack, the damage number rendered above the enemy. That
    /// number is what a video shows, which is what makes it checkable.
    /// </summary>
    private static string DescribeIntent(Creature enemy, Creature? target)
    {
        var intents = enemy.Monster?.NextMove?.Intents;
        if (intents is null || intents.Count == 0) return "none";

        var targets = target is null ? Array.Empty<Creature>() : [target];
        return string.Join("+", intents.Select(intent => intent switch
        {
            AttackIntent attack => $"Attack:{attack.GetTotalDamage(targets, enemy)}",
            _ => intent.IntentType.ToString(),
        }));
    }

    private static IEnumerable<string> Powers(Creature? creature) =>
        creature is null
            ? []
            : creature.Powers
                .Select(p => $"{p.Id}:{p.Amount}")
                .OrderBy(s => s, StringComparer.Ordinal);

    /// <summary>Card identity for canonical purposes: model id plus upgrade level.
    /// Deliberately not the display name, which is localized and would make the
    /// digest depend on the reader's language.</summary>
    private static string Describe(CardModel card)
    {
        var id = card.Id.ToString();
        if (card.CurrentUpgradeLevel > 0) id += "+" + card.CurrentUpgradeLevel;
        if (card.Enchantment is { } enchantment) id += "@" + enchantment.Id;
        return id;
    }

    /// <summary>
    /// An RNG stream's position. Read from the private counter because that is where
    /// the game keeps it; the position is the whole point, and a stream reported
    /// without one would be a field that always agrees.
    /// </summary>
    private static int Counter(Rng? stream)
    {
        if (stream is null) return -1;
        var field = typeof(Rng).GetField("_counter", NonPublicInstance)
            ?? throw new EngineException(
                "Rng._counter is absent from this build, so RNG stream positions cannot be read. " +
                "Refusing: a canonical state without them would compare two runs on everything " +
                "except the thing that actually distinguishes them.");
        return (int)field.GetValue(stream)!;
    }
}

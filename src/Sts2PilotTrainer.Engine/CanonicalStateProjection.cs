using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
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
        ProjectCombat(builder, run, player);

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

    /// <summary>
    /// The fight, while one is live; outside one, only that none is, and how the last
    /// one ended where the run is still standing in it.
    ///
    /// A finished fight stays on the player until the next fight replaces it -
    /// <c>PlayerCombatState</c> is reset at the next <c>SetUpCombat</c>, not at the
    /// end of the fight - and the game's own save carries no combat at all. So a run
    /// continued from the fight-won save stands on the same loot screen with a fresh
    /// combat state at turn 1, and one continued from an arrival save stands on the
    /// same floor with none, while the engine replaying the same history carries the
    /// fight as it was fought. Projecting that residue put every reading taken at a
    /// shop, a rest site, an event or a loot screen after a fight into the digest,
    /// and a Save and Quit there - the most ordinary thing a player does - made the
    /// recording fail reproduction at its next arrival with every decision in it
    /// individually true. Outside a live fight there is no fight to describe, so
    /// nothing of one is projected: the two hosts read the same state because the
    /// state they read is the same.
    ///
    /// Whether a fight is live is the combat manager's word and not the player's
    /// state, because that state outlives the fight, and asking it would report a
    /// finished fight as an active one - the reading that would let a whole-combat
    /// comparison compute total turns over a fight that had not finished.
    ///
    /// One reading of a finished fight is taken, and kept out of the digest: which
    /// side's turn it ended in, off the combat state the manager still holds in the
    /// fight's own room. A trace needs it because the step that ended the fight
    /// samples no roster afterwards, and an enemy leaves a fight alive only during its
    /// own side's turn - so a fight that ended on the player's side was ended by a
    /// kill, and one that ended on the enemy's may have been won by a flight. The
    /// game's save carries no combat state, so a run continued onto the loot screen
    /// has no such reading and the digest, which both have to agree on, never hashes
    /// it; <see cref="CanonicalState.OutsideTheDigest"/> owns that line.
    /// </summary>
    private static void ProjectCombat(CanonicalState.Builder builder, RunState run, Player player)
    {
        var manager = CombatManager.Instance;
        if (manager is null && player.PlayerCombatState is not null)
        {
            throw new EngineException(
                "The player is in a combat state but this build exposes no CombatManager, so whether the " +
                "fight is still running cannot be read. Refusing: a finished fight reported as an active " +
                "one is precisely the error this field exists to prevent.");
        }

        var combat = player.PlayerCombatState;
        if (manager is not { IsInProgress: true } || combat is null)
        {
            var outcome = OutcomeOutsideALiveFight(run, player);
            builder.Add("combat.in_progress", false);
            builder.Add("combat.outcome", outcome);
            if (outcome != "none" && manager?.DebugOnlyGetState() is { } ended)
            {
                builder.AddOutsideTheDigest(EndedOnSideField, ended.CurrentSide == CombatSide.Player ? "player" : "enemy");
            }
            return;
        }

        builder.Add("combat.in_progress", true);
        builder.Add("combat.outcome", "in_progress");
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

        var state = manager.DebugOnlyGetState();
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
    /// How the last fight ended, read from where the run is standing rather than
    /// from what the fight left behind.
    ///
    /// <c>defeat</c> is a player who is dead: no save carries one and no restore
    /// produces one, so it is the run's own fact. <c>victory</c> is a run still in the
    /// room of a fight the engine ended, which the room says of itself: the engine
    /// marks a combat room pre-finished when its combat ends, whatever ended it, and
    /// the game's save carries that mark - it is what puts the loot screen back on a
    /// Continue - so a run played through the fight and a run restored onto its loot
    /// screen say the same thing. Leaving the room is what takes the mark out of
    /// the reading, on both, and from then on the answer is <c>none</c>. The finished
    /// fight's own last frame is still sampled: the killing action's after-reading is
    /// taken once the engine has settled, by which time the room is marked, so the
    /// comparison's final frame keeps its outcome, and everything else it derives
    /// comes from the player, whose health, deck and potions are projected whatever
    /// the room.
    ///
    /// <c>victory</c> is the game's own word for that mark and not this projection's.
    /// On v0.111.0 the combat manager stops in exactly three ways: <c>EndCombatInternal</c>,
    /// which marks the room, saves the run and raises <c>CombatWon</c>; a pending loss,
    /// which is the dead player above; and the reset that leaving the room performs.
    /// The first is taken once no living enemy is primary - a minion is not, so a
    /// fight is won with one still standing, and the game awards its rewards, its
    /// progress and its save exactly as it does when the roster is empty. The
    /// <c>ended</c> reading an older projection gave that fight was its own invention
    /// over residue the save does not carry: a restore onto the loot screen
    /// regenerates the encounter's monsters at full health, so what stood at the end
    /// is not a fact the two hosts can agree on, and no reading is derived from it.
    /// Which enemies a fight can end around is answered where a comparison needs it,
    /// off the live roster before the killing action, and refused there rather than
    /// guessed.
    /// </summary>
    /// <summary>The trace-only field naming the side whose turn a finished fight
    /// ended in: <c>player</c> or <c>enemy</c>.</summary>
    public const string EndedOnSideField = ReplayTrace.EndedOnSideField;

    private static string OutcomeOutsideALiveFight(RunState run, Player player)
    {
        if (player.Creature is { IsAlive: false }) return "defeat";
        if (run.CurrentRoom is CombatRoom { IsPreFinished: true }) return "victory";
        return "none";
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
    internal static int Counter(Rng? stream)
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

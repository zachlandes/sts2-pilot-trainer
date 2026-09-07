using System.Globalization;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// What one relic of a run is, as the strip and the pane read it.
/// </summary>
/// <param name="Id">The relic's model id, which is what a host resolves art and a
/// tooltip from. Nothing here translates it: a display name is the game's own
/// answer and this layer has no model database.</param>
/// <param name="FoundAt">Which of the run's proved positions this relic first appears
/// at, counting from zero, so the strip can order by when it was found. A relic the
/// run began with is at zero.</param>
public sealed record RunRelic(string Id, int FoundAt);

/// <summary>One card of a deck and how many of it there are.</summary>
public sealed record DeckTile(string CardId, int Count);

/// <summary>
/// What a recording's own checkpoints say at one place in the run: the deck, the
/// relics, the player's health, and the fight if one starts there.
///
/// <para><b>A reading, and never a computation.</b> Every value is a field a checkpoint
/// carries and this parses it; nothing is derived, averaged, scored or filled in. A
/// field a checkpoint does not carry is null here, and the surface then draws a gap
/// rather than a zero - the same rule <c>docs/comparison-direction.md</c> holds for the
/// fight result, adopted here because it is the same kind of claim.</para>
/// </summary>
/// <param name="Deck">The deck at this position's start, one entry per distinct card
/// with its count, in the order the deck holds them. Null where the checkpoint says
/// nothing about it.</param>
/// <param name="Relics">The relics carried here, in the order the run found them.</param>
/// <param name="Hp">Health here, and the maximum, or null where nothing said.</param>
/// <param name="Encounter">The fight's encounter id where a fight starts here.</param>
/// <param name="Enemies">The enemies of that fight, in the order the checkpoint
/// numbers them.</param>
public sealed record RunReading(
    IReadOnlyList<DeckTile>? Deck,
    IReadOnlyList<string> Relics,
    int? Hp,
    int? MaxHp,
    string? Encounter,
    IReadOnlyList<RunEnemy> Enemies)
{
    /// <summary>What a position nobody recorded a checkpoint at reads as: nothing, and
    /// nothing invented.</summary>
    public static RunReading Nothing => new(null, [], null, null, null, []);

    /// <summary>How many cards the deck holds, or null where the checkpoint said
    /// nothing about it. A count is a sum of the tiles rather than a second reading, so
    /// the number beside the deck and the deck itself cannot disagree.</summary>
    public int? DeckCount => Deck?.Sum(tile => tile.Count);

    /// <summary>
    /// What the recording's checkpoints say at one action of the history.
    ///
    /// The checkpoint at that action, whichever kind it is: a combat start and the
    /// floor entry that opened it are the same moment and carry the same fields, so
    /// asking for one kind would leave the other's position unread.
    /// </summary>
    public static RunReading At(ReplayManifest recording, int afterSeq)
    {
        var fields = recording.Checkpoints
            .Where(checkpoint => checkpoint.AfterSeq == afterSeq)
            .SelectMany(checkpoint => checkpoint.Expect)
            .GroupBy(field => field.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value.Value, StringComparer.Ordinal);
        if (fields.Count == 0) return Nothing;

        return new RunReading(
            Sequence(fields, "player.deck") is { } deck ? Tiles(deck) : null,
            Sequence(fields, "player.relics") ?? [],
            Number(fields, "player.hp"),
            Number(fields, "player.max_hp"),
            Text(fields, "combat.encounter"),
            EnemiesIn(fields));
    }

    /// <summary>
    /// Every relic the run ever carried, in the order it found them, with the position
    /// each first appears at.
    ///
    /// Read across the recording's own checkpoints in history order: a relic is found
    /// when it first appears in one, which is what "the order found" means for a
    /// recording nobody watched being played. A run whose checkpoints say nothing about
    /// relics has none here rather than an empty claim about a run with relics in it.
    /// </summary>
    public static IReadOnlyList<RunRelic> RelicsFound(ReplayManifest recording)
    {
        var found = new List<RunRelic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var position = 0;
        foreach (var checkpoint in recording.Checkpoints.OrderBy(checkpoint => checkpoint.AfterSeq))
        {
            if (!checkpoint.Expect.TryGetValue("player.relics", out var relics)) continue;

            foreach (var relic in Split(relics.Value))
            {
                if (seen.Add(relic)) found.Add(new RunRelic(relic, position));
            }

            position++;
        }

        return found;
    }

    private static IReadOnlyList<RunEnemy> EnemiesIn(IReadOnlyDictionary<string, string> fields)
    {
        var enemies = new List<RunEnemy>();
        for (var index = 0; ; index++)
        {
            var prefix = $"{ReplayTrace.EnemyFieldPrefix}{index.ToString(CultureInfo.InvariantCulture)}.";
            if (Text(fields, $"{prefix}model") is not { } model) return enemies;

            enemies.Add(new RunEnemy(
                model, Number(fields, $"{prefix}hp"), Number(fields, $"{prefix}max_hp")));
        }
    }

    /// <summary>The deck as tiles: one per distinct card, counted, in the order the
    /// deck first holds each. Order is the deck's own rather than alphabetical, because
    /// the recording's order is what the run built.</summary>
    private static IReadOnlyList<DeckTile> Tiles(IReadOnlyList<string> cards)
    {
        var order = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var card in cards)
        {
            if (!counts.TryGetValue(card, out var count)) order.Add(card);
            counts[card] = count + 1;
        }

        return [.. order.Select(card => new DeckTile(card, counts[card]))];
    }

    private static IReadOnlyList<string>? Sequence(
        IReadOnlyDictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var value) ? Split(value) : null;

    /// <summary>An empty sequence field is an empty list and not one empty member: a
    /// run with no relics carries the field with nothing in it.</summary>
    private static IReadOnlyList<string> Split(string value) =>
        value.Length == 0
            ? []
            : [.. value.Split(CanonicalState.SequenceSeparator, StringSplitOptions.RemoveEmptyEntries)];

    private static string? Text(IReadOnlyDictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var value) && value.Length > 0 ? value : null;

    private static int? Number(IReadOnlyDictionary<string, string> fields, string name) =>
        fields.TryGetValue(name, out var value) &&
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}

/// <summary>One enemy of a recorded fight, as the fight pane names it.</summary>
public sealed record RunEnemy(string Model, int? Hp, int? MaxHp);

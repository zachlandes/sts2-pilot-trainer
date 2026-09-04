using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// The unlock state a run must be generated against.
///
/// This is environment identity, not a nicety. The game builds a run's content
/// pools from the player's unlocks - which cards, relics, potions and characters
/// exist to be drawn from - so two players on the same seed, build and content
/// hash do not get the same run unless they also have the same unlocks. The
/// difference is measured, not argued: see
/// <c>docs/environment-identity.md</c>.
///
/// Two requirements are expressible, and each is expressible because something can
/// check it. "Complete" is checkable against the build itself, because the game can
/// enumerate the full unlock set it ships; it is the honest requirement for a
/// recording read off a video, where the creator's unlocks are an inference about a
/// stranger. "Exact" names the state a recorder read out of the player's own
/// game - the values the game's own unlock state is constructed from - and then the
/// check is that this build ships every id in it. A partial requirement written by
/// hand remains impossible, because it would be claiming something nobody read.
/// </summary>
public sealed record UnlockRequirement
{
    /// <summary>Everything this build ships. What a video reading can honestly ask
    /// for, because it is the one requirement the build can enumerate for itself.</summary>
    public const string CompleteCompleteness = "complete";

    /// <summary>Exactly the ids in <see cref="Inventory"/>, as a recorder read them
    /// out of the game the run was played in.</summary>
    public const string ExactCompleteness = "exact";

    /// <summary>Readers must refuse anything outside this set rather than treat an
    /// unrecognised requirement as satisfied.</summary>
    public static readonly string[] Completenesses = [CompleteCompleteness, ExactCompleteness];

    [JsonPropertyName("completeness")]
    public required string Completeness { get; init; }

    /// <summary>Why the manifest claims this. Kept next to the value because
    /// "everything was unlocked" is an inference about a stranger, not a reading.</summary>
    [JsonPropertyName("basis")]
    public required string Basis { get; init; }

    /// <summary>
    /// The state itself, as the values the game constructs an unlock state from.
    /// Present for <see cref="ExactCompleteness"/> and absent otherwise.
    /// </summary>
    [JsonPropertyName("inventory")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public UnlockStateInventory? Inventory { get; init; }

    [JsonIgnore]
    public bool IsComplete => string.Equals(Completeness, CompleteCompleteness, StringComparison.Ordinal);

    [JsonIgnore]
    public bool IsExact => string.Equals(Completeness, ExactCompleteness, StringComparison.Ordinal);

    public static UnlockRequirement Complete(string basis) =>
        new() { Completeness = CompleteCompleteness, Basis = basis };

    public static UnlockRequirement Exact(string basis, UnlockStateInventory inventory) =>
        new() { Completeness = ExactCompleteness, Basis = basis, Inventory = inventory };
}

/// <summary>
/// The unlock state itself, as the values the game's own <c>UnlockState</c> is
/// constructed from: the epochs unlocked, the encounters seen, and how many runs
/// have been played.
///
/// These three and not a per-category list of cards, relics and potions. Those
/// categories are real, and they are derived: the game computes them over its model
/// database from this state, and its constructor takes nothing else - so a manifest
/// that named them would be asking for something no environment could be built to
/// satisfy, however exactly it matched. The check that this build ships every id
/// named still happens; it just happens over the values a state can actually be made
/// of. Measured rather than assumed - see <c>docs/environment-identity.md</c>.
/// </summary>
public sealed record UnlockStateInventory
{
    /// <summary>The epochs the player had unlocked, as model ids.</summary>
    [JsonPropertyName("epochs")]
    public required IReadOnlyList<string> Epochs { get; init; }

    /// <summary>Every encounter the player had seen, as model ids. Part of the state
    /// rather than a statistic: the game reads it when it builds a run.</summary>
    [JsonPropertyName("encounters_seen")]
    public required IReadOnlyList<string> EncountersSeen { get; init; }

    /// <summary>How many runs the player had played.</summary>
    [JsonPropertyName("runs")]
    public required int Runs { get; init; }

    /// <summary>
    /// The two id lists, paired with the name a diagnostic reports each under.
    ///
    /// Enumerated here rather than at each caller so that the validator and the
    /// preflight agree on the set - a second listing is a second answer waiting to
    /// omit one.
    /// </summary>
    public IEnumerable<(string Name, IReadOnlyList<string> Ids)> IdLists()
    {
        yield return ("epochs", Epochs);
        yield return ("encounters_seen", EncountersSeen);
    }
}

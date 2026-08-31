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
/// Only one requirement is expressible today, and deliberately so. "Complete" is
/// checkable against the build itself, because the game can enumerate the full
/// unlock set it ships. A partial requirement would have to enumerate every id a
/// particular source player had, which no video shows, so a manifest claiming one
/// would be claiming something nobody read.
/// </summary>
public sealed record UnlockRequirement
{
    /// <summary>The only supported value. Readers must refuse anything else rather
    /// than treat an unrecognised requirement as satisfied.</summary>
    public const string CompleteCompleteness = "complete";

    [JsonPropertyName("completeness")]
    public required string Completeness { get; init; }

    /// <summary>Why the manifest claims this. Kept next to the value because
    /// "everything was unlocked" is an inference about a stranger, not a reading.</summary>
    [JsonPropertyName("basis")]
    public required string Basis { get; init; }

    public bool IsComplete => string.Equals(Completeness, CompleteCompleteness, StringComparison.Ordinal);

    public static UnlockRequirement Complete(string basis) =>
        new() { Completeness = CompleteCompleteness, Basis = basis };
}

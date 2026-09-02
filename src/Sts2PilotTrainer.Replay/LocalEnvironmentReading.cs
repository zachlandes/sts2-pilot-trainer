using System.Text.Json.Serialization;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// The installation identity and unlock state the replay host reports.
///
/// The unlock state may come from this process's profile or from an explicitly named
/// host-supplied model; <see cref="UnlockInventory.Origin"/> and
/// <see cref="UnlockInventory.FromPlayerProfile"/> preserve that distinction.
/// This type carries no game code so that the preflight rules can be tested without
/// the game installed. Producing one is the engine's job and the engine's alone; see
/// <c>Sts2PilotTrainer.Engine.LocalEnvironment</c>.
/// </summary>
public sealed record LocalPrerequisites
{
    [JsonPropertyName("build_version")]
    public required string BuildVersion { get; init; }

    [JsonPropertyName("build_date_utc")]
    public required string BuildDateUtc { get; init; }

    [JsonPropertyName("content_hash")]
    public required string ContentHash { get; init; }

    /// <summary>
    /// Every mod the running engine reports loaded.
    ///
    /// Kept beside the content hash because it answers the question the hash cannot:
    /// mods that patch behaviour or declare themselves non-gameplay are still present
    /// here even when they contribute nothing to that checksum.
    /// </summary>
    [JsonPropertyName("loaded_mods")]
    public required IReadOnlyList<LoadedMod> LoadedMods { get; init; }

    [JsonPropertyName("unlocks")]
    public required UnlockInventory Unlocks { get; init; }

    /// <summary>
    /// Which of the acts the manifest names are locked under this environment's
    /// unlock state, asked of the game rather than derived from an epoch's name.
    ///
    /// The sharpest unlock gate there is: this build ships two acts at index 0 and
    /// only one of them is available to a new player, so an environment missing that
    /// unlock cannot climb the run at all - and would otherwise take the default
    /// variant, which generates different content from the same seed and the same map.
    /// </summary>
    [JsonPropertyName("locked_acts")]
    public required IReadOnlyList<string> LockedActs { get; init; }

    /// <summary>
    /// The highest ascension this process's profile records for the character the
    /// manifest names, or null when the unlock reading did not come from a player
    /// profile and there is therefore no profile ceiling to compare against.
    /// </summary>
    [JsonPropertyName("profile_ascension_ceiling")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ProfileAscensionCeiling { get; init; }
}

/// <summary>One mod the local engine says it loaded, identified by its own manifest.</summary>
public sealed record LoadedMod(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("affects_gameplay")] bool AffectsGameplay);

/// <summary>
/// The unlock state a run here would actually be generated against, category by
/// category, together with where the reading came from.
/// </summary>
public sealed record UnlockInventory
{
    /// <summary>Human-readable account of how this was obtained - a save profile, or
    /// a state the host supplies in place of one. Printed verbatim in diagnostics so
    /// nobody has to guess whether a pass was measured or assumed.</summary>
    [JsonPropertyName("origin")]
    public required string Origin { get; init; }

    /// <summary>True only when the counts came from this process's profile progress.
    /// A host-supplied state is a substitute for a player's profile, not a reading of
    /// one, and the two must not be reported as the same kind of evidence.</summary>
    [JsonPropertyName("from_player_profile")]
    public required bool FromPlayerProfile { get; init; }

    [JsonPropertyName("categories")]
    public required IReadOnlyList<UnlockCategory> Categories { get; init; }

    public bool IsComplete => Categories.All(category => category.IsComplete);

    public IReadOnlyList<UnlockCategory> Incomplete =>
        Categories.Where(category => !category.IsComplete).ToList();
}

/// <summary>
/// One unlock category: how much the selected unlock state has, against how much the
/// build ships. <paramref name="Required"/> is read from the game rather than written
/// down here, so it stays correct across builds that add content.
/// </summary>
public sealed record UnlockCategory(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("available")] int Available,
    [property: JsonPropertyName("required")] int Required,
    [property: JsonPropertyName("missing_sample")] IReadOnlyList<string> MissingSample)
{
    public bool IsComplete => Available >= Required;

    public int Missing => Math.Max(0, Required - Available);
}

/// <summary>
/// The run that exists in the game right now, read back from it.
///
/// An eventual in-game host reads the player's own run here. The arbiter reads the
/// run it just constructed, which is not a formality: it is how we learn that the
/// engine built the run the manifest asked for rather than something adjacent to it.
/// </summary>
public sealed record LocalRunReading
{
    [JsonPropertyName("origin")]
    public required string Origin { get; init; }

    [JsonPropertyName("seed")]
    public required string Seed { get; init; }

    [JsonPropertyName("game_mode")]
    public required string GameMode { get; init; }

    [JsonPropertyName("ascension")]
    public required int Ascension { get; init; }

    [JsonPropertyName("character")]
    public required string Character { get; init; }

    [JsonPropertyName("acts")]
    public required IReadOnlyList<string> Acts { get; init; }
}

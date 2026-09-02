namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// Every fixed word the Combat Trainer shows a player, in one place.
///
/// One file so that "what does the mod say" is answerable by reading a file rather
/// than by grepping a scene graph, and so that nothing can drift into inventing a
/// sentence: anything a screen renders is either here, derived from the selected
/// manifest, or a diagnostic <see cref="Sts2PilotTrainer.Replay.EnvironmentPreflight"/>
/// already produces and is shown verbatim.
///
/// These strings are approved wording. Changing one is a product decision, not a
/// refactor.
/// </summary>
public static class TrainerCopy
{
    /// <summary>The mod's name, in the game's mod list and on its mode card.</summary>
    public const string Name = "Combat Trainer";

    /// <summary>The mod list's description, and the mode card's.</summary>
    public const string Description =
        "Fight NaveGreed's Floor 2 Sludge Spinner exactly as recorded, then compare your fight with " +
        "the recording. Reads your game; never writes to it.";

    /// <summary>What this one recording is, under the screen's title.</summary>
    public const string Subtitle = "NaveGreed · Ironclad · Ascension 10 · Floor 2 · Sludge Spinner";

    public const string PassHeadline = "Your game can play this fight as recorded.";

    public const string FailHeadline = "Your game cannot play this fight as recorded yet.";

    /// <summary>
    /// Says which profile the unlock rows were measured against.
    ///
    /// Load-bearing rather than decorative: the game forks a separate profile for
    /// modded play, so a player with a complete unmodded profile can fail these rows
    /// and have no idea why. The remedy is the game's own import, which is why the
    /// sentence names it.
    /// </summary>
    public const string ProfileNote =
        "Checked against the profile the game uses when running modded. If your unmodded progress is " +
        "missing here, import it from the profile select screen.";

    public const string BackButton = "Back";

    /// <summary>The build the recording was made on, as the screen states it.</summary>
    public static string RecordingLine(string buildVersion, string buildDateUtc) =>
        $"Recorded on {buildVersion} ({buildDateUtc})";
}

namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// Whether Runmobile puts a row of its own on the game's main menu, and what the
/// settings control that governs it says.
///
/// <para><b>Why there is a row at all.</b> The game sends a player who has finished no
/// run straight from Singleplayer to character select and hides its own Compendium
/// until a run has been finished, so the Compendium card and the run-history plate -
/// the library's other two ways in - have no host surface for exactly the player who
/// has never played. A top-level row is the one entrance that exists before any
/// progression does. It opens the same browser the card opens; there is no second
/// library and no second playback path.</para>
///
/// <para><b>The default follows the player and the choice outranks it.</b> A player who
/// has finished no run gets the row, because they cannot reach the library any other
/// way. A player who has run history does not, because they can, and a mod is not
/// entitled to a permanent line on somebody's main menu. Either of them can say
/// otherwise on the settings page, and once they have, their sentence is what holds -
/// finishing a first run does not then take away a row the player turned on, and it
/// does not put back one they turned off. That is what the tri-state
/// <c>show_main_menu_row</c> is for: absent means nobody has said, and only then does
/// the run count decide.</para>
///
/// <para>One derivation, like every other surface here. <see cref="ShownWhen"/> is the
/// whole rule and both readers call it - the mod's main-menu patch, which draws the
/// row, and <see cref="MyRunsRow"/>, which states on the settings page what the menu is
/// currently doing. A patch that worked the rule out for itself would eventually
/// disagree with the control that claims to set it.</para>
/// </summary>
/// <param name="Shown">Whether the row is on the main menu right now, which the
/// settings row projects into its native ticked or unticked image.</param>
public sealed record MainMenuRow(bool Shown)
{
    /// <summary>
    /// The whole rule, in one expression: the player's own sentence where they have
    /// written one, and the run count where they have not.
    /// </summary>
    /// <param name="choice">What <c>show_main_menu_row</c> says, or null where the
    /// player has never touched it.</param>
    /// <param name="hasFinishedARun">Whether this save profile has finished a run -
    /// the same fact the game's own Compendium gate reads, so the row appears exactly
    /// where the game's route does not.</param>
    public static bool ShownWhen(bool? choice, bool hasFinishedARun) => choice ?? !hasFinishedARun;

    public static MainMenuRow For(bool? choice, bool hasFinishedARun) =>
        new(ShownWhen(choice, hasFinishedARun));
}

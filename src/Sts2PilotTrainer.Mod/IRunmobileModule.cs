using HarmonyLib;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// One feature of Runmobile, and everything the shell needs to know about it.
///
/// Runmobile is one mod made of three features - the recorder, the run library, and
/// the Combat Trainer - and this is the line between them. The shell owns what is
/// true of the mod however it is configured: the assembly resolver, the Harmony
/// instance, adopting the running game, the write barrier, and drawing the
/// singleplayer-menu cards. A module owns one feature's patches and the surfaces it
/// puts in front of a player, which it contributes as <see cref="MenuCard"/> entries
/// rather than by installing the renderer itself - so a module that refuses cannot
/// take another module's card down with it.
///
/// A module that cannot establish what it needs is disabled rather than fatal. It
/// installs no patch and contributes no surface, and the reason goes to the game's
/// log once - which is the same outcome a player saw before there were modules, with
/// the difference that the other two features are not taken down with it.
///
/// That holds for a module which declares itself disabled. A module whose
/// <see cref="Install"/> throws instead aborts the shell's start and may leave its
/// partial patches applied; that is a broken build rather than a runtime condition,
/// and the lifecycle that isolates a failed install arrives with the second module.
///
/// The seam is internal. A second plugin author would need it public, and until one
/// exists a public surface is a promise about a shape nothing has tested. See
/// docs/in-game-host.md.
/// </summary>
internal interface IRunmobileModule
{
    /// <summary>The feature's name, as it appears in this mod's own log lines. Not a
    /// player-facing string: those live in <c>Sts2PilotTrainer.Trainer</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Whether this module can run in this process. Asked before anything is
    /// installed, and answered by the module actually establishing what it needs
    /// rather than by a flag somebody set.
    /// </summary>
    bool Enabled { get; }

    /// <summary>Why it is not, in the module's own words. Null when it is.</summary>
    string? Refusal { get; }

    /// <summary>
    /// The singleplayer-menu cards this module contributes. Read only from an
    /// enabled module, so an implementation may assume what <see cref="Enabled"/>
    /// established.
    /// </summary>
    IReadOnlyList<MenuCard> MenuCards { get; }

    /// <summary>
    /// Installs this module's patches on the shell's Harmony instance. Called once,
    /// at mod start, and only on an enabled module.
    /// </summary>
    void Install(Harmony harmony);
}

/// <summary>
/// A card a module puts in the game's singleplayer menu.
///
/// The description is deferred because it is read out of the module's own data - the
/// recording a card is about - and a module builds this list before anything asks it
/// to draw.
/// </summary>
/// <param name="NodeName">The name given to the node, so a second pass over the same
/// menu sees its own work rather than adding another card.</param>
/// <param name="Title">The card's title, from <c>Sts2PilotTrainer.Trainer</c>.</param>
/// <param name="Description">The card's description, read when it is drawn.</param>
/// <param name="Open">What pressing it opens.</param>
internal sealed record MenuCard(string NodeName, string Title, Func<string> Description, Action Open);

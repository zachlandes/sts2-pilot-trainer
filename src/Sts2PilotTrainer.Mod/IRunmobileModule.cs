using HarmonyLib;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// One feature of Runmobile, and everything the shell needs to know about it.
///
/// Runmobile is one mod made of three features - the recorder, the run library, and
/// recorded fights - and this is the line between them. The shell owns what is
/// true of the mod however it is configured: the assembly resolver, the Harmony
/// instance, adopting the running game, and the write barrier. A module owns one
/// feature's patches and surfaces.
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
    /// Installs this module's patches on the shell's Harmony instance. Called once,
    /// at mod start, and only on an enabled module.
    /// </summary>
    void Install(Harmony harmony);
}

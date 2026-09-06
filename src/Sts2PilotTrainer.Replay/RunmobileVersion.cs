using System.Reflection;

namespace Sts2PilotTrainer.Replay;

/// <summary>
/// Which build of Runmobile this is, as one answer.
///
/// A native recording names this build twice - once in the mod set, as the mod the
/// game reported loaded, and once as the recorder that wrote the file - and a reader
/// holding the two side by side is entitled to see the same string. Every assembly
/// here is stamped from the <c>version</c> field of
/// <c>src/Sts2PilotTrainer.Mod/Runmobile.json</c> by <c>Directory.Build.props</c>,
/// so the manifest the player's game reads is the source and nothing declares a
/// second one. Which assembly asks is therefore not a question this has to answer.
/// </summary>
public static class RunmobileVersion
{
    /// <summary>The version as the mod manifest spells it, such as <c>0.1.0</c>.</summary>
    public static string Current { get; } = Of(typeof(RunmobileVersion).Assembly);

    /// <summary>How a recorder names itself in what it writes.</summary>
    public static string Recorder => $"runmobile-recorder/{Current}";

    /// <summary>
    /// The informational version rather than the assembly version, because that is
    /// the only one that survives the trip verbatim: an assembly version is always
    /// four parts, so a manifest saying 0.1.0 would come back as 0.1.0.0 and a reader
    /// comparing the two strings would be right to call that a disagreement.
    ///
    /// A build that stamped nothing throws rather than inventing a version. Everything
    /// downstream of this is evidence, and a recording labelled with a version its
    /// recorder guessed is worse than one that was never written.
    /// </summary>
    internal static string Of(Assembly assembly)
    {
        var stamped = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // Source-stamping tools append "+<commit>" to the informational version.
        var version = stamped?.Split('+', 2)[0];

        return string.IsNullOrWhiteSpace(version)
            ? throw new InvalidOperationException(
                $"{assembly.GetName().Name} carries no version. Every assembly here takes one from " +
                "the \"version\" field of src/Sts2PilotTrainer.Mod/Runmobile.json through " +
                "Directory.Build.props; this one was built without it.")
            : version;
    }
}

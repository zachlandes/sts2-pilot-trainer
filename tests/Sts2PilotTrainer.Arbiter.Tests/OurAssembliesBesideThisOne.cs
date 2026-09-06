using System.Reflection;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// Every assembly this repository builds that sits beside the running test binary.
///
/// The version sweeps ask this rather than a list they maintain: a list is only ever
/// as complete as the last person to remember it, and the invariant being asserted is
/// about assemblies nobody has thought of yet. A project reference brings its output
/// here transitively, so referencing a new project is enough to put it under the
/// sweep. GodotStubs is out by construction rather than by exclusion - it builds as
/// GodotSharp, which is not one of ours by name.
/// </summary>
internal static class OurAssembliesBesideThisOne
{
    public static IReadOnlyList<Assembly> All()
    {
        var beside = Path.GetDirectoryName(typeof(OurAssembliesBesideThisOne).Assembly.Location)!;

        return Directory
            .EnumerateFiles(beside, "*.dll")
            .Where(IsOurs)
            .Select(Assembly.LoadFrom)
            .OrderBy(assembly => assembly.GetName().Name, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsOurs(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);

        return name.StartsWith("Sts2PilotTrainer.", StringComparison.Ordinal)
            || string.Equals(name, "Runmobile", StringComparison.Ordinal);
    }
}

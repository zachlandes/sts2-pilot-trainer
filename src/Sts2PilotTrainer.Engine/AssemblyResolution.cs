using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Sts2PilotTrainer.IO;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Teaches the runtime where the prepared game assemblies live.
///
/// This runs from a module initializer, which is not a stylistic choice: the JIT
/// resolves the types a method references when it prepares that method, so a
/// resolver installed in the first line of a method that mentions a game type is
/// already too late. A module initializer runs when this assembly loads, before
/// any of its methods are prepared.
/// </summary>
internal static class AssemblyResolution
{
    /// <summary>Where the bootstrap put the prepared copy, if it is not in the
    /// default place. Set by the test host and by anyone building out of tree.</summary>
    private const string LibDirVariable = "STS2_PILOT_TRAINER_LIB";

    [ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Usage", "CA2255:The 'ModuleInitializer' attribute should not be used in libraries",
        Justification =
            "This is the scenario the rule carves out. The resolver has to be installed before " +
            "the JIT prepares any method that mentions a game type, which rules out every " +
            "explicit-initialisation alternative available to a library.")]
    internal static void Install()
    {
        var libDir = ResolveLibDirectory();
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            if (libDir is null || name.Name is null) return null;
            var path = Path.Combine(libDir, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
    }

    /// <summary>
    /// Finds <c>build/lib</c>. Explicit environment variable first, then a walk up
    /// from wherever this assembly was loaded, which covers running the CLI, running
    /// tests, and running from an IDE without any of them needing to agree on a
    /// relative path.
    /// </summary>
    internal static string? ResolveLibDirectory()
    {
        var configured = Environment.GetEnvironmentVariable(LibDirVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var full = WorktreePath.Require(configured.Trim());
            if (File.Exists(Path.Combine(full, "sts2.dll"))) return full;
            return null;
        }

        var dir = AppContext.BaseDirectory;
        for (var depth = 0; depth < 12 && !string.IsNullOrEmpty(dir); depth++)
        {
            foreach (var candidate in new[] { Path.Combine(dir, "lib"), Path.Combine(dir, "build", "lib") })
            {
                if (File.Exists(Path.Combine(candidate, "sts2.dll")))
                {
                    return WorktreePath.Require(candidate);
                }
            }
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        return null;
    }
}

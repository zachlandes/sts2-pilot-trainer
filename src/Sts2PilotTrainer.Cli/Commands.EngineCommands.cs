using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// Prints which of the game's own members each decision maps onto, and whether the
    /// three gameplay paths the engine's test-mode flag would otherwise change still
    /// take retail's branch under this host. Says whether either still holds against
    /// the prepared assembly.
    ///
    /// The command exists because both are seams this repository shares with a build it
    /// does not own, and a shared assumption nobody can inspect is one that drifts. It
    /// is the patch-day question in one line: after a game update, does this build still
    /// know how to make every decision it claims to, and does it still generate what the
    /// player's own client generates?
    ///
    /// The retail branches are reported here rather than under a verb of their own
    /// because that is what keeps the integration tests' arrangement intact: they drive
    /// the built CLI in a subprocess and the test project deliberately does not
    /// reference the engine, so the measurement has to be reachable through some verb,
    /// and this is the one already asking the same question.
    /// </summary>
    internal static int EngineCommandsCommand(string[] args)
    {
        // Reading the build first is load-bearing, not a formatting choice. The JIT
        // resolves the types a method mentions when it prepares that method, and this
        // one mentions engine entry points whose own signatures reach game types.
        // Touching a game-free engine type here loads the engine assembly, whose module
        // initializer installs the assembly resolver, before that happens. Without it
        // the command dies on 'Could not load file or assembly sts2'. See
        // AssemblyResolution.
        Console.WriteLine($"build    : {GameIdentity.Read().BuildVersion}");
        Console.WriteLine();

        foreach (var command in EngineCommands.All)
        {
            Console.WriteLine(
                $"  {command.Verb,-22} {command.Kind.ToString().ToLowerInvariant(),-9} {command.Describe()}");
        }

        foreach (var verb in Enum.GetValues<ActionVerb>()
                     .Where(verb => !EngineCommands.Maps(verb))
                     .OrderBy(verb => verb.ToString(), StringComparer.Ordinal))
        {
            Console.WriteLine($"  {verb,-22} unmapped  {EngineCommands.UnmappedReason(verb)}");
        }

        var problems = EngineCommands.Verify().ToList();

        Console.WriteLine();
        Console.WriteLine("restored retail branches (docs/headless-fidelity.md):");
        foreach (var site in RetailBranchProbe.Measure())
        {
            Console.WriteLine($"  {(site.Passed ? "pass" : "FAIL")}  {site.Name}");
            Console.WriteLine($"        expected: {site.Expected}");
            Console.WriteLine($"        observed: {site.Observed}");
            if (site.Passed) continue;
            problems.Add(
                $"{site.Name} did not take retail's path: expected {site.Expected}, observed {site.Observed}. " +
                "This host generates content the player's own client does not, and a replay of any run that " +
                "reaches it will diverge silently.");
        }

        Console.WriteLine();
        if (problems.Count == 0)
        {
            Console.WriteLine(
                "sound - every mapped member exists on this build, every verb is accounted for, and every " +
                "restored retail branch takes retail's path.");
            return 0;
        }

        foreach (var problem in problems) Console.Error.WriteLine($"  {problem}");
        Console.Error.WriteLine();
        Console.Error.WriteLine("DRIFTED - the host's account of this build does not describe it.");
        return 1;
    }
}

using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// Prints which of the game's own members each decision maps onto, and says
    /// whether the mapping still holds against the prepared assembly.
    ///
    /// The command exists because the table is the seam two things share - the
    /// headless driver that issues these decisions and the recorder that observes
    /// them - and a shared table nobody can inspect is a shared table that drifts.
    /// It is also the patch-day question in one line: after a game update, does this
    /// build still know how to make every decision it claims to?
    ///
    /// <c>--probe</c> answers the other half. Reflection says the game still has the
    /// member; only pushing a verb through the driver says the driver still has a
    /// case for it. Each mapped verb is applied to a freshly constructed run, and the
    /// only outcome this rejects is the driver reporting that the table names a
    /// command it has no case for. Every other refusal is the verb's own, which is
    /// exactly what a decision made in the wrong place should produce.
    /// </summary>
    internal static int EngineCommandsCommand(string[] args)
    {
        var probe = Args.Has(args, "--probe");

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
        if (probe) problems.AddRange(EngineCommands.ProbeDriver());

        Console.WriteLine();
        if (problems.Count == 0)
        {
            Console.WriteLine(probe
                ? "sound - every mapped member exists, every verb is accounted for, and the driver has a " +
                  "case for each mapping."
                : "sound - every mapped member exists on this build and every verb is accounted for.");
            return 0;
        }

        foreach (var problem in problems) Console.Error.WriteLine($"  {problem}");
        Console.Error.WriteLine();
        Console.Error.WriteLine("DRIFTED - the command table does not describe this build.");
        return 1;
    }
}

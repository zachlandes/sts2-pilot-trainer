using Sts2PilotTrainer.Engine;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// Measures one of the verbs format v6 added against the real engine, and says
    /// whether every seam it rests on still does what the driver says it does.
    ///
    /// A verb of its own rather than a row under <c>engine-commands</c>, because each
    /// probe starts a run and drives a prompt, which is minutes of engine rather than
    /// a reflection check. <see cref="VerbProbe"/> owns what is measured; this prints
    /// it and exits non-zero when anything did not pass.
    /// </summary>
    internal static int VerbProbeCommand(string[] args)
    {
        var probe = Args.Positional(args, 0, $"probe ({string.Join(" | ", VerbProbe.Probes)})");

        Console.WriteLine($"build : {GameIdentity.Read().BuildVersion}");
        Console.WriteLine($"probe : {probe}");
        Console.WriteLine();

        var results = VerbProbe.Measure(probe);
        foreach (var result in results)
        {
            Console.WriteLine($"  {(result.Passed ? "pass" : "FAIL")}  {result.Name}");
            Console.WriteLine($"        {result.Detail}");
        }

        Console.WriteLine();
        if (results.All(result => result.Passed))
        {
            Console.WriteLine("sound - every seam this verb rests on does what the driver says it does.");
            return 0;
        }

        Console.Error.WriteLine("DRIFTED - the host's account of this verb does not describe this build.");
        return 1;
    }
}

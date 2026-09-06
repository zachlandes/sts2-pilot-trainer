using System.Text.Json;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// Asks whether the three gameplay paths the engine's test-mode flag would
    /// otherwise change actually take retail's branch under this host.
    ///
    /// Two of the three - Cauldron's and Calling Bell's pickup effects - are reached by
    /// no committed recording and no fixture, so this is the only thing that measures
    /// them. The third, the merchant's potion price, is measured by every native
    /// recording that walks into a shop; it is included here anyway because a probe
    /// that covered two of three would be a report about the ones that were convenient.
    ///
    /// Exits non-zero when any site did not take retail's branch, or when the headless
    /// flag was not back on afterwards.
    /// </summary>
    internal static int RetailBranchProbe(string[] args)
    {
        var outPath = Args.Value(args, "--out");

        // Reading the build first is load-bearing, not a formatting choice. The JIT
        // resolves the types a method mentions when it prepares that method, and this
        // one mentions the probe - whose own signatures mention game types. Touching a
        // game-free engine type here loads the engine assembly, whose module
        // initializer installs the assembly resolver, before that happens. Without it
        // the command dies on 'Could not load file or assembly sts2'. See
        // AssemblyResolution.
        var build = GameIdentity.Read().BuildVersion;
        Console.WriteLine($"build            : {build}");

        var report = Engine.RetailBranchProbe.Run();

        if (outPath is not null)
        {
            EvidenceArtifact.PreparePath(outPath)
                .WriteAtomic(JsonSerializer.Serialize(report, Json.Indented) + "\n");
        }

        Console.WriteLine($"headless flag on : {(report.TestModeRestored ? "yes" : "NO - a finalizer did not restore it")}");
        foreach (var site in report.Sites)
        {
            Console.WriteLine($"  {(site.Passed ? "pass" : "FAIL")}  {site.Name}");
            Console.WriteLine($"        expected: {site.Expected}");
            Console.WriteLine($"        observed: {site.Observed}");
        }

        Console.WriteLine(report.AllRestored
            ? "\nEvery restored retail branch took retail's path."
            : "\nA restored retail branch did not take retail's path. This host generates content the " +
              "player's own client does not, and a replay of any run that reaches it will diverge silently.");
        return report.AllRestored ? 0 : 1;
    }
}

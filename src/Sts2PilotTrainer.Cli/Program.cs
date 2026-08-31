using Sts2PilotTrainer.Engine;

namespace Sts2PilotTrainer.Cli;

/// <summary>
/// The arbiter's command line. Thin by design: every command is a few lines of
/// argument handling around a library call, so that what the tool does and what
/// the tests exercise cannot drift apart.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Usage();
            return args.Length == 0 ? 2 : 0;
        }

        try
        {
            return args[0] switch
            {
                "gate" => Commands.Gate(args[1..]),
                "validate" => Commands.Validate(args[1..]),
                "preflight" => Commands.Preflight(args[1..]),
                "preflight-live" => Commands.PreflightLive(args[1..]),
                "verify-seed" => Commands.VerifySeed(args[1..]),
                "synthetic-fixture" => Commands.SyntheticFixture(args[1..]),
                "generate-synthetic-fixture" => Commands.GenerateSyntheticFixture(args[1..]),
                "baselib-parity" => Commands.BaseLibParity(args[1..]),
                "baselib-parity-probe" => Commands.BaseLibParityProbe(args[1..]),
                "baselib-reachability" => Commands.BaseLibReachability(args[1..]),
                "baselib-reachability-probe" => Commands.BaseLibReachabilityProbe(args[1..]),
                "mode-discrimination" => Commands.ModeDiscrimination(args[1..]),
                "mode-discrimination-probe" => Commands.ModeDiscriminationProbe(args[1..]),
                "replay" => Commands.Replay(args[1..]),
                "determinism" => Commands.Determinism(args[1..]),
                "negative-controls" => Commands.NegativeControls(args[1..]),
                "combat-snapshot" => Commands.CombatSnapshot(args[1..]),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (Exception ex) when (ex is EngineException or Replay.ManifestException)
        {
            // These carry a message written for a person; a stack trace would bury it.
            Console.Error.WriteLine();
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine();
            return 1;
        }
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"unknown command: {command}");
        Usage();
        return 2;
    }

    private static void Usage() => Console.WriteLine(
        """
        sts2-arbiter - deterministic replay arbiter for Slay the Spire 2

          gate            <manifest> [--map-observation <path>] [--baselib <path>] [--out <dir>]
              The publication gate. Runs every condition below and reports one verdict:
              may this reconstruction be published as exact? Nothing here accepts a
              cheaper proxy in place of replaying through the real engine.

          validate        <manifest> [--show-rejections]
              Check a manifest's structure and its account of where the recording came
              from - including that it starts at the run's start, which nothing
              downstream can check. No game needed.

          preflight       <manifest> [--progress all-unlocked|none-unlocked|local-profile]
              Compare a manifest's environment identity and its player prerequisites
              against this machine's game: build, content hash, unlocks category by
              category, whether the run's acts are unlocked at all, and - reading a
              real profile - whether its ascension is available. Refuses, with
              diagnostics and in-game remediation, rather than replaying into a
              mismatch. Nothing here writes to a save, a profile or the install.

          preflight-live  <manifest> [--progress local-profile|all-unlocked|none-unlocked]
              Read the current profile and existing active run, then compare build,
              content, unlocks, seed, mode, ascension, character and acts against the
              manifest. Refuses when no run is active. Synthetic startup and identity
              overrides are available only with --demo-start-run for tests and demos.

          verify-seed     <map-observation> --candidates <seed>[,<seed>...] [--out <dir>]
              Generate each candidate seed's Act 1 map through the real engine and
              compare its topology against a map read from a video. This is the seed
              check that does not depend on reading any text.

          baselib-reachability <manifest> <BaseLib.dll> --out <path>
              Record every PowerCmd.Apply in the exact history and prove the measured
              BaseLib branch detector with an injected affected-call negative control.

          mode-discrimination <manifest> --out <path>
              Compare the verified prefix under the real build's standard, custom and
              daily run construction, with a behavior-changing modifier control.

          replay          <manifest> [--out <path>] [--state-out <path>] [--stop-after <seq>]
                                     [--progress <model>] [--show-trace]
              Replay the manifest's ordered action history from run start and check
              every checkpoint. Writes the manifest back with its verification filled
              in, including the step-by-step trace. --show-trace prints what changed at
              each step; see docs/comparison-direction.md for what the trace is for.

          determinism     <manifest> --runs <n>
              Replay the same manifest in n fresh processes and compare canonical state.

          negative-controls <manifest> [--out <dir>]
              Damage the history in specific ways and show the arbiter rejects each,
              alongside what a video-only consistency check would have concluded.

          combat-snapshot <manifest> [--cache <dir>] [--out <dir>]
              Materialise the verified combat-start snapshot, restore it by
              re-deriving it in a fresh process, and describe exactly the action
              history the manifest contains. The report says whether combat remains
              active at the end. Nothing here resets state mid-fight or replays an
              alternative line. See docs/comparison-direction.md.

        Every command needs a prepared game assembly: run ./scripts/bootstrap.sh first.
        """);
}

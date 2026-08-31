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
                "verify-seed" => Commands.VerifySeed(args[1..]),
                "synthetic-fixture" => Commands.SyntheticFixture(args[1..]),
                "generate-synthetic-fixture" => Commands.GenerateSyntheticFixture(args[1..]),
                "baselib-parity" => Commands.BaseLibParity(args[1..]),
                "baselib-parity-probe" => Commands.BaseLibParityProbe(args[1..]),
                "replay" => Commands.Replay(args[1..]),
                "replay-line" => Commands.ReplayLine(args[1..]),
                "determinism" => Commands.Determinism(args[1..]),
                "negative-controls" => Commands.NegativeControls(args[1..]),
                "snapshot-lines" => Commands.SnapshotLines(args[1..]),
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

          gate            <manifest> [--out <dir>]
              The publication gate. Runs every condition below and reports one verdict:
              may this reconstruction be published as exact? Nothing here accepts a
              cheaper proxy in place of replaying through the real engine.

          validate        <manifest> [--show-rejections]
              Check a manifest's structure and its account of where the recording came
              from - including that it starts at the run's start, which nothing
              downstream can check. No game needed.

          preflight       <manifest>
              Compare a manifest's environment identity against this machine's game.
              Refuses, with diagnostics, rather than replaying into a mismatch.

          verify-seed     <map-observation> --candidates <seed>[,<seed>...] [--out <dir>]
              Generate each candidate seed's Act 1 map through the real engine and
              compare its topology against a map read from a video. This is the seed
              check that does not depend on reading any text.

          replay          <manifest> [--out <path>] [--state-out <path>] [--stop-after <seq>]
              Replay the manifest's ordered action history from run start and check
              every checkpoint. Writes the manifest back with its verification filled in.

          determinism     <manifest> --runs <n>
              Replay the same manifest in n fresh processes and compare canonical state.

          negative-controls <manifest> [--out <dir>]
              Damage the history in specific ways and show the arbiter rejects each,
              alongside what a video-only consistency check would have concluded.

          snapshot-lines  <manifest> --at <seq> --line <file> --line <file> [--out <path>]
              Materialise the verified pre-turn snapshot, restore it once per line,
              run each line's actions, and report the objective state deltas.

        Every command needs a prepared game assembly: run ./scripts/bootstrap.sh first.
        """);
}

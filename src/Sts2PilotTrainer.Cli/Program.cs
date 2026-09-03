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
                "adopt-live" => Commands.AdoptLive(args[1..]),
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
                "enter-fight" => Commands.EnterFight(args[1..]),
                "combat-compare" => Commands.CombatCompare(args[1..]),
                "recorded-fight" => Commands.RecordedFightCommand(args[1..]),
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

          preflight-live  <manifest> [--progress local-profile]
              Demonstrate the future live gate inside this headless process. It reads
              only the empty build/sandbox profile and this process's RunManager, not
              retail player state, so the default path refuses by design. Synthetic
              startup, progress models and identity overrides are available only with
              --demo-start-run for tests and demos.

          adopt-live
              Ask whether this process is a running game whose state can be read.
              It refuses here, because a console process is not one - the same
              refusal the in-game host gets if it asks before the game has finished
              starting up, which is a crash rather than a wrong answer if unguarded.

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

          synthetic-fixture / generate-synthetic-fixture --out <path> [--line reference|alternate]
              Emit the mechanically generated engine fixture. Both lines play the
              first combat to its end; they differ only in which end of the hand they
              play from, and neither is a claim about how to play.

          replay          <manifest> [--out <path>] [--state-out <path>] [--stop-after <seq>]
                                     [--progress <model>] [--show-trace]
              Replay the manifest's ordered action history from run start and check
              every checkpoint. Writes the manifest back with its verification filled
              in, including the step-by-step trace. --show-trace prints what changed at
              each step; see docs/comparison-direction.md for what the trace is for.

          determinism     <manifest> --runs <n>
              Replay the same manifest in n fresh processes and compare canonical state.

          negative-controls <manifest> [--out <dir>] [--require-all-controls]
              Damage the history in specific ways and show the arbiter rejects each,
              alongside what a video-only consistency check would have concluded.
              --require-all-controls also refuses histories that do not exercise every control.

          enter-fight     <manifest> [--control <name>] [--cache <dir>] [--out <dir>] [--step]
                                     [--play [--recorded-fight <path>]]
              Construct the recording's run, walk it through the recording's own
              decisions in order, and prove the fight it lands in is the recorded one -
              against what the recording observed at that boundary and against the
              manifest's engine-produced combat-start snapshot digest. Reports the profile before and after,
              because nothing here may write to it. --control damages one decision
              before the fight and shows the entry refused; --step stops after one.
              --play then plays the recording's own fight to its end through the same
              capture the in-game host observes a player with, projects it, and
              compares it with the shipped recorded fight - the whole S5 loop with no
              scene tree, standing in the recording for the player.

          recorded-fight  <manifest> [--out <path>]
              Replay the manifest and write the recording's own line of its first
              fight: the engine-produced trace through the end of that fight, bound to
              the history it replayed and to the combat-start snapshot digest. This is
              the recording's side of the in-game comparison, shipped inside the mod;
              the retail client cannot replay, so it is produced here.

          combat-compare  <manifest> <manifest> [--out <dir>]
              Replay two manifests of the same fight, project each one's completed
              combat, and print the differences. Two projections, kept apart: the
              combat summary carries no chronology, the turn detail carries the
              ordered actions and each turn's enemy and player health lost. Nothing is scored or
              ranked. Refuses two fights that did not start from the same boundary,
              and refuses a history whose combat never finishes. See
              docs/comparison-direction.md.

          combat-snapshot <manifest> [--cache <dir>] [--out <dir>]
              Materialise the verified combat-start snapshot, restore it by
              re-deriving it in a fresh process, and describe exactly the action
              history the manifest contains. The report says whether combat remains
              active at the end. Nothing here resets state mid-fight or replays an
              alternative line. See docs/comparison-direction.md.

        Every command needs a prepared game assembly: run ./scripts/bootstrap.sh first.
        """);
}

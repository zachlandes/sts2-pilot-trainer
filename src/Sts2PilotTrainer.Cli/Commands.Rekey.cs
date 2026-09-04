using System.Globalization;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    /// <summary>
    /// Asks whether a recording still reproduces on the build installed now, and
    /// writes the answer down beside it.
    ///
    /// The one path in this project that deliberately replays a history against a
    /// build it was not recorded on. Everything else refuses a build difference before
    /// it starts, which is right for publication and wrong for a catalogue: the game
    /// ships a minor version roughly every fortnight, most of them touch nothing on
    /// any given run, and refusing by declaration retires a whole catalogue that
    /// measurement would mostly have kept.
    ///
    /// Three things it will not do. It will not edit the manifest - that says what the
    /// recording was made on, permanently, and the build under test travels on the
    /// verdict instead; <see cref="Revalidation"/> writes down at length why the
    /// obvious rebase is wrong. It will not read past anything but the build: an
    /// ascension, a character or an unlock difference is its own refusal with its own
    /// remedy, and the verdict is Blocked rather than measured. And it will not answer
    /// for a build that is not the one installed, because the answer is what the
    /// engine did and there is only one engine here.
    /// </summary>
    private static int Rekey(string[] args, string manifestPath)
    {
        var target = Args.Value(args, "--rekey")!;
        var manifest = ManifestJson.Load(manifestPath);
        var verdictsPath = ReproductionVerdicts.PathFor(manifestPath);
        var artifact = EvidenceArtifact.PreparePath(verdictsPath);

        var local = GameIdentity.Read();
        if (!string.Equals(target, local.BuildVersion, StringComparison.Ordinal))
        {
            throw new EngineException(
                $"--rekey {target} asks what that build does with this recording, and this machine has " +
                $"{local.BuildVersion}. A verdict is what the engine actually did, so the build being asked " +
                "about has to be the build installed. Install it through the game's own version selection; " +
                "this tool never changes it for you.");
        }

        Console.WriteLine($"recording : {manifest.RunId}");
        Console.WriteLine($"recorded on: {manifest.Environment.BuildVersion.Value} " +
                          $"(content {manifest.Environment.ContentHash.Value})");
        Console.WriteLine($"asking about: {local.BuildVersion} (content {local.ContentHash})");
        Console.WriteLine();

        var verdict = Measure(manifest, local);

        Console.WriteLine($"verdict   : {verdict.Status.ToString().ToUpperInvariant()}");
        Console.WriteLine($"            {verdict.Note}");
        if (verdict.MovedBoundaries.Count > 0)
        {
            Console.WriteLine();
            foreach (var moved in verdict.MovedBoundaries) Console.WriteLine($"  ! {moved}");
        }

        // What this build cannot answer, said out loud rather than left out. Three
        // instruments in this repository are pinned to v0.111.0 by measurement rather
        // than by convention, and a re-key onto another build does not relax them: it
        // reports them as questions nobody asked here.
        var blocked = BlockedConditions(local.BuildVersion);
        if (blocked.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("blocked on this build, and not relaxed:");
            foreach (var condition in blocked) Console.WriteLine($"  blocked  {condition}");
        }

        var catalogue = ReproductionVerdicts.LoadOrEmpty(verdictsPath, manifest).With(verdict);
        catalogue.Bind(manifest);
        artifact.WriteAtomic(catalogue.Serialize() + "\n");

        Console.WriteLine();
        Console.WriteLine($"verdicts  : {Paths.Display(artifact.Path)} " +
                          $"({catalogue.Verdicts.Count.ToString(CultureInfo.InvariantCulture)} build(s) asked)");

        // The recording's own fights are bound to the manifest per fight by history
        // hash and combat-start boundary, so a re-key that left them alone would leave
        // the two disagreeing. docs/ingestion.md requires this in the same step.
        if (verdict.Status == ReproductionStatus.Reproduces)
        {
            var fights = SelfProcess.Run("recorded-fight", manifestPath);
            Console.Write(fights.StandardOutput);
            Console.Error.Write(fights.StandardError);
            if (fights.ExitCode != 0)
            {
                Console.Error.WriteLine(
                    "The recording's own fights could not be regenerated, so the verdict above stands beside a " +
                    "recorded-fights file that was measured on another build.");
                return 1;
            }
        }
        else
        {
            Console.WriteLine(
                "The recording's own fights are not regenerated: they are cut from a replay that reproduced, " +
                "and this one did not.");
        }

        return verdict.Status == ReproductionStatus.Reproduces ? 0 : 1;
    }

    /// <summary>
    /// Replays the history on this build and reads a verdict out of what happened.
    ///
    /// The preflight is asked first and on its own, because "the environment differs
    /// in a way a patch does not explain" and "the history diverged" are different
    /// findings and only one of them is about the patch.
    /// </summary>
    private static ReproductionVerdict Measure(ReplayManifest manifest, GameIdentity local)
    {
        var gate = Engine.Preflight.Evaluate(
            manifest.Environment, RecordedFightEntry.SuppliedProgressFor(manifest), manifest.Source.Kind,
            measuringBuildDrift: true);

        foreach (var field in gate.Fields)
        {
            Console.WriteLine(
                $"  {(field.Matches ? "ok  " : "FAIL")} {field.Field,-24} manifest={field.Expected,-22} " +
                $"local={field.Actual}");
        }
        Console.WriteLine();

        if (!gate.Matches)
        {
            return Revalidation.Blocked(
                manifest, local.BuildVersion, local.ContentHash,
                gate.Fields.Where(field => !field.Matches).Select(field => field.Field).ToList());
        }

        var outcome = Arbiter.Run(manifest, measuringBuildDrift: true);
        var report = outcome.Report;

        return Revalidation.Decide(
            manifest,
            local.BuildVersion,
            local.ContentHash,
            report.ActionHistoryHash,
            report.Status == VerificationStatus.Verified,
            report.Diagnostics.FirstOrDefault(),
            report.Boundaries);
    }

    /// <summary>
    /// The instruments this repository pins to one build by measurement, and what each
    /// pin is about.
    ///
    /// They are pins rather than assumptions: each was established against v0.111.0's
    /// own IL or its own generated content, and a build that changed either would make
    /// the instrument measure something else while still reporting a number. So a
    /// re-key onto another build reports them blocked. Empty on v0.111.0, where they
    /// are simply the conditions the ordinary gate already runs.
    /// </summary>
    private static IReadOnlyList<string> BlockedConditions(string build) =>
        string.Equals(build, PinnedBuild, StringComparison.Ordinal)
            ? []
            :
            [
                $"synthetic fixtures: the generator is pinned to {PinnedBuild}, so no fixture-based control " +
                $"can be produced on {build}.",
                $"baselib-path: the reachability probe is pinned to {PinnedBuild} and to one BaseLib hash, so " +
                $"the branch it proves unreachable was not measured on {build}.",
                $"evidence-binding: mode and BaseLib evidence bind to one build, and there is none for " +
                $"{build} to bind to.",
            ];

    /// <summary>The build the pinned instruments were measured on. One name, so a
    /// diagnostic and the probes cannot drift apart about which build that is.</summary>
    private const string PinnedBuild = "v0.111.0";
}

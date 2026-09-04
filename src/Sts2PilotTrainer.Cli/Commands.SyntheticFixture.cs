using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    internal static int SyntheticFixture(string[] args)
    {
        var outPath = Args.Value(args, "--out")
            ?? throw new ManifestException("synthetic-fixture needs --out <path>.");
        var artifact = EvidenceArtifact.PreparePath(outPath);
        artifact.WriteAtomic(ManifestJson.Serialize(SyntheticReplayFixture.Create()) + "\n");
        Console.WriteLine($"synthetic fixture: {Paths.Display(artifact.Path)}");
        return 0;
    }

    internal static int GenerateSyntheticFixture(string[] args)
    {
        var outPath = Args.Value(args, "--out")
            ?? throw new ManifestException("generate-synthetic-fixture needs --out <path>.");
        var journeyName = Args.Value(args, "--journey") ?? "first-fight";
        var journey = journeyName switch
        {
            "first-fight" => Engine.SyntheticJourney.FirstFight,
            "whole-act" => Engine.SyntheticJourney.WholeAct,
            "screen-at-boundary" => Engine.SyntheticJourney.ScreenAtBoundary,
            _ => throw new ManifestException(
                $"Unknown fixture journey '{journeyName}'. Known journeys: first-fight, whole-act, " +
                "screen-at-boundary."),
        };

        // Only the first fight has two lines to play it, so --line asked of any other
        // journey is a request this command cannot carry out. Refused rather than
        // dropped: the fixture written after a silently ignored line is not the one
        // that was asked for, and nothing in the output would say so.
        var requestedLine = Args.Value(args, "--line");
        if (requestedLine is not null && journey != Engine.SyntheticJourney.FirstFight)
        {
            throw new ManifestException(
                $"The {journeyName} journey has no lines, so --line {requestedLine} cannot be honoured. " +
                "Only the first-fight journey takes a line; drop --line, or ask for --journey first-fight.");
        }

        var line = requestedLine ?? "reference";
        var combatLine = line switch
        {
            "reference" => Engine.CombatLine.Reference,
            "alternate" => Engine.CombatLine.Alternate,
            _ => throw new ManifestException(
                $"Unknown fixture line '{line}'. Known lines: reference, alternate."),
        };

        var artifact = EvidenceArtifact.PreparePath(outPath);
        artifact.WriteAtomic(
            ManifestJson.Serialize(Engine.SyntheticFixtureGenerator.Generate(journey, combatLine)) + "\n");
        Console.WriteLine(
            $"generated synthetic fixture: {Paths.Display(artifact.Path)} ({journeyName}" +
            $"{(journey == Engine.SyntheticJourney.FirstFight ? $", {line} line" : "")})");
        return 0;
    }
}

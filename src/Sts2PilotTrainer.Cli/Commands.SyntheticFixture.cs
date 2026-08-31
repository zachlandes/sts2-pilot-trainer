using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    internal static int SyntheticFixture(string[] args)
    {
        var outPath = Args.Value(args, "--out")
            ?? throw new ManifestException("synthetic-fixture needs --out <path>.");
        var artifact = EvidenceArtifact.PreparePath(outPath);
        var lineArtifacts = Args.Value(args, "--lines-out") is { } linesOut
            ? Sts2PilotTrainer.Replay.SyntheticReplayFixture.CreateLines()
                .Select(pair => (Name: pair.Key, Line: pair.Value, Artifact: EvidenceArtifact.Prepare(linesOut, pair.Key + ".line.json")))
                .ToList()
            : [];
        artifact.WriteAtomic(ManifestJson.Serialize(Sts2PilotTrainer.Replay.SyntheticReplayFixture.Create()) + "\n");
        Console.WriteLine($"synthetic fixture: {Paths.Display(artifact.Path)}");
        foreach (var (name, line, lineArtifact) in lineArtifacts)
        {
            lineArtifact.WriteAtomic(System.Text.Json.JsonSerializer.Serialize(line, Json.Indented) + "\n");
            Console.WriteLine($"synthetic line: {Paths.Display(lineArtifact.Path)}");
        }
        return 0;
    }

    internal static int GenerateSyntheticFixture(string[] args)
    {
        var outPath = Args.Value(args, "--out")
            ?? throw new ManifestException("generate-synthetic-fixture needs --out <path>.");
        var artifact = EvidenceArtifact.PreparePath(outPath);
        artifact.WriteAtomic(ManifestJson.Serialize(Engine.SyntheticFixtureGenerator.Generate()) + "\n");
        Console.WriteLine($"generated synthetic fixture: {Paths.Display(artifact.Path)}");
        return 0;
    }
}

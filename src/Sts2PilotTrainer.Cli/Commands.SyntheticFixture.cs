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
        var line = Args.Value(args, "--line") ?? "reference";
        var combatLine = line switch
        {
            "reference" => Engine.CombatLine.Reference,
            "alternate" => Engine.CombatLine.Alternate,
            _ => throw new ManifestException(
                $"Unknown fixture line '{line}'. Known lines: reference, alternate."),
        };
        var artifact = EvidenceArtifact.PreparePath(outPath);
        artifact.WriteAtomic(ManifestJson.Serialize(Engine.SyntheticFixtureGenerator.Generate(combatLine)) + "\n");
        Console.WriteLine($"generated synthetic fixture: {Paths.Display(artifact.Path)} ({line} line)");
        return 0;
    }
}

using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Cli;

internal static partial class Commands
{
    internal static int SyntheticFixture(string[] args)
    {
        var outPath = Args.Value(args, "--out")
            ?? throw new ManifestException("synthetic-fixture needs --out <path>.");
        ManifestJson.Save(Sts2PilotTrainer.Replay.SyntheticReplayFixture.Create(), outPath);
        Console.WriteLine($"synthetic fixture: {Paths.Display(outPath)}");
        if (Args.Value(args, "--lines-out") is { } linesOut)
        {
            Directory.CreateDirectory(linesOut);
            foreach (var (name, line) in Sts2PilotTrainer.Replay.SyntheticReplayFixture.CreateLines())
            {
                var path = Path.Combine(linesOut, name + ".line.json");
                File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(line, Json.Indented) + "\n");
                Console.WriteLine($"synthetic line: {Paths.Display(path)}");
            }
        }
        return 0;
    }

    internal static int GenerateSyntheticFixture(string[] args)
    {
        var outPath = Args.Value(args, "--out")
            ?? throw new ManifestException("generate-synthetic-fixture needs --out <path>.");
        ManifestJson.Save(Engine.SyntheticFixtureGenerator.Generate(), outPath);
        Console.WriteLine($"generated synthetic fixture: {Paths.Display(outPath)}");
        return 0;
    }
}

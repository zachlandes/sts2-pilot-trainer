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
        return 0;
    }
}

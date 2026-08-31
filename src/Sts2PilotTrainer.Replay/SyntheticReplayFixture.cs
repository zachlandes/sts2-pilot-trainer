namespace Sts2PilotTrainer.Replay;

public static class SyntheticReplayFixture
{
    private const string ResourceName =
        "Sts2PilotTrainer.Replay.Fixtures.synthetic-v0111-pilot-trainer.replay.json";

    public static ReplayManifest Create()
    {
        using var stream = typeof(SyntheticReplayFixture).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new ManifestException($"Embedded synthetic fixture '{ResourceName}' is missing.");
        using var reader = new StreamReader(stream);
        return ManifestJson.Deserialize(reader.ReadToEnd());
    }
}

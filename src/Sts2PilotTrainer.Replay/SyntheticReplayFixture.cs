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

    public static IReadOnlyDictionary<string, IReadOnlyList<ActionRecord>> CreateLines()
    {
        var suffix = Create().Actions.Skip(2).Select((action, index) => action with
        {
            Seq = index,
            Source = FactSource.Inferred,
            Evidence = FactEvidence.Reasoning("Candidate line for the synthetic engine fixture."),
            Args = action.Args
                .Where(pair => !pair.Key.StartsWith("negative_control_", StringComparison.Ordinal))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        }).ToList();
        var first = suffix[0];
        var second = suffix[1];
        var firstIndex = int.Parse(first.Args["hand_index"], System.Globalization.CultureInfo.InvariantCulture);
        var secondIndex = int.Parse(second.Args["hand_index"], System.Globalization.CultureInfo.InvariantCulture);
        var secondInitial = secondIndex + (firstIndex <= secondIndex ? 1 : 0);
        var firstAfterSecond = firstIndex - (secondInitial < firstIndex ? 1 : 0);
        var reordered = new List<ActionRecord>
        {
            second with { Seq = 0, Args = WithIndex(second.Args, secondInitial) },
            first with { Seq = 1, Args = WithIndex(first.Args, firstAfterSecond) },
            suffix[2] with { Seq = 2 },
        };
        return new Dictionary<string, IReadOnlyList<ActionRecord>>(StringComparer.Ordinal)
        {
            ["declared-order"] = suffix,
            ["reordered"] = reordered,
        };
    }

    private static IReadOnlyDictionary<string, string> WithIndex(
        IReadOnlyDictionary<string, string> args, int index)
    {
        var copy = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in args) copy[key] = value;
        copy["hand_index"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return copy;
    }
}

using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// A minimal valid manifest, and helpers for breaking one rule at a time.
///
/// Every negative test starts from something the validator accepts and changes
/// exactly one thing. That is what makes a failure attributable: a fixture built
/// wrong in three ways proves only that the validator dislikes it.
/// </summary>
internal static class Fixtures
{
    internal static ReplayManifest ValidManifest() => new()
    {
        RunId = "test-run",
        Environment = new EnvironmentIdentity
        {
            BuildVersion = Fact<string>.Observed("v0.111.0", FactEvidence.AtVideoTime(1000, "overlay")),
            BuildDateUtc = Fact<string>.Observed("2026.08.14", FactEvidence.AtVideoTime(1000, "overlay")),
            GameMode = Fact<string>.Inferred("standard", FactEvidence.Reasoning("not date-formatted")),
            Seed = Fact<string>.Observed("SFXT47K77RFK", FactEvidence.AtVideoTime(1000, "overlay")),
            ContentHash = Fact<string>.Observed("1568834832", FactEvidence.AtVideoTime(1000, "overlay")),
            Ascension = Fact<int>.Observed(10, FactEvidence.AtVideoTime(1000, "badge")),
            Character = Fact<string>.Observed("CHARACTER.IRONCLAD", FactEvidence.AtVideoTime(1000, "sprite")),
            Acts = Fact<IReadOnlyList<string>>.Inferred(
                ["ACT.UNDERDOCKS"], FactEvidence.Reasoning("map screen title")),
        },
        Source = new SourceProvenance
        {
            Kind = "vod",
            Video = new VideoSource
            {
                Platform = "youtube",
                VideoId = "OJ-6QXhNgdg",
                ChannelId = "UCuuDxwofGcur0Lt6iP-aDww",
                DurationSeconds = 2049,
            },
            ExtractionMethod = "manual",
            Coverage = "opening turn only",
        },
        Actions =
        [
            new ActionRecord
            {
                Seq = 0,
                Verb = ActionVerb.ChooseNeowBlessing,
                Args = new SortedDictionary<string, string>(StringComparer.Ordinal) { ["option_index"] = "2" },
                Source = FactSource.Observed,
                Evidence = FactEvidence.AtVideoTime(26000, "effect on max health"),
            },
            new ActionRecord
            {
                Seq = 1,
                Verb = ActionVerb.MapMove,
                Args = new SortedDictionary<string, string>(StringComparer.Ordinal)
                {
                    ["row"] = "1",
                    ["column"] = "3",
                },
                Source = FactSource.Observed,
                Evidence = FactEvidence.AtVideoTime(73500, "ringed node on the map"),
            },
        ],
        Checkpoints =
        [
            new Checkpoint
            {
                Id = "combat-start",
                AfterSeq = 1,
                Kind = "combat_start",
                Expect = new Dictionary<string, Fact<string>>(StringComparer.Ordinal)
                {
                    ["combat.energy"] = Fact<string>.Observed("3", FactEvidence.AtVideoTime(75600, "energy orb")),
                },
            },
        ],
    };

    internal static ActionRecord Action(int seq, ActionVerb verb, params (string Key, string Value)[] args)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in args) map[key] = value;
        return new ActionRecord
        {
            Seq = seq,
            Verb = verb,
            Args = map,
            Source = FactSource.Observed,
            Evidence = FactEvidence.AtVideoTime(1000 * (seq + 1), "test fixture"),
        };
    }
}

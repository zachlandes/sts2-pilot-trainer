using System.Text.Json;

namespace EncounterIntel;

public sealed record PatternEdge(string To, string? Condition = null, float? Weight = null,
    string? WeightRule = null, string? Repeat = null, int Cooldown = 0, int MaxRepeats = 0)
{
    public string? SourceMember { get; init; }
}
public sealed record PatternAttack(decimal? Damage, int? Hits, string? Gap)
{
    public string? ReferenceRule { get; init; }
}
public sealed record PatternSummonCap(int Limit, string Authority);
public sealed record PatternNode(string Id, string Kind, IReadOnlyList<PatternEdge> Edges,
    IReadOnlyList<string> Intents, IReadOnlyList<PatternAttack> Attacks, bool MustPerformOnce,
    bool ReachableFromOpening, int? SelfStrengthGain, IReadOnlyList<string> Gaps)
{
    public IReadOnlyList<string> WikiMoveNames { get; init; } = [];
    public string? SourceMember { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PatternSummonCap? GroupSummonCap { get; init; }
}
public sealed record PatternVariant(string Context, IReadOnlyList<string> EncounterIds, string? Slot,
    string InitialState, string? OpeningMove, IReadOnlyList<PatternNode> Nodes,
    IReadOnlyList<PatternScheduleRow> Schedule, IReadOnlyList<string> Gaps);
public sealed record PatternScheduleRow(int Turn, string Move, IReadOnlyList<PatternAttack> Attacks,
    int? AccumulatedStrength, string? StopsAfter);
public sealed record PatternPhase(string Trigger, string Rule, IReadOnlyList<string> MonsterIds, string Authority);
public sealed record EnemyPattern(string Id, bool ConstructsWithoutFight, IReadOnlyList<PatternVariant> Variants,
    IReadOnlyList<PatternPhase> Phases, IReadOnlyList<string> Gaps);
public sealed record PatternColumn(int Ascension, IReadOnlyList<EnemyPattern> Enemies);
public sealed record PatternHookCoverage(string Member, string Standing, string Reason);
public sealed record PatternAtlas(int Schema, BuildIdentity Build, string Adapter,
    IReadOnlyList<PatternColumn> Columns, IReadOnlyList<string> Gaps)
{
    public IReadOnlyList<PatternHookCoverage> ReactiveCoverage { get; init; } = [];
    public int MaximumAscension { get; init; }

    public static PatternAtlas Read()
    {
        var build = SupportedBuild.Require();
        var assembly = typeof(PatternAtlas).Assembly;
        var expected = $"EncounterIntel.{build.AssemblySha256}/patterns.json";
        // RecursiveDir uses the build host's separator in embedded resource names
        var names = assembly.GetManifestResourceNames().Where(name => name.Replace('\\', '/') == expected).ToArray();
        if (names.Length != 1) throw new IntelRefusal("This identified build has no unique move-graph atlas.");
        using var stream = assembly.GetManifestResourceStream(names[0])
            ?? throw new IntelRefusal("This identified build has no move-graph atlas.");
        var atlas = JsonSerializer.Deserialize<PatternAtlas>(stream, PatternJson.Options)
            ?? throw new IntelRefusal("The move-graph atlas is unreadable.");
        Validate(atlas, build);
        return atlas;
    }

    public static void Validate(PatternAtlas atlas, BuildIdentity build)
    {
        if (atlas.Schema != 1 || atlas.Adapter != "move-graph-v1" || atlas.Build != build)
            throw new IntelRefusal("The move-graph atlas belongs to another build or graph schema.");
        if (atlas.Columns.Count == 0 || atlas.Columns[0].Ascension != 0 || atlas.MaximumAscension < 0 ||
            atlas.Columns.Any(column => column.Ascension < 0 || column.Ascension > atlas.MaximumAscension) ||
            !atlas.Columns.Select(column => column.Ascension).SequenceEqual(atlas.Columns.Select(column => column.Ascension).Distinct().Order()))
            throw new IntelRefusal("The move-graph atlas has missing, unordered or duplicate ascension columns.");
        foreach (var column in atlas.Columns)
        {
            if (column.Enemies.Count == 0 || column.Enemies.Select(enemy => enemy.Id).Distinct().Count() != column.Enemies.Count)
                throw new IntelRefusal("The move-graph atlas has missing or duplicate enemy rows.");
            foreach (var variant in column.Enemies.SelectMany(enemy => enemy.Variants))
            {
                var ids = variant.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
                if (variant.Nodes.Any(node => node.Kind is not ("move" or "conditional" or "random" or "unsupported") || node.Kind == "unsupported" && node.Gaps.Count == 0))
                    throw new IntelRefusal("The move-graph atlas has an unknown or unflagged state shape.");
                if (variant.Nodes.Any(node => node.GroupSummonCap is { } cap &&
                    (node.Kind != "move" || cap.Limit <= 0 || string.IsNullOrWhiteSpace(cap.Authority))))
                    throw new IntelRefusal("The move-graph atlas has an invalid or unattributed group summon cap.");
                if (ids.Count != variant.Nodes.Count || !ids.Contains(variant.InitialState) ||
                    variant.OpeningMove is { } opening && !ids.Contains(opening) ||
                    variant.Nodes.SelectMany(node => node.Edges).Any(edge => !ids.Contains(edge.To)))
                    throw new IntelRefusal("The move-graph atlas has a dangling or duplicate state.");
            }
        }
    }
}

public static class PatternJson
{
    public static JsonSerializerOptions Options => new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, WriteIndented = true };

    public static string SerializeAtlas(PatternAtlas atlas)
    {
        var compact = Options;
        compact.WriteIndented = false;
        var header = JsonSerializer.Serialize(atlas with { Columns = [] }, Options);
        var columns = atlas.Columns.Select(column => "    {\"ascension\":" + column.Ascension.ToString(System.Globalization.CultureInfo.InvariantCulture) +
            ",\"enemies\":[\n" + string.Join(",\n", column.Enemies.Select(enemy => "      " + JsonSerializer.Serialize(enemy, compact))) + "\n    ]}");
        return header.Replace("\"columns\": []", "\"columns\": [\n" + string.Join(",\n", columns) + "\n  ]", StringComparison.Ordinal) + "\n";
    }
}

public static class PatternSchedules
{
    // A reference line follows only fixed edges, never a predicate or a random roll
    public static IReadOnlyList<PatternScheduleRow> ReferenceLine(string? opening, IReadOnlyList<PatternNode> nodes, int turns = 9)
    {
        if (opening is null) return [];
        var byId = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var rows = new List<PatternScheduleRow>();
        var next = opening;
        int? strength = 0;
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (var turn = 1; turn <= turns; turn++)
        {
            if (!byId.TryGetValue(next, out var node) || node.Kind != "move") break;
            var firstUse = used.Add(node.Id);
            var attacks = node.Attacks.Select(attack => attack with
            {
                Damage = (firstUse || attack.ReferenceRule is null) && attack.Damage is { } damage && strength is { } gain ? damage + gain : null,
                Hits = firstUse || attack.ReferenceRule is null ? attack.Hits : null,
                Gap = !firstUse && attack.ReferenceRule is not null ? "Repeated dynamic attack; consult its rule rather than reusing first-use quantities."
                    : strength is null ? "Prior effects can change damage; no verified cumulative reference quantity." : attack.Gap,
            }).ToArray();
            var edge = node.Edges.Count == 1 && node.Edges[0].Condition is null && node.Edges[0].Repeat is null ? node.Edges[0] : null;
            var stop = edge is null ? "No fixed follow-up; read the branch or reactive rule." : byId[edge.To].Kind != "move"
                ? "Next decision is a branch; no roll or predicate was evaluated." : null;
            rows.Add(new(turn, node.Id, attacks, strength, stop));
            if (stop is not null) break;
            strength = node.SelfStrengthGain is { } amount && strength is { } current ? current + amount : null;
            next = edge!.To;
        }
        return rows;
    }
}

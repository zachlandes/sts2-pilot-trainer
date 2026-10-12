using System.Text.Json;

namespace EncounterIntel;

public sealed record WikiPatternClaim(string Kind, IReadOnlyList<string> Moves, int SourceLine, string SourceText, AscendingValue? Amount)
{
    public IReadOnlyList<decimal>? Weights { get; init; }
    public string? Scope { get; init; }
    public string? Repeat { get; init; }
}
public sealed record WikiPageGap(string Name, string Reason);
public sealed record WikiPatternRegion(int SourceLine, string Text);
public sealed record WikiPatternPage(string Name, WikiSource Source, string SourceText)
{
    public IReadOnlyList<WikiPatternRegion> PatternRegions { get; init; } = [];
    public IReadOnlyList<WikiPatternClaim> Claims { get; init; } = [];
    public IReadOnlyList<string> Gaps { get; init; } = [];
    public IReadOnlyList<WikiSource> RedirectSources { get; init; } = [];
}
public sealed record WikiPatternPages(int Schema, string Acquisition, IReadOnlyList<WikiPatternPage> Pages)
{
    public IReadOnlyList<WikiPageGap> UnavailablePages { get; init; } = [];
    public static WikiPatternPages Read()
    {
        using var stream = typeof(WikiPatternPages).Assembly.GetManifestResourceStream("EncounterIntel.wiki-pattern-pages.json")
            ?? throw new IntelRefusal("Enemy-page source receipt is absent.");
        var pages = JsonSerializer.Deserialize<WikiPatternPages>(stream, PatternJson.Options)
            ?? throw new IntelRefusal("Enemy-page source receipt is unreadable.");
        if (pages.Schema != 1 || pages.Pages.Select(page => page.Name).Distinct(StringComparer.Ordinal).Count() != pages.Pages.Count)
            throw new IntelRefusal("Enemy-page source receipt has an unknown schema or duplicate page.");
        return pages;
    }
}

public sealed record EncounterPatternMember(string MonsterId, IReadOnlyList<PatternVariant> OpeningAlternatives,
    IReadOnlyList<PatternPhase> Phases, IReadOnlyList<string> Gaps);
public sealed record EncounterScheduleColumn(int Ascension, IReadOnlyList<EncounterPatternMember> PossibleMembers);

public static partial class RuntimeAdapter
{
    public static PatternAtlas ReadPatterns()
    {
        SupportedBuild.Require();
        RefuseChangedReadMethods();
        return PatternAtlas.Read();
    }

    private static EnemyPattern? PatternAt(PatternAtlas atlas, int ascension, string id)
    {
        if (ascension < 0 || ascension > atlas.MaximumAscension) throw new IntelRefusal("This build's pattern atlas has no such ascension column.");
        return atlas.Columns.Last(column => column.Ascension <= ascension).Enemies.SingleOrDefault(enemy => enemy.Id == id);
    }

    private static CatalogueMonster WithPattern(CatalogueMonster monster, PatternAtlas atlas, WikiPatternPages pages, int ascension)
    {
        var source = pages.Pages.FirstOrDefault(page => monster.Wiki.Any(enemy => enemy.Name == page.Name));
        var pattern = PatternAt(atlas, ascension, monster.Id);
        var comparison = source is null ? WikiPatternComparison.Unavailable with
        {
            Reason = WikiPatternComparison.Unavailable.Reason + " " + string.Join("; ", pages.UnavailablePages.Where(page => monster.Wiki.Any(enemy => enemy.Name == page.Name)).Select(page => page.Reason)) + " Acquisition: " + pages.Acquisition,
        } : WikiPatternComparison.Compare(source, pattern, ascension);
        monster = monster with { PatternComparison = comparison };
        if (pattern is null) return monster with { Gaps = [.. monster.Gaps, "No graph row for this monster on this build."] };
        var nodes = pattern.Variants.FirstOrDefault()?.Nodes ?? [];
        var moves = monster.Moves.Select(move =>
        {
            var keys = monster.Wiki.Where(enemy => enemy.Moves.Contains(move.Wiki)).Select(enemy => enemy.Name + "." + move.Wiki.Name).ToHashSet(StringComparer.Ordinal);
            var candidates = nodes.Where(node => node.WikiMoveNames.Any(keys.Contains)).ToArray();
            var values = move.Quantities.ToList();
            foreach (var quantity in move.Wiki.Quantities.Where(quantity => quantity.Kind is "damage" or "hits"))
            {
                if (move.Wiki.Quantities.Count(value => value.Kind == quantity.Kind) != 1 || candidates.Length == 0) continue;
                var numbers = candidates.Select(node => node.Attacks.Count == 1 ? quantity.Kind == "damage"
                    ? node.Attacks[0].Damage : node.Attacks[0].Hits : null).Distinct().ToArray();
                if (numbers.Length != 1 || numbers[0] is not { } number || number != decimal.Truncate(number)) continue;
                values.Add(new("graph." + quantity.Kind + "@" + quantity.Offset, quantity.Amount.At(ascension), (int)number, QuantityAuthority.ConstructedGraph));
            }
            return move with { Quantities = values };
        }).ToArray();
        return monster with { Pattern = pattern, Moves = moves };
    }

    private static IReadOnlyList<EncounterScheduleColumn> SchedulesFor(CatalogueEncounter encounter, PatternAtlas atlas) =>
        atlas.Columns.Select(column => new EncounterScheduleColumn(column.Ascension,
            encounter.PossibleMonsterIds.Select(id =>
            {
                var enemy = column.Enemies.SingleOrDefault(enemy => enemy.Id == id);
                if (enemy is null) return new EncounterPatternMember(id, [], [], ["No constructed graph on this build; consult the enemy's separate wiki fallback receipt."]);
                var contexts = enemy.Variants.Where(variant => variant.EncounterIds.Contains(encounter.Id, StringComparer.Ordinal)).ToArray();
                var gaps = new List<string>(enemy.Gaps);
                if (contexts.Length == 0)
                {
                    // Definitions are references, never a hypothetical opening roster
                    contexts = enemy.Variants.Take(1).Select(variant => variant with
                    {
                        Context = "Definition reference, not an opening roster",
                        OpeningMove = enemy.Variants.Count == 1 ? variant.OpeningMove : null,
                        Schedule = enemy.Variants.Count == 1 ? variant.Schedule : [],
                    }).ToArray();
                    if (enemy.Variants.Count > 1) gaps.Add("No inspected per-encounter opening binding; context alternatives are not chosen or guessed.");
                }
                gaps.Add("Possible member only: no count, party, chosen offset or dealt encounter was generated.");
                return new EncounterPatternMember(id, contexts, enemy.Phases, gaps);
            }).ToArray())).ToArray();
}

namespace EncounterIntel;

public enum CatalogueStanding { Catalogued, Flagged, Unsupported }
public sealed record CheckedMove(WikiMove Wiki, IReadOnlyList<ResolvedQuantity> Quantities,
    IReadOnlyList<string> Gaps);
public sealed record CatalogueMonster(string Id, IReadOnlyList<WikiEnemy> Wiki,
    IReadOnlyList<ResolvedQuantity> Quantities, IReadOnlyList<CheckedMove> Moves,
    MonsterRule? ReviewedRule, IReadOnlyList<string> Gaps)
{
    public EnemyPattern? Pattern { get; init; }
    public WikiPatternComparison PatternComparison { get; init; } = WikiPatternComparison.Unavailable;
    public bool MayBeOutdated => Quantities.Concat(Moves.SelectMany(move => move.Quantities)).Any(value => value.Mismatch);
    public string? Warning => MayBeOutdated ? "May be outdated: wiki and game quantities disagree." : null;
    public CatalogueStanding Standing => Wiki.Count == 0 ? CatalogueStanding.Unsupported
        : MayBeOutdated || Gaps.Count > 0 || Moves.Any(move => move.Gaps.Count > 0) ? CatalogueStanding.Flagged
        : CatalogueStanding.Catalogued;
}
public sealed record CatalogueEncounter(string Id, IReadOnlyList<string> PossibleMonsterIds,
    IReadOnlyList<WikiEncounter> RelatedWikiCompositions, EncounterRule? ReviewedRule,
    IReadOnlyList<string> Gaps)
{
    public IReadOnlyList<EncounterScheduleColumn> DamageSchedules { get; init; } = [];
    public CatalogueStanding Standing => RelatedWikiCompositions.Count == 0 && ReviewedRule is null ? CatalogueStanding.Unsupported
        : Gaps.Count > 0 ? CatalogueStanding.Flagged : CatalogueStanding.Catalogued;
}
public sealed record CatalogueCoverage(string Kind, string Id, CatalogueStanding Standing,
    IReadOnlyList<string> Reasons);
public sealed record CatalogueReading(BuildIdentity Build, int Ascension, WikiAttribution Attribution,
    IReadOnlyList<WikiSource> Sources, IReadOnlyList<CatalogueMonster> Monsters,
    IReadOnlyList<CatalogueEncounter> Encounters, IReadOnlyList<CatalogueCoverage> Coverage)
{
    public IReadOnlyList<string> PatternGaps { get; init; } = [];
}

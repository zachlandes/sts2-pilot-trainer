using System.Globalization;

namespace EncounterIntel;

public sealed record PatternClaimCheck(WikiPatternClaim Claim, string Standing, IReadOnlyList<string> Contexts, string Reason)
{
    public decimal? WikiValue { get; init; }
    public decimal? GameValue { get; init; }
}
public sealed record WikiPatternComparison(string Standing, string Reason, string? SourceText)
{
    public IReadOnlyList<PatternClaimCheck> Checks { get; init; } = [];
    public IReadOnlyList<string> Gaps { get; init; } = [];
    public WikiSource? Source { get; init; }
    public IReadOnlyList<WikiSource> RedirectSources { get; init; } = [];
    public bool Mismatch => Checks.Any(check => check.Standing == "mismatch");
    public static WikiPatternComparison Unavailable => new("unavailable",
        "No cached enemy-page pattern text is available for this enemy; pinned modules contain no pattern text.", null);

    public static WikiPatternComparison Compare(WikiPatternPage page, EnemyPattern? pattern, int ascension)
    {
        if (page.SourceText.TrimStart().StartsWith("#REDIRECT", StringComparison.OrdinalIgnoreCase))
            return Unavailable with { Reason = "Only a redirect was cached; the enemy-page target is unavailable.", Source = page.Source, Gaps = page.Gaps };
        var checks = page.Claims.Select(claim => Check(claim, pattern, ascension)).ToArray();
        var standing = checks.Any(check => check.Standing == "mismatch") ? "mismatch"
            : checks.Any(check => check.Standing == "matched") ? "partial-agreement" : "unchecked";
        return new(standing,
            "Only the quoted literal claims are held to the constructed graph; remaining prose, qualifiers and reactive triggers are editorial fallback.",
            page.PatternText ?? page.SourceText)
        {
            Checks = checks,
            Gaps = page.Gaps,
            Source = page.Source,
            RedirectSources = page.RedirectSources,
        };
    }

    private static PatternClaimCheck Check(WikiPatternClaim claim, EnemyPattern? pattern, int ascension)
    {
        PatternClaimCheck Unresolved(string reason) => new(claim, "unresolved", [], reason);
        if (pattern is null || pattern.Variants.Count == 0) return Unresolved("No constructed graph; source text remains fallback, not agreement.");
        if (claim.Moves.Count == 0) return Unresolved("The source claim names no move.");
        bool Names(PatternNode node, string key) => node.WikiMoveNames.Any(name => Identity(name) == Identity(key));
        if (claim.Kind is "damage" or "hits")
        {
            if (claim.Moves.Count != 1 || claim.Amount is null) return Unresolved("Unrecognized quantity claim shape.");
            var nodes = pattern.Variants.SelectMany(variant => variant.Nodes).Where(node => Names(node, claim.Moves[0])).ToArray();
            if (nodes.Length == 0) return Unresolved("No inspected move alias binds this source quantity.");
            if (nodes.Any(node => node.Attacks.Count != 1 || node.Attacks[0].ReferenceRule is not null))
                return Unresolved("Dynamic or multiple attack quantities are not a single context-free reference.");
            var values = nodes.Select(node => claim.Kind == "damage" ? node.Attacks[0].Damage : node.Attacks[0].Hits).Distinct().ToArray();
            if (values.Length != 1 || values[0] is not { } game) return Unresolved("No unique constructed reference quantity across contexts.");
            var wiki = claim.Amount.At(ascension);
            return new(claim, game == wiki ? "matched" : "mismatch", [],
                $"Quoted reference {claim.Kind} at A{ascension}: wiki {wiki}, constructed graph {game.ToString(CultureInfo.InvariantCulture)}; before powers and interference.")
            {
                WikiValue = wiki,
                GameValue = game,
            };
        }
        if (claim.Kind == "opening-alternative")
        {
            if (claim.Moves.Count != 1 || !pattern.Variants.SelectMany(variant => variant.Nodes).Any(node => Names(node, claim.Moves[0])))
                return Unresolved("No inspected move alias binds this source opening.");
            var contexts = pattern.Variants.Where(variant => variant.OpeningMove is { } opening &&
                variant.Nodes.Single(node => node.Id == opening) is { } node && Names(node, claim.Moves[0])).Select(variant => variant.Context).ToArray();
            if (contexts.Length > 0) return new(claim, "matched", contexts,
                "This named opening exists among inspected contexts; the prose qualifier is not verified as a universal encounter rule.");
            if (pattern.Variants.Any(variant => variant.OpeningMove is null)) return Unresolved("An opening is conditional or random; no predicate or roll was evaluated.");
            return new(claim, "mismatch", pattern.Variants.Select(variant => variant.Context).ToArray(), "None of the constructed fixed openings names this move.");
        }
        if (claim.Kind is not ("fixed-cycle" or "fixed-path" or "self-loop")) return Unresolved("Uninterpreted source claim kind; not approximated.");
        if (claim.Moves.Select(Identity).Distinct().Count() != claim.Moves.Count)
            return Unresolved("Repeated display moves need distinct state bindings; no occurrence was guessed.");
        var mapped = new List<(PatternVariant Variant, PatternNode[] Nodes)>();
        foreach (var variant in pattern.Variants)
        {
            var matches = claim.Moves.Select(key => variant.Nodes.Where(node => Names(node, key)).ToArray()).ToArray();
            if (matches.All(nodes => nodes.Length == 1)) mapped.Add((variant, matches.Select(nodes => nodes[0]).ToArray()));
        }
        if (mapped.Count == 0) return Unresolved("No unique inspected alias for every move in the quoted sequence.");
        bool TwoWayNonrepeat(PatternNode from, PatternNode branch) => branch.Kind == "random" && branch.Edges.Count == 2 &&
            branch.Edges.All(edge => edge.Repeat == "CannotRepeat" && edge.Cooldown == 0 && edge.Weight > 0 && edge.WeightRule is null) &&
            branch.Edges.Select(edge => edge.To).Distinct().Count() == 2 && branch.Edges.Any(edge => edge.To == from.Id);
        bool Follows(PatternNode from, PatternNode to, IReadOnlyList<PatternNode> all)
        {
            if (from.Kind != "move" || from.Edges.Count != 1 || from.Edges[0].Condition is not null || from.Edges[0].Repeat is not null) return false;
            if (from.Edges[0].To == to.Id) return true;
            var branch = all.Single(node => node.Id == from.Edges[0].To);
            // With two positive candidates, CannotRepeat leaves exactly the other one
            return TwoWayNonrepeat(from, branch) && branch.Edges.Single(edge => edge.To != from.Id).To == to.Id;
        }
        foreach (var row in mapped)
        {
            var froms = claim.Kind == "fixed-path" ? row.Nodes.SkipLast(1) : row.Nodes;
            if (froms.Any(from => from.Edges.Count == 0 || from.Edges.Any(edge => row.Variant.Nodes.Single(node => node.Id == edge.To) is { } target &&
                target.Kind != "move" && !TwoWayNonrepeat(from, target))))
                return Unresolved("The quoted sequence reaches a predicate, unresolved random branch or reactive follow-up; no decision was evaluated.");
        }
        bool Matches(PatternVariant variant, PatternNode[] nodes)
        {
            if (claim.Kind == "self-loop") return nodes.Length == 1 && Follows(nodes[0], nodes[0], variant.Nodes);
            if (nodes.Length < 2) return false;
            return nodes.Zip(nodes.Skip(1)).All(pair => Follows(pair.First, pair.Second, variant.Nodes)) &&
                (claim.Kind == "fixed-path" || Follows(nodes[^1], nodes[0], variant.Nodes));
        }
        var matching = mapped.Where(row => Matches(row.Variant, row.Nodes)).Select(row => row.Variant.Context).ToArray();
        if (matching.Length == mapped.Count) return new(claim, "matched", matching,
            "Quoted fixed edges agree in the listed graph contexts; trigger and predicate qualifiers remain unverified.");
        if (matching.Length > 0) return Unresolved("Context variants disagree about these fixed edges; no context was guessed.");
        return new(claim, "mismatch", mapped.Select(row => row.Variant.Context).ToArray(),
            "The quoted fixed sequence differs from the constructed edges or crosses a branch the wiki sequence omits.");
    }

    private static string Identity(string value) => string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
}

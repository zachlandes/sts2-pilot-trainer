using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Models;

namespace EncounterIntel;

public static partial class RuntimeAdapter
{
    private static readonly IReadOnlyDictionary<string, string[]> WikiAliases = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["AssassinRubyRaider"] = ["Assassin Raider"],
        ["AxeRubyRaider"] = ["Axe Raider"],
        ["BruteRubyRaider"] = ["Brute Raider"],
        ["CrossbowRubyRaider"] = ["Crossbow Raider"],
        ["TrackerRubyRaider"] = ["Tracker Raider"],
        ["FakeMerchantMonster"] = ["The Merchant???"],
        ["BattleFriendV1"] = ["Battle Friend V1.0"],
        ["BattleFriendV2"] = ["Battle Friend V2.0"],
        ["BattleFriendV3"] = ["Battle Friend V3.0"],
        ["DecimillipedeSegmentFront"] = ["Decimillipede"],
        ["DecimillipedeSegmentMiddle"] = ["Decimillipede"],
        ["DecimillipedeSegmentBack"] = ["Decimillipede"],
        ["TestSubject"] = ["Test Subject", "Test Subject (Phase 2)", "Test Subject (Phase 3)"],
    };

    public static CatalogueReading ReadCatalogue(WikiCatalogue? catalogue = null)
    {
        var run = CurrentRun() ?? throw new IntelRefusal("There is no run for ascension-sensitive catalogue quantities.");
        var inputs = ReferenceInputs.Read(catalogue);
        var wiki = inputs.Wiki;
        var monsters = new List<CatalogueMonster>();
        var encounters = new List<CatalogueEncounter>();
        var coverage = new List<CatalogueCoverage>();
        var usedWiki = new HashSet<string>(StringComparer.Ordinal);

        // Metadata supplies the denominator even when a headless stub cannot load a type
        using var stream = File.OpenRead(typeof(MonsterModel).Assembly.Location);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        foreach (var definition in metadata.TypeDefinitions.Select(metadata.GetTypeDefinition))
        {
            var space = metadata.GetString(definition.Namespace);
            var kind = space == "MegaCrit.Sts2.Core.Models.Monsters" || space.StartsWith("MegaCrit.Sts2.Core.Models.Monsters.", StringComparison.Ordinal) ? "monster"
                : space == "MegaCrit.Sts2.Core.Models.Encounters" || space.StartsWith("MegaCrit.Sts2.Core.Models.Encounters.", StringComparison.Ordinal) ? "encounter" : null;
            if (kind is null) continue;
            var name = metadata.GetString(definition.Name);
            var fullName = space + "." + name;
            Type? type;
            try { type = typeof(MonsterModel).Assembly.GetType(fullName, throwOnError: true); }
            catch (TypeLoadException error)
            {
                coverage.Add(new(kind, fullName, CatalogueStanding.Unsupported, ["Type cannot be loaded: " + error.Message]));
                continue;
            }
            if (type is null || !(kind == "monster" ? typeof(MonsterModel) : typeof(EncounterModel)).IsAssignableFrom(type)) continue;
            if (type.IsAbstract || !ModelDb.Contains(type))
            {
                coverage.Add(new(kind, fullName, CatalogueStanding.Unsupported,
                    [type.IsAbstract ? "Abstract model, not an encounter or enemy instance." : "Model is not registered by this build."]));
                continue;
            }
            var id = ModelDb.GetId(type);
            if (space.EndsWith(".Mocks", StringComparison.Ordinal))
            {
                coverage.Add(new(kind, id.ToString(), CatalogueStanding.Unsupported, ["Game test mock, not a shipped encounter or enemy."]));
                continue;
            }
            if (kind == "monster")
            {
                var model = ModelDb.GetById<MonsterModel>(id);
                RequireNative(model);
                var row = CatalogueMonsterFor(model, inputs, run.AscensionLevel);
                foreach (var entry in row.Wiki) usedWiki.Add(entry.Name);
                monsters.Add(row);
                coverage.Add(new(kind, row.Id, row.Standing, MonsterReasons(row)));
            }
            else
            {
                var model = ModelDb.GetById<EncounterModel>(id);
                RequireNative(model);
                encounters.Add(CatalogueEncounterFor(model, inputs));
            }
        }

        foreach (var row in encounters)
            coverage.Add(new("encounter", row.Id, row.Standing, row.Gaps));
        foreach (var model in ModelDb.All.Where(model => model is MonsterModel or EncounterModel))
        {
            if (coverage.Any(row => row.Id == model.Id.ToString())) continue;
            coverage.Add(new(model is MonsterModel ? "monster" : "encounter", model.Id.ToString(), CatalogueStanding.Unsupported,
                [$"Registered definition outside the native catalogue namespaces: {model.GetType().FullName}."]));
        }
        foreach (var composition in wiki.Encounters)
            coverage.Add(new("wiki-encounter", composition.Id, CatalogueStanding.Flagged, composition.Gaps));
        foreach (var enemy in wiki.Enemies.Where(enemy => !usedWiki.Contains(enemy.Name)))
            coverage.Add(new("wiki-enemy", enemy.Name, CatalogueStanding.Unsupported,
                ["No registered monster identity on this build; editorial record retained.", .. enemy.Gaps]));
        return new(SupportedBuild.Require(), run.AscensionLevel, wiki.Attribution, wiki.Sources,
            monsters.OrderBy(row => row.Id, StringComparer.Ordinal).ToArray(),
            encounters.OrderBy(row => row.Id, StringComparer.Ordinal).ToArray(),
            coverage.OrderBy(row => row.Kind, StringComparer.Ordinal).ThenBy(row => row.Id, StringComparer.Ordinal).ToArray())
        {
            PatternGaps = inputs.Provenance.Gaps,
        };
    }

    private sealed record ReferenceInputs(WikiCatalogue Wiki, PatternAtlas Patterns, WikiPatternPages Pages,
        IReadOnlyDictionary<string, MonsterRule> Monsters, IReadOnlyDictionary<string, EncounterRule> Encounters)
    {
        internal CatalogueProvenance Provenance => new(Wiki.Attribution, Wiki.Sources,
            [.. Patterns.Gaps, "Enemy-page source receipt: " + Pages.Acquisition]);

        internal static ReferenceInputs Read(WikiCatalogue? catalogue = null)
        {
            var wiki = catalogue ?? WikiCatalogue.Read();
            var patterns = ReadPatterns();
            var legacy = patterns.Build.AssemblySha256 == SupportedBuild.AssemblySha256;
            return new(wiki, patterns, WikiPatternPages.Read(),
                legacy ? ReviewedExamples.Monsters(wiki).ToDictionary(rule => rule.Id, StringComparer.Ordinal) : new Dictionary<string, MonsterRule>(),
                legacy ? ReviewedExamples.Encounters().ToDictionary(rule => rule.Id, StringComparer.Ordinal) : new Dictionary<string, EncounterRule>());
        }
    }

    private static CatalogueMonster CatalogueMonsterFor(MonsterModel model, ReferenceInputs inputs, int ascension)
    {
        RequireNative(model);
        return WithPattern(CheckCatalogueMonster(model, WikiEntriesFor(model, inputs), inputs.Monsters.GetValueOrDefault(model.Id.ToString()), ascension),
            inputs.Patterns, inputs.Pages, ascension);
    }

    private static IReadOnlyList<WikiEnemy> WikiEntriesFor(MonsterModel model, ReferenceInputs inputs)
    {
        var name = model.GetType().Name;
        var names = WikiAliases.TryGetValue(name, out var aliases) ? aliases : [name];
        return inputs.Wiki.Enemies.Where(enemy => names.Any(alias => Identity(alias) == Identity(enemy.Name))).ToArray();
    }

    private static CatalogueEncounter CatalogueEncounterFor(EncounterModel model, ReferenceInputs inputs)
    {
        RequireNative(model);
        var possible = model.AllPossibleMonsters.Select(monster =>
        {
            RequireNative(monster);
            return monster.Id.ToString();
        }).Distinct(StringComparer.Ordinal).ToArray();
        // Shared types relate editorial compositions, never establish an opening roster
        var names = model.AllPossibleMonsters.SelectMany(monster => WikiEntriesFor(monster, inputs))
            .Select(enemy => enemy.Name).ToHashSet(StringComparer.Ordinal);
        var references = inputs.Wiki.Encounters.Where(entry => entry.References.Any(reference => names.Contains(reference.EnemyName))).ToArray();
        var reviewed = inputs.Encounters.GetValueOrDefault(model.Id.ToString());
        var gaps = new List<string>();
        if (reviewed is null)
        {
            gaps.Add("Opening counts, slots, alternatives and later phases are editorial, not game-verified on this build.");
            gaps.Add(references.Length > 0
                ? "Related wiki compositions share a monster; they may describe another encounter or a later spawn, not this opening."
                : "No encounter composition in the pinned bundle for these monsters.");
        }
        if (possible.Length == 0) gaps.Add("This definition has no possible monsters.");
        var row = new CatalogueEncounter(model.Id.ToString(), possible, references, reviewed, gaps);
        return row with { DamageSchedules = SchedulesFor(row, inputs.Patterns) };
    }

    private static IReadOnlyList<string> MonsterReasons(CatalogueMonster row) =>
        [.. row.Gaps, .. row.Moves.SelectMany(move => move.Gaps),
         .. row.PatternComparison.Checks.Where(check => check.Standing == "mismatch").Select(check => "May be outdated: " + check.Reason),
         .. row.Quantities.Concat(row.Moves.SelectMany(move => move.Quantities)).Where(value => value.Mismatch)
             .Select(value => $"May be outdated: {value.Field}: wiki {value.Wiki}, game {value.Game}.")];

    private static CatalogueMonster CheckCatalogueMonster(MonsterModel model, IReadOnlyList<WikiEnemy> entries,
        MonsterRule? reviewed, int ascension)
    {
        if (reviewed is not null)
        {
            var check = Check(reviewed, model, ascension);
            return new(model.Id.ToString(), entries, check.Quantities,
                entries.SelectMany(enemy => enemy.Moves).Select(move => new CheckedMove(move, [], [])).ToArray(), reviewed, []);
        }
        var quantities = new List<ResolvedQuantity>();
        var moves = new List<CheckedMove>();
        var gaps = new List<string>();
        if (entries.Count == 0) gaps.Add("The pinned wiki bundle has no moveset for this monster.");
        foreach (var enemy in entries)
        {
            gaps.AddRange(enemy.Gaps.Select(gap => enemy.Name + ": " + gap));
            gaps.Add(enemy.Name + ": " + (enemy.Pattern.Gap ?? "Editorial pattern is not game-verified."));
            if (model.GetType().Name == "Axebot")
                gaps.Add("Axebot reference HP depends on mutable Stock context; use the existing board's live HP reading.");
            else
            {
                var hp = Regex.Match((ascension >= 8 ? enemy.AscHp : null) ?? enemy.BaseHp ?? "", @"^(\d+)(?:-(\d+))?$");
                if (!hp.Success) gaps.Add(enemy.Name + ": wiki HP range is missing or unsupported.");
                else
                {
                    var phase = enemy.Name.EndsWith("(Phase 2)", StringComparison.Ordinal) ? "SecondFormHp"
                        : enemy.Name.EndsWith("(Phase 3)", StringComparison.Ordinal) ? "ThirdFormHp" : null;
                    quantities.Add(new(enemy.Name + ".min_hp", int.Parse(hp.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                        GameValue(model, phase ?? "MinInitialHp")));
                    quantities.Add(new(enemy.Name + ".max_hp", int.Parse(hp.Groups[2].Success ? hp.Groups[2].Value : hp.Groups[1].Value,
                        System.Globalization.CultureInfo.InvariantCulture), GameValue(model, phase ?? "MaxInitialHp")));
                }
            }
            foreach (var move in enemy.Moves)
            {
                var values = new List<ResolvedQuantity>();
                var uncheckedFields = new List<string> { $"{enemy.Name}.{move.Name ?? "unnamed"}: effect text is editorial; only bound quantities are checked." };
                foreach (var quantity in move.Quantities)
                {
                    var field = $"{enemy.Name}.{move.Name}.{quantity.Kind}@{quantity.Offset}";
                    var binding = SupportedBuild.Require().AssemblySha256 == SupportedBuild.AssemblySha256
                        ? CatalogueBindings.Find(model.GetType().Name, enemy.Name, move.Name, quantity.Kind) : null;
                    if (move.Quantities.Count(candidate => candidate.Kind == quantity.Kind) != 1)
                        uncheckedFields.Add(field + ": more than one quantity of this kind; no unambiguous binding.");
                    else if (binding is null) uncheckedFields.Add(field + ": no inspected numeric getter binding.");
                    else values.Add(new(field, quantity.Amount.At(ascension), GameValue(model, binding)));
                }
                moves.Add(new(move, values, uncheckedFields));
            }
        }
        return new(model.Id.ToString(), entries, quantities, moves, reviewed, gaps);
    }

    private static string Identity(string name) => string.Concat(name.Where(char.IsAsciiLetterOrDigit)).ToUpperInvariant();
}

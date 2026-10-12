using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using EncounterIntel;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.IO;

namespace Sts2PilotTrainer.Arbiter.Tests;

public sealed partial class EncounterIntelTests
{
    [GameFact]
    public void EveryMonsterGraphIsDerivedWithoutRunFightCommandsOrRandomness()
    {
        if (Environment.GetEnvironmentVariable("ENCOUNTER_INTEL_PATTERN_CHILD") != "1")
        {
            CheckPatternsInFreshProcess();
            return;
        }
        EngineHost.Start();
        try { CheckPatterns(); }
        finally { HeadlessEngine.Forget(); }
    }

    private static void CheckPatternsInFreshProcess()
    {
        // Earlier tests can inline ascension reads before the scoped probe is installed
        var root = WorktreeLocator.Find();
        var sandbox = Path.Combine(root, "build", "test-scratch", "encounter-patterns-" + Guid.NewGuid().ToString("N"));
        var configuration = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.Name;
        var start = new ProcessStartInfo(Path.Combine(root, "scripts", "test-session.sh"))
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "tests/Sts2PilotTrainer.Mod.Tests/Sts2PilotTrainer.Mod.Tests.csproj", "-c", configuration, "--no-build", "--nologo", "--filter", "FullyQualifiedName~EveryMonsterGraphIsDerivedWithoutRunFightCommandsOrRandomness" }) start.ArgumentList.Add(argument);
        start.Environment["ENCOUNTER_INTEL_PATTERN_CHILD"] = "1";
        start.Environment["STS2_PILOT_TRAINER_SANDBOX"] = sandbox;
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Pattern probe process did not start.");
        try
        {
            var output = child.StandardOutput.ReadToEndAsync();
            var error = child.StandardError.ReadToEndAsync();
            if (!child.WaitForExit(90_000))
            {
                child.Kill(entireProcessTree: true);
                child.WaitForExit();
                throw new TimeoutException("Fresh pattern probe exceeded ninety seconds.");
            }
            var combined = output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult();
            Assert.True(child.ExitCode == 0, combined);
            Assert.Contains("TEST SESSION PASSED", combined);
            Assert.DoesNotContain("TEST SESSION FAILED", combined);
        }
        finally
        {
            if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(); }
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    private static void CheckPatterns()
    {
        HeadlessRuns.EndAnyRun();
        var root = WorktreeLocator.Find() ?? throw new InvalidOperationException("Pattern proof requires its source worktree.");
        var directory = Path.Combine(root, "src", "EncounterIntel", "catalogue", "builds", SupportedBuild.Require().AssemblySha256);
        var input = JsonSerializer.Deserialize<EncounterIntelPatternGenerator.Input>(File.ReadAllText(Path.Combine(directory, "semantics.json")), PatternJson.Options)!;
        var files = ProfileReceipt();
        var harmony = new Harmony("encounter-intel-test.graph-forbidden-calls");
        PatternAtlas atlas;
        try
        {
            var prefix = new HarmonyMethod(typeof(EncounterIntelTests).GetMethod(nameof(RejectGraphCommand), BindingFlags.Static | BindingFlags.NonPublic)!);
            foreach (var method in WatchedGameplayMembers()) harmony.Patch(method, prefix: prefix);
            Assert.Throws<IntelRefusal>(() => MegaCrit.Sts2.Core.Random.Rng.Chaotic.NextInt(1));
            atlas = EncounterIntelPatternGenerator.Generate(input);
        }
        finally { harmony.UnpatchAll(harmony.Id); }
        Assert.False(RunManager.Instance.IsInProgress);
        Assert.Equal(files, ProfileReceipt());
        PatternAtlas.Validate(atlas, SupportedBuild.Require());
        Assert.Equal(ModelDb.All.OfType<MonsterModel>().Select(model => model.Id.ToString()).Order(StringComparer.Ordinal), atlas.Columns[0].Enemies.Select(enemy => enemy.Id));
        Assert.All(atlas.Columns.SelectMany(column => column.Enemies), enemy => Assert.True(enemy.ConstructsWithoutFight, string.Join("; ", enemy.Gaps)));
        Assert.All(atlas.Columns.SelectMany(column => column.Enemies).SelectMany(enemy => enemy.Variants).SelectMany(variant => variant.Nodes), node => Assert.NotEqual("unsupported", node.Kind));
        var serialized = PatternJson.SerializeAtlas(atlas);
        var path = Path.Combine(directory, "patterns.json");
        if (Environment.GetEnvironmentVariable("ENCOUNTER_INTEL_PATTERN_UPDATE") == "1") File.WriteAllText(path, serialized);
        else Assert.Equal(File.ReadAllText(path), serialized);
        Evidence("pattern-atlas", atlas);
        var nibbit = atlas.Columns[0].Enemies.Single(enemy => enemy.Id == "MONSTER.NIBBIT");
        Assert.Equal("BUTT_MOVE", nibbit.Variants.Single(variant => variant.Context == "Alone").OpeningMove);
        var alone = nibbit.Variants.Single(variant => variant.Context == "Alone").Schedule;
        Assert.Equal(new decimal?[] { 12, 6, 14, 8, 16, 10 }, alone.Where(row => row.Attacks.Count > 0).Select(row => row.Attacks[0].Damage));
        var spinner = atlas.Columns[0].Enemies.Single(enemy => enemy.Id == "MONSTER.SLUDGE_SPINNER").Variants[0];
        Assert.All(spinner.Nodes.Single(node => node.Kind == "random").Edges, edge =>
        {
            Assert.Equal(1f, edge.Weight);
            Assert.Equal("CannotRepeat", edge.Repeat);
        });
        var phrog = atlas.Columns[0].Enemies.Single(enemy => enemy.Id == "MONSTER.PHROG_PARASITE").Variants[0];
        Assert.Contains(phrog.Nodes, node => node.Kind == "random" && !node.ReachableFromOpening);
    }

    private static void RejectGraphCommand(MethodBase __originalMethod) =>
        throw new IntelRefusal($"Graph derivation attempted forbidden gameplay member {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}.");

    [Fact]
    public void PatternAtlasRefusesAnotherBuildSchemaAndDanglingStates()
    {
        var build = new BuildIdentity("test", "date", "commit", new string('a', 64));
        var node = new PatternNode("A", "move", [new("B")], [], [], false, true, 0, []);
        var variant = new PatternVariant("test", [], null, "A", "A", [node], [], []);
        var atlas = new PatternAtlas(1, build, "move-graph-v1", [new(0, [new("MONSTER.TEST", true, [variant], [], [])])], []);
        Assert.Throws<IntelRefusal>(() => PatternAtlas.Validate(atlas, build));
        var valid = atlas with { Columns = [new(0, [new("MONSTER.TEST", true, [variant with { Nodes = [node with { Edges = [new("A")] }] }], [], [])])] };
        PatternAtlas.Validate(valid, build);
        Assert.Throws<IntelRefusal>(() => PatternAtlas.Validate(valid, build with { Commit = "other" }));
        Assert.Throws<IntelRefusal>(() => PatternAtlas.Validate(valid with { Adapter = "future-shape" }, build));
        Assert.Throws<IntelRefusal>(() => PatternAtlas.Validate(valid with { Schema = 2 }, build));
        Assert.Throws<IntelRefusal>(() => PatternAtlas.Validate(valid with { Columns = [valid.Columns[0] with { Ascension = 1 }] }, build));
        Assert.Throws<IntelRefusal>(() => PatternAtlas.Validate(valid with { Columns = [valid.Columns[0], valid.Columns[0]] }, build));
        Assert.Throws<IntelRefusal>(() => PatternAtlas.Validate(valid with
        {
            Columns = [new(0, [new("MONSTER.TEST", true, [variant with { Nodes = [node with { Kind = "future", Edges = [] }] }], [], [])])],
        }, build));
    }

    [Fact]
    public void ReferenceScheduleStopsAtBranchesAndLeavesUnknownBuffDamageAbsent()
    {
        var a = new PatternNode("A", "move", [new("B")], [], [new(5, 1, null)], false, true, null, []);
        var b = new PatternNode("B", "move", [new("RAND")], [], [new(7, 2, null)], false, true, 0, []);
        var branch = new PatternNode("RAND", "random", [new("A", Weight: 1)], [], [], false, true, 0, []);
        var schedule = PatternSchedules.ReferenceLine("A", [a, b, branch]);
        Assert.Equal(2, schedule.Count);
        Assert.Equal(5, schedule[0].Attacks[0].Damage);
        Assert.Null(schedule[1].Attacks[0].Damage);
        Assert.NotNull(schedule[1].StopsAfter);
        Assert.Empty(PatternSchedules.ReferenceLine(null, [a, b, branch]));
        var dynamic = a with { Edges = [new("A")], SelfStrengthGain = 0, Attacks = [new PatternAttack(5, 3, "First use only") { ReferenceRule = "Gains one hit after each use" }] };
        var repeated = PatternSchedules.ReferenceLine("A", [dynamic], 2);
        Assert.Equal(3, repeated[0].Attacks[0].Hits);
        Assert.Null(repeated[1].Attacks[0].Hits);
        Assert.Null(repeated[1].Attacks[0].Damage);
    }

    [Fact]
    public void ConstantWeightReaderRejectsContextBodiesWithoutExecutingThem()
    {
        var method = typeof(EncounterIntelTests).GetMethod(nameof(WeightThatMustNotExecute), BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Null(ChoiceEntryPoints.ConstantSingleReturn(method));
    }

    [GameFact]
    public void CachedEnemyPagesAreCrossCheckedAtEveryAscensionWithoutStartingARun()
    {
        EngineHost.Start();
        try
        {
            Assert.False(RunManager.Instance.IsInProgress);
            var files = ProfileReceipt();
            var atlas = PatternAtlas.Read();
            var pages = WikiPatternPages.Read();
            Assert.NotEmpty(pages.Pages);
            WikiPatternPage? SourceFor(EnemyPattern enemy) => pages.Pages.FirstOrDefault(page => enemy.Variants.SelectMany(variant => variant.Nodes)
                .SelectMany(node => node.WikiMoveNames).Any(name => name.StartsWith(page.Name + ".", StringComparison.OrdinalIgnoreCase)));
            var rows = Enumerable.Range(0, atlas.MaximumAscension + 1).SelectMany(ascension =>
                atlas.Columns.Last(column => column.Ascension <= ascension).Enemies.Select(enemy =>
                {
                    var source = SourceFor(enemy);
                    return new { enemy.Id, Ascension = ascension, Comparison = source is null ? WikiPatternComparison.Unavailable with { Reason = "No cached source bound by an inspected move alias; see the unbound-page list." } : WikiPatternComparison.Compare(source, enemy, ascension) };
                })).ToArray();
            Assert.Equal(atlas.Columns[0].Enemies.Count * (atlas.MaximumAscension + 1), rows.Length);
            Assert.All(rows.SelectMany(row => row.Comparison.Checks), check => Assert.Contains(check.Standing, new[] { "matched", "mismatch", "unresolved" }));
            var axebot = atlas.Columns[0].Enemies.Single(enemy => enemy.Id == "MONSTER.AXEBOT");
            var source = pages.Pages.Single(page => page.Name == "Axebot");
            Assert.Contains(WikiPatternComparison.Compare(source, axebot, 0).Checks, check => check.Claim.Kind == "fixed-cycle" && check.Standing == "matched");
            var edited = source with { Claims = source.Claims.Select(claim => claim.Kind == "damage" ? claim with { Amount = new(999) } : claim).ToArray() };
            Assert.True(WikiPatternComparison.Compare(edited, axebot, 0).Mismatch);
            Assert.Equal(files, ProfileReceipt());
            Assert.False(RunManager.Instance.IsInProgress);
            Evidence("wiki-pattern-crosscheck", new
            {
                atlas.Build,
                pages.Acquisition,
                pages.UnavailablePages,
                Rows = rows,
                UnboundPages = pages.Pages.Where(page => !atlas.Columns[0].Enemies.Any(enemy => SourceFor(enemy)?.Name == page.Name)).Select(page => page.Name).ToArray(),
            });
        }
        finally { HeadlessEngine.Forget(); }
    }

    [Fact]
    public void WikiClaimComparisonKeepsPartialAgreementMismatchAndUnresolvedSeparate()
    {
        var a = new PatternNode("A", "move", [new("B")], [], [new(5, 1, null)], false, true, 0, []) { WikiMoveNames = ["Test.Attack"] };
        var b = new PatternNode("B", "move", [new("A")], [], [], false, true, 0, []) { WikiMoveNames = ["Test.Buff"] };
        var enemy = new EnemyPattern("MONSTER.TEST", true, [new("Definition", [], null, "A", "A", [a, b], [], [])], [], []);
        var source = new WikiSource("synthetic", "https://example.invalid", 1, "test", new string('a', 64));
        var cycle = new WikiPatternClaim("fixed-cycle", ["Test.Attack", "Test.Buff"], 1, "Synthetic cycle claim", null);
        var damage = new WikiPatternClaim("damage", ["Test.Attack"], 2, "Synthetic numeric claim", new(5));
        var page = new WikiPatternPage("Test", source, "Synthetic source, not wiki evidence") { PatternRegions = [new(1, "Synthetic pattern")], Claims = [cycle, damage] };
        var agreement = WikiPatternComparison.Compare(page, enemy, 0);
        Assert.Equal("partial-agreement", agreement.Standing);
        var rand = new PatternNode("RAND", "random", [new("A", Weight: 1, Repeat: "CannotRepeat"), new("B", Weight: 1, Repeat: "CannotRepeat")], [], [], false, true, 0, []);
        var alternating = enemy with { Variants = [enemy.Variants[0] with { Nodes = [a with { Edges = [new("RAND")] }, b with { Edges = [new("RAND")] }, rand] }] };
        Assert.Equal("partial-agreement", WikiPatternComparison.Compare(page with { Claims = [cycle] }, alternating, 0).Standing);
        var conditional = alternating with { Variants = [alternating.Variants[0] with { Nodes = [a with { Edges = [new("RAND")] }, b with { Edges = [new("RAND")] }, rand with { Kind = "conditional" }] }] };
        Assert.Equal("unresolved", Assert.Single(WikiPatternComparison.Compare(page with { Claims = [cycle] }, conditional, 0).Checks).Standing);
        Assert.All(agreement.Checks, check => Assert.Equal("matched", check.Standing));
        Assert.Equal(source, agreement.Source);
        var mismatch = WikiPatternComparison.Compare(page with { Claims = [cycle, damage with { Amount = new(999) }] }, enemy, 0);
        Assert.True(mismatch.Mismatch);
        Assert.Equal(999, mismatch.Checks[1].WikiValue);
        Assert.Equal(5, mismatch.Checks[1].GameValue);
        Assert.Equal("mismatch", WikiPatternComparison.Compare(page with { Claims = [cycle with { Kind = "self-loop", Moves = ["Test.Attack"] }] }, enemy, 0).Standing);
        var renamed = WikiPatternComparison.Compare(page with { Claims = [cycle with { Moves = ["Test.Unbound", "Test.Buff"] }] }, enemy, 0);
        Assert.Equal("unchecked", renamed.Standing);
        Assert.Equal("unresolved", Assert.Single(renamed.Checks).Standing);
        var noGraph = WikiPatternComparison.Compare(page, null, 0);
        Assert.Equal("unchecked", noGraph.Standing);
        Assert.Equal("Synthetic pattern", noGraph.SourceText);
        Assert.Equal("unavailable", WikiPatternComparison.Compare(page with { SourceText = "#REDIRECT [[sts2:Missing]]" }, enemy, 0).Standing);
        var dynamic = enemy with { Variants = [enemy.Variants[0] with { Nodes = [a with { Attacks = [a.Attacks[0] with { ReferenceRule = "Context dependent" }] }, b] }] };
        Assert.Equal("unresolved", Assert.Single(WikiPatternComparison.Compare(page with { Claims = [damage] }, dynamic, 0).Checks).Standing);
    }

    [Fact]
    public void StatedOddsAndRepeatRulesAreHeldToDeclaredBranchWeights()
    {
        var a = new PatternNode("A", "move", [new("RAND")], [], [], false, true, 0, []) { WikiMoveNames = ["Test.Attack"] };
        var b = new PatternNode("B", "move", [new("RAND")], [], [], false, true, 0, []) { WikiMoveNames = ["Test.Buff"] };
        var opening = new PatternNode("INITIAL", "random", [new("A", Weight: 2), new("B", Weight: 1)], [], [], false, true, 0, []);
        var rand = new PatternNode("RAND", "random", [new("A", Weight: 1, Repeat: "CannotRepeat"), new("B", Weight: 1, Repeat: "CanRepeatXTimes", MaxRepeats: 2)], [], [], false, true, 0, []);
        var enemy = new EnemyPattern("MONSTER.TEST", true, [new("Definition", [], null, "INITIAL", null, [a, b, opening, rand], [], [])], [], []);
        var source = new WikiSource("synthetic", "https://example.invalid", 1, "test", new string('a', 64));
        var page = new WikiPatternPage("Test", source, "Synthetic source, not wiki evidence");
        WikiPatternClaim Odds(string scope, params decimal[] weights) =>
            new("weights", ["Test.Attack", "Test.Buff"], 1, "Synthetic odds claim", null) { Weights = weights, Scope = scope };
        WikiPatternClaim Repeat(string move, string rule) => new("repeat", [move], 1, "Synthetic repetition claim", null) { Repeat = rule };
        string Standing(WikiPatternClaim claim, EnemyPattern? graph = null) =>
            Assert.Single(WikiPatternComparison.Compare(page with { Claims = [claim] }, graph ?? enemy, 0).Checks).Standing;
        Assert.Equal("matched", Standing(Odds("opening", 67, 33)));
        Assert.Equal("mismatch", Standing(Odds("opening", 1, 1)));
        Assert.Equal("matched", Standing(Odds("branch", 1, 1)));
        Assert.Equal("mismatch", Standing(Odds("branch", 1, 2)));
        var reversed = Odds("opening", 1, 2) with { Moves = ["Test.Buff", "Test.Attack"] };
        Assert.Equal("matched", Standing(reversed));
        var withoutOpening = enemy with { Variants = [enemy.Variants[0] with { InitialState = "A", Nodes = [a, b, rand] }] };
        Assert.Equal("unresolved", Standing(Odds("opening", 1, 1), withoutOpening));
        var dynamic = enemy with { Variants = [enemy.Variants[0] with { Nodes = [a, b, opening, rand with { Edges = [rand.Edges[0] with { Weight = null, WeightRule = "Context" }, rand.Edges[1]] }] }] };
        Assert.Equal("unresolved", Standing(Odds("branch", 1, 1), dynamic));
        Assert.Equal("unresolved", Standing(Odds("branch", 1, 1) with { Moves = ["Test.Attack", "Test.Unbound"] }));
        Assert.Equal("matched", Standing(Repeat("Test.Attack", "CannotRepeat"), enemy with { Variants = [enemy.Variants[0] with { Nodes = [a, b, rand] }] }));
        Assert.Equal("mismatch", Standing(Repeat("Test.Buff", "CannotRepeat"), enemy with { Variants = [enemy.Variants[0] with { Nodes = [a, b, rand] }] }));
        Assert.Equal("mismatch", Standing(Repeat("Test.Buff", "Unrestricted"), enemy with { Variants = [enemy.Variants[0] with { Nodes = [a, b, rand] }] }));
        var once = rand with { Edges = [rand.Edges[0] with { Repeat = "UseOnlyOnce" }, rand.Edges[1] with { Repeat = "CanRepeatForever", MaxRepeats = 0 }] };
        var lasting = enemy with { Variants = [enemy.Variants[0] with { Nodes = [a, b, once] }] };
        Assert.Equal("matched", Standing(Repeat("Test.Attack", "CannotRepeat"), lasting));
        Assert.Equal("matched", Standing(Repeat("Test.Buff", "Unrestricted"), lasting));
        Assert.Equal("unresolved", Standing(Repeat("Test.Attack", "CannotRepeat")));
        Assert.Equal("unresolved", Standing(new WikiPatternClaim("unparsed", ["Test.Attack"], 1, "Synthetic unparsed odds", null)));
    }

    [Fact]
    public void CachedPagesFlagStatedOddsAndRepeatRulesTheBetaGraphContradicts()
    {
        var root = WorktreeLocator.Find() ?? throw new InvalidOperationException("The committed atlas requires its source worktree.");
        var atlas = JsonSerializer.Deserialize<PatternAtlas>(File.ReadAllText(Path.Combine(root, "src", "EncounterIntel", "catalogue", "builds",
            SupportedBuild.AssemblySha256, "patterns.json")), PatternJson.Options)!;
        var pages = WikiPatternPages.Read();
        IReadOnlyList<PatternClaimCheck> Checks(string page, string id) => WikiPatternComparison.Compare(pages.Pages.Single(row => row.Name == page),
            atlas.Columns[0].Enemies.Single(enemy => enemy.Id == id), 0).Checks;
        var flyconid = Checks("Flyconid", "MONSTER.FLYCONID");
        Assert.Equal("mismatch", flyconid.Single(check => check.Claim.Kind == "weights" && check.Claim.Scope == "opening").Standing);
        var hunter = Checks("Hunter Killer", "MONSTER.HUNTER_KILLER");
        Assert.Equal("mismatch", hunter.Single(check => check.Claim.Kind == "weights").Standing);
        Assert.Equal("matched", hunter.Single(check => check.Claim.Kind == "repeat").Standing);
        var stalker = Checks("Fossil Stalker", "MONSTER.FOSSIL_STALKER");
        Assert.Equal("matched", stalker.Single(check => check.Claim.Kind == "weights").Standing);
        Assert.All(stalker.Where(check => check.Claim.Kind == "repeat"), check => Assert.Equal("mismatch", check.Standing));
        Assert.Equal(3, stalker.Count(check => check.Claim.Kind == "repeat"));
        Assert.All(Checks("Mysterious Knight", "MONSTER.MYSTERIOUS_KNIGHT").Where(check => check.Claim.Kind == "unparsed"),
            check => Assert.Equal("unresolved", check.Standing));
        var wriggler = Checks("Wriggler", "MONSTER.WRIGGLER");
        Assert.Equal("matched", wriggler.Single(check => check.Claim.Kind == "fixed-cycle").Standing);
        var subject = pages.Pages.Single(page => page.Name == "Test Subject");
        Assert.Equal(3, subject.PatternRegions.Count);
        Assert.Contains(subject.Claims, claim => claim.Moves.SequenceEqual(new[] { "Test Subject (Phase 2).Multi-Claw" }));
        Assert.Contains(subject.Claims, claim => claim.Kind == "fixed-cycle" && claim.Moves[0] == "Test Subject (Phase 3).Lacerate");
    }

    private static float WeightThatMustNotExecute() => throw new InvalidOperationException("A context-dependent weight must not execute.");
}

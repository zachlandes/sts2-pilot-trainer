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

    private static float WeightThatMustNotExecute() => throw new InvalidOperationException("A context-dependent weight must not execute.");
}

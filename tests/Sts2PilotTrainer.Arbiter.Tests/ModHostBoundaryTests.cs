using System.Text.Json;
using Mono.Cecil;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What the shipped mod is allowed to touch, read out of the assembly rather than
/// promised in a comment.
///
/// Two claims are load-bearing enough to be worth compiling against. The mod reads
/// the player's game and never writes to it, which for an artifact that runs inside
/// somebody's client is the difference between a tool and a liability. And it is the
/// gate, not the fight: entering the captured combat is the next slice, and a host
/// that had quietly grown a path into it would be shipping an unfinished feature to
/// a player.
///
/// Both are stated as "this assembly refers to nothing in these namespaces", which
/// is checkable and does not go stale the way a list of forbidden method names
/// would: the reading all happens in Sts2PilotTrainer.Engine, which the headless
/// arbiter shares and which is allowed to patch its own process.
/// </summary>
public sealed class ModHostBoundaryTests
{
    /// <summary>
    /// The subsystems that own a player's state. A mod host that referred to any of
    /// them would be reaching around Preflight, which is the one owner of what gets
    /// read and the one place that never writes.
    /// </summary>
    private static readonly string[] PlayerStateNamespaces =
    [
        "MegaCrit.Sts2.Core.Saves",
        "MegaCrit.Sts2.Core.Unlocks",
        "MegaCrit.Sts2.Core.Timeline",
    ];

    /// <summary>
    /// Types that construct, drive or read a run in progress. S4 is where the
    /// captured fight gets entered; until then, a reference to any of these from the
    /// mod means the slice boundary moved without anybody saying so.
    /// </summary>
    private static readonly string[] RunConstructionTypes =
    [
        "MegaCrit.Sts2.Core.Runs.RunManager",
        "MegaCrit.Sts2.Core.Combat.CombatManager",
        "Sts2PilotTrainer.Engine.RunDriver",
        "Sts2PilotTrainer.Engine.GameSession",
        "Sts2PilotTrainer.Engine.Arbiter",
    ];

    private static string ModAssembly =>
        Path.Combine(
            Arbiter.RepoRoot, "build", "bin", "Sts2PilotTrainer.Mod", "Release", "net9.0", "CombatTrainer.dll");

    private static string TrainerAssembly =>
        Path.Combine(
            Arbiter.RepoRoot, "build", "bin", "Sts2PilotTrainer.Trainer", "Release", "net9.0",
            "Sts2PilotTrainer.Trainer.dll");

    [ModFact]
    public void TheModNeverReachesIntoSaveProfileOrUnlockState()
    {
        foreach (var assembly in new[] { ModAssembly, TrainerAssembly })
        {
            var offenders = MemberReferences(assembly)
                .Where(member => PlayerStateNamespaces.Any(
                    forbidden => member.StartsWith(forbidden + ".", StringComparison.Ordinal)))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            Assert.True(offenders.Count == 0, $"{Path.GetFileName(assembly)}: {string.Join(", ", offenders)}");
        }
    }

    [ModFact]
    public void TheModNeverStartsTheHeadlessEngine()
    {
        // EngineHost.Start switches the engine into test mode, neutralises the save
        // subsystem and declares the mod loader finished. Defensible in a console
        // process that owns everything; a corrupted session in a player's client.
        var offenders = MemberReferences(ModAssembly)
            .Where(member => member is "Sts2PilotTrainer.Engine.EngineHost.Start")
            .ToList();

        Assert.Empty(offenders);
    }

    [ModFact]
    public void TheModHasNoPathIntoTheFightItself()
    {
        var offenders = MemberReferences(ModAssembly)
            .Where(member => RunConstructionTypes.Any(
                type => member.StartsWith(type + ".", StringComparison.Ordinal)))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(", ", offenders));
    }

    /// <summary>
    /// The mod declares itself non-gameplay, which is not paperwork: the content
    /// hash this project gates on is a checksum over the ids contributed by mods
    /// that declare themselves gameplay-affecting. A mod that declared otherwise
    /// would change the very number its own screen compares.
    /// </summary>
    [Fact]
    public void TheModManifestDeclaresItselfNonGameplayAndPackless()
    {
        var path = Path.Combine(
            Arbiter.RepoRoot, "src", "Sts2PilotTrainer.Mod", "CombatTrainer.json");
        var manifest = JsonDocument.Parse(File.ReadAllText(path)).RootElement;

        Assert.False(manifest.GetProperty("affects_gameplay").GetBoolean());
        Assert.False(manifest.GetProperty("has_pck").GetBoolean());
        Assert.True(manifest.GetProperty("has_dll").GetBoolean());
        Assert.Empty(manifest.GetProperty("dependencies").EnumerateArray());
        Assert.Equal("CombatTrainer", manifest.GetProperty("id").GetString());
    }

    /// <summary>
    /// Every member this assembly refers to, as
    /// <c>Namespace.Type.Member</c>. Reading the references rather than the
    /// instructions is what makes the claim total: a call, a field read and a token
    /// handed to reflection all appear here.
    /// </summary>
    private static IEnumerable<string> MemberReferences(string assemblyPath)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        foreach (var reference in assembly.MainModule.GetMemberReferences())
        {
            yield return $"{reference.DeclaringType.FullName}.{reference.Name}";
        }

        foreach (var reference in assembly.MainModule.GetTypeReferences())
        {
            yield return $"{reference.FullName}.";
        }
    }
}

/// <summary>
/// Skips when the mod has not been built, which needs the game. Separate from
/// <see cref="GameFactAttribute"/> because the mod is not part of the arbiter's own
/// build and a prepared assembly alone does not imply it is there.
/// </summary>
public sealed class ModFactAttribute : FactAttribute
{
    public ModFactAttribute()
    {
        var built = Path.Combine(
            Arbiter.RepoRoot, "build", "bin", "Sts2PilotTrainer.Mod", "Release", "net9.0", "CombatTrainer.dll");
        if (!File.Exists(built))
        {
            Skip =
                "Needs the built mod. Run ./scripts/install-mod.sh, which builds it against your own " +
                "Slay the Spire 2 installation.";
        }
    }
}

using System.Reflection;
using HarmonyLib;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The seam between the shell and its features.
///
/// None of this loads a game: what is being tested is that the shell installs
/// exactly what its modules own, that a module owns every patch class in the
/// assembly, and that the surfaces a module contributes are the ones drawn.
/// </summary>
public sealed class RunmobileModuleTests
{
    [Fact]
    public void TheShellCarriesTheCombatTrainerAndNothingElseYet()
    {
        var module = Assert.Single(RunmobileMod.Modules);

        Assert.Same(CombatTrainerModule.Instance, module);
        Assert.Equal("Combat Trainer", module.Name);
    }

    /// <summary>
    /// Every Harmony-annotated type in this assembly belongs to a module.
    ///
    /// The shell installs patches module by module rather than through
    /// <c>PatchAll</c>, which is the whole point of the seam - a disabled module
    /// patches nothing. The cost of that is a list, and this is what keeps the list
    /// honest: a patch class added without an owner would silently never install.
    /// </summary>
    [GameFact]
    public void EveryPatchClassInTheAssemblyIsOwnedByAModule()
    {
        // Reading a patch class's attributes resolves the game type it patches, so
        // the game has to be loaded in this process before the question is asked.
        _ = EngineHost.StartupPhase();

        var annotated = typeof(RunmobileMod).Assembly.GetTypes()
            .Where(type => type.GetCustomAttributes<HarmonyPatch>(inherit: false).Any() ||
                           type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.DeclaredOnly)
                               .Any(method => method.GetCustomAttributes<HarmonyPatch>(inherit: false).Any()))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

        var owned = RunmobileMod.Modules
            .SelectMany(OwnedPatchClasses)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(annotated, owned);
    }

    [Fact]
    public void TheCombatTrainerIsEnabledAndCarriesItsRecording()
    {
        var module = CombatTrainerModule.Instance;

        Assert.True(module.Enabled, module.Refusal);
        Assert.Null(module.Refusal);
        Assert.NotNull(module.Recording.Source);
        Assert.NotNull(module.RecordedFight);
    }

    /// <summary>
    /// The card the menu draws is the module's, described by the module's own
    /// recording. The shell knows a title, a node name and something to open, and
    /// nothing about what a fight is.
    /// </summary>
    [Fact]
    public void TheCombatTrainerContributesOneMenuCardDescribingItsRecording()
    {
        var card = Assert.Single(RunmobileMod.MenuCards);

        Assert.Equal("CombatTrainerButton", card.NodeName);
        Assert.Equal("Combat Trainer", card.Title);
        Assert.Equal(
            RecordingIdentity.Description(CombatTrainerModule.Instance.Recording), card.Description());
    }

    /// <summary>
    /// A module that refused is skipped, by name, and never asked to install
    /// anything or to describe a surface - the stub throws if it is. The modules
    /// after it are installed anyway, which is the difference between this seam and
    /// the single entry point it replaced.
    /// </summary>
    [GameFact]
    public void ADisabledModuleIsSkippedAndTheOnesAroundItAreStillInstalled()
    {
        // The shell says what it installed through the game's own logger.
        _ = EngineHost.StartupPhase();

        var installer = new RecordingModule();
        var harmony = new Harmony($"sts2-pilot-trainer.module-seam-test.{Guid.NewGuid():N}");

        var installed = RunmobileMod.InstallModules(harmony, [new StubModule(), installer]);

        Assert.Equal(["Recording"], installed);
        Assert.True(installer.Installed);
    }

    private static IReadOnlyList<Type> OwnedPatchClasses(IRunmobileModule module) =>
        module == CombatTrainerModule.Instance ? CombatTrainerModule.PatchClasses : [];

    private sealed class RecordingModule : IRunmobileModule
    {
        internal bool Installed { get; private set; }

        public string Name => "Recording";

        public bool Enabled => true;

        public string? Refusal => null;

        public IReadOnlyList<MenuCard> MenuCards => [];

        public void Install(Harmony harmony) => Installed = true;
    }

    private sealed class StubModule : IRunmobileModule
    {
        public string Name => "Stub";

        public bool Enabled => false;

        public string? Refusal => "there is nothing here";

        public IReadOnlyList<MenuCard> MenuCards =>
            throw new InvalidOperationException("A disabled module is never asked for its surfaces.");

        public void Install(Harmony harmony) =>
            throw new InvalidOperationException("A disabled module is never installed.");
    }
}

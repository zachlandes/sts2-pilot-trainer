using System.Reflection;
using HarmonyLib;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The seam between the shell and its features.
///
/// What is being tested is that the shell and module install only their own runtime
/// boundaries, and that the surfaces a module contributes are the ones drawn.
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

    [GameFact]
    public void EveryHarmonyPatchClassHasExactlyOneOwner()
    {
        _ = EngineHost.StartupPhase();
        var annotated = typeof(RunmobileMod).Assembly.GetTypes()
            .Where(type => type.GetCustomAttributes<HarmonyPatch>(inherit: false).Any() ||
                           type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                                           BindingFlags.DeclaredOnly)
                               .Any(method => method.GetCustomAttributes<HarmonyPatch>(inherit: false).Any()))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToList();
        var ownership = RunmobileMod.ShellPatchClasses
            .Select(type => (Type: type, Owner: "Runmobile shell"))
            .Concat(CombatTrainerModule.PatchClasses.Select(type =>
                (Type: type, Owner: CombatTrainerModule.Instance.Name)))
            .GroupBy(entry => entry.Type)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Owner).ToList());

        Assert.All(ownership, entry => Assert.Single(entry.Value));
        Assert.Equal(
            annotated,
            ownership.Keys.OrderBy(type => type.FullName, StringComparer.Ordinal).ToList());
        Assert.Equal("Runmobile shell", Assert.Single(ownership[typeof(ModeCard)]));
        Assert.All(
            CombatTrainerModule.PatchClasses,
            type => Assert.Equal("Combat Trainer", Assert.Single(ownership[type])));
    }

    [GameFact]
    public void InstallingTheCombatTrainerPatchesItsRuntimeBoundaries()
    {
        _ = EngineHost.StartupPhase();
        var harmony = new Harmony($"sts2-pilot-trainer.combat-trainer-test.{Guid.NewGuid():N}");
        var boundaries = new[]
        {
            GameMethod("MegaCrit.Sts2.Core.Runs.RunManager", "CleanUp"),
            GameMethod("MegaCrit.Sts2.Core.Nodes.NGame", "ReturnToMainMenu"),
            GameMethod("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "ChooseLocalOption"),
            GameMethod("MegaCrit.Sts2.Core.Runs.RunManager", "EnterMapCoord"),
        };
        var renderer = GameMethod(
            "MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NSingleplayerSubmenu", "_Ready");

        try
        {
            CombatTrainerModule.Instance.Install(harmony);

            Assert.All(boundaries, boundary => Assert.Contains(
                Harmony.GetPatchInfo(boundary)!.Owners,
                owner => owner == harmony.Id));
            var rendererOwners = Harmony.GetPatchInfo(renderer)?.Owners;
            Assert.True(rendererOwners is null || !rendererOwners.Contains(harmony.Id));
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    [GameFact]
    public void InstallingTheShellPatchesTheModuleCardRenderer()
    {
        _ = EngineHost.StartupPhase();
        var harmony = new Harmony($"sts2-pilot-trainer.shell-test.{Guid.NewGuid():N}");
        var boundary = GameMethod(
            "MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NSingleplayerSubmenu", "_Ready");

        try
        {
            RunmobileMod.InstallShellPatches(harmony);

            Assert.Contains(Harmony.GetPatchInfo(boundary)!.Owners, owner => owner == harmony.Id);
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    [Fact]
    public void TheCombatTrainerIsEnabledAndCarriesItsRecording()
    {
        var module = CombatTrainerModule.Instance;

        Assert.True(module.Enabled, module.Refusal);
        Assert.Null(module.Refusal);
        Assert.NotNull(module.Recording.Source);
        Assert.NotEmpty(module.RecordedFights.Fights);
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

        var modules = new IRunmobileModule[] { new StubModule(), installer };
        var installed = RunmobileMod.InstallModules(harmony, modules);
        var card = Assert.Single(RunmobileMod.MenuCardsFrom(modules));

        Assert.Equal(["Recording"], installed);
        Assert.True(installer.Installed);
        Assert.Equal("Recording", card.Title);
    }

    private static System.Reflection.MethodInfo GameMethod(string typeName, string methodName)
    {
        var game = AppDomain.CurrentDomain.GetAssemblies()
            .Single(assembly => assembly.GetName().Name == "sts2");
        return AccessTools.Method(game.GetType(typeName)!, methodName);
    }

    private sealed class RecordingModule : IRunmobileModule
    {
        internal bool Installed { get; private set; }

        public string Name => "Recording";

        public bool Enabled => true;

        public string? Refusal => null;

        public IReadOnlyList<MenuCard> MenuCards =>
        [
            new MenuCard("RecordingButton", "Recording", () => "Record a run", () => { }),
        ];

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

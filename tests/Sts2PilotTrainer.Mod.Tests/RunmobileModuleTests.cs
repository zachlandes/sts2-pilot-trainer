using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
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

    [GameFact]
    public void InstallingTheCombatTrainerPatchesItsRuntimeBoundaries()
    {
        _ = EngineHost.StartupPhase();
        var harmony = new Harmony($"sts2-pilot-trainer.combat-trainer-test.{Guid.NewGuid():N}");
        var boundaries = new[]
        {
            AccessTools.Method(typeof(NSingleplayerSubmenu), nameof(NSingleplayerSubmenu._Ready)),
            AccessTools.Method(typeof(RunManager), nameof(RunManager.CleanUp)),
            AccessTools.Method(typeof(NGame), nameof(NGame.ReturnToMainMenu)),
            AccessTools.Method(typeof(EventSynchronizer), nameof(EventSynchronizer.ChooseLocalOption)),
            AccessTools.Method(typeof(RunManager), nameof(RunManager.EnterMapCoord)),
        };

        try
        {
            CombatTrainerModule.Instance.Install(harmony);

            Assert.All(boundaries, boundary => Assert.Contains(
                Harmony.GetPatchInfo(boundary)!.Owners,
                owner => owner == harmony.Id));
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

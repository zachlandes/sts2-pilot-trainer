using System.Reflection;
using HarmonyLib;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The seam between the shell and its features.
/// </summary>
public sealed class RunmobileModuleTests
{
    [Fact]
    public void TheShellCarriesItsThreeFeatures()
    {
        Assert.Equal(
            [RecordedFightModule.Instance, RecorderModule.Instance, (IRunmobileModule)RunLibraryModule.Instance],
            RunmobileMod.Modules);
        Assert.Equal(
            ["Recorded fights", "Recorder", "Run library"],
            RunmobileMod.Modules.Select(module => module.Name));
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
            .Concat(RecordedFightModule.PatchClasses.Select(type =>
                (Type: type, Owner: RecordedFightModule.Instance.Name)))
            .Concat(RunRecorder.PatchClasses.Select(type =>
                (Type: type, Owner: RecorderModule.Instance.Name)))
            .Concat(RecorderModule.PresencePatchClasses.Select(type =>
                (Type: type, Owner: RecorderModule.Instance.Name)))
            .Concat(RunLibraryModule.PatchClasses.Select(type =>
                (Type: type, Owner: RunLibraryModule.Instance.Name)))
            .GroupBy(entry => entry.Type)
            .ToDictionary(group => group.Key, group => group.Select(entry => entry.Owner).ToList());

        Assert.All(ownership, entry => Assert.Single(entry.Value));
        Assert.Equal(
            annotated,
            ownership.Keys.OrderBy(type => type.FullName, StringComparer.Ordinal).ToList());
        Assert.Equal("Runmobile shell", Assert.Single(ownership[typeof(SingleplayerMenuRetention)]));
        Assert.All(
            RecordedFightModule.PatchClasses,
            type => Assert.Equal("Recorded fights", Assert.Single(ownership[type])));
        Assert.All(
            RunRecorder.PatchClasses,
            type => Assert.Equal("Recorder", Assert.Single(ownership[type])));
        Assert.All(
            RecorderModule.PresencePatchClasses,
            type => Assert.Equal("Recorder", Assert.Single(ownership[type])));
        Assert.All(
            RunLibraryModule.PatchClasses,
            type => Assert.Equal("Run library", Assert.Single(ownership[type])));
    }

    [GameFact]
    public void InstallingRecordedFightsPatchesItsRuntimeBoundaries()
    {
        _ = EngineHost.StartupPhase();
        var harmony = new Harmony($"sts2-pilot-trainer.recorded-fights-test.{Guid.NewGuid():N}");
        var boundaries = new[]
        {
            GameMethod("MegaCrit.Sts2.Core.Runs.RunManager", "CleanUp"),
            GameMethod("MegaCrit.Sts2.Core.Nodes.NGame", "ReturnToMainMenu"),
            GameMethod("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "ChooseLocalOption"),
            GameMethod("MegaCrit.Sts2.Core.Runs.RunManager", "EnterMapCoord"),
        };

        try
        {
            RecordedFightModule.Instance.Install(harmony);

            Assert.All(boundaries, boundary => Assert.Contains(
                Harmony.GetPatchInfo(boundary)!.Owners,
                owner => owner == harmony.Id));
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    [GameFact]
    public void InstallingTheRecorderPatchesTheDecisionsItWatches()
    {
        _ = EngineHost.StartupPhase();
        var harmony = new Harmony($"sts2-pilot-trainer.recorder-test.{Guid.NewGuid():N}");
        var boundaries = new[]
        {
            GameMethod("MegaCrit.Sts2.Core.Runs.RunManager", "SetUpNewSingleplayer"),
            GameMethod("MegaCrit.Sts2.Core.Runs.RunManager", "OnEnded"),
            GameMethod("MegaCrit.Sts2.Core.Multiplayer.Game.RewardsSetSynchronizer", "SelectLocalReward"),
            GameMethod("MegaCrit.Sts2.Core.Entities.Merchant.MerchantEntry", "OnTryPurchaseWrapper"),
        };

        try
        {
            RecorderModule.Instance.Install(harmony);

            Assert.All(boundaries, boundary => Assert.Contains(
                Harmony.GetPatchInfo(boundary)!.Owners,
                owner => owner == harmony.Id));
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    [GameFact]
    public void InstallingTheShellPatchesRetentionAndCardScreens()
    {
        _ = EngineHost.StartupPhase();
        var harmony = new Harmony($"sts2-pilot-trainer.shell-test.{Guid.NewGuid():N}");
        var boundaries = new[]
        {
            GameMethod("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NSingleplayerSubmenu", "_Ready"),
            GameMethod(
                "MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardGridSelectionScreen", "CardsSelected"),
            GameMethod(
                "MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardRewardSelectionScreen", "OptionSelected"),
        };

        try
        {
            RunmobileMod.InstallShellPatches(harmony);

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
    public void RecordedFightsAreEnabledAndCarryTheirRecording()
    {
        var module = RecordedFightModule.Instance;

        Assert.True(module.Enabled, module.Refusal);
        Assert.Null(module.Refusal);
        Assert.NotNull(module.Recording.Source);
        Assert.NotEmpty(module.RecordedFights.Fights);
    }

    [GameFact]
    public void ADisabledModuleIsSkippedAndTheOnesAroundItAreStillInstalled()
    {
        _ = EngineHost.StartupPhase();

        var installer = new RecordingModule();
        var harmony = new Harmony($"sts2-pilot-trainer.module-seam-test.{Guid.NewGuid():N}");

        var modules = new IRunmobileModule[] { new StubModule(), installer };
        var installed = RunmobileMod.InstallModules(harmony, modules);

        Assert.Equal(["Recording"], installed);
        Assert.True(installer.Installed);
    }

    private static MethodInfo GameMethod(string typeName, string methodName)
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

        public void Install(Harmony harmony) => Installed = true;
    }

    private sealed class StubModule : IRunmobileModule
    {
        public string Name => "Stub";

        public bool Enabled => false;

        public string? Refusal => "there is nothing here";

        public void Install(Harmony harmony) =>
            throw new InvalidOperationException("A disabled module is never installed.");
    }
}

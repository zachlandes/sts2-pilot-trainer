using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

public static class BaseLibParityProbe
{
    private static TaskCompletionSource? _beforeApplyCompletion;
    private static bool _beforeApplyEntered;

    public static BaseLibParityProbeResult Run(string baseLibPath, string mode)
    {
        if (mode is not ("baseline" or "patched" or "negative"))
        {
            throw new EngineException($"Unknown BaseLib parity probe mode '{mode}'.");
        }

        var fullBaseLibPath = Path.GetFullPath(baseLibPath);
        var baseLibHash = HashFile(fullBaseLibPath);
        const string expectedBaseLibHash =
            "sha256:ad2f89e43e8b31debfab65d783353d9429eba59a2cfe904ff933a894ce79d32e";
        if (baseLibHash != expectedBaseLibHash)
        {
            throw new EngineException(
                $"BaseLib parity probe requires the exact v3.4.5 release DLL ({expectedBaseLibHash}), " +
                $"not {baseLibHash}.");
        }

        var manifestPath = Path.Combine(Path.GetDirectoryName(fullBaseLibPath)!, "BaseLib.json");
        const string expectedManifestHash =
            "sha256:6d64d1ba9e48abf6e15479a6bda6f2d2b75a277453361a96cbcdd5508acccba3";
        var manifestHash = File.Exists(manifestPath) ? HashFile(manifestPath) : "missing";
        if (manifestHash != expectedManifestHash)
        {
            throw new EngineException(
                $"BaseLib parity probe requires the exact v3.4.5 release manifest ({expectedManifestHash}), " +
                $"not {manifestHash}.");
        }

        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullBaseLibPath);
        var patchType = assembly.GetType("BaseLib.Patches.Utils.SelfApplyDebuffPatch", throwOnError: true)!;
        var targetMethodFactory = patchType.GetMethod("TargetMethod", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new EngineException("BaseLib SelfApplyDebuffPatch.TargetMethod is absent.");
        var targetMethod = targetMethodFactory.Invoke(null, null) as MethodBase
            ?? throw new EngineException("BaseLib SelfApplyDebuffPatch did not resolve a target method.");
        var postfix = patchType.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new EngineException("BaseLib SelfApplyDebuffPatch.Postfix is absent.");
        var patchIl = postfix.GetMethodBody()?.GetILAsByteArray()
            ?? throw new EngineException("BaseLib SelfApplyDebuffPatch.Postfix has no IL body.");
        var targetIl = targetMethod.GetMethodBody()?.GetILAsByteArray()
            ?? throw new EngineException("PowerCmd.Apply has no IL body.");

        var identity = GameIdentity.Read();
        if (identity.BuildVersion != "v0.111.0")
        {
            throw new EngineException($"BaseLib parity probe supports v0.111.0, not {identity.BuildVersion}.");
        }

        var fixture = SyntheticReplayFixture.Create();
        var session = new GameSession();
        session.StartRun(
            fixture.Environment.Seed.Value,
            fixture.Environment.Character.Value,
            fixture.Environment.Ascension.Value,
            fixture.Environment.GameMode.Value,
            fixture.Environment.Acts.Value);
        var driver = new RunDriver(session);
        driver.EnterFirstRoom();
        foreach (var action in fixture.Actions.Take(2)) driver.Apply(action);

        var harmony = new Harmony($"sts2-pilot-trainer.baselib-parity.{mode}");
        if (mode is "patched" or "negative")
        {
            harmony.CreateClassProcessor(patchType).Patch();
            if (mode == "negative") harmony.Unpatch(targetMethod, postfix);
        }

        var patchRegistered = Harmony.GetPatchInfo(targetMethod)?.Postfixes
            .Any(patch => patch.PatchMethod == postfix) == true;
        var power = CreateCustomDebuff(assembly);
        var creature = session.RunState.Players[0].Creature;
        var before = CanonicalStateProjection.Project(session.RunState);

        _beforeApplyEntered = false;
        _beforeApplyCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var applyTask = PowerCmd.Apply(
            null!, power, creature, 1m, creature, cardSource: null, silent: true);
        var applyTaskWasIncomplete = !applyTask.IsCompleted;
        var beforeApplyWasEntered = _beforeApplyEntered;
        _beforeApplyCompletion.SetResult();
        applyTask.GetAwaiter().GetResult();
        Pump.Drain();

        var after = CanonicalStateProjection.Project(session.RunState);
        var events = new List<string>
        {
            $"target={targetMethod.DeclaringType?.FullName}.{targetMethod.Name}",
            $"patch_registered={patchRegistered}",
            $"before_apply_entered={beforeApplyWasEntered}",
            $"apply_task_incomplete={applyTaskWasIncomplete}",
            $"power_applied={creature.Powers.Contains(power)}",
            $"power_amount={power.Amount}",
            $"skip_next_duration_tick={power.SkipNextDurationTick}",
            $"applier_is_player={ReferenceEquals(power.Applier, creature)}",
        };

        var libDir = Path.GetDirectoryName(typeof(ModelDb).Assembly.Location)!;
        var receiptPath = Path.Combine(libDir, "prepared-assembly.json");
        var receipt = JsonNode.Parse(File.ReadAllText(receiptPath))!.AsObject();
        var preparedHashes = receipt["prepared_output_sha256"]!.AsObject()
            .ToDictionary(pair => pair.Key, pair => pair.Value!.GetValue<string>(), StringComparer.Ordinal);

        return new BaseLibParityProbeResult(
            Schema: "sts2-pilot-trainer/baselib-powercmd-probe/v2",
            Mode: mode,
            BuildVersion: identity.BuildVersion,
            BuildCommit: identity.Commit,
            PreparedReceiptSha256: HashFile(receiptPath),
            PreparedOutputSha256: preparedHashes,
            BaseLibVersion: assembly.GetName().Version?.ToString() ?? "unknown",
            BaseLibSha256: baseLibHash,
            BaseLibManifestSha256: manifestHash,
            BaseLibSourceCommit: "22757933ba10adc4322a628519a233a567507d87",
            TargetType: targetMethod.DeclaringType?.FullName ?? "unknown",
            TargetMethod: targetMethod.Name,
            TargetMetadataToken: targetMethod.MetadataToken,
            TargetIlSha256: HashBytes(targetIl),
            PatchType: patchType.FullName!,
            PatchMethod: postfix.Name,
            PatchModuleMvid: postfix.Module.ModuleVersionId.ToString("D"),
            PatchMetadataToken: postfix.MetadataToken,
            PatchIlSha256: HashBytes(patchIl),
            Seed: fixture.Environment.Seed.Value,
            ActionHistoryHash: SnapshotCacheKey.HashActions(fixture.Actions.Take(2)),
            PatchRegistered: patchRegistered,
            BeforeApplyWasEntered: beforeApplyWasEntered,
            ApplyTaskWasIncomplete: applyTaskWasIncomplete,
            PowerApplied: creature.Powers.Contains(power),
            PowerAmount: power.Amount,
            SkipNextDurationTick: power.SkipNextDurationTick,
            ApplierIsPlayer: ReferenceEquals(power.Applier, creature),
            Events: events,
            EventsSha256: HashBytes(Encoding.UTF8.GetBytes(string.Join("\n", events))),
            BeforeStateSha256: before.Digest(),
            AfterStateSha256: after.Digest(),
            BeforeRng: Rng(before),
            AfterRng: Rng(after));
    }

    public static Task WaitBeforeApplied()
    {
        _beforeApplyEntered = true;
        return _beforeApplyCompletion?.Task
            ?? throw new EngineException("BaseLib parity probe completion source is not initialized.");
    }

    private static PowerModel CreateCustomDebuff(Assembly baseLibAssembly)
    {
        var baseType = baseLibAssembly.GetType("BaseLib.Abstracts.CustomPowerModel", throwOnError: true)!;
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("Sts2PilotTrainer.BaseLibParityModel"), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("main");
        var type = module.DefineType(
            "Sts2PilotTrainer.BaseLibParityModel.CustomDebuffPower",
            TypeAttributes.Public | TypeAttributes.Sealed,
            baseType);
        type.DefineDefaultConstructor(MethodAttributes.Public);

        OverrideConstantGetter(type, baseType, "get_Type", (int)PowerType.Debuff);
        OverrideConstantGetter(type, baseType, "get_StackType", (int)PowerStackType.Counter);
        OverrideConstantGetter(type, baseType, "get_ShouldReceiveCombatHooks", 1);

        var beforeApplied = typeof(PowerModel).GetMethod(
            nameof(PowerModel.BeforeApplied),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new EngineException("PowerModel.BeforeApplied is absent from this build.");
        var beforeAppliedOverride = type.DefineMethod(
            beforeApplied.Name,
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(Task),
            beforeApplied.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
        var il = beforeAppliedOverride.GetILGenerator();
        il.Emit(OpCodes.Call, typeof(BaseLibParityProbe).GetMethod(nameof(WaitBeforeApplied))!);
        il.Emit(OpCodes.Ret);
        type.DefineMethodOverride(beforeAppliedOverride, beforeApplied);

        var canonical = (PowerModel)Activator.CreateInstance(type.CreateType())!;
        return canonical.ToMutable();
    }

    private static void OverrideConstantGetter(TypeBuilder type, Type baseType, string name, int value)
    {
        var baseGetter = baseType.GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new EngineException($"{baseType.FullName}.{name} is absent.");
        var getter = type.DefineMethod(
            name,
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
            baseGetter.ReturnType,
            Type.EmptyTypes);
        var il = getter.GetILGenerator();
        il.Emit(OpCodes.Ldc_I4, value);
        il.Emit(OpCodes.Ret);
        type.DefineMethodOverride(getter, baseGetter);
    }

    private static IReadOnlyDictionary<string, string> Rng(CanonicalState state) =>
        state.Fields
            .Where(pair => pair.Key.StartsWith("run.rng.", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string HashBytes(byte[] bytes) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
}

public sealed record BaseLibParityProbeResult(
    string Schema,
    string Mode,
    string BuildVersion,
    string BuildCommit,
    string PreparedReceiptSha256,
    IReadOnlyDictionary<string, string> PreparedOutputSha256,
    string BaseLibVersion,
    string BaseLibSha256,
    string BaseLibManifestSha256,
    string BaseLibSourceCommit,
    string TargetType,
    string TargetMethod,
    int TargetMetadataToken,
    string TargetIlSha256,
    string PatchType,
    string PatchMethod,
    string PatchModuleMvid,
    int PatchMetadataToken,
    string PatchIlSha256,
    string Seed,
    string ActionHistoryHash,
    bool PatchRegistered,
    bool BeforeApplyWasEntered,
    bool ApplyTaskWasIncomplete,
    bool PowerApplied,
    int PowerAmount,
    bool SkipNextDurationTick,
    bool ApplierIsPlayer,
    IReadOnlyList<string> Events,
    string EventsSha256,
    string BeforeStateSha256,
    string AfterStateSha256,
    IReadOnlyDictionary<string, string> BeforeRng,
    IReadOnlyDictionary<string, string> AfterRng);

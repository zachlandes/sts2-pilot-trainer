using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

public static class BaseLibReachabilityProbe
{
    private static readonly List<BaseLibPowerApplyCall> Calls = [];
    private static Type? _customModelType;
    private static int _actionSeq;

    public static BaseLibReachabilityResult Run(
        ReplayManifest manifest, string baseLibPath, bool injectAffectedCall)
    {
        var validation = ManifestValidator.Validate(manifest);
        if (!validation.IsValid)
        {
            throw new ManifestException("Manifest is not valid:\n" + validation.Describe());
        }
        if (manifest.Source.Kind != "vod" || manifest.Source.Video is null)
        {
            throw new ManifestException("BaseLib reachability evidence must replay a VOD manifest.");
        }

        var preflight = Preflight.Evaluate(manifest.Environment);
        if (!preflight.Matches)
        {
            var mismatches = preflight.Fields
                .Where(field => !field.Matches)
                .Select(field => $"{field.Field}: manifest '{field.Expected}', local '{field.Actual}'");
            throw new EngineException(
                "BaseLib reachability requires a matching environment preflight: " +
                string.Join("; ", mismatches));
        }

        var fullBaseLibPath = Path.GetFullPath(baseLibPath);
        const string expectedHash =
            "sha256:ad2f89e43e8b31debfab65d783353d9429eba59a2cfe904ff933a894ce79d32e";
        var baseLibHash = HashFile(fullBaseLibPath);
        if (baseLibHash != expectedHash)
        {
            throw new EngineException($"BaseLib reachability requires v3.4.5 ({expectedHash}), not {baseLibHash}.");
        }

        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(fullBaseLibPath);
        _customModelType = assembly.GetType("BaseLib.Abstracts.ICustomModel", throwOnError: true)!;
        var target = typeof(PowerCmd).GetMethod(
            nameof(PowerCmd.Apply),
            BindingFlags.Static | BindingFlags.Public,
            binder: null,
            types:
            [
                typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),
                typeof(PowerModel),
                typeof(Creature),
                typeof(decimal),
                typeof(Creature),
                typeof(CardModel),
                typeof(bool),
            ],
            modifiers: null) ?? throw new EngineException("Retail PowerCmd.Apply target is absent.");
        var postfix = typeof(BaseLibReachabilityProbe).GetMethod(
            nameof(ObservePostfix), BindingFlags.Static | BindingFlags.NonPublic)!;
        var harmony = new Harmony("sts2-pilot-trainer.baselib-reachability");
        Calls.Clear();
        harmony.Patch(target, postfix: new HarmonyMethod(postfix));

        try
        {
            var session = new GameSession();
            session.StartRun(
                manifest.Environment.Seed.Value,
                manifest.Environment.Character.Value,
                manifest.Environment.Ascension.Value,
                manifest.Environment.GameMode.Value,
                manifest.Environment.Acts.Value);
            var driver = new RunDriver(session);
            driver.EnterFirstRoom();

            foreach (var action in manifest.Actions.OrderBy(action => action.Seq))
            {
                _actionSeq = action.Seq;
                driver.Apply(action);
                if (injectAffectedCall && action.Seq == 1)
                {
                    InjectAffectedCall(session, assembly);
                }
            }

            var finalState = CanonicalStateProjection.Project(session.RunState);
            var identity = GameIdentity.Read();
            var calls = Calls.ToList();
            return new BaseLibReachabilityResult(
                Schema: "sts2-pilot-trainer/baselib-reachability/v1",
                NegativeControl: injectAffectedCall,
                RunId: manifest.RunId,
                VideoId: manifest.Source.Video.VideoId,
                BuildVersion: identity.BuildVersion,
                BuildCommit: identity.Commit,
                BaseLibVersion: assembly.GetName().Version?.ToString() ?? "unknown",
                BaseLibSha256: baseLibHash,
                TargetIlSha256: HashBytes(target.GetMethodBody()?.GetILAsByteArray()
                    ?? throw new EngineException("PowerCmd.Apply has no IL body.")),
                Seed: manifest.Environment.Seed.Value,
                ActionHistoryHash: SnapshotCacheKey.HashActions(manifest.Actions),
                FinalStateSha256: finalState.Digest(),
                FinalRng: Rng(finalState),
                Calls: calls,
                AffectedBranchReached: calls.Any(call => call.AffectedBranchReached));
        }
        finally
        {
            harmony.Unpatch(target, postfix);
        }
    }

    private static void InjectAffectedCall(GameSession session, Assembly baseLibAssembly)
    {
        _actionSeq = -2;
        var creature = session.RunState.Players[0].Creature;
        var power = BaseLibParityProbe.CreateCustomDebuff(baseLibAssembly);
        BaseLibParityProbe.BeginIncompletePowerApply();
        var task = PowerCmd.Apply(null!, power, creature, 1m, creature, null, true);
        if (task.IsCompleted)
        {
            throw new EngineException("BaseLib reachability negative control did not produce an incomplete original task.");
        }
        BaseLibParityProbe.CompleteIncompletePowerApply();
        task.GetAwaiter().GetResult();
        Pump.Drain();
    }

    private static void ObservePostfix(
        ref Task __result, PowerModel power, Creature target, Creature? applier, CardModel? cardSource)
    {
        var originalIncomplete = !__result.IsCompleted;
        var actionSeq = _actionSeq;
        __result = Observe(__result, power, target, applier, cardSource, actionSeq, originalIncomplete);
    }

    private static async Task Observe(
        Task original, PowerModel power, Creature target, Creature? applier, CardModel? cardSource,
        int actionSeq, bool originalIncomplete)
    {
        await original;
        var customModelType = _customModelType
            ?? throw new EngineException("BaseLib custom-model identity is not initialized.");
        var affected =
            target.CombatState?.CurrentSide == CombatSide.Player &&
            target.Side == CombatSide.Player &&
            power.Type == PowerType.Debuff &&
            power.Applier?.Side == CombatSide.Player &&
            (customModelType.IsInstanceOfType(power) ||
             customModelType.IsInstanceOfType(power.Applier?.Monster) ||
             customModelType.IsInstanceOfType(power.Applier?.Player?.Character) ||
             customModelType.IsInstanceOfType(cardSource));
        Calls.Add(new BaseLibPowerApplyCall(
            ActionSeq: actionSeq,
            PowerId: power.Id.ToString(),
            PowerType: power.Type.ToString(),
            TargetSide: target.Side.ToString(),
            ApplierSide: applier?.Side.ToString() ?? "none",
            OriginalTaskIncomplete: originalIncomplete,
            CustomModelParticipant: customModelType.IsInstanceOfType(power) ||
                                    customModelType.IsInstanceOfType(power.Applier?.Monster) ||
                                    customModelType.IsInstanceOfType(power.Applier?.Player?.Character) ||
                                    customModelType.IsInstanceOfType(cardSource),
            AffectedBranchReached: affected));
    }

    private static IReadOnlyDictionary<string, string> Rng(CanonicalState state) =>
        state.Fields.Where(pair => pair.Key.StartsWith("run.rng.", StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static string HashBytes(byte[] bytes) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes));
}

public sealed record BaseLibPowerApplyCall(
    int ActionSeq,
    string PowerId,
    string PowerType,
    string TargetSide,
    string ApplierSide,
    bool OriginalTaskIncomplete,
    bool CustomModelParticipant,
    bool AffectedBranchReached);

public sealed record BaseLibReachabilityResult(
    string Schema,
    bool NegativeControl,
    string RunId,
    string VideoId,
    string BuildVersion,
    string BuildCommit,
    string BaseLibVersion,
    string BaseLibSha256,
    string TargetIlSha256,
    string Seed,
    string ActionHistoryHash,
    string FinalStateSha256,
    IReadOnlyDictionary<string, string> FinalRng,
    IReadOnlyList<BaseLibPowerApplyCall> Calls,
    bool AffectedBranchReached);

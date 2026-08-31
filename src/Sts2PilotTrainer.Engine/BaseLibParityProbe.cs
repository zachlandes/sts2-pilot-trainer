using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

public static class BaseLibParityProbe
{
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
        var postfix = patchType.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new EngineException("BaseLib SelfApplyDebuffPatch.Postfix is absent.");
        var patchIl = postfix.GetMethodBody()?.GetILAsByteArray()
            ?? throw new EngineException("BaseLib SelfApplyDebuffPatch.Postfix has no IL body.");
        var identity = GameIdentity.Read();
        if (identity.BuildVersion != "v0.111.0")
        {
            throw new EngineException(
                $"BaseLib parity probe supports v0.111.0, not {identity.BuildVersion}.");
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

        var before = CanonicalStateProjection.Project(session.RunState);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task continuation = completion.Task;
        if (mode is "patched" or "negative")
        {
            object?[] arguments =
            [
                continuation,
                ModelDb.Power<WeakPower>(),
                session.RunState.Players[0].Creature,
                null,
                null,
            ];
            postfix.Invoke(null, arguments);
            continuation = (Task)arguments[0]!;
        }

        var events = new List<string>
        {
            $"original_incomplete={!completion.Task.IsCompleted}",
            $"continuation_incomplete={!continuation.IsCompleted}",
        };
        completion.SetResult();
        continuation.GetAwaiter().GetResult();
        events.Add($"original_completed={completion.Task.IsCompleted}");
        events.Add($"continuation_completed={continuation.IsCompleted}");

        if (mode == "negative") driver.Apply(fixture.Actions[2]);

        var after = CanonicalStateProjection.Project(session.RunState);
        var libDir = Path.GetDirectoryName(typeof(MegaCrit.Sts2.Core.Models.ModelDb).Assembly.Location)!;
        var receiptPath = Path.Combine(libDir, "prepared-assembly.json");
        var receipt = JsonNode.Parse(File.ReadAllText(receiptPath))!.AsObject();
        var preparedHashes = receipt["prepared_output_sha256"]!.AsObject()
            .ToDictionary(pair => pair.Key, pair => pair.Value!.GetValue<string>(), StringComparer.Ordinal);

        return new BaseLibParityProbeResult(
            Schema: "sts2-pilot-trainer/baselib-powercmd-probe/v1",
            Mode: mode,
            BuildVersion: identity.BuildVersion,
            BuildCommit: identity.Commit,
            PreparedReceiptSha256: HashFile(receiptPath),
            PreparedOutputSha256: preparedHashes,
            BaseLibVersion: assembly.GetName().Version?.ToString() ?? "unknown",
            BaseLibSha256: baseLibHash,
            BaseLibManifestSha256: manifestHash,
            BaseLibSourceCommit: "22757933ba10adc4322a628519a233a567507d87",
            PatchType: patchType.FullName!,
            PatchMethod: postfix.Name,
            PatchModuleMvid: postfix.Module.ModuleVersionId.ToString("D"),
            PatchMetadataToken: postfix.MetadataToken,
            PatchIlSha256: HashBytes(patchIl),
            Seed: fixture.Environment.Seed.Value,
            ActionHistoryHash: SnapshotCacheKey.HashActions(fixture.Actions.Take(2)),
            OriginalTaskWasIncomplete: events[0].EndsWith("True", StringComparison.Ordinal),
            ContinuationWasIncomplete: events[1].EndsWith("True", StringComparison.Ordinal),
            Events: events,
            EventsSha256: HashBytes(Encoding.UTF8.GetBytes(string.Join("\n", events))),
            BeforeStateSha256: before.Digest(),
            AfterStateSha256: after.Digest(),
            BeforeRng: Rng(before),
            AfterRng: Rng(after));
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
    string PatchType,
    string PatchMethod,
    string PatchModuleMvid,
    int PatchMetadataToken,
    string PatchIlSha256,
    string Seed,
    string ActionHistoryHash,
    bool OriginalTaskWasIncomplete,
    bool ContinuationWasIncomplete,
    IReadOnlyList<string> Events,
    string EventsSha256,
    string BeforeStateSha256,
    string AfterStateSha256,
    IReadOnlyDictionary<string, string> BeforeRng,
    IReadOnlyDictionary<string, string> AfterRng);

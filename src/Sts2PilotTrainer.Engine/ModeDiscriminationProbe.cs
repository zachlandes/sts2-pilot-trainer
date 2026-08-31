using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Models;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

public static class ModeDiscriminationProbe
{
    public const string NegativeModifierType = "MegaCrit.Sts2.Core.Models.Modifiers.Terminal";
    public const string ModifierVariantPrefix = "modifier:";

    public static ModeDiscriminationResult Run(ReplayManifest manifest, string variant)
    {
        var validation = ManifestValidator.Validate(manifest);
        if (!validation.IsValid)
        {
            throw new ManifestException("Manifest is not valid:\n" + validation.Describe());
        }
        if (manifest.Source.Kind != "vod" || manifest.Source.Video is null)
        {
            throw new ManifestException("Mode-discrimination evidence must replay a VOD manifest.");
        }

        var testedManifest = variant == "checkpoint-negative"
            ? Corruption.All.Single(corruption => corruption.Name == "reorder-plays").Apply(manifest)
            : manifest;
        var (gameMode, modifiers) = variant switch
        {
            "standard" or "checkpoint-negative" => ("standard", Array.Empty<string>()),
            "custom-default" => ("custom", Array.Empty<string>()),
            "daily-default" => ("daily", Array.Empty<string>()),
            "custom-negative" => ("custom", new[] { NegativeModifierType }),
            // A real daily always carries a server-selected modifier set, so each modifier is
            // exercised as a daily to learn whether it is observable in this history at all
            _ when variant.StartsWith(ModifierVariantPrefix, StringComparison.Ordinal)
                => ("daily", new[] { variant[ModifierVariantPrefix.Length..] }),
            _ => throw new EngineException($"Unknown mode-discrimination variant '{variant}'."),
        };

        var outcome = Arbiter.Run(
            testedManifest,
            gameModeOverride: gameMode,
            modifierTypeNames: modifiers);
        var state = outcome.FinalState
            ?? throw new EngineException($"Mode-discrimination variant '{variant}' produced no engine state.");
        var identity = GameIdentity.Read();
        var availableModifiers = ModelDb.All.OfType<ModifierModel>()
            .Select(modifier => modifier.GetType().FullName ?? modifier.GetType().Name)
            .Order(StringComparer.Ordinal)
            .ToList();
        var checkpoints = outcome.Report.Checkpoints;

        return new ModeDiscriminationResult(
            Schema: "sts2-pilot-trainer/mode-discrimination/v2",
            Variant: variant,
            GameMode: gameMode,
            ModifierTypes: modifiers,
            AvailableModifierTypes: availableModifiers,
            RunId: manifest.RunId,
            VideoId: manifest.Source.Video.VideoId,
            BuildVersion: identity.BuildVersion,
            BuildCommit: identity.Commit,
            Seed: manifest.Environment.Seed.Value,
            ActionHistoryHash: SnapshotCacheKey.HashActions(manifest.Actions),
            ExecutedActionHistoryHash: SnapshotCacheKey.HashActions(testedManifest.Actions),
            CompletedHistory: outcome.Report.ActionHistoryHash is not null,
            VerificationStatus: outcome.Report.Status.ToString(),
            Diagnostics: outcome.Report.Diagnostics,
            AllCheckpointsPassed: checkpoints.All(checkpoint => checkpoint.Passed),
            CheckpointSha256: Hash(JsonSerializer.Serialize(checkpoints, ManifestJson.Options)),
            FinalStateSha256: state.Digest(),
            BehavioralStateSha256: BehavioralDigest(state));
    }

    private static string BehavioralDigest(CanonicalState state)
    {
        var rendered = string.Join(
            "\n",
            state.Fields
                .Where(field => field.Key != "run.game_mode")
                .OrderBy(field => field.Key, StringComparer.Ordinal)
                .Select(field => $"{field.Key}={field.Value}")) + "\n";
        return Hash(rendered);
    }

    private static string Hash(string content) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}

public sealed record ModeDiscriminationResult(
    string Schema,
    string Variant,
    string GameMode,
    IReadOnlyList<string> ModifierTypes,
    IReadOnlyList<string> AvailableModifierTypes,
    string RunId,
    string VideoId,
    string BuildVersion,
    string BuildCommit,
    string Seed,
    string ActionHistoryHash,
    string ExecutedActionHistoryHash,
    bool CompletedHistory,
    string VerificationStatus,
    IReadOnlyList<string> Diagnostics,
    bool AllCheckpointsPassed,
    string CheckpointSha256,
    string FinalStateSha256,
    string BehavioralStateSha256);

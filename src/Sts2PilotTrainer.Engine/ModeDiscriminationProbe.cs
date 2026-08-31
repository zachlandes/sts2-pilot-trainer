using System.Security.Cryptography;
using System.Text;
using MegaCrit.Sts2.Core.Models;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

public static class ModeDiscriminationProbe
{
    public const string NegativeModifierType = "MegaCrit.Sts2.Core.Models.Modifiers.Terminal";

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

        var (gameMode, modifiers) = variant switch
        {
            "standard" => ("standard", Array.Empty<string>()),
            "custom-default" => ("custom", Array.Empty<string>()),
            "daily-default" => ("daily", Array.Empty<string>()),
            "custom-negative" => ("custom", new[] { NegativeModifierType }),
            _ => throw new EngineException($"Unknown mode-discrimination variant '{variant}'."),
        };

        var session = new GameSession();
        session.StartRun(
            manifest.Environment.Seed.Value,
            manifest.Environment.Character.Value,
            manifest.Environment.Ascension.Value,
            gameMode,
            manifest.Environment.Acts.Value,
            PlayerProgress.AllUnlocked,
            modifiers);

        var completed = true;
        string? rejection = null;
        var driver = new RunDriver(session);
        try
        {
            driver.EnterFirstRoom();
            foreach (var action in manifest.Actions.OrderBy(action => action.Seq))
            {
                driver.Apply(action);
            }
        }
        catch (EngineException exception)
        {
            completed = false;
            rejection = exception.Message;
        }

        var state = CanonicalStateProjection.Project(session.RunState);
        var identity = GameIdentity.Read();
        var availableModifiers = ModelDb.All.OfType<ModifierModel>()
            .Select(modifier => modifier.GetType().FullName ?? modifier.GetType().Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        return new ModeDiscriminationResult(
            Schema: "sts2-pilot-trainer/mode-discrimination/v1",
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
            CompletedHistory: completed,
            Rejection: rejection,
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
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rendered)));
    }
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
    bool CompletedHistory,
    string? Rejection,
    string FinalStateSha256,
    string BehavioralStateSha256);

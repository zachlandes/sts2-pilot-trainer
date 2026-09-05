using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Logging;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// What the player has told Runmobile to do, read from
/// <c>settings.json</c> in the store.
///
/// A file rather than a screen, for now. The settings surface belongs with the rest of
/// Runmobile's own drawing, and until that exists a player who wants the recorder off
/// edits one line - which is the honest shape of a setting that has no control yet
/// rather than a control nobody can find.
///
/// Recording is on by default because the recorder is not released to players before
/// that surface is: the default is what the person building this wants while it is
/// being built, and it becomes a decision the moment somebody else can see it.
///
/// It carries a schema string and refuses an unrecognised one, like every other file
/// under <see cref="RunmobileStore"/>. A settings file this build cannot read is not
/// a settings file it may guess at - a newer writer's <c>record_my_runs</c> could mean
/// something this build does not know about.
/// </summary>
internal sealed record RunmobileSettings
{
    internal const string Schema = "sts2-pilot-trainer/runmobile-settings/v1";

    internal const string FileName = "settings.json";

    [JsonPropertyName("schema")]
    public required string SchemaId { get; init; }

    /// <summary>Whether every run the player plays is recorded.</summary>
    [JsonPropertyName("record_my_runs")]
    public bool RecordMyRuns { get; init; } = true;

    /// <summary>What a player who has never touched the file gets.</summary>
    internal static RunmobileSettings Default => new() { SchemaId = Schema, RecordMyRuns = true };

    /// <summary>
    /// The settings this session runs under.
    ///
    /// An absent file is the default rather than a failure: nothing has been decided
    /// yet, and refusing to run because a player has never opened a settings screen
    /// would be a strange thing to do. A file that is there and unreadable is a
    /// different case - somebody wrote something - so it is reported and the default
    /// is used, because a recorder that silently ignored an "off" would be worse than
    /// one that said it could not read it.
    /// </summary>
    internal static RunmobileSettings Read()
    {
        try
        {
            if (RunmobileStore.Read(FileName) is not { } json) return Default;

            var settings = ManifestJson.DeserializeRequired<RunmobileSettings>(json, "Runmobile settings");
            if (!string.Equals(settings.SchemaId, Schema, StringComparison.Ordinal))
            {
                throw new ManifestException(
                    $"This settings file declares schema '{settings.SchemaId}', and this build reads " +
                    $"'{Schema}'.");
            }

            return settings;
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not read {FileName}, carrying on with the defaults: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
            return Default;
        }
    }
}

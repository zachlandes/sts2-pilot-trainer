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
/// something this build does not know about - and the direction it fails in is off,
/// because a file that is there is somebody having tried to say something.
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

    /// <summary>What a player whose file this build cannot read gets.</summary>
    private static RunmobileSettings DoNotRecord => new() { SchemaId = Schema, RecordMyRuns = false };

    /// <summary>
    /// The settings this session runs under.
    ///
    /// An absent file is the default rather than a failure: nothing has been decided
    /// yet, and refusing to record because a player has never opened a settings screen
    /// would be a strange thing to do. A file that is there and unreadable is the
    /// opposite case and answers the opposite way - somebody wrote something, the one
    /// thing they can write is an "off", and a recorder that recorded through a
    /// sentence it could not read would be recording without consent. It says so in
    /// the log either way.
    /// </summary>
    internal static RunmobileSettings Read()
    {
        string? json;
        try
        {
            json = RunmobileStore.Read(FileName);
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not open {FileName}, so this session records nothing: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
            return DoNotRecord;
        }

        if (json is null) return Default;

        try
        {
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
                $"[{RunmobileMod.ModId}] could not read {FileName}, so this session records nothing: " +
                $"{ex.GetType().Name}: {ex.Message}", 2);
            return DoNotRecord;
        }
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Logging;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// What the player has told Runmobile to do, read from
/// <c>settings.json</c> in the store.
///
/// A file rather than a screen, for now. The settings surface belongs with the rest of
/// Runmobile's own drawing, and until that exists a player who wants the recorder off,
/// or their recordings gone, edits one line - which is the honest shape of a control
/// that has no screen yet rather than a control nobody can find.
///
/// Recording is on by default because the recorder is not released to players before
/// that surface is: the default is what the person building this wants while it is
/// being built, and it becomes a decision the moment somebody else can see it.
///
/// Three things are said here and they are three different kinds of sentence. Whether
/// to record is a standing choice. How many runs to keep is a standing policy, and it
/// has a default rather than being unbounded because the recorder writes a real file
/// per run and nothing else ever removed one. Asking for every run to be removed is a
/// one-shot act, so it is the one member this mod writes back: it is honoured once and
/// then set to false, which is both how it stops repeating and how a player sees that
/// it happened.
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

    /// <summary>How many runs a player keeps before this mod removes one of its own
    /// accord. Fifty of them is roughly the size of a screenshot folder.</summary>
    internal const int DefaultKeepRecentRuns = 50;

    /// <summary>The internal sentinel for "remove nothing", used only where this build
    /// could not read the player's file: a file nobody could read is not permission to
    /// delete anything. There is no way to ask for it from the file itself, because an
    /// unbounded pile of recordings is the thing retention exists to end.</summary>
    internal const int KeepEveryRun = -1;

    [JsonPropertyName("schema")]
    public required string SchemaId { get; init; }

    /// <summary>Whether every run the player plays is recorded.</summary>
    [JsonPropertyName("record_my_runs")]
    public bool RecordMyRuns { get; init; } = true;

    /// <summary>
    /// How many of the player's most recent recorded runs are kept.
    ///
    /// Older ones are removed the next time this mod has a game to read, and the count
    /// is of runs rather than of files: a run's journal and its manifest go together or
    /// not at all. Zero keeps none, which is a standing purge rather than the one-shot
    /// one below; a negative number is refused and the default applied instead, so a
    /// player who wants more keeps writes a larger number rather than an opt-out of
    /// having a policy at all.
    /// </summary>
    [JsonPropertyName("keep_recent_runs")]
    public int KeepRecentRuns { get; init; } = DefaultKeepRecentRuns;

    /// <summary>
    /// Remove every run this mod has recorded, once.
    ///
    /// The player's control over their own disk, and the one member of this file the
    /// mod writes back: it acts at the next moment the mod has a game to read, and is
    /// then set to false in the file, so it is a thing a player does rather than a
    /// state they are left in. It removes the recordings and nothing else - not a save,
    /// not a profile, not run history, and not a file in this mod's own store that no
    /// recording is made of.
    /// </summary>
    [JsonPropertyName("purge_my_runs")]
    public bool PurgeMyRuns { get; init; }

    /// <summary>What a player who has never touched the file gets.</summary>
    internal static RunmobileSettings Default =>
        new() { SchemaId = Schema, RecordMyRuns = true, KeepRecentRuns = DefaultKeepRecentRuns };

    /// <summary>
    /// What a player whose file this build cannot read gets.
    ///
    /// Both answers fail in the direction of doing less: nothing is recorded, because
    /// the only thing this file can say is "off", and nothing is removed, because a
    /// sentence nobody could read is not somebody asking for their runs to be deleted.
    /// </summary>
    private static RunmobileSettings DoNotRecord =>
        new() { SchemaId = Schema, RecordMyRuns = false, KeepRecentRuns = KeepEveryRun };

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

            if (settings.KeepRecentRuns < 0)
            {
                Log.Error(
                    $"[{RunmobileMod.ModId}] {FileName} asks to keep " +
                    $"{settings.KeepRecentRuns.ToString(CultureInfo.InvariantCulture)} runs, which is not a " +
                    $"number of runs, so this session keeps the usual {DefaultKeepRecentRuns.ToString(CultureInfo.InvariantCulture)}. " +
                    "Write a larger number to keep more.", 2);
                return settings with { KeepRecentRuns = DefaultKeepRecentRuns };
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

    /// <summary>
    /// Writes these settings back over the player's file.
    ///
    /// The one thing this mod writes into a file a player hand-edits, and it exists for
    /// exactly one member: a purge that stayed requested would run again at every
    /// launch. Whole and atomic through <see cref="RunmobileStore"/>, so an interrupted
    /// write leaves the file the player wrote rather than half of a new one.
    /// </summary>
    internal void Save() =>
        RunmobileStore.Write(FileName, JsonSerializer.Serialize(this, ManifestJson.Options) + "\n");
}

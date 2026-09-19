using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Logging;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// What the player has told Runmobile to do, read from
/// <c>settings.json</c> in the store.
///
/// The file is the record and a screen is a way of editing it.
/// Four of these members have a control now: <c>MyRunsSettingsRow</c> draws the standing policy, the removal act, whether the shared-run index is fetched, and whether the main-menu row is drawn.
/// All four go through the writers below rather than keeping a second copy of the answer, so a player who edits the file by hand and a player who moves the control are saying the same thing in the same place.
/// Whether to record has no control yet and is a line in this file.
///
/// Recording is on by default because the recorder is not released to players before
/// that surface is: the default is what the person building this wants while it is
/// being built, and it becomes a decision the moment somebody else can see it.
///
/// Six things are said here and they are four different kinds of sentence.
/// Whether to record, whether to fetch the shared-run index and whether Runmobile draws
/// a row on the main menu are standing choices - the last of which has a third answer,
/// "nobody has said", because its default follows the player's own progression.
/// The sharing-service endpoint is the explicit authority for outbound transfer and has no default.
/// How many runs to keep is a standing policy, and it has a default rather than being unbounded because the recorder writes a real file per run and nothing else ever removed one.
/// Asking for every run to be removed is a one-shot act: it is honoured once and then set to false, which is both how it stops repeating and how a player sees that it happened.
/// The retail soak's plan is a seventh, of a kind none of the others are: an instrument's, written only by the soak script into an isolated profile, never by a control, and off wherever it is absent.
///
/// Every write here edits the member it names and leaves the rest of the document as
/// the player wrote it. That is not tidiness: the rest of the file is their own text,
/// including values this build refuses, and a mod that re-serialised the whole document
/// from this record would silently correct sentences it was never asked about.
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

    /// <summary>Whether the shared-run index for Others is fetched. This never submits a run.</summary>
    [JsonPropertyName("fetch_run_index")]
    public bool FetchRunIndex { get; init; } = true;

    [JsonPropertyName("sharing_service_url")]
    public string? SharingServiceUrl { get; init; }

    /// <summary>
    /// Whether Runmobile draws a row of its own on the game's main menu, or null where
    /// the player has never said.
    ///
    /// Three answers rather than two, and the third is the point. A player who has
    /// finished no run cannot reach this library any other way - the game hides its own
    /// Compendium until a run is finished - so the row is there for them by default and
    /// not for anybody else. Null is what lets that default follow the player's
    /// progression, and true or false is what stops it: once somebody has moved the
    /// control, finishing a first run must not quietly take away a row they turned on.
    /// <c>MainMenuRow.ShownWhen</c> is the one place those three answers become a
    /// visibility.
    /// </summary>
    [JsonPropertyName("show_main_menu_row")]
    public bool? ShowMainMenuRow { get; init; }

    /// <summary>
    /// How many of the player's most recent recorded runs are kept.
    ///
    /// Older ones are removed the next time the player reaches the singleplayer menu
    /// with a save profile chosen, and the count is of runs rather than of files: a run's journal and its manifest go together or
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
    /// The player's control over their own disk. It acts at the next singleplayer menu
    /// with a save profile chosen - or there and then, when the settings row is what
    /// asked - and is set to false in the file afterwards either way, so it is a thing a
    /// player does rather than a
    /// state they are left in. It removes the recordings and nothing else - not a save,
    /// not a profile, not run history, and not a file in this mod's own store that no
    /// recording is made of.
    /// </summary>
    [JsonPropertyName("purge_my_runs")]
    public bool PurgeMyRuns { get; init; }

    /// <summary>
    /// The nightly retail soak's plan, or null where there is none, which is the
    /// default and the only state a player's file is ever in.
    ///
    /// The soak is a test instrument and not a feature: it drives standard singleplayer
    /// runs from inside the retail client with the recorder attached, so the recorder
    /// can be held to parity over a night's worth of real-time play. It is on only where
    /// this member is an object, and nothing in the product writes one - no control
    /// offers it, <see cref="Set"/> preserves it as it preserves every member it was not
    /// asked about, and the soak script writes the whole file for an isolated profile.
    /// The schema stays v1 because the member is optional: a build that does not know it
    /// reads past it, and a build that does reads it only from a file it could read
    /// whole. What the plan has to say to be run is <see cref="RetailSoakPlan.Problems"/>,
    /// asked by the module that would run it; a file that could not be read at all is
    /// refused here as every other member of it is, and the soak with it.
    /// </summary>
    [JsonPropertyName("retail_soak")]
    public RetailSoakPlan? RetailSoak { get; init; }

    /// <summary>
    /// Whether the answers above are the player's own sentence or this build standing
    /// in for one it could not read.
    ///
    /// Not a member of the file and never written to it: it is what <see cref="Read"/>
    /// did, and a surface that showed a refused file's stand-in as the player's policy
    /// would be stating something nobody established. <see cref="RecordingRetention"/>
    /// carries it on to the row, which neither names the sentinel nor offers a control
    /// that would write into a document this build refuses.
    /// </summary>
    [JsonIgnore]
    public bool Readable { get; init; } = true;

    /// <summary>What a player who has never touched the file gets.</summary>
    internal static RunmobileSettings Default =>
        new() { SchemaId = Schema, RecordMyRuns = true, KeepRecentRuns = DefaultKeepRecentRuns };

    /// <summary>
    /// What a player whose file this build cannot read gets.
    ///
    /// Both answers fail in the direction of doing less: nothing is recorded, because
    /// the only thing this file can say is "off", and nothing is removed, because a
    /// sentence nobody could read is not somebody asking for their runs to be deleted.
    ///
    /// <see cref="ShowMainMenuRow"/> is left unsaid rather than answered either way, and
    /// that is the same direction: an unreadable file is a file nobody has said anything
    /// in, so the row falls back to the run count exactly as it does for a player who has
    /// never opened the settings page. Answering "off" here would take the library's only
    /// entrance away from a new player over a file fault, and answering "on" would put a
    /// row on the menu of somebody who may have turned it off.
    /// </summary>
    private static RunmobileSettings DoNotRecord =>
        new()
        {
            SchemaId = Schema,
            RecordMyRuns = false,
            FetchRunIndex = false,
            KeepRecentRuns = KeepEveryRun,
            Readable = false,
        };

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
    /// Takes a honoured purge request back out of the player's file, and touches
    /// nothing else in it.
    ///
    /// A purge that stayed requested would run again at every launch, so this one
    /// member has to be written; every other member is the player's own text and is
    /// written back exactly as they wrote it, refused values included. The file is
    /// edited rather than re-serialised from this record for that reason. Whole and
    /// atomic through <see cref="RunmobileStore"/>, so an interrupted write leaves the
    /// file the player wrote rather than half of a new one.
    /// </summary>
    internal static void ClearPurgeRequest()
    {
        if (RunmobileStore.Read(FileName) is not { } json) return;
        if (JsonNode.Parse(json) is not JsonObject settings) return;

        settings["purge_my_runs"] = false;
        RunmobileStore.Write(FileName, settings.ToJsonString(ManifestJson.Options) + "\n");
    }

    /// <summary>
    /// Records that the player has asked for every run to go, so that the act survives
    /// the game stopping in the middle of it.
    ///
    /// The other direction of the one member this mod writes, and the reason it is
    /// written before anything is deleted rather than instead of deleting: the settings
    /// screen removes the runs there and then, and a game that died between the two
    /// finishes at the next main menu, which is the direction a player who pressed
    /// Remove wants it to fail in. <see cref="ClearPurgeRequest"/> takes it back out
    /// afterwards.
    /// </summary>
    internal static void RequestPurge() => Set("purge_my_runs", true);

    /// <summary>
    /// Writes the player's standing policy, in runs.
    ///
    /// Refused rather than clamped outside the range the control offers: a caller
    /// asking to keep a negative number of runs is a caller with a bug, and
    /// <see cref="Read"/> already has an answer for a <em>file</em> that says one.
    /// </summary>
    internal static void SetKeepRecentRuns(int keep)
    {
        if (keep < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(keep), keep, "A policy keeps a number of runs, and a negative is not one.");
        }

        Set("keep_recent_runs", keep);
    }

    /// <summary>Writes only the network index preference. Sharing is always explicit.</summary>
    internal static void SetFetchRunIndex(bool fetch) => Set("fetch_run_index", fetch);

    /// <summary>
    /// Writes whether the main-menu row is drawn.
    ///
    /// It takes a bool rather than the file's own <c>bool?</c>: writing null back would
    /// be a player asking to un-decide something, which no control offers and which
    /// would silently hand their menu back to the run count. Hand-editing the member out
    /// of the file still does that, because the file is the record.
    /// </summary>
    internal static void SetShowMainMenuRow(bool show) => Set("show_main_menu_row", show);

    /// <summary>
    /// Writes one member of the player's file and leaves every other one exactly as
    /// they wrote it.
    ///
    /// The file is edited rather than re-serialised from this record, for the reason
    /// <see cref="ClearPurgeRequest"/> gives: the rest of it is the player's own text,
    /// refused values included, and a build that rewrote the whole document would
    /// quietly correct sentences it was never asked about. A player who has written
    /// nothing yet gets the defaults with this one member set, because there has to be
    /// somewhere to put the answer.
    ///
    /// A file this build cannot read is refused, and <see cref="Read"/> is what decides
    /// that rather than a second set of rules here: one reader means a document can
    /// never be unreadable to the row and writable by the control beside it. Overwriting
    /// it would be this mod discarding something a player wrote in order to store
    /// something they meant to add to it; editing one member of it would be worse,
    /// because a <c>keep_recent_runs</c> written with this build's meaning into a
    /// document written by a build with another is a sentence neither of them said.
    /// An absent file is not an unreadable one and still gets the defaults with this
    /// one member set, because there has to be somewhere to put the answer.
    /// </summary>
    private static void Set(string member, JsonNode value)
    {
        var json = RunmobileStore.Read(FileName);
        JsonObject settings;
        if (json is null)
        {
            settings = new JsonObject
            {
                ["schema"] = Schema,
                ["record_my_runs"] = Default.RecordMyRuns,
                ["fetch_run_index"] = Default.FetchRunIndex,
                ["sharing_service_url"] = Default.SharingServiceUrl,
                ["keep_recent_runs"] = Default.KeepRecentRuns,
                ["purge_my_runs"] = false,
                ["show_main_menu_row"] = null,
            };
        }
        else
        {
            if (!Read().Readable)
            {
                throw new ManifestException(
                    $"{FileName} is not a settings file this build can read, so Runmobile will not write " +
                    "into it.");
            }

            settings = JsonNode.Parse(json)!.AsObject();
        }

        settings[member] = value;
        RunmobileStore.Write(FileName, settings.ToJsonString(ManifestJson.Options) + "\n");
    }
}

/// <summary>
/// One night's plan for the retail soak, as the soak script writes it into the soak
/// profile's <c>settings.json</c>.
///
/// Read only through <see cref="RunmobileSettings.Read"/>, so a plan that is not JSON
/// of this shape refuses the whole file the way any malformed member does; what a
/// well-formed plan still has to say is <see cref="Problems"/>, and the module that
/// runs it refuses on any of them by name rather than clamping or guessing. The seed
/// list may be empty, which asks for a fresh random seed per run the way the game's
/// own lobby rolls one when the player writes none.
/// </summary>
internal sealed record RetailSoakPlan
{
    /// <summary>How many runs the night plays before it stops.</summary>
    [JsonPropertyName("runs")]
    public int Runs { get; init; }

    /// <summary>The seeds the runs use in order, cycling; empty for a fresh seed each run.</summary>
    [JsonPropertyName("seeds")]
    public IReadOnlyList<string> Seeds { get; init; } = [];

    /// <summary>The character every run is played as, by the game's own id
    /// (<c>CHARACTER.IRONCLAD</c>).</summary>
    [JsonPropertyName("character")]
    public required string Character { get; init; }

    /// <summary>The ascension level every run is started at.</summary>
    [JsonPropertyName("ascension")]
    public int Ascension { get; init; }

    /// <summary>The night's deadline, after which no new run is started, in minutes
    /// from the moment the soak armed.</summary>
    [JsonPropertyName("stop_after_minutes")]
    public int StopAfterMinutes { get; init; }

    /// <summary>The prefix every character id carries, which is the one thing about a
    /// character a game-free check can hold a plan to.</summary>
    internal const string CharacterIdPrefix = "CHARACTER.";

    /// <summary>
    /// Everything this plan says that no night could run on, in words; empty for a
    /// plan the module may start.
    ///
    /// Game-free, because the settings record is: whether the character exists on this
    /// build and whether the profile has unlocked the ascension are the module's
    /// questions, asked of the game once it has one.
    /// </summary>
    internal IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (Runs < 1) problems.Add($"runs is {Runs.ToString(CultureInfo.InvariantCulture)}, and a night plays at least one.");
        if (Ascension < 0) problems.Add($"ascension is {Ascension.ToString(CultureInfo.InvariantCulture)}, which is not an ascension level.");
        if (StopAfterMinutes < 1)
        {
            problems.Add(
                $"stop_after_minutes is {StopAfterMinutes.ToString(CultureInfo.InvariantCulture)}, and a night " +
                "has a deadline.");
        }

        if (string.IsNullOrWhiteSpace(Character) || !Character.StartsWith(CharacterIdPrefix, StringComparison.Ordinal))
        {
            problems.Add($"character '{Character}' is not a character id; one reads {CharacterIdPrefix}IRONCLAD.");
        }

        foreach (var seed in Seeds)
        {
            if (string.IsNullOrWhiteSpace(seed)) problems.Add("seeds holds an empty seed.");
        }

        return problems;
    }
}

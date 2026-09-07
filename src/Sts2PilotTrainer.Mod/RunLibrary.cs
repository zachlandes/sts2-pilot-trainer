using MegaCrit.Sts2.Core.Logging;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Mod;

/// <summary>
/// Every run this game could offer, gathered from the two places a run can come from,
/// with this build's verdict on each.
///
/// It is the seam between the disk and the derivation: <see cref="RunLibraryStore"/>
/// reads files, <see cref="RunVerdicts"/> asks the preflight, and
/// <c>RunBrowser</c> turns what comes back into a screen. Nothing here decides what
/// is shown - the hidden rule lives in <c>LibraryRun.Listed</c>, in one place, so
/// "why is this run not in the list" has one field to look at.
///
/// <para><b>Nothing is cached, and only one of the two questions builds the list.</b>
/// "Which runs are there" is <see cref="Runs"/>, and it is expensive on purpose: every
/// recording is deserialized and judged live, every time it is asked, because the
/// recorder writes into the library while the game is running and because a verdict is a
/// reading of the whole environment rather than of the build alone. Everything a player
/// is shown about whether a run plays comes from there and from nowhere else. "Is there
/// anything at all" is <see cref="HasAnythingToShow"/>, which the Compendium asks on
/// every menu open, and it reads no manifest: the shipped recordings are already in
/// memory and are judged directly, and the player's own runs are answered from the run
/// ids in the recorder's directory index alone.</para>
///
/// <para>The promise the cheap question makes is one-directional, and only that. It never
/// hides a run the list would hold; it may show the button when the list turns out empty.
/// That is the direction to be wrong in, because the browser is the only thing that
/// judges and the button is the only way to the browser - anything remembered that could
/// hide the button could hide the only path to judging again, and a persisted negative
/// that never corrects itself is how this feature was once lost permanently.</para>
/// </summary>
internal static class RunLibrary
{
    private static readonly IRunSharingApi Sharing = new HttpRunSharingApi(new HttpClient
    {
        BaseAddress = new Uri("https://runs.runmobile.app/v1/"),
        Timeout = TimeSpan.FromSeconds(10),
    });

    private static readonly Dictionary<string, SharedRun> Shared = new(StringComparer.Ordinal);

    /// <summary>
    /// Every run, listed or not, with the verdict that decides which.
    ///
    /// The hidden ones are here on purpose: the numeral under the list counts them, and
    /// a run code finds them. A list filtered before it arrived could do neither.
    /// </summary>
    internal static IReadOnlyList<LibraryRun> Runs()
    {
        var build = ThisBuild();
        var progress = RunLibraryStore.ReadProgress();
        var runs = new List<LibraryRun>();

        foreach (var included in Included())
        {
            runs.Add(LibraryRun.From(
                included,
                RunOrigin.Included,
                RunVerdicts.For(included, build),
                progress.PlayedFrom(included.RunId)));
        }

        foreach (var stored in RunLibraryStore.MyRecordings())
        {
            runs.Add(LibraryRun.From(
                stored.Recording,
                RunOrigin.Mine,
                RunVerdicts.For(stored.Recording, build),
                progress.PlayedFrom(stored.Recording.RunId),
                recorded: stored.Started));
        }

        if (RunmobileSettings.Read().FetchRunIndex)
        {
            try
            {
                Shared.Clear();
                foreach (var item in Sharing.Index())
                {
                    var recording = ManifestJson.Deserialize(item.ManifestJson);
                    var origin = item.Featured ? RunOrigin.Featured : RunOrigin.Recent;
                    var described = LibraryRun.From(
                        recording, origin, RunVerdicts.For(recording, build),
                        progress.PlayedFrom(recording.RunId), recorded: item.SubmittedAt) with
                    {
                        Creator = item.Submission.DisplayName,
                    };
                    Shared[item.Code] = item with { Run = described };
                    if (runs.All(run => !string.Equals(run.RunId, described.RunId, StringComparison.Ordinal)))
                        runs.Add(described);
                }
            }
            catch (Exception ex)
            {
                Log.Error(
                    $"[{RunmobileMod.ModId}] could not fetch the run index: " +
                    $"{ex.GetType().Name}: {ex.Message}", 2);
            }
        }

        return runs;
    }

    /// <summary>
    /// Whether the library has a single run to offer.
    ///
    /// The Compendium button asks this each time the menu opens. A button opening an
    /// empty browser would be a promise the mod cannot keep on a game whose build no
    /// run in it was recorded on.
    ///
    /// It reads no manifest, which is what makes it cheap enough to ask on a menu open
    /// at all. The shipped recordings are in memory already and are judged the same way
    /// the list judges them, so on an ordinary build they answer it outright. Only when
    /// none of them is playable does it reach the player's own runs, and there it asks
    /// the recorder's directory index and nothing more: any finished recording is a
    /// reason to show the button.
    ///
    /// So the answer is one-directional rather than exact. It never hides a run the list
    /// would hold, and it may show the button onto a list that turns out empty - a run
    /// this game cannot play is one the browser then leaves out, with the numeral under
    /// the list saying how many. That is the direction to be wrong in: the browser is the
    /// only thing that judges and the button is the only way to the browser, so anything
    /// that could hide the button could close the way in for good.
    /// </summary>
    internal static bool HasAnythingToShow()
    {
        try
        {
            var build = ThisBuild();
            foreach (var included in Included())
            {
                if (LibraryRun.From(included, RunOrigin.Included, RunVerdicts.For(included, build)).Listed)
                {
                    return true;
                }
            }

            return RunLibraryStore.StoredRunIds().Count > 0;
        }
        catch (Exception ex)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] could not read the run library: {ex.GetType().Name}: {ex.Message}", 2);
            return false;
        }
    }

    /// <summary>The recording behind one row, or null when nothing here is that run.
    /// Resolved by id through the directory index, so pressing a row costs that
    /// recording's manifest and no other's.</summary>
    internal static ReplayManifest? RecordingFor(string runId)
    {
        foreach (var included in Included())
        {
            if (string.Equals(included.RunId, runId, StringComparison.Ordinal)) return included;
        }

        if (RunLibraryStore.RecordingFor(runId) is { } local) return local;
        var shared = Shared.Values.FirstOrDefault(item =>
            string.Equals(item.Run.RunId, runId, StringComparison.Ordinal));
        return shared is null ? null : ManifestJson.Deserialize(shared.ManifestJson);
    }

    internal static SharedRun? FindShared(string code)
    {
        var wanted = code.Trim();
        var cached = Shared.Values.FirstOrDefault(item =>
            string.Equals(item.Code, wanted, StringComparison.OrdinalIgnoreCase));
        if (cached is not null) return cached;
        if (!RunmobileSettings.Read().FetchRunIndex) return null;

        var found = Sharing.Find(wanted);
        if (found is null) return null;

        var recording = ManifestJson.Deserialize(found.ManifestJson);
        var described = LibraryRun.From(
            recording,
            found.Featured ? RunOrigin.Featured : RunOrigin.Recent,
            RunVerdicts.For(recording, ThisBuild()),
            RunLibraryStore.ReadProgress().PlayedFrom(recording.RunId),
            recorded: found.SubmittedAt) with
        {
            Creator = found.Submission.DisplayName,
        };
        var local = found with { Run = described };
        Shared[local.Code] = local;
        return local;
    }

    internal static SharedRun Share(ReplayManifest recording, ShareSubmission submission)
    {
        var valid = ManifestValidator.Validate(recording).IsValid;
        var native = recording.Source.Native;
        var publishable = valid && native is
            { IsContinuous: true, StatesSomethingOtherThanComplete: false } &&
            RunVerdicts.For(recording, ThisBuild()) == RunVerdict.Passed;
        if (!publishable)
            throw new ShareValidationException("Local validation did not pass, so the run was not sent.");
        return Sharing.Submit(ManifestJson.Serialize(recording), submission);
    }

    /// <summary>
    /// The build this game is, as every verdict and every refusal names it.
    ///
    /// Read from the game rather than remembered, because it is the one value the whole
    /// hidden rule turns on and a stale copy of it would hide the wrong runs.
    /// </summary>
    internal static string ThisBuild() => GameIdentity.Read().BuildVersion;

    /// <summary>
    /// The recordings that travel inside the mod.
    ///
    /// The Combat Trainer's, because it is the module that ships one and reads it. A
    /// second copy of that resource here would be a second thing to keep in step with
    /// what the assembly actually carries, and a Combat Trainer that refused to read
    /// its own recording would be one this list quietly disagreed with.
    /// </summary>
    private static IReadOnlyList<ReplayManifest> Included() =>
        CombatTrainerModule.Instance.Enabled ? [CombatTrainerModule.Instance.Recording] : [];
}

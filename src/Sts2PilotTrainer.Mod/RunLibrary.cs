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
/// is shown about whether a run plays comes from there and from nowhere else. The
/// Compendium entry is always present while the shell may draw so an empty local library,
/// a disabled automatic index fetch, or a transport failure never removes direct code
/// lookup.</para>
/// </summary>
internal static class RunLibrary
{
    private static readonly IRunSharingApi Sharing = new HttpRunSharingApi(new HttpClient
    {
        BaseAddress = new Uri("https://runs.runmobile.app/v1/"),
        Timeout = TimeSpan.FromSeconds(10),
    });

    private static readonly Dictionary<string, SharedRunSummary> SharedIndex = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, SharedRun> SharedRecordings = new(StringComparer.Ordinal);
    private static bool indexRequested;
    private static bool indexLoaded;
    private static string? exactRunId;
    private static int nextSubmission;
    private static readonly object PendingSubmissionLock = new();
    private static readonly Dictionary<int, string> PendingManifestJson = [];
    private static readonly Dictionary<int, ShareSubmission> PendingSubmissions = [];

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

        var includeIndex = RunmobileSettings.Read().FetchRunIndex;
        foreach (var item in SharedIndex.Values)
        {
            if (!includeIndex &&
                !string.Equals(item.Run.RunId, exactRunId, StringComparison.Ordinal))
            {
                continue;
            }

            var duplicate = false;
            foreach (var run in runs)
            {
                if (run.Origin != RunOrigin.Mine &&
                    string.Equals(run.RunId, item.Run.RunId, StringComparison.Ordinal))
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate) runs.Add(item.Run);
        }

        return runs;
    }

    internal static Task<IReadOnlyList<SharedRunSummary>> FetchIndexAsync()
    {
        indexRequested = true;
        return Sharing.IndexAsync();
    }

    internal static bool ShouldFetchIndex =>
        !indexRequested && !indexLoaded && RunmobileSettings.Read().FetchRunIndex;

    internal static void AcceptIndex(IReadOnlyList<SharedRunSummary> index)
    {
        var accepted = new Dictionary<string, SharedRunSummary>(StringComparer.Ordinal);
        var build = ThisBuild();
        foreach (var item in index)
        {
            if (!string.Equals(
                item.Code, SharedRunIdentity.CodeFor(item.ShareId), StringComparison.Ordinal))
            {
                throw new ShareValidationException(
                    "The run index contains an invalid sharing identity.");
            }

            var verdict = RunVerdicts.For(
                item.Environment, item.SourceKind, item.Run.RunId, build);
            accepted[item.Code] = item with { Run = OnlineRun(item, verdict) };
        }

        SharedIndex.Clear();
        foreach (var item in accepted) SharedIndex[item.Key] = item.Value;
        indexRequested = false;
        indexLoaded = true;
    }

    internal static LibraryRun OnlineRun(SharedRunSummary item, RunVerdict verdict)
    {
        item.Submission.Validate();
        return item.Run with
        {
            Origin = item.Featured ? RunOrigin.Featured : RunOrigin.Recent,
            Creator = item.Submission.DisplayName,
            Character = item.Environment.Character.Value,
            Ascension = item.Environment.Ascension.Value,
            RecordedBuild = item.Environment.BuildVersion.Value,
            Verdict = verdict,
            Recorded = item.SubmittedAt,
        };
    }

    internal static void RefuseIndex()
    {
        indexRequested = false;
        indexLoaded = false;
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
        var shared = SharedRecordings.Values.FirstOrDefault(item =>
            string.Equals(item.Run.RunId, runId, StringComparison.Ordinal));
        return shared is null ? null : ManifestJson.Deserialize(shared.ManifestJson);
    }

    internal static Task<SharedRun?> FindSharedAsync(string code)
    {
        var wanted = code.Trim();
        var cached = SharedRecordings.Values.FirstOrDefault(item =>
            string.Equals(item.Code, wanted, StringComparison.OrdinalIgnoreCase));
        return cached is null ? Sharing.FindAsync(wanted) : Task.FromResult<SharedRun?>(cached);
    }

    internal static SharedRun AcceptShared(
        SharedRun found, string? expectedCode = null, bool exact = false)
    {
        found.Submission.Validate();
        var computedId = SharedRunIdentity.For(found.ManifestJson, found.Submission);
        var computedCode = SharedRunIdentity.CodeFor(computedId);
        if (!string.Equals(found.ShareId, computedId, StringComparison.Ordinal) ||
            !string.Equals(found.Code, computedCode, StringComparison.Ordinal) ||
            expectedCode is not null &&
            !string.Equals(expectedCode.Trim(), found.Code, StringComparison.OrdinalIgnoreCase))
        {
            throw new ShareValidationException(
                "The downloaded run does not match its advertised sharing identity.");
        }

        foreach (var advertised in SharedIndex.Values)
        {
            if (string.Equals(advertised.Code, found.Code, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(advertised.ShareId, found.ShareId, StringComparison.Ordinal))
            {
                throw new ShareValidationException(
                    "The downloaded run does not match the run index entry.");
            }
        }

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
        SharedRecordings[local.Code] = local;
        SharedIndex[local.Code] = local.Summary;
        if (exact) exactRunId = local.Run.RunId;
        return local;
    }

    internal static string? ShareCodeFor(string runId) =>
        SharedIndex.Values.FirstOrDefault(item =>
            string.Equals(item.Run.RunId, runId, StringComparison.Ordinal))?.Code;

    internal static Task<SharedRun> ShareAsync(
        ReplayManifest recording, ShareSubmission submission)
    {
        submission.Validate();
        var gate = PublicationGate.RunAsync(recording);
        var request = Interlocked.Increment(ref nextSubmission);
        lock (PendingSubmissionLock)
        {
            PendingManifestJson[request] = ManifestJson.Serialize(recording);
            PendingSubmissions[request] = submission;
        }
        return gate.ContinueWith(
            static (completed, value) => CompletePublicationGate(completed, (int)value!),
            request,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default).Unwrap();
    }

    private static Task<SharedRun> CompletePublicationGate(Task<bool> completed, int request)
    {
        string manifestJson;
        ShareSubmission submission;
        lock (PendingSubmissionLock)
        {
            manifestJson = PendingManifestJson[request];
            submission = PendingSubmissions[request];
            PendingManifestJson.Remove(request);
            PendingSubmissions.Remove(request);
        }
        if (!completed.IsCompletedSuccessfully)
            throw completed.Exception?.GetBaseException()
                ?? new ShareValidationException("Local validation could not finish.");
        if (!completed.Result)
            throw new ShareValidationException("Local validation did not pass, so the run was not sent.");
        return Sharing.SubmitAsync(manifestJson, submission);
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

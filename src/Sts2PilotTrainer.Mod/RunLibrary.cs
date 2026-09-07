using System.Text.Json;
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
/// is shown about whether a run plays comes from there and from nowhere else. The
/// Compendium entry is always present while the shell may draw so an empty local library,
/// a disabled automatic index fetch, or a transport failure never removes direct code
/// lookup.</para>
/// </summary>
internal static class RunLibrary
{
    private static readonly Dictionary<string, SharedRunSummary> SharedIndex = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, SharedRun> SharedRecordings = new(StringComparer.Ordinal);
    private static bool indexRequested;
    private static bool indexLoaded;
    private static IRunSharingApi? configuredSharing;
    private static string? configuredSharingEndpoint;
    private static string? sharingScope;
    private static readonly object SharingLock = new();
    private static int nextSubmission;
    private static readonly object PendingSubmissionLock = new();
    private static readonly Dictionary<int, string> PendingManifestJson = [];
    private static readonly Dictionary<int, ShareSubmission> PendingSubmissions = [];
    private static readonly Dictionary<int, string> PendingSharingEndpoints = [];
    private static readonly Dictionary<int, string> PendingSharingScopes = [];
    private static readonly Dictionary<int, TaskCompletionSource<SharedRun>> PendingSharingResults = [];

    /// <summary>
    /// Every run, listed or not, with the verdict that decides which.
    ///
    /// The hidden ones are here on purpose: the numeral under the list counts them, and
    /// a run code finds them. A list filtered before it arrived could do neither.
    /// </summary>
    internal static IReadOnlyList<LibraryRun> Runs()
    {
        RefreshSharingScope();
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

        var shared = new Dictionary<string, SharedRunSummary>(StringComparer.Ordinal);
        if (RunmobileSettings.Read().FetchRunIndex)
        {
            foreach (var item in SharedIndex) shared[item.Key] = item.Value;
        }
        foreach (var item in SharedRecordings) shared[item.Key] = item.Value.Summary;

        foreach (var item in shared.Values)
        {
            runs.Add(item.Run with
            {
                FightsPlayed = progress.PlayedFrom(item.Run.RunId),
            });
        }

        return runs;
    }

    internal static Task<IReadOnlyList<SharedRunSummary>> FetchIndexAsync() =>
        FetchIndexAsync(out _);

    internal static Task<IReadOnlyList<SharedRunSummary>> FetchIndexAsync(out string scope)
    {
        var endpoint = RequireSharingEndpoint();
        scope = CurrentSharingScope();
        indexRequested = true;
        return SharingAt(endpoint).IndexAsync();
    }

    internal static bool SharingAvailable => RefreshSharingScope() is not null;

    internal static bool ShouldFetchIndex =>
        SharingAvailable && !indexRequested && !indexLoaded && RunmobileSettings.Read().FetchRunIndex;

    internal static void AcceptIndex(IReadOnlyList<SharedRunSummary> index) =>
        AcceptIndex(index, CurrentSharingScope());

    internal static bool AcceptIndex(IReadOnlyList<SharedRunSummary> index, string expectedScope)
    {
        RefreshSharingScope();
        if (!IsCurrentSharingScope(expectedScope)) return false;
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
            if (accepted.Values.Any(acceptedItem =>
                    string.Equals(acceptedItem.Code, item.Code, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ShareValidationException(
                    "The run index assigns one sharing code to more than one run.");
            }

            accepted[item.ShareId] = item with
            {
                Run = OnlineRun(item, verdict),
            };
        }

        SharedIndex.Clear();
        foreach (var item in accepted) SharedIndex[item.Key] = item.Value;
        indexRequested = false;
        indexLoaded = true;
        return true;
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
            FightsPlayed = [],
            Recorded = item.SubmittedAt,
            ShareId = item.ShareId,
            ShareCode = item.Code,
        };
    }

    internal static void RefuseIndex() => RefuseIndex(CurrentSharingScope());

    internal static void RefuseIndex(string expectedScope)
    {
        RefreshSharingScope();
        if (!IsCurrentSharingScope(expectedScope)) return;
        indexRequested = false;
        indexLoaded = false;
    }

    /// <summary>The recording behind one row, or null when nothing here is that run.
    /// Resolved by id through the directory index, so pressing a row costs that
    /// recording's manifest and no other's.</summary>
    internal static ReplayManifest? RecordingFor(string entryId)
    {
        RefreshSharingScope();
        var shared = SharedRecordings.Values.FirstOrDefault(item =>
            string.Equals(item.ShareId, entryId, StringComparison.Ordinal));
        if (shared is not null) return ManifestJson.Deserialize(shared.ManifestJson);

        foreach (var included in Included())
        {
            if (string.Equals(included.RunId, entryId, StringComparison.Ordinal)) return included;
        }

        return RunLibraryStore.RecordingFor(entryId);
    }

    internal static Task<SharedRun?> FindSharedAsync(string code) =>
        FindSharedAsync(code, out _);

    internal static Task<SharedRun?> FindSharedAsync(string code, out string scope)
    {
        RefreshSharingScope();
        scope = CurrentSharingScope();
        var wanted = code.Trim();
        var cached = SharedRecordings.Values.FirstOrDefault(item =>
            string.Equals(item.Code, wanted, StringComparison.OrdinalIgnoreCase));
        if (cached is not null) return Task.FromResult<SharedRun?>(cached);
        return SharingAt(RequireSharingEndpoint()).FindAsync(wanted);
    }

    internal static SharedRun AcceptShared(
        SharedRun found, string? expectedCode = null, string? expectedScope = null)
    {
        RefreshSharingScope();
        if (expectedScope is not null && !IsCurrentSharingScope(expectedScope))
            throw new ShareValidationException("The sharing profile changed before that request finished.");
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

        var recording = ManifestJson.Deserialize(found.ManifestJson);
        var advertised = SharedIndex.Values.FirstOrDefault(item =>
            string.Equals(item.Code, found.Code, StringComparison.OrdinalIgnoreCase));
        if (advertised is not null && !IndexDescribes(advertised, found, recording))
        {
            throw new ShareValidationException(
                "The downloaded run does not match the run index entry.");
        }

        var described = LibraryRun.From(
            recording,
            found.Featured ? RunOrigin.Featured : RunOrigin.Recent,
            RunVerdicts.For(recording, ThisBuild()),
            fightsPlayed: [],
            recorded: found.SubmittedAt) with
        {
            Creator = found.Submission.DisplayName,
            ShareId = found.ShareId,
            ShareCode = found.Code,
        };
        var local = found with { Run = described };
        SharedRecordings[local.ShareId] = local;
        return local;
    }

    internal static Task<SharedRun> ShareAsync(
        ReplayManifest recording, ShareSubmission submission) =>
        ShareAsync(recording, submission, out _);

    internal static Task<SharedRun> ShareAsync(
        ReplayManifest recording, ShareSubmission submission, out string scope)
    {
        submission.Validate();
        var endpoint = RequireSharingEndpoint();
        scope = CurrentSharingScope();
        var gate = PublicationGate.RunAsync(recording);
        var request = Interlocked.Increment(ref nextSubmission);
        var result = new TaskCompletionSource<SharedRun>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        lock (PendingSubmissionLock)
        {
            PendingManifestJson[request] = ManifestJson.Serialize(recording);
            PendingSubmissions[request] = submission;
            PendingSharingEndpoints[request] = endpoint;
            PendingSharingScopes[request] = scope;
            PendingSharingResults[request] = result;
        }
        _ = gate.ContinueWith(
            static (completed, value) =>
            {
                var request = (int)value!;
                Godot.Callable.From(() => CompletePublicationGate(completed, request)).CallDeferred();
            },
            request,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return result.Task;
    }

    private static void CompletePublicationGate(Task<bool> completed, int request)
    {
        string manifestJson;
        ShareSubmission submission;
        string endpoint;
        string scope;
        TaskCompletionSource<SharedRun> result;
        lock (PendingSubmissionLock)
        {
            manifestJson = PendingManifestJson[request];
            submission = PendingSubmissions[request];
            endpoint = PendingSharingEndpoints[request];
            scope = PendingSharingScopes[request];
            result = PendingSharingResults[request];
            PendingManifestJson.Remove(request);
            PendingSubmissions.Remove(request);
            PendingSharingEndpoints.Remove(request);
            PendingSharingScopes.Remove(request);
            PendingSharingResults.Remove(request);
        }

        if (!completed.IsCompletedSuccessfully)
        {
            result.SetException(completed.Exception?.GetBaseException()
                ?? new ShareValidationException("Local validation could not finish."));
            return;
        }
        if (!completed.Result)
        {
            result.SetException(new ShareValidationException(
                "Local validation did not pass, so the run was not sent."));
            return;
        }
        if (!IsCurrentSharingScope(scope))
        {
            result.SetException(new ShareValidationException(
                "The sharing profile changed before local validation finished, so the run was not sent."));
            return;
        }

        Task<SharedRun> submissionTask;
        try
        {
            submissionTask = SharingAt(endpoint).SubmitAsync(manifestJson, submission);
        }
        catch (Exception ex)
        {
            result.SetException(ex);
            return;
        }

        _ = submissionTask.ContinueWith(
            static (finished, value) => CompleteSubmission(finished, (TaskCompletionSource<SharedRun>)value!),
            result,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void CompleteSubmission(
        Task<SharedRun> completed, TaskCompletionSource<SharedRun> result)
    {
        if (completed.IsCompletedSuccessfully)
        {
            result.SetResult(completed.Result);
        }
        else if (completed.IsCanceled)
        {
            result.SetCanceled();
        }
        else
        {
            result.SetException(completed.Exception?.GetBaseException()
                ?? new ShareValidationException("Sharing could not finish."));
        }
    }

    private static bool IndexDescribes(
        SharedRunSummary advertised, SharedRun found, ReplayManifest recording)
    {
        var described = LibraryRun.From(recording, RunOrigin.Recent, RunVerdict.Unjudged);
        return string.Equals(advertised.ShareId, found.ShareId, StringComparison.Ordinal) &&
               advertised.Submission == found.Submission &&
               string.Equals(advertised.Run.RunId, described.RunId, StringComparison.Ordinal) &&
               string.Equals(
                   advertised.Run.Creator, found.Submission.DisplayName, StringComparison.Ordinal) &&
               string.Equals(advertised.Run.Character, described.Character, StringComparison.Ordinal) &&
               advertised.Run.Ascension == described.Ascension &&
               string.Equals(
                   advertised.Run.RecordedBuild, described.RecordedBuild, StringComparison.Ordinal) &&
               advertised.Run.Fights.SequenceEqual(described.Fights) &&
               string.Equals(advertised.Run.Outcome, described.Outcome, StringComparison.Ordinal) &&
               string.Equals(advertised.SourceKind, recording.Source.Kind, StringComparison.Ordinal) &&
               string.Equals(
                   JsonSerializer.Serialize(advertised.Environment, ManifestJson.Options),
                   JsonSerializer.Serialize(recording.Environment, ManifestJson.Options),
                   StringComparison.Ordinal);
    }

    private static string RequireSharingEndpoint() =>
        RefreshSharingScope()
        ?? throw new ShareValidationException(LibraryCopy.SharingServiceUnavailable);

    private static string? ConfiguredSharingEndpoint()
    {
        var configured = RunmobileSettings.Read().SharingServiceUrl;
        if (string.IsNullOrWhiteSpace(configured)) return null;
        if (!Uri.TryCreate(configured, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            endpoint.UserInfo.Length > 0 ||
            endpoint.Query.Length > 0 ||
            endpoint.Fragment.Length > 0)
        {
            Log.Error(
                $"[{RunmobileMod.ModId}] settings.json does not name an authorized HTTPS sharing " +
                "service, so no network request will be made.", 2);
            return null;
        }

        return endpoint.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? endpoint.AbsoluteUri
            : endpoint.AbsoluteUri + "/";
    }

    private static string? RefreshSharingScope()
    {
        var endpoint = ConfiguredSharingEndpoint();
        string root;
        try
        {
            root = RunmobileStore.Root;
        }
        catch (Exception)
        {
            root = string.Empty;
        }

        var current = $"{root}\n{endpoint}";
        lock (SharingLock)
        {
            if (!string.Equals(sharingScope, current, StringComparison.Ordinal))
            {
                SharedIndex.Clear();
                SharedRecordings.Clear();
                indexRequested = false;
                indexLoaded = false;
                configuredSharing = null;
                configuredSharingEndpoint = null;
                sharingScope = current;
            }
        }

        return endpoint;
    }

    internal static string CurrentSharingScope()
    {
        RefreshSharingScope();
        lock (SharingLock) return sharingScope!;
    }

    internal static bool IsCurrentSharingScope(string expectedScope)
    {
        RefreshSharingScope();
        lock (SharingLock)
            return string.Equals(sharingScope, expectedScope, StringComparison.Ordinal);
    }

    private static IRunSharingApi SharingAt(string endpoint)
    {
        lock (SharingLock)
        {
            if (configuredSharing is null ||
                !string.Equals(configuredSharingEndpoint, endpoint, StringComparison.Ordinal))
            {
                configuredSharing = new HttpRunSharingApi(new HttpClient(
                    new HttpClientHandler { AllowAutoRedirect = false })
                {
                    BaseAddress = new Uri(endpoint),
                    Timeout = TimeSpan.FromSeconds(10),
                });
                configuredSharingEndpoint = endpoint;
            }

            return configuredSharing;
        }
    }

    internal static void ResetSharedRunsForTesting()
    {
        SharedIndex.Clear();
        SharedRecordings.Clear();
        indexRequested = false;
        indexLoaded = false;
        configuredSharing = null;
        configuredSharingEndpoint = null;
        sharingScope = null;
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

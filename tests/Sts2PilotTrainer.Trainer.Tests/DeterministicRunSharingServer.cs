using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Trainer.Tests;

internal sealed class DeterministicRunSharingServer(
    Func<ReplayManifest, bool> publicationGate,
    Func<ReplayManifest, LibraryRun> describe,
    Func<DateTimeOffset>? clock = null,
    Func<ReplayManifest, bool>? featured = null,
    bool asynchronous = false) : HttpMessageHandler
{
    private readonly object transaction = new();
    private readonly Dictionary<string, SharedRun> shared = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Pending> pending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> reservedCodes = new(StringComparer.Ordinal);
    private readonly HashSet<string> removed = new(StringComparer.Ordinal);
    private long revision;
    internal SharingPolicy? Policy { get; set; }
    internal SharingProtocol Protocol { get; set; } = new(2, 2, RunSharingProtocol.MaximumRequestBytes);
    internal Action? BeforeAdmission { get; set; }
    internal bool DropNextAcceptanceResponse { get; set; }
    internal Func<string, string> ReserveCode { get; set; } = SharedRunIdentity.CodeFor;
    internal int PageSize { get; set; } = RunSharingProtocol.MaximumPageSize;
    private LocalBuild? browseBuild;
    internal LocalBuild? BrowseBuild
    {
        get { lock (transaction) return browseBuild; }
        set { lock (transaction) { browseBuild = value; revision++; } }
    }
    private int admissionRequests;
    internal int AdmissionRequests => Volatile.Read(ref admissionRequests);
    internal int AcceptedCount { get { lock (transaction) return pending.Count; } }
    internal DateTimeOffset Now => (clock ?? (() => DateTimeOffset.UtcNow))();

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var path = request.RequestUri?.AbsolutePath.Trim('/') ?? string.Empty;
            var versioned = request.Headers.TryGetValues(RunSharingProtocol.VersionHeader, out var versions) &&
                versions.Single() == RunSharingProtocol.Version.ToString(CultureInfo.InvariantCulture);
            if (path == "sharing-protocol") return Json(Protocol);
            if (asynchronous && !versioned)
                return Problem(HttpStatusCode.UpgradeRequired, SharingError.UnsupportedClient, "Update Runmobile to use this sharing service.");
            if (path == "sharing-policy") return Json(Policy!);
            var principal = request.Headers.Authorization?.Parameter;
            if (path == "runs" && request.Method == HttpMethod.Post)
            {
                Interlocked.Increment(ref admissionRequests);
                if (asynchronous && principal != "alice" && principal != "bob")
                    return Problem(HttpStatusCode.Unauthorized, SharingError.Unauthorized, "A verified service session is required.");
                if (request.Content is null || request.Content.Headers.ContentLength > Protocol.MaximumRequestBytes ||
                    request.Content.Headers.ContentEncoding.Count != 0)
                    return Problem(HttpStatusCode.RequestEntityTooLarge, SharingError.TooLarge, "Submission too large or encoded.");
                var bytes = await HttpRunSharingApi.ReadBounded(request.Content, Protocol.MaximumRequestBytes, cancellationToken);
                if (!asynchronous)
                {
                    var body = Decode<HttpRunSharingApi.ShareRequest>(bytes);
                    return Json(Submit(body.ManifestJson, body.Submission));
                }
                var admission = Decode<HttpRunSharingApi.AdmissionRequest>(bytes);
                BeforeAdmission?.Invoke();
                lock (transaction)
                {
                    var status = Admit(admission, principal!);
                    if (DropNextAcceptanceResponse)
                    {
                        DropNextAcceptanceResponse = false;
                        throw new HttpRequestException("The acceptance response was lost.");
                    }
                    return Json(status, status.State == PublicationState.Processing ? HttpStatusCode.Accepted : HttpStatusCode.OK);
                }
            }
            if (request.Method == HttpMethod.Get && path.StartsWith("submissions/", StringComparison.Ordinal))
            {
                if (principal != "alice" && principal != "bob")
                    return Problem(HttpStatusCode.Unauthorized, SharingError.Unauthorized, "A verified service session is required.");
                lock (transaction)
                {
                    const string identityPath = "submissions/by-identity/";
                    var byIdentity = path.StartsWith(identityPath, StringComparison.Ordinal);
                    var entry = pending.Values.SingleOrDefault(row => row.Principal == principal && (byIdentity
                        ? row.Attempt.Status.Receipt.ShareId == path[identityPath.Length..]
                        : row.Attempt.Status.Receipt.ReceiptId == path[12..]));
                    if (entry is null) return Problem(HttpStatusCode.NotFound, SharingError.NotFound, "No private receipt was found.");
                    entry.Attempt.Expire(Now);
                    return Json(entry.Attempt.Status);
                }
            }
            if (request.Method == HttpMethod.Get && path == "runs")
            {
                lock (transaction)
                {
                    if (!asynchronous) return Json(Index());
                    var cursor = request.RequestUri!.Query.Length == 0 ? null : Uri.UnescapeDataString(request.RequestUri.Query[8..]);
                    var offset = 0;
                    if (cursor is not null)
                    {
                        var parts = cursor.Split(':');
                        if (parts.Length != 2 || !long.TryParse(parts[0], out var cursorRevision) ||
                            cursorRevision != revision || !int.TryParse(parts[1], out offset) || offset < 0)
                            return Problem(HttpStatusCode.Conflict, SharingError.StalePolicy, "Catalogue changed; begin a fresh page read.");
                    }
                    var tag = $"\"{revision}-{offset}\"";
                    if (request.Headers.IfNoneMatch.Any(value => value.ToString() == tag))
                    {
                        var unchanged = new HttpResponseMessage(HttpStatusCode.NotModified);
                        unchanged.Headers.ETag = new(tag);
                        return unchanged;
                    }
                    var index = Index();
                    var page = new SharedRunPage(index.Skip(offset).Take(PageSize).ToArray(),
                        offset + PageSize < index.Count ? $"{revision}:{offset + PageSize}" : null, tag);
                    var response = Json(page);
                    response.Headers.ETag = new(tag);
                    return response;
                }
            }
            if (request.Method == HttpMethod.Get && path.StartsWith("runs/", StringComparison.Ordinal))
            {
                lock (transaction)
                {
                    var found = Find(Uri.UnescapeDataString(path[5..]));
                    return found is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(found);
                }
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
        catch (ShareProtocolException ex)
        {
            return Problem(ex.Error switch
            {
                SharingError.Forbidden => HttpStatusCode.Forbidden,
                SharingError.TooLarge => HttpStatusCode.RequestEntityTooLarge,
                _ => HttpStatusCode.Conflict,
            }, ex.Error, ex.Message);
        }
        catch (Exception ex) when (ex is ShareValidationException or ManifestException or JsonException or ArgumentException)
        {
            return Problem(HttpStatusCode.BadRequest, SharingError.Malformed, ex.Message);
        }
    }

    private static T Decode<T>(byte[] bytes) => JsonSerializer.Deserialize<T>(bytes, RunSharingProtocol.CreateJsonOptions())
        ?? throw new ShareValidationException("No submission was sent.");

    private SubmissionStatus Admit(HttpRunSharingApi.AdmissionRequest request, string principal)
    {
        if (request.ManifestJson is null || request.Submission is null)
            throw new ShareValidationException("A recording and submission are required.");
        request.Submission.Validate();
        var manifest = ManifestJson.Deserialize(request.ManifestJson);
        var validation = ManifestValidator.Validate(manifest);
        if (!validation.IsValid)
            throw new ShareValidationException(string.Join(" ", validation.Problems));
        Policy!.For(request.Branch).RequireAdmission(manifest.Environment, request.PolicyGeneration, Now);
        var canonical = ManifestJson.Serialize(manifest);
        var hash = SharedRunIdentity.For(canonical, request.Submission);
        if (pending.TryGetValue(hash, out var existing))
        {
            if (existing.Principal != principal)
                throw new ShareProtocolException(SharingError.Forbidden, "This private submission belongs to another session.");
            existing.Attempt.Expire(Now);
            return existing.Attempt.Status;
        }
        var code = ReserveCode(hash);
        if (reservedCodes.TryGetValue(code, out var reserved) && reserved != hash)
            throw new ShareProtocolException(SharingError.CodeCollision, "The run code is already reserved. This run was not submitted.");
        var receipt = new SubmissionReceipt($"private-{pending.Count + 1}", hash, request.Branch,
            request.PolicyGeneration, Policy.For(request.Branch).Engine!, Now, Now + RunSharingProtocol.MaximumProcessingTime);
        var attempt = new PublicationAttempt(receipt);
        reservedCodes[code] = hash;
        pending.Add(hash, new Pending(principal, canonical, request.Submission, attempt));
        return attempt.Status;
    }

    internal void BeginUpdate(RunBranch branch, string buildId)
    {
        lock (transaction)
            Policy = new SharingPolicy(Policy!.Branches.Select(row => row.Branch == branch
                ? row.BeginUpdate(buildId, Now, Now.AddMinutes(5)) : row).ToArray());
    }

    internal bool Complete(string hash, int generation, PublicationState outcome = PublicationState.Published)
    {
        lock (transaction)
        {
            var entry = pending[hash];
            SharedRun? result = null;
            if (outcome == PublicationState.Published)
            {
                var manifest = ManifestJson.Deserialize(entry.Manifest);
                if (!publicationGate(manifest)) outcome = PublicationState.Refused;
                else result = MakeShared(manifest, entry.Manifest, entry.Submission, hash);
            }
            var committed = entry.Attempt.Complete(generation, outcome, result,
                outcome == PublicationState.Published ? null : "The remote publication gate did not pass or could not finish.", Now);
            if (committed && result is not null)
            {
                shared.Add(hash, result);
                revision++;
            }
            return committed;
        }
    }

    internal bool Retry(string hash, int generation)
    {
        lock (transaction) return pending[hash].Attempt.Retry(generation, Now);
    }

    internal void Remove(string hash)
    {
        lock (transaction) { removed.Add(hash); revision++; }
    }

    private SharedRun Submit(string manifestJson, ShareSubmission submission)
    {
        if (manifestJson is null || submission is null)
            throw new ShareValidationException("A recording and submission are required.");
        submission.Validate();
        var manifest = ManifestJson.Deserialize(manifestJson);
        if (!publicationGate(manifest))
            throw new ShareValidationException("Local validation did not pass, so the run was not sent.");
        var canonical = ManifestJson.Serialize(manifest);
        var hash = SharedRunIdentity.For(canonical, submission);
        if (shared.TryGetValue(hash, out var existing)) return existing;
        var result = MakeShared(manifest, canonical, submission, hash);
        shared.Add(hash, result);
        return result;
    }

    private SharedRun MakeShared(ReplayManifest manifest, string canonical, ShareSubmission submission, string hash) => new(
        hash, SharedRunIdentity.CodeFor(hash), canonical, submission,
        describe(manifest) with { Creator = submission.DisplayName }, Now, featured?.Invoke(manifest) == true);

    private LibraryRun Project(SharedRun run) => browseBuild is null ||
        EnvironmentPreflight.Build(ManifestJson.Deserialize(run.ManifestJson).Environment, browseBuild).All(field => field.Matches)
            ? run.Run : run.Run with { Verdict = RunVerdict.Absent };

    private IReadOnlyList<SharedRunSummary> Index() => shared.Values.Where(run => !removed.Contains(run.ShareId))
        .OrderByDescending(run => run.Featured).ThenByDescending(run => run.SubmittedAt)
        .ThenBy(run => run.ShareId, StringComparer.Ordinal).Select(run => (run with { Run = Project(run) }).Summary).ToArray();

    private SharedRun? Find(string code)
    {
        var run = shared.Values.SingleOrDefault(run => !removed.Contains(run.ShareId) &&
            string.Equals(run.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
        return run is null ? null : run with { Run = Project(run) };
    }

    private static HttpResponseMessage Problem(HttpStatusCode status, SharingError error, string message) =>
        Json(new SharingProblem(error, message), status);

    private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = JsonContent.Create(value, options: RunSharingProtocol.CreateJsonOptions()) };

    private sealed record Pending(string Principal, string Manifest, ShareSubmission Submission, PublicationAttempt Attempt);
}

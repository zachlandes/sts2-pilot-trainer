using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

public sealed partial class HttpRunSharingApi
{
    private static readonly JsonSerializerOptions WireJson = RunSharingProtocol.CreateJsonOptions();

    public async Task<SharingProtocol> NegotiateAsync(CancellationToken cancellationToken = default)
    {
        using var request = Versioned(HttpMethod.Get, "sharing-protocol");
        using var response = await Send(request, cancellationToken).ConfigureAwait(false);
        var protocol = await Read<SharingProtocol>(response, cancellationToken).ConfigureAwait(false);
        if (protocol.MinimumVersion > RunSharingProtocol.Version || protocol.MaximumVersion < RunSharingProtocol.Version ||
            protocol.MinimumVersion <= 0 || protocol.MinimumVersion > protocol.MaximumVersion || protocol.MaximumRequestBytes <= 0)
            throw new ShareProtocolException(SharingError.UnsupportedClient,
                "Update Runmobile to keep sharing runs.");
        return protocol;
    }

    public async Task<SharingPolicy> PolicyAsync(CancellationToken cancellationToken = default)
    {
        using var request = Versioned(HttpMethod.Get, "sharing-policy");
        using var response = await Send(request, cancellationToken).ConfigureAwait(false);
        var policy = await Read<SharingPolicy>(response, cancellationToken).ConfigureAwait(false);
        if (policy.Branches is null || policy.Branches.Count != 2)
            throw new ShareProtocolException(SharingError.Malformed, "Sharing sent back settings Runmobile couldn't read. Try again later.");
        _ = policy.For(RunBranch.Public);
        _ = policy.For(RunBranch.Beta);
        return policy;
    }

    public async Task<SubmissionStatus> SubmitForPublicationAsync(
        string manifestJson, ShareSubmission submission, RunBranch branch,
        Func<CancellationToken, Task<bool>> localPublicationGate,
        Func<DateTimeOffset> clock, CancellationToken cancellationToken = default)
    {
        submission.Validate();
        RequirePayloadSize(manifestJson);
        _ = await NegotiateAsync(cancellationToken).ConfigureAwait(false);
        _ = SharedRunIdentity.For(manifestJson, submission);
        var recording = ManifestJson.Deserialize(manifestJson);
        var before = (await PolicyAsync(cancellationToken).ConfigureAwait(false)).For(branch);
        before.RequireAdmission(recording.Environment, before.Generation, clock());
        if (!await localPublicationGate(cancellationToken).ConfigureAwait(false))
            throw new ShareValidationException("This run didn't pass Runmobile's checks, so it wasn't shared.");
        var after = (await PolicyAsync(cancellationToken).ConfigureAwait(false)).For(branch);
        after.RequireAdmission(recording.Environment, before.Generation, clock());
        return await AdmitAsync(manifestJson, submission, branch, before.Generation, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SubmissionStatus> AdmitAsync(
        string manifestJson, ShareSubmission submission, RunBranch branch, long policyGeneration,
        CancellationToken cancellationToken = default)
    {
        submission.Validate();
        RequirePayloadSize(manifestJson);
        if (!Enum.IsDefined(branch) || policyGeneration <= 0)
            throw new ShareProtocolException(SharingError.Malformed, "Sharing couldn't start. Try again later.");
        var protocol = await NegotiateAsync(cancellationToken).ConfigureAwait(false);
        var identity = SharedRunIdentity.For(manifestJson, submission);
        var recording = ManifestJson.Deserialize(manifestJson);
        using var request = Versioned(HttpMethod.Post, "runs");
        request.Content = BoundedJson(new AdmissionRequest(manifestJson, submission, branch, policyGeneration),
            Math.Min(protocol.MaximumRequestBytes, RunSharingProtocol.MaximumRequestBytes));
        // A wire retry still resolves through the server's canonical identity reservation
        request.Headers.TryAddWithoutValidation("Idempotency-Key", identity);
        using var response = await Send(request, cancellationToken).ConfigureAwait(false);
        var status = await Read<SubmissionStatus>(response, cancellationToken).ConfigureAwait(false);
        status.Validate();
        if (status.Receipt.ShareId != identity ||
            !EnvironmentPreflight.Build(recording.Environment, status.Receipt.Engine).All(field => field.Matches))
            throw new ShareProtocolException(SharingError.Malformed, "Sharing mixed up this run with another. Try again later.");
        if (status.PublishedRun is { } shared)
            SharedRunIdentity.RequireMatch(shared, manifestJson, submission);
        return status;
    }

    public async Task<SubmissionStatus> StatusAsync(
        SubmissionReceipt receipt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(receipt.ReceiptId) || receipt.ReceiptId.Length > 128)
            throw new ShareProtocolException(SharingError.Malformed, "This share receipt isn't valid.");
        using var request = Versioned(HttpMethod.Get, $"submissions/{Uri.EscapeDataString(receipt.ReceiptId)}");
        using var response = await Send(request, cancellationToken).ConfigureAwait(false);
        var status = await Read<SubmissionStatus>(response, cancellationToken).ConfigureAwait(false);
        status.Validate();
        if (status.Receipt != receipt)
            throw new ShareProtocolException(SharingError.Malformed, "Sharing sent back the status of a different run. Try again later.");
        return status;
    }

    public async Task<SubmissionStatus> StatusForIdentityAsync(
        string shareId, CancellationToken cancellationToken = default)
    {
        _ = SharedRunIdentity.CodeFor(shareId);
        using var request = Versioned(HttpMethod.Get, $"submissions/by-identity/{shareId}");
        using var response = await Send(request, cancellationToken).ConfigureAwait(false);
        var status = await Read<SubmissionStatus>(response, cancellationToken).ConfigureAwait(false);
        status.Validate();
        if (status.Receipt.ShareId != shareId)
            throw new ShareProtocolException(SharingError.Malformed, "Sharing sent back the status of a different run. Try again later.");
        return status;
    }

    public async Task<SharedRunPage> PageAsync(
        string? cursor = null, string? etag = null, CancellationToken cancellationToken = default)
    {
        if (cursor?.Length > RunSharingProtocol.MaximumCursorCharacters)
            throw new ShareProtocolException(SharingError.Malformed, "Couldn't load more runs.");
        using var request = Versioned(HttpMethod.Get, cursor is null ? "runs" : $"runs?cursor={Uri.EscapeDataString(cursor)}");
        if (etag is not null)
        {
            if (etag.Length > RunSharingProtocol.MaximumEntityTagCharacters || !EntityTagHeaderValue.TryParse(etag, out var parsed))
                throw new ShareProtocolException(SharingError.Malformed, "Couldn't refresh the run list. Try again later.");
            request.Headers.IfNoneMatch.Add(parsed);
        }
        using var response = await Send(request, cancellationToken).ConfigureAwait(false);
        var returnedTag = response.Headers.ETag?.ToString();
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            if (etag is null || returnedTag != etag)
                throw new ShareProtocolException(SharingError.Malformed, "Couldn't refresh the run list. Try again later.");
            return new SharedRunPage([], cursor, etag, NotModified: true);
        }
        var page = await Read<SharedRunPage>(response, cancellationToken).ConfigureAwait(false);
        if (page.Runs is null || page.Runs.Count > RunSharingProtocol.MaximumPageSize ||
            page.NextCursor?.Length > RunSharingProtocol.MaximumCursorCharacters || page.NotModified ||
            returnedTag is null || returnedTag.Length > RunSharingProtocol.MaximumEntityTagCharacters || page.ETag != returnedTag)
            throw new ShareProtocolException(SharingError.Malformed, "Couldn't load the run list. Try again later.");
        foreach (var row in page.Runs)
        {
            if (row is null || row.Submission is null || row.Run is null || row.Environment is null)
                throw new ShareProtocolException(SharingError.Malformed, "One run in the list came back incomplete.");
            row.Submission.Validate();
            if (row.Code != SharedRunIdentity.CodeFor(row.ShareId))
                throw new ShareProtocolException(SharingError.Malformed, "One run in the list has a code that doesn't match it.");
        }
        return page;
    }

    private static HttpRequestMessage Versioned(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add(RunSharingProtocol.VersionHeader, RunSharingProtocol.Version.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return request;
    }

    private static void RequirePayloadSize(string manifestJson)
    {
        if (manifestJson is null || manifestJson.Length > RunSharingProtocol.MaximumRequestBytes ||
            System.Text.Encoding.UTF8.GetByteCount(manifestJson) > RunSharingProtocol.MaximumRequestBytes)
            throw new ShareProtocolException(SharingError.TooLarge, "This run is too large to share.");
    }

    internal static HttpContent BoundedJson<T>(T value, int limit = RunSharingProtocol.MaximumRequestBytes)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, WireJson);
        if (bytes.Length > limit)
            throw new ShareProtocolException(SharingError.TooLarge, "This run is too large to share.");
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return content;
    }

    private async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(RunSharingProtocol.RequestTimeout);
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
    }

    internal static async Task<T> Read<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(RunSharingProtocol.RequestTimeout);
        var bytes = await ReadBounded(response.Content, RunSharingProtocol.MaximumResponseBytes, deadline.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => SharingError.Unauthorized,
                HttpStatusCode.Forbidden => SharingError.Forbidden,
                HttpStatusCode.RequestEntityTooLarge => SharingError.TooLarge,
                HttpStatusCode.NotFound => SharingError.NotFound,
                HttpStatusCode.TooManyRequests => SharingError.Overloaded,
                HttpStatusCode.UpgradeRequired => SharingError.UnsupportedClient,
                _ => SharingError.Malformed,
            };
            SharingProblem? problem = null;
            try { problem = JsonSerializer.Deserialize<SharingProblem>(bytes, WireJson); }
            catch (JsonException) { }
            var message = problem?.Message ?? System.Text.Encoding.UTF8.GetString(bytes.AsSpan(0, Math.Min(bytes.Length, 1024)));
            var retry = response.Headers.RetryAfter?.Delta;
            if (retry is { } delay) retry = TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 60));
            var reportedError = problem is not null && Enum.IsDefined(problem.Error) ? problem.Error : error;
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.UpgradeRequired)
                reportedError = error;
            throw new ShareProtocolException(reportedError,
                string.IsNullOrWhiteSpace(message) ? $"Sharing isn't responding right now (error {(int)response.StatusCode}). Try again later." : message, retry);
        }
        try
        {
            return JsonSerializer.Deserialize<T>(bytes, WireJson)
                ?? throw new ShareProtocolException(SharingError.Malformed, "Sharing didn't respond. Try again later.");
        }
        catch (JsonException)
        {
            throw new ShareProtocolException(SharingError.Malformed, "Sharing sent back something Runmobile couldn't read. Update Runmobile or try again later.");
        }
    }

    internal static async Task<byte[]> ReadBounded(HttpContent content, int limit, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentEncoding.Count != 0)
            throw new ShareProtocolException(SharingError.Malformed, "Sharing sent back something Runmobile couldn't read. Try again later.");
        if (content.Headers.ContentLength > limit)
            throw new ShareProtocolException(SharingError.TooLarge, "Sharing sent back more data than expected. Try again later.");
        using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, limit + 1 - (int)result.Length)), cancellationToken).ConfigureAwait(false);
            if (count == 0) return result.ToArray();
            result.Write(buffer, 0, count);
            if (result.Length > limit)
                throw new ShareProtocolException(SharingError.TooLarge, "Sharing sent back more data than expected. Try again later.");
        }
    }

    public sealed record AdmissionRequest(
        [property: JsonRequired] string ManifestJson,
        [property: JsonRequired] ShareSubmission Submission,
        [property: JsonRequired] RunBranch Branch,
        [property: JsonRequired] long PolicyGeneration);
}

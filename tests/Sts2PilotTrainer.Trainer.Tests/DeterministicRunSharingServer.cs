using System.Net;
using System.Net.Http.Json;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Trainer.Tests;

internal sealed class DeterministicRunSharingServer(
    Func<ReplayManifest, bool> publicationGate,
    Func<ReplayManifest, LibraryRun> describe,
    Func<DateTimeOffset>? clock = null,
    Func<ReplayManifest, bool>? featured = null) : HttpMessageHandler
{
    private readonly Dictionary<string, SharedRun> shared = new(StringComparer.Ordinal);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var path = request.RequestUri?.AbsolutePath.Trim('/') ?? string.Empty;
            if (request.Method == HttpMethod.Post && path == "runs")
            {
                var body = await request.Content!.ReadFromJsonAsync<HttpRunSharingApi.ShareRequest>(
                    cancellationToken: cancellationToken)
                    ?? throw new ShareValidationException("No submission was sent.");
                return Json(Submit(body.ManifestJson, body.Submission));
            }

            if (request.Method == HttpMethod.Get && path == "runs") return Json(Index());
            if (request.Method == HttpMethod.Get && path.StartsWith("runs/", StringComparison.Ordinal))
            {
                var found = Find(Uri.UnescapeDataString(path[5..]));
                return found is null ? new HttpResponseMessage(HttpStatusCode.NotFound) : Json(found);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
        catch (ShareValidationException ex)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(ex.Message),
            };
        }
    }

    private SharedRun Submit(string manifestJson, ShareSubmission submission)
    {
        submission.Validate();
        ReplayManifest manifest;
        try
        {
            manifest = ManifestJson.Deserialize(manifestJson);
        }
        catch (Exception ex)
        {
            throw new ShareValidationException($"The run is not a valid replay manifest: {ex.Message}");
        }

        if (!publicationGate(manifest))
            throw new ShareValidationException("Local validation did not pass, so the run was not sent.");

        var canonical = ManifestJson.Serialize(manifest);
        var hash = SharedRunIdentity.For(canonical, submission);
        if (shared.TryGetValue(hash, out var existing)) return existing;

        var run = describe(manifest) with { Creator = submission.DisplayName };
        var result = new SharedRun(
            hash, SharedRunIdentity.CodeFor(hash), canonical, submission, run,
            (clock ?? (() => DateTimeOffset.UtcNow))(), featured?.Invoke(manifest) == true);
        shared.Add(hash, result);
        return result;
    }

    private IReadOnlyList<SharedRunSummary> Index() =>
    [
        .. shared.Values.Where(run => run.Featured).Select(run => run.Summary),
        .. shared.Values.Where(run => !run.Featured)
            .OrderByDescending(run => run.SubmittedAt)
            .ThenBy(run => run.ShareId, StringComparer.Ordinal)
            .Select(run => run.Summary),
    ];

    private SharedRun? Find(string code) => shared.Values.SingleOrDefault(run =>
        string.Equals(run.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

    private static HttpResponseMessage Json<T>(T value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
}

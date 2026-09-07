using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

public sealed record ShareSubmission(
    string Name,
    string Description,
    string DisplayName,
    bool Cc0Consent)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 40)
            throw new ShareValidationException("Name is required and may contain at most 40 characters.");
        if (Description.Length > 200)
            throw new ShareValidationException("Description may contain at most 200 characters.");
        if (string.IsNullOrWhiteSpace(DisplayName))
            throw new ShareValidationException("Display name is required for submission.");
        if (!Cc0Consent)
            throw new ShareValidationException("CC0 consent is required before submission.");
    }
}

public sealed record SharedRunSummary(
    string ShareId,
    string Code,
    ShareSubmission Submission,
    LibraryRun Run,
    DateTimeOffset SubmittedAt,
    bool Featured = false);

public sealed record SharedRun(
    string ShareId,
    string Code,
    string ManifestJson,
    ShareSubmission Submission,
    LibraryRun Run,
    DateTimeOffset SubmittedAt,
    bool Featured = false)
{
    public SharedRunSummary Summary =>
        new(ShareId, Code, Submission, Run, SubmittedAt, Featured);
}

public sealed class ShareValidationException(string message) : Exception(message);

public interface IRunSharingApi
{
    Task<SharedRun> SubmitAsync(
        string manifestJson, ShareSubmission submission, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SharedRunSummary>> IndexAsync(CancellationToken cancellationToken = default);
    Task<SharedRun?> FindAsync(string code, CancellationToken cancellationToken = default);
}

public sealed class HttpRunSharingApi(HttpClient client) : IRunSharingApi
{
    public async Task<SharedRun> SubmitAsync(
        string manifestJson, ShareSubmission submission, CancellationToken cancellationToken = default)
    {
        submission.Validate();
        using var response = await client.PostAsJsonAsync(
            "runs", new ShareRequest(manifestJson, submission), cancellationToken).ConfigureAwait(false);
        return await Read<SharedRun>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SharedRunSummary>> IndexAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync("runs", cancellationToken).ConfigureAwait(false);
        return await Read<List<SharedRunSummary>>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SharedRun?> FindAsync(
        string code, CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync(
            $"runs/{Uri.EscapeDataString(code.Trim())}", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        return await Read<SharedRun>(response, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T> Read<T>(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new ShareValidationException(string.IsNullOrWhiteSpace(detail)
                ? $"The sharing service returned {(int)response.StatusCode}."
                : detail);
        }

        return await response.Content.ReadFromJsonAsync<T>(
            cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new ShareValidationException("The sharing service returned no result.");
    }

    public sealed record ShareRequest(string ManifestJson, ShareSubmission Submission);
}

/// <summary>A deterministic HTTP endpoint used to exercise the same transport as production.</summary>
public sealed class DeterministicRunSharingServer(
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
                    cancellationToken: cancellationToken) ?? throw new ShareValidationException("No submission was sent.");
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
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(ex.Message) };
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
        var identityInput = Encoding.UTF8.GetBytes(canonical + "\n" + submission.Name + "\n" +
            submission.Description + "\n" + submission.DisplayName);
        var hash = Convert.ToHexString(SHA256.HashData(identityInput)).ToLowerInvariant();
        if (shared.TryGetValue(hash, out var existing)) return existing;

        var run = describe(manifest) with { Creator = submission.DisplayName };
        var result = new SharedRun(
            hash, hash[..12].ToUpperInvariant(), canonical, submission, run,
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

public sealed record ShareRunForm(
    string IdentitySeal,
    string IntegritySeal,
    int NameLimit,
    int DescriptionLimit,
    string Privacy,
    string Consent,
    string LocalValidation)
{
    public static ShareRunForm For(ReplayManifest run) => new(
        $"{run.RunId} · {run.Environment.BuildVersion.Value}",
        run.Source.Native is { IsContinuous: true, StatesSomethingOtherThanComplete: false }
            ? "Complete recording · publication gate required"
            : "Recording is not eligible to share",
        40,
        200,
        LibraryCopy.SharePrivacy,
        LibraryCopy.ShareConsent,
        LibraryCopy.ShareLocalValidation);
}

using System.Net;
using System.Security.Cryptography;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

public sealed record ShareSubmission(
    string Name,
    string Description,
    string DisplayName,
    bool Cc0Consent)
{
    public const int NameCharacterLimit = 40;
    public const int DescriptionCharacterLimit = 200;
    public const int DisplayNameCharacterLimit = 80;

    public void Validate()
    {
        if (Description is null || DisplayName is null)
            throw new ShareValidationException("Submission text is required.");
        if (string.IsNullOrWhiteSpace(Name) ||
            Name.EnumerateRunes().Count() > NameCharacterLimit)
            throw new ShareValidationException("Name is required and may contain at most 40 characters.");
        if (Description.EnumerateRunes().Count() > DescriptionCharacterLimit)
            throw new ShareValidationException("Description may contain at most 200 characters.");
        if (string.IsNullOrWhiteSpace(DisplayName) ||
            DisplayName.EnumerateRunes().Count() > DisplayNameCharacterLimit)
            throw new ShareValidationException("Display name is required and may contain at most 80 characters.");
        if (!Cc0Consent)
            throw new ShareValidationException("CC0 consent is required before submission.");
    }
}

public sealed record SharedRunSummary(
    string ShareId,
    string Code,
    ShareSubmission Submission,
    LibraryRun Run,
    EnvironmentIdentity Environment,
    string SourceKind,
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
    public SharedRunSummary Summary
    {
        get
        {
            var manifest = Sts2PilotTrainer.Replay.ManifestJson.Deserialize(ManifestJson);
            return new SharedRunSummary(
                ShareId, Code, Submission, Run, manifest.Environment, manifest.Source.Kind,
                SubmittedAt, Featured);
        }
    }
}

public class ShareValidationException(string message) : Exception(message);

public static class SharedRunIdentity
{
    public static string For(string manifestJson, ShareSubmission submission)
    {
        var canonical = Sts2PilotTrainer.Replay.ManifestJson.Serialize(
            Sts2PilotTrainer.Replay.ManifestJson.Deserialize(manifestJson));
        var input = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            new IdentityPayload(canonical, submission));
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }

    public static string CodeFor(string shareId)
    {
        if (shareId is null || shareId.Length != 64 || shareId.Any(character => !Uri.IsHexDigit(character)))
            throw new ShareValidationException("The sharing identity is not a SHA-256 digest.");
        return shareId[..12].ToUpperInvariant();
    }

    public static void RequireMatch(
        SharedRun shared, string manifestJson, ShareSubmission submission)
    {
        if (shared.ManifestJson is null || shared.Submission is null || shared.Run is null)
            throw new ShareProtocolException(SharingError.Malformed, "The sharing service returned an incomplete run.");
        shared.Submission.Validate();
        var expectedId = For(manifestJson, submission);
        var responseId = For(shared.ManifestJson, shared.Submission);
        if (!string.Equals(shared.ShareId, expectedId, StringComparison.Ordinal) ||
            !string.Equals(responseId, expectedId, StringComparison.Ordinal) ||
            !string.Equals(shared.Code, CodeFor(expectedId), StringComparison.Ordinal))
        {
            throw new ShareValidationException(
                "The sharing service returned a different run than the one submitted.");
        }
    }

    private sealed record IdentityPayload(string ManifestJson, ShareSubmission Submission);
}

public interface IRunSharingApi
{
    Task<SharedRun> SubmitAsync(
        string manifestJson, ShareSubmission submission, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SharedRunSummary>> IndexAsync(CancellationToken cancellationToken = default);
    Task<SharedRun?> FindAsync(string code, CancellationToken cancellationToken = default);
    Task<SharingProtocol> NegotiateAsync(CancellationToken cancellationToken = default);
    Task<SharingPolicy> PolicyAsync(CancellationToken cancellationToken = default);
    Task<SubmissionStatus> AdmitAsync(
        string manifestJson, ShareSubmission submission, RunBranch branch, long policyGeneration,
        CancellationToken cancellationToken = default);
    Task<SubmissionStatus> StatusAsync(
        SubmissionReceipt receipt, CancellationToken cancellationToken = default);
    Task<SubmissionStatus> StatusForIdentityAsync(
        string shareId, CancellationToken cancellationToken = default);
    Task<SharedRunPage> PageAsync(
        string? cursor = null, string? etag = null, CancellationToken cancellationToken = default);
    Task<SubmissionStatus> SubmitForPublicationAsync(
        string manifestJson, ShareSubmission submission, RunBranch branch,
        Func<CancellationToken, Task<bool>> localPublicationGate,
        Func<DateTimeOffset> clock, CancellationToken cancellationToken = default);
}

public sealed partial class HttpRunSharingApi(HttpClient client) : IRunSharingApi
{
    public async Task<SharedRun> SubmitAsync(
        string manifestJson, ShareSubmission submission, CancellationToken cancellationToken = default)
    {
        submission.Validate();
        RequirePayloadSize(manifestJson);
        using var request = new HttpRequestMessage(HttpMethod.Post, "runs")
        {
            Content = BoundedJson(new ShareRequest(manifestJson, submission)),
        };
        using var response = await Send(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.Accepted)
            throw new ShareProtocolException(SharingError.UnsupportedClient,
                "This client cannot track a processing submission. Update Runmobile before submitting again.");
        var shared = await Read<SharedRun>(response, cancellationToken).ConfigureAwait(false);
        SharedRunIdentity.RequireMatch(shared, manifestJson, submission);
        return shared;
    }

    public async Task<IReadOnlyList<SharedRunSummary>> IndexAsync(
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "runs");
        using var response = await Send(request, cancellationToken).ConfigureAwait(false);
        return await Read<List<SharedRunSummary>>(response, cancellationToken).ConfigureAwait(false);
    }

    public async Task<SharedRun?> FindAsync(
        string code, CancellationToken cancellationToken = default)
    {
        var wanted = code.Trim().ToUpperInvariant();
        if (wanted.Length != 12 || wanted.Any(character => !Uri.IsHexDigit(character)))
            throw new ShareProtocolException(SharingError.Malformed, "A run code must contain twelve hexadecimal characters.");
        using var request = Versioned(HttpMethod.Get, $"runs/{wanted}");
        using var response = await Send(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        var shared = await Read<SharedRun>(response, cancellationToken).ConfigureAwait(false);
        SharedRunIdentity.RequireMatch(shared, shared.ManifestJson, shared.Submission);
        if (shared.Code != wanted)
            throw new ShareProtocolException(SharingError.Malformed, "The sharing service returned a different code.");
        return shared;
    }

    public sealed record ShareRequest(string ManifestJson, ShareSubmission Submission);
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
        ShareSubmission.NameCharacterLimit,
        ShareSubmission.DescriptionCharacterLimit,
        LibraryCopy.SharePrivacy,
        LibraryCopy.ShareConsent,
        LibraryCopy.ShareLocalValidation);
}

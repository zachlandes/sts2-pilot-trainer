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
    public const int NameCharacterLimit = 40;
    public const int DescriptionCharacterLimit = 200;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) ||
            Name.EnumerateRunes().Count() > NameCharacterLimit)
            throw new ShareValidationException("Name is required and may contain at most 40 characters.");
        if (Description.EnumerateRunes().Count() > DescriptionCharacterLimit)
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

public sealed class ShareValidationException(string message) : Exception(message);

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
        if (shareId.Length != 64 || shareId.Any(character => !Uri.IsHexDigit(character)))
            throw new ShareValidationException("The sharing identity is not a SHA-256 digest.");
        return shareId[..12].ToUpperInvariant();
    }

    public static void RequireMatch(
        SharedRun shared, string manifestJson, ShareSubmission submission)
    {
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
}

public sealed class HttpRunSharingApi(HttpClient client) : IRunSharingApi
{
    public async Task<SharedRun> SubmitAsync(
        string manifestJson, ShareSubmission submission, CancellationToken cancellationToken = default)
    {
        submission.Validate();
        using var response = await client.PostAsJsonAsync(
            "runs", new ShareRequest(manifestJson, submission), cancellationToken).ConfigureAwait(false);
        var shared = await Read<SharedRun>(response, cancellationToken).ConfigureAwait(false);
        SharedRunIdentity.RequireMatch(shared, manifestJson, submission);
        return shared;
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
        IntegritySealFor(run),
        ShareSubmission.NameCharacterLimit,
        ShareSubmission.DescriptionCharacterLimit,
        LibraryCopy.SharePrivacy,
        LibraryCopy.ShareConsent,
        LibraryCopy.ShareLocalValidation);

    /// <summary>
    /// What the form says about whether this recording may be shared at all.
    ///
    /// A broken watch is named rather than folded into the general refusal, because it
    /// is the one a player reaches by doing something ordinary - quitting and continuing
    /// from a save behind what had been recorded - and the run went on being recorded
    /// while it happened, so this is where they find out what it cost. The recording is
    /// still theirs; it is the sharing that is gone.
    ///
    /// It says the watch has a hole in it and not what made the hole. A manifest states
    /// <c>continuity</c> and no cause, and more than one thing puts a recording there,
    /// so a line naming the reload would be a claim this form has not read.
    /// </summary>
    private static string IntegritySealFor(ReplayManifest run) => run.Source.Native switch
    {
        { IsContinuous: true, StatesSomethingOtherThanComplete: false } =>
            "Complete recording · publication gate required",
        { IsContinuous: false } =>
            "Not shareable · the recorder could not account for this run from its start",
        _ => "Recording is not eligible to share",
    };
}

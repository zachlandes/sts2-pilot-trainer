using System.Text.Json;
using System.Text.Json.Serialization;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

public static class RunSharingProtocol
{
    public const int Version = 2;
    public const string VersionHeader = "Runmobile-Sharing-Version";
    public const int MaximumRequestBytes = 4 * 1024 * 1024;
    public const int MaximumResponseBytes = 4 * 1024 * 1024;
    public const int MaximumPageSize = 50;
    public const int MaximumCursorCharacters = 256;
    public const int MaximumEntityTagCharacters = 128;
    public const int MaximumAttempts = 3;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan MaximumProcessingTime = TimeSpan.FromHours(1);

    public static JsonSerializerOptions CreateJsonOptions() => new(JsonSerializerDefaults.Web)
    {
        MaxDepth = 64,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters =
        {
            new JsonStringEnumConverter<RunBranch>(allowIntegerValues: false),
            new JsonStringEnumConverter<PublicationState>(allowIntegerValues: false),
            new JsonStringEnumConverter<SharingError>(allowIntegerValues: false),
        },
    };
}

public enum RunBranch { Public, Beta }

public enum PublicationState { Processing, Published, Refused, Failed, Expired }

public enum SharingError
{
    UnsupportedClient, Unauthorized, Forbidden, Malformed, TooLarge, AdmissionClosed,
    StalePolicy, NotCurrentBuild, NotFound, Overloaded, CodeCollision,
}

public sealed class ShareProtocolException(
    SharingError error, string message, TimeSpan? retryAfter = null) : ShareValidationException(message)
{
    public SharingError Error { get; } = error;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed record SharingProtocol(int MinimumVersion, int MaximumVersion, int MaximumRequestBytes);
public sealed record SharingProblem(SharingError Error, string Message);

// Service policy is not a claim about whether the installed recorder can record
public sealed record BranchReadiness(
    RunBranch Branch, long Generation, string UpstreamBuildId,
    DateTimeOffset ObservedAt, DateTimeOffset RefreshExpiresAt,
    LocalBuild? Engine, bool ValidatorReady, bool AdmissionOpen)
{
    public bool IsOpenAt(DateTimeOffset now) =>
        Generation > 0 && !string.IsNullOrWhiteSpace(UpstreamBuildId) &&
        ObservedAt <= now && now < RefreshExpiresAt && Engine is not null &&
        ValidatorReady && AdmissionOpen;

    public void RequireAdmission(EnvironmentIdentity recording, long generation, DateTimeOffset now)
    {
        if (!IsOpenAt(now))
            throw new ShareProtocolException(SharingError.AdmissionClosed, SharingPublication.NotSubmitted);
        if (generation != Generation)
            throw new ShareProtocolException(SharingError.StalePolicy, SharingPublication.NotSubmitted);
        if (!EnvironmentPreflight.Build(recording, Engine!).All(field => field.Matches))
            throw new ShareProtocolException(SharingError.NotCurrentBuild,
                "This run was recorded on an older build of this game branch, so it wasn't shared.");
    }

    public BranchReadiness BeginUpdate(string upstreamBuildId, DateTimeOffset observedAt,
        DateTimeOffset refreshExpiresAt) => this with
        {
            Generation = checked(Generation + 1),
            UpstreamBuildId = upstreamBuildId,
            ObservedAt = observedAt,
            RefreshExpiresAt = refreshExpiresAt,
            Engine = null,
            ValidatorReady = false,
            AdmissionOpen = false,
        };
}

public sealed record SharingPolicy(IReadOnlyList<BranchReadiness> Branches)
{
    public BranchReadiness For(RunBranch branch)
    {
        var matching = Branches.Where(row => row.Branch == branch).ToArray();
        return matching.Length == 1 ? matching[0]
            : throw new ShareProtocolException(SharingError.Malformed,
                "Sharing isn't set up for this game branch right now. Try again later.");
    }
}

// Receipt identifiers are private status coordinates, never public run codes
public sealed record SubmissionReceipt(
    [property: JsonRequired] string ReceiptId,
    [property: JsonRequired] string ShareId,
    [property: JsonRequired] RunBranch Branch,
    [property: JsonRequired] long PolicyGeneration,
    [property: JsonRequired] LocalBuild Engine,
    [property: JsonRequired] DateTimeOffset AcceptedAt,
    [property: JsonRequired] DateTimeOffset ExpiresAt);

public sealed record SubmissionStatus(
    [property: JsonRequired] SubmissionReceipt Receipt,
    [property: JsonRequired] PublicationState State,
    [property: JsonRequired] int AttemptGeneration,
    SharedRun? PublishedRun = null, string? Reason = null)
{
    public void Validate()
    {
        if (Receipt is null || string.IsNullOrWhiteSpace(Receipt.ReceiptId) ||
            Receipt.ReceiptId.Length > 128 || Receipt.PolicyGeneration <= 0 || Receipt.Engine is null ||
            Receipt.ExpiresAt <= Receipt.AcceptedAt ||
            Receipt.ExpiresAt - Receipt.AcceptedAt > RunSharingProtocol.MaximumProcessingTime ||
            AttemptGeneration is < 1 or > RunSharingProtocol.MaximumAttempts || !Enum.IsDefined(State) ||
            !Enum.IsDefined(Receipt.Branch) ||
            ((State == PublicationState.Published) != (PublishedRun is not null)) ||
            (State is PublicationState.Refused or PublicationState.Failed or PublicationState.Expired &&
             string.IsNullOrWhiteSpace(Reason)))
            throw new ShareProtocolException(SharingError.Malformed, "Sharing sent back an unexpected status. Try again later.");
        _ = SharedRunIdentity.CodeFor(Receipt.ShareId);
        if (PublishedRun is { } shared)
        {
            SharedRunIdentity.RequireMatch(shared, shared.ManifestJson, shared.Submission);
            var recording = ManifestJson.Deserialize(shared.ManifestJson);
            if (shared.ShareId != Receipt.ShareId ||
                !EnvironmentPreflight.Build(recording.Environment, Receipt.Engine).All(field => field.Matches))
                throw new ShareProtocolException(SharingError.Malformed, "Something went wrong while publishing this run. Try sharing it again.");
        }
    }
}

// The caller commits attempt state and catalogue visibility in one transaction
public sealed class PublicationAttempt
{
    public SubmissionStatus Status { get; private set; }

    public PublicationAttempt(SubmissionReceipt receipt)
    {
        Status = new SubmissionStatus(receipt, PublicationState.Processing, 1);
        Status.Validate();
    }

    public bool Retry(int expectedAttempt, DateTimeOffset now)
    {
        if (!CanChange(expectedAttempt, now)) return false;
        Status = Status.AttemptGeneration < RunSharingProtocol.MaximumAttempts
            ? Status with { AttemptGeneration = Status.AttemptGeneration + 1 }
            : Status with { State = PublicationState.Failed, Reason = "Couldn't finish checking this run. Try again later." };
        return true;
    }

    public bool Complete(int expectedAttempt, PublicationState outcome,
        SharedRun? publishedRun, string? reason, DateTimeOffset now)
    {
        if (!CanChange(expectedAttempt, now)) return false;
        if (outcome == PublicationState.Processing)
            throw new ArgumentException("Completion must be terminal.", nameof(outcome));
        var next = Status with { State = outcome, PublishedRun = publishedRun, Reason = reason };
        next.Validate();
        Status = next;
        return true;
    }

    public void Expire(DateTimeOffset now)
    {
        if (Status.State == PublicationState.Processing && now >= Status.Receipt.ExpiresAt)
            Status = Status with { State = PublicationState.Expired, Reason = "Checking this run took too long, so it wasn't published." };
    }

    private bool CanChange(int expectedAttempt, DateTimeOffset now)
    {
        Expire(now);
        return Status.State == PublicationState.Processing && Status.AttemptGeneration == expectedAttempt;
    }
}

public sealed record SharedRunPage(
    IReadOnlyList<SharedRunSummary> Runs, string? NextCursor, string ETag, bool NotModified = false);

public sealed record SharingPublication(string Message, string? PublicCode, bool Published)
{
    public const string NotSubmitted =
        "Sharing is paused while Runmobile catches up with the latest game update. This run wasn't shared.";
}

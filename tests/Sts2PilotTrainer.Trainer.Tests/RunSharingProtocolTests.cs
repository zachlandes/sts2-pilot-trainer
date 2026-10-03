using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Trainer.Tests;

public sealed class RunSharingProtocolTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-03T12:00:00Z");
    private static readonly ShareSubmission Submission = new("Run", "A close finish", "Ada", true);
    private static string Manifest => ManifestJson.Serialize(RunSharingTests.Fixture());
    private static LocalBuild Build => new(RunSharingTests.Fixture().Environment.BuildVersion.Value,
        RunSharingTests.Fixture().Environment.BuildDateUtc.Value, RunSharingTests.Fixture().Environment.ContentHash.Value);

    [Fact]
    public async Task ProcessingIsAPrivateReceiptAndNeverACodeOrBrowserEntry()
    {
        using var fixture = new Contract();
        var accepted = await fixture.Admit();
        Assert.Equal(PublicationState.Processing, accepted.State);
        Assert.Equal(1, accepted.AttemptGeneration);
        Assert.Null(accepted.PublishedRun);
        var view = RunBrowser.Publication(accepted);
        Assert.False(view.Published);
        Assert.Null(view.PublicCode);
        Assert.Equal("Processing. Not published yet.", view.Message);
        Assert.Empty((await fixture.Api.PageAsync()).Runs);
        Assert.Null(await fixture.Api.FindAsync(SharedRunIdentity.CodeFor(accepted.Receipt.ShareId)));
        Assert.Equal(accepted, await fixture.Api.StatusAsync(accepted.Receipt));
        Assert.True(fixture.Server.Complete(accepted.Receipt.ShareId, 1));
        var published = await fixture.Api.StatusAsync(accepted.Receipt);
        Assert.Equal(PublicationState.Published, published.State);
        Assert.True(RunBrowser.Publication(published).Published);
        Assert.Equal(published.PublishedRun!.Code, RunBrowser.Publication(published).PublicCode);
        Assert.Single((await fixture.Api.PageAsync()).Runs);
        Assert.Equal(published.PublishedRun.ShareId,
            (await fixture.Api.FindAsync($" {published.PublishedRun.Code.ToLowerInvariant()} "))!.ShareId);
    }

    [Fact]
    public async Task PrivateStatusRequiresAuthenticationAndTheAcceptingPrincipal()
    {
        using var fixture = new Contract();
        var accepted = await fixture.Admit();
        fixture.Client.DefaultRequestHeaders.Authorization = null;
        await Error(SharingError.Unauthorized, () => fixture.Api.StatusAsync(accepted.Receipt));
        await Error(SharingError.Unauthorized, () => fixture.Admit());
        fixture.Client.DefaultRequestHeaders.Authorization = new("Bearer", "expired");
        await Error(SharingError.Unauthorized, () => fixture.Admit());
        fixture.Client.DefaultRequestHeaders.Authorization = new("Bearer", "bob");
        await Error(SharingError.NotFound, () => fixture.Api.StatusAsync(accepted.Receipt));
        await Error(SharingError.Forbidden, () => fixture.Admit());
        Assert.Equal(1, fixture.Server.AcceptedCount);
        fixture.Server.Complete(accepted.Receipt.ShareId, 1);
        Assert.NotNull(await fixture.Api.FindAsync(SharedRunIdentity.CodeFor(accepted.Receipt.ShareId)));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 3)]
    [InlineData(3, 2)]
    public async Task IncompatibleNegotiationSendsNoRecording(int minimum, int maximum)
    {
        using var fixture = new Contract();
        fixture.Server.Protocol = new(minimum, maximum, RunSharingProtocol.MaximumRequestBytes);
        await Error(SharingError.UnsupportedClient, () => fixture.Admit());
        Assert.Equal(0, fixture.Server.AdmissionRequests);
        Assert.Equal(0, fixture.Server.AcceptedCount);
    }

    [Fact]
    public async Task LegacyClientsAreRefusedBeforeAcceptanceAndCannotDeserializeProcessingAsShared()
    {
        using var fixture = new Contract();
        await Error(SharingError.UnsupportedClient, () => fixture.Api.SubmitAsync(Manifest, Submission));
        Assert.Equal(0, fixture.Server.AcceptedCount);
        using var client = new HttpClient(new ResponseHandler(_ => new(HttpStatusCode.Accepted)
        {
            Content = JsonContent.Create(new { state = "Processing", receiptId = "private" }),
        }))
        { BaseAddress = new("http://run-sharing.test/") };
        await Error(SharingError.UnsupportedClient, () => new HttpRunSharingApi(client).SubmitAsync(Manifest, Submission));
    }

    [Fact]
    public async Task CanonicalIdentityRatherThanWireBytesOrCallerTokenDeduplicatesRetries()
    {
        using var fixture = new Contract();
        var first = await fixture.Admit();
        var pretty = System.Text.Json.JsonSerializer.Serialize(
            System.Text.Json.JsonDocument.Parse(Manifest).RootElement,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
        Assert.NotEqual(Manifest, pretty);
        Assert.Equal(first.Receipt, (await fixture.Api.AdmitAsync(pretty, Submission, RunBranch.Public, 1)).Receipt);
        using var request = new HttpRequestMessage(HttpMethod.Post, "runs")
        {
            Content = JsonContent.Create(new HttpRunSharingApi.AdmissionRequest(Manifest, Submission, RunBranch.Public, 1), options: RunSharingProtocol.CreateJsonOptions()),
        };
        request.Headers.Add(RunSharingProtocol.VersionHeader, "2");
        request.Headers.Add("Idempotency-Key", "untrusted-and-different");
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, fixture.Server.AcceptedCount);
        fixture.Server.Complete(first.Receipt.ShareId, 1);
        Assert.Equal(PublicationState.Published, (await fixture.Admit()).State);
        var second = await fixture.Admit(Submission with { Description = "Changed" });
        Assert.NotEqual(first.Receipt.ShareId, second.Receipt.ShareId);
        Assert.Equal(2, fixture.Server.AcceptedCount);
    }

    [Fact]
    public void IdentityGoldenVectorFreezesTheExistingCanonicalizationOwner()
    {
        Assert.Equal("b69613a140960dfe9b402132913c34e17f5605b7e0d8a551689463819f94010c",
            SharedRunIdentity.For(Manifest, Submission));
        Assert.Equal("B69613A14096", SharedRunIdentity.CodeFor(SharedRunIdentity.For(Manifest, Submission)));
        var unicode = SharedRunIdentity.For(Manifest, Submission with { Name = "Run 🚀", DisplayName = "Zoë" });
        Assert.Equal("21263223d72c668ae03499b892021041dde1316167267098d0b658776e6d5b8a", unicode);
        var previousFormat = System.Text.Json.Nodes.JsonNode.Parse(Manifest)!.AsObject();
        previousFormat["manifest_version"] = 7;
        Assert.Equal("b69613a140960dfe9b402132913c34e17f5605b7e0d8a551689463819f94010c",
            SharedRunIdentity.For(previousFormat.ToJsonString(), Submission));
    }

    [Fact]
    public async Task LostAcceptanceResponseIsOutcomeUnknownAndRecoverableByPrivateIdentityDuringUpdate()
    {
        using var fixture = new Contract();
        fixture.Server.DropNextAcceptanceResponse = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Admit());
        Assert.Equal(1, fixture.Server.AcceptedCount);
        var hash = SharedRunIdentity.For(Manifest, Submission);
        fixture.Server.BeginUpdate(RunBranch.Public, "next");
        var recovered = await fixture.Api.StatusForIdentityAsync(hash);
        Assert.Equal(PublicationState.Processing, recovered.State);
        Assert.Equal(1, recovered.Receipt.PolicyGeneration);
        await Error(SharingError.AdmissionClosed, () => fixture.Admit());
        Assert.Equal(1, fixture.Server.AcceptedCount);
        fixture.Client.DefaultRequestHeaders.Authorization = new("Bearer", "bob");
        await Error(SharingError.NotFound, () => fixture.Api.StatusForIdentityAsync(hash));
    }

    [Fact]
    public async Task ConcurrentIdenticalRequestsReserveExactlyOneSubmission()
    {
        using var fixture = new Contract();
        var requests = Enumerable.Range(0, 8).Select(_ => Task.Run(() => fixture.Admit())).ToArray();
        var statuses = await Task.WhenAll(requests);
        Assert.Single(statuses.Select(status => status.Receipt.ReceiptId).Distinct());
        Assert.Equal(1, fixture.Server.AcceptedCount);
    }

    [Fact]
    public async Task LostAdmissionRaceCreatesNoReceiptReservationOrJobAndDoesNotAffectBeta()
    {
        using var fixture = new Contract();
        fixture.Server.BeforeAdmission = () => fixture.Server.BeginUpdate(RunBranch.Public, "public-next");
        var error = await Error(SharingError.AdmissionClosed, () => fixture.Admit());
        Assert.Equal("Sharing is paused while Runmobile catches up with the latest game update. This run wasn't shared.", error.Message);
        Assert.Equal(0, fixture.Server.AcceptedCount);
        Assert.Empty((await fixture.Api.PageAsync()).Runs);
        fixture.Server.BeforeAdmission = null;
        var beta = await fixture.Api.AdmitAsync(Manifest, Submission, RunBranch.Beta, 1);
        Assert.Equal(RunBranch.Beta, beta.Receipt.Branch);
        Assert.Equal(1, fixture.Server.AcceptedCount);
    }

    [Fact]
    public async Task BranchChecksSurroundTheLocalPublicationGateAndTheFinalServiceCheckResolvesTheRace()
    {
        using var fixture = new Contract();
        var calls = 0;
        Task<bool> Gate(CancellationToken _) { calls++; return Task.FromResult(true); }
        fixture.Server.BeginUpdate(RunBranch.Public, "next");
        await Error(SharingError.AdmissionClosed, () => fixture.Api.SubmitForPublicationAsync(
            Manifest, Submission, RunBranch.Public, Gate, () => fixture.Now));
        Assert.Equal(0, calls);
        Assert.Equal(0, fixture.Server.AdmissionRequests);
        fixture.OpenPublic(2);
        await Error(SharingError.AdmissionClosed, () => fixture.Api.SubmitForPublicationAsync(
            Manifest, Submission, RunBranch.Public, _ =>
            {
                fixture.Server.BeginUpdate(RunBranch.Public, "next-again");
                return Task.FromResult(true);
            }, () => fixture.Now));
        Assert.Equal(0, fixture.Server.AdmissionRequests);
        fixture.OpenPublic(4);
        fixture.Server.BeforeAdmission = () => fixture.Server.BeginUpdate(RunBranch.Public, "final-race");
        await Error(SharingError.AdmissionClosed, () => fixture.Api.SubmitForPublicationAsync(
            Manifest, Submission, RunBranch.Public, Gate, () => fixture.Now));
        Assert.Equal(0, fixture.Server.AcceptedCount);
        fixture.Server.BeforeAdmission = null;
        fixture.OpenPublic(6);
        await Assert.ThrowsAsync<ShareValidationException>(() => fixture.Api.SubmitForPublicationAsync(
            Manifest, Submission, RunBranch.Public, _ => Task.FromResult(false), () => fixture.Now));
        Assert.Equal(1, fixture.Server.AdmissionRequests);
    }

    [Fact]
    public async Task OldOrUnknownBuildStaleGenerationExpiredPolicyAndUnreadyValidatorFailClosed()
    {
        using var fixture = new Contract();
        fixture.OpenPublic(2);
        await Error(SharingError.StalePolicy, () => fixture.Admit());
        fixture.OpenPublic(1, Build with { ContentHash = "other-content" });
        await Error(SharingError.NotCurrentBuild, () => fixture.Admit());
        fixture.OpenPublic(1, Build with { BuildDateUtc = "different-date" });
        await Error(SharingError.NotCurrentBuild, () => fixture.Admit());
        fixture.OpenPublic(1, Build with { BuildVersion = "other-version" });
        await Error(SharingError.NotCurrentBuild, () => fixture.Admit());
        fixture.OpenPublic(1);
        fixture.Now = Start.AddMinutes(6);
        await Error(SharingError.AdmissionClosed, () => fixture.Admit());
        fixture.Now = Start;
        fixture.Server.Policy = new(fixture.Server.Policy!.Branches.Select(row => row with { ValidatorReady = false }).ToArray());
        await Error(SharingError.AdmissionClosed, () => fixture.Admit());
        fixture.Server.Policy = new(fixture.Server.Policy.Branches.Select(row => row with { Engine = null, ValidatorReady = true }).ToArray());
        await Error(SharingError.AdmissionClosed, () => fixture.Admit());
        Assert.Equal(0, fixture.Server.AcceptedCount);
    }

    [Fact]
    public async Task AcceptedGenerationDrainsDuringUpdateButStaleAttemptsAndDoubleCompletionCannotPublish()
    {
        using var fixture = new Contract();
        var accepted = await fixture.Admit();
        fixture.Server.BeginUpdate(RunBranch.Public, "next");
        Assert.True(fixture.Server.Retry(accepted.Receipt.ShareId, 1));
        Assert.False(fixture.Server.Complete(accepted.Receipt.ShareId, 1));
        Assert.False(fixture.Server.Retry(accepted.Receipt.ShareId, 1));
        Assert.Empty((await fixture.Api.PageAsync()).Runs);
        Assert.True(fixture.Server.Complete(accepted.Receipt.ShareId, 2));
        Assert.False(fixture.Server.Complete(accepted.Receipt.ShareId, 2));
        var status = await fixture.Api.StatusAsync(accepted.Receipt);
        Assert.Equal(1, status.Receipt.PolicyGeneration);
        Assert.Equal(Build, status.Receipt.Engine);
        Assert.Single((await fixture.Api.PageAsync()).Runs);
    }

    [Theory]
    [InlineData(PublicationState.Refused)]
    [InlineData(PublicationState.Failed)]
    [InlineData(PublicationState.Expired)]
    public async Task TerminalNonpublicationRemainsPrivateAndHasAnExplanation(PublicationState outcome)
    {
        using var fixture = new Contract();
        var accepted = await fixture.Admit();
        Assert.True(fixture.Server.Complete(accepted.Receipt.ShareId, 1, outcome));
        var status = await fixture.Api.StatusAsync(accepted.Receipt);
        Assert.Equal(outcome, status.State);
        Assert.NotEmpty(status.Reason!);
        Assert.False(RunBrowser.Publication(status).Published);
        Assert.Null(RunBrowser.Publication(status).PublicCode);
        Assert.Empty((await fixture.Api.PageAsync()).Runs);
        Assert.Null(await fixture.Api.FindAsync(SharedRunIdentity.CodeFor(accepted.Receipt.ShareId)));
        Assert.False(fixture.Server.Complete(accepted.Receipt.ShareId, 1));
    }

    [Fact]
    public async Task RemoteGateFailureCannotBeOverriddenByLocalSuccess()
    {
        using var fixture = new Contract(gate: false);
        var accepted = await fixture.Api.SubmitForPublicationAsync(Manifest, Submission, RunBranch.Public,
            _ => Task.FromResult(true), () => fixture.Now);
        fixture.Server.Complete(accepted.Receipt.ShareId, 1);
        Assert.Equal(PublicationState.Refused, (await fixture.Api.StatusAsync(accepted.Receipt)).State);
        Assert.Empty((await fixture.Api.PageAsync()).Runs);
    }

    [Fact]
    public async Task AttemptsAndDrainTimeAreBoundedAndCannotBeRestartedByIdenticalSubmission()
    {
        using var fixture = new Contract();
        var accepted = await fixture.Admit();
        Assert.True(fixture.Server.Retry(accepted.Receipt.ShareId, 1));
        Assert.True(fixture.Server.Retry(accepted.Receipt.ShareId, 2));
        Assert.True(fixture.Server.Retry(accepted.Receipt.ShareId, 3));
        Assert.Equal(PublicationState.Failed, (await fixture.Admit()).State);
        Assert.False(fixture.Server.Retry(accepted.Receipt.ShareId, 3));
        var expires = await fixture.Admit(Submission with { Name = "Expires" });
        fixture.Now = expires.Receipt.ExpiresAt;
        Assert.Equal(PublicationState.Expired, (await fixture.Api.StatusAsync(expires.Receipt)).State);
        Assert.False(fixture.Server.Complete(expires.Receipt.ShareId, 1));
        Assert.Empty((await fixture.Api.PageAsync()).Runs);
    }

    [Fact]
    public async Task ShortCodeCollisionNeverReplacesTheFirstReservation()
    {
        using var fixture = new Contract();
        var first = await fixture.Admit();
        fixture.Server.ReserveCode = _ => SharedRunIdentity.CodeFor(first.Receipt.ShareId);
        await Error(SharingError.CodeCollision, () => fixture.Admit(Submission with { Name = "Other" }));
        Assert.Equal(1, fixture.Server.AcceptedCount);
        fixture.Server.Complete(first.Receipt.ShareId, 1);
        Assert.Equal(first.Receipt.ShareId,
            (await fixture.Api.FindAsync(SharedRunIdentity.CodeFor(first.Receipt.ShareId)))!.ShareId);
    }

    [Fact]
    public async Task BoundedPagesConditionalRetrievalAndRemovalShareOneVisibilityRule()
    {
        using var fixture = new Contract();
        fixture.Server.PageSize = 2;
        for (var i = 0; i < 3; i++)
        {
            var accepted = await fixture.Admit(Submission with { Name = $"Run {i}" });
            fixture.Server.Complete(accepted.Receipt.ShareId, 1);
        }
        var first = await fixture.Api.PageAsync();
        Assert.Equal(2, first.Runs.Count);
        Assert.NotNull(first.NextCursor);
        var unchanged = await fixture.Api.PageAsync(etag: first.ETag);
        Assert.True(unchanged.NotModified);
        Assert.Empty(unchanged.Runs);
        var second = await fixture.Api.PageAsync(first.NextCursor);
        Assert.Single(second.Runs);
        Assert.Null(second.NextCursor);
        Assert.Equal(3, first.Runs.Concat(second.Runs).Select(row => row.ShareId).Distinct().Count());
        Assert.False((await fixture.Api.PageAsync(first.NextCursor, first.ETag)).NotModified);
        fixture.Server.Remove(first.Runs[0].ShareId);
        Assert.Null(await fixture.Api.FindAsync(first.Runs[0].Code));
        Assert.False((await fixture.Api.PageAsync(etag: first.ETag)).NotModified);
        await Error(SharingError.StalePolicy, () => fixture.Api.PageAsync(first.NextCursor));
    }

    [Fact]
    public async Task PatchDayPreservesCodeAndContentButNotAPassingCompatibilityProjection()
    {
        using var fixture = new Contract();
        var accepted = await fixture.Admit();
        fixture.Server.Complete(accepted.Receipt.ShareId, 1);
        var code = SharedRunIdentity.CodeFor(accepted.Receipt.ShareId);
        var original = (await fixture.Api.FindAsync(code))!;
        fixture.Server.BeginUpdate(RunBranch.Public, "next");
        fixture.Server.BrowseBuild = Build with { BuildVersion = "next", ContentHash = "new-content" };
        var found = (await fixture.Api.FindAsync(code))!;
        Assert.Equal(original.ManifestJson, found.ManifestJson);
        Assert.Equal(original.ShareId, found.ShareId);
        Assert.Equal(RunVerdict.Absent, found.Run.Verdict);
        var row = found.Run with { ShareId = found.ShareId, ShareCode = found.Code };
        Assert.Empty(RunBrowser.For(LibraryTab.Community, [row], "next").Groups);
        Assert.False(RunBrowser.For(LibraryTab.Community, [row], "next", selectedEntryId: row.EntryId).Pane!.OpenEnabled);
        Assert.Equal(LookupOutcome.IncompatibleBuild, RunBrowser.Lookup(row.EntryId, [row], "next").Outcome);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"manifestJson\":\"{}\",\"submission\":null,\"branch\":\"Public\",\"policyGeneration\":1}")]
    [InlineData("{\"unexpected\":\"sensitive-value\"}")]
    [InlineData("{\"manifestJson\":\"{}\",\"submission\":{\"name\":\"Run\",\"description\":\"\",\"displayName\":\"Ada\",\"cc0Consent\":true},\"branch\":\"Public\",\"policyGeneration\":1}")]
    public async Task MalformedRequestsAreRefusedWithoutAcceptedWork(string body)
    {
        using var fixture = new Contract();
        using var request = new HttpRequestMessage(HttpMethod.Post, "runs") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add(RunSharingProtocol.VersionHeader, "2");
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, fixture.Server.AcceptedCount);
    }

    [Fact]
    public async Task NegotiatedRequestAndDisplayNameBoundsRefuseBeforeUpload()
    {
        using var fixture = new Contract();
        fixture.Server.Protocol = new(2, 2, 100);
        await Error(SharingError.TooLarge, () => fixture.Admit());
        Assert.Equal(0, fixture.Server.AdmissionRequests);
        await Assert.ThrowsAsync<ShareValidationException>(() => fixture.Admit(Submission with
        {
            DisplayName = new string('a', ShareSubmission.DisplayNameCharacterLimit + 1),
        }));
        Assert.Equal(0, fixture.Server.AcceptedCount);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"receipt\":null,\"state\":\"Processing\",\"attemptGeneration\":1}")]
    [InlineData("{\"state\":\"FutureState\"}")]
    public async Task MalformedOrFutureResponsesNeverBecomePublished(string json)
    {
        using var fixture = new Contract();
        using var client = new HttpClient(new ResponseHandler(request => request.RequestUri!.AbsolutePath == "/sharing-protocol"
            ? new(HttpStatusCode.OK) { Content = JsonContent.Create(new SharingProtocol(2, 2, RunSharingProtocol.MaximumRequestBytes)) }
            : new(HttpStatusCode.Accepted) { Content = new StringContent(json, Encoding.UTF8, "application/json") }))
        { BaseAddress = new("http://run-sharing.test/") };
        await Error(SharingError.Malformed, () => new HttpRunSharingApi(client).AdmitAsync(Manifest, Submission, RunBranch.Public, 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResponseLengthBoundsApplyEvenWithoutContentLength(bool chunked)
    {
        using var client = new HttpClient(new ResponseHandler(_ => new(HttpStatusCode.OK)
        {
            Content = chunked ? new StreamContent(new NonSeekableBytes(RunSharingProtocol.MaximumResponseBytes + 1))
                : new ByteArrayContent(new byte[RunSharingProtocol.MaximumResponseBytes + 1]),
        }))
        { BaseAddress = new("http://run-sharing.test/") };
        await Error(SharingError.TooLarge, () => new HttpRunSharingApi(client).NegotiateAsync());
    }

    [Fact]
    public async Task OversizedCataloguePageIsRefusedEvenWithinTheByteLimit()
    {
        using var fixture = new Contract();
        var accepted = await fixture.Admit();
        fixture.Server.Complete(accepted.Receipt.ShareId, 1);
        var row = Assert.Single((await fixture.Api.PageAsync()).Runs);
        using var client = new HttpClient(new ResponseHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new SharedRunPage(
                    Enumerable.Repeat(row, RunSharingProtocol.MaximumPageSize + 1).ToArray(), null, "\"tag\""),
                    options: RunSharingProtocol.CreateJsonOptions()),
            };
            response.Headers.ETag = new("\"tag\"");
            return response;
        }))
        { BaseAddress = new("http://run-sharing.test/") };
        await Error(SharingError.Malformed, () => new HttpRunSharingApi(client).PageAsync());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("\"0\"")]
    [InlineData("\"Unknown\"")]
    public void WireEnumsRefuseIntegersAndUnknownValues(string value)
    {
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<PublicationState>(
            value, RunSharingProtocol.CreateJsonOptions()));
    }

    [Fact]
    public async Task ResponseStateCannotSmuggleAPublicRunOrChangeAReceipt()
    {
        using var fixture = new Contract();
        var accepted = await fixture.Admit();
        fixture.Server.Complete(accepted.Receipt.ShareId, 1);
        var published = await fixture.Api.StatusAsync(accepted.Receipt);
        Assert.Throws<ShareProtocolException>(() => (published with { State = PublicationState.Processing }).Validate());
        Assert.Throws<ShareProtocolException>(() => (published with { PublishedRun = null }).Validate());
        Assert.Throws<ShareProtocolException>(() => (accepted with { State = (PublicationState)999 }).Validate());
        Assert.Throws<ShareProtocolException>(() => (published with
        {
            Receipt = published.Receipt with { Engine = Build with { ContentHash = "wrong" } },
        }).Validate());
        using var client = new HttpClient(new ResponseHandler(_ => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(accepted with { Receipt = accepted.Receipt with { ReceiptId = "other" } }, options: RunSharingProtocol.CreateJsonOptions()),
        }))
        { BaseAddress = new("http://run-sharing.test/") };
        await Error(SharingError.Malformed, () => new HttpRunSharingApi(client).StatusAsync(accepted.Receipt));
    }

    [Fact]
    public async Task ResponseDepthAndEncodingAreBoundedBeforeDecoding()
    {
        using var deepClient = new HttpClient(new ResponseHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StringContent(new string('[', 65) + "0" + new string(']', 65), Encoding.UTF8, "application/json"),
        }))
        { BaseAddress = new("http://run-sharing.test/") };
        await Error(SharingError.Malformed, () => new HttpRunSharingApi(deepClient).NegotiateAsync());
        using var encodedClient = new HttpClient(new ResponseHandler(_ =>
        {
            var content = new ByteArrayContent([]);
            content.Headers.ContentEncoding.Add("gzip");
            return new(HttpStatusCode.OK) { Content = content };
        }))
        { BaseAddress = new("http://run-sharing.test/") };
        await Error(SharingError.Malformed, () => new HttpRunSharingApi(encodedClient).NegotiateAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerAlsoEnforcesRequestBoundsWithoutTrustingTheClient(bool chunked)
    {
        using var fixture = new Contract();
        fixture.Server.Protocol = new(2, 2, 10);
        var stream = new NonSeekableBytes(100);
        using var request = new HttpRequestMessage(HttpMethod.Post, "runs")
        {
            Content = chunked ? new StreamContent(stream) : new StringContent(new string('x', 11)),
        };
        request.Headers.Add(RunSharingProtocol.VersionHeader, "2");
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, fixture.Server.AcceptedCount);
        if (chunked) Assert.Equal(11, stream.BytesRead);
    }

    [Fact]
    public async Task CancellationAlsoBoundsAStalledResponseBody()
    {
        using var client = new HttpClient(new ResponseHandler(_ => new(HttpStatusCode.OK)
        {
            Content = new StreamContent(new NonSeekableBytes(1, wait: true)),
        }))
        { BaseAddress = new("http://run-sharing.test/") };
        using var cancellation = new CancellationTokenSource();
        var pending = new HttpRunSharingApi(client).NegotiateAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task MissingClientCancellationStillHasAProtocolDeadline()
    {
        using var client = new HttpClient(new WaitingHandler(() => { }))
        {
            BaseAddress = new("http://run-sharing.test/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new HttpRunSharingApi(client).NegotiateAsync());
    }

    [Fact]
    public async Task CancellationBoundsAnUnfinishedRequestAndNoAutomaticRetryOccurs()
    {
        var calls = 0;
        using var client = new HttpClient(new WaitingHandler(() => calls++)) { BaseAddress = new("http://run-sharing.test/") };
        using var cancellation = new CancellationTokenSource();
        var pending = new HttpRunSharingApi(client).NegotiateAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task OverloadRetryAdviceIsBoundedAndNeverAutomaticallyResubmits()
    {
        var calls = 0;
        using var client = new HttpClient(new ResponseHandler(_ =>
        {
            calls++;
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = JsonContent.Create(new SharingProblem(SharingError.Overloaded, "Try later. This run was not submitted."), options: RunSharingProtocol.CreateJsonOptions()) };
            response.Headers.RetryAfter = new(TimeSpan.FromDays(1));
            return response;
        }))
        { BaseAddress = new("http://run-sharing.test/") };
        var error = await Error(SharingError.Overloaded, () => new HttpRunSharingApi(client).NegotiateAsync());
        Assert.Equal(TimeSpan.FromSeconds(60), error.RetryAfter);
        Assert.Equal(1, calls);
    }

    private static async Task<ShareProtocolException> Error(SharingError expected, Func<Task> action)
    {
        var error = await Assert.ThrowsAsync<ShareProtocolException>(action);
        Assert.Equal(expected, error.Error);
        return error;
    }

    private sealed class Contract : IDisposable
    {
        internal DateTimeOffset Now { get; set; } = Start;
        internal DeterministicRunSharingServer Server { get; }
        internal HttpClient Client { get; }
        internal HttpRunSharingApi Api { get; }
        internal Contract(bool gate = true)
        {
            Server = new(_ => gate, manifest => LibraryRun.From(manifest, RunOrigin.Recent, RunVerdict.Passed), () => Now, asynchronous: true)
            {
                Policy = new SharingPolicy([
                    new(RunBranch.Public, 1, "public-current", Start, Start.AddMinutes(5), Build, true, true),
                    new(RunBranch.Beta, 1, "beta-current", Start, Start.AddMinutes(5), Build, true, true),
                ]),
            };
            Client = new HttpClient(Server) { BaseAddress = new("http://run-sharing.test/") };
            Client.DefaultRequestHeaders.Authorization = new("Bearer", "alice");
            Api = new(Client);
        }
        internal Task<SubmissionStatus> Admit(ShareSubmission? submission = null) =>
            Api.AdmitAsync(Manifest, submission ?? Submission, RunBranch.Public, 1);
        internal void OpenPublic(long generation, LocalBuild? build = null) =>
            Server.Policy = new(Server.Policy!.Branches.Select(row => row.Branch == RunBranch.Public
                ? row with { Generation = generation, Engine = build ?? Build, AdmissionOpen = true, ValidatorReady = true }
                : row).ToArray());
        public void Dispose() => Client.Dispose();
    }

    private sealed class ResponseHandler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }

    private sealed class WaitingHandler(Action started) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            started();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new(HttpStatusCode.OK);
        }
    }

    private sealed class NonSeekableBytes(int size, bool wait = false) : Stream
    {
        private readonly int length = size;
        private int remaining = size;
        internal int BytesRead => length - remaining;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (wait) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var read = Math.Min(buffer.Length, remaining);
            buffer.Span[..read].Fill((byte)' ');
            remaining -= read;
            return read;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = Math.Min(count, remaining);
            Array.Fill(buffer, (byte)' ', offset, read);
            remaining -= read;
            return read;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

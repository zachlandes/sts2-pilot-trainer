using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Trainer.Tests;

public sealed class RunSharingTests
{
    [Fact]
    public async Task ALocallyValidatedRunTravelsFromSubmissionThroughIndexAndExactCodeLookup()
    {
        var manifest = Fixture();
        var service = Service(
            _ => true,
            () => DateTimeOffset.Parse("2026-09-07T12:00:00Z"));
        var request = new ShareSubmission("A good run", "A close finish", "Ada", true);

        var shared = await service.SubmitAsync(ManifestJson.Serialize(manifest), request);

        var indexEntry = Assert.Single(await service.IndexAsync());
        Assert.Equal(shared.ShareId, indexEntry.ShareId);
        Assert.Equal(
            manifest.Environment.BuildVersion.Value,
            indexEntry.Environment.BuildVersion.Value);
        Assert.Equal(
            manifest.Environment.ContentHash.Value,
            indexEntry.Environment.ContentHash.Value);
        Assert.Equal(manifest.Environment.Mods.Value.Name, indexEntry.Environment.Mods.Value.Name);
        Assert.Equal(manifest.Source.Kind, indexEntry.SourceKind);
        Assert.DoesNotContain(
            "manifestJson",
            System.Text.Json.JsonSerializer.Serialize(indexEntry),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(shared.ShareId, (await service.FindAsync(shared.Code.ToLowerInvariant()))?.ShareId);
        Assert.Equal(
            shared.ShareId,
            (await service.SubmitAsync(ManifestJson.Serialize(manifest), request)).ShareId);
        Assert.Equal(12, shared.Code.Length);
    }

    [Fact]
    public async Task SubmissionRefusesAResponseForAnotherManifestOrSubmission()
    {
        var manifestJson = ManifestJson.Serialize(Fixture());
        var submitted = new ShareSubmission("Submitted", "", "Ada", true);
        var returnedSubmission = submitted with { Name = "Somebody else's" };
        var returnedId = SharedRunIdentity.For(manifestJson, returnedSubmission);
        var returned = new SharedRun(
            returnedId,
            SharedRunIdentity.CodeFor(returnedId),
            manifestJson,
            returnedSubmission,
            LibraryRun.From(Fixture(), RunOrigin.Recent, RunVerdict.Passed),
            DateTimeOffset.Parse("2026-09-07T12:00:00Z"));
        var service = new HttpRunSharingApi(new HttpClient(new FixedResponse(returned))
        {
            BaseAddress = new Uri("http://run-sharing.test/"),
        });

        var error = await Assert.ThrowsAsync<ShareValidationException>(() =>
            service.SubmitAsync(manifestJson, submitted));

        Assert.Contains("different run", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SharingIdentityBindsEverySubmissionFieldWithoutDelimiterCollisions()
    {
        var manifest = ManifestJson.Serialize(Fixture());
        var first = new ShareSubmission("one\ntwo", "three", "Ada", true);
        var sameJoinedText = new ShareSubmission("one", "two\nthree", "Ada", true);
        var consentChanged = first with { Cc0Consent = false };

        Assert.NotEqual(
            SharedRunIdentity.For(manifest, first),
            SharedRunIdentity.For(manifest, sameJoinedText));
        Assert.NotEqual(
            SharedRunIdentity.For(manifest, first),
            SharedRunIdentity.For(manifest, consentChanged));
    }

    [Fact]
    public async Task NothingIsSentUnlessTheExistingLocalPublicationGatePasses()
    {
        var service = Service(_ => false);

        var error = await Assert.ThrowsAsync<ShareValidationException>(() => service.SubmitAsync(
            ManifestJson.Serialize(Fixture()),
            new ShareSubmission("Run", "", "Ada", true)));

        Assert.Contains("Local validation did not pass", error.Message);
        Assert.Empty(await service.IndexAsync());
    }

    [Theory]
    [InlineData("", "", "Ada", true)]
    [InlineData("12345678901234567890123456789012345678901", "", "Ada", true)]
    [InlineData("Run", "123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901", "Ada", true)]
    [InlineData("Run", "", "", true)]
    [InlineData("Run", "", "Ada", false)]
    public async Task SubmissionEnforcesTheDesignedConsentAndLengthLimits(
        string name, string description, string displayName, bool consent)
    {
        var service = Service(_ => true);

        await Assert.ThrowsAsync<ShareValidationException>(() => service.SubmitAsync(
            ManifestJson.Serialize(Fixture()),
            new ShareSubmission(name, description, displayName, consent)));
        Assert.Empty(await service.IndexAsync());
    }

    [Fact]
    public void SubmissionLimitsCountUnicodeScalarValues()
    {
        var scalar = "\U0001F680";
        var nameAtLimit = string.Concat(Enumerable.Repeat(
            scalar, ShareSubmission.NameCharacterLimit));
        var descriptionAtLimit = string.Concat(Enumerable.Repeat(
            scalar, ShareSubmission.DescriptionCharacterLimit));

        new ShareSubmission(nameAtLimit, descriptionAtLimit, "Ada", true).Validate();

        Assert.Throws<ShareValidationException>(() => new ShareSubmission(
            nameAtLimit + scalar, "", "Ada", true).Validate());
        Assert.Throws<ShareValidationException>(() => new ShareSubmission(
            "Run", descriptionAtLimit + scalar, "Ada", true).Validate());
        var form = ShareRunForm.For(Fixture());
        Assert.Equal(ShareSubmission.NameCharacterLimit, form.NameLimit);
        Assert.Equal(ShareSubmission.DescriptionCharacterLimit, form.DescriptionLimit);
    }

    [Fact]
    public async Task SubmissionAcceptsAnUncappedNonemptyDisplayName()
    {
        var displayName = new string('A', 200);
        var service = Service(_ => true);

        var shared = await service.SubmitAsync(
            ManifestJson.Serialize(Fixture()),
            new ShareSubmission("Run", "", displayName, true));

        Assert.Equal(displayName, shared.Submission.DisplayName);
    }

    [Fact]
    public void AnIncompatibleExactCodeClearsTheDefaultFilterAndSelectsTheRun()
    {
        var incompatible = new LibraryRun(
            "run", RunOrigin.Recent, "Ada", "Ironclad", 0, "v0.110.0", [1], "won",
            false, RunVerdict.Absent, [], DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
            ShareId: "share-id", ShareCode: "ABC123");
        var shared = new SharedRun("id", "ABC123", "{}",
            new ShareSubmission("Run", "", "Ada", true), incompatible,
            DateTimeOffset.Parse("2026-09-07T00:00:00Z"));

        var initial = RunBrowser.For(LibraryTab.Community, [shared.Run], "v0.111.0");
        var found = RunBrowser.For(
            LibraryTab.Community, [shared.Run], "v0.111.0", selectedEntryId: "share-id");

        Assert.True(initial.CompatibleOnly);
        Assert.Empty(initial.Groups);
        Assert.False(found.CompatibleOnly);
        Assert.Equal("share-id", found.SelectedEntryId);
        Assert.Equal(incompatible, Assert.Single(Assert.Single(found.Groups).Runs));
        var lookup = RunBrowser.Lookup("share-id", [incompatible], "v0.111.0");
        Assert.Equal(LookupOutcome.IncompatibleBuild, lookup.Outcome);
        Assert.Contains("v0.110.0", lookup.Body);
        Assert.Contains("v0.111.0", lookup.Body);
    }

    [Fact]
    public void SharedEntriesWithOneManifestRunIdSelectBySharingIdentity()
    {
        var first = new LibraryRun(
            "same-run", RunOrigin.Recent, "Ada", "Ironclad", 0, "v0.110.0", [1], "won",
            false, RunVerdict.Absent, [], DateTimeOffset.Parse("2026-09-01T00:00:00Z"),
            ShareId: "first-share", ShareCode: "FIRST");
        var second = first with
        {
            Creator = "Grace",
            ShareId = "second-share",
            ShareCode = "SECOND",
        };

        var browser = RunBrowser.For(
            LibraryTab.Community, [first, second], "v0.111.0",
            compatibleOnly: true, selectedEntryId: second.EntryId);

        Assert.False(browser.CompatibleOnly);
        Assert.Equal(2, Assert.Single(browser.Groups).Runs.Count);
        Assert.Equal(second.EntryId, browser.SelectedEntryId);
        Assert.Equal(second, Assert.Single(
            Assert.Single(browser.Groups).Runs,
            run => run.EntryId == browser.SelectedEntryId));
    }

    [Fact]
    public void ASharedMineRunAppearsInBothTabs()
    {
        var mine = new LibraryRun(
            "run", RunOrigin.Mine, "Ada", "Ironclad", 0, "current", [1], "won",
            false, RunVerdict.Passed, [], DateTimeOffset.Parse("2026-09-01T00:00:00Z"));
        var shared = mine with { Origin = RunOrigin.Recent };

        var others = RunBrowser.For(LibraryTab.Community, [mine, shared], "current");
        var myRuns = RunBrowser.For(LibraryTab.MyRuns, [mine, shared], "current");

        Assert.Equal("run", Assert.Single(Assert.Single(others.Groups).Runs).RunId);
        Assert.Equal("run", Assert.Single(Assert.Single(myRuns.Groups).Runs).RunId);
    }

    [Theory]
    [InlineData(RunVerdict.Failed, false)]
    [InlineData(RunVerdict.Unjudged, false)]
    [InlineData(RunVerdict.Absent, true)]
    public void CompatibilityFilterOnlyRevealsOtherBuildRuns(
        RunVerdict verdict, bool visible)
    {
        var run = new LibraryRun(
            "run", RunOrigin.Recent, "Ada", "Ironclad", 0, "other", [1], "won",
            false, verdict, [], DateTimeOffset.Parse("2026-09-01T00:00:00Z"));

        var browser = RunBrowser.For(
            LibraryTab.Community, [run], "current", compatibleOnly: false);

        Assert.Equal(visible, browser.Groups.SelectMany(group => group.Runs).Any());
    }

    [Fact]
    public void CompatibilityFilterNeverRevealsMultiplayerRuns()
    {
        var run = new LibraryRun(
            "run", RunOrigin.Recent, "Ada", "Ironclad", 0, "other", [1], "won",
            true, RunVerdict.Absent, [], DateTimeOffset.Parse("2026-09-01T00:00:00Z"));

        var browser = RunBrowser.For(
            LibraryTab.Community, [run], "current", compatibleOnly: false);

        Assert.Empty(browser.Groups);
    }

    [Fact]
    public void SubmissionFormCarriesTheRunSealsLimitsAndDisclosure()
    {
        var manifest = Fixture();

        var form = ShareRunForm.For(manifest);

        Assert.Contains(manifest.RunId, form.IdentitySeal);
        Assert.Contains(manifest.Environment.BuildVersion.Value, form.IdentitySeal);
        Assert.Equal(40, form.NameLimit);
        Assert.Equal(200, form.DescriptionLimit);
        Assert.Contains("No other personal information", form.Privacy);
        Assert.Contains("CC0", form.Consent);
        Assert.Contains("locally", form.LocalValidation);
    }

    /// <summary>
    /// The seal on a run the recorder could not account for from its start.
    ///
    /// A reload behind what was recorded is how a player reaches this by doing something
    /// ordinary, and the recording went on being made while it happened, so the form is
    /// where they find out what it cost. It names the hole and not what made it: a
    /// manifest states <c>continuity</c> and no cause, and more than one thing puts a
    /// recording here.
    /// </summary>
    [Fact]
    public void ARecordingWithABrokenWatchIsSealedAsUnshareableAndSaysWhatIsWrong()
    {
        var manifest = WithContinuity(Fixture(), NativeSource.BrokenContinuity);

        var form = ShareRunForm.For(manifest);

        Assert.Contains("Not shareable", form.IntegritySeal, StringComparison.Ordinal);
        Assert.Contains("from its start", form.IntegritySeal, StringComparison.Ordinal);
    }

    /// <summary>And a continuous one is offered to the publication gate as before, so
    /// the new seal is a branch and not a replacement.</summary>
    [Fact]
    public void AContinuousRecordingIsStillSealedForThePublicationGate()
    {
        var form = ShareRunForm.For(WithContinuity(Fixture(), NativeSource.ContinuousContinuity));

        Assert.Contains("publication gate", form.IntegritySeal, StringComparison.Ordinal);
    }

    /// <summary>The fixture as a native recording of the given continuity. The fixture
    /// itself is reconstructed from a video and carries no native source, and it is the
    /// native source the seal reads.</summary>
    private static ReplayManifest WithContinuity(ReplayManifest run, string continuity) => run with
    {
        Source = run.Source with
        {
            Kind = "native",
            ExtractionMethod = "captured",
            Native = new NativeSource
            {
                RecorderVersion = "runmobile-recorder/0.1.0",
                WitnessedRunStart = Fact<bool>.Captured(true, FactEvidence.AtActionOrdinal(-1, 0)),
                Continuity = continuity,
                Outcome = "abandoned",
                Integrity = NativeSource.CompleteIntegrity,
            },
        },
    };

    private static IRunSharingApi Service(
        Func<ReplayManifest, bool> gate, Func<DateTimeOffset>? clock = null)
    {
        var server = new DeterministicRunSharingServer(
            gate,
            recording => LibraryRun.From(recording, RunOrigin.Recent, RunVerdict.Passed),
            clock);
        return new HttpRunSharingApi(new HttpClient(server)
        {
            BaseAddress = new Uri("http://run-sharing.test/"),
        });
    }

    private sealed class FixedResponse(SharedRun shared) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(shared),
            });
    }

    private static ReplayManifest Fixture()
    {
        var assembly = typeof(ReplayManifest).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "Sts2PilotTrainer.Replay.Fixtures.synthetic-v0111-pilot-trainer.replay.json")!;
        using var reader = new StreamReader(stream);
        return ManifestJson.Deserialize(reader.ReadToEnd());
    }
}

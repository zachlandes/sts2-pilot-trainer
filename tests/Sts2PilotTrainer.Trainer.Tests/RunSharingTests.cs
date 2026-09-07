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

        Assert.Equal(shared.ShareId, Assert.Single(await service.IndexAsync()).ShareId);
        Assert.Equal(shared.ShareId, (await service.FindAsync(shared.Code.ToLowerInvariant()))?.ShareId);
        Assert.Equal(
            shared.ShareId,
            (await service.SubmitAsync(ManifestJson.Serialize(manifest), request)).ShareId);
        Assert.Equal(12, shared.Code.Length);
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
    public void AnIncompatibleExactCodeClearsTheDefaultFilterAndSelectsTheRun()
    {
        var incompatible = new LibraryRun(
            "run", RunOrigin.Recent, "Ada", "Ironclad", 0, "v0.110.0", [1], "won",
            false, RunVerdict.Absent, [], DateTimeOffset.Parse("2026-09-01T00:00:00Z"));
        var shared = new SharedRun("id", "ABC123", "{}",
            new ShareSubmission("Run", "", "Ada", true), incompatible,
            DateTimeOffset.Parse("2026-09-07T00:00:00Z"));

        var initial = RunBrowser.For(LibraryTab.Community, [shared.Run], "v0.111.0");
        var found = RunBrowser.For(
            LibraryTab.Community, [shared.Run], "v0.111.0", selectedRunId: "run");

        Assert.True(initial.CompatibleOnly);
        Assert.Empty(initial.Groups);
        Assert.False(found.CompatibleOnly);
        Assert.Equal("run", found.SelectedRunId);
        Assert.Equal(incompatible, Assert.Single(Assert.Single(found.Groups).Runs));
        var lookup = RunBrowser.Lookup("run", [incompatible], "v0.111.0");
        Assert.Equal(LookupOutcome.IncompatibleBuild, lookup.Outcome);
        Assert.Contains("v0.110.0", lookup.Body);
        Assert.Contains("v0.111.0", lookup.Body);
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

    private static ReplayManifest Fixture()
    {
        var assembly = typeof(ReplayManifest).Assembly;
        using var stream = assembly.GetManifestResourceStream(
            "Sts2PilotTrainer.Replay.Fixtures.synthetic-v0111-pilot-trainer.replay.json")!;
        using var reader = new StreamReader(stream);
        return ManifestJson.Deserialize(reader.ReadToEnd());
    }
}

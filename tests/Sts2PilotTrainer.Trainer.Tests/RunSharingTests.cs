using System.Reflection;
using Sts2PilotTrainer.Replay;
using Sts2PilotTrainer.Trainer;

namespace Sts2PilotTrainer.Trainer.Tests;

public sealed class RunSharingTests
{
    [Fact]
    public void ALocallyValidatedRunTravelsFromSubmissionThroughIndexAndExactCodeLookup()
    {
        var manifest = Fixture();
        var service = Service(
            _ => true,
            () => DateTimeOffset.Parse("2026-09-07T12:00:00Z"));
        var request = new ShareSubmission("A good run", "A close finish", "Ada", true);

        var shared = service.Submit(ManifestJson.Serialize(manifest), request);

        Assert.Equal(shared.ShareId, Assert.Single(service.Index()).ShareId);
        Assert.Equal(shared.ShareId, service.Find(shared.Code.ToLowerInvariant())?.ShareId);
        Assert.Equal(shared.ShareId, service.Submit(ManifestJson.Serialize(manifest), request).ShareId);
        Assert.Equal(12, shared.Code.Length);
    }

    [Fact]
    public void NothingIsSentUnlessTheExistingLocalPublicationGatePasses()
    {
        var service = Service(_ => false);

        var error = Assert.Throws<ShareValidationException>(() => service.Submit(
            ManifestJson.Serialize(Fixture()),
            new ShareSubmission("Run", "", "Ada", true)));

        Assert.Contains("Local validation did not pass", error.Message);
        Assert.Empty(service.Index());
    }

    [Theory]
    [InlineData("", "", "Ada", true)]
    [InlineData("12345678901234567890123456789012345678901", "", "Ada", true)]
    [InlineData("Run", "123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901", "Ada", true)]
    [InlineData("Run", "", "", true)]
    [InlineData("Run", "", "Ada", false)]
    public void SubmissionEnforcesTheDesignedConsentAndLengthLimits(
        string name, string description, string displayName, bool consent)
    {
        var service = Service(_ => true);

        Assert.Throws<ShareValidationException>(() => service.Submit(
            ManifestJson.Serialize(Fixture()),
            new ShareSubmission(name, description, displayName, consent)));
        Assert.Empty(service.Index());
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

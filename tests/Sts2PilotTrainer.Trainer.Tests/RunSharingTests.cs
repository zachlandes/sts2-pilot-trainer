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
        var service = new LocalRunSharingService(
            _ => true,
            recording => LibraryRun.From(recording, RunOrigin.Recent, RunVerdict.Passed),
            () => DateTimeOffset.Parse("2026-09-07T12:00:00Z"));
        var request = new ShareSubmission("A good run", "A close finish", "Ada", true);

        var shared = service.Submit(ManifestJson.Serialize(manifest), request);

        Assert.Equal(shared, Assert.Single(service.Index()));
        Assert.Equal(shared, service.Find(shared.Code.ToLowerInvariant()));
        Assert.Equal(shared, service.Submit(ManifestJson.Serialize(manifest), request));
        Assert.Equal(12, shared.Code.Length);
    }

    [Fact]
    public void NothingIsSentUnlessTheExistingLocalPublicationGatePasses()
    {
        var service = new LocalRunSharingService(
            _ => false,
            recording => LibraryRun.From(recording, RunOrigin.Recent, RunVerdict.Passed));

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
        var service = new LocalRunSharingService(
            _ => true,
            recording => LibraryRun.From(recording, RunOrigin.Recent, RunVerdict.Passed));

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

        var initial = SharingBrowser.For(LibraryTab.Community, [shared], "v0.111.0");
        var found = SharingBrowser.For(LibraryTab.Community, [shared], "v0.111.0", selectedCode: "abc123");

        Assert.True(initial.CompatibleOnly);
        Assert.Empty(initial.Groups);
        Assert.False(found.CompatibleOnly);
        Assert.Equal("ABC123", found.SelectedCode);
        Assert.Equal(incompatible, Assert.Single(Assert.Single(found.Groups).Runs));
        Assert.Equal(LookupOutcome.IncompatibleBuild, found.Selected!.Outcome);
        Assert.Contains("v0.110.0", found.Selected.Body);
        Assert.Contains("v0.111.0", found.Selected.Body);
    }

    [Fact]
    public void SubmissionPopupStatesPrivacyConsentAndLocalValidation()
    {
        Assert.Contains("No other personal information", SharingCopy.Privacy);
        Assert.Contains("CC0", SharingCopy.Consent);
        Assert.Contains("locally", SharingCopy.LocalValidation);
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

using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The preflight against the real game: what it reads off this machine, and what it
/// refuses.
///
/// The rules themselves are proved without the game in
/// <c>Sts2PilotTrainer.Replay.Tests</c>. What these tests add is that the reading is
/// real - that the numbers being compared came out of the engine and the run came
/// out of <c>RunManager</c>, rather than out of the same code that judges them.
/// </summary>
public class PreflightTests
{
    // ---- unlock prerequisites, read from the engine ------------------------

    [GameFact]
    public void TheHostSuppliedCompleteUnlockStateSatisfiesTheManifest()
    {
        var result = Arbiter.Run("preflight", Arbiter.Manifest);

        Assert.True(result.Verified, result.All);
        Assert.Contains("ok   unlocks_epochs", result.Output, StringComparison.Ordinal);
        Assert.Contains("ok   acts_unlocked", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void AnEnvironmentWithNothingUnlockedIsRefusedCategoryByCategory()
    {
        // The negative control for the whole unlock gate, and the reason it is not a
        // formality: before this gate existed, a replay under an empty unlock state
        // ran happily and produced a different run that looked entirely valid.
        var result = Arbiter.Run("preflight", Arbiter.Manifest, "--progress", "none-unlocked");

        Assert.False(result.Verified, result.All);
        foreach (var category in new[]
                 {
                     "unlocks_characters", "unlocks_cards", "unlocks_card_pools",
                     "unlocks_relics", "unlocks_potions", "unlocks_epochs",
                 })
        {
            Assert.Contains($"FAIL {category}", result.Output, StringComparison.Ordinal);
        }

        Assert.Contains("locked: ACT.UNDERDOCKS", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void ThisHostsOwnProfileIsReadAndFailsTheAscensionPrerequisite()
    {
        // This host's profile lives in its sandbox and is empty, which is what makes
        // it a usable negative: the reading is genuine, and the player's own save is
        // never opened. Loaded inside the retail client the same reader sees the
        // player's progress instead.
        var result = Arbiter.Run("preflight", Arbiter.Manifest, "--progress", "local-profile");

        Assert.False(result.Verified, result.All);
        Assert.Contains("FAIL ascension_unlocked", result.Output, StringComparison.Ordinal);
        Assert.Contains("profile ceiling 0 for CHARACTER.IRONCLAD", result.Output, StringComparison.Ordinal);
        Assert.Contains("finish a run at the level below", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void NoRefusalOffersToChangeTheSaveOrTheInstall()
    {
        var result = Arbiter.Run("preflight", Arbiter.Manifest, "--progress", "none-unlocked");

        Assert.False(result.Verified);
        Assert.Contains("never writes to your save", result.Output, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "--unlock", "--grant", "edit your save", "unlock everything for you" })
        {
            Assert.DoesNotContain(forbidden, result.Output, StringComparison.OrdinalIgnoreCase);
        }
    }

    [GameFact]
    public void ReplayRefusesRatherThanRunningUnderAnIncompleteUnlockState()
    {
        // The gate has to bind the thing that actually replays, not only the command
        // that reports on it.
        var result = Arbiter.Run("replay", Arbiter.Manifest, "--progress", "none-unlocked");

        Assert.False(result.Verified, result.All);
        Assert.Contains("REFUSED", result.All, StringComparison.Ordinal);
    }

    // ---- run identity, read back out of the engine -------------------------

    [GameFact]
    public void NoActiveRunIsRefusedByDefault()
    {
        var result = Arbiter.Run("preflight-live", Arbiter.Manifest);

        Assert.False(result.Verified, result.All);
        Assert.Contains(
            "headless; user data is build/sandbox; retail profile and RunManager are not visible",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains("progress : LocalProfile", result.Output, StringComparison.Ordinal);
        Assert.Contains("FAIL run_present", result.Output, StringComparison.Ordinal);
        Assert.Contains("no run in progress", result.Output, StringComparison.Ordinal);
    }

    [GameTheory]
    [InlineData("all-unlocked")]
    [InlineData("none-unlocked")]
    public void LivePreflightRefusesASubstitutedUnlockModel(string progress)
    {
        var result = Arbiter.Run("preflight-live", Arbiter.Manifest, "--progress", progress);

        Assert.False(result.Verified, result.All);
        Assert.Contains("requires --progress local-profile", result.All, StringComparison.Ordinal);
        Assert.Contains("runtime player unlocks must be read, not replaced", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void ADemoRunStartedAtTheManifestsIdentityIsReadBackAndMatches()
    {
        var result = Arbiter.Run(
            "preflight-live", Arbiter.Manifest, "--demo-start-run", "--progress", "all-unlocked");

        Assert.True(result.Verified, result.All);
        Assert.Contains("run in progress, read from RunManager.State", result.Output, StringComparison.Ordinal);
        Assert.Contains("ok   run_seed", result.Output, StringComparison.Ordinal);
        Assert.Contains("environment and run match", result.Output, StringComparison.Ordinal);
    }

    [GameTheory]
    [InlineData("--seed", "SFXT47K77RFX", "run_seed")]
    [InlineData("--game-mode", "custom", "run_game_mode")]
    [InlineData("--ascension", "9", "run_ascension")]
    [InlineData("--character", "CHARACTER.SILENT", "run_character")]
    [InlineData("--acts", "ACT.OVERGROWTH,ACT.HIVE,ACT.GLORY", "run_acts")]
    public void ARunStartedAtADifferentIdentityIsRefusedOnThatDimension(
        string option, string value, string expectedField)
    {
        var result = Arbiter.Run(
            "preflight-live", Arbiter.Manifest, "--demo-start-run", "--progress", "all-unlocked", option, value);

        Assert.False(result.Verified, result.All);
        Assert.Contains($"FAIL {expectedField}", result.Output, StringComparison.Ordinal);
        Assert.Contains("refusing to replay", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void TheActVariantIsCaughtEvenThoughTheMapIsIdentical()
    {
        // Worth its own test because it is the substitution nothing downstream sees:
        // both index-0 acts generate the same map from the same seed, so the map
        // comparison that catches a wrong seed says nothing at all about this.
        var result = Arbiter.Run(
            "preflight-live", Arbiter.Manifest, "--demo-start-run", "--progress", "all-unlocked",
            "--acts", "ACT.OVERGROWTH,ACT.HIVE,ACT.GLORY");

        Assert.False(result.Verified);
        Assert.Contains("ACT.OVERGROWTH", result.Output, StringComparison.Ordinal);
        Assert.Contains("producing the same map", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void ManifestFieldsAreComparedAgainstTheEngineAndNotAgainstThemselves()
    {
        // A manifest that names a build this machine does not have must refuse, which
        // is what proves the build number came off the installation rather than off
        // the manifest being checked.
        var path = Path.Combine(TempDir(), "wrong-build.json");
        var manifest = ManifestJson.Load(Arbiter.Manifest);
        ManifestJson.Save(
            manifest with
            {
                Environment = manifest.Environment with
                {
                    BuildVersion = Fact<string>.Observed("v0.103.2", FactEvidence.AtVideoTime(1, "test")),
                },
            },
            path);

        var result = Arbiter.Run("preflight", path);

        Assert.False(result.Verified);
        Assert.Contains("FAIL build_version", result.Output, StringComparison.Ordinal);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}

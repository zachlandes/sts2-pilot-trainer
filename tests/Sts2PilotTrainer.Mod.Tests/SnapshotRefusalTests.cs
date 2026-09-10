using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Mod.Tests;

/// <summary>
/// What the restore path says when the arbiter left no snapshot behind.
///
/// A player pressed Continue and has been watching an indeterminate notice; what they
/// read next is the whole of what this decides. The bound that produced the first
/// case is <see cref="PackagedArbiter.SnapshotTimeout"/>, which is the restore path's
/// own and shorter than the publication gate's on purpose.
/// </summary>
public sealed class SnapshotRefusalTests
{
    private static PackagedArbiter.Result Result(bool timedOut, string output) =>
        new(timedOut ? -1 : 1, output, string.Empty, timedOut);

    [Fact]
    public void ARestoreThatOutlivedItsBoundIsRefusedInThePlayersOwnTerms()
    {
        var refusal = Refusal(timedOut: true);

        Assert.Contains("could not restore your run", refusal, StringComparison.Ordinal);
        Assert.Contains("3 minutes", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("still replaying", refusal, StringComparison.Ordinal);
    }

    /// <summary>Every other failure has the arbiter's own account, and gives it: a
    /// snapshot that did not bind is a fact somebody can act on.</summary>
    [Fact]
    public void ARefusalThatIsNotATimeoutQuotesWhatTheArbiterSaid()
    {
        var refusal = Refusal(timedOut: false);

        Assert.Contains("arrival on floor 7", refusal, StringComparison.Ordinal);
        Assert.Contains("nothing is cached", refusal, StringComparison.Ordinal);
        Assert.Contains("the engine refused this history", refusal, StringComparison.Ordinal);
        Assert.DoesNotContain("could not restore your run", refusal, StringComparison.Ordinal);
    }

    /// <summary>The two callers are bounded separately, and the one a player waits in
    /// front of is the short one. A shared number is what left the first retail press
    /// standing still for a quarter of an hour.</summary>
    [Fact]
    public void TheRestoreBoundIsTheShortOne()
    {
        Assert.True(PackagedArbiter.SnapshotTimeout < PackagedArbiter.PublicationTimeout);
        Assert.True(PackagedArbiter.SnapshotTimeout <= TimeSpan.FromMinutes(5));
    }

    private static string Refusal(bool timedOut) =>
        SnapshotStore.RefusalFor(
            Result(timedOut, timedOut ? "still replaying" : "the engine refused this history"),
            floor: 7,
            whyNot: "nothing is cached under a1b2c3");
}

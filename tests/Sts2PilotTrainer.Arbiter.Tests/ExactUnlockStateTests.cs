using System.Text.Json;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The recorded player's own unlock state, constructed and supplied to a run built
/// here.
///
/// This is the spike the exact progress model needs: not that the code runs, but that
/// a run generated against a state assembled out of three lists is the same run as one
/// generated against the state the game itself holds. It leans on the replay
/// verification rather than on a new check, because "the same run" already has an
/// owner and it is the arbiter.
///
/// The state is supplied to the run rather than compared with the person in front of
/// the game, which is what makes it symmetric: a viewer with fewer unlocks and a
/// viewer with more both get the recorded player's state, because neither one's own
/// ever enters the run. See docs/environment-identity.md.
/// </summary>
public sealed class ExactUnlockStateTests
{
    /// <summary>
    /// The whole-act fixture, whose history is long enough to have opinions: it walks
    /// several fights, a shop, a rest site, a chest and an event, all of which are
    /// generated out of the unlock state.
    /// </summary>
    private static string Fixture =>
        Path.Combine(
            Arbiter.RepoRoot, "src", "Sts2PilotTrainer.Replay", "Fixtures",
            "synthetic-v0111-whole-act.replay.json");

    [GameFact]
    public void AStateBuiltFromTheTripleReproducesTheRunTheCompleteStateProduces()
    {
        var complete = Arbiter.Run("replay", Fixture);
        Assert.True(complete.Verified, complete.All);

        var exact = Arbiter.Run("replay", WithExactUnlocks(ShippedIds(), runs: 100, name: "complete-triple"));

        Assert.True(exact.Verified, exact.All);
        Assert.Contains("Exact - the recorded player's own unlock state", exact.Output, StringComparison.Ordinal);

        // The whole claim, in two lines: the same seed under a state assembled here
        // ended in the same state, having made the same history. Read off the replay's
        // own report rather than recomputed, because what "the same run" means is the
        // arbiter's answer and not this test's.
        Assert.Equal(Line(complete.Output, "final state digest"), Line(exact.Output, "final state digest"));
        Assert.Equal(Line(complete.Output, "action history hash"), Line(exact.Output, "action history hash"));
    }

    /// <summary>
    /// And the state is really doing the work. A smaller triple generates a different
    /// run from the same seed, and the divergence is refused in words rather than
    /// carried - which is also the negative control for the whole exact arm: if the
    /// supplied state were being ignored, this would pass too.
    /// </summary>
    [GameFact]
    public void ASmallerStateGeneratesADifferentRunAndIsRefused()
    {
        var empty = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["epochs"] = [],
            ["encounters_seen"] = [],
        };

        var result = Arbiter.Run("replay", WithExactUnlocks(empty, runs: 0, name: "empty-triple"));

        Assert.False(result.Verified, result.All);
        Assert.Contains("REJECTED", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "not the one the recording describes", result.All, StringComparison.Ordinal);
    }

    /// <summary>
    /// An id this build never heard of cannot go into a state at all, so it is refused
    /// rather than dropped: a state missing one epoch generates a different run behind
    /// an identical map.
    ///
    /// Reported, not thrown. The preflight exists to tell somebody why a recording
    /// cannot be replayed here, so it has to print its FAIL row and its refusal rather
    /// than abort while reading: a reading that threw would print no rows at all and
    /// leave the message on stderr, which is what the row assertions distinguish.
    /// </summary>
    [GameFact]
    public void AnIdThisBuildDoesNotShipIsReportedAsAShortfallRatherThanDropped()
    {
        var manifest = WithExactUnlocks(WithAStranger(), runs: 100, name: "unshipped-epoch");

        var result = Arbiter.Run("preflight", manifest);

        Assert.False(result.Verified, result.All);
        Assert.StartsWith("FAIL", Row(result.Output, "unlocks_epochs"), StringComparison.Ordinal);
        Assert.Contains("does not ship 1 of the", result.Output, StringComparison.Ordinal);
        Assert.Contains("EPOCH.THIS.BUILD.NEVER.HEARD.OF", result.Output, StringComparison.Ordinal);
        Assert.Contains(
            "environment does NOT match; refusing to replay", result.Output, StringComparison.Ordinal);

        // And the acts row says it was never asked, rather than reporting a state that
        // could not be built as one with nothing locked.
        Assert.StartsWith("FAIL", Row(result.Output, "acts_unlocked"), StringComparison.Ordinal);
        Assert.Contains("not checked", Row(result.Output, "acts_unlocked"), StringComparison.Ordinal);
    }

    /// <summary>
    /// And no run is generated against a state this build cannot build: the replay
    /// refuses on the same shortfall, in its own words, before it constructs anything.
    /// </summary>
    [GameFact]
    public void ARunIsNeverConstructedAgainstAStateThisBuildCannotBuild()
    {
        var manifest = WithExactUnlocks(WithAStranger(), runs: 100, name: "unshipped-epoch-replay");

        var result = Arbiter.Run("replay", manifest);

        Assert.False(result.Verified, result.All);
        Assert.Contains("status         : REFUSED", result.Output, StringComparison.Ordinal);
        Assert.Contains("does not ship 1 of the", result.Output, StringComparison.Ordinal);
        Assert.Contains("action history hash: (none)", result.Output, StringComparison.Ordinal);
    }

    /// <summary>This build's own id lists, with one epoch it has never heard of.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> WithAStranger()
    {
        var shipped = ShippedIds();
        return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["epochs"] = [.. shipped["epochs"], "EPOCH.THIS.BUILD.NEVER.HEARD.OF"],
            ["encounters_seen"] = shipped["encounters_seen"],
        };
    }

    /// <summary>One preflight row, by the field it reports on, with the pass mark it
    /// was printed with kept at the front.</summary>
    private static string Row(string output, string field) =>
        output.Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line =>
                (line.StartsWith("ok ", StringComparison.Ordinal) ||
                 line.StartsWith("FAIL", StringComparison.Ordinal)) &&
                line.Contains(field, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"The preflight printed no '{field}' row:\n{output}");

    /// <summary>
    /// The reading names what this build ships, which is what an exact requirement is
    /// checked against. Before it did, every exact requirement refused as unchecked -
    /// the honest answer, and not one anybody could act on.
    /// </summary>
    [GameFact]
    public void ThePreflightReadingEnumeratesWhatThisBuildShips()
    {
        var shipped = ShippedIds();

        Assert.NotEmpty(shipped["epochs"]);
        Assert.NotEmpty(shipped["encounters_seen"]);
        Assert.All(shipped["epochs"], id => Assert.False(string.IsNullOrWhiteSpace(id)));
        Assert.All(shipped["encounters_seen"], id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    /// <summary>The two id lists this build ships, read out of the preflight's own
    /// artifact so the test asks the same question the preflight answers.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ShippedIds()
    {
        var path = Path.Combine(Scratch(), "preflight-reading.json");
        var result = Arbiter.Run("preflight", Fixture, "--out", path);
        Assert.True(result.Verified, result.All);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var shipped = document.RootElement
            .GetProperty("reading").GetProperty("unlocks").GetProperty("shipped_ids");

        return new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["epochs"] = Ids(shipped, "epochs"),
            ["encounters_seen"] = Ids(shipped, "encounters_seen"),
        };
    }

    private static IReadOnlyList<string> Ids(JsonElement shipped, string name) =>
        shipped.GetProperty(name).EnumerateArray().Select(id => id.GetString()!).ToList();

    /// <summary>The fixture with its unlock requirement rewritten as the state itself.
    /// Written to scratch rather than over the fixture: a manifest is somebody's
    /// evidence, and a test that edited one would be a test that changed what it was
    /// measuring.</summary>
    private static string WithExactUnlocks(
        IReadOnlyDictionary<string, IReadOnlyList<string>> inventory, int runs, string name)
    {
        var manifest = ManifestJson.Load(Fixture);
        var rewritten = manifest with
        {
            Environment = manifest.Environment with
            {
                Unlocks = Fact<UnlockRequirement>.Declared(UnlockRequirement.Exact(
                    "The state written as the three values the game's own UnlockState is constructed from, " +
                    "for the spike that a state assembled here generates the run the game's own does.",
                    new UnlockStateInventory
                    {
                        Epochs = inventory["epochs"],
                        EncountersSeen = inventory["encounters_seen"],
                        Runs = runs,
                    })),
            },
        };

        var path = Path.Combine(Scratch(), $"exact-{name}.replay.json");
        ManifestJson.Save(rewritten, path);
        return path;
    }

    private static string Scratch()
    {
        var path = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch");
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>One labelled line of a report, by the label the report prints.</summary>
    private static string Line(string output, string label) =>
        output.Split('\n').FirstOrDefault(line => line.StartsWith(label, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"The report has no '{label}' line:\n{output}");
}

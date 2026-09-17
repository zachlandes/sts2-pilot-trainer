namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// A test that asserts how an answer is handled obtains the list it answers from the
/// engine's own producer, never from a literal written in the test.
///
/// The card-reward Skip defect was covered by a test: it wrote the alternative list
/// by hand as the one alternative the handler happened to expect, so the handler was
/// held to a list the game never offers and passed for as long as the defect lived.
/// The rule is in AGENTS.md's card-prompt paragraph; this is the lint that keeps it,
/// over the test sources rather than the compiled tests, because the thing it refuses
/// is a way of writing a test and not a behaviour any test could observe. What it
/// proves is exactly that: no test source constructs an alternative or a creation
/// result as an offered list, except the sites named below with their reason.
///
/// An alternative is constructed by <c>CardRewardAlternative.Generate</c> and the
/// relics that add to it, and by nothing in this repository's tests. A creation
/// result is what the engine hands its seam; a test that builds one is allowed only
/// where the list is the engine entry point's input rather than the list a handler
/// is held to.
/// </summary>
public sealed class FixtureProvenanceTests
{
    private static readonly string TestsRoot = Path.Combine(Arbiter.RepoRoot, "tests");

    /// <summary>Sites allowed to construct a creation result, each with the reason
    /// it is the engine's input and not an offered list somebody wrote.</summary>
    private static readonly IReadOnlyDictionary<string, string> CreationResultSitesAllowed =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Sts2PilotTrainer.Mod.Tests/CardPromptOfferTests.cs"] =
                "the results are the argument handed to CardSelectCmd.FromSimpleGridForRewards, the way the " +
                "game's own callers hand it one, and the test asserts what the entry point then offers",
        };

    [Fact]
    public void NoTestConstructsACardRewardAlternative()
    {
        var offenders = Sites("new CardRewardAlternative(").ToList();

        Assert.True(
            offenders.Count == 0,
            "An alternative is produced by CardRewardAlternative.Generate and the relics that add to it, never " +
            "written in a test; obtain the list from the engine at a real card reward (CardRewardAlternativeTests " +
            "shows how):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void ACreationResultIsConstructedOnlyWhereItIsTheEnginesInput()
    {
        var offenders = Sites("new CardCreationResult(")
            .Where(site => !CreationResultSitesAllowed.ContainsKey(site.File))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A creation result is what the engine hands its seam; a test that answers a list of them obtains it " +
            "from the reward the engine put on the loot screen. A site that is the entry point's own input is " +
            "named in CreationResultSitesAllowed with its reason:\n" + string.Join("\n", offenders));
    }

    /// <summary>An allow-list entry that no site needs any more is a stale excuse.</summary>
    [Fact]
    public void EveryAllowedSiteStillConstructsOne()
    {
        var sites = Sites("new CardCreationResult(").Select(site => site.File).ToHashSet(StringComparer.Ordinal);

        Assert.All(CreationResultSitesAllowed.Keys, file => Assert.Contains(file, sites));
    }

    private static IEnumerable<(string File, string Line)> Sites(string construction) =>
        Directory.EnumerateFiles(TestsRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.EndsWith(nameof(FixtureProvenanceTests) + ".cs", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .SelectMany(path => File.ReadLines(path)
                .Select((text, index) => (Text: text, Number: index + 1))
                .Where(line => line.Text.Contains(construction, StringComparison.Ordinal))
                .Select(line => (
                    File: Path.GetRelativePath(TestsRoot, path).Replace(Path.DirectorySeparatorChar, '/'),
                    Line: $"{Path.GetRelativePath(TestsRoot, path)}:{line.Number}: {line.Text.Trim()}")));
}

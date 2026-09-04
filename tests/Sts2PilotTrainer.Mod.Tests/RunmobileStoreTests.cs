using Sts2PilotTrainer.IO;
using Sts2PilotTrainer.Mod;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The one writer, and the paths it refuses.
///
/// These run in a process with no game, against a root under the test's own
/// temporary directory: what is being tested is the rule, not where the game says
/// <c>user://</c> is. Every refusal is asserted to have happened <em>before</em> a
/// write, because a store that refused after creating the file would be a store that
/// wrote outside itself and then complained.
/// </summary>
public sealed class RunmobileStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"runmobile-store-{Guid.NewGuid():N}", "Runmobile", "steam", "76561197960287930",
        "profile1");

    public RunmobileStoreTests()
    {
        Directory.CreateDirectory(_root);
        RunmobileStore.UseRootForTesting(_root);
    }

    public void Dispose()
    {
        RunmobileStore.UseRootForTesting(null);
        var sandbox = _root[.._root.IndexOf("Runmobile", StringComparison.Ordinal)];
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
    }

    [Fact]
    public void AWriteLandsInsideTheStoreAndReadsBack()
    {
        RunmobileStore.Write("recordings/one.json", "{}");

        Assert.True(RunmobileStore.Exists("recordings/one.json"));
        Assert.Equal("{}", RunmobileStore.Read("recordings/one.json"));
        Assert.Equal(
            Path.Combine(_root, "recordings", "one.json"), RunmobileStore.PathOf("recordings/one.json"));
    }

    [Fact]
    public void AProfileSwitchChangesTheRootUsedByTheNextOperation()
    {
        var first = _root;
        var second = Path.Combine(Path.GetDirectoryName(_root)!, "profile2");
        var current = first;
        RunmobileStore.UseRootProviderForTesting(() => current);

        RunmobileStore.Write("progress.json", "first");
        current = second;
        RunmobileStore.Write("progress.json", "second");

        Assert.Equal("first", File.ReadAllText(Path.Combine(first, "progress.json")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(second, "progress.json")));
    }

    [Fact]
    public void AWriteLeavesNoTemporarySiblingBehind()
    {
        RunmobileStore.Write("progress.json", "{\"fights\":[]}");

        Assert.Equal(
            ["progress.json"],
            Directory.EnumerateFileSystemEntries(_root).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ASecondWriteReplacesTheFileRatherThanAppendingToIt()
    {
        RunmobileStore.Write("progress.json", "first");
        RunmobileStore.Write("progress.json", "second");

        Assert.Equal("second", RunmobileStore.Read("progress.json"));
    }

    [Fact]
    public void ATraversalIsRefusedBeforeAnythingIsWritten()
    {
        var escape = Path.Combine(Path.GetDirectoryName(_root)!, "stolen.json");

        Assert.Throws<PathContainmentException>(
            () => RunmobileStore.Write("../stolen.json", "no"));

        Assert.False(File.Exists(escape));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    /// <summary>
    /// The sibling whose name merely starts with the root's. A containment rule
    /// written as a string prefix accepts <c>.../Runmobile-backup</c> as being inside
    /// <c>.../Runmobile</c>; this one compares path components.
    /// </summary>
    [Fact]
    public void ASiblingWhoseNameStartsWithTheRootsIsRefused()
    {
        var sibling = _root + "-backup";

        Assert.Throws<PathContainmentException>(
            () => RunmobileStore.Write($"../{Path.GetFileName(sibling)}/notes.json", "no"));

        Assert.False(Directory.Exists(sibling));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_root));
    }

    [Fact]
    public void AnAbsolutePathIsRefused()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"runmobile-outside-{Guid.NewGuid():N}.json");

        Assert.Throws<PathContainmentException>(() => RunmobileStore.Write(outside, "no"));

        Assert.False(File.Exists(outside));
    }

    [Fact]
    public void TheStoresOwnRootIsNotAnEntryInIt()
    {
        Assert.Throws<PathContainmentException>(() => RunmobileStore.Write(".", "no"));
        Assert.Throws<ArgumentException>(() => RunmobileStore.Write("  ", "no"));
    }

    /// <summary>
    /// A path inside a game installation is refused even where containment would
    /// allow it - which is the case a store rooted in one would produce. The game is
    /// a read-only input, and that is the rule that says so.
    /// </summary>
    [Theory]
    [InlineData("Steam")]
    [InlineData("steamapps")]
    [InlineData("Slay the Spire 2")]
    public void APathWithAProtectedInstallComponentIsRefused(string component)
    {
        var protectedRoot = Path.Combine(
            Path.GetTempPath(), $"runmobile-protected-{Guid.NewGuid():N}", component, "Runmobile");
        Directory.CreateDirectory(protectedRoot);

        try
        {
            Assert.Throws<ProtectedInstallPathException>(
                () => RunmobileStore.UseRootForTesting(protectedRoot));
            Assert.Empty(Directory.EnumerateFileSystemEntries(protectedRoot));
        }
        finally
        {
            RunmobileStore.UseRootForTesting(_root);
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(protectedRoot)!)!, recursive: true);
        }
    }

    /// <summary>
    /// The store lives under the profile scope the game resolved for itself, taken
    /// whole from the game's own answer. Two accounts on one machine, and two
    /// profiles on one account, therefore do not share a library, and a modded
    /// session lands beside the game's own modded profile because the game's answer
    /// says <c>modded</c> and this does not re-derive it.
    /// </summary>
    [Theory]
    [InlineData("user://steam/76561197960287930/profile1", "user://Runmobile/steam/76561197960287930/profile1/")]
    [InlineData("user://steam/76561197960287930/modded/profile1",
        "user://Runmobile/steam/76561197960287930/modded/profile1/")]
    [InlineData("user://default/1/profile0", "user://Runmobile/default/1/profile0/")]
    public void TheStoreMirrorsTheGamesOwnProfileScope(string gameScope, string expected)
    {
        Assert.Equal(expected, RunmobileStore.ScopedUserPath(gameScope));
    }

    [Theory]
    [InlineData("res://steam/1/profile0")]
    [InlineData("/Users/someone/steam/1/profile0")]
    [InlineData("user://")]
    public void AScopeThatIsNotUnderTheGamesUserDirectoryIsRefused(string gameScope)
    {
        Assert.Throws<InvalidOperationException>(() => RunmobileStore.ScopedUserPath(gameScope));
    }

    [Fact]
    public void AGameThatDoesNotSayWhereItsUserDirectoryIsGetsNoStore()
    {
        Assert.Throws<InvalidOperationException>(() => RunmobileStore.ResolveRoot(string.Empty));
    }

    [Fact]
    public void ReadingAnEntryThatIsNotThereIsNotAFailure()
    {
        Assert.Null(RunmobileStore.Read("nothing.json"));
        Assert.False(RunmobileStore.Exists("nothing.json"));
    }
}

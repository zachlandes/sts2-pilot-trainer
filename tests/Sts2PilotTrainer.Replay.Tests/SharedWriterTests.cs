using Sts2PilotTrainer.IO;

namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// The two rules the arbiter's evidence writer and the mod's store share, tested on
/// a machine that does not own the game because that is where they both hold.
/// </summary>
public sealed class SharedWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"shared-writer-{Guid.NewGuid():N}");

    public SharedWriterTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void AnAtomicWriteReplacesTheFileAndLeavesNoTemporarySibling()
    {
        var path = Path.Combine(_directory, "evidence.json");

        AtomicFile.WriteAllText(path, "first");
        AtomicFile.WriteAllText(path, "second");

        Assert.Equal("second", File.ReadAllText(path));
        Assert.Equal(["evidence.json"], Directory.EnumerateFiles(_directory).Select(Path.GetFileName));
    }

    /// <summary>
    /// A write that fails leaves nothing half-written and no temporary sibling: the
    /// directory has to exist first, and creating it is the caller's decision rather
    /// than something a writer does on the way past.
    /// </summary>
    [Fact]
    public void AWriteIntoADirectoryThatIsNotThereFailsAndLeavesNothing()
    {
        var path = Path.Combine(_directory, "missing", "evidence.json");

        Assert.ThrowsAny<IOException>(() => AtomicFile.WriteAllText(path, "content"));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_directory));
    }

    [Theory]
    [InlineData("/Users/someone/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/mods")]
    [InlineData("/Users/someone/Steam/x")]
    [InlineData("/opt/steamapps/y")]
    public void APathInsideAGameInstallationIsRefused(string path)
    {
        Assert.True(ProtectedInstallPath.HasProtectedComponent(path));
        Assert.Throws<ProtectedInstallPathException>(() => ProtectedInstallPath.RequireUnprotected(path));
    }

    [Theory]
    [InlineData("/opt/SteamApps/common/game")]
    [InlineData("/opt/slay the spire 2/mods")]
    public void InstallComponentsFollowTheHostFileSystemsCasing(string path)
    {
        var protectedOnThisHost = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

        Assert.Equal(protectedOnThisHost, ProtectedInstallPath.HasProtectedComponent(path));
    }

    /// <summary>
    /// The component rule is about a whole path component, not a substring: a
    /// directory whose name merely contains "steam" is somebody's ordinary folder.
    /// </summary>
    [Theory]
    [InlineData("/Users/someone/steamed-buns/notes.txt")]
    [InlineData("/Users/someone/Library/Application Support/SlayTheSpire2/Runmobile")]
    // The game's own user-data platform level, in lower case, which is where the
    // mod's store lives. Refusing it would refuse the store's own root.
    [InlineData("/Users/someone/Library/Application Support/SlayTheSpire2/steam/76561197960287930/profile1")]
    public void APathThatOnlyLooksLikeOneIsNot(string path)
    {
        Assert.False(ProtectedInstallPath.HasProtectedComponent(path));
        Assert.Equal(Path.GetFullPath(path), ProtectedInstallPath.RequireUnprotected(path));
    }
}

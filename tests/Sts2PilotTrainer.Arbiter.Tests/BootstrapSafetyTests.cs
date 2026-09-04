using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sts2PilotTrainer.Arbiter.Tests;

public class BootstrapSafetyTests
{
    [Fact]
    public void RefusesAnOutputDirectoryOutsideTheWorktreeBeforeWriting()
    {
        var outside = OutsideThisWorktree("bootstrap-escape");

        var result = RunBootstrap("--out", outside);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("resolves outside the allowed root", result.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outside));
    }

    /// <summary>
    /// The archive keeps a copy of the prepared set for a build a recording was made
    /// on, and it is a copy the tool makes on its own: the containment check has to
    /// happen before anything is prepared, or a refused destination would still have
    /// left the bootstrap's other writes behind.
    ///
    /// The game directory named here is an empty scratch directory, so this run would
    /// fail on its own a step later. That is what makes the assertion mean something:
    /// the refusal has to come from the archive path, before the tool ever looks at
    /// what is installed.
    /// </summary>
    [Fact]
    public void RefusesAnArchiveDirectoryOutsideTheWorktreeBeforeWriting()
    {
        var outside = OutsideThisWorktree("bootstrap-archive-escape");

        var result = RunBootstrap("--archive", outside);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("resolves outside the allowed root", result.Output, StringComparison.Ordinal);
        Assert.False(Directory.Exists(outside));

        // The run stopped before it read the installation, which is the step that would
        // have followed. A refusal that arrived after the copying had started would
        // still be a refusal, and would not be this one.
        Assert.DoesNotContain("build        :", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--game-dir")]
    [InlineData("--out")]
    [InlineData("--archive")]
    public void RefusesAPresentOptionWithoutAValue(string option)
    {
        var result = RunBootstrapRaw(option);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains($"Option {option} requires a value", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAnArchiveOptionFollowedByAnotherFlag()
    {
        var result = RunBootstrap("--archive", "--out", "build/lib");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Option --archive requires a value", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAVersionDirectorySymlinkOutsideTheArchive()
    {
        var prepared = WritePreparedSet();
        var archiveDir = ScratchDirectory("archive-version-link");
        var redirected = ScratchDirectory("archive-version-target");
        Directory.CreateDirectory(archiveDir);
        Directory.CreateSymbolicLink(
            Path.Combine(archiveDir, PreparedIdentity.Version), redirected);

        Assert.Throws<Sts2PilotTrainer.IO.PathContainmentException>(() =>
            Sts2PilotTrainer.Bootstrap.Program.Archive(
                prepared.Directory, archiveDir, PreparedIdentity, PristineHash, prepared.Hashes));
        Assert.Empty(Directory.EnumerateFileSystemEntries(redirected));
    }

    [Fact]
    public void RefusesAnArchivedFileSymlinkOutsideTheVersionDirectory()
    {
        var prepared = WritePreparedSet();
        var archiveDir = ScratchDirectory("archive-file-link");
        var versionDir = Path.Combine(archiveDir, PreparedIdentity.Version);
        Directory.CreateDirectory(versionDir);
        var victim = Path.Combine(ScratchDirectory("archive-file-target"), "victim.dll");
        File.WriteAllText(victim, "must remain unchanged");
        File.CreateSymbolicLink(Path.Combine(versionDir, "0Harmony.dll"), victim);

        Assert.Throws<Sts2PilotTrainer.IO.PathContainmentException>(() =>
            Sts2PilotTrainer.Bootstrap.Program.Archive(
                prepared.Directory, archiveDir, PreparedIdentity, PristineHash, prepared.Hashes));
        Assert.Equal("must remain unchanged", File.ReadAllText(victim));
    }

    [Fact]
    public void ArchivesACleanPreparedSetIdempotently()
    {
        var prepared = WritePreparedSet();
        var archiveDir = ScratchDirectory("archive-clean");

        var first = Sts2PilotTrainer.Bootstrap.Program.Archive(
            prepared.Directory, archiveDir, PreparedIdentity, PristineHash, prepared.Hashes);
        var second = Sts2PilotTrainer.Bootstrap.Program.Archive(
            prepared.Directory, archiveDir, PreparedIdentity, PristineHash, prepared.Hashes);

        Assert.Equal(first, second);
        Assert.Equal("prepared harmony", File.ReadAllText(Path.Combine(first, "0Harmony.dll")));
        Assert.True(File.Exists(Path.Combine(first, "prepared-assembly.json")));
    }

    [Fact]
    public void AcceptsAnIdenticalPreparedSetDespitePatchedSts2BytesChanging()
    {
        var receipt = WriteArchiveReceipt(new Dictionary<string, string>
        {
            ["sts2.dll"] = "archived-patched-bytes",
            ["0Harmony.dll"] = "same-harmony",
            ["release_info.json"] = "same-release-info",
        });
        var current = new Dictionary<string, string>
        {
            ["sts2.dll"] = "new-patched-bytes",
            ["0Harmony.dll"] = "same-harmony",
            ["release_info.json"] = "same-release-info",
        };

        Sts2PilotTrainer.Bootstrap.Program.RefuseDriftedArchive(
            receipt, "same-commit", "same-pristine-sts2", current);
    }

    [Fact]
    public void RefusesAChangedPreparedSiblingBeforeItCanBeOverwritten()
    {
        var receipt = WriteArchiveReceipt(new Dictionary<string, string>
        {
            ["sts2.dll"] = "archived-patched-bytes",
            ["0Harmony.dll"] = "archived-harmony",
        });
        var current = new Dictionary<string, string>
        {
            ["sts2.dll"] = "new-patched-bytes",
            ["0Harmony.dll"] = "current-harmony",
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            Sts2PilotTrainer.Bootstrap.Program.RefuseDriftedArchive(
                receipt, "same-commit", "same-pristine-sts2", current));

        Assert.Contains("prepared output 0Harmony.dll", error.Message, StringComparison.Ordinal);
        Assert.Contains("archived archived-harmony", error.Message, StringComparison.Ordinal);
        Assert.Contains("this run current-harmony", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RefusesAPreparedSiblingPresentOnOnlyOneSide(bool presentInArchive)
    {
        var archived = new Dictionary<string, string> { ["sts2.dll"] = "archived-patched-bytes" };
        var current = new Dictionary<string, string> { ["sts2.dll"] = "new-patched-bytes" };
        if (presentInArchive)
        {
            archived["0Harmony.dll"] = "harmony";
        }
        else
        {
            current["0Harmony.dll"] = "harmony";
        }
        var receipt = WriteArchiveReceipt(archived);

        var error = Assert.Throws<InvalidOperationException>(() =>
            Sts2PilotTrainer.Bootstrap.Program.RefuseDriftedArchive(
                receipt, "same-commit", "same-pristine-sts2", current));

        Assert.Contains("prepared output 0Harmony.dll", error.Message, StringComparison.Ordinal);
        Assert.Contains("unknown", error.Message, StringComparison.Ordinal);
    }

    private static string WriteArchiveReceipt(IReadOnlyDictionary<string, string> outputHashes)
    {
        var directory = ScratchDirectory("archive-receipt");
        var path = Path.Combine(directory, "prepared-assembly.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            build = new { commit = "same-commit" },
            pristine_sts2_sha256 = "same-pristine-sts2",
            prepared_output_sha256 = outputHashes,
        }));
        return path;
    }

    private static (string Directory, IReadOnlyDictionary<string, string> Hashes) WritePreparedSet()
    {
        var directory = ScratchDirectory("prepared-set");
        var assembly = Path.Combine(directory, "0Harmony.dll");
        File.WriteAllText(assembly, "prepared harmony");
        var hashes = new Dictionary<string, string>
        {
            ["0Harmony.dll"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(assembly))),
        };
        File.WriteAllText(Path.Combine(directory, "prepared-assembly.json"), JsonSerializer.Serialize(new
        {
            build = new { commit = PreparedIdentity.Commit },
            pristine_sts2_sha256 = PristineHash,
            prepared_output_sha256 = hashes,
        }));
        return (directory, hashes);
    }

    private static string ScratchDirectory(string name)
    {
        var directory = Path.Combine(
            Arbiter.RepoRoot, "build", "test-scratch", $"{name}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private const string PristineHash = "same-pristine-sts2";

    private static readonly Sts2PilotTrainer.Bootstrap.Program.InstalledIdentity PreparedIdentity =
        new("v0.111.0", "2026.01.01", "same-commit", "main", 123);

    private static string OutsideThisWorktree(string name) =>
        Path.GetFullPath(Path.Combine(Arbiter.RepoRoot, "..", $"{name}-{Guid.NewGuid():N}"));

    private static (int ExitCode, string Output) RunBootstrap(params string[] args)
    {
        var scratch = Path.Combine(
            Arbiter.RepoRoot, "build", "test-scratch", Guid.NewGuid().ToString("N"));
        var gameDir = Path.Combine(scratch, "game");
        Directory.CreateDirectory(gameDir);
        return RunBootstrapRaw(["--game-dir", gameDir, .. args]);
    }

    private static (int ExitCode, string Output) RunBootstrapRaw(params string[] args)
    {
        var bootstrap = Path.Combine(
            Arbiter.RepoRoot, "build", "bin", "Sts2PilotTrainer.Bootstrap", "Release", "net9.0",
            "Sts2PilotTrainer.Bootstrap.dll");

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = Arbiter.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(bootstrap);
        foreach (var arg in args) startInfo.ArgumentList.Add(arg);

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}

using Godot;

namespace Sts2PilotTrainer.Arbiter.Tests;

public class SandboxSafetyTests
{
    [Fact]
    public void RefusesASymlinkUnderTheSandboxThatTargetsOutside()
    {
        var parent = Path.GetFullPath(Path.Combine("build", "test-scratch", Guid.NewGuid().ToString("N")));
        var root = Path.Combine(parent, "sandbox");
        var outside = Path.Combine(parent, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        Directory.CreateSymbolicLink(Path.Combine(root, "link"), outside);
        HeadlessSandbox.SetRoot(root);

        Assert.Throws<UnauthorizedAccessException>(() => HeadlessSandbox.Globalize("user://link/file"));
        Assert.Throws<UnauthorizedAccessException>(() => DirAccess.MakeDirAbsolute(Path.Combine(root, "link", "created")));
        Assert.False(Directory.Exists(Path.Combine(outside, "created")));
    }

    [Fact]
    public void RefusesTraversalSiblingAndAbsoluteWritesOutsideTheSandbox()
    {
        var parent = Path.GetFullPath(Path.Combine("build", "test-scratch", Guid.NewGuid().ToString("N")));
        var root = Path.Combine(parent, "sandbox");
        HeadlessSandbox.SetRoot(root);

        var traversalTarget = Path.Combine(parent, "outside");
        Assert.Throws<UnauthorizedAccessException>(() => HeadlessSandbox.Globalize("user://../outside"));
        Assert.False(Directory.Exists(traversalTarget));

        var sibling = root + "-escape";
        Assert.Throws<UnauthorizedAccessException>(() => HeadlessSandbox.Guard(sibling));
        Assert.False(Directory.Exists(sibling));

        var protectedPaths = new[]
        {
            Path.Combine(parent, "steamapps", "absolute"),
            Path.Combine(parent, "steamapps", "recursive-absolute"),
            Path.Combine(parent, "steamapps", "recursive"),
        };
        Assert.Throws<UnauthorizedAccessException>(() => DirAccess.MakeDirAbsolute(protectedPaths[0]));
        Assert.Throws<UnauthorizedAccessException>(() => DirAccess.MakeDirRecursiveAbsolute(protectedPaths[1]));
        Assert.Throws<UnauthorizedAccessException>(() => new DirAccess().MakeDirRecursive(protectedPaths[2]));
        Assert.All(protectedPaths, path => Assert.False(Directory.Exists(path)));
    }
}

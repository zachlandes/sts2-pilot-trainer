namespace Sts2PilotTrainer.IO;

public static class WorktreePath
{
    public static string Require(string path) =>
        PathContainment.RequireContained(WorktreeLocator.Find(), path);

    public static string RequireChild(string directory, string child)
    {
        var root = Require(directory);
        return PathContainment.RequireContained(WorktreeLocator.Find(), Path.Combine(root, child));
    }
}

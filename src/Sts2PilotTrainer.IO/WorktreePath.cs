namespace Sts2PilotTrainer.IO;

public static class WorktreePath
{
    private static string Root()
    {
        var configured = Environment.GetEnvironmentVariable("STS2_PILOT_TRAINER_WORKSPACE");
        return string.IsNullOrWhiteSpace(configured)
            ? WorktreeLocator.Find()
            : ProtectedInstallPath.RequireUnprotected(Path.GetFullPath(configured));
    }

    public static string Require(string path) =>
        PathContainment.RequireContained(Root(), path);

    public static string RequireChild(string directory, string child)
    {
        var root = Root();
        var parent = PathContainment.RequireContained(root, directory);
        return PathContainment.RequireContained(root, Path.Combine(parent, child));
    }
}

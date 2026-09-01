namespace Sts2PilotTrainer.IO;

public static class WorktreeLocator
{
    public static string Find()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var gitPath = Path.Combine(directory.FullName, ".git");
                if (File.Exists(Path.Combine(directory.FullName, "sts2-pilot-trainer.sln")) &&
                    (File.Exists(gitPath) || Directory.Exists(gitPath)))
                {
                    return PathContainment.ResolveExistingPath(directory.FullName);
                }
                directory = directory.Parent;
            }
        }

        throw new InvalidOperationException("Could not locate the sts2-pilot-trainer worktree root.");
    }
}

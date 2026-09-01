namespace Sts2PilotTrainer.IO;

public static class PathContainment
{
    public static string RequireContained(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        if (!IsComponentWithin(fullPath, fullRoot) || !IsResolvedWithin(fullPath, fullRoot))
        {
            throw new PathContainmentException(
                $"Path '{fullPath}' resolves outside the allowed root '{fullRoot}'.");
        }
        return fullPath;
    }

    public static bool IsResolvedWithin(string path, string root) =>
        IsComponentWithin(ResolveExistingPath(path), ResolveExistingPath(root));

    public static string ResolveExistingPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        var components = full[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < components.Length; index++)
        {
            var candidate = Path.Combine(current, components[index]);
            FileSystemInfo entry = Directory.Exists(candidate)
                ? new DirectoryInfo(candidate)
                : new FileInfo(candidate);
            var linkTarget = entry.LinkTarget;
            if (linkTarget is not null)
            {
                current = entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                    ?? throw new IOException($"Could not resolve symbolic link '{entry.FullName}'.");
                continue;
            }

            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return Path.Combine(current, Path.Combine(components[index..]));
            }

            current = entry.FullName;
        }

        return current;
    }

    private static bool IsComponentWithin(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return relative == "." ||
               (!Path.IsPathRooted(relative) &&
                relative != ".." &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }
}

public sealed class PathContainmentException(string message) : InvalidOperationException(message);

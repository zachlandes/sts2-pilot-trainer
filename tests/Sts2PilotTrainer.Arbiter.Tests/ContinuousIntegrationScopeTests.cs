using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The CI workflow builds a hand-listed subset of the solution, because the engine
/// half needs the licensed game assembly and cannot run on a public runner. That
/// subset is only meaningful while every project in it really is free of the game
/// dependency, so the closure is computed here rather than trusted as a comment.
/// </summary>
public sealed class ContinuousIntegrationScopeTests
{
    private static readonly string WorkflowPath =
        Path.Combine(Arbiter.RepoRoot, ".github", "workflows", "ci.yml");

    private const string GameDependentProject = "Sts2PilotTrainer.Engine";

    [Fact]
    public void WorkflowListsProjects()
    {
        Assert.NotEmpty(WorkflowProjects());
    }

    [Fact]
    public void EveryWorkflowProjectBuildsWithoutTheGameAssembly()
    {
        foreach (var project in WorkflowProjects())
        {
            var path = Path.Combine(Arbiter.RepoRoot, project);
            Assert.True(File.Exists(path), $"{project} is listed in CI but does not exist.");
            var closure = ReferenceClosure(path).ToList();
            Assert.DoesNotContain(closure, entry =>
                Path.GetFileNameWithoutExtension(entry) == GameDependentProject);
            Assert.DoesNotContain(closure, ReferencesGameAssembly);
        }
    }

    /// <summary>
    /// The negative control: the detector must fail the project that genuinely does
    /// depend on the game, or it has not been shown able to fail at all.
    /// </summary>
    [Fact]
    public void DetectorRejectsAGameDependentProject()
    {
        var cli = Path.Combine(
            Arbiter.RepoRoot, "src", "Sts2PilotTrainer.Cli", "Sts2PilotTrainer.Cli.csproj");
        var closure = ReferenceClosure(cli).ToList();
        Assert.Contains(closure, entry =>
            Path.GetFileNameWithoutExtension(entry) == GameDependentProject);
        Assert.Contains(closure, ReferencesGameAssembly);
    }

    private static bool ReferencesGameAssembly(string projectPath) =>
        XDocument.Load(projectPath).Descendants("Reference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Any(include => include is not null &&
                include.Contains("sts2", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> ReferenceClosure(string projectPath)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([Path.GetFullPath(projectPath)]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current) || !File.Exists(current))
            {
                continue;
            }
            yield return current;
            var directory = Path.GetDirectoryName(current)!;
            foreach (var reference in XDocument.Load(current).Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")?.Value;
                if (include is null)
                {
                    continue;
                }
                pending.Push(Path.GetFullPath(
                    Path.Combine(directory, include.Replace('\\', '/'))));
            }
        }
    }

    /// <summary>Reads the workflow's PROJECTS list as the list it is, not as text.</summary>
    private static IReadOnlyList<string> WorkflowProjects()
    {
        var workflow = File.ReadAllText(WorkflowPath);
        var block = Regex.Match(workflow, @"PROJECTS:\s*>-\s*\n(?<body>(?:\s+\S+\.csproj\s*\n)+)");
        Assert.True(block.Success, "The CI workflow no longer declares a PROJECTS list.");
        return block.Groups["body"].Value
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}

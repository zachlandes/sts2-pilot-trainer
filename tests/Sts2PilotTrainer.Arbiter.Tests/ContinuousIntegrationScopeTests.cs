using System.Text.Json;
using System.Xml.Linq;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// Public CI builds the solution filter whose projects do not need the licensed
/// game assembly. The filter is the shared contract consumed by both CI and this
/// reference-closure check.
/// </summary>
public sealed class ContinuousIntegrationScopeTests
{
    private static readonly string SolutionFilterPath =
        Path.Combine(Arbiter.RepoRoot, "sts2-pilot-trainer.domain.slnf");

    private const string GameDependentProject = "Sts2PilotTrainer.Engine";

    [Fact]
    public void SolutionFilterListsProjects()
    {
        Assert.NotEmpty(SolutionFilterProjects());
    }

    [Fact]
    public void EverySolutionFilterProjectBuildsWithoutTheGameAssembly()
    {
        foreach (var project in SolutionFilterProjects())
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

    private static IReadOnlyList<string> SolutionFilterProjects()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SolutionFilterPath));
        return document.RootElement.GetProperty("solution").GetProperty("projects")
            .EnumerateArray()
            .Select(project => project.GetString()!.Replace('\\', '/'))
            .ToList();
    }
}

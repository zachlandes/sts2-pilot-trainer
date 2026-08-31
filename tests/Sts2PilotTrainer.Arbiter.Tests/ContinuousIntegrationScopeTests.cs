using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace Sts2PilotTrainer.Arbiter.Tests;

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
                Path.GetFileNameWithoutExtension(entry.ProjectPath) == GameDependentProject);
            Assert.DoesNotContain(closure, ReferencesGameAssembly);
        }
    }

    [Fact]
    public void DetectorRejectsAGameDependentProject()
    {
        var cli = Path.Combine(
            Arbiter.RepoRoot, "src", "Sts2PilotTrainer.Cli", "Sts2PilotTrainer.Cli.csproj");
        var closure = ReferenceClosure(cli).ToList();
        Assert.Contains(closure, entry =>
            Path.GetFileNameWithoutExtension(entry.ProjectPath) == GameDependentProject);
        Assert.Contains(closure, ReferencesGameAssembly);
    }

    [Fact]
    public void DetectorIncludesProjectReferencesFromImportedMsBuildState()
    {
        var fixture = Path.Combine(
            Arbiter.RepoRoot, "build", "test-scratch", "msbuild-graph", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var engine = Path.Combine(
            Arbiter.RepoRoot, "src", "Sts2PilotTrainer.Engine", "Sts2PilotTrainer.Engine.csproj");
        new XDocument(
            new XElement("Project",
                new XElement("ItemGroup",
                    new XElement("ProjectReference", new XAttribute("Include", engine)))))
            .Save(Path.Combine(fixture, "Directory.Build.targets"));
        var entry = Path.Combine(fixture, "ImportedReference.csproj");
        File.WriteAllText(
            entry,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
            "<TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>\n");

        var closure = ReferenceClosure(entry).ToList();

        Assert.Contains(closure, evaluated =>
            Path.GetFileNameWithoutExtension(evaluated.ProjectPath) == GameDependentProject);
        Assert.Contains(closure, ReferencesGameAssembly);
    }

    private static bool ReferencesGameAssembly(EvaluatedProject project) =>
        project.AssemblyReferences.Any(reference =>
            string.Equals(
                reference.Split(',', 2)[0],
                "sts2",
                StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<EvaluatedProject> ReferenceClosure(string projectPath)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>([Path.GetFullPath(projectPath)]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            var evaluated = Evaluate(current);
            yield return evaluated;
            foreach (var reference in evaluated.ProjectReferences)
            {
                pending.Push(reference);
            }
        }
    }

    private static EvaluatedProject Evaluate(string projectPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            WorkingDirectory = Arbiter.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("msbuild");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-nologo");
        startInfo.ArgumentList.Add("-verbosity:quiet");
        startInfo.ArgumentList.Add("-property:Configuration=Release");
        startInfo.ArgumentList.Add("-getItem:ProjectReference,Reference");

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, error + output);

        using var document = JsonDocument.Parse(output);
        var items = document.RootElement.GetProperty("Items");
        var projectReferences = Items(items, "ProjectReference")
            .Select(item => item.GetProperty("FullPath").GetString()!)
            .ToList();
        var assemblyReferences = Items(items, "Reference")
            .Select(item => item.GetProperty("Identity").GetString()!)
            .ToList();
        return new EvaluatedProject(projectPath, projectReferences, assemblyReferences);
    }

    private static IEnumerable<JsonElement> Items(JsonElement items, string name) =>
        items.TryGetProperty(name, out var values)
            ? values.EnumerateArray().ToList()
            : [];

    private static IReadOnlyList<string> SolutionFilterProjects()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(SolutionFilterPath));
        return document.RootElement.GetProperty("solution").GetProperty("projects")
            .EnumerateArray()
            .Select(project => project.GetString()!.Replace('\\', '/'))
            .ToList();
    }

    private sealed record EvaluatedProject(
        string ProjectPath,
        IReadOnlyList<string> ProjectReferences,
        IReadOnlyList<string> AssemblyReferences);
}

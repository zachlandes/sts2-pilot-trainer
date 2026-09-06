using System.Text.Json;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What the two committed recordings a person played actually answer, run through the
/// commands that answer it.
///
/// <c>ReplayTests.NativeRecordingReproducesEveryBoundaryItCaptured</c> establishes that
/// both reproduce every boundary they captured. That is not the same question as what
/// the publication gate says about them, and the two answers differ: the longer run is
/// publishable and the shorter one is not, for a reason that is about the run rather
/// than about the engine. Without this, both results lived only in somebody's terminal,
/// and the README states them.
///
/// <c>NativeGateTests</c> asks which conditions the gate puts to a recording of this
/// kind and deliberately does not read the verdicts, over a synthesized fixture. These
/// read the verdicts, over the recordings themselves.
/// </summary>
public sealed class NativeEvidenceTests
{
    private const string Publishable = "native-3LACFJ5NJ371-20260906-015901.replay.json";
    private const string ShortRun = "native-9F8CY60C5BK7-20260906-005737.replay.json";

    /// <summary>
    /// The longer recording passes every condition, negative controls included, and the
    /// controls are the whole set rather than the subset its history happens to admit.
    /// </summary>
    [GameFact]
    public void TheLongerNativeRecordingIsPublishableWithEveryControlApplied()
    {
        InScratch(outDir =>
        {
            var gate = Arbiter.Run("gate", Manifest(Publishable), "--out", outDir);

            Assert.True(gate.Verified, gate.All);
            Assert.Contains("PUBLISHABLE - every condition of the gate holds", gate.Output, StringComparison.Ordinal);
            var conditions = Conditions(gate.Output);
            Assert.DoesNotContain(conditions, condition => !condition.Value);
            Assert.True(conditions["rejection"]);

            var controls = JsonDocument
                .Parse(File.ReadAllText(Path.Combine(outDir, "negative-controls.json")))
                .RootElement;

            Assert.Equal(10, controls.GetProperty("total_controls").GetInt32());
            Assert.Equal(10, controls.GetProperty("applicable_controls").GetInt32());
        });
    }

    /// <summary>
    /// The shorter recording reproduces and is still refused, and the refusal is about
    /// what its run never did: three controls have nothing in that history to damage.
    /// A run this short is not evidence of the standard, which is the distinction the
    /// README draws between the two.
    /// </summary>
    [GameFact]
    public void TheShortNativeRecordingIsRefusedOnlyForTheControlsItsRunCannotSupply()
    {
        InScratch(outDir =>
        {
            var gate = Arbiter.Run("gate", Manifest(ShortRun), "--out", outDir);

            Assert.False(gate.Verified, gate.All);
            Assert.Contains("NOT PUBLISHABLE", gate.Output, StringComparison.Ordinal);

            var failed = Conditions(gate.Output).Where(c => !c.Value).Select(c => c.Key).ToList();
            Assert.Equal(new[] { "rejection" }, failed);

            Assert.Contains("ONLY 7 OF 10 REQUIRED CONTROLS APPLIED", gate.All, StringComparison.Ordinal);

            var inapplicable = gate.All
                .Split('\n')
                .Where(line => line.Contains("NOT APPLICABLE - this control needs ", StringComparison.Ordinal))
                .ToList();
            Assert.Equal(3, inapplicable.Count);
            Assert.Contains(inapplicable, line => line.Contains("needs a card reward", StringComparison.Ordinal));
            Assert.Contains(
                inapplicable, line => line.Contains("needs a card marked on a screen", StringComparison.Ordinal));
            Assert.Contains(inapplicable, line => line.Contains("needs an event choice", StringComparison.Ordinal));
        });
    }

    /// <summary>
    /// And the run's own second fight can be entered from its history alone, arriving at
    /// the state the recorder captured there rather than at a plausible one.
    /// </summary>
    [GameFact]
    public void TheLongerNativeRecordingsSecondFightIsEnteredAtTheDigestItRecorded()
    {
        InScratch(outDir =>
        {
            var entered = Arbiter.Run(
                "enter-fight", Manifest(Publishable), "--fight", "2", "--out", outDir);

            Assert.True(entered.Verified, entered.All);
            Assert.Contains("ENTERED", entered.Output, StringComparison.Ordinal);

            var recorded = Field(entered.Output, "recorded");
            var thisGame = Field(entered.Output, "this game");
            Assert.NotEmpty(recorded);
            Assert.Equal(recorded, thisGame);
        });
    }

    private static string Manifest(string fileName) =>
        Path.Combine(Arbiter.RepoRoot, "manifests", fileName);

    /// <summary>Every condition the gate printed, and the verdict it printed for it.</summary>
    private static IReadOnlyDictionary<string, bool> Conditions(string output) =>
        output
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("pass ", StringComparison.Ordinal) ||
                           line.StartsWith("FAIL ", StringComparison.Ordinal))
            .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToDictionary(
                parts => parts[1],
                parts => string.Equals(parts[0], "pass", StringComparison.Ordinal),
                StringComparer.Ordinal);

    /// <summary>One of <c>enter-fight</c>'s labelled snapshot lines.</summary>
    private static string Field(string output, string label) =>
        output
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith(label + " ", StringComparison.Ordinal) &&
                           line.Contains(':', StringComparison.Ordinal))
            .Select(line => line.Split(':', 2)[1].Trim())
            .FirstOrDefault() ?? "";

    /// <summary>
    /// Evidence refuses to be written outside the repository, which is the sandbox rule
    /// rather than an accident of this test.
    /// </summary>
    private static void InScratch(Action<string> body)
    {
        var directory = Path.Combine(
            Arbiter.RepoRoot, "build", "test-scratch", $"native-evidence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            body(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

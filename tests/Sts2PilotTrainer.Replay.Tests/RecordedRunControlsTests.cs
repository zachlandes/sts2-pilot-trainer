namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// Whether a recording this project's own recorder made can clear the publication
/// gate's rejection condition at all.
///
/// That condition runs <c>negative-controls --require-all-controls</c>, which passes
/// only when nothing was skipped - every one of <see cref="Corruption.All"/> found the
/// decision it damages. Three of them find it only when the history nominates the
/// alternative the control takes, and for a long while the recorder wrote none of the
/// three: every value it recorded was true, every engine condition passed, and no
/// recording could ever be published. Nothing said so until somebody ran the gate on
/// one, because the fixture the gate's own test used was converted from the shipped
/// video reconstruction and inherited nominations no recorder writes.
///
/// So this asks the same question of a manifest a recorder produced, and it needs no
/// engine to ask it.
/// </summary>
public sealed class RecordedRunControlsTests
{
    [Fact]
    public void EveryNegativeControlAppliesToARecordingTheRecorderProduced()
    {
        var manifest = RecordedRun.Manifest();

        var inapplicable = Corruption.All
            .Where(control => !control.AppliesTo(manifest))
            .Select(control => $"{control.Name} (needs {control.Requires})")
            .ToList();

        Assert.True(
            inapplicable.Count == 0,
            $"A recording cannot be published while any control has nothing to damage: " +
            $"{string.Join("; ", inapplicable)}.");
    }

    /// <summary>
    /// And each of them still produces a history the validator accepts, because a
    /// corruption the validator rejects is caught before the engine ever sees it and
    /// proves nothing about the engine.
    /// </summary>
    [Fact]
    public void AndEachOfThemLeavesAHistoryTheValidatorStillReads()
    {
        var manifest = RecordedRun.Manifest();

        Assert.True(ManifestValidator.Validate(manifest).IsValid, ManifestValidator.Validate(manifest).Describe());

        foreach (var control in Corruption.All)
        {
            var corrupted = control.Apply(manifest);
            var result = ManifestValidator.Validate(corrupted);

            Assert.True(result.IsValid, $"{control.Name}: {result.Describe()}");
            Assert.NotEqual(
                manifest.Actions.Select(action => (action.Seq, action.Verb, Rendered(action.Args))),
                corrupted.Actions.Select(action => (action.Seq, action.Verb, Rendered(action.Args))));
        }
    }

    private static string Rendered(IReadOnlyDictionary<string, string> args) =>
        string.Join(",", args.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}"));
}

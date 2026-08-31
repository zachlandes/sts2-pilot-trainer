using System.Text.Json;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

public class ReplayTests
{
    [GameFact]
    public void PreflightAcceptsTheAuditedSourceToolingIdentity()
    {
        var result = Arbiter.Run("preflight", Arbiter.Manifest);

        Assert.True(result.Verified, result.All);
        Assert.Contains("audited source tooling", result.Output, StringComparison.Ordinal);
        Assert.Contains("environment matches", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void PreflightAcceptsACompleteVanillaEnvironment()
    {
        var result = Arbiter.Run("preflight", Arbiter.SyntheticReplayFixture());

        Assert.True(result.Verified, result.All);
        Assert.Contains("environment matches", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void PreflightRefusesAManifestFromADifferentBuild()
    {
        // The negative input for the preflight checker. Replaying into a mismatched
        // environment does not fail - it succeeds at producing a different run - so
        // this refusal is the only thing standing between a mismatch and a confident
        // wrong answer.
        var path = Temp("wrong-build.json");
        var manifest = ManifestJson.Load(Arbiter.Manifest);
        ManifestJson.Save(
            manifest with
            {
                Environment = manifest.Environment with
                {
                    BuildVersion = Fact<string>.Observed("v0.103.2", FactEvidence.AtVideoTime(1, "test")),
                },
            },
            path);

        var result = Arbiter.Run("preflight", path);

        Assert.False(result.Verified);
        Assert.Contains("does NOT match", result.Output, StringComparison.Ordinal);
        Assert.Contains("build_version", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void PreflightRefusesAManifestWithADifferentContentHash()
    {
        var path = Temp("wrong-hash.json");
        var manifest = ManifestJson.Load(Arbiter.Manifest);
        ManifestJson.Save(
            manifest with
            {
                Environment = manifest.Environment with
                {
                    ContentHash = Fact<string>.Observed("1234567890", FactEvidence.AtVideoTime(1, "test")),
                },
            },
            path);

        var result = Arbiter.Run("preflight", path);

        Assert.False(result.Verified);
        Assert.Contains("content_hash", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void SyntheticReplayReproducesEveryPinnedEngineCheckpoint()
    {
        var result = Arbiter.Run("replay", Arbiter.SyntheticReplayFixture());

        Assert.True(result.Verified, result.All);
        Assert.Contains("status         : VERIFIED", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("FAIL", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void ReplayRefusesAnUnreachableMapJump()
    {
        var path = Temp("unreachable-map-jump.json");
        var manifest = ManifestJson.Load(Arbiter.SyntheticReplayFixture());
        var actions = manifest.Actions.Select(action => action.Seq == 1
            ? action with
            {
                Args = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["act"] = "0",
                    ["row"] = "2",
                    ["column"] = "2",
                },
            }
            : action).ToList();
        ManifestJson.Save(manifest with { Actions = actions }, path);

        var result = Arbiter.Run("replay", path);

        Assert.False(result.Verified);
        Assert.Contains("not reachable", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void ReplayRefusesAMapMoveNamingADifferentAct()
    {
        var path = Temp("wrong-map-act.json");
        var manifest = ManifestJson.Load(Arbiter.SyntheticReplayFixture());
        var actions = manifest.Actions.Select(action =>
        {
            if (action.Seq != 1) return action;
            var changedArgs = action.Args.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            changedArgs["act"] = "1";
            return action with { Args = changedArgs };
        }).ToList();
        ManifestJson.Save(manifest with { Actions = actions }, path);

        var result = Arbiter.Run("replay", path);

        Assert.False(result.Verified);
        Assert.Contains("names act 1, but the run is in act 0", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void ReplayRefusesATargetForACardThatDoesNotConsumeIt()
    {
        var path = Temp("false-card-target.json");
        var manifest = ManifestJson.Load(Arbiter.SyntheticReplayFixture());
        var changed = false;
        var actions = manifest.Actions.Select(action =>
        {
            if (changed || action.Verb != ActionVerb.PlayCard ||
                action.Args.GetValueOrDefault("card_id") != "CARD.DEFEND_IRONCLAD")
            {
                return action;
            }

            changed = true;
            var changedArgs = action.Args.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            changedArgs["target_index"] = "999";
            return action with { Args = changedArgs };
        }).ToList();
        Assert.True(changed);
        ManifestJson.Save(manifest with { Actions = actions }, path);

        var result = Arbiter.Run("replay", path);

        Assert.False(result.Verified);
        Assert.Contains("does not target an enemy", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void ReplayingTwiceInFreshProcessesProducesByteIdenticalState()
    {
        var result = Arbiter.Run(
            "determinism", Arbiter.SyntheticReplayFixture(), "--runs", "2", "--out", TempDir());

        Assert.True(result.Verified, result.All);
        Assert.Contains("byte-identical canonical state", result.Output, StringComparison.Ordinal);
    }

    [GameFact]
    public void FailedDeterminismRunClearsAnEarlierIdenticalResult()
    {
        var outDir = TempDir();
        var initial = Arbiter.Run(
            "determinism", Arbiter.SyntheticReplayFixture(), "--runs", "2", "--out", outDir);
        Assert.True(initial.Verified, initial.All);
        var reportPath = Path.Combine(outDir, "determinism.json");
        Assert.True(File.Exists(reportPath));

        var malformed = Path.Combine(outDir, "malformed-manifest.json");
        File.WriteAllText(malformed, "{");
        var result = Arbiter.Run("determinism", malformed, "--runs", "2", "--out", outDir);

        Assert.False(result.Verified);
        Assert.False(File.Exists(reportPath));
    }

    [GameFact]
    public void EveryCorruptedHistoryIsRejectedAndTheUncorruptedOneIsNot()
    {
        var outDir = TempDir();

        var result = Arbiter.Run(
            "negative-controls", Arbiter.SyntheticReplayFixture(), "--out", outDir);

        Assert.True(result.Verified, result.All);

        var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "negative-controls.json"))).RootElement;
        Assert.True(report.GetProperty("baseline_verified").GetBoolean());
        Assert.True(report.GetProperty("all_rejected").GetBoolean());

        var controls = report.GetProperty("controls").EnumerateArray().ToList();
        Assert.All(controls, control =>
        {
            Assert.True(control.GetProperty("arbiter_rejected").GetBoolean());
            Assert.False(control.GetProperty("ingestion_rejected").GetBoolean());
            Assert.Equal("Rejected", control.GetProperty("replay_status").GetString());
        });

        // The two corruptions arithmetic on the footage cannot see must both be here
        // and must both be rejected. Without them the suite would only demonstrate
        // what the cheaper checks already caught.
        foreach (var name in new[] { "reorder-plays", "substitute-same-cost" })
        {
            var control = controls.Single(c => c.GetProperty("name").GetString() == name);
            Assert.Equal("Undetected", control.GetProperty("video_only_verdict").GetString());
            Assert.True(control.GetProperty("arbiter_rejected").GetBoolean());
        }
    }

    [GameFact]
    public void IngestionFailureDoesNotCountAsArbiterRejection()
    {
        var manifest = ManifestJson.Load(Arbiter.SyntheticReplayFixture());
        var lastPlaySeq = manifest.Actions.Last(action => action.Verb == ActionVerb.PlayCard).Seq;
        manifest = manifest with
        {
            Checkpoints = manifest.Checkpoints.Where(checkpoint => checkpoint.AfterSeq == lastPlaySeq).ToList(),
        };
        var path = Temp("ingestion-negative-control.json");
        ManifestJson.Save(manifest, path);
        var outDir = TempDir();

        var result = Arbiter.Run("negative-controls", path, "--out", outDir);

        Assert.False(result.Verified);
        var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "negative-controls.json"))).RootElement;
        Assert.False(report.GetProperty("all_rejected").GetBoolean());
        var omitted = report.GetProperty("controls").EnumerateArray()
            .Single(control => control.GetProperty("name").GetString() == "omit-play");
        Assert.True(omitted.GetProperty("ingestion_rejected").GetBoolean());
        Assert.False(omitted.GetProperty("arbiter_rejected").GetBoolean());
        Assert.Equal("IngestionRejected", omitted.GetProperty("replay_status").GetString());
    }

    [GameFact]
    public void MissingRejectedStateIsReportedAsUnavailable()
    {
        var manifest = ManifestJson.Load(Arbiter.SyntheticReplayFixture());
        var actions = manifest.Actions.Select(action =>
        {
            if (!action.Args.ContainsKey("negative_control_substitute_hand_index")) return action;
            var changedArgs = action.Args.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            changedArgs["negative_control_substitute_hand_index"] = "999";
            return action with { Args = changedArgs };
        }).ToList();
        var path = Temp("unavailable-negative-state.json");
        ManifestJson.Save(manifest with { Actions = actions }, path);
        var outDir = TempDir();

        var result = Arbiter.Run("negative-controls", path, "--out", outDir);

        Assert.True(result.Verified, result.All);
        Assert.Contains("end state       : UNAVAILABLE", result.Output, StringComparison.Ordinal);
        var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "negative-controls.json"))).RootElement;
        var controls = report.GetProperty("controls").EnumerateArray().ToList();
        var unavailable = controls.Single(control =>
            control.GetProperty("name").GetString() == "substitute-same-cost");
        Assert.Equal(JsonValueKind.Null, unavailable.GetProperty("end_state_differs").ValueKind);
        Assert.Equal("Unavailable", unavailable.GetProperty("end_state_comparison").GetString());

        var completed = controls.Single(control => control.GetProperty("name").GetString() == "reorder-plays");
        Assert.Contains(
            completed.GetProperty("end_state_differs").ValueKind,
            new[] { JsonValueKind.True, JsonValueKind.False });
        Assert.NotEqual("Unavailable", completed.GetProperty("end_state_comparison").GetString());
    }

    [GameFact]
    public void ReorderingIsCaughtAtTheFirstDivergentCheckpoint()
    {
        // The reordered cards spend the same energy and produce the same final state.
        // The first bound checkpoint catches their order inside the turn.
        var outDir = TempDir();
        var result = Arbiter.Run("negative-controls", Arbiter.Manifest, "--out", outDir);
        Assert.True(result.Verified, result.All);

        var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "negative-controls.json"))).RootElement;
        var reorder = report.GetProperty("controls").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "reorder-plays");

        Assert.True(reorder.GetProperty("arbiter_rejected").GetBoolean());
        Assert.False(reorder.GetProperty("ingestion_rejected").GetBoolean());
        Assert.Equal("Rejected", reorder.GetProperty("replay_status").GetString());
        Assert.Equal("Undetected", reorder.GetProperty("video_only_verdict").GetString());
        Assert.False(reorder.GetProperty("end_state_differs").GetBoolean());
        Assert.Contains(
            "checkpoint 'after-hellraiser'",
            reorder.GetProperty("first_divergence").GetString(),
            StringComparison.Ordinal);
    }

    private static string Temp(string name)
    {
        var dir = TempDir();
        return Path.Combine(dir, name);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}

/// <summary>
/// The publication gate. Its whole job is to be hard to pass, so it needs a
/// demonstrated failure as much as a demonstrated pass.
/// </summary>
public class PublicationGateTests
{
    /// <summary>
    /// The mode condition passes on parity across the enumerated space, never on an
    /// identification. The recording does not show the mode and nothing here may claim
    /// it does, so the gate passing and the mode staying unestablished have to hold at
    /// the same time.
    /// </summary>
    [GameFact]
    public void PublishesOnModeParityWithoutEstablishingTheSourceMode()
    {
        var outDir = TempDir();
        var result = Arbiter.Run("gate", Arbiter.Manifest, "--out", outDir);

        Assert.True(result.Verified, result.All);
        Assert.Contains("PUBLISHABLE", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("NOT PUBLISHABLE", result.Output, StringComparison.Ordinal);

        var report = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(outDir, "publication-gate.json"))).RootElement;
        var mode = report.GetProperty("conditions").EnumerateArray()
            .Single(condition => condition.GetProperty("name").GetString() == "game-mode");
        Assert.True(mode.GetProperty("passed").GetBoolean());

        var modeReport = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(outDir, "mode-discrimination.json"))).RootElement;
        Assert.False(modeReport.GetProperty("mode_established").GetBoolean());
        Assert.True(modeReport.GetProperty("path_specific_mode_parity").GetBoolean());
        Assert.True(modeReport.GetProperty("combination_space_not_enumerated").GetBoolean());
    }

    [GameFact]
    public void ProvenanceRefusalSkipsPreflightAndEveryEngineCondition()
    {
        var outDir = TempDir();
        var manifest = ManifestJson.Load(Arbiter.Manifest);
        var path = Path.Combine(outDir, "resumed-run.json");
        ManifestJson.Save(
            manifest with
            {
                Source = manifest.Source with
                {
                    RunStart = manifest.Source.RunStart! with
                    {
                        ResumeModalSeen = manifest.Source.RunStart.ResumeModalSeen with { Value = true },
                    },
                },
            },
            path);

        var result = Arbiter.Run(
            "gate", path,
            "--map-observation", Path.Combine(outDir, "must-not-be-read.json"),
            "--baselib", Path.Combine(outDir, "must-not-be-read.dll"),
            "--out", outDir);

        Assert.False(result.Verified);
        Assert.DoesNotContain("must-not-be-read", result.All, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(outDir, "baselib-reachability.json")));
        Assert.False(File.Exists(Path.Combine(outDir, "seed-verification-summary.json")));
        var report = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(outDir, "publication-gate.json"))).RootElement;
        Assert.False(report.GetProperty("conditions").EnumerateArray()
            .Single(condition => condition.GetProperty("name").GetString() == "provenance")
            .GetProperty("passed").GetBoolean());
        Assert.False(report.GetProperty("conditions").EnumerateArray()
            .Single(condition => condition.GetProperty("name").GetString() == "environment")
            .GetProperty("passed").GetBoolean());
    }

    [GameFact]
    public void ParentFailureClearsAnEarlierPublishableGateArtifact()
    {
        var outDir = TempDir();
        var gatePath = Path.Combine(outDir, "publication-gate.json");
        File.WriteAllText(gatePath, "{\"publishable\":true}");
        var malformedManifest = Path.Combine(outDir, "malformed-manifest.json");
        File.WriteAllText(malformedManifest, "{");

        var result = Arbiter.Run("gate", malformedManifest, "--out", outDir);

        Assert.False(result.Verified);
        Assert.False(File.Exists(gatePath));
    }

    [GameFact]
    public void RefusesSyntheticEngineFixturesAsPublicationEvidence()
    {
        var outDir = TempDir();
        var result = Arbiter.Run("gate", Arbiter.SyntheticReplayFixture(), "--out", outDir);

        Assert.False(result.Verified);
        var report = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(outDir, "publication-gate.json"))).RootElement;
        var source = report.GetProperty("conditions").EnumerateArray()
            .Single(condition => condition.GetProperty("name").GetString() == "publication-source");
        Assert.False(source.GetProperty("passed").GetBoolean());
    }

    [GameFact]
    public void RequiresTheManifestSeedToMatchTheBoundVodMap()
    {
        var outDir = TempDir();
        var path = Path.Combine(outDir, "wrong-legal-seed.json");
        var manifest = ManifestJson.Load(Arbiter.Manifest);
        ManifestJson.Save(
            manifest with
            {
                Environment = manifest.Environment with
                {
                    Seed = manifest.Environment.Seed with { Value = "SEXT47K77REK" },
                },
                Source = manifest.Source with
                {
                    RunSummary = manifest.Source.RunSummary! with
                    {
                        Seed = manifest.Source.RunSummary.Seed with { Value = "SEXT47K77REK" },
                    },
                },
            },
            path);

        Arbiter.Run("gate", path, "--map-observation", Arbiter.MapObservation, "--out", outDir);

        var report = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(outDir, "publication-gate.json"))).RootElement;
        var seed = report.GetProperty("conditions").EnumerateArray()
            .Single(condition => condition.GetProperty("name").GetString() == "seed-topology");
        Assert.False(seed.GetProperty("passed").GetBoolean());
    }

    [GameFact]
    public void ModeDiscriminationDetectsItsBehaviorChangingControl()
    {
        var outDir = TempDir();
        var reportPath = Path.Combine(outDir, "mode-discrimination.json");

        var result = Arbiter.Run(
            "mode-discrimination", Arbiter.Manifest, "--out", reportPath);

        Assert.True(result.Verified, result.All);
        var report = JsonDocument.Parse(File.ReadAllText(reportPath)).RootElement;
        Assert.True(report.GetProperty("instrument_passed").GetBoolean());
        Assert.True(report.GetProperty("negative_control_detected").GetBoolean());
        Assert.True(report.GetProperty("checkpoint_negative_control_detected").GetBoolean());
        var standard = report.GetProperty("standard");
        var checkpointControl = report.GetProperty("checkpoint_negative_control");
        Assert.Equal(
            standard.GetProperty("BehavioralStateSha256").GetString(),
            checkpointControl.GetProperty("BehavioralStateSha256").GetString());
        Assert.NotEqual(
            standard.GetProperty("CheckpointSha256").GetString(),
            checkpointControl.GetProperty("CheckpointSha256").GetString());
        Assert.False(report.GetProperty("mode_established").GetBoolean());

        // Every modifier the build offers is accounted for, and none of them is left in
        // the one bucket that would keep the source mode open.
        var outcomes = report.GetProperty("modifier_outcomes").EnumerateArray().ToList();
        Assert.Equal(
            report.GetProperty("modifier_space_enumerated").GetInt32(), outcomes.Count);
        Assert.NotEmpty(outcomes);
        Assert.All(outcomes, outcome => Assert.NotEqual(
            "state_only_divergence", outcome.GetProperty("Classification").GetString()));
        Assert.Empty(report.GetProperty("unbound_modifiers").EnumerateArray());
    }

    [GameFact]
    public void BaseLibReachabilityDetectorRejectsItsInjectedAffectedCall()
    {
        var outDir = TempDir();
        var reportPath = Path.Combine(outDir, "baselib-reachability.json");

        var result = Arbiter.Run(
            "baselib-reachability", Arbiter.Manifest, Path.Combine(Arbiter.RepoRoot, "build", "parity", "BaseLib.dll"),
            "--out", reportPath);

        Assert.True(result.Verified, result.All);
        var report = JsonDocument.Parse(File.ReadAllText(reportPath)).RootElement;
        Assert.True(report.GetProperty("instrument_passed").GetBoolean());
        Assert.False(report.GetProperty("affected_branch_reached_in_history").GetBoolean());
        Assert.True(report.GetProperty("negative_control_detected").GetBoolean());
    }

    [GameFact]
    public void BaseLibReachabilityRefusesAMismatchedEnvironment()
    {
        var outDir = TempDir();
        var manifest = ManifestJson.Load(Arbiter.Manifest);
        const string wrongHash = "1234567890";
        manifest = manifest with
        {
            Environment = manifest.Environment with
            {
                ContentHash = manifest.Environment.ContentHash with { Value = wrongHash },
            },
            Source = manifest.Source with
            {
                RunSummary = manifest.Source.RunSummary! with
                {
                    ContentHash = manifest.Source.RunSummary.ContentHash with { Value = wrongHash },
                },
            },
        };
        var path = Path.Combine(outDir, "wrong-reachability-environment.json");
        ManifestJson.Save(manifest, path);
        var reportPath = Path.Combine(outDir, "baselib-reachability.json");

        var result = Arbiter.Run(
            "baselib-reachability", path,
            Path.Combine(Arbiter.RepoRoot, "build", "parity", "BaseLib.dll"),
            "--out", reportPath);

        Assert.False(result.Verified);
        Assert.Contains("matching environment preflight", result.All, StringComparison.Ordinal);
        Assert.Contains("content_hash", result.All, StringComparison.Ordinal);
        Assert.False(File.Exists(reportPath));
    }

    [GameFact]
    public void FailedReplayClearsAnEarlierCanonicalState()
    {
        var outDir = TempDir();
        var statePath = Path.Combine(outDir, "canonical.state");
        File.WriteAllText(statePath, "stale success");

        var result = Arbiter.Run(
            "replay", Path.Combine(outDir, "missing-manifest.json"), "--state-out", statePath);

        Assert.False(result.Verified);
        Assert.False(File.Exists(statePath));
    }

    [GameFact]
    public void FailedGeneratedFixtureClearsAnEarlierManifest()
    {
        var outDir = TempDir();
        var fixturePath = Path.Combine(outDir, "generated.replay.json");
        File.WriteAllText(fixturePath, "stale success");

        var result = Arbiter.RunWithEnvironment(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["STS2_PILOT_TRAINER_TEST_REQUIRED_INIT_FAILURE"] = "negative-control",
            },
            "generate-synthetic-fixture", "--out", fixturePath);

        Assert.False(result.Verified);
        Assert.False(File.Exists(fixturePath));
    }

    [GameFact]
    public void RefusesPublicationWhenRequiredEngineInitializationFails()
    {
        var outDir = TempDir();
        var result = Arbiter.RunWithEnvironment(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["STS2_PILOT_TRAINER_TEST_REQUIRED_INIT_FAILURE"] = "negative-control",
            },
            "gate", Arbiter.Manifest, "--out", outDir);

        Assert.False(result.Verified);
        Assert.Contains("Required engine initialization failed", result.All, StringComparison.Ordinal);
        var report = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(outDir, "publication-gate.json"))).RootElement;
        Assert.False(report.GetProperty("publishable").GetBoolean());
        Assert.Contains(
            report.GetProperty("conditions").EnumerateArray(),
            condition => !condition.GetProperty("passed").GetBoolean());
    }

    [GameFact]
    public void RefusesWhenTheEnvironmentDoesNotMatch()
    {
        // The cheapest way to make a condition fail without touching the history.
        // A gate that passed here would be reporting on nothing.
        var outDir = TempDir();
        var path = Path.Combine(outDir, "wrong-build.json");
        var manifest = ManifestJson.Load(Arbiter.Manifest);
        ManifestJson.Save(
            manifest with
            {
                Environment = manifest.Environment with
                {
                    BuildVersion = Fact<string>.Observed("v0.103.2", FactEvidence.AtVideoTime(1, "test")),
                },
            },
            path);

        var result = Arbiter.Run(
            "gate", path,
            "--map-observation", Arbiter.MapObservation,
            "--baselib", Path.Combine(outDir, "must-not-be-read.dll"),
            "--out", outDir);

        Assert.False(result.Verified);
        Assert.Contains("NOT PUBLISHABLE", result.Output, StringComparison.Ordinal);

        var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "publication-gate.json"))).RootElement;
        Assert.False(report.GetProperty("publishable").GetBoolean());

        var environment = report.GetProperty("conditions").EnumerateArray()
            .Single(c => c.GetProperty("name").GetString() == "environment");
        Assert.False(environment.GetProperty("passed").GetBoolean());
        Assert.False(File.Exists(Path.Combine(outDir, "baselib-reachability.json")));
        Assert.DoesNotContain("must-not-be-read.dll", result.All, StringComparison.Ordinal);
    }

    [GameFact]
    public void RecordsTheStandardItAppliedAlongsideTheVerdict()
    {
        // So an artifact can never be read as having met a weaker standard than the
        // one actually applied.
        var outDir = TempDir();
        Arbiter.Run("gate", Arbiter.Manifest, "--out", outDir);

        var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(outDir, "publication-gate.json"))).RootElement;
        var standard = report.GetProperty("standard").GetString()!;

        Assert.Contains("real-engine", standard, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No proxy is accepted", standard, StringComparison.Ordinal);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Arbiter.RepoRoot, "build", "test-scratch", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}

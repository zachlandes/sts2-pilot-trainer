using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// What can be established about the recorder on this machine without playing a run.
///
/// Three questions, and all three are about drift rather than about logic - the logic
/// is <see cref="RunCapture"/>'s and is tested without the game at all. Does this build
/// still have every member the recorder attaches to; does the set of decisions it can
/// write down still equal the set the driver can replay; and is the recorder still
/// inert while a trainer run is live. A recorder that quietly stopped seeing one kind
/// of decision would write a history missing decisions, and a history missing
/// decisions replays perfectly into a different run.
/// </summary>
public sealed class RunRecorderTests
{
    [GameFact]
    public void EveryMethodTheRecorderAttachesToExistsOnThisBuild()
    {
        // Asked of the module, because that is the thing the shell asks before it
        // installs anything: a build the recorder cannot watch completely is one it
        // declines to watch at all.
        Assert.Null(RecorderModule.Instance.Refusal);
        Assert.True(RecorderModule.Instance.Enabled);
    }

    [GameFact]
    public void TheRecorderIsOneOfTheModulesTheShellInstallsAndDrawsNothing()
    {
        Assert.Contains(RunmobileMod.Modules, module => ReferenceEquals(module, RecorderModule.Instance));
        Assert.Empty(RecorderModule.Instance.MenuCards);
    }

    /// <summary>
    /// The recorder writes down exactly the decisions the driver can replay.
    ///
    /// The two halves meet at <see cref="EngineCommands"/>: the driver calls those
    /// members to make a recorded decision and the recorder watches the same members
    /// being called. A verb one side has and the other does not is either a recording
    /// nothing can replay or a replay of a decision nothing can record, and both are
    /// silent until somebody tries.
    /// </summary>
    [GameFact]
    public void TheRecordersDecisionsAreTheOnesTheDriverCanReplay()
    {
        var mapped = EngineCommands.All.Select(command => command.Verb).ToHashSet();

        Assert.Equal(mapped.Order(), RunRecorder.RecordedVerbs.Order());
    }

    [GameFact]
    public void TheRecorderStaysOutOfTheWayOfATrainerRun()
    {
        // The barrier is raised for the whole of a trainer run, which is this mod's own
        // construction rather than the player's: recording it would publish somebody
        // else's recording back as the player's own. Checked by doing it, because the
        // interaction is between two global pieces of state and a comment asserting it
        // is not a test.
        Assert.Null(RunRecorder.Active);
        ProfileWriteBarrier.Raise();
        try
        {
            RunRecorder.NoticeRun();
            Assert.Null(RunRecorder.Active);
        }
        finally
        {
            ProfileWriteBarrier.Lower();
        }
    }

    [GameFact]
    public void RecordingIsOnUntilSomebodySaysOtherwise()
    {
        var settings = RunmobileSettings.Default;

        Assert.True(settings.RecordMyRuns);
        Assert.Equal(RunmobileSettings.Schema, settings.SchemaId);
    }

    [GameFact]
    public void ARecordingIsNamedByItsSeedAndWhenTheRunBegan()
    {
        // Both halves survive a reload, which is what lets a session continued tomorrow
        // find the journal it was being written into. Nothing in it says whose game it
        // was.
        var name = LiveRun.NameRecording(
            "SFXT47K77RFK", new DateTimeOffset(2026, 9, 5, 3, 14, 15, TimeSpan.Zero));

        Assert.Equal("native-SFXT47K77RFK-20260905-031415", name);
    }
}

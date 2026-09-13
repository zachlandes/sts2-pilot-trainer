using System.Globalization;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer.Tests;

/// <summary>
/// The recorder's row read off a capture's own state and integrity rather than facts
/// written by hand, the way <c>RecorderPresenceRow.Facts</c> reads them off
/// <c>RunRecorder.Active</c>.
///
/// <see cref="RecorderPresenceTests"/> pins the derivation over every combination of
/// facts. This holds the two writers together: what <see cref="RunCapture"/> says
/// about a run that was lost, with and without the console, is what the overlay row
/// then says about it.
/// </summary>
public sealed class RecorderPresenceLifecycleTests
{
    [Fact]
    public void ARunLostWhileRecordingReadsRecordingCompleteInGreenOnceItIsFinished()
    {
        var capture = RunCapture.Begin(Start());
        capture.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            new StateReading(Floor(1), Digest(-1)), new StateReading(Floor(1), Digest(0)));

        Assert.Equal(RecorderCopy.Recording, Row(capture).Text);
        Assert.Equal(RecorderRowTone.Overlay, Row(capture).Tone);

        capture.Finish("lost");

        var row = Row(capture);
        Assert.Equal(Presence.Drawn, row.Row.Presence);
        Assert.Equal(RecorderCopy.RecordingComplete, row.Text);
        Assert.Equal(RecorderRowTone.Positive, row.Tone);
    }

    [Fact]
    public void ARunLostAfterTheConsoleWasUsedDrawsNoRowOnceItIsFinished()
    {
        var capture = RunCapture.Begin(Start());
        capture.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            new StateReading(Floor(1), Digest(-1)), new StateReading(Floor(1), Digest(0)));
        capture.MarkNonStandard();

        // Still recording, and the row says so: the integrity is on the recording.
        Assert.Equal(RecorderCopy.Recording, Row(capture).Text);

        capture.Finish("lost");

        Assert.Equal(RunCaptureState.Finished, capture.State);
        Assert.Equal(RecorderPresence.Nothing, Row(capture));
    }

    [Fact]
    public void ARunWhoseWatchBrokeReadsRecordingStoppedInRedAndStaysStoppedWhenItEnds()
    {
        var capture = RunCapture.Begin(Start());
        capture.MarkBroken("A decision went by unread.");

        Assert.Equal(RecorderCopy.RecordingStopped, Row(capture).Text);
        Assert.Equal(RecorderRowTone.Warning, Row(capture).Tone);

        capture.Finish("lost");

        Assert.Equal(RecorderCopy.RecordingStopped, Row(capture).Text);
        Assert.Equal(RecorderRowTone.Warning, Row(capture).Tone);
    }

    /// <summary>The same three readings <c>RecorderPresenceRow.Facts</c> takes.</summary>
    private static RecorderPresence Row(RunCapture capture) =>
        RecorderPresence.For(new RecorderFacts(true, capture.State, capture.Integrity));

    private static RunRecordingStart Start() => new()
    {
        RunId = "native-SFXT47K77RFK-20260905-030000",
        RecorderVersion = "runmobile-recorder/0.1.0",
        Identity = new RunIdentityReading
        {
            BuildVersion = "v0.111.0",
            BuildDateUtc = "2026.08.14",
            ContentHash = "1568834832",
            GameMode = "standard",
            Seed = "SFXT47K77RFK",
            Ascension = 10,
            Character = "CHARACTER.IRONCLAD",
            Acts = ["ACT.UNDERDOCKS"],
            Unlocks = new UnlockStateInventory
            {
                Epochs = ["EPOCH.ONE"],
                EncountersSeen = ["ENCOUNTER.TEST"],
                Runs = 137,
            },
            Mods = ModEnvironment.AsRecorded(
                [new LocalMod("Runmobile", "Runmobile", "0.1.0", AffectsGameplay: false, "Loaded")],
                new PatchRoster
                {
                    Members =
                    [
                        new PatchedMember(
                            "MegaCrit.Sts2.Core.Saving.ProgressSaveManager", "SaveProgressFile()",
                            [PatchRoster.HostOwnerId], Prefixes: 1, Postfixes: 0, Transpilers: 0, Finalizers: 0),
                    ],
                }),
        },
        State = Floor(1),
        Digest = Digest(-1),
        RunClockMs = 0,
    };

    private static IReadOnlyDictionary<string, string> Floor(int floor) => new Dictionary<string, string>(
        StringComparer.Ordinal)
    {
        ["combat.in_progress"] = "false",
        ["combat.outcome"] = "none",
        ["run.total_floor"] = floor.ToString(CultureInfo.InvariantCulture),
        ["run.map_coord"] = $"r{floor.ToString(CultureInfo.InvariantCulture)}c3",
        ["run.act_floor"] = floor.ToString(CultureInfo.InvariantCulture),
        ["player.hp"] = "68",
        ["player.max_hp"] = "68",
    };

    private static string Digest(int seq) =>
        "sha256:" + (seq + 1).ToString("x2", CultureInfo.InvariantCulture).PadLeft(64, 'a');

    private static IReadOnlyDictionary<string, string> Args(params (string Key, string Value)[] args) =>
        args.ToDictionary(arg => arg.Key, arg => arg.Value, StringComparer.Ordinal);
}

using System.Globalization;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The two things a recorder's refusal can be, told apart on the recording it writes.
///
/// A hole in the watch - a decision that went by unread - breaks the recording:
/// continuity <c>broken</c>, integrity untouched. A decision the recorder saw and could
/// not name stops it: integrity <c>unmapped</c>, continuity untouched, and what was
/// met written down raw. Every refusal used to be the first, so a recorder that met a
/// reward this format has no verb for reported a watch that stopped and started
/// again, which is not what happened, and the validator's sentence said so in those
/// words. Driven here through the same two entry points the recorder's patches reach,
/// against a store in a temporary directory, with the game loaded because the recorder
/// is a type of the mod.
/// </summary>
public sealed class RunRecorderStopTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"runmobile-stop-{Guid.NewGuid():N}", "Runmobile", "steam", "test", "profile1");

    public RunRecorderStopTests()
    {
        Directory.CreateDirectory(_root);
        RunmobileStore.UseRootForTesting(_root);
    }

    public void Dispose()
    {
        RunmobileStore.UseRootForTesting(null);
        var sandbox = _root[.._root.IndexOf("Runmobile", StringComparison.Ordinal)];
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
    }

    [GameFact]
    public void ADecisionSeenAndNotNamedStopsTheRecordingWithItsWatchIntact()
    {
        var (recorder, capture, journalPath) = Recording();
        var seq = capture.NextSeq;

        recorder.StopAt(
            RunRecorder.MetAtMember(
                typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SelectLocalReward), "mystery",
                "This format has no verb for that kind of reward.",
                ("reward", "MysteryReward")),
            Reading(Floor(2), Digest(1), 4200));

        Assert.Equal(NativeSource.UnmappedIntegrity, capture.Integrity);
        Assert.Equal(NativeSource.ContinuousContinuity, capture.Continuity);
        Assert.Equal(RunCaptureState.Unmapped, capture.State);
        Assert.Empty(capture.Refusals);

        var stop = Assert.IsType<JournalStop>(capture.Stop);
        Assert.Equal(seq, stop.Decision.Seq);
        Assert.Equal(UnmappedDecision.MemberSeam, stop.Decision.Seam);
        Assert.Equal("RewardsSetSynchronizer.SelectLocalReward", stop.Decision.Name);
        Assert.Equal("mystery", stop.Decision.Discriminator);
        Assert.Equal("MysteryReward", stop.Decision.Args["reward"]);
        Assert.Equal(seq, stop.Decision.Evidence.ActionOrdinal);
        Assert.Equal(4200, stop.Decision.Evidence.RunClockMs);
        Assert.Equal("This format has no verb for that kind of reward.", stop.Decision.Evidence.Note);
        Assert.Equal(Digest(1), stop.BeforeDigest);

        // On the file as well as held, so a session continued from the journal stops
        // where this one did rather than recording past it.
        var journal = RunJournal.Parse(RunmobileStore.Read(journalPath)!);
        Assert.NotNull(journal.Stop);
        Assert.Equal(seq, journal.Stop!.Decision.Seq);
        Assert.Empty(journal.Refusals);
    }

    [GameFact]
    public void AHoleInTheWatchBreaksTheRecordingWithoutStoppingIt()
    {
        var (recorder, capture, journalPath) = Recording();

        recorder.Refuse("A MapMove could not be read: the engine never settled.");

        Assert.Equal(NativeSource.BrokenContinuity, capture.Continuity);
        Assert.Equal(NativeSource.CompleteIntegrity, capture.Integrity);
        Assert.Equal(RunCaptureState.Broken, capture.State);
        Assert.Null(capture.Stop);

        var journal = RunJournal.Parse(RunmobileStore.Read(journalPath)!);
        Assert.Null(journal.Stop);
        var refusal = Assert.Single(journal.Refusals);
        Assert.Contains("never settled", refusal.Reason, StringComparison.Ordinal);
    }

    /// <summary>
    /// The validator's sentence is the reader's account of what happened, and the two
    /// recordings get different ones: the stop names the decision the recorder met, and
    /// only the hole says the recorder stopped and started again.
    /// </summary>
    [GameFact]
    public void TheValidatorNamesTheDecisionForAStopAndTheHoleForABreak()
    {
        var (stopped, stoppedCapture, _) = Recording();
        stopped.StopAt(
            RunRecorder.MetAtMember(
                typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SelectLocalReward), "mystery",
                "This format has no verb for that kind of reward.",
                ("reward", "MysteryReward")),
            Reading(Floor(2), Digest(1), 4200));
        stoppedCapture.Finish("abandoned");
        var stopProblems = ManifestValidator.Validate(stoppedCapture.ToManifest()).Describe();

        Assert.Contains("source.native.integrity is 'unmapped'", stopProblems, StringComparison.Ordinal);
        Assert.Contains(
            "The recorder met: member RewardsSetSynchronizer.SelectLocalReward (mystery) with reward=MysteryReward",
            stopProblems, StringComparison.Ordinal);
        Assert.DoesNotContain("stopped and started again", stopProblems, StringComparison.Ordinal);

        var (broken, brokenCapture, _) = Recording();
        broken.Refuse("A MapMove could not be read: the engine never settled.");
        brokenCapture.Finish("abandoned");
        var breakProblems = ManifestValidator.Validate(brokenCapture.ToManifest()).Describe();

        Assert.Contains("stopped and started again", breakProblems, StringComparison.Ordinal);
        Assert.DoesNotContain("The recorder met", breakProblems, StringComparison.Ordinal);
    }

    /// <summary>
    /// Past a stop nothing is recorded, so nothing past it can go unrecorded: a
    /// refusal raised there would claim a hole in a watch that is not watching.
    /// </summary>
    [GameFact]
    public void ARefusalAfterAStopChangesNothing()
    {
        var (recorder, capture, journalPath) = Recording();
        recorder.StopAt(
            RunRecorder.MetAtMember(
                typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper), "MerchantOddEntry",
                "The entry is not on any shelf this recorder knows."),
            Reading(Floor(2), Digest(1), 4200));
        var written = RunmobileStore.Read(journalPath);

        recorder.Refuse("A MapMove could not be read: the engine never settled.");

        Assert.Equal(NativeSource.ContinuousContinuity, capture.Continuity);
        Assert.Equal(NativeSource.UnmappedIntegrity, capture.Integrity);
        Assert.Empty(capture.Refusals);
        Assert.Equal(written, RunmobileStore.Read(journalPath));
    }

    [GameFact]
    public void ARecordingStopsOnceAtTheFirstDecisionItCouldNotName()
    {
        var (recorder, capture, _) = Recording();
        recorder.StopAt(
            RunRecorder.MetAtMember(
                typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper), "MerchantOddEntry",
                "The entry is not on any shelf this recorder knows."),
            Reading(Floor(2), Digest(1), 4200));

        recorder.StopAt(
            RunRecorder.MetAtMember(
                typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SelectLocalReward), "mystery",
                "This format has no verb for that kind of reward."),
            Reading(Floor(2), Digest(1), 4300));

        Assert.Equal("MerchantEntry.OnTryPurchaseWrapper", capture.Stop!.Decision.Name);
        Assert.Equal(NativeSource.ContinuousContinuity, capture.Continuity);
    }

    /// <summary>
    /// A screen is answered from inside the decision that opened it, so a stop held
    /// beside the screen's answers stands at that decision's ordinal and with its
    /// before-reading, and the decision is not recorded: written without its answer it
    /// would be one a replay makes differently.
    /// </summary>
    [GameFact]
    public void AStopHeldBesideAScreensAnswersStopsTheDecisionThatOpenedTheScreen()
    {
        var (recorder, capture, _) = Recording();
        var seq = capture.NextSeq;
        var before = Reading(Floor(2), Digest(1), 4200);

        recorder.HoldScreenAnswerStop(RunRecorder.MetAtScreen(
            "NCardGridSelectionScreen", null,
            "The card is not one of the cards the screen offered.",
            ("card_id", "CARD.STRANGE"), ("offered", "3")));
        recorder.Commit(
            nameof(ActionVerb.ChooseEventOption),
            Args(("event_id", "EVENT.TEST"), ("option_index", "0"), ("option_key", "OPTION.TEST")),
            before,
            Reading(Floor(2, hp: 60), Digest(2), 4600));

        var stop = Assert.IsType<JournalStop>(capture.Stop);
        Assert.Equal(seq, capture.NextSeq);
        Assert.Equal(seq, stop.Decision.Seq);
        Assert.Equal(UnmappedDecision.PlayerChoiceSeam, stop.Decision.Seam);
        Assert.Equal("NCardGridSelectionScreen", stop.Decision.Name);
        Assert.Equal("CARD.STRANGE", stop.Decision.Args["card_id"]);
        Assert.Equal(Digest(1), stop.BeforeDigest);
        Assert.Equal(NativeSource.ContinuousContinuity, capture.Continuity);
    }

    // ── Fixtures ─────────────────────────────────────────────────────────────────

    /// <summary>A recorder over a capture one decision in, with its journal on the
    /// store the way <c>Attach</c> leaves it.</summary>
    private static (RunRecorder Recorder, RunCapture Capture, string JournalPath) Recording()
    {
        var capture = RunCapture.Begin(Start());
        capture.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            new StateReading(Floor(1), Digest(-1)), new StateReading(Floor(2), Digest(1)), runClockMs: 1000);

        var journalPath = $"{RunRecorder.RecordingsDirectory}/{capture.RunId}{RunJournal.FileExtension}";
        RunmobileStore.Write(journalPath, capture.Journal.Render());
        return (new RunRecorder(capture, journalPath), capture, journalPath);
    }

    private static RunRecorder.TakenReading Reading(
        IReadOnlyDictionary<string, string> sample, string digest, int clock) => new(sample, digest, clock);

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
                HostRoster()),
        },
        State = Floor(1),
        Digest = Digest(-1),
        RunClockMs = 0,
    };

    /// <summary>A roster shaped like the real one: the shell patches the profile write
    /// and the members it watches, and a roster with none of ours on it is the broken
    /// reading the preflight refuses.</summary>
    private static PatchRoster HostRoster() => new()
    {
        Members =
        [
            new PatchedMember(
                "MegaCrit.Sts2.Core.Saving.ProgressSaveManager", "SaveProgressFile()", [PatchRoster.HostOwnerId],
                Prefixes: 1, Postfixes: 0, Transpilers: 0, Finalizers: 0),
            new PatchedMember(
                "MegaCrit.Sts2.Core.Run.RunManager", "StartNewSingleplayerRun(RunSetup, Boolean)",
                [PatchRoster.HostOwnerId], Prefixes: 1, Postfixes: 0, Transpilers: 0, Finalizers: 0),
        ],
    };

    private static IReadOnlyDictionary<string, string> Floor(int floor, int hp = 68) => new Dictionary<string, string>(
        StringComparer.Ordinal)
    {
        ["combat.in_progress"] = "false",
        ["combat.outcome"] = "none",
        ["run.total_floor"] = floor.ToString(CultureInfo.InvariantCulture),
        ["run.map_coord"] = $"r{floor.ToString(CultureInfo.InvariantCulture)}c3",
        ["run.act_floor"] = floor.ToString(CultureInfo.InvariantCulture),
        ["player.hp"] = hp.ToString(CultureInfo.InvariantCulture),
        ["player.max_hp"] = "68",
    };

    private static string Digest(int seq) =>
        "sha256:" + (seq + 1).ToString("x2", CultureInfo.InvariantCulture).PadLeft(64, 'a');

    private static IReadOnlyDictionary<string, string> Args(params (string Key, string Value)[] args) =>
        args.ToDictionary(arg => arg.Key, arg => arg.Value, StringComparer.Ordinal);
}

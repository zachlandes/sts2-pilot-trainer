using System.Globalization;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Mod;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The Headbutt break, turned into the equivalence it violated: a prompt a played
/// card opens inside a fight is recorded at the position a fresh replay consumes, and
/// the replay ends in the state the recording did.
///
/// Headbutt broke a normal run in ordinary play. Its prompt is over the discard, and
/// the screen the client draws for it keeps an empty list of its own, so a recorder
/// reading the screen refused every answer as "not one of the 0 cards it offered" and
/// the recording ended broken. What is held here is the whole of what should have
/// happened instead, in one process and without a screen: the engine takes the retail
/// path - no selector on its stack, the action paused for the player, the list read
/// after the pause - and answers from the local seam the way a player's client fills
/// it; <see cref="CardPrompts"/> derives the list at the pause; the recorder writes the
/// answer as <c>SelectCardFromScreen</c>; and a second run of the same seed, staged
/// the same way, replays that answer through the arbiter's own driver and arrives at
/// the same complete digest.
///
/// A harness rather than a fixture, and labelled as one: the card is put in the hand
/// with the engine's own command at fight start, which no natural history does, so
/// the recording could not validate as a run. What it proves is the capture and the
/// replay agreeing about one prompt shape, which is exactly what the screen read could
/// not. It does not hold that a natural run through such a prompt finishes
/// <c>continuity = continuous</c> and <c>integrity = complete</c>; that proof is a
/// swept-seed fixture asserting both end to end, and lives in the slice-6 headless
/// gameplay integration fixtures rather than here.
/// </summary>
public sealed class CardPromptCaptureTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"runmobile-prompt-{Guid.NewGuid():N}", "Runmobile", "steam", "test", "profile1");

    public CardPromptCaptureTests()
    {
        _ = EngineHost.StartupPhase();
        Directory.CreateDirectory(_root);
        RunmobileStore.UseRootForTesting(_root);
    }

    public void Dispose()
    {
        RunmobileStore.UseRootForTesting(null);
        var sandbox = _root[.._root.IndexOf("Runmobile", StringComparison.Ordinal)];
        if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
    }

    /// <summary>Headbutt: a prompt over the discard pile, answered with a card that
    /// then goes to the top of the draw pile.</summary>
    [GameFact]
    public void AHeadbuttPromptIsRecordedWhereTheReplayConsumesIt() =>
        CaptureAndReplay(
            () => ModelDb.Card<Headbutt>(),
            stage: combat =>
            {
                Discard(combat.Hand.Cards[0]);
                Discard(combat.Hand.Cards[0]);
                Discard(combat.Hand.Cards[0]);
            },
            chooses: offered => offered[^1],
            leaves: (combat, chosen) => Assert.Same(chosen, combat.DrawPile.Cards[0]));

    /// <summary>Burning Pact: a prompt over the hand, the other shape the screen read
    /// never saw at all, answered with a card that is exhausted.</summary>
    [GameFact]
    public void ABurningPactPromptIsRecordedWhereTheReplayConsumesIt() =>
        CaptureAndReplay(
            () => ModelDb.Card<BurningPact>(),
            stage: _ => { },
            chooses: offered => offered[^1],
            leaves: (combat, chosen) => Assert.Contains(chosen, combat.ExhaustPile.Cards));

    // ── The harness ──────────────────────────────────────────────────────────────

    /// <summary>What one run wrote down about the prompt, and where it ended.</summary>
    private sealed record Captured(
        int HandIndex, IReadOnlyList<ActionRecord> Answers, string DigestAfter, string ChosenId);

    /// <summary>
    /// Two runs of the probe seed. The first plays the card through the retail path and
    /// records the prompt's answer; the second replays that answer through the driver.
    /// </summary>
    private static void CaptureAndReplay(
        Func<CardModel> card,
        Action<PlayerCombatState> stage,
        Func<IReadOnlyList<CardModel>, CardModel> chooses,
        Action<PlayerCombatState, CardModel> leaves)
    {
        var captured = Capture(card, stage, chooses, leaves);

        Assert.Single(captured.Answers);
        var answer = captured.Answers[0];
        Assert.Equal(ActionVerb.SelectCardFromScreen, answer.Verb);
        Assert.Equal(captured.ChosenId, answer.Args["card_id"]);

        var replayed = Replay(card, stage, captured, leaves);

        Assert.Equal(captured.DigestAfter, replayed);
    }

    /// <summary>
    /// The retail path, headlessly: the driver walks the run into its first fight and
    /// is then put away, so nothing of its own is on the engine's selector stack; a
    /// selector on the local stack fills the seam the player's client fills; the card
    /// is played through the engine's own action. The prompt's answer reaches the
    /// recorder the way it does in the client - through <see cref="CardPrompts"/> - and
    /// the decision it followed is committed with it.
    /// </summary>
    private static Captured Capture(
        Func<CardModel> dealt,
        Action<PlayerCombatState> stage,
        Func<IReadOnlyList<CardModel>, CardModel> chooses,
        Action<PlayerCombatState, CardModel> leaves)
    {
        Captured? captured = null;
        HeadlessRuns.WithARun(session =>
        {
            var player = session.RunState.Players[0];
            using (var driver = new RunDriver(session))
            {
                HeadlessRuns.EnterTheFirstFight(driver, session);
            }

            var combat = player.PlayerCombatState!;
            var card = Deal(dealt(), player);
            stage(combat);
            var handIndex = combat.Hand.Cards.ToList().IndexOf(card);
            Assert.True(handIndex >= 0, $"{card.Id} is not in the hand");

            var (recorder, capture) = Recording();
            using var watching = recorder;
            var seam = new LocalSeam(chooses);
            CardPrompts.Prompt? prompt = null;
            IReadOnlyList<CardModel>? chosen = null;

            using (Patched(() =>
                   {
                       CardPrompts.Answered = (asked, answer) =>
                       {
                           prompt = asked;
                           chosen = answer;
                           recorder.HoldCardPromptAnswers(asked, answer);
                       };
                   }))
            using (CardSelectCmd.PushSelector(seam, localOnly: true))
            {
                Assert.Null(CardSelectCmd.Selector);

                var before = Reading();
                RunManager.Instance.ActionQueueSet.EnqueueWithoutSynchronizing(new PlayCardAction(card, Target(card)));
                Pump.Drain();

                Assert.NotNull(prompt);
                Assert.Equal(CardPrompts.PromptState.Offered, prompt!.State);
                Assert.True(seam.PausedBeforeAsking, "the engine did not pause the action before reading the pile");
                Assert.Equal(seam.Handed, prompt.Offered);
                Assert.Same(seam.Chose, Assert.Single(chosen!));

                recorder.Commit(nameof(ActionVerb.PlayCard), PlayArgs(card, handIndex), before);
            }

            leaves(combat, seam.Chose!);
            capture.Finish("abandoned");
            var actions = capture.ToManifest().Actions;
            var play = actions.Last(action => action.Verb == ActionVerb.PlayCard);
            captured = new Captured(
                handIndex,
                actions.Where(action => action.Seq > play.Seq).ToList(),
                LiveRun.Read().Digest,
                seam.Chose!.Id.ToString());
        });

        return captured!;
    }

    /// <summary>The arbiter's own replay of the recorded answer, in a fresh run of the
    /// same seed staged the same way.</summary>
    private static string Replay(
        Func<CardModel> dealt, Action<PlayerCombatState> stage, Captured captured,
        Action<PlayerCombatState, CardModel> leaves)
    {
        string? digest = null;
        HeadlessRuns.WithARun(session =>
        {
            var player = session.RunState.Players[0];
            using var driver = new RunDriver(session);
            HeadlessRuns.EnterTheFirstFight(driver, session);

            var combat = player.PlayerCombatState!;
            var card = Deal(dealt(), player);
            stage(combat);
            Assert.Equal(captured.HandIndex, combat.Hand.Cards.ToList().IndexOf(card));

            var play = HeadlessRuns.Record(10, ActionVerb.PlayCard) with { Args = PlayArgs(card, captured.HandIndex) };
            var upcoming = captured.Answers
                .Select((answer, offset) => answer with { Seq = 11 + offset })
                .ToList();

            driver.Apply(play, upcoming);
            foreach (var answer in upcoming) driver.Apply(answer);

            var chosen = combat.AllPiles.SelectMany(pile => pile.Cards)
                .First(candidate => candidate.Id.ToString() == captured.ChosenId);
            leaves(combat, chosen);
            digest = LiveRun.Read().Digest;
        });

        return digest!;
    }

    /// <summary>Puts one generated card in the hand the way a card that creates one
    /// does: created into the combat state, then added through the engine's command.</summary>
    private static CardModel Deal(CardModel canonical, Player player)
    {
        var card = CombatManager.Instance!.DebugOnlyGetState()!.CreateCard(canonical, player);
        CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player).GetAwaiter().GetResult();
        Pump.Drain();
        return card;
    }

    private static void Discard(CardModel card)
    {
        CardPileCmd.Add(card, PileType.Discard).GetAwaiter().GetResult();
        Pump.Drain();
    }

    /// <summary>The first living enemy for a card that targets one, and nothing for a
    /// card that does not - which is the driver's own rule for a recorded target.</summary>
    private static Creature? Target(CardModel card) =>
        card.TargetType == TargetType.AnyEnemy
            ? CombatManager.Instance!.DebugOnlyGetState()!.Enemies.First(enemy => enemy is { IsAlive: true })
            : null;

    /// <summary>The play as the recorder writes it and the driver reads it.</summary>
    private static IReadOnlyDictionary<string, string> PlayArgs(CardModel card, int handIndex)
    {
        var args = new List<(string, string)>
        {
            ("card_id", card.Id.ToString()),
            ("hand_index", HeadlessRuns.Number(handIndex)),
        };
        if (card.TargetType == TargetType.AnyEnemy) args.Add(("target_index", "0"));
        return Args([.. args]);
    }

    /// <summary>The shell's prompt patches, installed for the block.</summary>
    private static IDisposable Patched(Action subscribe)
    {
        var harmony = new Harmony($"sts2-pilot-trainer.card-prompt-capture.{Guid.NewGuid():N}");
        var previous = CardPrompts.Answered;
        foreach (var patchClass in CardPrompts.PatchClasses) harmony.CreateClassProcessor(patchClass).Patch();
        CardPrompts.Forget();
        subscribe();
        return new Unpatches(harmony, previous);
    }

    private sealed class Unpatches(Harmony harmony, Action<CardPrompts.Prompt, IReadOnlyList<CardModel>>? previous)
        : IDisposable
    {
        public void Dispose()
        {
            CardPrompts.Answered = previous;
            CardPrompts.Forget();
            harmony.UnpatchAll(harmony.Id);
        }
    }

    /// <summary>
    /// The seam a player's client fills, on the local stack: the engine reaches it only
    /// after it has paused the action for the choice, which is the moment this records.
    /// </summary>
    private sealed class LocalSeam(Func<IReadOnlyList<CardModel>, CardModel> chooses) : ICardSelector
    {
        internal IReadOnlyList<CardModel>? Handed { get; private set; }

        internal CardModel? Chose { get; private set; }

        internal bool PausedBeforeAsking { get; private set; }

        public Task<IEnumerable<CardModel>> GetSelectedCards(
            IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            Handed = options.ToList();
            PausedBeforeAsking = RunManager.Instance.ActionExecutor.CurrentlyRunningAction is
                { State: GameActionState.GatheringPlayerChoice };
            Chose = chooses(Handed);
            return Task.FromResult<IEnumerable<CardModel>>([Chose]);
        }

        public CardRewardSelection GetSelectedCardReward(
            IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives) =>
            throw new NotSupportedException("No card reward is asked here.");
    }

    // ── A recorder to hold the answer, over a capture one decision in ────────────

    private static (RunRecorder Recorder, RunCapture Capture) Recording()
    {
        var capture = RunCapture.Begin(Start());
        capture.Record(
            ActionVerb.ChooseNeowBlessing, Args(("option_index", "0"), ("option_key", "NEOW.BLESSING")),
            new StateReading(Floor(1), Digest(-1)), new StateReading(Floor(2), Digest(1)), runClockMs: 1000);

        var journalPath = $"{RunRecorder.RecordingsDirectory}/{capture.RunId}{RunJournal.FileExtension}";
        RunmobileStore.Write(journalPath, capture.Journal.Render());
        return (new RunRecorder(capture, journalPath), capture);
    }

    private static RunRecorder.TakenReading Reading()
    {
        var (sample, digest) = LiveRun.Read();
        return new RunRecorder.TakenReading(sample, digest, LiveRun.RunClockMs());
    }

    private static RunRecordingStart Start() => new()
    {
        RunId = $"native-{HeadlessRuns.Seed}-20260913-120000",
        RecorderVersion = "runmobile-recorder/0.1.0",
        Identity = new RunIdentityReading
        {
            BuildVersion = "v0.111.0",
            BuildDateUtc = "2026.08.14",
            ContentHash = "1568834832",
            GameMode = "standard",
            Seed = HeadlessRuns.Seed,
            Ascension = 0,
            Character = "CHARACTER.IRONCLAD",
            Acts = ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"],
            Unlocks = new UnlockStateInventory
            {
                Epochs = ["EPOCH.ONE"],
                EncountersSeen = ["ENCOUNTER.TEST"],
                Runs = 1,
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
        ["run.total_floor"] = floor.ToString(CultureInfo.InvariantCulture),
        ["player.hp"] = "68",
    };

    private static string Digest(int n) => $"digest-{n.ToString(CultureInfo.InvariantCulture)}";

    private static IReadOnlyDictionary<string, string> Args(params (string Key, string Value)[] args) =>
        new SortedDictionary<string, string>(
            args.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal);
}

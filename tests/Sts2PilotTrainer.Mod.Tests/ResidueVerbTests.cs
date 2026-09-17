using System.Globalization;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The verbs format v6 added, measured against the real engine, in this process.
///
/// No committed history passes through any of them, and a fixture that would need a
/// run holding Scroll Boxes, Pael's Wing or a second-act event is a run no generated
/// journey reaches. So each is exercised where it lives: a run is started, the prompt
/// is asked the way the engine asks it, and what the engine did with the answer is
/// read off the engine. Every refusal asserted is the driver's own sentence or the
/// selector's, so a sentence that changes fails here.
/// </summary>
public sealed class ResidueVerbTests
{
    // ── EndTurn through the action, and the undo the client never offers ──

    /// <summary>
    /// The turn ends through the same action the end-turn button enqueues, and the
    /// undo is refused by measurement: the client offers it only while another player
    /// has not ended their turn, so no singleplayer run reaches the window.
    /// </summary>
    [GameFact]
    public void AnEndedTurnGoesThroughTheActionAndTheUndoIsRefusedOnThisBuild() => HeadlessRuns.WithARun(session =>
    {
        using var driver = new RunDriver(session);
        HeadlessRuns.EnterTheFirstFight(driver, session);

        Assert.Equal("1", HeadlessRuns.Field(session, "combat.turn"));

        var undo = HeadlessRuns.Record(2, ActionVerb.UndoEndTurn);
        driver.Apply(HeadlessRuns.Record(1, ActionVerb.EndTurn), [undo]);

        Assert.Equal("2", HeadlessRuns.Field(session, "combat.turn"));
        Assert.Equal("true", HeadlessRuns.Field(session, "combat.in_progress"));

        var refusal = Assert.Throws<EngineException>(() => driver.Apply(undo));
        Assert.Contains("no singleplayer run on v0.111.0 can", refusal.Message, StringComparison.Ordinal);
    });

    // ── The bundle screen Scroll Boxes opens ──────────────────────────────

    /// <summary>Obtaining Scroll Boxes asks which bundle; the stand-in hands the
    /// question to the answerer, and the engine adds the chosen bundle's cards.</summary>
    [GameFact]
    public void ABundleScreenIsAnsweredFromTheManifestAndRefusedWhereItIsWrong() => HeadlessRuns.WithARun(session =>
    {
        var player = session.RunState.Players[0];
        using (var driver = new RunDriver(session))
        {
            driver.EnterFirstRoom();
            var answerer = new Answering();
            answerer.Saw = bundles => answerer.Selector.Enqueue(new ManifestCardSelector.BundlePick(
                1, ManifestCardSelector.BundleIds(bundles[^1]), bundles.Count - 1));

            var deckBefore = player.Deck.Cards.Count;
            using (Answers(answerer))
            {
                RelicCmd.Obtain(ModelDb.Relic<ScrollBoxes>().ToMutable(), player).GetAwaiter().GetResult();
                Pump.Drain();
            }

            // The prompt was asked, and what came back is the bundle the answer named:
            // the deck grew by exactly that bundle's cards.
            var offered = answerer.Bundles;
            Assert.NotNull(offered);
            Assert.True(offered.Count > 1, $"the prompt offered {offered.Count} bundle(s)");
            Assert.Null(answerer.Selector.Refusal);
            Assert.Equal(offered[^1].Count, player.Deck.Cards.Count - deckBefore);
        }

        // The three refusals, each on a selector of its own because the first refusal
        // is the one kept.
        var bundles = TwoBundles(player);

        Assert.Contains(
            "takes bundle 5, but this screen offers 2",
            Refused(selector =>
            {
                selector.Enqueue(new ManifestCardSelector.BundlePick(
                    1, ManifestCardSelector.BundleIds(bundles[0]), 5));
                selector.GetSelectedBundle(bundles);
            }),
            StringComparison.Ordinal);

        Assert.Contains(
            "expects bundle CARD.NOT_OFFERED,CARD.NOR_THIS at bundle option 0",
            Refused(selector =>
            {
                selector.Enqueue(new ManifestCardSelector.BundlePick(1, "CARD.NOT_OFFERED,CARD.NOR_THIS", 0));
                selector.GetSelectedBundle(bundles);
            }),
            StringComparison.Ordinal);

        Assert.Contains(
            "asked which of its 2 bundle(s) was taken and the manifest does not say",
            Refused(selector => selector.GetSelectedBundle(bundles)),
            StringComparison.Ordinal);
    });

    // ── The relic screen nothing on this build opens ──────────────────────

    /// <summary>The relic screen answers through the same stand-in, and a history
    /// that records one on this build meets the no-caller sentence.</summary>
    [GameFact]
    public void ARelicScreenIsAnsweredFromTheManifestAndNoActionOnThisBuildOpensOne() => HeadlessRuns.WithARun(session =>
    {
        var player = session.RunState.Players[0];
        var relics = ModelDb.AllRelics.Take(3).ToList();

        using (var driver = new RunDriver(session))
        {
            driver.EnterFirstRoom();
            var answerer = new Answering();
            answerer.Selector.Enqueue(new ManifestCardSelector.RelicPick(1, relics[1].Id.ToString(), 1));

            using (Answers(answerer))
            {
                var picked = RelicSelectCmd.FromChooseARelicScreen(player, relics).GetAwaiter().GetResult();

                Assert.Same(relics[1], picked);
                Assert.Null(answerer.Selector.Refusal);
            }
        }

        Assert.Contains(
            "takes relic 9 off a screen, but this screen offers 3",
            Refused(selector =>
            {
                selector.Enqueue(new ManifestCardSelector.RelicPick(1, relics[0].Id.ToString(), 9));
                selector.GetSelectedRelic(relics);
            }),
            StringComparison.Ordinal);

        Assert.Contains(
            "asked which of its 3 relic(s) was taken and the manifest does not say",
            Refused(selector => selector.GetSelectedRelic(relics)),
            StringComparison.Ordinal);

        // What a history that records the verb meets on this build: no action opens
        // the screen, so the answer is one no screen consumed.
        using (var driver = new RunDriver(session))
        {
            var refusal = Assert.Throws<EngineException>(() => driver.Apply(HeadlessRuns.Record(
                1, ActionVerb.SelectRelicFromScreen, ("relic_id", "RELIC.ANCHOR"), ("option_index", "0"))));

            Assert.Contains("answers a relic screen no action opened", refusal.Message, StringComparison.Ordinal);
            Assert.Contains(
                "No caller reaches RelicSelectCmd.FromChooseARelicScreen on v0.111.0",
                refusal.Message,
                StringComparison.Ordinal);
        }
    });

    // ── A card reward answered past its cards ─────────────────────────────

    /// <summary>A card reward answered past its cards comes back through the same
    /// seam a card does, named by the alternative's own id. The alternatives are the
    /// ones the engine generates for a reward the player holds Pael's Wing over -
    /// Skip, and the wing's sacrifice - never a list written here.</summary>
    [GameFact]
    public void ACardRewardsAlternativeIsAnsweredThroughTheSeamAndCheckedByIdAndPosition() => HeadlessRuns.WithARun(session =>
    {
        var player = session.RunState.Players[0];
        RelicCmd.Obtain(ModelDb.Relic<PaelsWing>().ToMutable(), player).GetAwaiter().GetResult();
        var reward = ACardReward(player);
        var options = OfferedCards(reward);
        var alternatives = CardRewardAlternative.Generate(reward);
        Assert.Equal(["Skip", "SACRIFICE"], alternatives.Select(alternative => alternative.OptionId));

        var selector = new ManifestCardSelector();
        selector.Enqueue(new ManifestCardSelector.AlternativePick(1, "SACRIFICE", 4));
        var selection = selector.GetSelectedCardReward(options, alternatives);

        Assert.Same(alternatives[1], selection.alternative);
        Assert.Null(selection.card);
        Assert.Null(selector.Refusal);

        Assert.Contains(
            "with alternative 'REROLL', and this reward offers 0:Skip, 1:SACRIFICE",
            Refused(refusing =>
            {
                refusing.Enqueue(new ManifestCardSelector.AlternativePick(1, "REROLL", 4));
                refusing.GetSelectedCardReward(options, alternatives);
            }),
            StringComparison.Ordinal);

        Assert.Contains(
            "at option 7, and this screen reports it at 4",
            Refused(refusing =>
            {
                refusing.Enqueue(new ManifestCardSelector.AlternativePick(1, "SACRIFICE", 7));
                refusing.GetSelectedCardReward(options, alternatives);
            }),
            StringComparison.Ordinal);
    });

    /// <summary>
    /// What an alternative does to the reward is read off the alternative, as the
    /// engine reads it. The loot screen's Skip ends the selection and leaves the reward
    /// unclaimed, and is answered; the reroll keeps the selection open for an answer the
    /// recorder never writes, and is refused by name with nothing answered, which is
    /// the engine's own "declined" path out of the selection.
    /// </summary>
    [GameFact]
    public void AnAlternativesEffectOnTheRewardIsTheAlternativesOwn() => HeadlessRuns.WithARun(session =>
    {
        var player = session.RunState.Players[0];
        // Driftwood is what lets a card reward be rerolled on this build; the reward's
        // own generator then offers the reroll after the skip
        var driftwood = RelicCmd.Obtain(ModelDb.Relic<Driftwood>().ToMutable(), player).GetAwaiter().GetResult();
        var reward = ACardReward(player);
        Assert.True(driftwood.TryModifyRewardsLate(player, [reward], room: null));
        var options = OfferedCards(reward);
        var alternatives = CardRewardAlternative.Generate(reward);
        Assert.Equal(["Skip", "REROLL"], alternatives.Select(alternative => alternative.OptionId));

        var selector = new ManifestCardSelector();
        selector.Enqueue(new ManifestCardSelector.AlternativePick(1, "Skip", 3));
        var skipped = selector.GetSelectedCardReward(options, alternatives);

        Assert.Same(alternatives[0], skipped.alternative);
        Assert.Same(alternatives[0], selector.AnsweredAlternative);
        Assert.Null(selector.Refusal);

        var refusing = new ManifestCardSelector();
        refusing.Enqueue(new ManifestCardSelector.AlternativePick(2, "REROLL", 4));
        var rerolled = refusing.GetSelectedCardReward(options, alternatives);

        Assert.Null(rerolled.alternative);
        Assert.Null(rerolled.card);
        Assert.Null(refusing.AnsweredAlternative);
        Assert.Contains(
            "alternative 'REROLL', which keeps the reward's selection open on this build (DoNothing)",
            refusing.Refusal,
            StringComparison.Ordinal);
    });

    // ── The Crystal Sphere's own screen ───────────────────────────────────

    /// <summary>The Crystal Sphere's screen is stood in for, its reveals go through
    /// the minigame's own members with the recorded tool, and the minigame completes
    /// on its last divination.</summary>
    [GameFact]
    public void ACrystalSphereIsRevealedFromTheManifestThroughTheStoodInScreen() => HeadlessRuns.WithARun(session =>
    {
        var player = session.RunState.Players[0];
        using var driver = new RunDriver(session);
        driver.EnterFirstRoom();

        Assert.Contains(
            "no Crystal Sphere is open",
            Assert.Throws<EngineException>(() => driver.Apply(HeadlessRuns.Record(
                1, ActionVerb.RevealCrystalSphereCell, ("tool", "small"), ("x", "0"), ("y", "0")))).Message,
            StringComparison.Ordinal);

        var minigame = new CrystalSphereMinigame(player, new Rng(7), 3);
        var play = minigame.PlayMinigame();

        // The screen the retail client would have drawn is not drawn, and the minigame
        // it would have drawn is what the driver clicks through instead.
        Assert.Same(minigame, ScreenStandIns.OpenMinigame);
        Assert.False(play.IsCompleted);

        var first = Hidden(minigame).First();
        driver.Apply(HeadlessRuns.Record(1, ActionVerb.RevealCrystalSphereCell,
            ("tool", "small"), ("x", HeadlessRuns.Number(first.X)), ("y", HeadlessRuns.Number(first.Y))));

        Assert.False(first.IsHidden);
        Assert.Equal(2, minigame.DivinationCount);

        Assert.Contains(
            "which is already revealed",
            Assert.Throws<EngineException>(() => driver.Apply(HeadlessRuns.Record(
                2, ActionVerb.RevealCrystalSphereCell,
                ("tool", "small"), ("x", HeadlessRuns.Number(first.X)), ("y", HeadlessRuns.Number(first.Y))))).Message,
            StringComparison.Ordinal);

        Assert.Contains(
            "outside the 11x11 grid",
            Assert.Throws<EngineException>(() => driver.Apply(HeadlessRuns.Record(
                2, ActionVerb.RevealCrystalSphereCell, ("tool", "small"), ("x", "11"), ("y", "0")))).Message,
            StringComparison.Ordinal);

        var second = Hidden(minigame).First();
        Assert.Contains(
            "has no tool for",
            Assert.Throws<EngineException>(() => driver.Apply(HeadlessRuns.Record(
                2, ActionVerb.RevealCrystalSphereCell,
                ("tool", "medium"), ("x", HeadlessRuns.Number(second.X)), ("y", HeadlessRuns.Number(second.Y))))).Message,
            StringComparison.Ordinal);

        // A big reveal on an interior hidden cell clears it and its neighbours.
        var interior = Hidden(minigame).First(cell => cell.X is > 0 and < 10 && cell.Y is > 0 and < 10);
        var hiddenBefore = Hidden(minigame).Count();
        driver.Apply(HeadlessRuns.Record(2, ActionVerb.RevealCrystalSphereCell,
            ("tool", "big"), ("x", HeadlessRuns.Number(interior.X)), ("y", HeadlessRuns.Number(interior.Y))));

        Assert.False(interior.IsHidden);
        Assert.True(hiddenBefore - Hidden(minigame).Count() > 1);
        Assert.Equal(1, minigame.DivinationCount);

        var last = Hidden(minigame).First();
        driver.Apply(HeadlessRuns.Record(3, ActionVerb.RevealCrystalSphereCell,
            ("tool", "small"), ("x", HeadlessRuns.Number(last.X)), ("y", HeadlessRuns.Number(last.Y))));

        // Finished, and still open while its loot is on offer.
        Assert.True(minigame.IsFinished);
        Assert.Equal(0, minigame.DivinationCount);
        Assert.Same(minigame, ScreenStandIns.OpenMinigame);

        Assert.Contains(
            "has no divination left",
            Assert.Throws<EngineException>(() => driver.Apply(HeadlessRuns.Record(
                4, ActionVerb.RevealCrystalSphereCell, ("tool", "small"), ("x", "5"), ("y", "5")))).Message,
            StringComparison.Ordinal);

        // The minigame's own completion offers what was revealed as loot, and the
        // engine's task completes only once that loot is decided - like any loot
        // screen. Declined here, which is what a history records as SkipRewards.
        if (driver.UnclaimedRewardKinds.Count > 0) driver.Apply(HeadlessRuns.Record(4, ActionVerb.SkipRewards));
        Pump.Drain();

        Assert.True(play.IsCompletedSuccessfully);
        Assert.Null(ScreenStandIns.OpenMinigame);

        Assert.Contains(
            "no Crystal Sphere is open",
            Assert.Throws<EngineException>(() => driver.Apply(HeadlessRuns.Record(
                5, ActionVerb.RevealCrystalSphereCell, ("tool", "small"), ("x", "5"), ("y", "5")))).Message,
            StringComparison.Ordinal);
    });

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>The refusal a selector recorded for one asking, on a selector of its
    /// own because a selector keeps its first refusal.</summary>
    /// <summary>
    /// A card reward the engine would put on a loot screen for this player, built the
    /// way the engine builds one, so what it offers past its cards is what
    /// <c>CardRewardAlternative.Generate</c> and the player's relics say and not a list
    /// written here. Its cards are the first three of the deck, which is enough for a
    /// position past them to mean something.
    /// </summary>
    private static CardReward ACardReward(Player player)
    {
        var cards = player.Deck.Cards.Take(3).ToList();
        var creation = new CardCreationOptions([], CardCreationSource.Encounter, CardRarityOddsType.Uniform);
        var reward = new CardReward(cards, CardCreationSource.Encounter, player, creation);
        Assert.Equal(3, reward.Cards.Count());
        return reward;
    }

    /// <summary>The cards a reward offers, as the engine hands them to the seam: the
    /// reward's own list, read through the field the engine reads it from, because
    /// the reward exposes the cards and not the creation results the seam is given.</summary>
    private static IReadOnlyList<CardCreationResult> OfferedCards(CardReward reward) =>
        (IReadOnlyList<CardCreationResult>)HarmonyLib.AccessTools.Field(typeof(CardReward), "_cards").GetValue(reward)!;

    private static string Refused(Action<ManifestCardSelector> ask)
    {
        var selector = new ManifestCardSelector();
        ask(selector);
        return selector.Refusal ?? "the selector accepted it";
    }

    /// <summary>This answerer for as long as the block runs, and no answerer after
    /// it: what the stand-ins consult is process-wide.</summary>
    private static IDisposable Answers(ScreenStandIns.IStandInAnswerer answerer)
    {
        ScreenStandIns.Current = answerer;
        return new Restores();
    }

    private sealed class Restores : IDisposable
    {
        public void Dispose() => ScreenStandIns.Current = null;
    }

    /// <summary>
    /// An answerer of its own, holding the same selector a driver answers from.
    ///
    /// A prompt whose contents the engine rolls cannot have its answer written down in
    /// advance, so what a recording would have named is queued from what the prompt
    /// actually offered - and then checked by the same selector, against the same ids
    /// and position a manifest would carry.
    /// </summary>
    private sealed class Answering : ScreenStandIns.IStandInAnswerer
    {
        internal ManifestCardSelector Selector { get; } = new();

        internal IReadOnlyList<IReadOnlyList<CardModel>>? Bundles { get; private set; }

        internal Action<IReadOnlyList<IReadOnlyList<CardModel>>>? Saw { get; set; }

        public IReadOnlyList<CardModel> AnswerBundle(IReadOnlyList<IReadOnlyList<CardModel>> bundles)
        {
            Bundles = bundles;
            Saw?.Invoke(bundles);
            return Selector.GetSelectedBundle(bundles);
        }

        public RelicModel? AnswerRelic(IReadOnlyList<RelicModel> relics) => Selector.GetSelectedRelic(relics);
    }

    private static IReadOnlyList<IReadOnlyList<CardModel>> TwoBundles(Player player)
    {
        var deck = player.Deck.Cards;
        return [[deck[0], deck[1]], [deck[2], deck[3]]];
    }

    private static IEnumerable<CrystalSphereCell> Hidden(CrystalSphereMinigame minigame)
    {
        for (var x = 0; x < minigame.GridSize.X; x++)
        {
            for (var y = 0; y < minigame.GridSize.Y; y++)
            {
                if (minigame.cells[x, y].IsHidden) yield return minigame.cells[x, y];
            }
        }
    }
}

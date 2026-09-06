using System.Globalization;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Engine;

/// <summary>One thing a probe established, or failed to, and what it saw.</summary>
public sealed record ProbeResult(string Name, bool Passed, string Detail);

/// <summary>
/// Measures the verbs format v6 added against the real engine, one prompt at a time.
///
/// Each of them reaches a seam no committed history passes through - a bundle screen,
/// a relic screen, a card reward's alternative, the Crystal Sphere, an ended turn
/// taken back - and the fixtures that would pass through them need a run that holds
/// Scroll Boxes, Pael's Wing or a second-act event, which no generated journey
/// reaches. So the seams are exercised where they are: a real run is started, and each
/// prompt is asked the way the engine asks it, with the driver's own selector
/// answering from a queued decision or refusing. Nothing here replays a history and
/// nothing here is evidence about a recording; it is the patch-day question for these
/// five verbs, and <c>ResidueVerbTests</c> is what runs it.
///
/// Every refusal sentence a probe asserts is the driver's or the selector's own, so a
/// sentence that changes is a probe that fails rather than a test that still passes.
/// </summary>
public static class VerbProbe
{
    public static readonly string[] Probes =
        ["undo-end-turn", "bundle", "relic-screen", "card-reward-alternative", "crystal-sphere"];

    public static IReadOnlyList<ProbeResult> Measure(string probe)
    {
        var session = new GameSession();
        session.StartRun("P1L0TTRA1NER", "CHARACTER.IRONCLAD", 0, "standard", ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"]);

        return probe switch
        {
            "undo-end-turn" => UndoEndTurn(session),
            "bundle" => Bundle(session),
            "relic-screen" => RelicScreen(session),
            "card-reward-alternative" => CardRewardAlternative(session),
            "crystal-sphere" => CrystalSphere(session),
            _ => throw new EngineException(
                $"'{probe}' is not a probe. One of: {string.Join(", ", Probes)}."),
        };
    }

    // ── EndTurn through the action, and the undo the client never offers ──

    private static IReadOnlyList<ProbeResult> UndoEndTurn(GameSession session)
    {
        var results = new List<ProbeResult>();
        using var driver = new RunDriver(session);
        EnterTheFirstFight(driver, session);

        var turnBefore = Field(session, "combat.turn");
        var undo = Record(2, ActionVerb.UndoEndTurn);
        driver.Apply(Record(1, ActionVerb.EndTurn), [undo]);
        var turnAfter = Field(session, "combat.turn");
        results.Add(new ProbeResult(
            "end-turn-through-the-action",
            turnBefore == "1" && turnAfter == "2" && Field(session, "combat.in_progress") == "true",
            $"EndPlayerTurnAction enqueued on the run's queue took the fight from turn {turnBefore} to " +
            $"turn {turnAfter}, in progress {Field(session, "combat.in_progress")}"));

        results.Add(Refuses(
            "undo-refused-on-this-build",
            () => driver.Apply(undo),
            "no singleplayer run on v0.111.0 can"));

        return results;
    }

    // ── The bundle screen Scroll Boxes opens ──────────────────────────────

    private static IReadOnlyList<ProbeResult> Bundle(GameSession session)
    {
        var results = new List<ProbeResult>();
        var player = session.RunState.Players[0];

        // The real prompt, answered through the stand-in: obtaining Scroll Boxes asks
        // which bundle, the prefix hands the question to the answerer, and the
        // engine adds the chosen bundle's cards to the deck.
        using (var driver = new RunDriver(session))
        {
            driver.EnterFirstRoom();
            var deckBefore = player.Deck.Cards.Count;
            IReadOnlyList<IReadOnlyList<CardModel>>? offered = null;
            string? chosenIds = null;

            ScreenStandIns.Current = new Capturing(driver, bundles =>
            {
                offered = bundles;
                chosenIds = ManifestCardSelector.BundleIds(bundles[^1]);
                driver.Selector.Enqueue(new ManifestCardSelector.BundlePick(1, chosenIds, bundles.Count - 1));
            });

            RelicCmd.Obtain(ModelDb.Relic<ScrollBoxes>().ToMutable(), player).GetAwaiter().GetResult();
            Pump.Drain();

            var deckAfter = player.Deck.Cards.Count;
            var added = deckAfter - deckBefore;
            var chosenCount = offered?[^1].Count ?? -1;
            results.Add(new ProbeResult(
                "bundle-prompt-answered-through-the-stand-in",
                offered is { Count: > 1 } && added == chosenCount && driver.Selector.Refusal is null,
                offered is null
                    ? "the prompt was never asked: Scroll Boxes did not reach CardSelectCmd.FromChooseABundleScreen"
                    : $"the prompt offered {offered.Count} bundle(s), the answer was the last ({chosenIds}), and " +
                      $"the deck grew by {added} for a bundle of {chosenCount}" +
                      (driver.Selector.Refusal is { } refused ? $"; refused: {refused}" : string.Empty)));
        }

        // The three refusals, each on a fresh selector because the first refusal is
        // the one kept.
        var bundles = TwoBundles(player);
        results.Add(Refusal(session, "bundle-index-past-the-screen",
            driver =>
            {
                driver.Selector.Enqueue(new ManifestCardSelector.BundlePick(1, ManifestCardSelector.BundleIds(bundles[0]), 5));
                driver.Selector.GetSelectedBundle(bundles);
            },
            "takes bundle 5, but this screen offers 2"));
        results.Add(Refusal(session, "bundle-ids-differ",
            driver =>
            {
                driver.Selector.Enqueue(new ManifestCardSelector.BundlePick(1, "CARD.NOT_OFFERED,CARD.NOR_THIS", 0));
                driver.Selector.GetSelectedBundle(bundles);
            },
            "expects bundle CARD.NOT_OFFERED,CARD.NOR_THIS at bundle option 0"));
        results.Add(Refusal(session, "bundle-nobody-recorded",
            driver => driver.Selector.GetSelectedBundle(bundles),
            "asked which of its 2 bundle(s) was taken and the manifest does not say"));

        return results;
    }

    // ── The relic screen nothing on this build opens ──────────────────────

    private static IReadOnlyList<ProbeResult> RelicScreen(GameSession session)
    {
        var results = new List<ProbeResult>();
        var player = session.RunState.Players[0];
        var relics = ModelDb.AllRelics.Take(3).ToList();

        using (var driver = new RunDriver(session))
        {
            driver.EnterFirstRoom();
            driver.Selector.Enqueue(new ManifestCardSelector.RelicPick(1, relics[1].Id.ToString(), 1));
            var picked = RelicSelectCmd.FromChooseARelicScreen(player, relics).GetAwaiter().GetResult();
            results.Add(new ProbeResult(
                "relic-prompt-answered-through-the-stand-in",
                ReferenceEquals(picked, relics[1]) && driver.Selector.Refusal is null,
                $"the prompt offered {relics.Count} relic(s) and the answer came back as {picked?.Id.ToString() ?? "nothing"}" +
                (driver.Selector.Refusal is { } refused ? $"; refused: {refused}" : string.Empty)));
        }

        results.Add(Refusal(session, "relic-index-past-the-screen",
            driver =>
            {
                driver.Selector.Enqueue(new ManifestCardSelector.RelicPick(1, relics[0].Id.ToString(), 9));
                RelicSelectCmd.FromChooseARelicScreen(player, relics).GetAwaiter().GetResult();
            },
            "takes relic 9 off a screen, but this screen offers 3"));
        results.Add(Refusal(session, "relic-nobody-recorded",
            driver => RelicSelectCmd.FromChooseARelicScreen(player, relics).GetAwaiter().GetResult(),
            "asked which of its 3 relic(s) was taken and the manifest does not say"));

        // What a history that records the verb meets on this build: no action opens
        // the screen, so the answer is one no screen consumed.
        using (var driver = new RunDriver(session))
        {
            results.Add(Refuses(
                "relic-answer-no-action-opened",
                () => driver.Apply(Record(1, ActionVerb.SelectRelicFromScreen, ("relic_id", "RELIC.ANCHOR"), ("option_index", "0"))),
                "answers a relic screen no action opened"));
        }

        return results;
    }

    // ── A card reward answered past its cards ─────────────────────────────

    private static IReadOnlyList<ProbeResult> CardRewardAlternative(GameSession session)
    {
        var results = new List<ProbeResult>();
        var player = session.RunState.Players[0];
        var options = player.Deck.Cards.Take(3).Select(card => new CardCreationResult(card)).ToList();
        var alternatives = new List<CardRewardAlternative>
        {
            new("SACRIFICE", PostAlternateCardRewardAction.EndSelectionAndCompleteReward),
        };

        using (var driver = new RunDriver(session))
        {
            driver.Selector.Enqueue(new ManifestCardSelector.AlternativePick(1, "SACRIFICE", 3));
            var selection = driver.Selector.GetSelectedCardReward(options, alternatives);
            results.Add(new ProbeResult(
                "alternative-answered-through-the-seam",
                ReferenceEquals(selection.alternative, alternatives[0]) && selection.card is null &&
                driver.Selector.Refusal is null,
                $"ICardSelector.GetSelectedCardReward answered with alternative " +
                $"{selection.alternative?.OptionId ?? "none"} and card {selection.card?.Id.ToString() ?? "none"}" +
                (driver.Selector.Refusal is { } refused ? $"; refused: {refused}" : string.Empty)));
        }

        results.Add(Refusal(session, "alternative-this-reward-does-not-offer",
            driver =>
            {
                driver.Selector.Enqueue(new ManifestCardSelector.AlternativePick(1, "REROLL", 3));
                driver.Selector.GetSelectedCardReward(options, alternatives);
            },
            "with alternative 'REROLL', and this reward offers 0:SACRIFICE"));
        results.Add(Refusal(session, "alternative-at-the-wrong-position",
            driver =>
            {
                driver.Selector.Enqueue(new ManifestCardSelector.AlternativePick(1, "SACRIFICE", 7));
                driver.Selector.GetSelectedCardReward(options, alternatives);
            },
            "at option 7, and this screen reports it at 3"));

        return results;
    }

    // ── The Crystal Sphere's own screen ───────────────────────────────────

    private static IReadOnlyList<ProbeResult> CrystalSphere(GameSession session)
    {
        var results = new List<ProbeResult>();
        var player = session.RunState.Players[0];
        using var driver = new RunDriver(session);
        driver.EnterFirstRoom();

        results.Add(Refuses(
            "reveal-with-no-sphere-open",
            () => driver.Apply(Record(1, ActionVerb.RevealCrystalSphereCell, ("tool", "small"), ("x", "0"), ("y", "0"))),
            "no Crystal Sphere is open"));

        var minigame = new CrystalSphereMinigame(player, new Rng(7), 3);
        var play = minigame.PlayMinigame();
        results.Add(new ProbeResult(
            "screen-stood-in-for",
            ReferenceEquals(ScreenStandIns.OpenMinigame, minigame) && !play.IsCompleted,
            ScreenStandIns.OpenMinigame is null
                ? "NCrystalSphereScreen.ShowScreen was not reached, or the stand-in did not capture the minigame"
                : "the minigame the screen would have drawn is captured and the engine is waiting on its clicks"));

        var hidden = Hidden(minigame).ToList();
        var first = hidden[0];
        driver.Apply(Record(1, ActionVerb.RevealCrystalSphereCell,
            ("tool", "small"), ("x", Number(first.X)), ("y", Number(first.Y))));
        results.Add(new ProbeResult(
            "small-reveal-clears-one-cell",
            !first.IsHidden && minigame.DivinationCount == 2,
            $"cell ({first.X}, {first.Y}) hidden={first.IsHidden}, divinations left {minigame.DivinationCount}"));

        results.Add(Refuses(
            "reveal-of-a-revealed-cell",
            () => driver.Apply(Record(2, ActionVerb.RevealCrystalSphereCell,
                ("tool", "small"), ("x", Number(first.X)), ("y", Number(first.Y)))),
            "which is already revealed"));
        results.Add(Refuses(
            "reveal-outside-the-grid",
            () => driver.Apply(Record(2, ActionVerb.RevealCrystalSphereCell, ("tool", "small"), ("x", "11"), ("y", "0"))),
            "outside the 11x11 grid"));
        results.Add(Refuses(
            "reveal-with-a-tool-the-minigame-has-not-got",
            () => driver.Apply(Record(2, ActionVerb.RevealCrystalSphereCell,
                ("tool", "medium"), ("x", Number(hidden[1].X)), ("y", Number(hidden[1].Y)))),
            "has no tool for"));

        // A big reveal on an interior hidden cell clears it and its neighbours.
        var interior = Hidden(minigame).First(cell => cell.X is > 0 and < 10 && cell.Y is > 0 and < 10);
        var hiddenBefore = Hidden(minigame).Count();
        driver.Apply(Record(2, ActionVerb.RevealCrystalSphereCell,
            ("tool", "big"), ("x", Number(interior.X)), ("y", Number(interior.Y))));
        var cleared = hiddenBefore - Hidden(minigame).Count();
        results.Add(new ProbeResult(
            "big-reveal-clears-the-neighbourhood",
            !interior.IsHidden && cleared > 1 && minigame.DivinationCount == 1,
            $"a big reveal at ({interior.X}, {interior.Y}) cleared {cleared} cell(s), divinations left {minigame.DivinationCount}"));

        var last = Hidden(minigame).First();
        driver.Apply(Record(3, ActionVerb.RevealCrystalSphereCell,
            ("tool", "small"), ("x", Number(last.X)), ("y", Number(last.Y))));
        results.Add(new ProbeResult(
            "last-divination-finishes-the-minigame",
            minigame.IsFinished && minigame.DivinationCount == 0 && ReferenceEquals(ScreenStandIns.OpenMinigame, minigame),
            $"finished={minigame.IsFinished}, divinations left {minigame.DivinationCount}, still open while its " +
            $"loot is on offer={ScreenStandIns.OpenMinigame is not null}"));

        results.Add(Refuses(
            "reveal-after-the-last-divination",
            () => driver.Apply(Record(4, ActionVerb.RevealCrystalSphereCell, ("tool", "small"), ("x", "5"), ("y", "5"))),
            "has no divination left"));

        // The minigame's own completion offers what was revealed as loot, and the
        // engine's task completes only once that loot is decided - like any loot
        // screen. Declined here, which is what a history records as SkipRewards.
        var offered = driver.UnclaimedRewardKinds;
        if (offered.Count > 0) driver.Apply(Record(4, ActionVerb.SkipRewards));
        Pump.Drain();
        results.Add(new ProbeResult(
            "loot-decided-completes-the-engines-task",
            play.IsCompletedSuccessfully && ScreenStandIns.OpenMinigame is null,
            $"the minigame offered {offered.Count} reward(s) ({string.Join(", ", offered)}); once decided, " +
            $"PlayMinigame completed={play.IsCompletedSuccessfully} and the sphere is open={ScreenStandIns.OpenMinigame is not null}"));

        results.Add(Refuses(
            "reveal-after-the-minigame-completed",
            () => driver.Apply(Record(5, ActionVerb.RevealCrystalSphereCell, ("tool", "small"), ("x", "5"), ("y", "5"))),
            "no Crystal Sphere is open"));

        return results;
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>Neow's first option and the map move into the first fight, the way
    /// the first-fight fixture starts.</summary>
    private static void EnterTheFirstFight(RunDriver driver, GameSession session)
    {
        driver.EnterFirstRoom();
        driver.Apply(Record(0, ActionVerb.ChooseNeowBlessing, ("option_index", "0")));

        var current = Field(session, "run.map_coord");
        var separator = current.IndexOf('c');
        var row = int.Parse(current.AsSpan(1, separator - 1), CultureInfo.InvariantCulture);
        var column = int.Parse(current.AsSpan(separator + 1), CultureInfo.InvariantCulture);
        var edge = session.CurrentMapTopology().Edges
            .Where(candidate => candidate.FromRow == row && candidate.FromColumn == column)
            .OrderBy(candidate => candidate.ToColumn)
            .First();
        driver.Apply(Record(1, ActionVerb.MapMove,
            ("act", Number(session.RunState.CurrentActIndex)),
            ("row", Number(edge.ToRow)),
            ("column", Number(edge.ToColumn))));

        if (Field(session, "combat.in_progress") != "true")
        {
            throw new EngineException("The first map move of the probe seed did not enter a fight.");
        }
    }

    private static ProbeResult Refuses(string name, Action attempt, string expected)
    {
        try
        {
            attempt();
            return new ProbeResult(name, false, $"accepted; expected a refusal saying '{expected}'");
        }
        catch (EngineException refusal)
        {
            return new ProbeResult(
                name,
                refusal.Message.Contains(expected, StringComparison.Ordinal),
                refusal.Message);
        }
    }

    /// <summary>A selector-level refusal, on a driver of its own because a selector
    /// keeps its first refusal.</summary>
    private static ProbeResult Refusal(GameSession session, string name, Action<RunDriver> ask, string expected)
    {
        using var driver = new RunDriver(session);
        ask(driver);
        var refusal = driver.Selector.Refusal;
        return new ProbeResult(
            name,
            refusal is not null && refusal.Contains(expected, StringComparison.Ordinal),
            refusal ?? $"accepted; expected a refusal saying '{expected}'");
    }

    private static IReadOnlyList<IReadOnlyList<CardModel>> TwoBundles(MegaCrit.Sts2.Core.Entities.Players.Player player)
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

    private static string Field(GameSession session, string field) =>
        CanonicalStateProjection.Project(session.RunState).Fields[field];

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static ActionRecord Record(int seq, ActionVerb verb, params (string Key, string Value)[] args) => new()
    {
        Seq = seq,
        Verb = verb,
        Args = new SortedDictionary<string, string>(
            args.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal),
        Source = FactSource.Declared,
    };

    /// <summary>An answerer that sees what a prompt offered before the driver's
    /// selector answers it, so a probe can queue the right answer to a prompt whose
    /// contents the engine rolls.</summary>
    private sealed class Capturing(RunDriver driver, Action<IReadOnlyList<IReadOnlyList<CardModel>>> saw)
        : ScreenStandIns.IStandInAnswerer
    {
        public IReadOnlyList<CardModel> AnswerBundle(IReadOnlyList<IReadOnlyList<CardModel>> bundles)
        {
            saw(bundles);
            return ((ScreenStandIns.IStandInAnswerer)driver).AnswerBundle(bundles);
        }

        public RelicModel? AnswerRelic(IReadOnlyList<RelicModel> relics) =>
            ((ScreenStandIns.IStandInAnswerer)driver).AnswerRelic(relics);
    }
}

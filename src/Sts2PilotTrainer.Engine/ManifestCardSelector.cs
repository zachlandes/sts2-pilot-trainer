using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.TestSupport;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// Answers the engine's card-selection screens from the manifest, and refuses when
/// the manifest has nothing to say.
///
/// The engine asks rather than being told: a card reward and an enchantment both
/// suspend inside an engine call and pull the player's answer back through
/// <see cref="ICardSelector"/>, which is the seam the game's own tests use and the
/// only supported way to answer those screens without a scene tree. So the driver
/// queues what the manifest recorded immediately before making the call, and this
/// consumes the queue.
///
/// Nothing here guesses. A screen the manifest did not anticipate, an option index
/// that is out of range, or an offered card whose identity disagrees with the
/// manifest all record a refusal and hand back an empty selection. The refusal is
/// recorded rather than thrown because the engine runs both of these callbacks
/// inside fire-and-forget tasks that swallow exceptions - a throw here would be
/// logged and lost, and the replay would carry on with a decision nobody made. The
/// driver reads <see cref="Refusal"/> after every action and fails there instead.
/// </summary>
internal sealed class ManifestCardSelector : ICardSelector
{
    /// <summary>One card the manifest says was picked off a selection screen.</summary>
    internal readonly record struct Pick(int Seq, string CardId, int OptionIndex);

    /// <summary>One alternative the manifest says a card reward was answered with.</summary>
    internal readonly record struct AlternativePick(int Seq, string OptionId, int OptionIndex);

    /// <summary>One bundle the manifest says was picked off a bundle screen, by the
    /// joined ids of its cards and its position.</summary>
    internal readonly record struct BundlePick(int Seq, string CardIds, int OptionIndex);

    /// <summary>One relic the manifest says was picked off a relic screen.</summary>
    internal readonly record struct RelicPick(int Seq, string RelicId, int OptionIndex);

    /// <summary>How a bundle's cards are joined into one identity, in the order the
    /// prompt listed them.</summary>
    internal const char BundleSeparator = ',';

    private readonly Queue<Pick> _pending = new();
    private readonly Queue<AlternativePick> _pendingAlternatives = new();
    private readonly Queue<BundlePick> _pendingBundles = new();
    private readonly Queue<RelicPick> _pendingRelics = new();

    /// <summary>
    /// The queued picks a screen has actually taken, by the sequence number of the
    /// action that recorded each.
    ///
    /// Filled here, where a screen really asked, and never at the point the driver
    /// queued them. The two are the same moment headlessly and are not inside the
    /// retail client, where the engine resumes the call that opens the screen on a
    /// later frame - so a driver that recorded a queued pick as consumed would be
    /// reporting an answer nobody had given yet.
    /// </summary>
    private readonly List<int> _consumed = [];

    /// <summary>
    /// The queued picks a screen has actually taken, by the sequence number of the
    /// action that recorded each.
    ///
    /// Filled here, where a screen really asked, and never at the point the driver
    /// queued them. The two are the same moment headlessly and are not inside the
    /// retail client, where the engine resumes the call that opens the screen on a
    /// later frame - so a driver that recorded a queued pick as consumed would be
    /// reporting an answer nobody had given yet.
    /// </summary>
    private readonly List<int> _consumed = [];

    /// <summary>
    /// Whether a screen the manifest is silent about is answered from the front of
    /// what it offered instead of refused.
    ///
    /// Off everywhere but the fixture generator, and it must stay that way: for a
    /// reconstruction, a screen nobody wrote down is a decision nobody made, and
    /// answering it would be inventing one. The generator is the one caller with no
    /// manifest to be silent - it is writing the manifest, and a card screen only
    /// exists inside the call that opens it, so what it answered is read back out of
    /// <see cref="TakeImprovised"/> and recorded as the actions that opened it.
    /// </summary>
    internal bool AnswersFromTheFrontWhenSilent { get; set; }

    private readonly List<Pick> _improvised = [];

    /// <summary>What this selector answered without being told, since the last time it
    /// was asked. Empty unless <see cref="AnswersFromTheFrontWhenSilent"/> is on.</summary>
    internal IReadOnlyList<Pick> TakeImprovised()
    {
        var taken = _improvised.ToList();
        _improvised.Clear();
        return taken;
    }

    /// <summary>Why the last selection could not be answered, if it could not be.</summary>
    internal string? Refusal { get; private set; }

    internal void Enqueue(Pick pick) => _pending.Enqueue(pick);

    internal void Enqueue(AlternativePick pick) => _pendingAlternatives.Enqueue(pick);

    internal void Enqueue(BundlePick pick) => _pendingBundles.Enqueue(pick);

    internal void Enqueue(RelicPick pick) => _pendingRelics.Enqueue(pick);

    internal int PendingCount =>
        _pending.Count + _pendingAlternatives.Count + _pendingBundles.Count + _pendingRelics.Count;

    /// <summary>The picks taken since this was last asked, and clears them.</summary>
    internal IReadOnlyList<int> TakeConsumed()
    {
        var taken = _consumed.ToList();
        _consumed.Clear();
        return taken;
    }

    /// <summary>The picks taken since this was last asked, and clears them.</summary>
    internal IReadOnlyList<int> TakeConsumed()
    {
        var taken = _consumed.ToList();
        _consumed.Clear();
        return taken;
    }

    /// <summary>The queued picks nothing consumed, by the action that recorded each,
    /// so a refusal names the stray decisions rather than counting them.</summary>
    internal string DescribePending() =>
        string.Join(", ",
            _pending.Select(pick => $"action {pick.Seq} ({pick.CardId})")
                .Concat(_pendingAlternatives.Select(pick => $"action {pick.Seq} (alternative {pick.OptionId})"))
                .Concat(_pendingBundles.Select(pick => $"action {pick.Seq} (bundle {pick.CardIds})"))
                .Concat(_pendingRelics.Select(pick => $"action {pick.Seq} (relic {pick.RelicId})")));

    /// <summary>
    /// The bundle taken off a choose-a-bundle screen, or an empty list with the
    /// refusal recorded.
    ///
    /// Reached from the host's stand-in at <c>CardSelectCmd.FromChooseABundleScreen</c>,
    /// because the engine's own test branch takes the first bundle without asking and
    /// <c>ICardSelector</c> has no bundle member. The identity is the bundle's cards in
    /// the order the prompt listed them, so a build that offers the bundles in another
    /// order refuses rather than hands over a different set of cards.
    /// </summary>
    internal IReadOnlyList<CardModel> GetSelectedBundle(IReadOnlyList<IReadOnlyList<CardModel>> bundles)
    {
        if (_pendingBundles.Count == 0)
        {
            Refuse(
                $"A bundle screen asked which of its {bundles.Count} bundle(s) was taken and the manifest does " +
                "not say. Every bundle picked off a screen has to be a recorded decision; answering would be " +
                "inventing one.");
            return [];
        }

        var pick = _pendingBundles.Dequeue();
        if (pick.OptionIndex < 0 || pick.OptionIndex >= bundles.Count)
        {
            Refuse(
                $"Action {pick.Seq} takes bundle {pick.OptionIndex}, but this screen offers {bundles.Count}: " +
                $"{DescribeBundles(bundles)}.");
            return [];
        }

        var offered = BundleIds(bundles[pick.OptionIndex]);
        if (offered != pick.CardIds)
        {
            Refuse(
                $"Action {pick.Seq} expects bundle {pick.CardIds} at bundle option {pick.OptionIndex}, but the " +
                $"engine offers {offered}. The screen is {DescribeBundles(bundles)}. The replay has diverged " +
                "from the recorded history before this point.");
            return [];
        }

        _consumed.Add(pick.Seq);
        return bundles[pick.OptionIndex];
    }

    /// <summary>
    /// The relic taken off a choose-a-relic screen, or null with the refusal recorded.
    ///
    /// Reached from the host's stand-in at <c>RelicSelectCmd.FromChooseARelicScreen</c>.
    /// Nothing on v0.111.0 opens that screen, so on this build the refusal that fires
    /// for a recorded relic pick is the one in <c>RunDriver.Apply</c> for a queued
    /// answer no screen consumed.
    /// </summary>
    internal RelicModel? GetSelectedRelic(IReadOnlyList<RelicModel> relics)
    {
        if (_pendingRelics.Count == 0)
        {
            Refuse(
                $"A relic screen asked which of its {relics.Count} relic(s) was taken and the manifest does " +
                "not say. Every relic picked off a screen has to be a recorded decision; answering would be " +
                "inventing one.");
            return null;
        }

        var pick = _pendingRelics.Dequeue();
        if (pick.OptionIndex < 0 || pick.OptionIndex >= relics.Count)
        {
            Refuse(
                $"Action {pick.Seq} takes relic {pick.OptionIndex} off a screen, but this screen offers " +
                $"{relics.Count}: {DescribeRelics(relics)}.");
            return null;
        }

        var relic = relics[pick.OptionIndex];
        if (relic.Id.ToString() != pick.RelicId)
        {
            Refuse(
                $"Action {pick.Seq} expects {pick.RelicId} at relic option {pick.OptionIndex}, but the engine " +
                $"offers {relic.Id}. The screen is {DescribeRelics(relics)}. The replay has diverged from the " +
                "recorded history before this point.");
            return null;
        }

        _consumed.Add(pick.Seq);
        return relic;
    }

    internal static string BundleIds(IEnumerable<CardModel> bundle) =>
        string.Join(BundleSeparator, bundle.Select(card => card.Id.ToString()));

    private static string DescribeBundles(IReadOnlyList<IReadOnlyList<CardModel>> bundles) =>
        string.Join("; ", bundles.Select((bundle, index) => $"{index}:{BundleIds(bundle)}"));

    private static string DescribeRelics(IReadOnlyList<RelicModel> relics) =>
        string.Join(", ", relics.Select((relic, index) => $"{index}:{relic.Id}"));

    /// <summary>
    /// Raises a refusal, keeping the first one. The first is the one that describes
    /// the divergence; anything after it is downstream of a state the manifest never
    /// described.
    /// </summary>
    private void Refuse(string message) => Refusal ??= message;

    /// <summary>
    /// The card taken from a combat's card reward.
    ///
    /// Called from <c>CardReward.OnSelect</c> once the reward itself has been
    /// selected, which in this driver only ever happens because a <c>TakeCard</c>
    /// action asked for it. Declining a card reward does not come through here at all
    /// - <c>SkipRewards</c> skips the whole set, and the engine's own skip path runs
    /// instead - so an empty queue means a card reward was opened by something that
    /// did not say which card came back, and that is refused rather than read as a
    /// skip.
    /// </summary>
    public CardRewardSelection GetSelectedCardReward(
        IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives)
    {
        // An alternative is answered past the cards. The id names which one, because
        // a build can reorder them, and the index is the screen's own - the count of
        // cards offered plus the alternative's position - so both are checked.
        if (_pendingAlternatives.Count > 0)
        {
            var alternative = _pendingAlternatives.Dequeue();
            var position = -1;
            for (var candidate = 0; candidate < alternatives.Count; candidate++)
            {
                if (alternatives[candidate].OptionId != alternative.OptionId) continue;
                position = candidate;
                break;
            }

            if (position < 0)
            {
                Refuse(
                    $"Action {alternative.Seq} answers a card reward with alternative '{alternative.OptionId}', " +
                    $"and this reward offers {DescribeAlternatives(alternatives)}. The replay has diverged " +
                    "from the recorded history before this point.");
                return default;
            }

            var expectedIndex = options.Count + position;
            if (alternative.OptionIndex != expectedIndex)
            {
                Refuse(
                    $"Action {alternative.Seq} answers a card reward with alternative '{alternative.OptionId}' " +
                    $"at option {alternative.OptionIndex}, and this screen reports it at {expectedIndex} " +
                    $"({options.Count} card(s) then {DescribeAlternatives(alternatives)}).");
                return default;
            }

            return new CardRewardSelection { alternative = alternatives[position] };
        }

        if (_pending.Count == 0)
        {
            Refuse(
                $"A card reward asked which of its {options.Count} card(s) was taken and the manifest does " +
                "not say. Taking one and declining the reward are different decisions, and only one of them " +
                "is written down.");
            return default;
        }

        var pick = _pending.Dequeue();
        var offered = options.Select(option => option.Card).ToList();

        if (pick.OptionIndex < 0 || pick.OptionIndex >= offered.Count)
        {
            Refuse(
                $"Action {pick.Seq} takes card reward option {pick.OptionIndex}, but this reward offers " +
                $"{offered.Count}: {Describe(offered)}.");
            return default;
        }

        var card = offered[pick.OptionIndex];
        if (card.Id.ToString() != pick.CardId)
        {
            Refuse(
                $"Action {pick.Seq} expects {pick.CardId} at card reward option {pick.OptionIndex}, but the " +
                $"engine offers {card.Id}. The reward is {Describe(offered)}. The replay has diverged from " +
                "the recorded history before this point.");
            return default;
        }

        _consumed.Add(pick.Seq);
        return new CardRewardSelection { card = card };
    }

    /// <summary>
    /// Cards picked off a selection screen over the deck - the enchantment screen is
    /// the only one this milestone's history reaches.
    ///
    /// The engine states how many it wants, and the manifest has to supply exactly
    /// that many. Supplying fewer would let the engine fall back on its own
    /// behaviour, and supplying more would mean an action nobody will ever consume.
    /// </summary>
    public Task<IEnumerable<CardModel>> GetSelectedCards(
        IEnumerable<CardModel> options, int minSelect, int maxSelect)
    {
        var offered = options.ToList();

        if (_pending.Count < maxSelect && AnswersFromTheFrontWhenSilent)
        {
            var front = offered.Take(maxSelect).ToList();
            if (front.Count < maxSelect)
            {
                Refuse(
                    $"A card-selection screen asked for {maxSelect} card(s) and offered {offered.Count}.");
                return Task.FromResult<IEnumerable<CardModel>>([]);
            }

            _pending.Clear();
            _improvised.AddRange(front.Select((card, index) => new Pick(-1, card.Id.ToString(), index)));
            return Task.FromResult<IEnumerable<CardModel>>(front);
        }

        if (_pending.Count < maxSelect)
        {
            Refuse(
                $"A card-selection screen asked for {maxSelect} card(s) from {offered.Count} option(s) and the " +
                $"manifest supplies {_pending.Count}. Every card picked off a screen has to be a recorded " +
                "decision; answering with fewer would let the engine choose the rest.");
            _pending.Clear();
            return Task.FromResult<IEnumerable<CardModel>>([]);
        }

        var chosen = new List<CardModel>();
        for (var i = 0; i < maxSelect; i++)
        {
            var pick = _pending.Dequeue();
            if (pick.OptionIndex < 0 || pick.OptionIndex >= offered.Count)
            {
                Refuse(
                    $"Action {pick.Seq} selects screen option {pick.OptionIndex}, but the screen offers " +
                    $"{offered.Count}: {Describe(offered)}.");
                return Task.FromResult<IEnumerable<CardModel>>([]);
            }

            var card = offered[pick.OptionIndex];
            if (card.Id.ToString() != pick.CardId)
            {
                Refuse(
                    $"Action {pick.Seq} expects {pick.CardId} at screen option {pick.OptionIndex}, but the " +
                    $"engine offers {card.Id}. The screen is {Describe(offered)}. The replay has diverged " +
                    "from the recorded history before this point.");
                return Task.FromResult<IEnumerable<CardModel>>([]);
            }

            if (chosen.Contains(card))
            {
                Refuse(
                    $"Action {pick.Seq} selects screen option {pick.OptionIndex} a second time. One card " +
                    "cannot be picked twice on one screen.");
                return Task.FromResult<IEnumerable<CardModel>>([]);
            }

            chosen.Add(card);
            _consumed.Add(pick.Seq);
        }

        return Task.FromResult<IEnumerable<CardModel>>(chosen);
    }

    private static string Describe(IEnumerable<CardModel> cards) =>
        string.Join(", ", cards.Select((card, index) => $"{index}:{card.Id}"));

    private static string DescribeAlternatives(IReadOnlyList<CardRewardAlternative> alternatives) =>
        alternatives.Count == 0
            ? "no alternative"
            : string.Join(", ", alternatives.Select((alternative, index) => $"{index}:{alternative.OptionId}"));
}

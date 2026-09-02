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

    private readonly Queue<Pick> _pending = new();

    /// <summary>Why the last selection could not be answered, if it could not be.</summary>
    internal string? Refusal { get; private set; }

    internal void Enqueue(Pick pick) => _pending.Enqueue(pick);

    internal int PendingCount => _pending.Count;

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
        }

        return Task.FromResult<IEnumerable<CardModel>>(chosen);
    }

    private static string Describe(IEnumerable<CardModel> cards) =>
        string.Join(", ", cards.Select((card, index) => $"{index}:{card.Id}"));
}

using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using Sts2PilotTrainer.Engine;
using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Arbiter.Tests;

/// <summary>
/// The reading a producer row's seed is chosen by, and the hunt that chose it.
///
/// A row that is after a relic needs a run that deals that relic, and the game fixes
/// who deals what at the run's start: Neow's three options are rolled as its room is
/// entered, the run's shared relic grab bag is shuffled from the seed and dealt
/// from its front at a chest, the player's own bag - shuffled apart - from its front
/// at an elite's loot and from its back at the merchant's relic shelf
/// (<c>RelicGrabBag.Populate</c>, <c>TreasureRoomRelicSynchronizer</c>,
/// <c>RelicFactory</c>, <c>MerchantRelicEntry</c>),
/// and the rarity the act's first chest draws is the first roll of a stream only
/// chests consume (<c>RunRngType.TreasureRoomRelics</c>, rolled by
/// <c>TreasureRoomRelicSynchronizer</c> through <c>RelicFactory.RollRarity</c>). So
/// the reading is cheap - a run started and stood in its first room, tens of
/// milliseconds, the chest's roll taken off the run that is then thrown away - and a
/// hunt is that reading over candidate seeds until one deals the relic, then the
/// row's own walk to confirm the run survives to the decision.
///
/// A run whose acts list opens on act 2 or 3 opens on that act's ancient rather than
/// on Neow, rolled from the act's own ancients at the act's start and offering three
/// relics from its option pools, and the same reading takes the ancient and its offer
/// off the first room; a row after a relic an ancient deals hunts a seed of that act
/// alone whose ancient offers it.
///
/// The hunt is run once, by hand, and the seed it finds is pinned as a constant in
/// the row with this reading as its criterion; a game update that moves the RNG fails
/// the row on the reading, naming the seed and the relic, and the hunt is run again.
/// A test helper and not a product path: nothing a player uses reads the bag.
/// </summary>
internal static class SeedHunt
{
    /// <summary>What a fresh run's first room says about who deals what: the event the
    /// run opened on - Neow's, or an act's ancient - and the relics it offers, the
    /// relic at the front of each rarity's shared bag, the relic at the back of each
    /// rarity's player bag, and the rarity the act's first chest will draw.</summary>
    internal sealed record Opening(
        string OpeningEventId,
        IReadOnlyList<string> OfferedRelics,
        IReadOnlyDictionary<string, string> SharedBagFronts,
        IReadOnlyDictionary<string, string> PlayerBagBacks,
        string FirstChestRarity)
    {
        /// <summary>The relics Neow offers: the opening's offer where the run opened
        /// on Neow's room, and none otherwise.</summary>
        internal IReadOnlyList<string> NeowRelics =>
            OpeningEventId == DecisionFacts.NeowEventId ? OfferedRelics : [];

        /// <summary>Whether this opening deals the relic through the dealer named: at
        /// the chest, the front of the shared bag the chest's own roll draws from; at
        /// an ancient, the run opened on that ancient and it offers the relic.</summary>
        internal bool Deals(string relicId, Dealer dealer) => dealer switch
        {
            Dealer.Neow => NeowRelics.Contains(relicId, StringComparer.Ordinal),
            Dealer.Chest => SharedBagFronts.TryGetValue(FirstChestRarity, out var front) && front == relicId,
            Dealer.Shop => PlayerBagBacks.TryGetValue(nameof(RelicRarity.Shop), out var back) && back == relicId,
            Dealer.Ancient => OpeningEventId != DecisionFacts.NeowEventId && OfferedRelics.Contains(relicId, StringComparer.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(dealer), dealer, "not a dealer"),
        };
    }

    /// <summary>How a run deals a relic: the opening blessing, the chest that draws
    /// the front of the relic's rarity bag, the merchant's shelf that draws the back
    /// of the shop bag, or the ancient an act opens on where the run's first act is
    /// act 2 or 3.</summary>
    internal enum Dealer
    {
        Neow,
        Chest,
        Shop,
        Ancient,
    }

    /// <summary>The game's own seed alphabet.</summary>
    private const string Alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    /// <summary>Candidate seeds, the same sequence every time, so a hunt rerun after a
    /// game update walks the same seeds in the same order.</summary>
    internal static IEnumerable<string> Candidates()
    {
        var random = new Random(20260917);
        while (true)
        {
            yield return new string(Enumerable.Range(0, 10).Select(_ => Alphabet[random.Next(Alphabet.Length)]).ToArray());
        }
    }

    /// <summary>The opening of a run of this seed, read off a run started on the acts
    /// list - the default progression where none is given - and stood in its first
    /// room and cleaned up again.</summary>
    internal static Opening ReadOpening(string seed, IReadOnlyList<string>? acts = null)
    {
        EngineHost.Start();
        if (RunManager.Instance is { IsInProgress: true } stale) stale.CleanUp();
        var session = new GameSession();
        session.StartRun(seed, "CHARACTER.IRONCLAD", 0, "standard", acts ?? RecordedActWalk.Acts);
        try
        {
            using var driver = new RunDriver(session);
            driver.EnterFirstRoom();
            var opening = RunManager.Instance.EventSynchronizer?.GetLocalEvent()
                ?? throw new InvalidOperationException($"seed {seed} did not open on an event room");
            var offered = opening.CurrentOptions
                .Select(option => option.Relic?.Id.ToString())
                .OfType<string>()
                .ToList();
            var shared = session.RunState.SharedRelicGrabBag.ToSerializable().RelicIdLists;
            var own = session.RunState.Players[0].RelicGrabBag.ToSerializable().RelicIdLists;

            // The roll the first chest makes, taken now off a stream nothing else
            // consumes, on a run that is cleaned up below
            var firstChestRarity = RelicFactory.RollRarity(session.RunState.Rng.TreasureRoomRelics).ToString();
            return new Opening(
                opening.Id.ToString(),
                offered,
                shared.Where(entry => entry.Value.Count > 0).ToDictionary(entry => entry.Key.ToString(), entry => entry.Value[0].ToString(), StringComparer.Ordinal),
                own.Where(entry => entry.Value.Count > 0).ToDictionary(entry => entry.Key.ToString(), entry => entry.Value[^1].ToString(), StringComparer.Ordinal),
                firstChestRarity);
        }
        finally
        {
            if (RunManager.Instance is { IsInProgress: true } manager) manager.CleanUp();
        }
    }

    /// <summary>
    /// The first candidate seed whose opening deals the relic and whose walk, run by
    /// the caller, comes back true; null when none did among the candidates tried.
    /// Run by hand from a scratch test when a row's seed no longer deals its relic;
    /// the seeds a hunt found are the constants in <c>GeneratedCoverageTests</c>.
    /// </summary>
    internal static string? Find(
        string relicId, Dealer dealer, Func<string, bool> walks, int maxCandidates = 40, IReadOnlyList<string>? acts = null)
    {
        var candidates = 0;
        foreach (var seed in Candidates())
        {
            if (candidates >= maxCandidates) return null;
            if (!ReadOpening(seed, acts).Deals(relicId, dealer)) continue;
            candidates++;
            if (walks(seed)) return seed;
        }

        return null;
    }
}

using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
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
/// alone whose ancient offers it. Darv, dealt to one act after the first at the run's
/// start (<c>RunManager.GenerateRooms</c>) and opened on by no act alone, is not read
/// here: a row after it walks the first act through, and the reading of the second
/// act's room set goes in with the survival seed such a row needs.
///
/// The event a question mark opens is fixed at the run's start as well: the act's
/// events are shuffled into its room set as the run is generated and dealt in that
/// order (<c>ActModel.GenerateRooms</c>, <c>RoomSet.NextEvent</c>), and whether the
/// first question mark opens an event at all is the first roll of a stream only
/// question marks consume (<c>RunRngType.UnknownMapPoint</c>,
/// <c>UnknownMapPointOdds.Roll</c>). So the reading takes both off the fresh run: the
/// first event the act allows, and what the first mark rolls.
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
    /// <param name="FirstQuestionMarkRoom">The room type the act's first question mark
    /// rolls, as <c>RoomType</c> names it.</param>
    /// <param name="FirstEventId">The first event of the act's shuffled set, past the
    /// opening room's own, that the run allows at its start: what the first question
    /// mark opens where it rolls an event, short of what the run's state between
    /// allows or forbids by then.</param>
    /// <param name="FirstEncounterId">The encounter the act's first monster room
    /// fights, the first of the act's shuffled set.</param>
    internal sealed record Opening(
        string OpeningEventId,
        IReadOnlyList<string> OfferedRelics,
        IReadOnlyDictionary<string, string> SharedBagFronts,
        IReadOnlyDictionary<string, string> PlayerBagBacks,
        string FirstChestRarity,
        string FirstQuestionMarkRoom,
        string? FirstEventId,
        string? FirstEncounterId)
    {
        /// <summary>Whether the act's first question mark opens the event with this
        /// id: the mark rolls an event, and the event is the first the act deals.</summary>
        internal bool OpensAtTheFirstQuestionMark(string eventId) =>
            FirstQuestionMarkRoom == nameof(RoomType.Event) && FirstEventId == eventId;

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

            // The roll the first chest makes, and the roll the first question mark
            // makes, each taken now off a stream nothing else consumes, on a run that
            // is cleaned up below
            var firstChestRarity = RelicFactory.RollRarity(session.RunState.Rng.TreasureRoomRelics).ToString();
            var firstQuestionMark = session.RunState.Odds.UnknownMapPoint.Roll([], session.RunState).ToString();

            // The act's events in the order its room set deals them, past the first:
            // the opening room is an event room too and is counted as one dealt, so
            // the first question mark takes the second entry the run allows. Allowed
            // as the run stands at its start, which is a reading and not the roll -
            // an event allowed only with gold or a potion in hand is allowed later,
            // one allowed only without a pet is not - so a walk confirms it
            var rooms = session.RunState.Act.ToSave().SerializableRooms;
            var firstEvent = rooms.EventIds
                .Skip(1)
                .Select(id => ModelDb.GetById<EventModel>(id))
                .FirstOrDefault(model => model.IsAllowed(session.RunState))?.Id.ToString();
            return new Opening(
                opening.Id.ToString(),
                offered,
                shared.Where(entry => entry.Value.Count > 0).ToDictionary(entry => entry.Key.ToString(), entry => entry.Value[0].ToString(), StringComparer.Ordinal),
                own.Where(entry => entry.Value.Count > 0).ToDictionary(entry => entry.Key.ToString(), entry => entry.Value[^1].ToString(), StringComparer.Ordinal),
                firstChestRarity,
                firstQuestionMark,
                firstEvent,
                rooms.NormalEncounterIds.FirstOrDefault()?.ToString());
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
        string relicId, Dealer dealer, Func<string, bool> walks, int maxCandidates = 40, IReadOnlyList<string>? acts = null) =>
        Find(opening => opening.Deals(relicId, dealer), walks, maxCandidates, acts);

    /// <summary>The same, for any reading of the opening: a row after an event hunts
    /// the seed whose first question mark opens it.</summary>
    internal static string? Find(
        Func<Opening, bool> criterion, Func<string, bool> walks, int maxCandidates = 40, IReadOnlyList<string>? acts = null)
    {
        var candidates = 0;
        foreach (var seed in Candidates())
        {
            if (candidates >= maxCandidates) return null;
            if (!criterion(ReadOpening(seed, acts))) continue;
            candidates++;
            if (walks(seed)) return seed;
        }

        return null;
    }
}

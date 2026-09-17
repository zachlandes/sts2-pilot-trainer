namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The choices the whole-act walk consults at the decisions it has a rule for, so the
/// same journey can be pointed at a decision point the committed corpus never
/// reaches: decline a card reward on its own screen, take a named rest option, buy
/// from a named shelf, drink or discard a potion on the map, skip the chest, claim an
/// elite's relic.
///
/// A record of choices rather than a second walker, because the journey's route,
/// its fight rule and its survival are what make an act finishable, and a second copy
/// of those is a second thing to keep alive. Every member defaults to today's rule,
/// so a walk that names no policy is the fixture's walk unchanged; a walk that names
/// one and never meets the decision it asks for finishes the act without it, and the
/// test that asked is what says so.
/// </summary>
public sealed record WalkPolicy
{
    /// <summary>Today's rules, exactly.</summary>
    public static WalkPolicy Default { get; } = new();

    /// <summary>Decline the first card reward on its own screen - the loot screen's
    /// Skip, answered past the cards - before taking a card from it; the reward stays
    /// on the screen and the walk then takes the card it would have taken.</summary>
    public bool DeclineTheFirstCardReward { get; init; }

    /// <summary>The rest option to take wherever a rest site offers it, by its own
    /// id, in place of the heal-when-hurt-else-smith rule; a site that does not offer
    /// it takes today's rule.</summary>
    public string? RestOption { get; init; }

    /// <summary>The shelf to buy from before any other, one of the format's shop kinds:
    /// the walk buys everything it can afford on that shelf, cheapest first, and only
    /// then spends what is left of the purse by today's cheapest-first rule across the
    /// rest.</summary>
    public string? ShopKind { get; init; }

    /// <summary>Drink the first potion the belt holds that can be drunk outside a
    /// fight, on the map, at the first floor after a fight where there is one.</summary>
    public bool DrinkAPotionOnTheMap { get; init; }

    /// <summary>Discard the first potion the belt holds, on the map, at the first
    /// floor after a fight where there is one.</summary>
    public bool DiscardAPotionOnTheMap { get; init; }

    /// <summary>Claim the relic a won fight offers - an elite's - instead of declining
    /// it with the rest of the loot.</summary>
    public bool ClaimTheRelicReward { get; init; }

    /// <summary>Leave the chest's relic where it is instead of taking it.</summary>
    public bool SkipTheChest { get; init; }

    /// <summary>Take the chest's relic, which is today's rule, and count reaching the
    /// chest as the ask met.</summary>
    public bool TakeTheChest { get; init; }

    /// <summary>Take the opening blessing that grants the relic with this id where
    /// Neow offers it, in place of the first option; an opening that does not offer it
    /// takes today's rule. What a walk after Winged Boots' free travel asks for.</summary>
    public string? NeowRelic { get; init; }

    /// <summary>At the first map move where the game's own travel rule offers a node
    /// the node being left does not lead to - the whole next row under Winged Boots
    /// or the Flight modifier - walk to the cheapest such node the journey has rules
    /// for, leftmost first, and count that as the ask met; the route is planned again
    /// from there.</summary>
    public bool TravelFreely { get; init; }

    /// <summary>Count the first loot screen that offers two unclaimed rewards of one
    /// kind, both of which the walk claims by position, as the ask met. The walk
    /// claims every gold reward a screen offers under any policy; this one says
    /// reaching a screen with two is what the walk is for.</summary>
    public bool ClaimTwoOfAKind { get; init; }

    /// <summary>The node types the route has to pass through on a walk that otherwise
    /// only has to reach the boss, from the journey's own required set; null leaves
    /// that to the walk's <c>visitEveryRoomType</c>.</summary>
    public IReadOnlyList<MegaCrit.Sts2.Core.Map.MapPointType>? RouteThrough { get; init; }

    /// <summary>Stop the walk at the end of the floor on which the policy's ask was
    /// first met, without going on to the boss or the act's transition: a walk that
    /// is after one decision rather than a finished act, on a run the journey's
    /// survival rules were not tuned to carry past that decision.</summary>
    public bool StopOnceMet { get; init; }
}

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
///
/// A policy can name a relic as well as a decision - the opening blessing that grants
/// it, the relic the run's own bag deals at a chest, an elite's loot or the
/// merchant's shelf, or the relic an act's ancient offers where the run opens on an
/// ancient rather than on Neow - so the walk can be pointed at what that relic adds. Which
/// decision is the ask then follows one rule: where the policy asks for nothing past
/// the relic, obtaining it is the ask, which is what a relic that opens its prompt on
/// being obtained wants; where it does - a rest option, a fight, a second gold, a
/// flight - that decision is the ask and counts only once the relic is held, because a
/// decision a relic adds to is the relic's only after the relic.
/// </summary>
public sealed record WalkPolicy
{
    /// <summary>Today's rules, exactly.</summary>
    public static WalkPolicy Default { get; } = new();

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
    /// takes today's rule. What a walk after Winged Boots' free travel asks for, and
    /// what a walk after any relic Neow deals asks for.</summary>
    public string? NeowRelic { get; init; }

    /// <summary>Take the ancient's option that grants the relic with this id where the
    /// run's first room is an act's ancient rather than Neow's - a run whose acts list
    /// opens on act 2 or 3 - in place of the first option; an ancient that does not
    /// offer it takes today's rule. What a walk after any relic an ancient deals asks
    /// for.</summary>
    public string? AncientRelic { get; init; }

    /// <summary>Obtain the relic with this id wherever the run deals it on the route:
    /// the chest that offers it, the merchant's relic shelf before anything else on
    /// it, the loot screen of a fight that offers it. A route that deals it nowhere
    /// takes today's rules throughout.</summary>
    public string? BagRelic { get; init; }

    /// <summary>Count the first fight entered while holding the policy's relic, played
    /// to its end and its loot taken, as the ask met: what a relic that changes a
    /// fight's turns or its loot asks for.</summary>
    public bool FightWhileHoldingIt { get; init; }

    /// <summary>Count the first merchant entered while holding the policy's relic as
    /// the ask met: what a relic whose own work runs as the shop is entered asks for.</summary>
    public bool ShopWhileHoldingIt { get; init; }

    /// <summary>Take the card-reward alternative with this option id on the first card
    /// reward that offers it, by the alternative's own id - the loot screen's Skip,
    /// answered past the cards, or a relic's sacrifice - and count that as the ask met;
    /// an alternative that keeps the reward's selection open, as Skip does, is followed
    /// by the card the walk would have taken.</summary>
    public string? CardRewardAlternative { get; init; }

    /// <summary>At the first map move where the game's own travel rule offers a node
    /// the node being left does not lead to - the whole next row under Winged Boots
    /// or the Flight modifier - walk to the cheapest such node the journey has rules
    /// for, leftmost first, and count that as the ask met; the route is planned again
    /// from there.</summary>
    public bool TravelFreely { get; init; }

    /// <summary>Claim every reward of this kind a loot screen offers - one of the
    /// format's reward kinds the walk declines by today's rule, the special card a
    /// thief dies holding or the removal a power earns - and count the first claim as
    /// the ask met. A screen that offers none takes today's rule.</summary>
    public string? RewardKindToClaim { get; init; }

    /// <summary>Count the first loot screen that offers two unclaimed rewards of one
    /// kind, both of which the walk claims by position, as the ask met. The walk
    /// claims every gold reward a screen offers under any policy; this one says
    /// reaching a screen with two is what the walk is for.</summary>
    public bool ClaimTwoOfAKind { get; init; }

    /// <summary>The event the walk is after, by id: the route passes through the first
    /// question mark it can reach and ends there, and the room it opens is answered
    /// page by page (<see cref="EventOptionKey"/>). A question mark is routed only for
    /// a walk that names one, because what it resolves to is the run's own stream's
    /// business - a seed is hunted so the first one opens this event, and a walk whose
    /// question mark opened something else finishes without meeting its ask.</summary>
    public string? EventId { get; init; }

    /// <summary>The option to take on the first page of that event that offers it,
    /// by the key the recorder writes - the option's text key, or the relic's id for an
    /// option that deals one - and the ask of a walk after an event: choosing it is
    /// the ask met. Every other page takes today's rule, or the way named by
    /// <see cref="EventOptionsOnTheWay"/>.</summary>
    public string? EventOptionKey { get; init; }

    /// <summary>Choose the option <see cref="EventOptionKey"/> names on every page
    /// that offers it, past the game's own warning that it will kill the player, until
    /// the game ends the run: what a walk after the run's end outside a fight asks
    /// for, on an event whose option costs health each time it is taken. The ask is
    /// met when the run has ended on that option, and never by choosing it alone.</summary>
    public bool ChooseItUntilTheRunEnds { get; init; }

    /// <summary>The options to take on the pages before the one the key is offered
    /// on, by key, where a page offers one of them: how a walk reaches a page behind
    /// another - Punch Off's fight behind its challenge, a trial's verdict behind its
    /// acceptance - without a rule per event. A page that offers none of them takes
    /// today's rule.</summary>
    public IReadOnlyList<string>? EventOptionsOnTheWay { get; init; }

    /// <summary>Make the ask in the act after the first: walk the first act through to
    /// the far side of its boss on the cheapest route, and only then take the
    /// ancient the next act opens on (<see cref="AncientRelic"/>) or route to its
    /// first question mark (<see cref="EventId"/>). The one way to what a run reaches
    /// only past its first act: Darv, who is rolled for an act after the first and
    /// opened on by no act alone, and the events the game allows only from the second
    /// act on.</summary>
    public bool AskInTheNextAct { get; init; }

    /// <summary>The node types the route has to pass through on a walk that otherwise
    /// only has to reach the boss, from the journey's own required set; null leaves
    /// that to the walk's <c>visitEveryRoomType</c>.</summary>
    public IReadOnlyList<MegaCrit.Sts2.Core.Map.MapPointType>? RouteThrough { get; init; }

    /// <summary>Stop the walk at the end of the floor on which the policy's ask was
    /// first met, without going on to the boss or the act's transition: a walk that
    /// is after one decision rather than a finished act, on a run the journey's
    /// survival rules were not tuned to carry past that decision.</summary>
    public bool StopOnceMet { get; init; }

    /// <summary>The relic this policy names, or null: the one the bag deals where
    /// more than one is named, since a walk is after one relic.</summary>
    public string? Relic => BagRelic ?? NeowRelic ?? AncientRelic;

    /// <summary>Whether the policy asks for a decision past obtaining its relic, which
    /// is then the ask.</summary>
    public bool AsksPastTheRelic =>
        CardRewardAlternative is not null || RestOption is not null || ShopKind is not null || DrinkAPotionOnTheMap ||
        DiscardAPotionOnTheMap || ClaimTheRelicReward || SkipTheChest || TakeTheChest || TravelFreely ||
        ClaimTwoOfAKind || FightWhileHoldingIt || ShopWhileHoldingIt || EventOptionKey is not null ||
        RewardKindToClaim is not null;

    /// <summary>Whether the policy asks for a decision past the event option it names,
    /// which is then the ask: the reward the option's fight earns, claimed off its
    /// loot screen, or the rest option taken at a rest site after the event, which is
    /// how a relic an event deals is walked to what it adds at the next rest.</summary>
    public bool AsksPastTheEvent => RewardKindToClaim is not null || RestOption is not null;

    /// <summary>Whether obtaining the policy's relic is the ask: a relic named and
    /// nothing asked past it.</summary>
    public bool ObtainingIsTheAsk => Relic is not null && !AsksPastTheRelic;

    /// <summary>Whether opening the next act is the ask: the walk is into the next
    /// act and names nothing to obtain or choose there, so answering the room that
    /// act opens on is what it is after.</summary>
    public bool OpeningTheNextActIsTheAsk => AskInTheNextAct && Relic is null && EventId is null && !AsksPastTheRelic;
}

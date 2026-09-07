using Sts2PilotTrainer.Replay;

namespace Sts2PilotTrainer.Trainer;

/// <summary>
/// What a floor of a run held, as the surface names it.
///
/// A fight is one thing a floor can hold rather than a thing beside it, which is the
/// settled rule behind the single play-from row: the label is fixed and this is what
/// its second line names. Five kinds and an unknown, and the unknown is not a sixth
/// kind - it is the honest answer for a floor whose recording made no decision that
/// says what was there.
/// </summary>
public enum FloorKind
{
    /// <summary>Nothing in the recording says what this floor held. The second line
    /// then names the floor and stops, rather than naming a kind nobody
    /// established.</summary>
    Unknown,

    Combat,
    Shop,
    Rest,
    Event,
    Treasure,
}

/// <summary>
/// What each floor of a recording held, derived from the decisions the recording
/// itself carries.
///
/// <para><b>Derived from what was done, never from what a node was labelled.</b> A
/// manifest's map move records the act and the coordinate it moved to and nothing
/// about the room, because the retail client rolls the room type after the move - so
/// there is no room type in a recording to read. What there is is the run's own
/// decisions, and a shop purchase, a rest-site option, an event option or a chest
/// relic each says what the player was standing in. That is a reading of what
/// happened rather than a guess about what was there, which is the distinction
/// <c>AGENTS.md</c> asks every claim on this surface to keep.</para>
///
/// <para>The window is the floor's own, half-open at its far end: a floor entry and
/// whatever it opens carry the same <c>after_seq</c>, so a floor holds what happens
/// from its own entry up to the next floor's. Combat is asked first, because a fight
/// is the one kind that also has a boundary and the one the entry treats differently.
/// </para>
/// </summary>
public static class FloorKinds
{
    /// <summary>
    /// The kind of the floor whose window runs from <paramref name="afterSeq"/> up to
    /// <paramref name="until"/>.
    /// </summary>
    public static FloorKind Between(ReplayManifest recording, int afterSeq, int until)
    {
        var kind = FloorKind.Unknown;
        foreach (var action in recording.Actions)
        {
            if (action.Seq < afterSeq || action.Seq >= until) continue;
            if (RecordedFightPlan.IsCombatVerb(action.Verb)) return FloorKind.Combat;

            // First non-combat decision wins, and combat still overrules it, because a
            // floor's fight is followed by its reward screens and those are decisions
            // too. Nothing here is a second reading of a fight: a card taken after a
            // fight says nothing about the room and is not one of these verbs.
            if (kind != FloorKind.Unknown) continue;
            kind = action.Verb switch
            {
                ActionVerb.ShopPurchase => FloorKind.Shop,
                ActionVerb.ChooseRestSiteOption => FloorKind.Rest,
                ActionVerb.ChooseEventOption => FloorKind.Event,
                ActionVerb.TakeChestRelic or ActionVerb.SkipChestRelic => FloorKind.Treasure,
                _ => FloorKind.Unknown,
            };
        }

        return kind;
    }
}

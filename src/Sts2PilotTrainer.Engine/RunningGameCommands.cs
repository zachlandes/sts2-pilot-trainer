using MegaCrit.Sts2.Core.Map;

namespace Sts2PilotTrainer.Engine;

/// <summary>
/// The decisions a host inside the retail client issues on the driver's behalf,
/// because the game issues them through a screen rather than through an engine call.
///
/// One record rather than a parameter per delegate, so that "what the client supplies"
/// is a list with a name and adding to it is a considered thing. Every member is
/// required, and each is required for the same reason: the engine command underneath
/// it is only the middle of what the screen does, and issuing the middle alone leaves
/// the run somewhere the client is not.
///
/// Null everywhere else. Headlessly there are no screens, and each of these decisions
/// is made through the engine's own call or through a stand-in that says why it exists;
/// see docs/headless-fidelity.md.
/// </summary>
/// <param name="Travel">
/// Moves on the act's map the way a clicked node does. The engine's own
/// <c>EnterMapCoord</c> is the middle of it: measured, entering the coordinate alone
/// leaves the client standing on the map with the next room built behind it and its
/// combat never dealt.
/// </param>
/// <param name="SelectCard">
/// Takes the recording's card off the selection screen an earlier decision opened,
/// by the card's model id and the position it sits at in the list the engine offered.
/// Both are passed because either alone would be ambiguous; see
/// <see cref="PrefightTarget.CardOnScreen"/>. There is no engine command for this at
/// all - the screen is the game's own UI, and answering it without one is what the
/// <c>ICardSelector</c> seam is for headlessly.
/// </param>
public sealed record RunningGameCommands(
    Func<MapCoord, Task> Travel,
    Func<string, int, Task> SelectCard);

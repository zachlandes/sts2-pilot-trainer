# What the replay result has to keep, and why

The arbiter's job is to decide whether a reconstruction is exact.
The product's job, later, is to compare what a player did against what a line would have done.
Those are different questions, and the second one is why this document exists: a report shaped only for the first would throw away almost everything the second needs, and it would do so silently.

This is recorded direction.
The contract it asked for now exists: `CombatProjection` and `CombatComparison` in `Sts2PilotTrainer.Replay` derive the two projections below from a replay trace and put two completed fights side by side.
This document still owns the direction; those types own the computation, and where they had to settle something this document had left open, the answer is written down here.

## The boundary: combat start, and the whole fight

The supported reset and replay boundary is **the start of a combat**, and the unit of
work is the complete fight.
A future solution must be computed and verified by replaying the whole combat from that boundary, which is what keeps the engine's state aligned with the run that produced it.
The generated engine fixture now plays its first combat to the end, in both of its lines, so a completed fight is something this repository produces rather than something it describes.
The shipped VOD reconstruction still covers only the opening turn; reading the rest of that fight off the video is its own slice, and until it lands the comparison refuses that manifest rather than projecting a fight that has not finished.

That is a product decision with teeth, so here is what it rules out. No turn-level
state reset. No pre-turn branching into an alternative line. No turn-level solver.
None of those are missing pieces of an unfinished milestone; they are outside the
boundary, and machinery that existed only to serve them has been taken out rather
than left lying around looking like a commitment.

What survives the boundary is the ordered per-turn record. Players will eventually
want to walk through a solution turn by turn, and that walkthrough is **read-only
presentation of the already-computed whole-combat solution** - it re-solves nothing
and resets nothing. So the ordered actions, the turn boundaries, and the resulting
state either side of each step are all kept.
`combat-snapshot` materialises the combat-start snapshot, re-derives it to read it, and describes only the manifest's covered history turn by turn without ranking anything.
Its report states whether combat remains active at the end of that history, and how the fight ended when it is over.
`CombatComparison` requires the digest of that complete canonical snapshot to match on both sides before comparing them.
The smaller sampled boundary in each trace remains descriptive; it is not treated as identity because it omits hidden state such as draw-pile order and RNG positions.

## The two projections, kept apart

The comparison the product leads with is **the complete combat, after the player finishes it**.
Turn-by-turn comparison is secondary and supported, not the headline.

They are two projections of the same events, and they stay separate:

| projection | says |
| --- | --- |
| **combat summary** | which consumables were used, total turns, final health, and signed net health change |
| **turn chronology** | the exact turn each consumable was used, and that turn's enemy health lost and player health lost |

Turn chronology does not get baked into the summary.
The summary answers "what happened in this fight"; a summary that carried turn numbers would be answering the other question badly, and every later consumer would have to decide which half to trust.

One consequence of keeping them apart is worth knowing before reading either.
The summary's `net_health_change` is final health minus starting health, so a positive value is a net gain and a negative value is a net loss.
It includes whatever resolves as combat ends - Ironclad's starting relic heals six the moment the last enemy dies.
The turn detail's player health lost is the gross amount that came off during that turn.
The two therefore need not add up, and they are not reconciled: quietly picking one would throw away a real thing about the fight.

Permanent card removal is represented, and deliberately not prioritised in presentation.
It is rare, it matters when it happens, and it is not worth designing a screen around.

## What that requires of the replay result

Every quantity above is a *difference between two moments*.
Total turns is the last turn number minus the first; net health change is final health minus starting health; enemy health lost in a turn is an enemy's hit points before that turn against after it; a consumable use is a potion slot that held something and then did not except during an explicit discard action; a permanent removal is a card that was in the deck and then was not.

A result that keeps only the final state has kept none of these.
It has the answer to "was it exact" and nothing else, and no amount of re-reading it recovers the rest - the run would have to be replayed again, which is precisely the expensive thing a stored result exists to avoid.

So `VerificationReport.Trace` samples the canonical state either side of every action and keeps both samples.
It computes nothing, ranks nothing, and labels no line better than another.
Deriving the two projections above is later code's job, working from data that is already there.

`ReplayTrace.SampledFields` is the list of what gets kept, named explicitly.
Adding a derivation to the direction above means adding its inputs to that list, on purpose, rather than discovering later that the field was never recorded.

## A recorded interface hypothesis, and what it needs kept

One shape has been floated for the turn-level view and is explicitly *not* a
commitment: a turn-indexed chart plotting enemy health lost and player health lost
for the player and VOD solution, with potion artwork at the turn it was drunk and an
immediately legible visual distinction between the player's line and the VOD's.

It is written down here only so the data it would need survives long enough to test
it. Whether it is the right interface is for interface design to decide, and
discarding it is a perfectly good outcome.

What the trace keeps for it today:

- **Stable turn indices.** `combat.turn` is sampled either side of every step, so
  every event has a turn on both ends and a step that crosses a turn boundary is
  visible as one.
- **Item identity and use timing.** `player.potions` is sampled as model ids, so a
  disappearance is a use at a known turn unless the action explicitly discarded it.
  This retains automatic consumption such as Fairy in a Bottle while excluding a
  discarded potion. The model id is the stable key any artwork lookup would use; no
  art reference is stored here.
- **Damage events.** Each enemy's hit points and block are sampled by index, as are
  the player's, so a damage event is a subtraction over one step.
- **Actor identity, as far as it goes.** For the player's own actions the actor is
  the step: `verb` and `args` say which card was played. For the other direction each
  enemy carries `combat.enemy.N.intent` and `next_move` in the *before* sample, which
  is the attribution signal the engine exposes at that point.

And the limits worth knowing before designing against it.

With more than one enemy alive, damage *received* cannot be attributed to a particular enemy from hit-point deltas alone.
Intent and next-move are what is available.
If the interface needs firm attribution, that is a new thing to capture, not something to infer after the fact.

Enemy health lost has the mirror problem, and the contract refuses rather than guesses.
The engine takes a dead enemy out of the combat state instead of leaving it at zero health, so a step that kills one of several enemies re-indexes the survivors, and a hit-point delta taken by index across that step is a number about two different creatures.
`CombatProjection` refuses such a step by name.
A fight that ends with every enemy dead is the one case the sampled state still resolves exactly, because each one's remaining health is what that step dealt.

Damage absorbed by block is not included in either health-loss measurement.
Enemy health lost is the decrease in enemy hit points, so enemy block depletion is not counted.
Player block is reset at the start of a turn and the trace samples either side of an action rather than inside one, so player health lost likewise reports only the damage that got through.

## What is deliberately not built yet

No comparison UI. No scoring. No verdict about which of two lines was better.
`combat-compare` states differences between two completed fights and nothing else, and it refuses two fights that did not start from the same boundary rather than producing a table that populates and means nothing.
`combat-snapshot` describes only the covered action history.

Neither side of a comparison has ever been a fight played by a person.
Both are replayed through the real engine from the same combat-start boundary, because no mod host exists to capture a retail player's fight, and the comparison says so in its own caveats.
See [the proof-of-concept path](proof-of-concept-path.md) for where that slice sits.

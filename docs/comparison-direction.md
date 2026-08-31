# What the replay result has to keep, and why

The arbiter's job is to decide whether a reconstruction is exact.
The product's job, later, is to compare what a player did against what a line would have done.
Those are different questions, and the second one is why this document exists: a report shaped only for the first would throw away almost everything the second needs, and it would do so silently.

This is recorded direction, not a contract.
The processed comparison contract is owned by a separate work item, and nothing here should be read as having settled it.

## The boundary: combat start, and the whole fight

The supported reset and replay boundary is **the start of a combat**, and the unit of
work is the complete fight. A solution is computed and verified by replaying the whole
combat from that boundary, which is what keeps the engine's state aligned with the
run that produced it.

That is a product decision with teeth, so here is what it rules out. No turn-level
state reset. No pre-turn branching into an alternative line. No turn-level solver.
None of those are missing pieces of an unfinished milestone; they are outside the
boundary, and machinery that existed only to serve them has been taken out rather
than left lying around looking like a commitment.

What survives the boundary is the ordered per-turn record. Players will eventually
want to walk through a solution turn by turn, and that walkthrough is **read-only
presentation of the already-computed whole-combat solution** - it re-solves nothing
and resets nothing. So the ordered actions, the turn boundaries, and the resulting
state either side of each step are all kept. `combat-snapshot` materialises the
combat-start snapshot, re-derives it to read it, and describes the fight turn by turn
without ranking anything.

## The two projections, kept apart

The comparison the product leads with is **the complete combat, after the player finishes it**.
Turn-by-turn comparison is secondary and supported, not the headline.

They are two projections of the same events, and they stay separate:

| projection | says |
| --- | --- |
| **combat summary** | which consumables were used, total turns, and the health outcome - how much was lost and what it ended at |
| **turn chronology** | the exact turn each consumable was used, and that turn's damage dealt and damage received |

Turn chronology does not get baked into the summary.
The summary answers "what happened in this fight"; a summary that carried turn numbers would be answering the other question badly, and every later consumer would have to decide which half to trust.

Permanent card removal is represented, and deliberately not prioritised in presentation.
It is rare, it matters when it happens, and it is not worth designing a screen around.

## What that requires of the replay result

Every quantity above is a *difference between two moments*.
Total turns is the last turn number minus the first; health lost is a subtraction; damage dealt in a turn is an enemy's hit points before that turn against after it; a consumable use is a potion slot that held something and then did not; a permanent removal is a card that was in the deck and then was not.

A result that keeps only the final state has kept none of these.
It has the answer to "was it exact" and nothing else, and no amount of re-reading it recovers the rest - the run would have to be replayed again, which is precisely the expensive thing a stored result exists to avoid.

So `VerificationReport.Trace` samples the canonical state either side of every action and keeps both samples.
It computes nothing, ranks nothing, and labels no line better than another.
Deriving the two projections above is later code's job, working from data that is already there.

`ReplayTrace.SampledFields` is the list of what gets kept, named explicitly.
Adding a derivation to the direction above means adding its inputs to that list, on purpose, rather than discovering later that the field was never recorded.

## A recorded interface hypothesis, and what it needs kept

One shape has been floated for the turn-level view and is explicitly *not* a
commitment: a turn-indexed chart plotting the player's damage dealt and received
against the VOD solution's, with potion artwork at the turn it was drunk and an
immediately legible visual distinction between the player's line and the VOD's.

It is written down here only so the data it would need survives long enough to test
it. Whether it is the right interface is for interface design to decide, and
discarding it is a perfectly good outcome.

What the trace keeps for it today:

- **Stable turn indices.** `combat.turn` is sampled either side of every step, so
  every event has a turn on both ends and a step that crosses a turn boundary is
  visible as one.
- **Item identity and use timing.** `player.potions` is sampled as model ids, so a
  use is a slot that held an id and then did not, at a known turn. The model id is
  the stable key any artwork lookup would use; no art reference is stored here.
- **Damage events.** Each enemy's hit points and block are sampled by index, as are
  the player's, so a damage event is a subtraction over one step.
- **Actor identity, as far as it goes.** For the player's own actions the actor is
  the step: `verb` and `args` say which card was played. For the other direction each
  enemy carries `combat.enemy.N.intent` and `next_move` in the *before* sample, which
  is the attribution signal the engine exposes at that point.

And the limit worth knowing before designing against it: with more than one enemy
alive, damage *received* cannot be attributed to a particular enemy from hit-point
deltas alone. Intent and next-move are what is available. If the interface needs firm
attribution, that is a new thing to capture, not something to infer after the fact -
which is exactly the kind of question the comparison-contract task exists to settle.

## What is deliberately not built yet

No comparison UI. No scoring. No verdict about which of two lines was better.
`combat-snapshot` describes one completed combat and stops; that restraint is
deliberate, not an accident of an unfinished milestone.

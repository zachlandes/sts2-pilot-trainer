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
The shipped VOD reconstruction now covers its whole first combat, read off the video with the same provenance as its opening turn, so the recording is a completed side rather than a history the comparison has to refuse.
A history that stops mid-combat is still refused, and that refusal is exercised against the shipped manifest cut back to where it used to stop.

That history now runs past that first fight - through a second one it also plays to a victory, and into the opening turns of a third - and none of that changes what is read.
The projection reads the first fight the history enters and requires it to have finished; later fights are state the replay passed through, not additional sides.
Two fights in one history are not a comparison and are not treated as one: a comparison has two runs, and reading a run against itself would be inventing a second line nobody played.
Where a later fight's boundary matters is as a *destination* - the floor-5 combat start the current history reaches is the boundary an eventual reset would restore to, and it is checkpointed for that reason and not compared against anything.

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

Entering a fight is held to the same boundary from the other direction.
`RecordedFightEntry` walks a constructed run through the recording's decisions and stops there, and `CombatStartEquality` refuses to hand the fight over unless the live state is the recorded boundary on both readings - every value the recording observed, and the complete canonical snapshot's digest.
That is the same reason the digest is required of a comparison: a boundary that agreed on everything a video shows and differed in a random stream's position is a fight that diverges at the next shuffle.
There is no entry at any other point, and a fight that has already started is never resumed mid-way.

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

The projections above depend on more than the fight's final state.
Total turns is the last combat turn reached; net health change is final health minus starting health; enemy health lost in a turn is an enemy's hit points before that turn against after it; a consumable use is a potion slot that held something and then did not except during an explicit discard action; a permanent removal is a card that was in the deck and then was not.

A result that keeps only the final state has final health and the last turn reached, but not the starting state and chronology needed to derive the rest.
No amount of re-reading it recovers those missing facts; the run would have to be replayed again, which is precisely the expensive thing a stored result exists to avoid.

So `VerificationReport.Trace` samples the canonical state either side of every action and keeps both samples.
A fight a person plays in the retail client is kept the same way: `FightCapture`
samples the same canonical projection either side of every action the game's own
executor announces, into the same trace, so the two lines reach `CombatProjection`
through one reading rather than two that would have to be reconciled.
It refuses a trace with a gap in it - a change between two samples that no action
accounts for - rather than bridging one, because a bridged gap would attribute its
damage to nothing and the projection would under-count without saying so.
It computes nothing, ranks nothing, and labels no line better than another.
`CombatProjection` derives the two projections above from data that is already there.

`ReplayTrace.SampledFields` is the list of what gets kept, named explicitly.
Adding a derivation to the direction above means adding its inputs to that list, on purpose, rather than discovering later that the field was never recorded.

## A recorded interface hypothesis, and what it needs kept

One shape has been floated for the turn-level view and is explicitly *not* a commitment: a turn-indexed chart plotting enemy health lost and player health lost for the player and VOD solution, with potion artwork at the turn it was used and an immediately legible visual distinction between the player's line and the VOD's.

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

The retail mod now captures a person's completed fight, compares it with the recording from the same combat-start boundary, and draws the result: the whole-combat summary as figures, then the turn chronology as card art in the order it was played, then the chart.
The text-led modal that first showed it is gone; the captain read it and reported that prose describing the difference from the recording, on a large popup, was not the interface.
The chart is the one hypothesised above, built and kept honest: `FightResultChart` in `Sts2PilotTrainer.Trainer` derives it from `CombatComparison` alone, plots enemy health lost and player health lost for both lines against the turn, marks potions by their stable model ids at the turn they were spent, and leaves a gap in a line where a projection has no value rather than drawing a zero.
It lives with the presentation rather than in this contract on purpose: what a comparison *says* is still an interface question, and a chart baked into the contract would be an answer nothing could revisit.

There is still no turn-level reset or branching and no solver.
There is no score and no verdict about which line was better.
`combat-compare` and the in-game panel state differences between two completed fights and nothing else, and the comparison refuses fights that did not start from the same boundary rather than producing a table that populates and means nothing.
Nothing on the panel ranks the two lines, scores either of them, or says which was better.
`combat-snapshot` describes only the covered action history.
See [the proof-of-concept path](proof-of-concept-path.md) for the current product boundary.

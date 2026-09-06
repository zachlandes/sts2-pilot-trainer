# The engine's own replay container, and why this project does not use it

Before inventing a replay format, the sensible question is whether the game already
has one. It does. This is what it is, and why it does not replace the manifest.

## What `.mcr` is

The game writes `profile1/replays/latest.mcr`. Its header is length-prefixed and
readable without any tooling:

```
0800 0000 7630 2e31 3131 2e30   len=8, "v0.111.0"
0800 0000 3431 6365 6631 6561   len=8, "41cef1ea"        <- the release commit
1085 825d                        0x5d828510 = 1568834832  <- the ModelDb content hash
```

That third value is **the same content hash this project's preflight compares**, and
the same one the game's version overlay renders. An older `.mcr` on the same machine,
written by `v0.103.2`, carries a different one. So the engine independently agrees
that `(version, commit, content hash)` is the right identity triple for deciding
whether a recording can be replayed — which is a useful corroboration of the design
rather than a coincidence.

The container is set up by `RunManager.SetUpReplay(RunState, CombatReplay, ulong)`,
documented as: *"Set up a run that's been loaded from a CombatReplay file. No
start-of-run initialization code will be run here, since we're loading an existing
state. … The replay also contains the serialized version of the run."* It is a
debug and multiplayer-desync artifact — `NMultiplayerTest` exposes
`ChooseReplayToLoad`, `WriteReplayAsSave` and `_ignoreReplayModelIdHash` — not a
player-facing feature.

## Why it is not this project's format

They answer different questions.

`.mcr` carries a **serialized run state** plus per-step events, and explicitly skips
start-of-run initialization when loading. It is a way to restore a game to a
position. That is a strong primitive and this project may yet use it as the storage
for a materialised snapshot.

The manifest carries the **ordered history from run start, where each action came
from, and what independently observed values it must reproduce**. Its job is to make
a claim falsifiable by somebody who was not there. A serialized state cannot do that:
it is the answer, so replaying it can only ever confirm itself.

The distinction is the whole point of the project. Restoring a snapshot assembled
from visible fields cannot work, because the RNG stream positions are not visible and
cannot be inferred from anything on screen. The only sound construction is to replay
the complete ordered history and let the engine derive the hidden state — which is
why the snapshot here is a *derived cache*, keyed by the history that produced it,
and never a source of truth.

## What to reuse from it

- **The identity triple.** Already adopted, and now corroborated.
- **`_ignoreReplayModelIdHash`.** The game version-gates its own replays on the
  content hash and offers an explicit override for developers. This project gates the
  same way and offers no override, which is the right default for an artifact meant
  to be shared.
- **`SerializableRun` as snapshot storage.** Measured twice, and the answer depends on
  the moment: no at a combat start, yes at a floor arrival where a fight is live. Both
  measurements, and the rule that follows from them, are below.

## Whether a boundary can be stored as a serialized run

`./scripts/arbiter snapshot-restore-probe <manifest>` replays a manifest to its
combat-start boundary, hands the run to the game's own `RunManager.ToSave`, and
restores it in a fresh process through the retail continue-run call sequence
(`RunState.FromSerializable`, `RunManager.SetUpSavedSingleplayer`, which reaches the
private `InitializeSavedRun`, then the engine half of `NGame.LoadRun`: `Launch`,
`GenerateMap`, `LoadIntoLatestMapCoord`).
It projects both sides through `CanonicalStateProjection.Project` and compares them
field by field, refusing before it compares anything if either side's act room set
degraded to the `"unavailable"` sentinel — two states that both lost `_rooms` agree on
that sentinel exactly, and `--control unreadable-room-set` demonstrates the refusal by
making the two digests agree and showing the probe decline to call it agreement.
Only the terminal `room_re_entered` stage decides whether the boundary is restorable, because that is where the retail sequence hands control back to the player; the pre-entry `save_restored` stage is diagnostic only.

On v0.111.0, against the synthetic whole-run fixture, the answer is that a
combat-start boundary cannot be stored this way.
The save carries the run and not the fight: of the 42 fields the restored run
projects, 39 agree exactly — seed, act list, act room set and its visited counts,
every run-persistent RNG stream position, gold, deck order, relics, potions — while 27
of the replay's 29 `combat.*` fields are absent from it altogether and the remaining
two say there is no combat.
`SerializableRun` has no representation of an in-progress combat at all:
`SerializablePlayer` holds a deck, relics, potions and RNG, and no hand, draw pile or
enemy.
(The one non-combat field that differs, `run.act_floor`, reads 0 until a room is
entered.)
Going on through `LoadIntoLatestMapCoord`, which is how a continued run gets a room to
be in, does not recover it and makes matters worse: re-entering the last visited map
coordinate generates a *fresh* fight, so the restored run stands at total floor 3
against the replay's 2, in `ENCOUNTER.SLIMES_WEAK` rather than
`ENCOUNTER.FUZZY_WURM_CRAWLER_WEAK`, with `Shuffle` advanced from 10 to 20.
That is the failure mode this project exists to catch, in its most convincing costume:
a run that loaded cleanly, reports the same seed and build, and is not the run.

So a combat-start boundary stays a derived cache keyed by the history that produced it, and
entering a recorded fight keeps meaning "replay the prefix".
The probe's report is `build/evidence/snapshot-restore-probe.json`.
Its `snapshot-restore-probe.capture.json`, `snapshot-restore-probe.restore.json`, and `snapshot-restore-probe.run-save.json` inputs are published beside it as the coherent evidence set.

## A floor arrival, which is a different moment and a different answer

That measurement did not refuse a boundary at a floor entry, and the floor-entry
measurement has since been made.
The answer is yes, at one shape of floor arrival, and `./scripts/arbiter floor-snapshot`
is where it is computed rather than asserted.

Two things about the retail client, read out of `build/lib/sts2.dll`, are what make the
floor moment different from the combat-start one.

`RunManager.EnterMapPointInternal` sets the act floor and the map coordinate, then calls
`SaveManager.SaveRun`, and only then rolls the room type and creates the room.
The game's own run save is therefore a floor-entry snapshot by construction: it is taken
at the floor, before the room that floor turns out to hold exists.

`NGame.LoadRun` restores it by passing the save's own `preFinishedRoom` to
`RunManager.LoadIntoLatestMapCoord`, and `EnterMapPointInternal` branches on that
argument: a non-null room is one the run had already finished, so the room type is not
re-rolled and no room is created.
Passing null where the save carries a room generates a second, different room off the
run's own stream - a run that loads cleanly, reports the same seed and build, and is not
the run.
That is why the combat-start probe above, which serializes after the room exists and
restores with `preFinishedRoom: null`, generates the fight twice; part of what it
measured is that mistake rather than the save format.
The `SerializableRun` negative it reports is real and unchanged: the save has no
representation of an in-progress combat at all.

### What may be cached, and the one shape that may not

**A floor arrival where a fight is live restores field for field. A floor arrival where
one is not does not, and is refused by a rule rather than noticed later.**

That split was first measured by a throwaway probe over 16 floor arrivals of three
recordings on v0.111.0: the 11 with a live fight reproduced the recorded boundary digest
exactly, 0 fields differing; the 5 without one differed in exactly 17 fields, every one
of them `combat.*`, and in nothing else.
Seed, act list, act room set, all 12 run RNG stream positions, all 3 player stream
positions, deck order, relics, potions, gold, HP, map coordinate and both floor counters
agreed in all 16.

`floor-snapshot` reproduces the same split on its own.
Over all 16 floor arrivals of `synthetic-v0111-whole-act`, the 9 with a live fight were
each restored in a fresh process and cached, and the 7 without one were refused, every
refusal reading `combat.in_progress=false, combat.outcome=victory`.

The 17 fields are the *previous* fight's finished `PlayerCombatState`, still attached to
the live run and absent from the restored one.
`CanonicalStateProjection.ProjectCombat` emits the whole block whenever that state is
non-null, whatever its outcome.
So an arrival with a won fight behind it is the same run in every respect the run itself
has, and a different canonical state - which is the most convincing shape a wrong answer
takes, and the reason the rule is a rule.
`FloorEntrySnapshotEligibility` owns it and is pure, so it is tested where there is no game.

Making the ineligible arrivals pass by dropping a finished fight from the projection would be
defensible on its own terms and is a **different change with its own migration**: it
moves every boundary digest in every committed recording, including the ones a recorder
captured inside a player's own client.
It must not happen as a side effect of wanting a cache to hit.

### What makes it a derived cache rather than a stored answer

Three conditions, and all three are in `floor-snapshot`:

- **Keyed by the history.** `SnapshotCacheKey` covers the environment identity and the
  whole ordered action history up to the arrival, so a save can never be served for a run
  that would not actually reach it. A recording edited anywhere in that prefix hashes
  differently and the cache simply does not answer.
- **Verified by restoring, before it is cached.** The candidate is written to a
  quarantine path, restored in a process that replayed nothing, and compared against what
  the recording observed at that arrival *and* the digest it declares. Only then is it
  written into the cache, save first and record second, so a crash between the two leaves
  a cache with nothing in it rather than a record pointing at a save that never arrived.
- **Verified again on every restore.** `enter-fight --restore` re-derives the digest from
  the bytes and puts it through the same `BoundaryEquality` a walked entry goes through.
  A cached snapshot is a shortcut to the same proof, never a substitute for it.

`--control wrong-floor` restores one floor's save against another floor's boundary and
requires the comparison to refuse it.

### Where it can be pointed today

`floor-snapshot` reaches an arrival through `FloorEntryPlan`, which requires the
recording to carry a checkpoint there naming `run.total_floor` and `run.map_coord`.
That is the same requirement `enter-fight --floor` already has and refuses without, and
the committed native recordings do not meet it: their floor-entry checkpoints carry the
floor and not the coordinate.
So the cache is materialisable today for the whole-act fixture and for any recording
whose arrivals are observed that fully.
Widening it is a question about what a recorder captures at an arrival, not about this
cache.

### What this does not establish

- **Act 1 only.** Every boundary measured sits at `act_index` 0, because no committed
  history crosses an act boundary - the whole-act fixture stops at `ProceedToNextAct`. An
  arrival in a later act is gated by the digest like any other, so shipping it is safe;
  how often it *works* is unmeasured. `act_index` is recorded in every snapshot so the
  question stays answerable.
- **Headless, not the retail client.** The restore runs in `EngineHost`, and the
  presentation half of `NGame.LoadRun` is skipped, exactly as the combat-start probe skips
  it. `GameSession.RestoreSavedRun` refuses to run inside a running game for that reason:
  continuing a run in the client is the client's own path through its main menu, and it
  has not been measured.
- **A save this host collected, not a save file on disk.** The interception takes
  `RunManager.ToSave(preFinishedRoom)` at the game's own call site, upstream of the
  `ShouldSave` gate `RunSaveManager` consults. For a snapshot produced from a replay that
  is the same object; reading a save a player's own client wrote is a different path and
  is not this one.
- **The bytes are not reproducible and are not the identity.** Two captures of one
  arrival differ in exactly `save_time`, `run_time` and `start_time` and agree everywhere
  else, so a cache addressed by the save's own hash would miss every time. The history
  addresses it; `save_sha256` is an integrity check on one file.

### What it is worth

At floor 17 of the 222-action whole-act fixture, median of three: restoring reaches the
arrival in 2.44 s against 3.23 s for replaying the 182 decisions that produced it.
Both are dominated by engine start, so the case for this is the floor-level entry point
itself rather than throughput - a run library that can only stand somebody at floor 40 by
replaying 39 floors has a problem the second is not the unit of.
Do not sell it on time saved.

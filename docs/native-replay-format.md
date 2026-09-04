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
- **`SerializableRun` as snapshot storage.** Measured, and the answer is no for the
  boundary this project uses. See below.

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

So the boundary stays a derived cache keyed by the history that produced it, and
entering a recorded fight keeps meaning "replay the prefix".
What the measurement does *not* refuse is a boundary at a floor entry, where the
game's own save is taken and where every field the probe saw restore correctly is the
whole of the state; that is a separate measurement, and it has not been made.
The probe's report is `build/evidence/snapshot-restore-probe.json`.
Its `snapshot-restore-probe.capture.json`, `snapshot-restore-probe.restore.json`, and `snapshot-restore-probe.run-save.json` inputs are published beside it as the coherent evidence set.

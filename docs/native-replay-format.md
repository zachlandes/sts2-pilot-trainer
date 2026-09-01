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
- **`SerializableRun` as snapshot storage.** Worth revisiting. It would turn
  "restore" from re-deriving into deserializing, at the cost that a deserialized
  snapshot no longer re-proves itself on every restore. The current design prefers
  the slower option for that reason; the cache key is the same either way.

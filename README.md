# sts2-pilot-trainer

A deterministic replay arbiter for Slay the Spire 2.

Given a video of somebody's run, it reconstructs the ordered history of what they did, replays it through the real shipped game engine, and checks the result against what the video actually shows.
If everything agrees, the run's verified gameplay history has been reproduced exactly — including hidden gameplay state no video can show, like the position of every random-number stream and the order of the draw pile.
This does not identify an unobserved source configuration when multiple configurations reproduce that history; the report states that limit.
If anything disagrees, it says which field, at which moment, and stops.

This is the foundation for a training tool: once a mid-run position can be
reconstructed exactly, it can be handed to a player as a puzzle, and two different
lines can be played from the identical position and compared. It is not that tool
yet.

## What has been demonstrated

Against [one NaveGreed run](https://www.youtube.com/watch?v=OJ-6QXhNgdg), on
`v0.111.0`:

- **The seed was verified without reading it.** An earlier optical pass reported
  `SEXT47K77REK` with full confidence and was wrong in two places. Regenerating each
  candidate's Act 1 map through the game and comparing topology against the map the
  video shows resolves it: `SFXT47K77RFK` reproduces all 61 transcribed nodes, the
  next best candidate reproduces 19.
- **The headless replay matches 21 VOD values and the source-tooling residual is history-bound.** The enemy state, ordered hand, energy, block, and turn outcome agree. The three visible-build utilities are non-gameplay tooling. BaseLib can change `SkipNextDurationTick` for a player-applied custom debuff, so the gate instruments every `PowerCmd.Apply` in this exact history and requires a negative control before accepting that the affected branch is unreachable.
- **Replay machinery is exercised by an independent synthetic fixture.** A mechanically generated seed and action sequence, distinct from the VOD trace, pin engine-produced checkpoints. Fresh-process determinism, corrupted-history rejection, and two-line snapshot restore are claims about that fixture only.

- **Provenance is gated before any engine starts.** A run resumed from run history
  matches on seed, build, content hash and acts and replays perfectly — it is just
  not the run its history describes. That is checked on the recording, along with a
  second reading of the environment taken from the end-of-run screen 2,038 seconds
  after the first.
- **The source game mode remains unestablished, while path-specific parity is established over the enumerated mode configurations.**
  Standard and custom with no modifiers agree at every observed checkpoint and in every canonical field except the recorded `run.game_mode`; their full final-state digests differ.
  Every one of the build's 17 modifiers changes an observed checkpoint when replayed alone as a daily, so no single-modifier daily fits the footage.
  Modifier combinations are not enumerated, and the gate does not identify the source mode.

Two accepted assumptions turned out to be wrong and are now recorded as identity:
the act variant (this build ships two different Act 1s) and the player's unlock
state (which moves the shared RNG stream). See
[environment identity](docs/environment-identity.md).

**The publication gate.** All of it is one verdict, computed rather than concluded.
The selected VOD currently returns `PUBLISHABLE` on path-specific parity across the enumerated mode configurations, not on identification of its source mode:

```bash
./scripts/arbiter gate manifests/navegreed-OJ-6QXhNgdg.replay.json
```

The standard is successful reproduction through the real engine, and no condition
accepts a cheaper stand-in — not reader confidence, not arithmetic over the footage,
not a screenshot of a mod list. Those are useful filters and they are not evidence:
two of the four history corruptions pass every arithmetic check available from the
frames, and a run resumed from run history passes every check that is not about the
recording itself.

## Running it

You need the game. It is a read-only input: the bootstrap copies your installed
assemblies into a gitignored working directory, hashes the installation before and
after, and fails if anything moved. No game content is in this repository.
All bootstrap, evidence, state, and snapshot-cache output paths must resolve inside the current worktree, including through symbolic links.

```bash
./scripts/build.sh                      # prepare the assembly copy, build everything
./scripts/arbiter preflight      manifests/navegreed-OJ-6QXhNgdg.replay.json
./scripts/arbiter preflight-live manifests/navegreed-OJ-6QXhNgdg.replay.json
./scripts/arbiter synthetic-fixture --out build/evidence/synthetic.replay.json
./scripts/arbiter replay     build/evidence/synthetic.replay.json
```

`./scripts/arbiter` with no arguments lists the rest: `gate`, `validate`,
`verify-seed`, `determinism`, `negative-controls`, `combat-snapshot`. `validate` needs
no game.

The full walkthrough, with commands and their real output, is in
[demo/DEMO.md](demo/DEMO.md).

## Layout

| | |
|---|---|
| `src/Sts2PilotTrainer.Replay` | The replay format and its rules. Depends on nothing — not the game, not a video pipeline, not a storefront. Its tests run on a machine that does not own the game. |
| `src/Sts2PilotTrainer.Engine` | The only project that knows about a specific game version. |
| `src/Sts2PilotTrainer.Cli` | The arbiter's commands. |
| `manifests/` | The reconstructed run, and the map read from the video. Facts only. |
| `docs/` | [Environment identity](docs/environment-identity.md) · [headless fidelity](docs/headless-fidelity.md) · [dependencies](docs/dependencies.md) · [distribution](docs/distribution.md) · [the engine's own replay format](docs/native-replay-format.md) |

## What this repository does not contain

No game assemblies, localization tables or art — MegaCrit's property, copied from
your own installation at build time. No video footage of any kind: no frames, no
clips, no stills. Only unprotectable facts read from the video, together with the
public video id and the timestamps that let anyone re-check each one against the
original. See [NOTICE](NOTICE).

## Licence

MIT. See [LICENSE](LICENSE) and [NOTICE](NOTICE).

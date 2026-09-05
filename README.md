# sts2-pilot-trainer

A deterministic replay arbiter for Slay the Spire 2.

Given a video of somebody's run, it reconstructs the ordered history of what they did, replays it through the real shipped game engine, and checks the result against what the video actually shows.
If everything agrees, the run's verified gameplay history has been reproduced exactly — including hidden gameplay state no video can show, like the position of every random-number stream and the order of the draw pile.
This does not identify an unobserved source configuration when multiple configurations reproduce that history; the report states that limit.
If anything disagrees, it says which field, at which moment, and stops.

The pre-rename `CombatTrainer` artifact demonstrated the training proof of concept: once a combat-start position is reconstructed exactly, its Combat Trainer lets a player fight it, captures that fight, and compares it with the VOD solution replayed from the same boundary.
The renamed `Runmobile` package has since been installed, loaded and exercised through a whole watched journey in a retail session, with the game's own mod line naming it and a clean protected-files ledger.
[docs/in-game-host.md](docs/in-game-host.md) owns that distinction.
[The proof-of-concept path](docs/proof-of-concept-path.md) records how that loop was built, slice by slice.

## What has been demonstrated

Against [one NaveGreed run](https://www.youtube.com/watch?v=OJ-6QXhNgdg), on
`v0.111.0`:

- **The seed was verified without reading it.** An earlier optical pass reported
  `SEXT47K77REK` with full confidence and was wrong in two places. Regenerating each
  candidate's Act 1 map through the game and comparing topology against the map the
  video shows resolves it: `SFXT47K77RFK` reproduces all 61 transcribed nodes, the
  next best candidate reproduces 19.
- **The headless replay matches 141 VOD values and the source-tooling residual is history-bound.** The enemy state, ordered hand, energy, block, gold, the potion belt, the deck size, and the outcome of every turn of two whole fights agree - through the loot each of them offered, an event that spent 99 gold enchanting two cards, and into the opening turns of a third fight. The three visible-build utilities are non-gameplay tooling. BaseLib can change `SkipNextDurationTick` for a player-applied custom debuff, so the gate instruments every `PowerCmd.Apply` in this exact history and requires a negative control before accepting that the affected branch is unreachable.
- **Replay machinery is exercised by an independent synthetic fixture.**
  A mechanically generated seed and action sequence, distinct from the VOD trace, pin engine-produced checkpoints.
  Fresh-process determinism, corrupted-history rejection, and combat-start snapshot re-derivation are claims about that fixture only.
- **A whole combat is replayed to its end, and two lines of it are compared.**
  The fixture plays its first fight to a victory the canonical state can see, in two mechanically different lines from the same combat-start boundary.
  `combat-compare` derives the combat summary and the turn detail from each and states the differences, scoring nothing.
  The shipped VOD reconstruction now covers its whole first combat, read off the video action by action, so the recording is one of those completed sides.
  It runs on past that fight to the start of the floor-5 fight's third turn, which is the boundary a candidate search over that turn would have to begin from; the projection still reads the first fight the history enters and requires it to have finished.
  A history that stops mid-combat is still refused, which is what the recording used to be.
  The fight a person played in the retail client was captured as the same trace by the pre-rename Combat Trainer mod, projected the same way, and shown beside the recording's on the mod's visual result panel.

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
four of the ten history corruptions pass every arithmetic check available from the
frames, and a run resumed from run history passes every check that is not about the
recording itself.

## Running it

You need the game. It is a read-only input: the bootstrap copies your installed
assemblies into a gitignored working directory, hashes the installation before and
after, and fails if anything moved. No game content is in this repository.
All bootstrap, evidence, state, and snapshot-cache output paths must resolve inside the current worktree, including through symbolic links.
Pass `--archive <dir>` to the bootstrap to retain the complete receipted prepared set under `<dir>/<build-version>` before an installed-game update replaces it.
Re-archiving the same prepared set is safe; a conflicting or unreceipted directory for that version is refused rather than overwritten.

```bash
./scripts/build.sh                      # prepare the assembly copy, build everything
./scripts/bootstrap.sh --archive build/archive  # also retain it under its game version
./scripts/arbiter preflight      manifests/navegreed-OJ-6QXhNgdg.replay.json
./scripts/arbiter preflight-live manifests/navegreed-OJ-6QXhNgdg.replay.json # reads only the headless sandbox and refuses
./scripts/arbiter synthetic-fixture --out build/evidence/synthetic.replay.json
./scripts/arbiter replay     build/evidence/synthetic.replay.json
```

`preflight-live` is a headless demonstration, not a connection to the retail process.
Its user data is redirected to `build/sandbox`, it cannot see the retail `RunManager`, and its default path therefore reads an empty sandbox profile, finds no active run, and refuses by design.
`Preflight.EvaluateLiveHost` is the API the in-game host calls before showing a player anything, and the mod is where it meets a real client:

```bash
./scripts/install-mod.sh                # build the Runmobile mod into the game's own mods directory
./scripts/install-mod.sh --uninstall    # remove it
./scripts/arbiter adopt-live            # the refusal, from a process that is not a running game
```

The corresponding retail flow was demonstrated with the pre-rename Combat Trainer artifact: opening Singleplayer showed a fourth mode card, `Combat Trainer`, which checked whether that install could reproduce the recording and offered `Enter the fight` when it could.
Winning the fight showed the mod's visual result panel with the player's fight beside the recording's: compact summary figures, card and potion art by turn, and a chart of enemy and player health lost each turn.
The two lines stayed distinct by colour and marker shape, and the panel stated differences without scoring either line or giving a verdict.
The trainer supplied the recording's unlocks, acts, and Ascension 10 in memory, then visibly made the recording's pre-fight decisions and handed over only after the live combat-start state matched the manifest's observed fields and snapshot digest.
Under the renamed `Runmobile` artifact a later session repeated the fight itself - the recording's decisions, the handover, the fight played to a win and to a deliberate loss, and the panel each earns - but not the mode card and the offer that precede them, which are still claimed only for the pre-rename artifact.
See [docs/in-game-host.md](docs/in-game-host.md), [demo/RECORDED-FIGHT-ENTRY.md](demo/RECORDED-FIGHT-ENTRY.md), and [demo/VISUAL-COMPARISON.md](demo/VISUAL-COMPARISON.md).

```bash
./scripts/arbiter generate-synthetic-fixture --out build/evidence/alternate.replay.json --line alternate
./scripts/arbiter combat-compare build/evidence/synthetic.replay.json build/evidence/alternate.replay.json
./scripts/arbiter enter-fight manifests/navegreed-OJ-6QXhNgdg.replay.json --play   # the whole loop, the recording standing in for the player
```

Standing in the recording's own fight, which is what the in-game mod does with a
scene tree in the way:

```bash
./scripts/arbiter enter-fight manifests/navegreed-OJ-6QXhNgdg.replay.json
```

It constructs the run at the recording's identity, makes the recording's decisions
before its fight in order, and reports the fight it lands in as the recorded one -
against everything the recording observed there and against the manifest's
engine-produced combat-start snapshot digest - with the profile unchanged either side.
`--fight <n>` walks to that fight of the run instead, and `--floor <n>` to the moment
it arrived on that floor; without either it is the first fight.
A floor arrival is proved by where the run stands, so `--floor` needs a checkpoint at
that arrival naming `run.total_floor` and `run.map_coord`.
The shipped video reconstruction above records no map coordinate anywhere - it was read
off footage of fights - so its floor boundaries are declared but not enterable, and
`--floor` refuses on it.
The committed engine-generated whole-act history does carry them, and is what `--floor`
is demonstrated on:

```bash
./scripts/arbiter enter-fight src/Sts2PilotTrainer.Replay/Fixtures/synthetic-v0111-whole-act.replay.json --floor 5
```

[demo/RECORDED-FIGHT-ENTRY.md](demo/RECORDED-FIGHT-ENTRY.md) has it with its real
output.

`./scripts/arbiter` with no arguments lists the rest: `gate`, `validate`,
`engine-commands`, `verify-seed`, `determinism`, `negative-controls`,
`combat-snapshot`, `snapshot-restore-probe`, `migrate-manifest`. `engine-commands`
prints which of the game's own members each recorded decision maps onto, and says of
every verb it does not map why there is nothing to map it onto. `validate` and
`migrate-manifest` need no game, the latter unless it is deriving boundaries;
`migrate-manifest` is the only command that rewrites a manifest on disk, so
reading somebody's evidence never edits it.
It rewrites the manifest in place, or writes to `--out <path>`, which is written even when the input was already in this format, so a script that migrates and then reads its output never meets a missing file.
`--derive-boundaries` additionally replays the run through the real engine and writes in every boundary the history passes - each fight's start, each floor's arrival and each turn - with the digest that replay produced, refusing if the history does not reproduce.
Like every command here that writes, it refuses a destination outside this repository.

The full walkthrough, with commands and their real output, is in
[demo/DEMO.md](demo/DEMO.md).

## Layout

| | |
|---|---|
| `src/Sts2PilotTrainer.Replay` | The replay format and its rules. Depends on nothing — not the game, not a video pipeline, not a storefront. Its tests run on a machine that does not own the game. |
| `src/Sts2PilotTrainer.Engine` | The only project that knows about a specific game version. |
| `src/Sts2PilotTrainer.Trainer` | The game-free owner of the Combat Trainer screen model, wording, and chart derivation. |
| `src/Sts2PilotTrainer.Mod` | The only project loaded into the retail game; it owns the native mode card and retail presentation. |
| `src/Sts2PilotTrainer.Cli` | The arbiter's commands. |
| `manifests/` | The reconstructed run, and the map read from the video. Facts only. |
| `docs/` | [The proof-of-concept path](docs/proof-of-concept-path.md) · [the in-game host](docs/in-game-host.md) · [environment identity](docs/environment-identity.md) · [comparison direction](docs/comparison-direction.md) · [headless fidelity](docs/headless-fidelity.md) · [dependencies](docs/dependencies.md) · [distribution](docs/distribution.md) · [the engine's own replay format](docs/native-replay-format.md) |

## What this repository does not contain

No extracted game assemblies, localization tables or art assets — MegaCrit's property, copied from your own installation at build time.
The only committed images from the client are screenshots of this mod's own UI under `demo/`; no source-video footage of any kind is stored here: no frames, clips or stills.
Only unprotectable facts read from the video, together with the public video id and the timestamps that let anyone re-check each one against the original, are retained.
See [NOTICE](NOTICE).

## Licence

MIT. See [LICENSE](LICENSE) and [NOTICE](NOTICE).

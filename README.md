# Runmobile

**Play from the fight a top player played, and see how your fight compared.**

A free, open-source mod for Slay the Spire 2.
It ships with one run reconstructed from a top player's public video: play from a fight of that run, from the moment it began, with the same deck, relics, health, enemies and opening hand.
When the fight ends, your fight is shown beside theirs, turn by turn, with no grade and no score.
Playing from a recording writes nothing to your saves, your stats or your run history, win or lose.
Recording your own runs changes nothing about them: they save and count exactly as they always did.
Works on Slay the Spire 2 `v0.111.0`, needs no other mod, and asks you to play from a recording with only Runmobile enabled.

This repository is `sts2-pilot-trainer`: the mod a player installs is `Runmobile`, and the Combat Trainer is one feature inside it.
Anything marked **Coming soon** below is planned for launch and not built yet.
A feature stops being coming soon by deleting the tag, so this file stays current without losing the framing.

## Why it exists

The way this community teaches itself to climb ascensions is to watch a top player and second-guess them: pause at the card reward, decide what you would take, unpause, see what they did.
Watching only lets you guess.
You never find out whether your pick would have worked, because you can never be in that fight.
Runmobile puts you in it.

A run takes an hour and never repeats.
A recorded fight takes five minutes and repeats exactly.

## What you can do

**Play the fight a top player played, then see how yours compared.**
Runmobile ships with one run this project reconstructed from a top player's public video.
Open it and the game walks that player's decisions before the fight on its own screens, with playback controls: each screen arrives with nothing chosen so you can decide for yourself, one press shows what the player chose and one more makes it; play them through at half to double speed, or look back at one already made.
Then the fight is yours.
When it ends, nothing is shown until you ask: show the comparison, fight it again, or leave.
The comparison puts the two fights side by side, won or lost: summary figures, the cards and potions each of you played by turn, and a chart of health lost each turn.
The two lines stay distinct by colour and marker and nothing scores either one.
Built and shown in the retail client; [demo/PLAYBACK-TRANSPORT.md](demo/PLAYBACK-TRANSPORT.md) and [demo/VISUAL-COMPARISON.md](demo/VISUAL-COMPARISON.md) have the screenshots.
Today that is one featured run, and its menu card opens the first fight; the run library below opens every fight that recording proves.
- **Coming soon:** more featured runs, from more creators, named on the page.
- **Coming soon:** every screen between fights carried on the playback controls, so a featured run plays through as one journey.
- **Coming soon:** watch the creator's own fight played through, and peek at it mid-fight if you choose to.
- **Coming soon:** continue the run past the fight, as them or as you.

**Practise the fight you keep losing.**
Play the same fight again from the same start, as many times as you like, and try the other line.
From inside the fight, one control takes you back to its proven start or straight to the end of your attempt; both ask first.
A lost fight is compared too, on request: the comparison reads Lost against Won.

**Your own runs, recorded.**
Every singleplayer run you play is recorded: an ordered history of every decision from run start, written as you play so a crash keeps what happened.
It stays on your machine, under the mod's own directory, and carries no Steam id, machine path or profile id.
A multiplayer game gets nothing at all from this mod - nothing recorded and nothing drawn, not even an indicator saying a run is not being recorded, because one of the people in that game never installed it.
A run you used the game's own console in is still recorded and still kept on your machine; the recording says which it is, and one that says so is never publishable, because what a command did to the run is not among the decisions the history holds.
Two runs played by a person on this build have been replayed through the real engine.
`native-9F8CY60C5BK7-20260906-005737` replays with zero boundary mismatches, and does not pass the publication gate: its run is short enough to have no card reward, no marked card and no event, so three required controls are not applicable.
`native-3LACFJ5NJ371-20260906-015901` replays with zero boundary mismatches and passes the gate `PUBLISHABLE` with all ten controls applied.
Three of the gate's conditions need a video, game-mode, seed-topology and evidence-binding, and a recording made inside the game is never asked them.
A fourth, baselib-path, is not asked either, and a weaker check stands in for it: the loaded mods' own declaration that they do not affect gameplay, which the gate's artifact says out loud.
The arbiter enters fight 2 of that recording headlessly and reproduces the recorded digest byte for byte.
Recording is on by default while the mod is unreleased; [docs/in-game-host.md](docs/in-game-host.md#producing-a-recording-and-checking-it) says how to turn it off.
Settings show how much space recordings take, how many are kept, remove them on request, and control whether the shared-run index is fetched; index fetching defaults on.
No setting shares a run automatically.
- **Coming soon:** a control for turning recording itself off.

**Browse the runs and play from one.**
The Compendium, which the game opens when no run is active, has a Runmobile button beside its own Run History.
Two tabs: Others - the runs included with Runmobile, the featured ones and the recent ones, newest first - and Mine, your own recordings with a line under the list saying how many there are and what they take on this computer.
Open a run and it lists every place the recording proves you can be stood: from a fight's recorded start, from a floor's first screen, "Continue: play from fight N" for the next fight you have not played from, or from run start with every choice shown.
The visible `Compatible with your game version` filter defaults on, hides incompatible runs during ordinary browsing, and leaves a muted "{n} not shown" count underneath.
Turning the filter off reveals incompatible runs as disabled rows; entering an exact code does that automatically, selects its run in the sorted position, and shows both the build it requires and the current build.
An established multiplayer run remains hidden.
Your own finished runs are reachable a second way: the game's own run history carries a plate under its pane offering that run's last fight or last floor, and where it cannot offer them the rows stay in place with the reason on them - the recording has a gap in it, or it was made on another build, or a run is in progress.
That plate also offers `Share this run` when the profile's `settings.json` names an authorized HTTPS sharing service; Runmobile has no built-in service, so otherwise the row says sharing is unavailable and sends nothing.
The single sharing popup shows the run's identity and integrity seals, accepts a required run name of at most 40 characters and an optional description of at most 200, and requires a display name only when submitting.
It says that no other personal information travels, requires explicit CC0 consent, and runs the full publication validation locally before sending the manifest and those entered fields.

**One whole fight, no undo.**
You play from the moment the fight began to the moment it ends.
There is no taking a turn back.
Whether you see any of their line before you play is your choice, and until that choice exists you see none of it.
You learn the fight, not the shuffle.

**Playing from a recording touches nothing.**
While you play from a recording the mod blocks every write the game would make to your progress file, your run save, your run history and your achievements, win or lose.
That is measured, not asserted: a ledger of the game's files taken before a session and compared after, with the mod's own directory reported separately from everything that must not change.
Your own runs are different on purpose: recording them changes nothing about how they save or count.

**It tells you when it can't.**
Before you play from a recording, Runmobile checks whether your install can reproduce it: game version, content, act variant, unlock state and which mods are active.
Each thing that does not match is a sentence naming what the recording needs and what it found, and what to play to fix it where playing fixes it.
No black screen.

**Nothing is a saved snapshot.**
A recording is the run's decisions, and to open a fight Runmobile hands them to the real game in order until it reaches that fight, then checks that the fight it arrived in is the recorded one, hidden state included: draw order and the position of every random stream.
A fight that does not match is refused rather than played.

## Compared with other mods

Figures are Steam Workshop current subscribers or Nexus downloads on 2026-09-06, from the stores' own APIs and pages.
Nothing here is a claim about anybody's craft: each row is what the mod does and what Runmobile does instead.

| Mod | What it does | Runmobile |
|---|---|---|
| [Rewind](https://steamcommunity.com/sharedfiles/filedetails/?id=3747557762) (44,187) | Undo turns inside a fight; return to a room checkpoint. | One whole fight from its real start, no undo. The fight is re-derived through the real engine and refused if it is not the recorded one. |
| [Hindsight](https://www.nexusmods.com/slaythespire2/mods/925) (670 Nexus downloads, not on the Workshop) | Re-enter your own finished run at a floor, from a save snapshot it took. | Plays from history, never a snapshot. Playing from a recording writes nothing to saves, stats or run history. |
| [RunReplays](https://steamcommunity.com/sharedfiles/filedetails/?id=3759973128) (53) | Record every decision of a run and replay it by driving the game's screens; load to a floor. | Replays through the engine and checks the result at every boundary; nothing is compared on trust. A version mismatch is a sentence before anything starts. |
| [STS2Dojo](https://steamcommunity.com/sharedfiles/filedetails/?id=3758840693) (21) | Rebuild one fight from your run-history file and a seed, as a standalone practice combat. | The fight is the one that happened, reached by replaying the run's decisions, and the comparison is against the recorded fight. |
| [Training Ground](https://steamcommunity.com/sharedfiles/filedetails/?id=3772723979) (277) | Build any fight you want and practise it; nothing written to your save. | A different job: Training Ground builds a situation, Runmobile puts you in the one a real run produced. They coexist. |

Run-history sites are a different thing again: [OP.GG](https://op.gg) held 2.7 million runs on 2026-09-06, and its pages show you the deck a good player ended with.
Runmobile lets you play from the fight they played.

## Works on

- Slay the Spire 2 `v0.111.0`, the build the featured recording and both recorded runs were made on.
- No dependencies. The mod is DLL-only, declares `affects_gameplay: false`, and needs no BaseLib.
- Playing from a recording asks you to run with only Runmobile enabled, and says so in a sentence if another mod is active, because another mod's behaviour cannot be established from the game's content hash.
- Recording your own runs works with other mods loaded; the recording notes which mods were active. A run recorded with a gameplay-affecting, undeclared or unidentified mod loaded is recorded, and refused when you later play from it.
- Each recording is keyed to the build it was made on. When the game updates, the featured runs are re-verified on the new build and the ones that no longer reproduce are retired; your own new recordings are on the new build because you played them there.

## Install

- **Coming soon:** Steam Workshop, with a Nexus mirror.
- Today, from source: `./scripts/install-mod.sh` packages the mod, prepares its private replay runtime from the local game installation, and puts it in the game's own mods directory; `--uninstall` removes it.
  Launch the game through Steam, enable only Runmobile, and open Singleplayer.

## How it works, in one breath

A recorded run is the list of every decision made in it, from the first card pick on, with the run's identity: seed, build, ascension, acts and the unlock state it was generated against.
To open a fight, Runmobile hands those decisions to the real game, in order, until it reaches that fight, checks that the state it arrived in is the recorded one, and hands the game to you.
Nothing is reimplemented: every verb maps onto a method the retail client itself calls.
The same machinery runs headlessly as a command-line arbiter that verifies a recording against what a video shows, and that is where the rest of this file goes.

---

# For contributors

The rest of this file is the project as an engineering artifact: what has been established, how to run it, and how it is laid out.
[AGENTS.md](AGENTS.md) holds the build and test commands and the invariants every change has to respect.

The repository began as a deterministic replay arbiter.
Given a video of somebody's run, it reconstructs the ordered history of what they did, replays it through the real shipped game engine, and checks the result against what the video actually shows.
If everything agrees, the run's verified gameplay history has been reproduced exactly, including hidden gameplay state no video can show, like the position of every random-number stream and the order of the draw pile.
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
./scripts/package-mod.sh                # build the distributable package without game content
./scripts/install-mod.sh                # package, prepare, and install Runmobile
./scripts/install-mod.sh --uninstall    # remove it
./scripts/arbiter adopt-live            # the refusal, from a process that is not a running game
```

The corresponding retail flow was demonstrated with the pre-rename Combat Trainer artifact: opening Singleplayer showed a fourth mode card, `Combat Trainer`, which checked whether that install could reproduce the recording and offered `Enter the fight` when it could.
Winning the fight showed the mod's visual result panel with the player's fight beside the recording's: compact summary figures, card and potion art by turn, and a chart of enemy and player health lost each turn.
The two lines stayed distinct by colour and marker shape, and the panel stated differences without scoring either line or giving a verdict.
The trainer supplied the recording's unlocks, acts, and Ascension 10 in memory, then visibly made the recording's pre-fight decisions and handed over only after the live combat-start state matched the manifest's observed fields and snapshot digest.
Under the renamed `Runmobile` artifact a later session repeated the fight itself - the recording's decisions, the handover, the fight played to a win and to a deliberate loss, and the panel each earns - but not the mode card and the offer that precede them, which are still claimed only for the pre-rename artifact.
See [docs/in-game-host.md](docs/in-game-host.md), [demo/RECORDED-FIGHT-ENTRY.md](demo/RECORDED-FIGHT-ENTRY.md), and [demo/VISUAL-COMPARISON.md](demo/VISUAL-COMPARISON.md); [demo/PLAYBACK-TRANSPORT.md](demo/PLAYBACK-TRANSPORT.md) is the `Runmobile` session, and [docs/mod-ui-direction.md](docs/mod-ui-direction.md) owns what those surfaces are.

The mod also records the player's own runs, on unless `settings.json` in its store says otherwise: every run played becomes a manifest of the same kind under `user://Runmobile/recordings/`, which `gate` judges by the same standard as one transcribed from a video.
[docs/in-game-host.md](docs/in-game-host.md#producing-a-recording-and-checking-it) has the steps for producing one and checking it.

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
No video shows a map coordinate, so where a reconstruction records none the arrival is
derived from the map move its boundary names - the row and column that move carries are
the coordinate - and marked as inferred for that reason.
The shipped video reconstruction above carries one at each of its floor entries and
`--floor` enters them; the committed engine-generated whole-act history carries them as
engine-produced values, and is what `--floor` is demonstrated on:

```bash
./scripts/arbiter enter-fight src/Sts2PilotTrainer.Replay/Fixtures/synthetic-v0111-whole-act.replay.json --floor 5
```

[demo/RECORDED-FIGHT-ENTRY.md](demo/RECORDED-FIGHT-ENTRY.md) has it with its real
output.

A floor arrival can also be reached from the game's own save at that floor instead of by walking the decisions that lead to it.
`./scripts/arbiter floor-snapshot <manifest> --floor <n>` materialises that snapshot, keyed by the whole history that produced it and cached only once a restore in a fresh process has reproduced the digest the recording declares.
`enter-fight --floor <n> --restore` then uses it, and replays as usual where no such snapshot is there.
The boundary is proved the same way either way; only arrivals with a live fight are cached, and [docs/native-replay-format.md](docs/native-replay-format.md) owns why.

`./scripts/arbiter` with no arguments lists the rest: `gate`, `validate`,
`engine-commands`, `verify-seed`, `determinism`, `negative-controls`,
`combat-snapshot`, `floor-snapshot`, `snapshot-restore-probe`, `migrate-manifest`. `engine-commands`
prints which of the game's own members each recorded decision maps onto, says of
every verb it does not map why there is nothing to map it onto, and checks that the
three gameplay paths the engine's test-mode flag would otherwise change still take
retail's branch under this host. `validate` and
`migrate-manifest` need no game, the latter unless it is deriving boundaries;
`migrate-manifest` is the only command that rewrites a manifest on disk, so
reading somebody's evidence never edits it.
It rewrites the manifest in place, or writes to `--out <path>`, which is written even when the input was already in this format, so a script that migrates and then reads its output never meets a missing file.
`--derive-boundaries` additionally replays the run through the real engine and writes in every boundary the history passes - each fight's start, each floor's arrival and each turn - with the digest that replay produced, refusing if the history does not reproduce.
It also writes the arrival checkpoint each floor entry needs, derived from the map move
that boundary names, and does so before the replay as well as after - which is the one
repair a recording written before the recorder sampled the coordinate has.
A floor entry naming no map move is refused rather than repaired into existence.
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
| `manifests/` | The reconstructed run and the map read from the video, plus two runs recorded inside the player's own game. Facts only. |
| `docs/` | [The proof-of-concept path](docs/proof-of-concept-path.md) · [the in-game host](docs/in-game-host.md) · [environment identity](docs/environment-identity.md) · [comparison direction](docs/comparison-direction.md) · [headless fidelity](docs/headless-fidelity.md) · [dependencies](docs/dependencies.md) · [distribution](docs/distribution.md) · [the engine's own replay format](docs/native-replay-format.md) |

## What this repository does not contain

No extracted game assemblies, localization tables or art assets — MegaCrit's property, copied from your own installation at build time.
The only committed images from the client are screenshots of this mod's own UI under `demo/`; no source-video footage of any kind is stored here: no frames, clips or stills.
Only unprotectable facts read from the video, together with the public video id and the timestamps that let anyone re-check each one against the original, are retained.
See [NOTICE](NOTICE).

## Licence

MIT. See [LICENSE](LICENSE) and [NOTICE](NOTICE).

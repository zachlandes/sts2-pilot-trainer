# A night of the retail soak, without the game

*2026-09-19T20:10:36Z by Showboat 0.6.1*
<!-- showboat-id: b596e0f2-0151-48c6-97f3-f2c1c41a0d3a -->

This document runs `scripts/retail-soak.sh` - the nightly retail soak - through its whole lifecycle against the stand-in retail executable the launch tests use, and records what it printed. Every code block below was executed from the repository root on a built tree (`./scripts/build.sh`); the output under it is that run's output. `showboat --workdir .. verify RETAIL-SOAK.md` re-runs the lot, and its diff is the pids, the times and the sandbox's random suffix, and the home directory and the worktree, which are written here as `~` and `<worktree>` - nothing else should.

**The claim being tested.** A night is one command. It holds the machine awake for its own lifetime, installs the mod, writes the soak profile's `settings.json` whole with the night's plan, launches the retail client through `./scripts/retail-client.sh` with `--headless`, waits for the mod to write `soak-done` under the profile's store, releases through the same helper, copies the recordings written under the profile since its launch into dated evidence, runs parity and coverage over that copy beside `manifests/`, prints the third release figure, and exits non-zero on a parity failure or a run the mod could not finish cleanly. Nothing under the store is deleted. The stand-in plays the soak's client: under `--headless` it writes the night's recording into the store and then `soak-done` the way `RetailSoak` writes it, and quits the way `NGame.Quit` does; without the flag it never writes it, which is what makes the flag load-bearing. `RetailSoakScriptTests` holds all of it; this is the same script, watched.

Two things are stood in for and one is real. The executable is `tests/stand-ins/retail-client.sh`, and the process table is a `ps` that lists the stand-in's pid as the game, because the real table names a script by its interpreter. The recording is real: `native-67L571H38L-20260919-091953`, a run the mod itself played and recorded under `--headless` on v0.111.0 during the soak probe of 2026-09-19 - two fights, a loot screen, a give-up, thirty decisions - copied out of the store on this machine into the sandbox's `night/`, from where the stand-in writes it into the sandbox store during each night, because a stand-in plays no run and the script measures only what was written after its launch.

## The sandbox: a home, a stand-in game, a profile pointer, and one real recording

```bash
rm -rf build/soak-demo && mkdir -p build/soak-demo && cd build/soak-demo && HOME_DIR="$PWD/home" && USER_DIR="$HOME_DIR/Library/Application Support/SlayTheSpire2" && GAME="$PWD/install/SlayTheSpire2.app/Contents/MacOS/Slay the Spire 2" && mkdir -p "$(dirname "$GAME")" tools night "$USER_DIR/default/2/modded" "$USER_DIR/Runmobile/default/2/modded/profile1/recordings" && cp ../../tests/stand-ins/retail-client.sh "$GAME" && chmod +x "$GAME" && printf "{\n  \"last_profile_id\": 1,\n  \"schema_version\": 2\n}\n" > "$USER_DIR/default/2/modded/profile.save" && cp "$HOME/Library/Application Support/SlayTheSpire2/Runmobile/default/1/modded/profile3/recordings/native-67L571H38L-20260919-091953."* night/ && cat > tools/ps <<'EOF'
#!/usr/bin/env bash
if [[ -n "${FAKE_GAME_PIDS:-}" && -f "$FAKE_GAME_PIDS" ]]; then
  while read -r pid; do
    if kill -0 "$pid" 2>/dev/null; then printf "%5s %5s %s\n" "$pid" 1 "$FAKE_GAME_EXECUTABLE"; fi
  done < "$FAKE_GAME_PIDS"
fi
EOF
chmod +x tools/ps && cat > env.sh <<EOF
export HOME="$HOME_DIR"
export FAKE_GAME_LOG="$PWD/game.log"
export FAKE_GAME_PIDS="$PWD/game.pids"
export FAKE_GAME_EXECUTABLE="$GAME"
export FAKE_SOAK_DONE="$USER_DIR/Runmobile/default/2/modded/profile1/soak-done"
export FAKE_SOAK_DONE_AFTER=2
export FAKE_SOAK_RECORDS_FROM="$PWD/night"
export PATH="$PWD/tools:\$PATH"
EOF
find . -type f | sort | sed "s|^\./||"
```

```output
env.sh
home/Library/Application Support/SlayTheSpire2/default/2/modded/profile.save
install/SlayTheSpire2.app/Contents/MacOS/Slay the Spire 2
night/native-67L571H38L-20260919-091953.journal.jsonl
night/native-67L571H38L-20260919-091953.replay.json
tools/ps
```

`env.sh` is what the tests set on the script's process: a sandbox `HOME`, where the stand-in writes what it saw, where the stand-in's pid is listed, where the stand-in writes `soak-done` - the path the mod writes it to, under the profile's store - and where it takes the night's recording from. The sandbox store's `recordings/` is empty until a night writes into it, exactly as a fresh soak profile's is.

## The night, as planned: one run, exit 0

```bash
( source build/soak-demo/env.sh && ./scripts/retail-soak.sh --game "$FAKE_GAME_EXECUTABLE" --skip-install --client-id 2 --runs 1 --seeds 67L571H38L --stop-after-minutes 1 --out build/soak-demo/night-1 ); echo "exit $?"
```

```output
20:10:37Z retail soak: 1 run(s) of CHARACTER.IRONCLAD at ascension 0, seeds [67L571H38L], deadline 1m
20:10:37Z store        : <worktree>/build/soak-demo/home/Library/Application Support/SlayTheSpire2/Runmobile/default/2/modded/profile1
20:10:37Z evidence     : build/soak-demo/night-1
20:10:37Z wrote <worktree>/build/soak-demo/home/Library/Application Support/SlayTheSpire2/Runmobile/default/2/modded/profile1/settings.json
20:10:37Z launching: ./scripts/retail-client.sh launch --owner retail-soak 2026-09-19 --client-id 2 --headless --game <worktree>/build/soak-demo/install/SlayTheSpire2.app/Contents/MacOS/Slay the Spire 2
client       : pid 29610
executable   : <worktree>/build/soak-demo/install/SlayTheSpire2.app/Contents/MacOS/Slay the Spire 2
arguments    : --force-steam=off --clientId=2 --headless
save tree    : user://default/2
working dir  : <worktree>/build/soak-demo/home/Library/Application Support/sts2-pilot-trainer/retail-client/cwd.G7Gyfz
log          : <worktree>/build/soak-demo/home/Library/Application Support/sts2-pilot-trainer/retail-client/client.log
record       : <worktree>/build/soak-demo/home/Library/Application Support/sts2-pilot-trainer/retail-client/owner
owner        : retail-soak 2026-09-19
release with : ./scripts/retail-client.sh release
20:10:48Z soak-done arrived after 10s
20:10:48Z soak-done: { "schema": "sts2-pilot-trainer/retail-soak-done/v1", "finished_at_utc": "2026-09-19T20:10:39Z", "runs_planned": 1, "runs_started": 1, "runs": [ { "run": 1, "seed": "STANDIN", "outcome": "ended" } ], "refusal": null, "recorder_version": "stand-in"}
20:10:48Z copied 1 recording(s) to build/soak-demo/night-1/recordings
20:10:48Z parity:
  PARITY      native-67L571H38L-20260919-091953  30 in the journal, 30 replayed
  older recorder native-3LACFJ5NJ371-20260906-015901
              journal written by recorder 'runmobile-recorder/1.0.0.0'; this build's recorder is 'runmobile-recorder/0.2.0', and what changed between them is why the journal is not held to a replay. The manifest still replays on this build, so what it reached is credited to coverage
  older recorder native-9F8CY60C5BK7-20260906-005737
              journal written by recorder 'runmobile-recorder/1.0.0.0'; this build's recorder is 'runmobile-recorder/0.2.0', and what changed between them is why the journal is not held to a replay. The manifest still replays on this build, so what it reached is credited to coverage
  not native  navegreed-OJ-6QXhNgdg
              a vod reconstruction has no journal by construction; nothing per decision to hold a replay to
parity: 1 of 3 native recording(s) (1 compared, 0 of another build, 0 without a journal, 0 with a journal it cannot read, 0 with an integrity other than complete, 0 with a broken continuity, 2 written by an older recorder, 0 refused; 1 not native)
AT PARITY - every recording with a journal replays decision for decision, and none is incomplete
parity artifact: build/soak-demo/night-1/parity.json
20:10:50Z coverage:
points: 522  covered: 21  co-occurrence: 8  excused: 465  uncovered: 0  not projectable: 28  recordings: 4
excused and reached by this corpus: 4
COVERED - every point this build offers is reached by a recording, excused in writing, or one this format cannot count
coverage artifact: build/soak-demo/night-1/coverage.json

20:10:59Z the third figure: parity holds over the night's copy beside manifests/; coverage holds; 0 run(s) to read; evidence in build/soak-demo/night-1
exit 0
```

The plan was written whole, the launch went through the helper with `--headless` last, `soak-done` was written two seconds after the launch and found on the script's first ten-second poll, the release found the client already gone, the one recording the night wrote was copied and never moved, and both numbers hold: the night's copy is one native recording at parity, beside the committed corpus's two older-recorder natives and its one reconstruction. What the stand-in saw is the proof of the launch arguments:

```bash
cat build/soak-demo/game.log
```

```output
cwd	<worktree>/build/soak-demo/home/Library/Application Support/sts2-pilot-trainer/retail-client/cwd.G7Gyfz
entries	
args	--force-steam=off --clientId=2 --headless
SteamAppId	unset
soak-done written; quitting the way NGame.Quit does
```

And what the script wrote: the plan, the night's own `soak-done`, and the evidence directory.

```bash
cat "build/soak-demo/home/Library/Application Support/SlayTheSpire2/Runmobile/default/2/modded/profile1/settings.json"; echo ---; cat build/soak-demo/night-1/soak-done.json; echo ---; find build/soak-demo/night-1 -maxdepth 2 | sort
```

```output
{
  "schema": "sts2-pilot-trainer/runmobile-settings/v1",
  "record_my_runs": true,
  "fetch_run_index": false,
  "sharing_service_url": null,
  "keep_recent_runs": 100000,
  "purge_my_runs": false,
  "show_main_menu_row": null,
  "retail_soak": {
    "runs": 1,
    "seeds": ["67L571H38L"],
    "character": "CHARACTER.IRONCLAD",
    "ascension": 0,
    "stop_after_minutes": 1
  }
}
---
{
  "schema": "sts2-pilot-trainer/retail-soak-done/v1",
  "finished_at_utc": "2026-09-19T20:10:39Z",
  "runs_planned": 1,
  "runs_started": 1,
  "runs": [
    { "run": 1, "seed": "STANDIN", "outcome": "ended" }
  ],
  "refusal": null,
  "recorder_version": "stand-in"
}
---
build/soak-demo/night-1
build/soak-demo/night-1/coverage.json
build/soak-demo/night-1/coverage.txt
build/soak-demo/night-1/launched-at
build/soak-demo/night-1/parity
build/soak-demo/night-1/parity.json
build/soak-demo/night-1/parity.txt
build/soak-demo/night-1/parity/native-67L571H38L-20260919-091953
build/soak-demo/night-1/recordings
build/soak-demo/night-1/recordings/native-67L571H38L-20260919-091953.journal.jsonl
build/soak-demo/night-1/recordings/native-67L571H38L-20260919-091953.replay.json
build/soak-demo/night-1/retail-soak.log
build/soak-demo/night-1/soak-done.json
```

## A parity failure exits 1

The same night with the recording the stand-in writes edited at its journal's fifth decision - the player's health read as 1 before it - which is the shape a recorder timing defect takes in a morning's `parity.txt`.

```bash
( source build/soak-demo/env.sh && J="build/soak-demo/night/native-67L571H38L-20260919-091953.journal.jsonl" && cp "$J" build/soak-demo/journal.good && python3 - "$J" <<'EOF'
import json, sys
path = sys.argv[1]
lines = open(path).read().splitlines()
for i, line in enumerate(lines):
    entry = json.loads(line)
    if entry.get("seq") == 5 and "verb" in entry:
        entry["before"]["player.hp"] = "1"
        lines[i] = json.dumps(entry, separators=(",", ":"))
        break
open(path, "w").write("\n".join(lines) + "\n")
EOF
./scripts/retail-soak.sh --game "$FAKE_GAME_EXECUTABLE" --skip-install --client-id 2 --runs 1 --stop-after-minutes 1 --out build/soak-demo/night-2 ); echo "exit $?"
```

```output
20:10:59Z retail soak: 1 run(s) of CHARACTER.IRONCLAD at ascension 0, seeds [fresh each], deadline 1m
20:10:59Z store        : <worktree>/build/soak-demo/home/Library/Application Support/SlayTheSpire2/Runmobile/default/2/modded/profile1
20:10:59Z evidence     : build/soak-demo/night-2
20:10:59Z wrote <worktree>/build/soak-demo/home/Library/Application Support/SlayTheSpire2/Runmobile/default/2/modded/profile1/settings.json
20:10:59Z launching: ./scripts/retail-client.sh launch --owner retail-soak 2026-09-19 --client-id 2 --headless --game <worktree>/build/soak-demo/install/SlayTheSpire2.app/Contents/MacOS/Slay the Spire 2
client       : pid 52600
executable   : <worktree>/build/soak-demo/install/SlayTheSpire2.app/Contents/MacOS/Slay the Spire 2
arguments    : --force-steam=off --clientId=2 --headless
save tree    : user://default/2
working dir  : <worktree>/build/soak-demo/home/Library/Application Support/sts2-pilot-trainer/retail-client/cwd.v1Dv0y
log          : <worktree>/build/soak-demo/home/Library/Application Support/sts2-pilot-trainer/retail-client/client.log
record       : <worktree>/build/soak-demo/home/Library/Application Support/sts2-pilot-trainer/retail-client/owner
owner        : retail-soak 2026-09-19
release with : ./scripts/retail-client.sh release
20:11:10Z soak-done arrived after 10s
20:11:10Z soak-done: { "schema": "sts2-pilot-trainer/retail-soak-done/v1", "finished_at_utc": "2026-09-19T20:11:01Z", "runs_planned": 1, "runs_started": 1, "runs": [ { "run": 1, "seed": "STANDIN", "outcome": "ended" } ], "refusal": null, "recorder_version": "stand-in"}
20:11:10Z copied 1 recording(s) to build/soak-demo/night-2/recordings
20:11:10Z parity:
  DIVERGED    native-67L571H38L-20260919-091953  30 in the journal, 30 replayed
              decision 5 (PlayCard) before: player.hp: 1 -> 80
  older recorder native-3LACFJ5NJ371-20260906-015901
              journal written by recorder 'runmobile-recorder/1.0.0.0'; this build's recorder is 'runmobile-recorder/0.2.0', and what changed between them is why the journal is not held to a replay. The manifest still replays on this build, so what it reached is credited to coverage
  older recorder native-9F8CY60C5BK7-20260906-005737
              journal written by recorder 'runmobile-recorder/1.0.0.0'; this build's recorder is 'runmobile-recorder/0.2.0', and what changed between them is why the journal is not held to a replay. The manifest still replays on this build, so what it reached is credited to coverage
  not native  navegreed-OJ-6QXhNgdg
              a vod reconstruction has no journal by construction; nothing per decision to hold a replay to
parity: 0 of 3 native recording(s) (1 compared, 0 of another build, 0 without a journal, 0 with a journal it cannot read, 0 with an integrity other than complete, 0 with a broken continuity, 2 written by an older recorder, 0 refused; 1 not native)
NOT AT PARITY - see the recording marked above; a recording of another build is unproven on this one
parity artifact: build/soak-demo/night-2/parity.json
20:11:12Z coverage:
points: 522  covered: 21  co-occurrence: 8  excused: 465  uncovered: 0  not projectable: 28  recordings: 4
excused and reached by this corpus: 4
COVERED - every point this build offers is reached by a recording, excused in writing, or one this format cannot count
coverage artifact: build/soak-demo/night-2/coverage.json

20:11:21Z the third figure: parity does not hold over the night's copy beside manifests/; coverage holds; 0 run(s) to read; evidence in build/soak-demo/night-2
exit 1
```

## A run the mod could not finish exits 4

The journal put back, and the stand-in told to end its one run in `unknown-state` - what `RetailSoak` writes when an event, a room or a screen reached no state it knows and the run was given up through the pause menu. The figure still holds; the run is a finding for `godot.log`, and the exit code says so.

```bash
( source build/soak-demo/env.sh && cp build/soak-demo/journal.good build/soak-demo/night/native-67L571H38L-20260919-091953.journal.jsonl && export FAKE_SOAK_OUTCOME=unknown-state && ./scripts/retail-soak.sh --game "$FAKE_GAME_EXECUTABLE" --skip-install --client-id 2 --runs 1 --stop-after-minutes 1 --out build/soak-demo/night-3 ); echo "exit $?"
```

```output
20:11:21Z retail soak: 1 run(s) of CHARACTER.IRONCLAD at ascension 0, seeds [fresh each], deadline 1m
20:11:21Z store        : <worktree>/build/soak-demo/home/Library/Application Support/SlayTheSpire2/Runmobile/default/2/modded/profile1
20:11:21Z evidence     : build/soak-demo/night-3
20:11:21Z wrote <worktree>/build/soak-demo/home/Library/Application Support/SlayTheSpire2/Runmobile/default/2/modded/profile1/settings.json
20:11:21Z launching: ./scripts/retail-client.sh launch --owner retail-soak 2026-09-19 --client-id 2 --headless --game <worktree>/build/soak-demo/install/SlayTheSpire2.app/Contents/MacOS/Slay the Spire 2
client       : pid 61794
executable   : <worktree>/build/soak-demo/install/SlayTheSpire2.app/Contents/MacOS/Slay the Spire 2
arguments    : --force-steam=off --clientId=2 --headless
save tree    : user://default/2
working dir  : <worktree>/build/soak-demo/home/Library/Application Support/sts2-pilot-trainer/retail-client/cwd.QwNL06
log          : <worktree>/build/soak-demo/home/Library/Application Support/sts2-pilot-trainer/retail-client/client.log
record       : <worktree>/build/soak-demo/home/Library/Application Support/sts2-pilot-trainer/retail-client/owner
owner        : retail-soak 2026-09-19
release with : ./scripts/retail-client.sh release
20:11:31Z soak-done arrived after 10s
20:11:31Z soak-done: { "schema": "sts2-pilot-trainer/retail-soak-done/v1", "finished_at_utc": "2026-09-19T20:11:23Z", "runs_planned": 1, "runs_started": 1, "runs": [ { "run": 1, "seed": "STANDIN", "outcome": "unknown-state" } ], "refusal": null, "recorder_version": "stand-in"}
20:11:31Z copied 1 recording(s) to build/soak-demo/night-3/recordings
20:11:31Z a run ended 'unknown-state'; read godot.log
20:11:31Z parity:
  PARITY      native-67L571H38L-20260919-091953  30 in the journal, 30 replayed
  older recorder native-3LACFJ5NJ371-20260906-015901
              journal written by recorder 'runmobile-recorder/1.0.0.0'; this build's recorder is 'runmobile-recorder/0.2.0', and what changed between them is why the journal is not held to a replay. The manifest still replays on this build, so what it reached is credited to coverage
  older recorder native-9F8CY60C5BK7-20260906-005737
              journal written by recorder 'runmobile-recorder/1.0.0.0'; this build's recorder is 'runmobile-recorder/0.2.0', and what changed between them is why the journal is not held to a replay. The manifest still replays on this build, so what it reached is credited to coverage
  not native  navegreed-OJ-6QXhNgdg
              a vod reconstruction has no journal by construction; nothing per decision to hold a replay to
parity: 1 of 3 native recording(s) (1 compared, 0 of another build, 0 without a journal, 0 with a journal it cannot read, 0 with an integrity other than complete, 0 with a broken continuity, 2 written by an older recorder, 0 refused; 1 not native)
AT PARITY - every recording with a journal replays decision for decision, and none is incomplete
parity artifact: build/soak-demo/night-3/parity.json
20:11:34Z coverage:
points: 522  covered: 21  co-occurrence: 8  excused: 465  uncovered: 0  not projectable: 28  recordings: 4
excused and reached by this corpus: 4
COVERED - every point this build offers is reached by a recording, excused in writing, or one this format cannot count
coverage artifact: build/soak-demo/night-3/coverage.json

20:11:43Z the third figure: parity holds over the night's copy beside manifests/; coverage holds; 1 run(s) to read; evidence in build/soak-demo/night-3
exit 4
```

## A night that ended without soak-done exits 3

A client that quit without finishing - the stand-in exits after two seconds having written nothing, and an earlier night's `soak-done` is still on disk. The script waits for one written after its own launch and reads nothing older.

```bash
( source build/soak-demo/env.sh && export FAKE_SOAK_EXIT_WITHOUT_DONE=1 && ./scripts/retail-soak.sh --game "$FAKE_GAME_EXECUTABLE" --skip-install --client-id 2 --runs 1 --stop-after-minutes 1 --out build/soak-demo/night-4 ); echo "exit $?"
```

```output
20:11:43Z retail soak: 1 run(s) of CHARACTER.IRONCLAD at ascension 0, seeds [fresh each], deadline 1m
20:11:43Z store        : <worktree>/build/soak-demo/home/Library/Application Support/SlayTheSpire2/Runmobile/default/2/modded/profile1
20:11:43Z evidence     : build/soak-demo/night-4
20:11:43Z wrote <worktree>/build/soak-demo/home/Library/Application Support/SlayTheSpire2/Runmobile/default/2/modded/profile1/settings.json
20:11:44Z launching: ./scripts/retail-client.sh launch --owner retail-soak 2026-09-19 --client-id 2 --headless --game <worktree>/build/soak-demo/install/SlayTheSpire2.app/Contents/MacOS/Slay the Spire 2
client       : pid 86262
executable   : <worktree>/build/soak-demo/install/SlayTheSpire2.app/Contents/MacOS/Slay the Spire 2
arguments    : --force-steam=off --clientId=2 --headless
save tree    : user://default/2
working dir  : <worktree>/build/soak-demo/home/Library/Application Support/sts2-pilot-trainer/retail-client/cwd.5fJ9LR
log          : <worktree>/build/soak-demo/home/Library/Application Support/sts2-pilot-trainer/retail-client/client.log
record       : <worktree>/build/soak-demo/home/Library/Application Support/sts2-pilot-trainer/retail-client/owner
owner        : retail-soak 2026-09-19
release with : ./scripts/retail-client.sh release
20:11:56Z no soak-done after 10s; releasing the client
20:11:56Z the night ended without soak-done; nothing is measured. Read <worktree>/build/soak-demo/home/Library/Application Support/SlayTheSpire2/logs/godot.log.
exit 3
```

## A plan the mod refused exits 3, in the morning rather than after the deadline

A plan the client refuses before starting a run - here a character this build has not got - is written to `soak-done` as zero runs with the sentence, and the client quits the way a finished night's does; the stand-in writes that file for the refusal it is told. The script reads the refusal, measures nothing and says so, rather than waiting out the night's whole budget for a client sitting at the main menu.

```bash
( source build/soak-demo/env.sh && export FAKE_SOAK_REFUSAL="this build has no character 'CHARACTER.NOBODY'" && ./scripts/retail-soak.sh --game "$FAKE_GAME_EXECUTABLE" --skip-install --client-id 2 --runs 1 --character CHARACTER.NOBODY --stop-after-minutes 1 --out build/soak-demo/night-5 ); echo "exit $?"; cat build/soak-demo/night-5/soak-done.json
```

```output
20:11:56Z retail soak: 1 run(s) of CHARACTER.NOBODY at ascension 0, seeds [fresh each], deadline 1m
20:11:56Z store        : <worktree>/build/soak-demo/home/Library/Application Support/SlayTheSpire2/Runmobile/default/2/modded/profile1
20:11:56Z evidence     : build/soak-demo/night-5
20:11:56Z wrote <worktree>/build/soak-demo/home/Library/Application Support/SlayTheSpire2/Runmobile/default/2/modded/profile1/settings.json
20:11:56Z launching: ./scripts/retail-client.sh launch --owner retail-soak 2026-09-19 --client-id 2 --headless --game <worktree>/build/soak-demo/install/SlayTheSpire2.app/Contents/MacOS/Slay the Spire 2
client       : pid 183
executable   : <worktree>/build/soak-demo/install/SlayTheSpire2.app/Contents/MacOS/Slay the Spire 2
arguments    : --force-steam=off --clientId=2 --headless
save tree    : user://default/2
working dir  : <worktree>/build/soak-demo/home/Library/Application Support/sts2-pilot-trainer/retail-client/cwd.2cGwRz
log          : <worktree>/build/soak-demo/home/Library/Application Support/sts2-pilot-trainer/retail-client/client.log
record       : <worktree>/build/soak-demo/home/Library/Application Support/sts2-pilot-trainer/retail-client/owner
owner        : retail-soak 2026-09-19
release with : ./scripts/retail-client.sh release
20:12:07Z soak-done arrived after 10s
20:12:07Z soak-done: { "schema": "sts2-pilot-trainer/retail-soak-done/v1", "finished_at_utc": "2026-09-19T20:11:59Z", "runs_planned": 1, "runs_started": 0, "runs": [], "refusal": "this build has no character 'CHARACTER.NOBODY'", "recorder_version": "stand-in"}
20:12:07Z the mod refused the night: this build has no character 'CHARACTER.NOBODY'; nothing is measured. Read <worktree>/build/soak-demo/home/Library/Application Support/SlayTheSpire2/logs/godot.log.
exit 3
{
  "schema": "sts2-pilot-trainer/retail-soak-done/v1",
  "finished_at_utc": "2026-09-19T20:11:59Z",
  "runs_planned": 1,
  "runs_started": 0,
  "runs": [],
  "refusal": "this build has no character 'CHARACTER.NOBODY'",
  "recorder_version": "stand-in"
}
```

## The store is never deleted from, and a profile that is not a soak's is refused

Everything the five nights left in the sandbox store: the recording the nights wrote, both files, the settings the script wrote and the last `soak-done`. Then the refusal that protects a player's profile: a settings file with no `retail_soak` plan in it is somebody's, and the script will not replace it whole unless told to.

```bash
( source build/soak-demo/env.sh && S="$HOME/Library/Application Support/SlayTheSpire2/Runmobile/default/2/modded/profile1" && find "$S" -type f | sed "s|$S/||" | sort && printf "{\"schema\":\"sts2-pilot-trainer/runmobile-settings/v1\",\"sharing_service_url\":\"https://example.test/\"}\n" > "$S/settings.json" && ./scripts/retail-soak.sh --game "$FAKE_GAME_EXECUTABLE" --skip-install --client-id 2 --runs 1 --stop-after-minutes 1 --out build/soak-demo/night-6; echo "exit $?"; cat "$S/settings.json" )
```

```output
recordings/native-67L571H38L-20260919-091953.journal.jsonl
recordings/native-67L571H38L-20260919-091953.replay.json
settings.json
soak-done
The settings file <worktree>/build/soak-demo/home/Library/Application Support/SlayTheSpire2/Runmobile/default/2/modded/profile1/settings.json is not a soak profile's (it carries no retail_soak plan).
This script replaces it whole. Use a dedicated tree (--client-id) or pass --adopt-profile once.
exit 2
{"schema":"sts2-pilot-trainer/runmobile-settings/v1","sharing_service_url":"https://example.test/"}
```

## What a real night looks like

The same script with no `--game` and no `--skip-install` finds the Steam installation, installs the mod, and launches the real client headless in the isolated tree `--client-id` names; the game's two one-time clicks are owed once per tree by a person, and `docs/in-game-host.md`, "The retail soak", owns the rest. The first night has not run: `docs/release-bar.md`, "The third number", records the command and where its evidence lands, and the first figure is written there the morning after.

```bash
./scripts/retail-soak.sh --help 2>&1 | head -32
```

```output
usage: retail-soak.sh [--runs <n>] [--seeds <a,b,c>] [--character <CHARACTER.X>]
                      [--ascension <a>] [--stop-after-minutes <m>]
                      [--client-id <n>] [--profile <p>] [--adopt-profile]
                      [--game <executable>] [--skip-install] [--out <dir>]

--runs                how many runs the night plays (default 6)
--seeds               the seeds the runs use in order, cycling; empty for a fresh
                      seed per run (default empty)
--character           the game's own character id (default CHARACTER.IRONCLAD)
--ascension           the ascension every run starts at (default 0)
--stop-after-minutes  the night's deadline, from the moment the mod arms (default 360)
--client-id           the isolated save tree user://default/<n>/ (default 2)
--profile             the profile under that tree; default: the tree's own pointer
--adopt-profile       replace a settings.json that is not already a soak's
--game                the retail executable; default: the Steam installation
--skip-install        do not run ./scripts/install-mod.sh first (the stand-in lifecycle)
--out                 where the night's evidence goes (default build/evidence/soak/<date>)

Exit 0 when soak-done arrived, every run ended cleanly, and parity and coverage
hold over the night's copy beside manifests/; 1 when parity or coverage does not
hold; 3 when the night measured nothing - the launch was refused, the night ended
without soak-done, the mod refused the plan before starting a run, or it recorded
nothing; 4 when a run ended in a state the mod refused - unknown-state, failed,
timed-out or client-unusable - which is a finding to read in godot.log; 2 on a
usage error.
```

# Launching the retail client through one owner

*2026-09-16T11:56:49Z by Showboat 0.6.1*
<!-- showboat-id: abc384fc-cca0-4126-9d8b-e3387b0bd91e -->

This document runs the retail-client helper against the real v0.111.0 installation on the captain's machine and records what it printed. Every code block below was executed from the repository root; the output under it is that run's output. `showboat --workdir .. verify RETAIL-CLIENT-LAUNCH.md` re-runs the lot, and its diff is the pids, the times and the ledger's file count, which differ per session, and the home directory, the worktree and the launching account, which are written here as `~`, `<worktree>` and `<user>@<host>` - nothing else should.

**The claim being tested.** The two launch failures that kept recurring during retail testing - Steam's `Game already running` and the game's `No appID found` - cannot be reached through `./scripts/retail-client.sh`: it asks Steam nothing, refuses while any Slay the Spire 2 client exists, launches the retail executable with MegaCrit's own `--force-steam=off` and an explicit `--clientId` from an empty working directory, writes one ownership record, and releases exactly that process with TERM. `RetailClientLaunchTests` holds all of that without the game; this is the same helper against the game.

The Steam client was not running at all during this session, so nothing here could have involved it even by accident. Nothing drives the display: the client is launched to its main menu and released from the shell.

## Before: no client, no Steam, and a ledger of everything the session must not change

```bash
./scripts/retail-client.sh status; echo "exit $?"; printf "steam client processes: %s\n" "$(ps -Ao comm= | grep -c steam_osx)"
```

```output
record       : ~/Library/Application Support/sts2-pilot-trainer/retail-client/owner
record state : none - no client is owned by this helper

clients      : none
exit 0
steam client processes: 0
```

```bash
./scripts/protected-files.sh snapshot build/retail-client-launch/before.ledger; printf "steam gameprocess_log lines: %s\n" "$(wc -l < "$HOME/Library/Application Support/Steam/logs/gameprocess_log.txt")"
```

```output
ledger       : <worktree>/build/retail-client-launch/before.ledger
files        : 686
user         : ~/Library/Application Support/SlayTheSpire2
mods         : ~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods
steam gameprocess_log lines:     1035
```

## The launch

One command. It finds the retail executable, sees no client, creates an empty working directory of its own, starts the executable with `--force-steam=off --clientId=1` detached from this shell, waits for the process table to show it as the game, and writes the record.

```bash
./scripts/retail-client.sh launch --owner "steamless-retail-launch-owner crewmate"; echo "exit $?"
```

```output
client       : pid 35945
executable   : ~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/Slay the Spire 2
arguments    : --force-steam=off --clientId=1
save tree    : user://default/1
working dir  : ~/Library/Application Support/sts2-pilot-trainer/retail-client/cwd.3UV2wk
log          : ~/Library/Application Support/sts2-pilot-trainer/retail-client/client.log
record       : ~/Library/Application Support/sts2-pilot-trainer/retail-client/owner
owner        : steamless-retail-launch-owner crewmate
release with : ./scripts/retail-client.sh release
exit 0
```

Thirty seconds later - well past the time the client takes to reach its main menu - the record is live, the process table shows the one client, and `status` exits 1 because a client exists.

```bash
sleep 30; ./scripts/retail-client.sh status; echo "exit $?"
```

```output
record       : ~/Library/Application Support/sts2-pilot-trainer/retail-client/owner
record state : live
  pid                35945
  executable         ~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/Slay the Spire 2
  arguments          --force-steam=off --clientId=1
  client_id          1
  save_tree          user://default/1
  working_directory  ~/Library/Application Support/sts2-pilot-trainer/retail-client/cwd.3UV2wk
  log                ~/Library/Application Support/sts2-pilot-trainer/retail-client/client.log
  owner              steamless-retail-launch-owner crewmate
  launched_by        <user>@<host>
  launched_from      <worktree> (pid 35779)
  launched_at        2026-09-16T11:57:09Z

clients      :
  pid 35945  owned by steamless-retail-launch-owner crewmate  (parent 1)
  ~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/Slay the Spire 2
exit 1
```

What the client wrote as it came up, from the helper's own capture of its stdout and stderr and from the game's log. The skip line is the Steamless branch firing; the absence of any Steamworks line is the point; the mod loads; the save root is the isolated non-Steam tree.

```bash
log="$HOME/Library/Application Support/sts2-pilot-trainer/retail-client/client.log"; echo "--- client.log ($(wc -l < "$log" | tr -d " ") lines)"; grep -E "Command Line Args|Steam initialization skipped|Steamworks|No appID|Loading assembly DLL|RUNNING MODDED|Profile-scoped data path|Time to main menu" "$log" | head -12; echo "--- godot.log"; grep -E "Command Line Args|Steam initialization skipped|Steamworks|No appID|RUNNING MODDED|Profile-scoped data path|Time to main menu" "$HOME/Library/Application Support/SlayTheSpire2/logs/godot.log" | head -12
```

```output
--- client.log (213 lines)
[INFO] Steam initialization skipped (editor mode). Use --force-steam to enable.
[INFO] Loading assembly DLL ~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods/Runmobile/Runmobile.dll
[INFO]  --- RUNNING MODDED! --- Loaded 1 mods (4 total)
[INFO] Profile-scoped data path initialized: user://default/1/modded/profile3
[WARN] Cannot initialize Steam Input because Steamworks is not initialized. Falling back to standard input.
Command Line Args: --force-steam=off --clientId=1
User Command Line Args: 
[INFO] [Startup] Time to main menu (Godot ticks): 2212ms
[INFO] [Startup] Time to main menu: 2,218ms
--- godot.log
[INFO] Steam initialization skipped (editor mode). Use --force-steam to enable.
[INFO]  --- RUNNING MODDED! --- Loaded 1 mods (4 total)
[INFO] Profile-scoped data path initialized: user://default/1/modded/profile3
[WARN] Cannot initialize Steam Input because Steamworks is not initialized. Falling back to standard input.
Command Line Args: --force-steam=off --clientId=1
User Command Line Args: 
[INFO] [Startup] Time to main menu (Godot ticks): 2212ms
[INFO] [Startup] Time to main menu: 2,218ms
```

## A second worker asks for a launch

This is the situation that used to end in Steam's `Game already running` dialog. The helper refuses before anything starts, names the client and who owns it, and says how it is released. Nothing was launched and Steam was not asked.

```bash
./scripts/retail-client.sh launch --owner "a second worker"; echo "exit $?"
```

```output
A Slay the Spire 2 client already exists; refusing to launch a second one.
  pid 35945  owned by steamless-retail-launch-owner crewmate  (parent 1)
  ~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/Slay the Spire 2
Release it through whoever owns it - './scripts/retail-client.sh release' for one this helper launched - and launch again.
exit 1
```

## Release

TERM to exactly the pid the record names, then a wait. With only Runmobile loaded the teardown is a matter of seconds; the record and the helper's own working directory go once the process is gone.

```bash
./scripts/retail-client.sh release --wait 120; echo "exit $?"
```

```output
Sent TERM to pid 35945 (owner steamless-retail-launch-owner crewmate).
pid 35945 exited after 1s; the record is cleared.
exit 0
```

## After: no client, Steam untouched, and what the session changed

```bash
./scripts/retail-client.sh status; echo "exit $?"; printf "steam client processes: %s\n" "$(ps -Ao comm= | grep -c steam_osx)"; printf "steam gameprocess_log lines: %s\n" "$(wc -l < "$HOME/Library/Application Support/Steam/logs/gameprocess_log.txt")"; ls "$HOME/Library/Application Support/sts2-pilot-trainer/retail-client/"
```

```output
record       : ~/Library/Application Support/sts2-pilot-trainer/retail-client/owner
record state : none - no client is owned by this helper

clients      : none
exit 0
steam client processes: 0
steam gameprocess_log lines:     1035
client.log
```

```bash
./scripts/protected-files.sh compare build/retail-client-launch/before.ledger; echo "exit $?"
```

```output
ledger       : <worktree>/build/retail-client-launch/before.ledger
user         : ~/Library/Application Support/SlayTheSpire2
mods         : ~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods

protected files (must not change):
  nothing

user://Runmobile/ (this mod's own store):
  nothing

the game's own churn (written on any launch, mod or not):
  added    user/logs/godot2026-09-16T04.57.09.log
  changed  user/logs/godot.log
  removed  user/logs/godot2026-09-15T22.58.25.log
exit 0
```

## Reading it

The client came up on the Steamless branch - `Steam initialization skipped`, no `Steamworks: attempting initialization`, no `No appID found` - with Runmobile the one mod loaded and its save root at `user://default/1/modded/profile3`, inside the isolated non-Steam tree; the main menu took 2.2 seconds. Steam had no part in it: the Steam client was not running before, during or after, and its `gameprocess_log.txt` gained no line. The second launch was refused, naming the owner, before anything started. The release was TERM and one second, because only this mod was loaded in that tree. The ledger says the session changed nothing the mod must not change and nothing in the mod's own store - only the game's own log rotation, which every launch writes with or without a mod. The helper's own state directory ends the session holding `client.log` alone: the record and the working directory went with the release.

What this does not show: a fight, a capture, or any input to the client. The client was launched to its main menu and released from the shell, which is the whole of what the helper owns.

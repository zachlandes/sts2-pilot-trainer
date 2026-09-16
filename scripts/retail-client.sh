#!/usr/bin/env bash
# The one way a retail Slay the Spire 2 client is started, watched and stopped here.
#
#   ./scripts/retail-client.sh launch [--owner <label>] [--client-id <n>] [-- <game args>]
#   ./scripts/retail-client.sh status
#   ./scripts/retail-client.sh release [--wait <seconds>]
#
# Two launch failures kept recurring during retail testing, and both are avoidable
# by construction rather than by care:
#
#   "Game already running"   Steam's refusal when it is asked to launch while a
#                            client already exists - one worker's client, another
#                            worker's request. Steam is never asked here at all,
#                            and a launch refuses first while any client exists.
#   "No appID found"         the retail executable opened directly: Steamworks
#                            initialises, finds no app id, and the client stops on
#                            an error popup having loaded nothing. Every launch here
#                            carries MegaCrit's own --force-steam=off, which skips
#                            Steamworks entirely, from a working directory that is
#                            empty by construction.
#
# The launch is the one an investigation proved end to end on v0.111.0: the
# retail executable, --force-steam=off, a neutral empty working directory, and an
# explicit --clientId so the session lives in the isolated non-Steam save tree
# user://default/<client id>/ and never in the player's Steam tree. It does not
# initialise Steamworks, cannot disconnect a Steam session on another machine, and
# constructs no cloud store. docs/in-game-host.md, "Launching the retail client",
# owns the procedure and its evidence.
#
# Ownership is a record, not a convention. The client this launched is written
# down - pid, command line, save tree, working directory, who launched it and from
# where - in one file every worker on the machine reads, so a second worker learns
# whose client is running instead of asking Steam and being told "already running".
# `release` stops exactly the process that record names, with TERM, and waits: the
# retail client always accepts TERM and takes anywhere from three seconds to forty
# minutes to finish tearing down, so a slow exit is slow and never hung. Nothing
# here sends any other signal, and nothing here signals a process the record does
# not name.
#
# Writes only under its own state directory: the record, the client's stdout and
# stderr, the neutral working directory and a lock around launching. Never into
# the game's user data, never into the installation, and never a steam_appid.txt.
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
usage: retail-client.sh launch [--owner <label>] [--client-id <n>] [--game <executable>] [-- <game args>]
       retail-client.sh status
       retail-client.sh release [--wait <seconds>]

launch   start the retail client with --force-steam=off from an empty working
         directory, refusing while any Slay the Spire 2 client exists, and write
         the ownership record. --client-id selects the isolated save tree
         user://default/<n>/ (default 1). Arguments after -- go to the game;
         --force-steam and --clientId among them are refused.
status   print the ownership record and every client that exists. Exit 0 when
         none does, 1 when one does.
release  send TERM to the client the record names and wait up to --wait seconds
         (default 60) for it to exit; exit 3 and keep the record while it is
         still tearing down, so a later release keeps waiting. Never force-kills,
         and never signals a client the record does not name.

The game executable is discovered from the Steam installation, or named with
--game or STS2_GAME_EXECUTABLE.
EOF
}

command="${1:-}"
case "$command" in
  launch|status|release) shift ;;
  -h|--help|help) usage; exit 0 ;;
  *) usage; exit 2 ;;
esac

game="${STS2_GAME_EXECUTABLE:-}"
owner=""
client_id="1"
wait_seconds="60"
extra_args=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --owner) owner="$2"; shift 2 ;;
    --client-id) client_id="$2"; shift 2 ;;
    --game) game="$2"; shift 2 ;;
    --wait) wait_seconds="$2"; shift 2 ;;
    --) shift; extra_args=("$@"); break ;;
    *) echo "unknown argument: $1" >&2; usage; exit 2 ;;
  esac
done

case "$OSTYPE" in
  darwin*) state="$HOME/Library/Application Support/sts2-pilot-trainer/retail-client" ;;
  *) state="${XDG_STATE_HOME:-$HOME/.local/state}/sts2-pilot-trainer/retail-client" ;;
esac
record="$state/owner"
client_log="$state/client.log"
lock="$state/launch.lock"

# The executable's own name, which is what the process table shows for it. The
# probe established the reading as `ps -Ao comm | grep -c "MacOS/Slay the Spire 2$"`;
# this is that reading over every row, so a client launched by Steam, by a hand
# shell or by this helper is found the same way.
client_name="Slay the Spire 2"

# pid, ppid and the executable of every process. `comm` rather than `args`: an
# argument list that names the game - this helper's own --game, a grep for it, a
# shell waiting on it - is how a process check came to count itself.
process_table() {
  ps -Ao pid=,ppid=,comm=
}

# The rows of the process table that are a retail client, as "pid<TAB>ppid<TAB>comm".
clients() {
  local pid ppid comm
  while read -r pid ppid comm; do
    [[ -z "$pid" ]] && continue
    if [[ "$comm" == "$client_name" || "$comm" == *"/$client_name" || ( -n "$game" && "$comm" == "$game" ) ]]; then
      printf '%s\t%s\t%s\n' "$pid" "$ppid" "$comm"
    fi
  done < <(process_table)
}

record_value() {
  [[ -f "$record" ]] || return 0
  awk -F'\t' -v key="$1" '$1 == key { print substr($0, length(key) + 2); exit }' "$record"
}

alive() { kill -0 "$1" 2>/dev/null; }

# Whether the record names a live retail client: the pid is alive and the process
# table still shows it as the game, so a reused pid is never mistaken for it.
record_is_live() {
  local pid
  pid="$(record_value pid)"
  [[ -n "$pid" ]] || return 1
  alive "$pid" || return 1
  clients | awk -F'\t' -v pid="$pid" '$1 == pid { found = 1 } END { exit !found }'
}

# The one installation this launch is established on. Elsewhere the executable is
# named rather than guessed.
discover_game() {
  local candidate
  candidate="$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/$client_name"
  if [[ -x "$candidate" ]]; then game="$candidate"; return 0; fi
  return 1
}

discover_user_dir() {
  local candidate
  for candidate in \
    "$HOME/Library/Application Support/SlayTheSpire2" \
    "$HOME/.local/share/SlayTheSpire2" \
    "${APPDATA:-}/SlayTheSpire2"
  do
    if [[ -n "$candidate" && -d "$candidate" ]]; then echo "$candidate"; return 0; fi
  done
  return 1
}

# One launch at a time on the machine, so two workers cannot both see no client and
# both start one. A lock left by a process that is gone is broken, not honoured.
take_lock() {
  local holder
  mkdir -p "$state"
  if mkdir "$lock" 2>/dev/null; then
    echo "$$" > "$lock/pid"
    return 0
  fi
  holder="$(cat "$lock/pid" 2>/dev/null || true)"
  if [[ -n "$holder" ]] && alive "$holder"; then
    echo "Another launch or release is in progress (pid $holder). Refusing to run beside it." >&2
    exit 1
  fi
  rm -rf "$lock"
  mkdir "$lock"
  echo "$$" > "$lock/pid"
}

print_record() {
  local key value
  while IFS=$'\t' read -r key value; do
    [[ "$key" == \#* ]] && continue
    printf '  %-18s %s\n' "$key" "$value"
  done < "$record"
}

describe_client() {
  local pid="$1" ppid="$2" comm="$3" owned_pid
  owned_pid="$(record_value pid)"
  if [[ -n "$owned_pid" && "$pid" == "$owned_pid" ]]; then
    printf '  pid %s  owned by %s  (parent %s)\n  %s\n' "$pid" "$(record_value owner)" "$ppid" "$comm"
  else
    printf '  pid %s  not launched by this helper  (parent %s)\n  %s\n' "$pid" "$ppid" "$comm"
  fi
}

clear_record() {
  local cwd
  cwd="$(record_value working_directory)"
  if [[ -n "$cwd" && -d "$cwd" ]]; then
    case "$cwd" in
      "$state"/cwd.*) rm -rf "$cwd" ;;
    esac
  fi
  rm -f "$record"
}

# ---------------------------------------------------------------- status

if [[ "$command" == status ]]; then
  existing="$(clients)"
  echo "record       : $record"
  if [[ -f "$record" ]]; then
    if record_is_live; then
      echo "record state : live"
    else
      echo "record state : stale - the client it names is gone; the next launch or release clears it"
    fi
    print_record
  else
    echo "record state : none - no client is owned by this helper"
  fi
  echo
  if [[ -z "$existing" ]]; then
    echo "clients      : none"
    exit 0
  fi
  echo "clients      :"
  while IFS=$'\t' read -r pid ppid comm; do
    describe_client "$pid" "$ppid" "$comm"
  done <<< "$existing"
  exit 1
fi

# ---------------------------------------------------------------- release

if [[ "$command" == release ]]; then
  if ! [[ "$wait_seconds" =~ ^[0-9]+$ ]]; then
    echo "--wait takes a whole number of seconds, not '$wait_seconds'." >&2
    exit 2
  fi
  take_lock
  trap 'rm -rf "$lock"' EXIT

  if [[ ! -f "$record" ]]; then
    existing="$(clients)"
    if [[ -n "$existing" ]]; then
      echo "No ownership record, and a Slay the Spire 2 client exists that this helper did not launch:" >&2
      while IFS=$'\t' read -r pid ppid comm; do
        describe_client "$pid" "$ppid" "$comm" >&2
      done <<< "$existing"
      echo "Refusing to signal a client this helper does not own. Whoever launched it releases it." >&2
      exit 1
    fi
    echo "Nothing to release: no ownership record and no client."
    exit 0
  fi

  pid="$(record_value pid)"
  if ! record_is_live; then
    echo "The client the record named (pid $pid, owner $(record_value owner)) is already gone; clearing the record."
    clear_record
    exit 0
  fi

  if [[ -n "$(record_value term_sent_at)" ]]; then
    echo "TERM was already sent to pid $pid at $(record_value term_sent_at); waiting for it to finish tearing down."
  else
    kill -TERM "$pid"
    printf 'term_sent_at\t%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" >> "$record"
    echo "Sent TERM to pid $pid (owner $(record_value owner))."
  fi

  waited=0
  while alive "$pid" && [[ "$waited" -lt "$wait_seconds" ]]; do
    sleep 1
    waited=$((waited + 1))
  done

  if alive "$pid"; then
    echo "pid $pid is still exiting after ${waited}s. The retail client always accepts TERM and can take" >&2
    echo "minutes to finish; a slow teardown is slow, not hung. The record is kept - run release again" >&2
    echo "to keep waiting. It is never force-killed." >&2
    exit 3
  fi

  echo "pid $pid exited after ${waited}s; the record is cleared."
  clear_record
  exit 0
fi

# ---------------------------------------------------------------- launch

if ! [[ "$client_id" =~ ^[1-9][0-9]*$ ]]; then
  echo "--client-id must be a positive integer naming the isolated save tree user://default/<n>/, not '$client_id'." >&2
  exit 2
fi

for arg in "${extra_args[@]+"${extra_args[@]}"}"; do
  case "$arg" in
    --force-steam*|--clientId*)
      echo "Refusing to pass '$arg' to the game: --force-steam=off and --clientId are this helper's to set." >&2
      exit 2
      ;;
  esac
done

# An app-id override is the other transport Steamworks reads an app id from, and
# it is how a launch would authenticate against the player's own Steam account.
for variable in SteamAppId SteamGameId; do
  if [[ -n "${!variable:-}" ]]; then
    echo "Refusing to launch with $variable set in the environment: it is an app-id override for Steamworks." >&2
    exit 2
  fi
done

if [[ -z "$game" ]] && ! discover_game; then
  echo "Could not find the Slay the Spire 2 executable. Name it with --game <executable> or STS2_GAME_EXECUTABLE." >&2
  exit 2
fi
if [[ ! -x "$game" ]]; then
  echo "Not an executable: $game" >&2
  exit 2
fi
game="$(cd "$(dirname "$game")" && pwd -P)/$(basename "$game")"

take_lock
trap 'rm -rf "$lock"' EXIT

existing="$(clients)"
if [[ -n "$existing" ]]; then
  echo "A Slay the Spire 2 client already exists; refusing to launch a second one." >&2
  while IFS=$'\t' read -r pid ppid comm; do
    describe_client "$pid" "$ppid" "$comm" >&2
  done <<< "$existing"
  echo "Release it through whoever owns it - './scripts/retail-client.sh release' for one this helper launched - and launch again." >&2
  exit 1
fi

if [[ -f "$record" ]]; then
  echo "Clearing a stale ownership record: pid $(record_value pid) (owner $(record_value owner)) is gone."
  clear_record
fi

if user_dir="$(discover_user_dir)"; then
  save_tree="$user_dir/default/$client_id"
else
  save_tree="user://default/$client_id"
fi

launched_from="$PWD"
launched_by="$(id -un)@$(hostname -s 2>/dev/null || hostname)"
if [[ -z "$owner" ]]; then owner="$launched_by from $launched_from"; fi

# A working directory that is empty by construction, so no steam_appid.txt can be
# beside the launch. It is the helper's own and goes with the record.
cwd="$(mktemp -d "$state/cwd.XXXXXX")"
if [[ -n "$(ls -A "$cwd")" ]]; then
  echo "The fresh working directory $cwd is not empty; refusing to launch from it." >&2
  exit 2
fi

arguments=(--force-steam=off "--clientId=$client_id")
if [[ "${#extra_args[@]}" -gt 0 ]]; then arguments+=("${extra_args[@]}"); fi

: > "$client_log"
(
  cd "$cwd"
  exec nohup "$game" "${arguments[@]}" < /dev/null >> "$client_log" 2>&1
) &
pid=$!
disown "$pid" 2>/dev/null || true

# The process is the game once it has exec'd and the table says so. Ten seconds
# is far past what that takes; a process that died in the meantime is a failed
# launch, reported with what it wrote.
recognised=0
for _ in $(seq 1 20); do
  if ! alive "$pid"; then break; fi
  if clients | awk -F'\t' -v pid="$pid" '$1 == pid { found = 1 } END { exit !found }'; then
    recognised=1
    break
  fi
  sleep 0.5
done

if [[ "$recognised" == 0 ]]; then
  if alive "$pid"; then
    # Our own child, not a client the table recognises: stop it rather than leave
    # an unowned process behind.
    kill -TERM "$pid" 2>/dev/null || true
    echo "pid $pid did not become a Slay the Spire 2 client within ten seconds; it was sent TERM." >&2
  else
    echo "The client exited during launch." >&2
  fi
  if [[ -s "$client_log" ]]; then
    echo "Its output:" >&2
    tail -n 20 "$client_log" | sed 's/^/  /' >&2
  fi
  rm -rf "$cwd"
  exit 1
fi

record_tmp="$record.$$.tmp"
{
  printf '# sts2-pilot-trainer retail-client owner v1\n'
  printf 'pid\t%s\n' "$pid"
  printf 'executable\t%s\n' "$game"
  printf 'arguments\t%s\n' "${arguments[*]}"
  printf 'client_id\t%s\n' "$client_id"
  printf 'save_tree\t%s\n' "$save_tree"
  printf 'working_directory\t%s\n' "$cwd"
  printf 'log\t%s\n' "$client_log"
  printf 'owner\t%s\n' "$owner"
  printf 'launched_by\t%s\n' "$launched_by"
  printf 'launched_from\t%s (pid %s)\n' "$launched_from" "$PPID"
  printf 'launched_at\t%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
} > "$record_tmp"
mv "$record_tmp" "$record"

echo "client       : pid $pid"
echo "executable   : $game"
echo "arguments    : ${arguments[*]}"
echo "save tree    : $save_tree"
echo "working dir  : $cwd"
echo "log          : $client_log"
echo "record       : $record"
echo "owner        : $owner"
echo "release with : ./scripts/retail-client.sh release"
exit 0

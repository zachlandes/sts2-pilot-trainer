#!/usr/bin/env bash
# One night of the retail soak: the retail client, headless, playing recorded runs
# through the mod until the plan is done, and the two release numbers over what it
# recorded.
#
#   ./scripts/retail-soak.sh [--runs <n>] [--seeds <a,b,c>] [--character <CHARACTER.X>]
#                            [--ascension <a>] [--stop-after-minutes <m>]
#                            [--client-id <n>] [--profile <p>] [--adopt-profile]
#                            [--game <executable>] [--skip-install] [--out <dir>]
#
# In order: hold the machine awake for exactly this script's lifetime (caffeinate -i),
# install the mod, write the soak profile's settings.json whole with the night's plan,
# launch the retail client through ./scripts/retail-client.sh with --headless, wait
# for the mod to write `soak-done` under the profile's store or for the night's
# deadline, release through the same helper, copy the recordings the night made into
# dated build evidence, run parity and coverage over that copy beside the committed
# manifests, print the third release figure, and exit non-zero on a parity failure or
# a run the mod could not finish cleanly. The night's recordings are the ones written
# under the profile's recordings/ since this launch, so the figure is this night's and
# not the profile's whole history; a profile that has run before keeps every earlier
# night where it is. Nothing under the store is ever deleted by this script; retention
# is the soak profile's own setting and it is written to keep every run.
#
# The soak profile is `user://default/<client id>/modded/profile<p>/`, the tree the
# helper's --client-id selects and the profile that tree's own pointer names. It is a
# dedicated profile: the settings file is replaced whole, so a profile whose settings
# are not already a soak's is refused unless --adopt-profile says it is meant. The
# game's two one-time clicks (accept the mods warning, disable other mods) are owed
# once per tree by a person; docs/in-game-host.md, "Launching the retail client".
#
# The plan is read by RetailSoakModule inside the client; RetailSoak.cs owns what a
# night does with it and docs/release-bar.md owns what the figure means.
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
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
EOF
}

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

runs=6
seeds=""
character="CHARACTER.IRONCLAD"
ascension=0
stop_after_minutes=360
client_id=2
profile=""
adopt_profile=0
game="${STS2_GAME_EXECUTABLE:-}"
skip_install=0
out=""
while [[ $# -gt 0 ]]; do
  case "$1" in
    --runs) runs="$2"; shift 2 ;;
    --seeds) seeds="$2"; shift 2 ;;
    --character) character="$2"; shift 2 ;;
    --ascension) ascension="$2"; shift 2 ;;
    --stop-after-minutes) stop_after_minutes="$2"; shift 2 ;;
    --client-id) client_id="$2"; shift 2 ;;
    --profile) profile="$2"; shift 2 ;;
    --adopt-profile) adopt_profile=1; shift ;;
    --game) game="$2"; shift 2 ;;
    --skip-install) skip_install=1; shift ;;
    --out) out="$2"; shift 2 ;;
    -h|--help|help) usage; exit 0 ;;
    *) echo "unknown argument: $1" >&2; usage; exit 2 ;;
  esac
done

for numeric in runs ascension stop_after_minutes client_id; do
  if ! [[ "${!numeric}" =~ ^[0-9]+$ ]]; then
    echo "--${numeric//_/-} takes a whole number, not '${!numeric}'." >&2
    exit 2
  fi
done
if [[ "$runs" -lt 1 || "$stop_after_minutes" -lt 1 || "$client_id" -lt 1 ]]; then
  echo "--runs, --stop-after-minutes and --client-id are each at least 1." >&2
  exit 2
fi
if [[ "$character" != CHARACTER.* ]]; then
  echo "--character takes the game's own id, which reads CHARACTER.IRONCLAD, not '$character'." >&2
  exit 2
fi

# Where the game keeps user:// on this platform, and the soak profile under it. The
# tree's own pointer names the profile the game will play as, which is the one the
# mod's store is scoped by.
case "$OSTYPE" in
  darwin*) user_dir="$HOME/Library/Application Support/SlayTheSpire2" ;;
  *) user_dir="${XDG_DATA_HOME:-$HOME/.local/share}/SlayTheSpire2" ;;
esac
tree="$user_dir/default/$client_id"
if [[ -z "$profile" ]]; then
  pointer="$tree/modded/profile.save"
  if [[ ! -f "$pointer" ]]; then
    echo "No profile pointer at $pointer: the tree user://default/$client_id has not been played modded yet." >&2
    echo "Launch it once by hand (./scripts/retail-client.sh launch --client-id $client_id), do the game's two" >&2
    echo "one-time clicks, pick a profile, quit, and run this again - or name the profile with --profile." >&2
    exit 2
  fi
  profile="$(sed -n 's/.*"last_profile_id"[[:space:]]*:[[:space:]]*\([0-9][0-9]*\).*/\1/p' "$pointer" | head -n 1)"
  if [[ -z "$profile" ]]; then
    echo "The profile pointer $pointer names no last_profile_id; name the profile with --profile." >&2
    exit 2
  fi
fi
store="$user_dir/Runmobile/default/$client_id/modded/profile$profile"
settings="$store/settings.json"
recordings="$store/recordings"
done_file="$store/soak-done"

if [[ -z "$out" ]]; then out="build/evidence/soak/$(date -u +%Y-%m-%d)"; fi
mkdir -p "$out"
log="$out/retail-soak.log"
say() { printf '%s %s\n' "$(date -u +%H:%M:%SZ)" "$*" | tee -a "$log"; }

# A settings file this script did not write is somebody's, and is replaced only on
# request: the file is written whole, so a player's sharing url and menu choice would
# go with it.
if [[ -f "$settings" && "$adopt_profile" == 0 ]] && ! grep -q '"retail_soak"' "$settings"; then
  echo "The settings file $settings is not a soak profile's (it carries no retail_soak plan)." >&2
  echo "This script replaces it whole. Use a dedicated tree (--client-id) or pass --adopt-profile once." >&2
  exit 2
fi

# Held awake for exactly this script's lifetime and released with it
caffeinate_pid=""
if command -v caffeinate >/dev/null 2>&1; then
  caffeinate -i -w $$ &
  caffeinate_pid=$!
fi
cleanup() {
  if [[ -n "$caffeinate_pid" ]]; then kill "$caffeinate_pid" 2>/dev/null || true; fi
}
trap cleanup EXIT

say "retail soak: $runs run(s) of $character at ascension $ascension, seeds [${seeds:-fresh each}], deadline ${stop_after_minutes}m"
say "store        : $store"
say "evidence     : $out"

if [[ "$skip_install" == 0 ]]; then
  say "installing the mod"
  ./scripts/install-mod.sh >> "$log" 2>&1
fi

# The night's plan, written whole. keep_recent_runs is the soak profile's retention
# policy and it keeps every run of the night: the file cannot say "keep all", so it
# says a number no night reaches.
seeds_json="[]"
if [[ -n "$seeds" ]]; then
  seeds_json="[$(printf '%s' "$seeds" | awk -F, '{ for (i = 1; i <= NF; i++) { gsub(/^[ \t]+|[ \t]+$/, "", $i); printf "%s\"%s\"", (i > 1 ? ", " : ""), $i } }')]"
fi
mkdir -p "$store"
settings_tmp="$settings.$$.tmp"
cat > "$settings_tmp" <<EOF
{
  "schema": "sts2-pilot-trainer/runmobile-settings/v1",
  "record_my_runs": true,
  "fetch_run_index": false,
  "sharing_service_url": null,
  "keep_recent_runs": 100000,
  "purge_my_runs": false,
  "show_main_menu_row": null,
  "retail_soak": {
    "runs": $runs,
    "seeds": $seeds_json,
    "character": "$character",
    "ascension": $ascension,
    "stop_after_minutes": $stop_after_minutes
  }
}
EOF
mv "$settings_tmp" "$settings"
say "wrote $settings"

# A soak-done or a recording from an earlier night is not tonight's: the wait is for
# a soak-done written after the launch and the copy is of the recordings written
# after it, and nothing here removes the old ones
launch_marker="$out/launched-at"
: > "$launch_marker"

launch_args=(launch --owner "retail-soak $(date -u +%Y-%m-%d)" --client-id "$client_id" --headless)
if [[ -n "$game" ]]; then launch_args+=(--game "$game"); fi
say "launching: ./scripts/retail-client.sh ${launch_args[*]}"
if ! ./scripts/retail-client.sh "${launch_args[@]}" 2>&1 | tee -a "$log"; then
  say "the launch was refused; see $log"
  exit 3
fi

# The night: soak-done, or the deadline plus the run bound the mod gives a run in
# flight at the deadline (25 minutes), plus a margin for the game's own startup
budget_seconds=$(( stop_after_minutes * 60 + 25 * 60 + 5 * 60 ))
waited=0
soak_done=""
while [[ "$waited" -lt "$budget_seconds" ]]; do
  if [[ -f "$done_file" && "$done_file" -nt "$launch_marker" ]]; then soak_done="$done_file"; break; fi
  if ./scripts/retail-client.sh status > /dev/null 2>&1; then
    # No client exists any more; a soak-done written in its last second is still
    # picked up by the read below
    sleep 2
    if [[ -f "$done_file" && "$done_file" -nt "$launch_marker" ]]; then soak_done="$done_file"; fi
    break
  fi
  sleep 10
  waited=$((waited + 10))
done

if [[ -n "$soak_done" ]]; then
  say "soak-done arrived after ${waited}s"
else
  say "no soak-done after ${waited}s; releasing the client"
fi

# The client's own quit runs ahead of this; a client still tearing down is waited
# for, never force-killed, and a release that runs out of patience is run again
./scripts/retail-client.sh release --wait 2400 >> "$log" 2>&1 || {
  say "the client was still tearing down after 40 minutes; releasing again"
  ./scripts/retail-client.sh release --wait 2400 >> "$log" 2>&1 || true
}

if [[ -z "$soak_done" ]]; then
  say "the night ended without soak-done; nothing is measured. Read $user_dir/logs/godot.log."
  exit 3
fi
cp "$soak_done" "$out/soak-done.json"
say "soak-done: $(tr -d '\n' < "$soak_done" | tr -s ' ')"

# A night the mod refused before starting a run is written as zero runs with the
# sentence, and measured nothing
refusal="$(sed -n 's/.*"refusal"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$soak_done" | head -n 1)"
if [[ -n "$refusal" ]]; then
  say "the mod refused the night: $refusal; nothing is measured. Read $user_dir/logs/godot.log."
  exit 3
fi

# The night's copy: the recordings written since the launch, copied and never moved
copy="$out/recordings"
mkdir -p "$copy"
if [[ -d "$recordings" ]]; then
  find "$recordings" -mindepth 1 -maxdepth 1 -type f \( -name '*.replay.json' -o -name '*.journal.jsonl' \) -newer "$launch_marker" -exec cp -p {} "$copy/" \;
fi
if ! ls "$copy"/*.replay.json > /dev/null 2>&1; then
  say "the night recorded nothing under $recordings; nothing is measured"
  exit 3
fi
say "copied $(ls "$copy"/*.replay.json | wc -l | tr -d ' ') recording(s) to $copy"

# A run the mod could not finish cleanly is a finding, named here from soak-done:
# one object per run, with its outcome
findings=0
while IFS= read -r outcome; do
  case "$outcome" in
    ended|deadline) ;;
    *) findings=$((findings + 1)); say "a run ended '$outcome'; read godot.log" ;;
  esac
done < <(sed -n 's/.*"outcome"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$soak_done")

# The two numbers, over the night's copy beside the committed corpus. The engine's
# own startup lines are kept in the log and out of the figure, and the coverage
# report's five hundred rows stay in coverage.txt: what is printed is its totals,
# its verdict and the excusals the night reached, which is what the release note
# carries.
parity_figure() {
  grep -v '^\[INFO\]\|^\[ERROR\]\|^\[WARN\]\|^SentryGodotInitializer\|^   at \|^$' "$1" || true
}
coverage_figure() {
  grep '^points:\|^recordings credited\|COVERED\|excused and reached\|misnamed producer\|stale\|inadmissible\|unreadable\|outside the denominator\|^coverage artifact' "$1" || true
}
verdict=0
say "parity:"
set +e
./scripts/arbiter parity --corpus "$copy" --corpus manifests --out "$out" > "$out/parity.txt" 2>&1
parity_status=$?
set -e
cat "$out/parity.txt" >> "$log"
parity_figure "$out/parity.txt"
say "coverage:"
set +e
./scripts/arbiter coverage --corpus "$copy" --corpus manifests --out "$out" > "$out/coverage.txt" 2>&1
coverage_status=$?
set -e
cat "$out/coverage.txt" >> "$log"
coverage_figure "$out/coverage.txt"

echo
say "the third figure: parity $([[ "$parity_status" == 0 ]] && echo holds || echo 'does not hold') over the night's copy beside manifests/; coverage $([[ "$coverage_status" == 0 ]] && echo holds || echo 'does not hold'); $findings run(s) to read; evidence in $out"
if [[ "$parity_status" != 0 || "$coverage_status" != 0 ]]; then verdict=1; fi
if [[ "$verdict" == 0 && "$findings" -gt 0 ]]; then verdict=4; fi
exit "$verdict"

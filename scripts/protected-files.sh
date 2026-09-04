#!/usr/bin/env bash
# Hashes everything this mod must not change, so "nothing outside user://Runmobile/
# was touched" is measured rather than asserted.
#
# The first such measurement was run by hand over 154 files (docs/in-game-host.md).
# This is that measurement, repeatable: take a ledger before a session, take the
# same one after, and compare.
#
#   ./scripts/protected-files.sh snapshot before.ledger
#   ... launch the game, play, quit ...
#   ./scripts/protected-files.sh compare before.ledger
#
# Two roots are covered: the game's user data directory, where saves, profiles, run
# history, settings and mod configs live, and the mods directory, where this mod and
# everybody else's are installed. Everything under user://Runmobile/ is this mod's
# own store; it is hashed like everything else and reported separately, because a
# change there is the mod working and a change anywhere else is the mod failing.
#
# Read-only. It opens files to hash them and writes only the ledger path it is given,
# which must not be inside either root.
set -euo pipefail

usage() {
  cat >&2 <<'EOF'
usage: protected-files.sh snapshot <ledger>   write a ledger of the current state
       protected-files.sh compare  <ledger>   report what changed since that ledger

options (either command):
  --user-dir <path>   the game's user data directory
  --mods-dir <path>   the game's mods directory

Both are discovered when not given. Exit status of compare is 0 when nothing outside
user://Runmobile/ changed, 1 when something did.
EOF
}

command="${1:-}"
case "$command" in
  snapshot|compare) shift ;;
  *) usage; exit 2 ;;
esac

ledger="${1:-}"
if [[ -z "$ledger" || "$ledger" == --* ]]; then usage; exit 2; fi
shift

user_dir="${STS2_USER_DIR:-}"
mods_dir="${STS2_MODS_DIR:-}"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --user-dir) user_dir="$2"; shift 2 ;;
    --mods-dir) mods_dir="$2"; shift 2 ;;
    *) echo "unknown argument: $1" >&2; usage; exit 2 ;;
  esac
done

if [[ -z "$user_dir" ]]; then
  for candidate in \
    "$HOME/Library/Application Support/SlayTheSpire2" \
    "$HOME/.local/share/SlayTheSpire2" \
    "${APPDATA:-}/SlayTheSpire2"
  do
    if [[ -d "$candidate" ]]; then user_dir="$candidate"; break; fi
  done
fi

if [[ -z "$mods_dir" ]]; then
  for candidate in \
    "$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods" \
    "$HOME/.steam/steam/steamapps/common/Slay the Spire 2/mods" \
    "$HOME/.local/share/Steam/steamapps/common/Slay the Spire 2/mods" \
    "/c/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/mods"
  do
    if [[ -d "$candidate" ]]; then mods_dir="$candidate"; break; fi
  done
fi

for pair in "user:$user_dir" "mods:$mods_dir"; do
  name="${pair%%:*}"
  path="${pair#*:}"
  if [[ -z "$path" || ! -d "$path" ]]; then
    echo "Could not find the game's $name directory. Pass it with --$name-dir <path>." >&2
    exit 2
  fi
done

absolute() { (cd "$1" && pwd -P); }
user_dir="$(absolute "$user_dir")"
mods_dir="$(absolute "$mods_dir")"

# The ledger is evidence about those roots and cannot live inside one of them: a
# snapshot that wrote into what it measures would make its own comparison wrong.
ledger_dir="$(cd "$(dirname "$ledger")" && pwd -P)"
ledger="$ledger_dir/$(basename "$ledger")"
case "$ledger_dir/" in
  "$user_dir"/*|"$mods_dir"/*)
    echo "Refusing to write the ledger inside a directory it measures: $ledger" >&2
    exit 2
    ;;
esac

hash_file() {
  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$1" | cut -d' ' -f1
  else
    sha256sum "$1" | cut -d' ' -f1
  fi
}

# One line per file: "<sha256> <namespace>/<path relative to that root>". Sorted by
# the path so two ledgers can be compared line by line. Symbolic links are recorded
# as links rather than followed - a link that changed target is a change, and
# following one would hash a file outside the roots.
walk() {
  local namespace="$1" root="$2" path relative digest target listing
  listing="$(mktemp)"
  if ! find "$root" \( -type f -o -type l \) -print | LC_ALL=C sort > "$listing"; then
    rm -f "$listing"
    echo "Could not enumerate every file under $root; no ledger was accepted." >&2
    return 1
  fi

  while IFS= read -r path; do
    relative="${path#"$root"/}"
    if [[ -L "$path" ]]; then
      if ! target="$(readlink "$path")"; then
        rm -f "$listing"
        echo "Could not read symbolic link $path; no ledger was accepted." >&2
        return 1
      fi
      printf 'symlink:%s\t%s/%s\n' "$target" "$namespace" "$relative"
    else
      if ! digest="$(hash_file "$path")"; then
        rm -f "$listing"
        echo "Could not hash $path; no ledger was accepted." >&2
        return 1
      fi
      printf '%s\t%s/%s\n' "$digest" "$namespace" "$relative"
    fi
  done < "$listing"
  rm -f "$listing"
}

take_snapshot() {
  printf '# runmobile protected-files ledger v1\n'
  printf '# taken\t%s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  printf '# user\t%s\n' "$user_dir"
  printf '# mods\t%s\n' "$mods_dir"
  walk user "$user_dir" || return 1
  walk mods "$mods_dir" || return 1
}

# The mod's own store, and the only subtree this mod is allowed to change.
store_prefix="user/Runmobile/"

# What the game writes on any launch, mod or no mod: its log, its shader caches and
# its crash-reporter state. Listed by name and reported by name - never hidden -
# because a tool that went red on every session is a tool people stop reading. That
# these are the game's own is established by a control launch with no trainer run,
# which is how the first 154-file measurement established it too (docs/in-game-host.md);
# re-check it rather than trusting this list.
game_churn_directories=(
  "user/logs/"
  "user/shader_cache/"
  "user/vulkan/"
)
game_churn_files=(
  "user/sentry.dat"
)

if [[ "$command" == snapshot ]]; then
  snapshot_tmp="$ledger.$$.tmp"
  if ! take_snapshot > "$snapshot_tmp"; then
    rm -f "$snapshot_tmp"
    exit 2
  fi
  mv "$snapshot_tmp" "$ledger"
  files="$(grep -vc '^#' "$ledger" || true)"
  echo "ledger       : $ledger"
  echo "files        : $files"
  echo "user         : $user_dir"
  echo "mods         : $mods_dir"
  exit 0
fi

if [[ ! -f "$ledger" ]]; then
  echo "No ledger at $ledger. Take one with: protected-files.sh snapshot $ledger" >&2
  exit 2
fi

recorded_user="$(awk -F'\t' '$1 == "# user" { print $2 }' "$ledger")"
recorded_mods="$(awk -F'\t' '$1 == "# mods" { print $2 }' "$ledger")"
if [[ "$recorded_user" != "$user_dir" || "$recorded_mods" != "$mods_dir" ]]; then
  echo "Refusing to compare a ledger for different roots." >&2
  echo "ledger user : ${recorded_user:-missing}" >&2
  echo "current user: $user_dir" >&2
  echo "ledger mods : ${recorded_mods:-missing}" >&2
  echo "current mods: $mods_dir" >&2
  exit 2
fi

before="$(mktemp)"
after="$(mktemp)"
after_ledger="$(mktemp)"
trap 'rm -f "$before" "$after" "$after_ledger"' EXIT
grep -v '^#' "$ledger" > "$before" || true
if ! take_snapshot > "$after_ledger"; then exit 2; fi
grep -v '^#' "$after_ledger" > "$after" || true

# added, removed and changed, computed from the two ledgers rather than from a
# recursive diff, so a file whose content changed and a file that appeared are
# different findings rather than the same one.
report="$(mktemp)"
trap 'rm -f "$before" "$after" "$after_ledger" "$report"' EXIT
LC_ALL=C join -t"$(printf '\t')" -j 2 -v 1 -o 0 \
  <(LC_ALL=C sort -t"$(printf '\t')" -k2,2 "$before") \
  <(LC_ALL=C sort -t"$(printf '\t')" -k2,2 "$after") \
  | sed 's/^/removed\t/' >> "$report"
LC_ALL=C join -t"$(printf '\t')" -j 2 -v 2 -o 0 \
  <(LC_ALL=C sort -t"$(printf '\t')" -k2,2 "$before") \
  <(LC_ALL=C sort -t"$(printf '\t')" -k2,2 "$after") \
  | sed 's/^/added\t/' >> "$report"
LC_ALL=C join -t"$(printf '\t')" -j 2 -o 0,1.1,2.1 \
  <(LC_ALL=C sort -t"$(printf '\t')" -k2,2 "$before") \
  <(LC_ALL=C sort -t"$(printf '\t')" -k2,2 "$after") \
  | awk -F'\t' '$2 != $3 { print "changed\t" $1 }' >> "$report"

store_report="$(mktemp)"
churn_report="$(mktemp)"
protected_report="$(mktemp)"
trap 'rm -f "$before" "$after" "$after_ledger" "$report" "$store_report" "$churn_report" "$protected_report"' EXIT

while IFS=$'\t' read -r verdict path; do
  if [[ "$path" == "$store_prefix"* ]]; then
    printf '%s\t%s\n' "$verdict" "$path" >> "$store_report"
    continue
  fi

  is_churn=0
  for churn_file in "${game_churn_files[@]}"; do
    if [[ "$path" == "$churn_file" ]]; then is_churn=1; break; fi
  done
  if [[ "$is_churn" == 0 ]]; then
    for churn_directory in "${game_churn_directories[@]}"; do
      if [[ "$path" == "$churn_directory"* ]]; then is_churn=1; break; fi
    done
  fi

  if [[ "$is_churn" == 1 ]]; then
    printf '%s\t%s\n' "$verdict" "$path" >> "$churn_report"
  else
    printf '%s\t%s\n' "$verdict" "$path" >> "$protected_report"
  fi
done < <(LC_ALL=C sort "$report")

store="$(cat "$store_report")"
churn="$(cat "$churn_report")"
protected="$(cat "$protected_report")"

print_section() {
  local title="$1" body="$2"
  echo "$title"
  if [[ -z "$body" ]]; then
    echo "  nothing"
  else
    while IFS=$'\t' read -r verdict path; do
      printf '  %-8s %s\n' "$verdict" "$path"
    done <<< "$body"
  fi
}

echo "ledger       : $ledger"
echo "user         : $user_dir"
echo "mods         : $mods_dir"
echo
print_section "protected files (must not change):" "$protected"
echo
print_section "user://Runmobile/ (this mod's own store):" "$store"
echo
print_section "the game's own churn (written on any launch, mod or not):" "$churn"

if [[ -n "$protected" ]]; then exit 1; fi
exit 0

#!/usr/bin/env bash
# Installs a packaged Runmobile build and prepares the player's game assemblies locally.
set -euo pipefail

package="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
mod_id="Runmobile"
former_mod_id="CombatTrainer"
mods_dir="${STS2_MODS_DIR:-}"
game_dir=""
uninstall=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --mods-dir) mods_dir="$2"; shift 2 ;;
    --game-dir) game_dir="$2"; shift 2 ;;
    --uninstall) uninstall=1; shift ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

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
if [[ -z "$mods_dir" ]]; then
  echo "Could not find the game's mods directory. Pass --mods-dir <path>." >&2
  exit 2
fi
case "$(basename "$mods_dir")" in
  mods|mods_STEAMTEST) ;;
  *) echo "Refusing to install into '$mods_dir': it is not a mods directory." >&2; exit 3 ;;
esac

target="$mods_dir/$mod_id"
if [[ "$uninstall" == 1 ]]; then
  removed=0
  for directory in "$target" "$mods_dir/$former_mod_id"; do
    if [[ -d "$directory" ]]; then rm -rf "$directory"; removed=1; fi
  done
  if [[ "$removed" == 0 ]]; then echo "nothing to remove at $target"; fi
  exit 0
fi

if [[ ! -f "$package/runtime-id" ]]; then
  echo "Package is missing its runtime identity; refusing a partial install." >&2
  exit 4
fi
rid="$(<"$package/runtime-id")"
bootstrap="Sts2PilotTrainer.Bootstrap"
arbiter="sts2-arbiter"
if [[ "$rid" == win-* ]]; then
  bootstrap="$bootstrap.exe"
  arbiter="$arbiter.exe"
fi
payload_files=(
  "Runmobile.json"
  "Runmobile.dll"
  "Sts2PilotTrainer.Trainer.dll"
  "Sts2PilotTrainer.Engine.dll"
  "Sts2PilotTrainer.Replay.dll"
  "Sts2PilotTrainer.IO.dll"
)
for file in "${payload_files[@]}"; do
  if [[ ! -f "$package/payload/$file" ]]; then
    echo "Package payload is missing $file; refusing a partial install." >&2
    exit 4
  fi
done
if [[ ! -f "$package/payload/arbiter/$arbiter" ]]; then
  echo "Package payload is missing $arbiter; refusing a partial install." >&2
  exit 4
fi
if [[ ! -f "$package/bootstrap/$bootstrap" ]]; then
  echo "Package is missing its local preparation tool; refusing a partial install." >&2
  exit 4
fi

staging="$(mktemp -d "$mods_dir/.${mod_id}.install.XXXXXX")"
runtime="$(mktemp -d "${TMPDIR:-/tmp}/Runmobile.runtime.XXXXXX")"
backup=""
former_backup=""
replacement_started=0
committed=0
cleanup() {
  if [[ -n "$staging" ]]; then rm -rf "$staging"; fi
  if [[ -n "$runtime" ]]; then rm -rf "$runtime"; fi
  if [[ "$committed" == 0 ]]; then
    if [[ "$replacement_started" == 1 ]]; then rm -rf "$target"; fi
    if [[ -n "$backup" && ( -e "$backup" || -L "$backup" ) ]]; then mv "$backup" "$target"; fi
    if [[ -n "$former_backup" && ( -e "$former_backup" || -L "$former_backup" ) ]]; then
      rm -rf "$mods_dir/$former_mod_id"
      mv "$former_backup" "$mods_dir/$former_mod_id"
    fi
  else
    if [[ -n "$backup" ]]; then rm -rf "$backup"; fi
    if [[ -n "$former_backup" ]]; then rm -rf "$former_backup"; fi
  fi
}
trap cleanup EXIT

cp -R "$package/payload/." "$staging/"
bootstrap_args=(--out "$runtime/lib")
if [[ -n "$game_dir" ]]; then bootstrap_args+=(--game-dir "$game_dir"); fi
STS2_PILOT_TRAINER_WORKSPACE="$runtime" \
  "$package/bootstrap/$bootstrap" "${bootstrap_args[@]}"
cp -R "$runtime/lib" "$staging/arbiter/lib"
rm -rf "$runtime"
runtime=""

if [[ -e "$target" || -L "$target" ]]; then
  backup="$(mktemp -d "$mods_dir/.${mod_id}.previous.XXXXXX")"
  rmdir "$backup"
  mv "$target" "$backup"
fi
if [[ -e "$mods_dir/$former_mod_id" || -L "$mods_dir/$former_mod_id" ]]; then
  former_backup="$(mktemp -d "$mods_dir/.${former_mod_id}.previous.XXXXXX")"
  rmdir "$former_backup"
  mv "$mods_dir/$former_mod_id" "$former_backup"
fi
replacement_started=1
mv "$staging" "$target"
staging=""
committed=1
if [[ -n "$backup" ]]; then rm -rf "$backup"; backup=""; fi
if [[ -n "$former_backup" ]]; then rm -rf "$former_backup"; former_backup=""; fi
trap - EXIT

echo "installed    : Runmobile and local arbiter -> ${target/#$HOME/\~}"

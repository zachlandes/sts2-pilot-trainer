#!/usr/bin/env bash
# Builds the Combat Trainer mod and puts it where the game looks for mods.
#
# This is the one script in this repository that writes inside a Slay the Spire 2
# installation. Its final state is exactly CombatTrainer under the selected supported
# mod directory (mods or mods_STEAMTEST); upgrades use temporary siblings there so the
# complete named file set replaces the old one. That is the game's own mod surface -
# the same directory Steam Workshop installs into
# - and there is no other: the game derives it from its executable's location and
# offers no user-data alternative.
#
# Nothing outside the selected mod directory is touched, and nothing here reads or
# writes a save, a profile or a run. --uninstall removes the final directory and
# nothing more.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

mod_id="CombatTrainer"
uninstall=0
mods_dir="${STS2_MODS_DIR:-}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --mods-dir) mods_dir="$2"; shift 2 ;;
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
  cat >&2 <<'EOF'
Could not find the game's mods directory.

Pass one explicitly:
  ./scripts/install-mod.sh --mods-dir <path>

The path wanted is the directory the game loads mods from, e.g. on macOS
  .../Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods
EOF
  exit 2
fi

# Refuse to write anywhere that is not a mods directory. Everything under this
# script's hand is inside a directory somebody paid for.
case "$(basename "$mods_dir")" in
  mods|mods_STEAMTEST) ;;
  *)
    echo "Refusing to install into '$mods_dir': it is not a mods directory." >&2
    exit 3
    ;;
esac

target="$mods_dir/$mod_id"

if [[ "$uninstall" == 1 ]]; then
  if [[ -d "$target" ]]; then
    rm -rf "$target"
    echo "removed      : $target"
  else
    echo "nothing to remove at $target"
  fi
  exit 0
fi

dotnet build src/Sts2PilotTrainer.Mod/Sts2PilotTrainer.Mod.csproj -c Release --nologo -v quiet

built="build/bin/Sts2PilotTrainer.Mod/Release/net9.0"

# Named rather than globbed: what a mod ships is a decision, and a stray file that
# appeared in an output directory is not a reason to put it in someone's game.
files=(
  "$mod_id.json"
  "$mod_id.dll"
  "Sts2PilotTrainer.Trainer.dll"
  "Sts2PilotTrainer.Engine.dll"
  "Sts2PilotTrainer.Replay.dll"
  "Sts2PilotTrainer.IO.dll"
)

for file in "${files[@]}"; do
  if [[ ! -f "$built/$file" ]]; then
    echo "Build output is missing $file; refusing to install a partial mod." >&2
    exit 4
  fi
done

staging="$(mktemp -d "$mods_dir/.${mod_id}.install.XXXXXX")"
backup=""
cleanup() {
  if [[ -n "$staging" ]]; then
    rm -rf "$staging"
  fi
  if [[ -n "$backup" && ( -e "$backup" || -L "$backup" ) ]]; then
    if [[ ! -e "$target" && ! -L "$target" ]]; then
      mv "$backup" "$target"
    else
      rm -rf "$backup"
    fi
  fi
}
trap cleanup EXIT

for file in "${files[@]}"; do
  cp "$built/$file" "$staging/$file"
done

if [[ -e "$target" || -L "$target" ]]; then
  backup="$(mktemp -d "$mods_dir/.${mod_id}.previous.XXXXXX")"
  rmdir "$backup"
  mv "$target" "$backup"
fi
mv "$staging" "$target"
staging=""
if [[ -n "$backup" ]]; then
  rm -rf "$backup"
  backup=""
fi
trap - EXIT

echo "installed    : ${#files[@]} files -> ${target/#$HOME/\~}"
echo "next         : launch Slay the Spire 2, allow mod loading, then Singleplayer"

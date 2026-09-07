#!/usr/bin/env bash
# Builds the distributable Runmobile package, then installs it through its own installer.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

mods_dir="${STS2_MODS_DIR:-}"
uninstall=0
while [[ $# -gt 0 ]]; do
  case "$1" in
    --mods-dir) mods_dir="$2"; shift 2 ;;
    --uninstall) uninstall=1; shift ;;
    *) echo "unknown argument: $1" >&2; exit 2 ;;
  esac
done

if [[ "$uninstall" == 1 ]]; then
  args=(--uninstall)
  if [[ -n "$mods_dir" ]]; then args+=(--mods-dir "$mods_dir"); fi
  exec scripts/install-package.sh "${args[@]}"
fi

rid="$(dotnet --info | awk '$1 == "RID:" {print $2; exit}')"
if [[ -z "$rid" ]]; then
  echo "Could not determine the current .NET runtime identifier." >&2
  exit 4
fi
package="build/distribution/Runmobile-$rid"
./scripts/package-mod.sh --directory "$package"
args=()
if [[ -n "$mods_dir" ]]; then args+=(--mods-dir "$mods_dir"); fi
exec "$package/install.sh" "${args[@]}"

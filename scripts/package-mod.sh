#!/usr/bin/env bash
# Builds the platform-specific Runmobile package without copying game content into it.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

if [[ $# -gt 0 ]]; then
  echo "package-mod.sh takes no arguments" >&2
  exit 2
fi

rid="$(dotnet --info | awk '$1 == "RID:" {print $2; exit}')"
if [[ -z "$rid" ]]; then
  echo "Could not determine the current .NET runtime identifier." >&2
  exit 4
fi
out_dir="build/distribution/Runmobile-$rid"
archive="$out_dir.tar.gz"
work="build/publish/runmobile-package/$rid"
rm -rf "$work" "$out_dir"
rm -f "$archive"
cleanup() {
  status=$?
  trap - EXIT
  if [[ "$status" -ne 0 ]]; then
    rm -rf "$work" "$out_dir" || true
    rm -f "$archive" || true
  fi
  exit "$status"
}
trap cleanup EXIT

./scripts/build.sh

mkdir -p "$work/arbiter" "$work/bootstrap" "$out_dir/payload"

dotnet publish src/Sts2PilotTrainer.Cli/Sts2PilotTrainer.Cli.csproj \
  -c Release -r "$rid" --self-contained true --nologo -v quiet -o "$work/arbiter"
dotnet publish tools/Sts2PilotTrainer.Bootstrap/Sts2PilotTrainer.Bootstrap.csproj \
  -c Release -r "$rid" --self-contained true --nologo -v quiet -o "$work/bootstrap"

built="build/bin/Sts2PilotTrainer.Mod/Release/net9.0"
files=(
  "Runmobile.json"
  "Runmobile.dll"
  "Sts2PilotTrainer.Trainer.dll"
  "Sts2PilotTrainer.Engine.dll"
  "Sts2PilotTrainer.Replay.dll"
  "Sts2PilotTrainer.IO.dll"
)
for file in "${files[@]}"; do
  if [[ ! -f "$built/$file" ]]; then
    echo "Build output is missing $file; refusing to package a partial mod." >&2
    exit 4
  fi
  cp "$built/$file" "$out_dir/payload/$file"
done

cp -R "$work/arbiter" "$out_dir/payload/arbiter"
(
  cd "$out_dir/payload/arbiter"
  find . -type f -print | LC_ALL=C sort
) > "$out_dir/arbiter-files.txt"
cp -R "$work/bootstrap" "$out_dir/bootstrap"
cp scripts/install-package.sh "$out_dir/install.sh"
chmod +x "$out_dir/install.sh"
printf '%s\n' "$rid" > "$out_dir/runtime-id"
tar -czf "$archive" -C "$out_dir" .
trap - EXIT

echo "packaged     : $out_dir"
echo "archive      : $archive"

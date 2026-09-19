#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
corpus=0
for arg in "$@"; do case "$arg" in --corpus) corpus=1;; --dry-run) ;; *) echo "unknown option: $arg" >&2; exit 2;; esac; done
if (( corpus )); then
  echo "corpus adoption requires a decision on cross-build corpus handling; refusing to guess" >&2
  exit 3
fi
compare() {
  local label="$1" left="$2" right="$3"
  if [[ -e "$left" && -e "$right" ]] && ! diff -u "$left" "$right"; then
    echo "first non-empty diff: $label" >&2
    exit 1
  fi
}
compare game-build scripts/game-build.txt build/lib/game-build.txt
steps=(
  "./scripts/bootstrap.sh --archive build/archive"
  "./scripts/arbiter engine-commands --update"
  "./scripts/arbiter coverage --corpus manifests --update"
  "./scripts/choice-entry-points.sh --update"
  "./scripts/save-points.sh --update"
  "./scripts/act-topology.sh --update"
  "./scripts/assert-expected-skips.sh --update"
  "./scripts/format-reference.sh"
  "seed verification"
  "./scripts/build.sh && ./scripts/fetch-baselib-parity.sh && ./scripts/test-session.sh"
)
printf 'adoption dry run: no diff\n'
printf 'next: %s\n' "${steps[0]}"

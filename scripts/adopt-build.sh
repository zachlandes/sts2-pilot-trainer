#!/usr/bin/env bash
# Adopts a game build deliberately: runs the ordered checks docs/build-adoption.md
# lists, each of which regenerates one committed record from the prepared game or
# proves a set of pinned facts still holds on it, and stops at the first record whose
# regenerated copy differs from the committed one, or the first proof that fails, so
# each change is reviewed before the next record is rewritten over it.
#
# Nothing under manifests/ is edited: the records are the build's, the manifests are
# evidence, and a step that touched one is refused. Corpus evidence is adopted only
# once the cross-build corpus decision is made, so --corpus refuses rather than guesses.
#
#   ./scripts/adopt-build.sh            run the ordered checks, stop at the first non-empty diff
#   ./scripts/adopt-build.sh --corpus   refused until the cross-build corpus decision is resolved
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

case "${1-}" in
  --corpus)
    echo "adopt-build: corpus adoption requires a decision on cross-build corpus handling; refusing to guess" >&2
    exit 3 ;;
  "") ;;
  *) echo "usage: adopt-build.sh [--corpus]" >&2; exit 2 ;;
esac

refuse_manifest_edits() {
  local changed
  changed="$(git status --porcelain -- manifests)"
  if [ -n "$changed" ]; then
    echo "adopt-build: step '$1' changed manifests/, which this script never does:" >&2
    echo "$changed" >&2
    exit 1
  fi
}

# check <label> <committed record>... -- <command>...
# Runs the command, then compares each record with its committed copy; the first
# record that differs stops the run with its diff on the console.
check() {
  local label="$1"; shift
  local -a records=()
  while [ "$1" != "--" ]; do records+=("$1"); shift; done
  shift
  echo "adopt-build: $label" >&2
  "$@"
  refuse_manifest_edits "$label"
  local record
  for record in ${records[@]+"${records[@]}"}; do
    if ! git diff --quiet HEAD -- "$record"; then
      git --no-pager diff HEAD -- "$record"
      echo "adopt-build: first non-empty diff: $label ($record); review it before running again" >&2
      exit 1
    fi
  done
}

# The prepared set and the CLI every later step drives; the archive keeps the
# receipted set under its build, and an archive made before the build record
# existed refuses here and is moved aside by hand (docs/build-adoption.md).
check "prepare and archive the build" -- ./scripts/build.sh --archive build/archive
if ! diff -u scripts/game-build.txt build/lib/game-build.txt; then
  echo "adopt-build: first non-empty diff: game-build (scripts/game-build.txt against the prepared build/lib/game-build.txt)" >&2
  exit 1
fi

check "decision ledger" scripts/decision-ledger.txt -- ./scripts/arbiter engine-commands --update
check "decision coverage" scripts/decision-coverage.txt scripts/producer-map.txt -- ./scripts/arbiter coverage --corpus manifests --update
check "choice entry points" scripts/choice-entry-points.txt scripts/unreadable-choice-scan-bodies.txt -- ./scripts/choice-entry-points.sh --update
check "save points" scripts/save-points.txt -- ./scripts/save-points.sh --update
check "act topology" scripts/act-topology.txt -- ./scripts/act-topology.sh --update
check "expected hosted skips" scripts/expected-hosted-skips.txt -- ./scripts/assert-expected-skips.sh --update
check "format reference" docs/manifest-format.md -- ./scripts/format-reference.sh

# Seed verification: every pinned SeedHunt row is a GeneratedCoverageTests row that
# hunts its seed, plays the walk it pins and replays it to parity, so the rows are
# the proof and a failed one is this step's non-empty diff.
check "seed verification" -- dotnet test tests/Sts2PilotTrainer.Mod.Tests -c Release --nologo --verbosity quiet \
  --filter "FullyQualifiedName~Sts2PilotTrainer.Arbiter.Tests.GeneratedCoverageTests"

check "the suite" -- ./scripts/fetch-baselib-parity.sh
check "the suite" -- ./scripts/test-session.sh

echo "adopt-build: every record is what this build produces and every proof holds; corpus evidence is still --corpus, which is refused until the cross-build decision is made"

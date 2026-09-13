#!/usr/bin/env bash
# Holds scripts/choice-entry-points.txt to the game build this tree has prepared.
#
# The file lists every game type whose own code calls a player-choice entry point -
# a public member of CardSelectCmd or RelicSelectCmd that hands back a chosen card,
# cards or relic - read from the game assembly's IL. It exists so a card or relic
# that gains a prompt on a game update shows as a diff in the change that adopted
# the build, beside the entry-point check that would fail on a prompt nothing
# watches. The reading is RunRecorderTests.TheTypesThatReachEachChoiceEntryPointAreTheRecordedOnes
# in tests/Sts2PilotTrainer.Mod.Tests, which needs the prepared game and so cannot
# run on a hosted runner; the local merge gate runs it.
#
#   ./scripts/choice-entry-points.sh            check
#   ./scripts/choice-entry-points.sh --update   rewrite the list from this build
set -euo pipefail

readonly REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly TEST="RunRecorderTests.TheTypesThatReachEachChoiceEntryPointAreTheRecordedOnes"

update=0
case "${1-}" in
  --update) update=1 ;;
  "") ;;
  *) echo "usage: choice-entry-points.sh [--update]" >&2; exit 2 ;;
esac

if [ ! -f "$REPO_ROOT/build/lib/sts2.dll" ]; then
  echo "choice-entry-points: build/lib/sts2.dll is not prepared; run ./scripts/build.sh first." >&2
  exit 1
fi

if [ "$update" -eq 1 ]; then
  export CHOICE_ENTRY_POINTS_UPDATE=1
fi

(cd "$REPO_ROOT" && dotnet test tests/Sts2PilotTrainer.Mod.Tests --nologo --verbosity quiet \
  --filter "FullyQualifiedName~$TEST")

if [ "$update" -eq 1 ]; then
  echo "Rewrote scripts/choice-entry-points.txt from this build."
fi

#!/usr/bin/env bash
# Holds two committed records to the game build this tree has prepared.
#
# scripts/choice-entry-points.txt lists every game type whose own code calls a
# player-choice entry point - a public member of CardSelectCmd or RelicSelectCmd that
# hands back a chosen card, cards or relic - read from the game assembly's IL. It
# exists so a card or relic that gains a prompt on a game update shows as a diff in
# the change that adopted the build, beside the entry-point check that would fail on
# a prompt nothing watches.
#
# scripts/unreadable-choice-scan-bodies.txt lists every game type with a method body
# that scan could not read, because its signature or a call site names a Godot member
# the vendored stubs have not got, and every type the runtime could not load against
# them at all. Each is code the funnel check does not see, so the set is held rather
# than tolerated and a stub gap fails as a diff.
#
# Both readings are [GameFact]s in RunRecorderTests, in tests/Sts2PilotTrainer.Mod.Tests,
# which need the prepared game and the built CLI and so cannot run on a hosted runner;
# the local merge gate runs them. This refuses on the same two files that attribute
# reads, and reads the run's own results, so a test that skipped or did not run is
# never reported as a check or a rewrite.
#
#   ./scripts/choice-entry-points.sh            check
#   ./scripts/choice-entry-points.sh --update   rewrite both records from this build
set -euo pipefail

readonly REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly CLI="$REPO_ROOT/build/bin/Sts2PilotTrainer.Cli/Release/net9.0/sts2-arbiter.dll"
readonly TESTS=(
  "RunRecorderTests.TheTypesThatReachEachChoiceEntryPointAreTheRecordedOnes"
  "RunRecorderTests.TheMethodBodiesTheChoiceScanCannotReadAreTheRecordedOnes"
)

# The assertion message of one failed test in a trx, unescaped; the records' diff
# lives there and nowhere on the console under quiet verbosity.
failure_message() {
  python3 - "$1" "$2" <<'PY'
import sys, xml.etree.ElementTree as ET
trx, test = sys.argv[1], sys.argv[2]
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
for result in ET.parse(trx).getroot().iter("{%s}UnitTestResult" % ns["t"]):
    if result.get("testName") == f"Sts2PilotTrainer.Arbiter.Tests.{test}":
        message = result.find("t:Output/t:ErrorInfo/t:Message", ns)
        if message is not None and message.text:
            print(message.text)
PY
}

update=0
case "${1-}" in
  --update) update=1 ;;
  "") ;;
  *) echo "usage: choice-entry-points.sh [--update]" >&2; exit 2 ;;
esac

if [ ! -f "$REPO_ROOT/build/lib/sts2.dll" ] || [ ! -f "$CLI" ]; then
  echo "choice-entry-points: needs a prepared game assembly and a built CLI; run ./scripts/build.sh first." >&2
  exit 1
fi

if [ "$update" -eq 1 ]; then
  export CHOICE_ENTRY_POINTS_UPDATE=1
fi

filter=""
for test in "${TESTS[@]}"; do
  filter="${filter:+$filter|}FullyQualifiedName~$test"
done

results="$(mktemp -d)"
trap 'rm -rf "$results"' EXIT

# A failed test is read below rather than aborting here: quiet verbosity prints no
# assertion message, and the message is the diff the check exists to show.
(cd "$REPO_ROOT" && dotnet test tests/Sts2PilotTrainer.Mod.Tests -c Release --nologo --verbosity quiet \
  --filter "$filter" --logger "trx;LogFileName=choice-entry-points.trx" --results-directory "$results") || true

# The verdict is read from the results and not from the exit code: a test that
# skipped, or a filter that matched nothing, both exit zero.
trx="$results/choice-entry-points.trx"
for test in "${TESTS[@]}"; do
  if ! grep -oE '<UnitTestResult [^>]*' "$trx" 2>/dev/null \
       | grep -F "testName=\"Sts2PilotTrainer.Arbiter.Tests.$test\"" | grep -qF 'outcome="Passed"'; then
    echo "choice-entry-points: $test did not run to a pass, so nothing was checked or rewritten." >&2
    failure_message "$trx" "$test" >&2
    exit 1
  fi
done

if [ "$update" -eq 1 ]; then
  echo "Rewrote scripts/choice-entry-points.txt and scripts/unreadable-choice-scan-bodies.txt from this build."
fi

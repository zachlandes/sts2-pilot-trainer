#!/usr/bin/env bash
# Holds scripts/act-topology.txt to the game build this tree has prepared.
#
# The file lists every act the model database ships at each index - whether it is
# that index's default, the epochs its IsUnlocked names, its room and floor counts -
# and every type this build subclasses ActModel with and the database leaves out,
# all read from the game assembly's model database, IL and type table. The recorder's and the replay's
# fixtures name the default act at each index as their progression and one
# alternative at index 0, and scripts/save-points.sh cannot see an act change: a new
# or alternative act keeps every SaveManager.SaveRun call site and every ancient
# event subclass where they were. A game update that adds an act, moves one to
# another index, or changes which is the default or what unlocks one, now shows up
# as a diff in the change that adopts the build,
# rather than as fixtures that keep naming a progression the game no longer defaults to.
#
# The check is a [GameFact] in RunRecorderTests, in tests/Sts2PilotTrainer.Mod.Tests,
# which needs the prepared game and the built CLI and so cannot run on a hosted
# runner; the local merge gate runs it. This refuses on the same file that attribute
# reads, and reads the run's own results, so a test that skipped or did not run is
# never reported as a check or a rewrite.
#
#   ./scripts/act-topology.sh            check
#   ./scripts/act-topology.sh --update   rewrite the record from this build
set -euo pipefail

readonly REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly CLI="$REPO_ROOT/build/bin/Sts2PilotTrainer.Cli/Release/net9.0/sts2-arbiter.dll"
readonly TEST="RunRecorderTests.TheGamesActTopologyIsTheRecordedOne"

# The assertion message of the one failed test in a trx, unescaped; the records' diff
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
  *) echo "usage: act-topology.sh [--update]" >&2; exit 2 ;;
esac

if [ ! -f "$REPO_ROOT/build/lib/sts2.dll" ] || [ ! -f "$CLI" ]; then
  echo "act-topology: needs a prepared game assembly and a built CLI; run ./scripts/build.sh first." >&2
  exit 1
fi

if [ "$update" -eq 1 ]; then
  export ACT_TOPOLOGY_UPDATE=1
fi

results="$(mktemp -d)"
trap 'rm -rf "$results"' EXIT

# A failed test is read below rather than aborting here: quiet verbosity prints no
# assertion message, and the message is the diff the check exists to show.
(cd "$REPO_ROOT" && dotnet test tests/Sts2PilotTrainer.Mod.Tests -c Release --nologo --verbosity quiet \
  --filter "FullyQualifiedName~$TEST" --logger "trx;LogFileName=act-topology.trx" --results-directory "$results") || true

# The verdict is read from the results and not from the exit code: a test that
# skipped, or a filter that matched nothing, both exit zero.
trx="$results/act-topology.trx"
if ! grep -oE '<UnitTestResult [^>]*' "$trx" 2>/dev/null \
     | grep -F "testName=\"Sts2PilotTrainer.Arbiter.Tests.$TEST\"" | grep -qF 'outcome="Passed"'; then
  echo "act-topology: $TEST did not run to a pass, so nothing was checked or rewritten." >&2
  failure_message "$trx" "$TEST" >&2
  exit 1
fi

if [ "$update" -eq 1 ]; then
  echo "Rewrote scripts/act-topology.txt from this build."
fi

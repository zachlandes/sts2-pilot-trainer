#!/usr/bin/env bash
# Runs a `dotnet test` session and states one verdict for it.
#
# This exists because `dotnet test` reports a run it did not finish as a pass. When
# the session is aborted - a TestSessionTimeout in .runsettings, or a cancellation -
# it still prints a per-assembly summary line beginning "Passed!", counting only the
# tests that got to run before the abort:
#
#   Aborting test run: test run timeout of 900000 milliseconds exceeded.
#   Passed!  - Failed: 0, Passed: 80, Skipped: 0, Total: 80, ...
#   Test Run Aborted.
#
# Nothing in that summary says the total is partial. Three runs were read as green
# that way before anybody noticed, because the reader's eye lands on the summary and
# not on the exit code. So the verdict here is computed from whether the session
# actually completed, never from the printed count: a non-zero exit code fails, and
# so does an abort or cancellation marker in the output even if the tool exits zero.
# The last line printed is always the verdict, in the same words either way.
#
#   ./scripts/test-session.sh                       the change-aware solution, Release
#   ./scripts/test-session.sh --full                force every generated coverage row
#   ./scripts/test-session.sh <dotnet test args>    ... or any session you name
#
# It wraps `dotnet test` only. Preparing the tree - ./scripts/build.sh and
# ./scripts/fetch-baselib-parity.sh - stays with the caller, because what a session
# needs prepared differs per session and AGENTS.md and .no-mistakes.yaml both say so.
set -uo pipefail

# vstest prints each of these at the start of a line. Anchoring there keeps a test
# whose own failure output quotes one of them from being read as an abort of this
# session - the tests that hold this script quote all of them.
readonly ABORT_MARKERS='^(Test Run Aborted|Test Run Canceled|Test Run Cancelled|Aborting test run|The active test run was aborted)'

force_full=false
if [ "${1:-}" = --full ]; then
  force_full=true
  shift
fi

if [ "$#" -eq 0 ]; then
  # Serialize test assemblies so the collection cap is a suite-wide budget
  set -- sts2-pilot-trainer.sln -c Release --nologo -m:1
  threads="${RUNMOBILE_TEST_THREADS:-12}"
  if ! [[ "$threads" =~ ^[1-9][0-9]*$ ]]; then
    echo "TEST SESSION FAILED - RUNMOBILE_TEST_THREADS must be a positive integer."
    exit 1
  fi

  # Only known independent paths may omit the expensive generated rows
  # Missing history, no changes and unknown paths all keep the complete suite
  coverage=full
  reason='forced or no trustworthy change set'
  if [ "$force_full" = false ] && base="$(git merge-base HEAD refs/remotes/origin/main 2>/dev/null)" && paths="$(mktemp "${TMPDIR:-/tmp}/sts2-test-paths.XXXXXX")"; then
    if git diff --name-only --no-renames -z "$base" -- > "$paths" && git ls-files --others --exclude-standard -z >> "$paths"; then
      coverage=omit-generated
      reason="only independent paths changed against $base"
      changed=false
      while IFS= read -r -d '' path; do
        changed=true
        case "$path" in
          docs/*|demo/*|README.md|AGENTS.md|CLAUDE.md|LICENSE|.editorconfig|src/Sts2PilotTrainer.Trainer/*|tests/Sts2PilotTrainer.Trainer.Tests/*) ;;
          *) coverage=full; reason="relevant or unknown path: $path (base $base)"; break ;;
        esac
      done < "$paths"
      if [ "$changed" = false ]; then
        coverage=full
        reason="no changes against $base; run the complete standard"
      fi
    fi
    rm -f "$paths"
  fi
  echo "Generated coverage: $coverage - $reason"
  echo "Test parallelism: $threads collections, one test assembly at a time"
  if [ "$coverage" = omit-generated ]; then
    set -- "$@" --filter 'FullyQualifiedName!~Sts2PilotTrainer.Arbiter.Tests.GeneratedCoverageTests'
  fi
  set -- "$@" -- "xUnit.MaxParallelThreads=$threads"
else
  echo "Test scope: explicit dotnet test arguments (no automatic coverage exclusion)"
fi

# An explicit template rather than `mktemp -t`, which means a prefix on macOS and a
# template needing X's on GNU coreutils.
if ! transcript="$(mktemp "${TMPDIR:-/tmp}/sts2-test-session.XXXXXX")"; then
  echo
  echo "TEST SESSION FAILED - its transcript could not be created."
  exit 1
fi
trap 'rm -f "$transcript"' EXIT

dotnet test "$@" 2>&1 | tee "$transcript" >/dev/null
pipeline_status=("${PIPESTATUS[@]}")
status="${pipeline_status[0]}"
transcript_status="${pipeline_status[1]}"

if [ "$transcript_status" -ne 0 ]; then
  echo
  echo "TEST SESSION FAILED - its transcript could not be recorded."
  exit 1
fi

grep -qE "$ABORT_MARKERS" "$transcript"
marker_status="$?"

if [ "$marker_status" -eq 0 ]; then
  if ! sed 's/Passed/Incomplete/g' "$transcript"; then
    echo
    echo "TEST SESSION FAILED - its transcript could not be read."
    exit 1
  fi
  echo
  echo "A session that times out under load needs TestSessionTimeout in .runsettings"
  echo "raised, or the hang that consumed the bound found; it never needs interpreting."
  echo "TEST SESSION ABORTED - it did not run to completion, so this is not a pass."
  exit 1
fi

if [ "$marker_status" -ne 1 ]; then
  echo
  echo "TEST SESSION FAILED - its transcript could not be read."
  exit 1
fi

if ! cat "$transcript"; then
  echo
  echo "TEST SESSION FAILED - its transcript could not be read."
  exit 1
fi

if [ "$status" -ne 0 ]; then
  echo
  echo "TEST SESSION FAILED - dotnet test exited $status."
  exit "$status"
fi

echo
echo "TEST SESSION PASSED - the session ran to completion and every test in it passed."

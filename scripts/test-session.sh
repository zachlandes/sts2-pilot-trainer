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
#   ./scripts/test-session.sh                       the whole solution, Release
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

if [ "$#" -eq 0 ]; then
  set -- sts2-pilot-trainer.sln -c Release --nologo
fi

# An explicit template rather than `mktemp -t`, which means a prefix on macOS and a
# template needing X's on GNU coreutils.
if ! transcript="$(mktemp "${TMPDIR:-/tmp}/sts2-test-session.XXXXXX")"; then
  echo
  echo "TEST SESSION FAILED - its transcript could not be created."
  exit 1
fi
trap 'rm -f "$transcript"' EXIT

dotnet test "$@" 2>&1 | tee "$transcript"
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
  echo
  echo "TEST SESSION ABORTED - it did not run to completion, so this is not a pass."
  echo "Any \"Passed!\" summary above counts only the tests that ran before the abort."
  echo "A session that times out under load needs TestSessionTimeout in .runsettings"
  echo "raised, or the hang that consumed the bound found; it never needs interpreting."
  exit 1
fi

if [ "$marker_status" -ne 1 ]; then
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

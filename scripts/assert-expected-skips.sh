#!/usr/bin/env bash
# Asserts that the set of tests CI skips is the set we agreed it would skip.
#
# CI runs sts2-pilot-trainer.domain.slnf on a hosted runner that does not own the
# game. Of Sts2PilotTrainer.Arbiter.Tests' 147 tests, 99 skip there, and the job
# still prints "Test Run Successful" and renders a green tick. The skip itself is
# correct - not owning the game is a good reason to be unable to run a test, and
# tests/Sts2PilotTrainer.Arbiter.Tests/Arbiter.cs says so. What was missing is that
# nothing recorded WHICH tests were skipped, so a test moved behind [GameFact],
# deleted, or swapped for a different one changed CI's real coverage without
# changing its verdict.
#
# This compares the skipped set against scripts/expected-hosted-skips.txt and fails
# when they differ. It catches structural drift only - a test appearing in or
# disappearing from the skip set. A test that is already skipped here and is broken
# inside stays invisible to it, and stays the local gate's job: ./scripts/build.sh
# && dotnet test sts2-pilot-trainer.sln, which runs everything.
#
#   ./scripts/assert-expected-skips.sh            check
#   ./scripts/assert-expected-skips.sh --update   rewrite the expected list
#
# It runs its own `dotnet test` rather than reading the CI test step's results
# because a single solution-wide run cannot write one trx per project: the trx
# logger's file name is a literal (MSBuild properties are not expanded in it) and
# its automatic names are only second-granular, so two projects finishing in the
# same second silently overwrite each other. A gate that flakes gets turned off, so
# each project is run under a file name of its own instead. It costs a second run
# of an eight-second suite.
#
# Whether a test skips is decided in an attribute constructor at discovery time from
# two File.Exists calls - build/lib/sts2.dll and the built CLI - so on a tree without
# a prepared game the set is the same every run and on every platform. Which is also
# why this refuses to measure a tree that HAS the game: nothing would skip there. On
# such a machine it copies the tracked working tree, uncommitted edits included, into
# a game-free temporary directory and measures that.
set -euo pipefail

readonly REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
readonly EXPECTED="$REPO_ROOT/scripts/expected-hosted-skips.txt"
readonly FILTER="sts2-pilot-trainer.domain.slnf"

update=0
case "${1-}" in
  --update) update=1 ;;
  "") ;;
  *) echo "usage: assert-expected-skips.sh [--update]" >&2; exit 2 ;;
esac

scratch="$(mktemp -d)"
trap 'rm -rf "$scratch"' EXIT

# Every test project the solution filter lists, in filter order.
test_projects() {
  local tree="$1" project
  while read -r project; do
    [ -f "$tree/$project" ] || continue
    grep -q 'Microsoft.NET.Test.Sdk' "$tree/$project" && echo "$project"
  done < <(sed -n 's/.*"\(.*\.csproj\)".*/\1/p' "$tree/$FILTER")
}

# The tracked working tree, minus everything git ignores - which is where the
# prepared game assemblies live. Uncommitted edits are copied as they stand, so a
# contributor can regenerate the list for a test they have not committed yet.
#
# The copy is made a git repository of its own because WorktreeLocator.Find only
# accepts a directory holding both the solution file and a .git, and several tests
# resolve paths through it. A checkout on a runner has one, so this matches it.
copy_game_free_tree() {
  local dst="$1" file
  while IFS= read -r -d '' file; do
    mkdir -p "$dst/$(dirname "$file")"
    cp "$REPO_ROOT/$file" "$dst/$file"
  done < <(git -C "$REPO_ROOT" ls-files -z)
  git -C "$dst" init -q -b main
  git -C "$dst" add -A
  git -C "$dst" -c user.name=skip-audit -c user.email=skip-audit@invalid \
    commit -q -m "game-free copy for the skip audit"
}

# Skipped test names, one per line, from a trx. Reported as outcome="NotExecuted";
# a skipped theory is one entry under its bare method name, because xunit does not
# enumerate the data rows of a theory it is not going to run.
#
# Both greps tolerate no match, because a project where nothing skips is the normal
# case for two of the three and must not read as a broken measurement.
skipped_names_from_trx() {
  { grep -oE '<UnitTestResult [^>]*' "$1" || true; } \
    | { grep -F 'outcome="NotExecuted"' || true; } \
    | sed -E 's/.*testName="([^"]*)".*/\1/'
}

# Non-comment, non-blank lines of the expected list.
expected_entries() {
  grep -v -e '^#' -e '^[[:space:]]*$' "$EXPECTED" || true
}

tree="$REPO_ROOT"
if [ -f "$REPO_ROOT/build/lib/sts2.dll" ]; then
  echo "build/lib/sts2.dll is present, so this tree is not what a hosted runner sees." >&2
  echo "Measuring a game-free copy of the tracked tree instead." >&2
  tree="$scratch/tree"
  mkdir -p "$tree"
  copy_game_free_tree "$tree"
fi

results="$scratch/results"
mkdir -p "$results"
while read -r project; do
  name="$(basename "$project" .csproj)"
  (cd "$tree" && dotnet test "$project" -c Release --nologo --verbosity quiet \
    --logger "trx;LogFileName=$name.trx" --results-directory "$results") >&2
done < <(test_projects "$tree")

# No results at all means the run produced nothing to read, which is a broken
# measurement and not an empty skip set.
if [ -z "$(find "$results" -name '*.trx')" ]; then
  echo "assert-expected-skips: no test results were produced; the measurement failed." >&2
  exit 1
fi

actual="$scratch/actual"
find "$results" -name '*.trx' -print0 \
  | while IFS= read -r -d '' trx; do skipped_names_from_trx "$trx"; done \
  | LC_ALL=C sort -u > "$actual"

if [ "$update" -eq 1 ]; then
  {
    echo "# Every test that skips on a runner without the game, asserted against the"
    echo "# real run by ./scripts/assert-expected-skips.sh. Regenerate with --update."
    cat "$actual"
  } > "$EXPECTED"
  echo "Recorded $(expected_entries | wc -l | tr -d ' ') expected skips in scripts/expected-hosted-skips.txt"
  exit 0
fi

if [ ! -f "$EXPECTED" ]; then
  echo "assert-expected-skips: $EXPECTED is missing. Create it with --update." >&2
  exit 1
fi

expected="$scratch/expected"
expected_entries | LC_ALL=C sort -u > "$expected"

appeared="$(LC_ALL=C comm -13 "$expected" "$actual")"
disappeared="$(LC_ALL=C comm -23 "$expected" "$actual")"

if [ -z "$appeared" ] && [ -z "$disappeared" ]; then
  echo "$(wc -l < "$actual" | tr -d ' ') tests skipped without the game, exactly as recorded."
  exit 0
fi

{
  echo
  echo "The set of tests that skip without the game is not the recorded set."
  echo "CI cannot run these tests, so a change here changes what CI actually covers"
  echo "while leaving its verdict green. That is why the set is recorded by name."
  if [ -n "$appeared" ]; then
    echo
    echo "Now skipped, and not in scripts/expected-hosted-skips.txt:"
    echo "$appeared" | sed 's/^/  + /'
    echo
    echo "A test appears here when it is newly written as [GameFact], [GameTheory] or"
    echo "[BaseLibFact], or when a test CI used to run was moved behind one of them."
    echo "The second case takes it out of everything CI can see."
  fi
  if [ -n "$disappeared" ]; then
    echo
    echo "Recorded as skipped, but not skipped any more:"
    echo "$disappeared" | sed 's/^/  - /'
    echo
    echo "A test disappears here when it was deleted or renamed, or when it stopped"
    echo "needing the game. Deleted and renamed look identical from here, so say which"
    echo "one it was."
  fi
  echo
  echo "If the change was intended, record it in the commit that made it:"
  echo
  echo "    ./scripts/assert-expected-skips.sh --update"
  echo
  echo "and commit scripts/expected-hosted-skips.txt with the test change."
  echo
} >&2
exit 1

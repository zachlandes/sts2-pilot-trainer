#!/usr/bin/env bash
# Writes docs/manifest-format.md from the code that enforces the format, or checks
# that the committed copy still matches it.
#
#   ./scripts/format-reference.sh            regenerate
#   ./scripts/format-reference.sh --check    fail if the committed copy has drifted
#
# The check runs in CI. It has to work on a runner with no game, which is why the
# generator reads both declarations as source rather than loading them: the
# engine-command table is built out of typeof over game types and its assembly does
# not load without a copy of Slay the Spire 2.
set -euo pipefail

readonly REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

exec dotnet run --project tools/Sts2PilotTrainer.FormatReference -c Release -v quiet -- "$@"

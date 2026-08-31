#!/usr/bin/env bash
# Prepares the game assembly and builds everything.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

./scripts/bootstrap.sh "$@"
dotnet build sts2-pilot-trainer.sln -c Release --nologo -v quiet

#!/usr/bin/env bash
# Copies your own Slay the Spire 2 installation into build/lib and prepares it for
# headless loading. The installation is read-only: it is hashed before and after,
# and the run fails if a single byte moved.
#
# Nothing this writes is ever committed - build/ is gitignored, and the game's
# assemblies are MegaCrit's property.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"

dotnet run --project tools/Sts2PilotTrainer.Bootstrap --nologo -v quiet -- --out build/lib "$@"

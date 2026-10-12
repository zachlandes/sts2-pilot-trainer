#!/usr/bin/env bash
# Package only the independent prototype, never the game's inputs
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd -P)"
cd "$root"
./scripts/bootstrap.sh
python3 tools/encounter-intel/import_wiki.py src/EncounterIntel/catalogue/wiki-sources src/EncounterIntel/catalogue/wiki-enemies.json --check
python3 tools/encounter-intel/import_wiki_pages.py src/EncounterIntel/catalogue/wiki-page-sources src/EncounterIntel/catalogue/wiki-pattern-pages.json --check
dotnet build src/EncounterIntel/EncounterIntel.csproj -c Release --nologo -m:1
stage="$(mktemp -d "${TMPDIR:-/tmp}/encounter-intel-package.XXXXXX")"
trap 'rm -rf "$stage"' EXIT
mkdir -p "$stage/EncounterIntel" build/packages
cp build/bin/EncounterIntel/Release/net9.0/EncounterIntel.dll src/EncounterIntel/EncounterIntel.json src/EncounterIntel/catalogue/ATTRIBUTION.txt "$stage/EncounterIntel/"
cp LICENSE "$stage/EncounterIntel/IMPLEMENTATION-LICENSE.txt"
# Replace the archive rather than leaving obsolete entries in an existing ZIP
python3 - "$stage" "$root/build/packages/EncounterIntel.zip" <<'PY'
from pathlib import Path
import sys
import zipfile
stage, output = map(Path, sys.argv[1:])
with zipfile.ZipFile(output, 'w', zipfile.ZIP_DEFLATED) as archive:
    for path in sorted(stage.rglob('*')):
        if path.is_file():
            archive.write(path, path.relative_to(stage))
print(output)
PY

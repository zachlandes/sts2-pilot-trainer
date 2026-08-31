#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="$root/build/parity"
mkdir -p "$out"

curl --fail --location --silent --show-error \
  https://github.com/Alchyr/BaseLib-StS2/releases/download/v3.4.5/BaseLib.dll \
  --output "$out/BaseLib.dll"
curl --fail --location --silent --show-error \
  https://github.com/Alchyr/BaseLib-StS2/releases/download/v3.4.5/BaseLib.json \
  --output "$out/BaseLib.json"

echo 'ad2f89e43e8b31debfab65d783353d9429eba59a2cfe904ff933a894ce79d32e  BaseLib.dll' > "$out/SHA256SUMS"
echo '6d64d1ba9e48abf6e15479a6bda6f2d2b75a277453361a96cbcdd5508acccba3  BaseLib.json' >> "$out/SHA256SUMS"
(cd "$out" && shasum -a 256 -c SHA256SUMS)

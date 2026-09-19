# Build adoption

A game update is adopted deliberately, never by changing a literal during release.

## The adopted build

`scripts/game-build.txt` is the one build this checkout supports: version, build date, commit, branch, the sha256 of the pristine `sts2.dll` and, under the key `id_database_hash`, the `main_assembly_hash` the game's own `release_info.json` names (not the model-id-database content hash the manifests record), every field read off the installation it was taken from by `./scripts/bootstrap.sh`, which writes the same record beside the prepared set as `build/lib/game-build.txt`.
`GameIdentity.Read` holds that prepared record to the bootstrap receipt field for field and refuses a prepared set that carries none, and `GameBuildTests` holds the prepared record to the committed one; `SyntheticFixtureGenerator` and `BaseLibParityProbe` refuse a build the record does not name.

The record names one build, and this is deliberate: before release the project supports the current build of each Steam branch and nothing older, and support for several builds at once begins only after release.

### Provenance

The committed record was taken from a Steam installation on the `public-beta` branch, whose own `release_info.json` names its branch `v0.111.0`; that is the `branch` field, and the pristine hash is that installation's `sts2.dll`.
Nothing in this repository establishes whether the retail branch ships the same build, and the two branches' `sts2.dll` need not hash the same.
If the retail branch's current build differs, it needs an adoption of its own by the procedure below, and holding both at once is outside this pre-release work.

## Before adopting: an archive made before the record existed

`./scripts/bootstrap.sh --archive build/archive` refuses to archive over an existing archive of the same version whose prepared set differs, and an archive made before `game-build.txt` was part of the set differs by exactly that file (`prepared output game-build.txt: archived unknown`).
Such an archive does not prove the build record and is not tolerated: move it aside, or remove it and archive the build again, before the first step below.
Nothing here moves or removes one for you.
A prepared set pointed at through `STS2_PILOT_TRAINER_LIB` that predates the record is refused by `GameIdentity.Read` in the same words as a missing receipt, and the remedy is the same: re-run the bootstrap.

## The ordered checks

`./scripts/adopt-build.sh` runs these in order and stops at the first record whose regenerated copy differs from the committed one, or the first proof that fails, so each change is reviewed before the next record is rewritten over it.
It edits nothing under `manifests/` and refuses a step that does.

1. `./scripts/build.sh --archive build/archive`: prepare the set, archive it under its build and build the CLI every later step drives.
2. Compare the prepared `build/lib/game-build.txt` with the committed `scripts/game-build.txt`; a new build is that diff, and committing the prepared record is the adoption.
3. `./scripts/arbiter engine-commands --update`: `scripts/decision-ledger.txt`.
4. `./scripts/arbiter coverage --corpus manifests --update`: `scripts/decision-coverage.txt` and `scripts/producer-map.txt`.
5. `./scripts/choice-entry-points.sh --update`: `scripts/choice-entry-points.txt` and `scripts/unreadable-choice-scan-bodies.txt`.
6. `./scripts/save-points.sh --update`: `scripts/save-points.txt`.
7. `./scripts/act-topology.sh --update`: `scripts/act-topology.txt`.
8. `./scripts/assert-expected-skips.sh --update`: `scripts/expected-hosted-skips.txt`, with the two figures in AGENTS.md.
9. `./scripts/format-reference.sh`: `docs/manifest-format.md`.
10. Seed verification: `dotnet test tests/Sts2PilotTrainer.Mod.Tests -c Release --filter "FullyQualifiedName~Sts2PilotTrainer.Arbiter.Tests.GeneratedCoverageTests"`; every pinned `SeedHunt` row hunts its seed, plays the walk it pins and replays it to parity, and a row that fails is this step's diff.
11. `./scripts/fetch-baselib-parity.sh && ./scripts/test-session.sh`, read from its final verdict.

Corpus evidence is adopted separately, once the cross-build corpus decision is made; `./scripts/adopt-build.sh --corpus` refuses while it is unresolved.

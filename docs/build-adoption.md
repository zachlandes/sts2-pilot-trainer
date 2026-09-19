# Build adoption

A game update is adopted deliberately, never by changing a literal during release.

1. Run `./scripts/bootstrap.sh --archive build/archive` and inspect the receipt and archive.
2. Review the generated `build/lib/game-build.txt` against committed `scripts/game-build.txt`.
3. Run `./scripts/arbiter engine-commands --update` and review `scripts/decision-ledger.txt`.
4. Run `./scripts/arbiter coverage --corpus manifests --update` and review coverage records.
5. Run `./scripts/choice-entry-points.sh --update` and review choice entry points.
6. Run `./scripts/save-points.sh --update` and review save points.
7. Run `./scripts/act-topology.sh --update` and review act topology.
8. Run `./scripts/assert-expected-skips.sh --update` and review hosted skips.
9. Run `./scripts/format-reference.sh` and review the generated format reference.
10. Run seed verification; every pinned `SeedHunt` row must still satisfy its criterion.
11. Resolve the cross-build corpus decision before adopting corpus evidence; `scripts/adopt-build.sh --corpus` refuses while it is unresolved.
12. Run `./scripts/build.sh && ./scripts/fetch-baselib-parity.sh && ./scripts/test-session.sh` and read its final verdict.

`./scripts/adopt-build.sh --dry-run` performs the ordered, first-diff check without editing manifests.

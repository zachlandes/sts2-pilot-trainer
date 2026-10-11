# Encounter Intel: phase 1 data prototype

`src/EncounterIntel` is a separate mod, packaged as `EncounterIntel.dll` and `EncounterIntel.json`.
It has no Runmobile project references, recorder, replay host, UI, settings, uploads or save writes.
It installs no Harmony patches.
Its initializer logs that the data prototype loaded and does not read the model database.
An isolated retail demonstration has not been performed; the proof here is a headless real-engine lookup, not a claim that an overlay works.

The fit decision follows the scout's distinction: “general reference information during an ordinary run is a different responsibility from Runmobile's deterministic replay, recording, and fight comparison.”
The new mod owns its adapter and catalogue rather than adding encyclopedia behavior to Runmobile.
The tests reuse the repository's existing headless fixtures and IL reader, but neither is a dependency of the packaged mod.

## Supported build and lookup

The adapter supports the inspected game assembly only:

```text
v0.111.0 / 2026.08.14 / 41cef1ea
SHA-256: 9cb4f1ad8c9f284aa8fec3122ffd6d780bbf543d875c817abdd12ff63fbf12b4
```

`SupportedBuild.Require` hashes the loaded game's assembly file once, read-only, and refuses another hash before looking up a run.
The reported version, date and commit describe that hash, not the latest game release or a mod manifest's version claim.
`RuntimeAdapter.ReadCurrent` samples synchronously on demand; a later UI must call it on the game's thread and request another reading after state changes.
It returns detached records, not game models or a resumable snapshot.

- `ActIntel.Id` reads `RunState.Act.Id`, so Overgrowth and Underdocks remain distinct even though both are act index zero.
- Weak, regular, elite and boss pools read the act's public definition properties.
  `PossibleMonsterIds` is a type set, not an opening roster or a generated composition.
- `SelectedBosses` reads the current act's `BossEncounter` and, when `HasSecondBoss` is true, `SecondBossEncounter`.
  It does not inspect future acts' selected bosses or hardcode the second-boss rule to index two.
- `BoardReading` samples an existing combat room, or the already-created `EventCombatSynchronizer.CombatStateForLayout` while an event room is visible.
  It does not materialize a hypothetical board.
- Each enemy retains its slot, current health, block, powers and assigned `NextMove`.
  Attack intents use the game's `GetTotalDamage` over that board's player creatures, including its own local-player damage-modification context.
  A missing announced move is absent, not predicted.

The adapter never reads a seed, an RNG position, an encounter counter, an ordered deal queue or `RoomSet.Next*`.
It never calls `Roll`, `GenerateRooms`, `PullNextEncounter`, `GenerateMonstersWithSlots`, `RollMove` or `FindNextMove`.
The game's public pool properties may memoize definition enumerations; no gameplay state, RNG position, visit counter or profile file changes during lookup in the tested scenarios.
The tests use the arbiter's complete projection, which can see the private queues and RNG streams, as an independent before/after witness; the adapter does not depend on that projection.

Unknown model or intent implementations, unrecognized damage delegates and Harmony changes to the inspected reading or worked-example semantics are refused by name.
This is a conservative base-build adapter, not a compatibility claim about arbitrary mods, untracked runtime edits or every possible hook.
A process without a current run has no lookup.

## Wiki import and licensing

The saved sources are `src/EncounterIntel/catalogue/wiki-sources/`.
Plain raw-text, Special:Export and dump-discovery requests were refused with HTTP 403 / error code 1010.
The authorized Kernel fallback saved the root module and its seven delegates, without a challenge solver, at human-paced navigation intervals.
The saved source text, source URL, revision, fetch time and SHA-256 hash are committed; no runtime request to the wiki is made.
The browser sessions used for acquisition were closed and their deletion checked.

| Module | Revision |
| --- | ---: |
| StS2_data | 28121 |
| Overgrowth | 50310 |
| Underdocks | 50311 |
| Hive | 50309 |
| Glory | 50308 |
| Elites | 50306 |
| Bosses | 50305 |
| Events | 50307 |

`tools/encounter-intel/import_wiki.py` reads only that saved bundle.
It parses literal Lua tables without evaluating Lua, reconciles the delegates, verifies hashes and revision URLs, and accepts only the known editorial-link wrapper after a table.
Unknown expressions, changed wrappers, duplicate keys, mixed tables, missing modules and escaping paths fail rather than producing partial data.
The generated `wiki-enemies.json` contains 109 editorial enemy records, preserving move text and ascension variants as wikitext.
It is not rendered HTML, executable content or a build-verified denominator.
The importer omits art filenames from enemy fields and does not fetch any art.
Encounter markup still contains wiki links describing composition.

The adapted wiki data and saved source tables retain **CC BY-SA 4.0**, with contributor attribution, source revisions, license link and a changes notice in the catalogue and `catalogue/ATTRIBUTION.txt`.
The independently authored implementation uses the repository's MIT license.
The separate package includes both notices and no game assemblies, decompiled game code or art.

Regenerate or check the catalogue offline:

```bash
python3 tools/encounter-intel/import_wiki.py \
  src/EncounterIntel/catalogue/wiki-sources \
  src/EncounterIntel/catalogue/wiki-enemies.json
python3 tools/encounter-intel/import_wiki.py \
  src/EncounterIntel/catalogue/wiki-sources \
  src/EncounterIntel/catalogue/wiki-enemies.json --check
python3 -m unittest discover -s tools/encounter-intel -p 'test_*.py' -v
```

The importer is the only writer of the generated catalogue.
A later refresh needs a newly authorized source acquisition and a review of its diff, not an automatic scraper in gameplay.
An optional Codex API cross-check attempted the version-qualified Nibbit endpoint and received HTTP 403; it was not retried or used to fill missing facts.
No fresh Codex agreement is claimed by this change.
The local game's getters and commands are the authority for the implemented proof.

## Worked examples and mismatch handling

`ReviewedExamples.Monsters` converts the bounded wiki examples to typed effects and keeps independently reviewed openings, transitions and encounter phases bound to the supported assembly hash.
Only Nibbit, Phrog Parasite, its Wrigglers and Sludge Spinner are semantically reviewed here.
A changed or additional effect in their wiki wording is named unsupported; the rest of the imported catalogue remains editorial data.

| Monster | HP below A8 / A8+ | Complete reviewed moves |
| --- | --- | --- |
| Nibbit | 42–46 / 44–48 | Butt 12 / 13 at A9; Hesitant Slice 6 / 7 at A9 plus Block 5 / 6 at A8; Hiss Strength 2 / 3 at A9 |
| Phrog Parasite | 61–64 / 66–68 | Infect adds 3 Infection to players' discard piles; Lash 4×4 / 5×4 at A9 |
| Wriggler | 17–21 / 18–22 | Spawned is stunned with no gameplay work; Nasty Bite 6 / 7 at A9; Wriggle adds 1 Infection to discard and gains 2 Strength |
| Sludge Spinner | 37–39 / 41–42 | Oil Spray 8 / 9 at A9 plus 1 Weak; Slam 11 / 12 at A9; Rage 6 / 7 at A9 plus 3 Strength |

Nibbit cycles Butt → Slice → Hiss and opens Butt alone, Slice in the front or Hiss in the back.
Phrog opens Infect and alternates with Lash; the unused random state is not classified as a reachable random pattern.
It starts with Infested 4; its death hook produces four stunned Wrigglers in slots `wriggler1` through `wriggler4` and prevents combat ending during the spawn.
After Spawned, slots one and three start Nasty Bite, while two and four start Wriggle, then alternate.
The stun is a spawn-context rule, not a claim that every Wriggler begins stunned.
Sludge Spinner opens Oil Spray; all three moves lead to equal-weight alternatives with `CannotRepeat`, rather than a permanently eligible three-way distribution.
No next-turn forecast is implemented.

`RuntimeAdapter.ReadWorkedExamples` checks the wiki quantities against the current run's ascension context without constructing monsters or move graphs.
`CheckedMonster.Mismatch` and each `ResolvedQuantity.Mismatch` are explicit data flags when the wiki and inspected game values differ.
Both quantities are retained; the live game value remains available to a later presentation without silently rewriting the wiki evidence.
`QuantityAuthority` distinguishes live numeric getters, game metadata constants and reviewed command-body literals.
A reviewed literal is not described as a live getter reading.
The reference HP range is before multiplayer scaling; the visible enemy record separately carries its actual current and maximum HP.
Worked-example attack quantities are ascension-adjusted reference values, while the visible board's announced damage includes the current powers and native damage hooks.

## Reproduce the proof and package

These commands prepare only local inputs and run the narrow suite, not the full generated-coverage suite.
Coordinate the full repository gate with other lanes before starting it.

```bash
./scripts/bootstrap.sh
dotnet build tests/Sts2PilotTrainer.Mod.Tests/Sts2PilotTrainer.Mod.Tests.csproj \
  -c Release --nologo -m:1
./scripts/test-session.sh tests/Sts2PilotTrainer.Mod.Tests/Sts2PilotTrainer.Mod.Tests.csproj \
  -c Release --no-build --nologo --filter FullyQualifiedName~EncounterIntelTests
./scripts/package-encounter-intel.sh
```

The narrow session completed with 21 passing cases and no skips on the identified build.
The importer's six tests passed, including malformed-input and provenance negative controls.
The suite covers both act-one variants, A9's single and A10's double current-map bosses, quantities below and at A8/A9, live announced intents, Nibbit's context-dependent openings, Sludge Spinner's real branch table, Phrog's actual death hook and its spawned opening turn, and an event board before combat begins.
Repeated lookups compare the complete gameplay digest and sandbox profile-file hashes and modification times before and after.
Applying Strength and Weak through the game's own commands changes the announced Oil Spray reading from 8 to 11 to 8, while each lookup leaves those powers and the complete digest unchanged.
Changing a wiki HP range produces a visible mismatch, changing effect wording refuses, changing the build hash refuses, and a patched numeric getter refuses until unpatched.
During a live fight, Harmony prefixes observe every non-generic method of the game's `Commands` types, the `Rng` stream's non-generic `Next` methods, `ActModel.GenerateRooms`, `PullNextEncounter` and `PullNextEvent`, `EncounterModel.GenerateMonstersWithSlots` and `MonsterModel.RollMove` while `ReadCurrent` and `ReadWorkedExamples` run, and none is called.
An executed `RollMove` under the same prefixes is recorded, so the observation can actually see a call; generic members and call sites the JIT inlined are outside it.
A separate load context proves that the mod's types can be enumerated with all Runmobile siblings unreachable.

For reviewable readings from the actual engine, set `ENCOUNTER_INTEL_DEMO` to a temporary directory when running the narrow suite.
The tests then write detached JSON readings there; ordinary test runs write none.
The readings include both act variants, current bosses, the four checked monsters at each tested ascension, the live spinner intent, the spawned Phrog phase and the event board.
They contain no seed, private queue, profile identity or machine path.

The package is `build/packages/EncounterIntel.zip` with one independent mod folder, its manifest and DLL, and the attribution and implementation-license notices.
It is not included in `package-mod.sh` or the Runmobile installer.
Do not install or launch it in a player's regular profile to prove this phase.
UI work and an explicitly authorized isolated retail check are still later steps.

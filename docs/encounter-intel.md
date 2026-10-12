# Encounter Intel: build-scoped move graphs and reference catalogue

`src/EncounterIntel` is a separate mod, packaged as `EncounterIntel.dll` and `EncounterIntel.json`.
It has no Runmobile project references, recorder, replay host, UI, settings, uploads or save writes.
It installs no Harmony patches.
Its initializer logs that the data prototype loaded and does not read the model database.
An isolated retail demonstration has not been performed; the proof here is a headless real-engine lookup, not a claim that an overlay works.

The fit decision follows the scout's distinction: “general reference information during an ordinary run is a different responsibility from Runmobile's deterministic replay, recording, and fight comparison.”
The new mod owns its adapter and catalogue rather than adding encyclopedia behavior to Runmobile.
The tests reuse the repository's existing headless fixtures and IL reader, but neither is a dependency of the packaged mod.

## Supported build and lookup

The build registry currently admits only the inspected beta game assembly:

```text
v0.111.0 / 2026.08.14 / 41cef1ea
SHA-256: 9cb4f1ad8c9f284aa8fec3122ffd6d780bbf543d875c817abdd12ff63fbf12b4
```

`SupportedBuild.Require` hashes the loaded game's assembly file once, read-only, and resolves it through `catalogue/builds.json` before looking up a run.
An unregistered beta or retail hash refuses.
Registering another build requires its own evidence and `catalogue/builds/<sha>/` inputs and atlas; registering its identity does not reuse this beta's reviewed worked examples or numeric bindings.
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
Schema 2 also imports encounter icon rows and party descriptions into typed records with source references.
Repeated icon links retain their multiplicity, while party descriptions retain alternative compositions and spawn context as editorial text.
Both are related evidence, not verified opening rosters.
The importer extracts unambiguous damage, Block, Strength and hit-count claims, retaining their ascension thresholds and source-text offsets.
Complex formulas stay in the move text rather than being partially interpreted as constants.
The pinned modules contain no move-pattern field; every imported enemy names that gap instead of receiving a guessed pattern.
An entry without intents, such as the Knight Gang group, names its missing moveset.

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

## Full catalogue and coverage

The conceptual-fit decision for phase 2 is to extend the existing importer and adapter.
No new scraper, game-command interpreter, Runmobile dependency or configuration surface was added.
The source bundle did not change during phase 2, which made no wiki or Codex request.

`RuntimeAdapter.ReadCatalogue` returns the full reference catalogue in the current run's ascension context.
It enumerates monster and encounter type metadata in the supported assembly, resolves registered canonical models, and reconciles every registered monster or encounter in `ModelDb.All` against that enumeration.
Abstract definitions, unregistered definitions, types that cannot load, mock models and definitions with no wiki counterpart are named unsupported rather than disappearing from the denominator.
This is broader than `ModelDb.AllEncounters`, which omits the Dense Vegetation and Punch Off event encounters on this build.
It reads definition pools only, never an encounter's generated roster, its RNG or a dealt queue.

Each monster retains its wiki moves, source module, initial-power text and editorial spawn statements.
Exact identity aliases bind the Ruby Raiders, Battle Friend variants and Merchant to their game names.
The three Decimillipede segment types share the editorial Decimillipede entry, and Test Subject's three editorial phases bind to the same game model with separate HP getters.
These identity bindings do not establish their opening patterns or reactive transitions.
The existing four reviewed monsters and four reviewed encounter rules remain the examples with fully reviewed effect semantics; nothing in the broader import receives that authority by association.
The separate move-graph atlas below supplies structural patterns and inspected public context rules without claiming that every move's effects were verified.

The adapter checks reference HP and an allowlist of inspected, side-effect-free numeric getters.
Move bindings require both the monster identity and the move's normalized name and quantity kind.
A missing binding is a named unchecked quantity, not a comparison against any getter whose number happens to match.
Axebot's reference HP is flagged as context-dependent because its getter needs mutable Stock context; the existing visible-board reading still supplies actual HP.
Private inherited getters are read from their declaring type, which permits the segment and Mysterious Knight definitions without creating a new monster instance.

`CatalogueMonster.MayBeOutdated` and `Warning` retain a visible mismatch disposition alongside every wiki and game value.
`CatalogueStanding` separates catalogued, flagged and unsupported records.
Missing patterns, unverified effect text and unchecked quantities are gaps distinct from numerical disagreement.
`CatalogueEncounter.RelatedWikiCompositions` explicitly names related editorial evidence: sharing a monster does not prove that a row describes the same encounter, weak-versus-regular variant or opening phase.
`ReviewedRule` supplies the original verified opening counts and slots.
The move-graph atlas adds independently inspected context alternatives and source-described phases, with their authority retained separately from executed evidence.
The Phrog death phase still describes four stunned Wrigglers and prevention of combat end through that existing rule.

`CatalogueReading.Coverage` is the report, with a disposition and reasons for every game definition and every imported editorial encounter.
Wiki enemies absent from the build are listed separately, including Doormaker and the Knight Gang group.
No missing moveset or composition is represented as a successful empty record.
The API has no UI in this phase; a later surface must show the warning and gaps rather than treating `Flagged` as verified.

The pinned wiki modules still supply no pattern text for an independent cross-check.
Outstanding interpretation gaps include unbound encounter contexts, complex quantities, and full semantics for move effects and power hooks.
Event fights remain separate definitions: Dense Vegetation's four-Wriggler icon row is imported, while a missing event-specific composition is named in its coverage row.
These are source or interpretation gaps, not permission to fetch another wiki revision or generate a hypothetical encounter.

To reproduce the coverage report without a retail client, use the narrow test command below with `ENCOUNTER_INTEL_DEMO` set to a temporary directory.
`catalogue-a0.json`, `catalogue-a7.json`, `catalogue-a8.json` and `catalogue-a9.json` contain the full detached catalogue and coverage rows.
The tests compare every registered definition against those rows, sample complete gameplay and profile receipts before and after lookup, and run the catalogue under the existing command/RNG/deal observation guard.
They also edit an unreviewed number to require a warning, remove an enemy to require an unsupported row, and rename a move to require an unchecked binding.
These headless readings establish catalogue behavior and read-only lookup, not retail UI compatibility or complete semantic verification.

## Offline move-graph derivation

The fit decision is to extend Encounter Intel's existing catalogue adapter, with an offline generator in the existing test project.
The packaged mod reads an embedded atlas; it never constructs a hypothetical monster in the player's run.
`EncounterIntelMod.Patterns()` is available without a current run, while `Catalogue()` attaches the selected ascension's patterns and all encounter schedule columns.

`EncounterIntelPatternGenerator` clones each registered canonical monster and calls only its graph setup.
It supplies ascension through a scoped read adapter, creates no creature or fight, and never executes a move, predicate, weight delegate, spawn hook or encounter roster factory.
Constant weights are read from captured float arguments or exact literal-return bodies through the engine's existing IL reader.
Unknown weight bodies stay uninterpreted.
The probe uses a fresh test process because a previous test can inline the game's ascension read before the scoped adapter is installed.
It preserves the existing worktree sandbox containment rule.

On this beta all 126 registered monster models construct without fight context, including the registered mocks and non-combat definitions named in the denominator.
The atlas retains every state, ordered conditional edge, weight, cooldown, repeat policy and mandatory transition flag.
Structural reachability distinguishes Phrog's unused random branch from its actual alternating graph.
This is reachability through all graph edges, not a prediction that a particular predicate will succeed.
Unknown state shapes are flagged with gaps, and a failed construction names the exception without starting a fight to repair it.

`semantics.json` contains build-scoped, source-inspected bindings for predicates, encounter property setters, move-name aliases, dynamic quantities and reactive phases.
It contains facts rather than extracted game code.
Openings include each inspected slot or offset alternative, never the choice the run dealt.
The direct-call scan of `CreatureCmd.Add`, `CreatureCmd.Stun`, `SetMoveImmediate` and `ForceCurrentState` names producer members with source references or gaps.
It uses the existing IL reader and does not establish complete coverage of indirect hooks or unreadable bodies.
Reactive rules describe changes outside the ordinary graph, including death, revive, stun, summon and in-place hatch transitions; none was executed by this derivation.

`patterns.json` is generated and compared in the merge suite, not edited by hand.
Its header binds the whole atlas to assembly identity, graph schema and the game's maximum ascension.
The beta's equal columns compress to thresholds 0, 8 and 9, covering A0 through A10.
Unknown builds and graph schemas refuse instead of inheriting beta assumptions.
The old reviewed examples remain bound to their original assembly hash even after another build is registered.

`CatalogueEncounter.DamageSchedules` lists possible members and every inspected opening alternative by ascension column.
A schedule follows fixed edges for nine turns, accumulating only inspected self-Strength gains, and stops at a conditional or random branch.
It is reference damage before initial powers, allies or player interference, not announced damage or a next-turn forecast.
An unknown prior buff leaves later damage absent rather than showing a plausible zero.
Dynamic first-use values carry their rule, such as Multi Claw's increasing hit count or Pressure Gun's increasing damage; later dynamic values are not simulated.
A context-bearing model without a binding for that encounter has no inferred opening schedule.

For example, the beta Nibbit references begin as follows:

| Encounter context | A0–A8 reference turns 1–6 | A9–A10 reference turns 1–6 |
| --- | --- | --- |
| Alone | Butt 12, Slice 6, Hiss, Butt 14, Slice 8, Hiss | Butt 13, Slice 7, Hiss, Butt 16, Slice 10, Hiss |
| Front | Slice 6, Hiss, Butt 14, Slice 8, Hiss, Butt 16 | Slice 7, Hiss, Butt 16, Slice 10, Hiss, Butt 19 |
| Back | Hiss, Butt 14, Slice 8, Hiss, Butt 16, Slice 10 | Hiss, Butt 16, Slice 10, Hiss, Butt 19, Slice 13 |

Graph damage and hit-count comparisons require an explicit monster-and-move alias and an unambiguous quantity.
`QuantityAuthority.ConstructedGraph` distinguishes them from live getters and reviewed command-body literals.
The adapter preserves both numbers and flags disagreement; a name collision or dynamic ambiguity does not become an agreement.

## Cached enemy-page cross-check

An initial non-stealth acquisition stopped at a challenge without saving pages.
A later authorization permitted one paced stealth Kernel session to cache enemy-page source revisions and their redirect targets.
Acquisition remains separate from graph generation, and rerunning the importer or checker makes no network request.
Each saved page retains its revision URL, timestamp and hash; a resolved redirect retains both the redirect receipt and the target's receipt.
The session saved 96 pinned source revisions, including six redirect targets, and its deletion was verified.
The 17 requested pages without a source receipt remain named gaps rather than a successful empty cache.
The first three were left without a receipt while correcting an edit-page revision reader; the others did not supply a public source textarea and revision together under their requested titles.

The check is deliberately narrow, as the captain approved: it covers fixed move sequences, opening alternatives, damage and hit numbers, odds or weights, and repeat limits, including once per fight, once every N turns and literal group summon caps.
Everything else on a page - other prose, predicates and reactive triggers - stays unchecked fallback text and never counts as agreement.

`import_wiki_pages.py` preserves source text and extracts bounded literal assertions from explicit pattern sections: fixed cycles or paths, opening alternatives, self-loops, unambiguous reference damage or hit counts, stated branch odds and stated repetition rules.
It reads every pattern section the enemy owns, each kept as a region with its source line: every phase of a tabbed boss, and an anchored enemy's same-level sibling `Pattern` section as well as its own.
A section under another enemy's heading and infobox belongs to that enemy's page, never the host's, and a table row's move takes its owner from the table it is in.
Each assertion carries its exact source passage and line number.
Literal attack quantities are read after "Deal" or "Deals" in every written form: `damage ×N`, `damage xN`, a packed `(DxN;{{Asc2|T|D'xN'}})` amount and a hit count with its own ascension value; a formula that is not literal numbers stays out.
A fixed sequence is read from "cycles", "alternates" or "cycle:" followed by its linked moves, or from a `#` or `*` list introduced as a sequence, cycle or pattern; the existing qualifier guards still drop a sequence that names a condition, check or random choice.
An intent-table row runs from its opener to the brace that closes it, whether that closes on the effect line or on a line of its own, so nested templates never merge two rows.
Odds are read only where a sentence writes them literally: a fraction, a percentage or a weight beside every move of a random pick, an equal-chance qualifier, "twice as likely", or a bulleted list of percentages.
Repetition is read only from "cannot be used twice in a row" or "no restriction on repeating", bound to the moves it names or to the random pick it follows.
A use limit is read only from "can only be used once per fight" or "once every N turns", bound to the one move before it in that sentence or, where the sentence names none, the one move the sentence before it names.
A literal "the group can summon up to N times total" or "the group may summon at most N times total" binds to the one move named before it or in the preceding sentence.
Missing or ambiguous move bindings, conditional cap wording and competing numeric bounds stay `unparsed`.
An opening alternative needs its opening verb and its one move in the same sentence, so a starting power beside a summoning move is not read as an opening.
Odds or repetition wording that binds to no moves, or annotates only some of them, is kept as an `unparsed` claim and is always unresolved rather than matched.
Complex formulas and conditional sequence wording are not partially interpreted.
`WikiPatternComparison.Compare` holds those assertions to explicit move aliases in the constructed graph without running game code.
Stated odds are compared with the declared per-roll weights of the one random branch that offers exactly those aliased moves - the initial state for an opening, a branch a move leads to otherwise - within half a percentage point for rounding.
Repeat exclusions and long-run frequencies are not computed, so odds a page derived from a repeat rule disagree with the declared weights and are shown as such.
A fixed sequence that passes through a random branch whose weights and repeat rules are all known is held to that branch: it agrees only where the repeat rules leave exactly the stated next move, and a branch that can also offer another move is a mismatch; a predicate or context-dependent weight stays unresolved, and no roll is made.
A stated repetition rule is compared with the repeat rule on every branch edge into that move: once per fight with `UseOnlyOnce`, once every N turns with a cooldown of N.
A group summon cap is compared separately with that move's build-scoped shared-counter bound, not its per-monster repeat policy.
For the Rat, the existing IL reader reads the constant compared with `CallForBackupCount` in `CanSummon`; `CallForBackup`'s counter sharing is an inspected source binding, and neither member executes.
A changed or unreadable comparison shape refuses generation; absent or conflicting cap bindings leave the comparison unresolved.
The cached Rat claim agrees on three total summons across every inspected opening context, while an edited cap produces a mismatch.
Its dispositions are partial agreement, mismatch and unchecked; each claim separately names matched, mismatch or unresolved.
A missing graph still gets the cached editorial fallback, never an invented game pattern.

An opening match establishes that the named opening exists in an inspected context, not that an uninterpreted prose qualifier applies to every encounter.
Remaining effect prose, conditional predicates and reactive triggers are not made verified by nearby matching claims.
`CatalogueMonster.MayBeOutdated` also flags page-claim disagreements and retains both quantities.
The current-run catalogue compares its ascension, and `CachedEnemyPagesAreCrossCheckedAtEveryAscensionWithoutStartingARun` checks every supported ascension, including wiki thresholds between compressed graph columns.
`PatternComparison.SourceText` is the source fallback and `PatternGaps` retains the acquisition receipt.
At A0, the beta's 126 registered graph rows have 79 partial agreements, six mismatches, five unchecked readings and 36 without an inspected source binding; these include mocks and non-combat models and are not counts of wiki pages.
The compared A0 claims are 288 matches, nine mismatches and 52 unresolved assertions, seven of them unparsed odds, repetition or limit prose.
Vantom's cached Dismember row says 27 damage below A9, while this beta's constructor supplies 26, and both say 30 at A9.
The page's own V0.104.0 update history records the reduction to 26, but its intent row was not updated.
Flyconid's page gives its opening 2/3 Frail Spores and 1/3 Smash, and Hunter Killer's makes Puncture twice as likely as Bite, where both branches declare equal weights.
Fossil Stalker's page says nothing restricts repeating a move, and Mysterious Knight's that no move is used twice in a row, where Fossil Stalker's three moves and Mysterious Knight's Flail and Ram may each repeat twice.
Spectral Knight's page says it alternates Soul Slash and Soul Flame, where its branch lets Soul Slash follow itself.
Each source passage and both sides of the disagreement remain visible rather than rewriting the wiki page.

```bash
python3 tools/encounter-intel/import_wiki_pages.py \
  src/EncounterIntel/catalogue/wiki-page-sources \
  src/EncounterIntel/catalogue/wiki-pattern-pages.json --check
```

## Worked examples and mismatch handling

`ReviewedExamples.Monsters` converts the bounded wiki examples to typed effects and keeps independently reviewed openings, transitions and encounter phases bound to the supported assembly hash.
Only Nibbit, Phrog Parasite, its Wrigglers and Sludge Spinner are semantically reviewed here.
A changed or additional effect in their wiki wording is refused; the rest of the imported catalogue retains editorial text and explicit gaps beside its bound live quantities.

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

The narrow suite includes the phase-1 engine proofs, full-catalogue cases and offline graph derivation.
Its older semantic examples execute bounded fights; the new graph probe does not.
To regenerate an atlas deliberately, set `ENCOUNTER_INTEL_PATTERN_UPDATE=1` and filter to `EveryMonsterGraphIsDerivedWithoutRunFightCommandsOrRandomness`, then rebuild to embed the new resource before running the suite again.
Read its final `TEST SESSION` verdict rather than inferring success from printed totals.
The importer tests include malformed-input and provenance negative controls, typed composition references and a hit-count ascension case that would otherwise produce a false disagreement.
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

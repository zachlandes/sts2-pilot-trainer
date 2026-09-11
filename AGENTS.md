# sts2-pilot-trainer

A deterministic replay arbiter for Slay the Spire 2: reconstruct a run from a video,
replay it through the real game engine, and check the result against what the video
shows. `Runmobile` is the mod a player installs to play from a reconstructed run; it is
not released yet. See [README.md](README.md).

## Build / test / run

```bash
./scripts/build.sh          # bootstrap the game assembly copy, then build everything
./scripts/package-mod.sh    # build the platform package without game content
./scripts/install-mod.sh    # package and install the mod, preparing game inputs locally
./scripts/protected-files.sh snapshot <ledger>   # hash everything the mod must not change
./scripts/protected-files.sh compare  <ledger>   # ... and say what a session changed
./scripts/build.sh && ./scripts/fetch-baselib-parity.sh && ./scripts/test-session.sh   # the suite, with one verdict
./scripts/arbiter gate manifests/navegreed-OJ-6QXhNgdg.replay.json   # the whole standard, one verdict
./scripts/arbiter <command> # gate | validate | preflight | preflight-live | adopt-live |
                            # verify-seed | replay | determinism | negative-controls |
                            # combat-snapshot | floor-snapshot | combat-compare |
                            # enter-fight | recorded-fight |
                            # snapshot-restore-probe | migrate-manifest | engine-commands
./scripts/bootstrap.sh --archive build/archive   # keep the receipted prepared set under its build
./scripts/assert-expected-skips.sh          # what CI skips is still what we recorded (--update to re-record)
./scripts/format-reference.sh               # rewrite docs/manifest-format.md from the code (--check to compare)
```

`dotnet test` works without the game: the integration suite skips with an explanation
and the pure suite still runs.
Run it on its own only when that is what you want, because the skip is silent in the
totals and the suite still reports green.
Building first is what makes it run everything: nothing in the solution references
`Sts2PilotTrainer.Cli`, so `dotnet test` never builds the arbiter the integration
tests drive, and bootstrapping alone leaves every test that drives it skipped.
Every test run is bounded by `TestSessionTimeout` in `.runsettings`, wired in from
`Directory.Build.props` so it applies however `dotnet test` was started. A run that
exceeds it aborts with a non-zero exit rather than hanging: a deadlocked test used to
wait for as long as anybody let it. Raise the bound rather than removing it;
`.runsettings` records the rationale for the current bound.
**Read the verdict from `./scripts/test-session.sh`, never from the printed totals.**
`dotnet test` can report an aborted session as `Passed!` with a partial count. The
script refuses a non-zero exit or an abort marker, suppresses that misleading success,
and prints its verdict last. `TestSessionVerdictTests` holds it to that against a real
timed-out session.
`scripts/arbiter` goes through `dotnet <dll>` rather
than the generated apphost, which needs `DOTNET_ROOT` that a Homebrew install does
not set.

## Conventions

**The game is a read-only input.** `scripts/bootstrap.sh` copies the player's
installed assemblies into `build/lib` and hashes the installation before and after.
The headless host also routes every engine write into a sandbox and throws on any
path inside a Steam or Slay the Spire 2 directory. Do not weaken either.

**Nothing extracted from the game or from a source video is ever committed.**
No game assemblies, localization tables, source-VOD frames or source-VOD stills.
`.gitignore` blocks the file types; the judgement is yours.
The sole visual exception is screenshots of this mod's own UI captured in the player's client and committed under `demo/` as product evidence; that is not permission to commit any source footage.
Facts read from a video are fine and are what `manifests/` holds.

**One owner for game-version-specific code.** Everything that knows how v0.111.0 is
put together lives in `Sts2PilotTrainer.Engine`. `Sts2PilotTrainer.Replay` must stay
free of the game assembly — that is what lets the format and its tests outlive a
build.

**Find the engine's own command before writing one.** Every verb in `RunDriver` maps
onto a method the retail client calls; none of them reimplement game logic. Reading
`build/lib/sts2.dll` is how that is established — decompile it into a scratch
directory outside the repository, never into it, and never commit anything you find
there. `ilspycmd -p -o <scratch> build/lib/sts2.dll` does the job in about twenty
seconds; on a Homebrew .NET it needs `DOTNET_ROOT` set to the `libexec` directory and
`DOTNET_ROLL_FORWARD=Major`.
The client's own command is found the same way and written in `ClientCommands` beside it: per verb the running client issues, the screen handler the retail button reaches, whether the driver calls the engine member or the host supplies the screen's command, and the lock that keeps the decision the recording's; its `Verify()` holds the table's verbs equal to `RetailPlayback.Verbs`, every handler and lock to this build, and every patch the journey hangs to a row or a written excuse, and `RecordedFightModule` refuses on it as the recorder refuses on a renamed member.

**Provenance is not decoration.** Every value in a manifest records whether it was
observed, inferred, engine-produced, declared or captured, and each carries the
coordinates that let someone re-check it - the video timestamp for an observation,
the action ordinal in the run for a captured value. The validator enforces the parts
it can. Do not add a field without deciding which of those it is.

**Where a player can be stood is `boundaries[]`, and its kinds are a closed set.**
`combat_start`, `floor_entry` and `turn_start`, enforced by the validator, because a
host dispatches on them. Every boundary's digest is engine-produced or captured from
a live game - no video shows draw order or a random stream's position. Reading an
older manifest is `ManifestJson`'s job and happens in memory; rewriting one on disk
happens only in `./scripts/arbiter migrate-manifest`, so reading somebody's evidence
never edits it. A boundary is named in two spellings on purpose and
`BoundarySelector` owns both: the kind's own way - `combat_start:2`, `floor_entry:5`,
`turn_start:2.3` - for the commands that take `--boundary`, and `enter-fight`'s
readable `--fight n` or `--floor n`, which was named that way in its own specification
and stays. `Parse` reads the first, `ParseFightOrFloor` the second, and `PlanFor` is
the one place a boundary becomes a plan whichever spelling asked for it, so a
coordinate has one reader and each spelling refuses in its own words. Do not read one
anywhere else; an ordinal counted across the list would mean a different thing per
kind.
Where a manifest carries a verified whole-run trace, every declared boundary of every
kind is cross-checked against it - a fight the trace holds and finishes, a floor it
arrives on, a turn that fight takes, each at the action the trace says.
`ManifestValidator` reads those off `RunCoverage`, which is what the derive path builds
its boundaries from, so the guard and the deriver cannot disagree about one history.
Without that cross-check, a coordinate can pass publication on shape alone and be refused later, in front of a player.
Do not add a boundary kind without its cross-check.
Everything else that entry demands of a floor_entry is refused there too, and needs no
trace to ask: the action it names is the map move that arrived, and a checkpoint there
names `run.total_floor` and `run.map_coord` for the floor the boundary names, resolved
the way `FloorEntryPlan.For` resolves several checkpoints at one action.
`FloorArrival` is the one owner of that arrival - the validator re-derives through it
and `migrate-manifest --derive-boundaries` writes through it before it replays as well
as after, so the deriver's own output validates and a recording written before the
recorder sampled the coordinate has one repair rather than none.
A derived arrival is inferred and says so, and is admissible only
because this validator can re-derive it from the recorded map move.
The file a recorder or a stranger hands over carries no trace, so `gate` asks the
validator again of the verified copy its own replay wrote, as the `declared-boundaries`
condition; without that the cross-checks never run on the manifests they exist for.
The gate's `combat-boundary` condition then compares the complete hidden-state digest at every declared boundary the verified replay also derives, by that kind's own coordinate; checking only the first fight is not publication evidence.

**Real-engine reproduction is the publication standard.** `gate` is where it is
written down and computed. No condition may be satisfied by a cheaper proxy - not
reader confidence, not arithmetic over the footage, not a screenshot of a mod list.
Those are filters worth having and they are not evidence: four of the ten history
corruptions pass every arithmetic check the frames allow.

**A boundary is re-derived, never deserialized, except at a floor arrival with a live
fight, which may be restored - headlessly and in the client - and only through the one
cache that verifies it.**
`./scripts/arbiter snapshot-restore-probe` measured the game's own save round trip at a
combat-start boundary on v0.111.0: `SerializableRun` carries the run's identity and
hidden state - seed, every RNG stream position, act room set, deck order and relics - but
no combat, so a combat start that is not also a floor arrival keeps meaning "replay the prefix".
`./scripts/arbiter floor-snapshot` measured the floor arrival, which is a different
moment: the retail client takes its own run save inside `EnterMapPointInternal`, at the
floor and before the room type is rolled, and restoring it through the retail continue
path with the save's own `preFinishedRoom` reproduces the boundary digest field for field
wherever a fight is live there. Where one is not, the live run is still carrying the
previous fight's finished `PlayerCombatState` and the save has no representation of it -
the same run, a different canonical state - so `FloorEntrySnapshotEligibility` refuses it
by rule rather than leaving it to be noticed.
A snapshot binds by that moment and not by a plan's kind: the arrival and the fight the same map move dealt are one engine state and `RunCoverage` derives both digests from it, so `FloorEntrySnapshot.Binds` accepts any plan whose boundary is at the snapshot's own action and whose declared digest is the one the snapshot was verified at, and `enter-fight --fight n --restore` restores a fight past the first exactly as `--floor n --restore` does.
`RetailPlayback.RouteTo` is the one reader of what that makes reachable in the client - walk, restore, restore then walk, or the first decision no route gets past - read from the recording alone, and `RetailPlayback.RestorableArrivals` is the manifest's reading of the live-fight rule: an arrival with a combat start declared at the same action.
The run library offers on that answer and `RecordedFightRun.Start` executes it; nothing else re-derives reachability.
Inside the client the restore is the retail continue handler's own path - `GameSession.PrepareRestoreInRunningGame` is its engine half and the public `NGame.LoadRun` its presentation half, with the save's own `preFinishedRoom` - and the save it continues is materialised by the packaged arbiter in its own process into `RunmobileStore`'s `snapshots/cache`, never by the mod.
The packaged arbiter runs from a game installation, with no worktree above it and none of the running game's own environment: `PackagedArbiterTests` in the arbiter suite runs the built arbiter from a copy outside any worktree, and the one in the mod suite holds the spawned child's environment free of what the game exports; the retail proof found both the hard way.
Both answers, their numbers and their limits are in
[docs/native-replay-format.md](docs/native-replay-format.md).
The cache is a derived one and stays that way: keyed by `SnapshotCacheKey` over the whole
history that produced it, written only after a restore in a fresh process has already
reproduced the digest the recording declares, and re-verified through the same
`BoundaryEquality` on every restore. Do not add a second one, do not cache a boundary the
measurement did not cover, and do not make the ineligible arrivals pass by changing what
`CanonicalStateProjection.ProjectCombat` emits - that moves every committed boundary
digest and is a separate change with its own migration.

**Refuse rather than approximate.** An unknown action verb, a card that is not where
the manifest says, a mismatched environment: each of these fails loudly. A replay
that quietly does something plausible is the failure mode this whole project exists
to prevent.

**What CI cannot run is recorded by name.** On a runner without the game, the 150
tests named in `scripts/expected-hosted-skips.txt` skip out of the 214 cases
`Sts2PilotTrainer.Arbiter.Tests` reports there, and the job still reports success.
Both figures are what a game-free run prints and neither can be arrived at by adding
up attributes: a `[GameTheory]` skipped there is one case and expands into a row per
datum where it runs, so a run with the game reports more cases than 214.
`./scripts/assert-expected-skips.sh` asserts the skipped set against that list, so
adding a `[GameFact]`, moving a test behind one, or deleting one fails CI until the
list is regenerated with `--update` in the same commit. It catches structural drift
only. A test that skips there and is broken inside is caught by the local gate, which
runs everything.

**Read [docs/environment-identity.md](docs/environment-identity.md) before touching run setup or preflight.**
Two fields on that list are there because a replay looked correct and was not: the act variant and the player's unlock state.
Both change every fight in a run while leaving the map identical.
The document also owns the distinction between a runtime reading and an explicitly supplied headless progress model.
`LocalEnvironment` owns the v0.111.0 adapter, `EnvironmentPreflight` owns the game-free rules, and neither path writes.
What a mod *says* about itself and what it *did* are two readings, not one: `HarmonyRoster` takes the second from Harmony's own registry, the recorder captures it into `environment.mods.patch_roster`, and the preflight judges it beside the declaration rule rather than in place of it.
**A refused prerequisite is an errand or a statement, and `PreflightField.Outcome` is where that is decided once.**
Most are errands - `NotMet`, carrying `UnlockRemediation`, remediated by playing the game.
A native recording's `exact` unlock state names ids this build either ships or does not, and a build that does not is `Unavailable`, carrying `ContentNotShipped`: no play adds content a build has not got, so an errand there is an instruction that can never be carried out.
Every surface projects that outcome and none re-derives it - the command line's third mark, the eligibility screen's third row state and its headline without "yet" - and where the state could not be built at all, `LocalPrerequisites.UnlockStateShortfall` carries the engine's own reason beside the null `LockedActs` rather than leaving the absence to be read as a locked act.
Do not add a path that edits a save, a profile, an unlock, a build or a game mode.

**Read [docs/comparison-direction.md](docs/comparison-direction.md) before changing
what a verification report keeps, where a replay can start, or what the comparison
computes.** The supported boundary is combat start and the unit is the whole fight -
no turn-level reset, no pre-turn branching, no turn-level solver.
`VerificationReport.Trace` samples state either side of every action and computes
nothing; `CombatProjection` and `CombatComparison` do the deriving, over a fight that
finished and never over one still being fought. Do not collapse the trace into final
snapshots or prose, do not bake turn chronology into the summary, and do not add a
score, a ranking or a verdict about which line was better.
What the result *looks like* is presentation and stays out of that contract:
`FightResultScreen` and `FightResultChart` in `Sts2PilotTrainer.Trainer` derive the
drawn model, and `FightResultPanel` in the mod draws it. A value the projection cannot
derive honestly is a gap in a line, never a zero.
The result is drawn on request and never unbidden: after the fight the chip offers
`PostFightChoice`'s rows and the result surface is the first of them.
A run with a bound recorded line gets the comparison panel, where a lost line compares
(Lost against Won) rather than standing behind a notice; a native run has no such line
in this build and gets the plain no-line notice instead.
Which fights were shown this sitting is held in memory by `RecordedFightModule` for the
sitting and written nowhere.

**Where this is going, and what is runnable at each step, is
[docs/proof-of-concept-path.md](docs/proof-of-concept-path.md).** Read it before
planning work toward the first tryable proof; it names the remaining slices in
dependency order and what is deliberately not built.

**Some checks cannot be moved downstream.** A run resumed from run history matches on
seed, build, content hash and acts, and replays perfectly — it is simply not the run
its history describes. That is caught at ingestion, on the recording, or not at all.
Same for the end-of-run reading: one reading of the environment cannot catch its own
drift. Do not weaken `source.run_start` or `source.run_summary` on the grounds that
the replay would catch it, nor their native counterparts
`source.native.witnessed_run_start` and `source.native.continuity`, which answer the
same two questions for a recording made inside the player's own game.

**Read [docs/headless-fidelity.md](docs/headless-fidelity.md) before changing what
the host patches or stands in for.** Each patch has a stated reason and the set is
deliberately small. `TestMode` in particular reaches further than its name suggests:
at four sites it changes what the game *generates* rather than how it is drawn, and
`HeadlessPatches.RestoreRetailBranches` runs each of those with the flag off so the
engine's own retail branch decides. Adding a fifth is a sweep of the class, not a
guess - a gameplay path that consumes randomness only when test mode is off leaves a
run-persistent stream in the wrong place, silently, for the rest of the run.
Four screens have no engine command at all - the loot a won fight offers, the chest a
treasure room opens, the card screens a reward or an enchantment opens, and the Crystal
Sphere's own screen - so the host drives the first two and the last and answers the
third from the manifest. Three prompts the `ICardSelector` seam does not reach - the
bundle screen, the relic screen and the Crystal Sphere's screen - are stood in for at
the prompt itself by `ScreenStandIns`, headlessly only. None of them decides anything,
and each refuses where the manifest is silent.
The card screen an opening blessing opens is the one the retail client draws rather than answers: no selector is pushed there at all, the game puts up its own screen, and the recording's card is lit on it and pressed like every other decision before the fight.
Which screen that is belongs to the relic - a removal opens `NDeckCardSelectScreen` and a transform opens `NDeckTransformSelectScreen`, through different commands with differently named confirm buttons - so `RecordedCardScreen` is written to the `NCardGridSelectionScreen` base they share and never to one of them.
`RecordedCardScreen` is the one reader and presser of that screen, so the card the reveal lights is the card the commit presses, and the recorded `option_index` indexes the list the engine handed the screen rather than the sorted grid a player sees.
That makes the two hosts differ in whether the screen is drawn, so whether a recorded answer is a decision somebody watches is `RunDriver.ShowsTheAnswerBeingGiven` and nothing re-derives it - the counting, the captions and the reveal all ask it.
`docs/headless-fidelity.md` owns the mechanism.
Do not narrow the verbs one host issues, or the answers it draws a screen for, without holding them against a recorded walk to its first fight; `RetailPlayback.Verbs` owns what the running client issues, `RunDriver.AnswersShownOnTheGamesOwnScreen` owns what it draws, and `RecordedFightVerbAgreementTests` holds the committed fixture against both without the game installed.
The run library asks `RetailPlayback` about a boundary's prefix before it offers a player a place to stand, because a second copy of the verb set is how the library came to offer a floor the journey aborts on.
The driver still enforces the shared set, and `RetailPlayback` authorises nothing.

**Read [docs/in-game-host.md](docs/in-game-host.md) before touching anything that runs
inside the retail client.** `Sts2PilotTrainer.Mod` is the only project loaded into the
player's game; `EngineHost.Start` must never run there, and `AdoptRunningGame` is the
way in. Five traps in that process are written down there. Two cost a crash each: mod
initialization runs before the game has a model database, and Godot does not load the
game into the default assembly load context. Two are about *when* rather than what, and
were live in a build whose tests were green: waiting a length of time is not waiting for
the game to finish something, and returning to the main menu frees the popup that
explains why you returned. The fifth stops the mod loading at all, before a line of its
code runs: nothing about a type in `Runmobile` may require a sibling assembly in order
to *load* it. The game enumerates this assembly's types to find the mod initializer, one
phase before `SiblingAssemblies` has said where the siblings are, and loading a type
resolves its base class, the interfaces it implements and enough of its instance fields
to lay it out. Method bodies, method signatures, static fields and reference-typed
fields are resolved later and are fine. The shapes are not the rule and naming them is
how the rule was too narrow five times running - a generic built over a sibling type
has done it, a type implementing a sibling interface has, so has a *lambda*, because
a closure is a compiler-written class whose fields are whatever it captured, and so has
an *async method*, because its state machine is a struct whose fields are its awaiters
and a `Task<T>` over a sibling type is one. Run
`ModAssemblyLoadOrderTests` against a new type rather than judging its shape; it is the
only thing here that reproduces the game's own condition. It has fired six times, twice
through a green CI run. `DelegatingFightSampleSink` is how the mod reaches an interface
it may not implement.
`./scripts/install-mod.sh` is the one
script here that writes inside a Slay the Spire 2 installation.
Its final state is exactly `Runmobile` under the selected supported game mod directory (`mods` or `mods_STEAMTEST`); upgrades use temporary siblings there to replace the complete artifact without mixing versions, and remove the `CombatTrainer` directory the mod was installed under before the rename.

**The run library is the entry module, and browsing is after the fact.**
`RunLibraryModule` owns a row on the game's own main menu (`NMainMenu`), the Compendium button (`NCompendiumSubmenu`), the browser behind both, one run opened, and the plate under the game's own run history (`NMapPointHistoryEntry.Released`).
The main-menu row exists because the other two do not reach a brand-new player: the game sends a player with no finished run from Singleplayer straight to character select and hides its own Compendium from them at the menu and in the first run's pause menu alike, so the card and the plate hang off surfaces that player never sees.
`MainMenuRow.ShownWhen` is the one owner of whether the row is drawn - the player's stored `show_main_menu_row` where they have written one, and the game's own `Progress.NumberOfRuns` where they have not, read through `RunsFinished.Any` - so a new player gets the row, a player with run history does not, and a choice either of them makes on the settings page outranks the count from then on.
`MyRunsRow` states that same answer on the settings page from the same owner and `MainMenuLibraryRow` draws the row from it; a second derivation is what would let the control and the menu disagree.
An unreadable settings file says nothing rather than "off", because the row is a new player's only entrance.
That row is the one surface that must not adopt the running game where it is built: the main menu exists one startup phase before the game has a model database, and `EngineHost.AdoptRunningGame` latches its refusal for the process, so asking there costs the Compendium card and the recorder too - it asks at the press instead.
Two settled rules run through all of it and neither is a preference: a player plays *from* a run, one verb everywhere; and ordinary browsing hides a run this build has no passing verdict for, or an established multiplayer run.
`LibraryRun.Listed` is that rule in one place, the visible `Compatible with your game version` filter defaults on, and the numeral under the list counts what it hid.
Turning the filter off reveals incompatible runs as disabled rows, and an exact code does that automatically before selecting its run in the sorted position and naming both its required build and the current build.
The player-facing tabs are Community and Mine; `LibraryTab.Community` and `LibraryTab.MyRuns` are internal names only.
They are the game's own settings tab (`NSettingsTab`, the Statistics and Achievements tabs' scene) instantiated by `LibraryTabArt`, and Community wears the stats screen's own lock while `CommunityLock.For` says it is short of a sharing service or of the `Show community runs` setting; the lock hides nothing - the included runs and a run looked up by code are still listed under it, and its icon, its tooltip and one line over the tabs say what to do.
The strip's floor markers are the run-history screen's own icons through `FloorMarkerArt`, and `LibraryPaneArt.CellGeometry` is the one place a cell's icon, numeral, badge, ring and bookmark tab are placed so they cannot collide.
Every ribbon the library duplicates from the popup's own button carries its own copy of the two materials the retail button animates, through `LibraryRibbonArt.OwnMaterials`; shared, one hover lit every ribbon on the screen.
An online row's identity is its share id and code, while `RunId` remains the manifest's identity; two submissions of one run are two rows and each resolves through its own share.
Where a player can be stood is the recording's own `boundaries[]`, read through `RunView`, so no row can offer somewhere `RecordedFightEntry` would refuse; `RecordedFightRun.Start` takes the plan, because there is still one playback path.
A boundary existing and a boundary being reachable are two facts and a row needs both: `RunViewPosition.Reachable` asks `RetailPlayback.RouteTo` whether this client has a route there - walking the recording's own decisions, or restoring the game's own save from a floor arrival with a live fight - and `Playable` folds it in beside the run's start and an unfinished fight, so every surface that reads that rule - the play-from row, Continue, Start the run over, both strips and the run-history plate - refuses in the same words rather than constructing a run and aborting mid-journey.
On this build that leaves the first fight reachable by walking, every later fight whose arrival dealt it reachable by restoring, and every floor between fights refused by name, because reaching one means playing the fight before it.
`OwnRunPlaybackTests` drives every row the library offers on both committed native recordings to the engine's own entry, so the offer and the entry cannot disagree behind a green suite.
`RunProgress` under the store holds fight ordinals and nothing resumable - it is the pips and Continue's number, never a save.
What a player reads is `LibraryCopy`; what is drawn is `LibraryScreen`, and [docs/in-game-host.md](docs/in-game-host.md) owns the accepted parchment design it draws.

**The mod a player installs is `Runmobile`, and recorded fights, recording and the run library are its modules.**
`RunmobileMod` is the shell and `IRunmobileModule` the line between it and a feature; a module says whether it can run, installs its own patches and contributes its own surfaces, and one that refuses does not take the rest of the mod with it.
Whether this mod may draw anything at all is the shell's and never a feature's: `GameSessionWatch` observes a multiplayer game and `RunmobileMod.MayDraw` then says no to every surface, including a surface a module draws from its own Harmony patches.
A module asks that gate rather than reading the session for itself, because whether a surface may be put in front of a player is not a feature's decision, and a no draws nothing at all - not a greyed control, not a popup saying why, not an indicator saying a run is not being recorded.
Whether a run may be *recorded* is the other question and has the other answer: `RunRecorder.Attach` reads `GameSessionWatch.Observed` and asks `RunSession.MayBeRecorded` directly, which is the reading the recorder paragraph below requires.
`RunmobileStore` is the only thing in the mod that writes, under `user://Runmobile/` scoped by the game's own resolved platform, account and profile - taken whole from `UserDataPathProvider`, never reassembled here, and never part of an exported recording's identity.
`ProfileWriteBarrier` is a different thing and stays as it is: it suppresses the game's own writes during a trainer run.
`./scripts/protected-files.sh` is how "nothing outside that subtree changed" is measured rather than asserted.
Removing is a write and goes through the same gate - `RunmobileStore.Remove` names one file and refuses a directory, while `RemoveTree` is reserved for the derived trees a subprocess wrote - the publication workspace, a snapshot materialisation's work directory and the snapshot cache - and *which* files is `RecordingRetention`'s, so the one operation that cannot be undone does not also pick its own targets.
The one recording it never names, under any policy, is the run the game can currently Continue: a journal deleted under a live run is one the recorder picks up again and publishes as a run it watched from the start.
The player's `settings.json` says how many runs to keep and can ask for them all to be removed; both are `RecordingLibrary.Cull` with a different number, applied at the shell's singleplayer-menu patch and again at `RunmobileMod.EnsureAdopted`, because those are the first moment there is a profile and the last moment before any journal is open, once for each save profile the process plays as rather than once for the process.
The retention policy, removal request, index-fetch choice and main-menu row have controls, and they are one row rather than a section: `MyRunsRow` in `Sts2PilotTrainer.Trainer` derives every line the way `PlaybackTransport.For` derives the transport, `MyRunsSettingsRow` draws it, `MyRunsSettings` wires it to the disk, and the run library's own module places it beside the game's modding settings entry point.
`MyRunsSettingsRow` scopes its controls under one left-aligned Runmobile heading and borrows the native action and tickbox images through `MyRunsSettingsArt`; `docs/in-game-host.md` owns the presentation and its limits.
Adding one to that column also leaves the screen's scroll extent short until the panel that owns it is asked to measure again, which `MyRunsSettings.RefreshExtent` does through the panel's own command; [docs/in-game-host.md](docs/in-game-host.md) owns why.
`sharing_service_url` in that profile-scoped file is the only authority for an outbound sharing request; there is no built-in endpoint, and without a valid absolute HTTPS value the UI says sharing is unavailable and sends nothing.
Its size figure is a sum of `RunmobileStore.SizeOf` - a read through the same containment gate a write goes through - taken by `RecordingRetention.OnDisk`, because that is already the one place that knows where recordings live; pressing Remove is `RecordingRetention.PurgeNow`, which records the request before it removes anything and is not the once-per-profile policy.
Moving the policy removes nothing where it stands and forgets that profile's retention latch through `RecordingRetention.ReapplyPolicyAtNextMenu`, so the next main menu applies what the row promised rather than the next launch; a store that cannot be asked is a fact on the facts and one refused line, never an exception out of `MyRunsSettings.Build`, and the only cause that line may name is the store's own `StoreNotReadyException` - no save profile chosen - because every other fault is one the row has not established.
What the second line promises is what `Apply` will do and not what the arithmetic says, so `OnDisk` asks `ContinuableRun` the way `Apply` asks it and the row leaves the continuable run out of the count.
Whether the policy on it is the player's own sentence or the default shown in place of a file this build could not read travels with it as a fact, because a row that showed the unreadable-file sentinel would be stating a number nobody wrote, and one that claimed the usual policy stood would be stating a rule that is not in force - under an unreadable file nothing is removed of this mod's own accord, and the row says that instead.
`RunmobileSettings.Set` refuses such a file through `Read` rather than through rules of its own, and every control that would write into it is refused with it, the removal included, because the purge records its request there before it takes anything.
The singleplayer-menu patch asks for retention first and unconditionally, before any module surface can draw, and attempts adoption only where a module needs it - so a build on which every module declines never reaches adoption from there and still honours a purge, as does one the engine layer refuses to adopt, in every profile the player switches to.
Retention's own condition is a chosen save profile and never the adoption verdict.
Do not add a second writer, a second path rule, a second account-identity mechanism or a second thing that deletes; [docs/in-game-host.md](docs/in-game-host.md) owns the detail.

**The per-verb format reference is generated, never written.**
[docs/manifest-format.md](docs/manifest-format.md) is produced by `./scripts/format-reference.sh`
from `ManifestValidator`'s per-verb argument rules and `EngineCommands`' table, and CI
fails when the committed copy is not what those two declarations produce. Regenerate it
in the change that moved either one; editing it by hand only moves that failure onto
somebody else's branch. Both are read as source rather than loaded, because the
engine-command table needs the game to load and the check runs where there is none. It
is an internal engineering reference and nothing a player sees is written from it.

**Read [docs/ingestion.md](docs/ingestion.md) before touching how a recording is found or
dated.** Screening runs on free metadata and establishes nothing: a seed it recovers is a
candidate for `verify-seed` and a build it dates is a candidate for `preflight`, and the engine
settles both. Creator eligibility - the seed as text, or the overlay unoccluded - is the first
gate and runs before anything is fetched. `Revalidation` answers whether an existing
reconstruction still reproduces on another build, as a verdict keyed to (recording, build);
the manifest records what the recording was made on and is never edited to match a build under
test. That document also names three limits this path does not remove.

**Standing somebody in a recorded fight has one owner.**
`RecordedFightEntry` constructs the run, makes the recording's decisions in order and refuses a boundary that is not the recorded one; the mod owns retail timing, presentation, deviation locks and write isolation.
It has two ways in and one proof: `StartHeadless` walks the decisions, `RestoreHeadless` continues the run from a verified floor-entry snapshot at any boundary on the way and walks whatever is left, and both end at the same `VerifyBoundary`; `PrepareInRunningGame` and `PrepareRestoreInRunningGame` are their in-client siblings, through the same `Prepare`. Headlessly, restoring is an optimisation a consumer opts into with `enter-fight --restore`, which replays instead whenever the cache is absent, of another history or of another build; in the client it is the only way past a fight, and the route is `RetailPlayback.RouteTo`'s.
The watched journey is one long-lived transport and not a popup per step: `PlaybackTransport` in `Sts2PilotTrainer.Trainer` owns what it says, `PlaybackTransportStrip` draws it, `PlaybackTransportDock` parents it to the run's own persistent interface so it survives the map-to-combat transition, and `RecordedFightReveal` lights the game's own selected state without clicking.
Do not add a second playback path beside it; `docs/in-game-host.md` owns why.
A screen the recording still has to answer is a screen the run is still inside, and the journey presses past nothing while `RecordedFightEntry.NextStepAnswersAScreenAlreadyOpened` - the event underneath an open card screen is mid-transition, and its proceed would dismiss a decision that has not been made.
Whether a step is one to be shown depends on the host, and on one thing only: whether the screen it happens on is drawn.
A card selection is the recording's answer to a screen an earlier decision opened, so headlessly the engine takes it inside the call that opened it and there is nothing on screen to point at by the time its step runs - it is executed without a reveal, a hold or a number.
In the retail client that screen is the game's own and is drawn, so the same step is a decision lit and pressed like any other.
`CardScreenAnswers.IsAnAnswer` says which steps are answers, `RunDriver.ShowsTheAnswerBeingGiven` says whether this host draws the screen for one, and `RecordedFightEntry.Decisions` is what a counter counts; nothing else re-derives either question.
**What the transport *is* at any moment is derived in one place and never built by hand.**
`PlaybackTransport.For(phase, facts)` is total and pure - every phase has an answer, null included for the two that draw nothing - and the five shapes behind it are private, so there is no way round it; `TransportSurface` answers present, drawn and pressable separately for every element and the strip projects that table without reading the mode; `RecordedFightRun.Transition` is the only thing that changes the phase and it re-derives, as does every fact that can change under it.
Four defects came from the one boolean this replaced. `docs/mod-ui-direction.md` owns the table and the rule.
What those surfaces look like is settled and is `docs/mod-ui-direction.md`: a hanging tag under the game's own meta cluster, icon-only controls with tooltips, the mod's own drawn glyph family where a filled shape moves the run and a hollow one only looks.
A redesign changes what `PlaybackTransportStrip` draws and nothing else.
Keeping construction in the engine owner lets `./scripts/arbiter enter-fight` exercise the journey without a scene tree.
The run is generated against a supplied complete unlock state and can persist nothing:
`shouldSave: false` plus the mod's `ProfileWriteBarrier`, which is installed at mod
start and inert unless a trainer run is live. Do not weaken either, and do not add a
path that writes what the barrier suppresses.

**No surface in this mod writes down or derives a font or font size.**
`GameText` and `GameTextStyle` in `Sts2PilotTrainer.Mod` copy both from the native element whose role each Runmobile element fills, and missing native furniture refuses the surface rather than substituting a default.
The one surface that says its line at Godot's own size instead of refusing is the restoring notice, because refusing there is the blank screen it exists to remove; `docs/mod-ui-direction.md` owns that exception and it is the only one.
Which native element each role asks is a judgement per element and `docs/mod-ui-direction.md` owns it, along with the two surfaces that scale their whole geometry by the reading.
A number here is a number that was right on one window and one build: the game carries no project theme, so nothing inherits, and the sizes it draws its own elements at are the only ones that read as native.
A surface that sits inside one of the game's own containers is a child of it and asks it for height only, laid out again once that container has sorted - a settings screen has not been laid out when its `_Ready` runs, and a child's minimum width is a demand its host obeys.
`demo/RUNMOBILE-NATIVE-TYPE.md` records the current retail evidence and the surfaces that still require an in-client capture.
**A role names a node in one of the game's own scenes, and a role this build has not got is named in the log before the surface that asks for it refuses.**
`GameText.Verify` resolves every role once, from `RunmobileMod.EnsureAdopted` - not from the mod initializer, which reads nothing because a scene is the game - and names each role this build cannot answer in the game's log.
It exists because a single wrong node path was noticed nowhere until the surface asking for it was drawn: v0.111.0's ledger row pointed one container too high and abandoned every recorded fight a player entered, with a message about text.
The refusal itself is the rule above unchanged - nothing is substituted for missing native furniture.
Godot loads no resources under `dotnet test`, so a path is checked by reading the shipped pack itself: `NativeTextRoleTests` is what holds the role table to this build, and the library's borrowed art is held to the scenes that draw it the same way, which `docs/mod-ui-direction.md` owns.
`NativeScenes.Decide` owns whether it can - it runs where the installed pack is the build `build/lib` was copied from, and otherwise skips saying which of not-prepared, another build or no pack it was, because a green skip over an unchecked table is how this defect shipped and a red would blame the table for a Steam update.

**A run a person plays is recorded by one owner, and refused rather than repaired.**
`RunCapture` in `Sts2PilotTrainer.Replay` is the whole-run counterpart of `FightCapture` and delegates the inside of each fight to one, so there is one capture path.
It records singleplayer runs only, and which kind of run this is is read - `LiveRun.ReadSession` off the game's own networking and player list, with `RunSession` owning what each `RunSessionKind` permits - never inferred from the name of the setup member the game called.
A run the console was used in is kept whole, recorded to its end, and marked `source.native.integrity = "non-standard"` through `RunCapture.MarkNonStandard`, which the validator refuses for publication; `integrity` is the one field that says this.
`RunRecorder` in the mod owns only what a pure class cannot: which game member is which decision, what its arguments are, and when the engine has settled enough to read.
Inside a fight it hands over to the same `PlayerFightObserver` the recorded-fight journey uses, through `IFightSampleSink`.
The recorder refuses a run whose start it did not witness and marks `continuity = broken` when a resumed session's live state is not the state its journal last recorded, except for the game's observed rollback of a live fight to that fight's room-entry boundary.
That rollback stays continuous, keeps the unwound fight decisions as discarded evidence in the journal and manifest, and resumes the replayable history at the boundary; no other mismatch is repaired.
**A hole in the account of a run and a recorder that has stopped watching it are two things, and `RunRefusal` is where a refusal says which it is.**
A resume the recorder can place in its own history is a reload that rewound the run behind what it had recorded - a save-scum - and it costs the recording its sharing and the watch nothing: the decisions the reload abandoned go where the game's own rollback puts an unwound fight, a discarded branch marked `reload` from the decision the game came back to, the replayable history resumes there, and the recorder goes on recording.
That recording is `continuity = rewound`: whole, so the validator takes it and the run is the player's to play from, and never theirs to share, which the run-history plate's and the Mine pane's submit rows, `RunSharing`'s seal and the gate's `continuity` condition all refuse - the validator cannot, because it is what `RecordedFightEntry` asks before it stands anybody in a fight.
A hole outranks a rewind: a reload the recorder can place after a resume it could not leaves the recording `broken`.
A resume it cannot place stops the watch, because nothing establishes what the run is from there.
The class is on the journal line as `watch_continues` rather than derived from the sentence, so a session after this one keeps the disposition rather than reading the words and guessing; a line without it is a refusal that stopped the watch, which is every refusal an older journal holds.
`source.native.integrity` is the one field that says whether a recording may ever be published, and it is required from format v6: `complete`, `non-standard` for a run the console was used in, or `unmapped` for a recorder that stopped at a decision it could not name, with what it met in `source.native.unmapped` - `RunCapture.MarkNonStandard` and `RunCapture.MarkUnmapped` are the only writers.
A version-5 file states none, and `ManifestJson.MigrateFromVersion5` reads it as `complete` with `migrated_from_version = 5` beside it; that note is what excuses the `option_key` a version-5 recorder never read, and nothing else is ever excused by it.
A version-5 recorder sampled no map coordinate either, so the same reading derives the arrival checkpoint at every floor arrival the file declares, through `FloorArrival` and marked inferred, because the validator refuses a floor arrival nothing proves and a player's own recording in the store is the file the recorder wrote; `OwnRunPlaybackTests` drives the library's offer from that on-disk form as well as from the migrated copy.
Every decision in a journal carries the reading it began from as well as the one it settled into, because the comparison instant is before each decision; `RunJournal.Schema` is v3 - v2 added the before-reading, v3 the bookmark line - a v2 journal is read as-is because every v2 line is a v3 line, and a v1 journal is refused on resume rather than repaired.
**Every integrity claim it makes is derived from what was observed, never from what a component assumes it observed.**
Continuity, a witnessed start, the mod set a run was played under, the controls a verdict rests on: each of these is a claim about what happened, and a component that reports one it did not establish produces evidence nobody can check while every value in it is individually true.
Four such claims were shipped on this branch and caught in review; [docs/in-game-host.md](docs/in-game-host.md) names them, so the rule is checkable rather than an abstraction.
It writes an append-only `RunJournal` after every decision so a crash keeps the prefix, never raises `ProfileWriteBarrier` - the player's own run saves normally - and never writes a Steam id, a machine path, a profile id or hardware.
Every write goes through `RunmobileStore`. [docs/in-game-host.md](docs/in-game-host.md) owns the detail.
The recorder's one persistent surface is a row of the game's own version overlay rather than a plate or a chip: `RecorderPresence.For` in `Sts2PilotTrainer.Trainer` derives it from exactly `RunRecorder.Active` and `RunCapture.State`, and `RecorderPresenceRow` in the mod draws it as a sibling label under the overlay's own MODDED row, installed apart from the watch above so a renamed overlay costs the row rather than the recorder. [docs/in-game-host.md](docs/in-game-host.md) owns the detail.
**A bookmark is a declared fact in the recording, pressed at the moment a fight ends, and nothing else writes one.**
`source.native.bookmarks` is one optional list keyed by fight ordinal, written by `RunCapture.MarkBookmark` through a journal line per press so a crash keeps whatever was pressed, and emitted by `ToManifest` for the marks that are on; the validator holds every entry to a fight with a `combat_start`, declared, in order, pressed at or after that fight and never past the history's end.
The control is `FightMark.For` in `Sts2PilotTrainer.Trainer`, total and pure over `RunRecorder.FightMarkFacts` - a recorder attached, `RunCapture.State`, `RunCapture.LastEndedFight`, `MovedOnFromLastFight`, and whether that fight is marked, so a recording that stopped offers no tag and one recorded to its end still does - and `FightMarkTag` draws it under the top bar on the transport's own anchor, from a fight's last action to the map move that leaves its floor, which covers the loot screen, the card screen behind it and the game's death screen with one derivation.
On a loss the manifest is on disk before that screen is drawn, so `RunRecorder.Bookmark` writes it again through the same path and gate `Finish` used, and that rewrite changes nothing but the field.
A lost run is finished by `RunRecorder.End` only once the engine has ended the fight it was lost in: the game calls `RunManager.OnEnded` from inside the killing enemy turn and raises `CombatEnded` afterwards, so a recording finished at the first left the killing turn open and the lost fight without an end, a boundary or a line, and the death screen with nothing to offer; `RunTornDown` is the safety net that finishes it anyway.
The strip draws the mark as the gold tab off a cell's top-left corner - `RunStripCell.Bookmarked`, read once in `RunView.PositionsIn` from the recording, so the opened run and both browser tabs agree - opposite the teal played tick, because a bookmark is about the run and the tick is about the person; the whole-run save the browser design still owes takes a keep glyph, never the bookmark and never a star.
Combats only, in the moment only; a bookmark added later from the library would be the one edit of a finished recording the mod makes outside the recorder, and is deferred rather than built.

**A fight a person plays is captured, never re-read.**
`FightCapture` in `Sts2PilotTrainer.Replay` is the one owner of turning what the game's own action executor announces into the same `ReplayTrace` the headless arbiter produces; `PlayerFightObserver` in the mod only decides when a sample is taken.
A projection is handed over only once the fight ended inside a sampled action, and a gap between two samples is refused rather than bridged.
The recording's side of the in-game comparison is `manifests/<id>.recorded-fights.json`, produced by `./scripts/arbiter recorded-fight` from a fresh replay and bound to the manifest per fight by run id, history hash and the combat-start boundary of the same ordinal; regenerate it in the same change that edits the manifest's fights.
Do not add a second capture path, a turn-level reset, a score or a verdict; `docs/comparison-direction.md` owns why.

**One version, written in one place.** `src/Sts2PilotTrainer.Mod/Runmobile.json`'s
`version` field is the only version declaration for the assemblies the mod ships;
`Directory.Build.props` reads it and stamps every assembly, `RunmobileVersion` is the
one reader, and a native recording names that build twice - in its mod set and as
`source.native.recorder_version` - so the two have to be the same string. Do not add a
second declaration; recordings written before this carry the old wrong value and are
not edited to match.
Two versions sit outside that on purpose. `Arbiter.Version` is the packaged headless CLI's own,
a separate executable inside the mod archive, so `arbiter_version` in every
verification report is bumped deliberately rather than following a mod-only release -
after a mod bump it still reads the arbiter's version, which is the point.
`GodotStubs` has to keep `GodotSharp`'s identity for the game assembly's references to
resolve. [docs/distribution.md](docs/distribution.md) owns the detail.

**Player-facing wording is a template, never a recording.** Everything the mod says
lives in `Sts2PilotTrainer.Trainer`, and every recording-specific value in it is
interpolated - the credit from `RecordingIdentity`, the blessing and the node
from the run the decision is about to act on.
Two recordings are credited and `RecordingIdentity.Credit` is the one reader: a reconstruction from a video is credited to `source.video.channel_name`, a native run the library knows is the player's own is credited to them, and a native run received from somebody else is credited neutrally as "This run" because the recording stores no identity.
That credit is a `RecordingCredit` rather than a name because the same slot is a sentence subject in one caption and a possessive in the next, and one string forced into both reads "Watch You's fight"; a manifest that is neither is still refused rather than attributed.
A sentence that names NaveGreed, the
Underdocks or a Sludge Spinner is a bug; the one remaining exception is named in
`TrainerCopy.FightFloor` and `TrainerCopy.FightEnemy`, with the manifest fields they
are waiting on.

## Maintaining this file

Keep this short and true. Record only what almost every future session needs: how to
build and test, the invariants above, and where the real explanations live. Prefer a
pointer to the authoritative file over a copy of its contents — a duplicated
explanation is one that will drift. When something here stops being true, fix it in
the same change that made it untrue.

<!-- CLAUDE.md imports this file. Edit AGENTS.md, never CLAUDE.md. -->

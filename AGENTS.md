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
./scripts/retail-client.sh launch|status|release   # the retail client: --force-steam=off, one owner record, TERM and wait; never through Steam
./scripts/retail-soak.sh --runs 6 --stop-after-minutes 360 --client-id 2   # a night of the retail soak, headless, and the third release figure over what it recorded
./scripts/protected-files.sh snapshot <ledger>   # hash everything the mod must not change
./scripts/protected-files.sh compare  <ledger>   # ... and say what a session changed
./scripts/build.sh && ./scripts/fetch-baselib-parity.sh && ./scripts/test-session.sh   # the suite, with one verdict
./scripts/arbiter gate manifests/navegreed-OJ-6QXhNgdg.replay.json   # the whole standard, one verdict
./scripts/arbiter parity --corpus manifests   # the recorder's standard: every journalled recording replays decision for decision
./scripts/arbiter coverage --corpus manifests # ... and every decision point the build offers is reached or excused (--update to re-record the denominator and the producer map)
./scripts/arbiter engine-commands --update  # re-record scripts/decision-ledger.txt: every way a decision reaches the game, claimed by a row or excused
./scripts/arbiter <command> # gate | parity | coverage | validate | preflight | preflight-live | adopt-live |
                            # verify-seed | replay | determinism | negative-controls |
                            # combat-snapshot | floor-snapshot | combat-compare |
                            # enter-fight | recorded-fight |
                            # snapshot-restore-probe | migrate-manifest | engine-commands
./scripts/bootstrap.sh --archive build/archive   # keep the receipted prepared set under its build
Read [docs/build-adoption.md](docs/build-adoption.md) before adopting a game build.
./scripts/assert-expected-skips.sh          # what CI skips is still what we recorded (--update to re-record)
./scripts/format-reference.sh               # rewrite docs/manifest-format.md from the code (--check to compare)
./scripts/choice-entry-points.sh            # which game types reach each card or relic prompt, and which bodies that scan cannot read, are still what we recorded (--update to re-record both)
./scripts/save-points.sh                    # which SaveManager.SaveRun overloads exist, who calls each, and which types subclass AncientEventModel are still what we recorded (--update to re-record)
./scripts/act-topology.sh                   # which acts ship at each index, which is the default and what unlocks each are still what we recorded (--update to re-record)
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

**`dotnet format` needs `./scripts/bootstrap.sh` first too, for the same reason as `dotnet test`.**
Run bare against a tree with no `build/lib`, it silently analyses a broken
compilation - Engine, Mod and Cli fail to resolve the game's own types - and
reports hundreds of spurious errors that look like formatting violations but
are not.
`.no-mistakes.yaml`'s `format` command already bootstraps first; run it that
way; a bare `dotnet format sts2-pilot-trainer.sln --verify-no-changes` is not
a valid reproduction of a formatting failure.

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
onto a method the retail client calls; none of them reimplement game logic. Whether a
play took effect is read off the game's own combat history (`CombatHistory.CardPlaysStarted`,
written by `CardModel.OnPlayWrapper` as the play begins) and never off where the card
ended up: Particle Wall and a 0-cost attack under Feral go back to the hand after a
play the engine executed in full, and a driver that read the hand refused both
(`PlayReturnedToHandTests`). Reading
`build/lib/sts2.dll` is how that is established — decompile it into a scratch
directory outside the repository, never into it, and never commit anything you find
there. `ilspycmd -p -o <scratch> build/lib/sts2.dll` does the job in about twenty
seconds; on a Homebrew .NET it needs `DOTNET_ROOT` set to the `libexec` directory and
`DOTNET_ROLL_FORWARD=Major`.
The rule holds for what a verb checks as well as for what it calls: where the run may move next is `MapTravelRule.TravelableFrom`, the game's own `MapTravel.GetTravelablePointsFrom` under the map screen's two boss lines, read by the driver's map move and the recorder's negative-control nomination alike, because a driver that read the node's children refused a Winged Boots flight the game allows.
Which reward of a loot screen a decision took is `reward_index`, the reward's position in the set the engine holds and the game's own `RewardSelectedMessage.rewardIndex`, written by the recorder on every loot-screen decision and read by the driver where a recording carries it; a set that offers two of a kind - a relic's second gold or second card reward - is claimed by it, and a recording without it is refused there rather than guessed, since which one was taken is not recorded.
`ReplayRefusalRegressionTests` holds both paths as recordings played through the recorder and replayed to parity.
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

**The recorder's release bar is two numbers `./scripts/arbiter` computes over a corpus, and `parity` is the first; [docs/release-bar.md](docs/release-bar.md) owns the procedure, the release statement and the measurements.**
Per recording and per decision: a fresh replay of the manifest has to reproduce the `.journal.jsonl` the recorder wrote beside it - the same decision at the same place, the sampled state it began from and settled into through `ReplayTrace.SameSample`, and the complete digest of each reading where both sides carry one - and the first decision where it does not is named with the field, or as hidden state where every sampled field agrees and only the digest differs.
`TraceParity.Compare` in `Sts2PilotTrainer.Replay` is the one oracle: the CLI's `parity` holds a journal to `Arbiter.Run`'s trace through it and `HeadlessGameplayCaptureTests` holds the recorder's own headless capture to an in-process replay through the same call, so the two cannot drift.
`ReplayStep` carries `before_digest` and `after_digest` for that comparison, `RunJournalEntry.AsStep` is the one conversion from a journal line to a step, and `RunJournal.Trace` is the continued history as the trace a replay is held to.
Two digests are not held, each for the reason the recorder's own resume already carries, and nothing else is excused: the reading a fight-ending decision settled into, because the retail client rolls the rewards after the engine has settled and the next decision's before-reading is where both hosts read the same state (`ReplayStep.EndsAFight`, the rule `RunCapture.MatchesTheRestoredLootScreen` reads); and the opening reading, which is not a decision's reading and which the retail client's first room changes before the first decision - its random streams, and on an ascension that starts the run damaged the player's health - reported beside the verdict as `opening reading`, fields and digest alike, never folded into it; what that room did not change is held at the first decision's own before-reading.
The corpus is a directory of `*.replay.json` with each `.journal.jsonl` beside it where one is on hand, read by `RecordingCorpus.Enumerate` and nothing else: `manifests/` is the committed one, and a player's store is a copy the person takes and names with `--corpus`, never a path the CLI derives.
A recording without a journal, with one in a schema this build does not read, or whose `continuity` the recorder marked `broken`, holds nothing and is counted in the printed denominator so the figure cannot read as whole; the two committed native recordings carry none, the journals the recorder wrote for them being schema v1, and `ParityTests` holds the command to saying so over `manifests/`.
A journal in this build's own schema that cannot be parsed, a journal of another run, and a replay the engine refused each fail the figure as `REFUSED`; `UnreadableJournalSchemaException` is how `RunJournal.Parse` tells the first apart from an older schema.
The store corpus is release evidence and never part of the merge gate.
**`coverage` is the second number: for every decision point this build can offer, how many recordings in a corpus exercise it, with a denominator walked off the game assembly and written by nobody.**
A point is a `DecisionPoint` - a kind from `DecisionKinds` and an identity - and the vocabulary lives in `Sts2PilotTrainer.Replay` because both sides answer in it: `DecisionSurface` in `Sts2PilotTrainer.Engine` walks what the build offers, one walk per kind reading the game the way the recorder or the driver reads that thing (a reward class through `LootRewards.KindOf`, a shelf the way the purchase patch maps a merchant entry, an alternative through the literal `CardRewardAlternative.Generate` and Pael's Wing construct one with, a rest option through its `OptionId` constant, an event, the ancients an act rolls one of included, Neow left out by name because its decision is `ChooseNeowBlessing` and the Architect added by name because the victory room is an event room over it that no act lists, through the model database, a prompt through `ChoiceEntryPoints`, a net action through its own `ToGameAction`), and `DecisionFacts.Of` projects what a recording exercised off its manifest's own arguments, with no game.
`ChoiceEntryPoints` and `SavePoints` live in the engine for that reason, and `EntryPointSignature.Of` is the one spelling of an entry point the recorder's forwarder table, the walks and the coverage number share; the three committed records they produce did not change by a byte in the move.
The IL is read one way, by reflection over the loaded assembly against the vendored stubs, and a body the stubs cannot resolve refuses by name rather than thinning a count; a second IL reader is a second mechanism and is not added.
Two kinds cannot be projected from this format and are listed as such, counted neither way: a card prompt, because a card selection records the position picked in the list the prompt offered and not which entry point asked (the journal is the right place for the entry point, a v7 schema still to come), and a net action, because which game action a decision went through is the claims table's knowledge, which this build does not carry.
**The map is seam-centric, because every replay refusal this project has recorded was a seam and never a card effect.**
An event's option is a point of its own (`event-option`, projected off `option_key` under the event's id and under `EVENT.NEOW` for a blessing): an ancient's options are the game's own `AllPossibleOptions` keyed the way `RunDriver.OptionKey` keys one, every other event's are read off its IL and every `EventOption` it constructs is held to a whole key literal on the way to it, a bare literal it is constructed with (`PROCEED`) is read off the construction, a construction more than one string could be the key of - two literals with no call between, a literal beside an interpolation, a string read from a field or a slot no literal was stored in - is refused by name in `ChoiceEntryPoints.ConstructionsIn` rather than read as the last one, because reading one would list one option where the body offers two, and an event that builds a key at runtime is derived in `DecisionSurface.RuntimeBuiltOptionKeys` the way its own code builds it or refused by name - a bound its code compares a counter against is read off the constant the branch loads (`ChoiceEntryPoints.ConstantsComparedWith`, never a number written here), every option keyed by an interpolation is read as a template (`ChoiceEntryPoints.Construction.Template`) that some derived key has to fit, and where every built key of an event is such a template every derived key has to fit one, so a build that moves a bound or renames a page fails the walk by name rather than listing another build's keys.
A seam at a timing class is a point too (`seam`): `DecisionSurface.ProducerMap` walks every model the database registers from each hook it overrides, through its own code and the game members it calls to the walk's depth, to the first seam - a prompt entry point, a screen, a reward set offered, a reward kind, rest option, shop shelf, event option or alternative constructed - and lists the producers under `seam @ hook`, with `DealtBy(relic)` from rarity and every option that offers it and `ReachableIn(act)` from the act models; `scripts/producer-map.txt` is that map on this build, regenerated by the same `--update` and held by `DecisionSurfaceTests`, kept apart from the coverage record because a claim about code and a count over recordings are different facts.
A seam is reached by co-occurrence - a crediting recording that met one of its producers (`DecisionFacts.ModelsMet`, every model id in its samples and arguments) and answered one of the points the seam is answered at - and is printed as `co-occurrence`, never as covered, because the format does not say which producer opened the decision.
`DecisionExcusals` is the written excuse for every point no committed recording reaches, code rather than a data file so a build is held to it and a reviewer sees an excusal appear in a diff; an excusal naming a point no walk produces is stale and fails the bar on any corpus, one a crediting recording of the corpus under test reached is named as `excused and reached by this corpus` - over `manifests/` a sentence gone false, which `CoverageTests` holds to none, over a store copy progress - and an excusal by rule rather than by id is what would let a game update add a point nobody counted.
Every excusal carries an `ExcusalClass` beside its prose, and `DecisionSurface.AdmissibleExcusals` says which the map admits per point: eight are derived - no producer on this build (nothing produces the seam and no engine path constructs the thing), multiplayer only (constructed under the game's own player-count branch, or a verb the client offers only while another player has not acted, `RunDriver.OfferedOnlyWithAnotherPlayer`), a screen the headless host has none for (`ScreenStandIns.StoodInFor`, for a verb and for a seam every answer of which is such a verb), retail-only timing (derived for nothing on this build; the undo it was thought to class is offered to another player), not replayable (no recording of it can replay on this build: an alternative whose after-action keeps the selection open, by `ManifestCardSelector.EndsTheSelection`, or an event option keyed by a LocString's raw text, which the recorder writes as a localized title the driver never matches, by `DecisionSurface.TitleKeyedConstructions`), reached by the win (the Architect, its options and the seams it alone produces), not choosable (an event option constructed locked, with no work, where the player cannot afford the real one, which its own button refuses to press, by `DecisionSurface.NotChoosableOptions`), offered only to another character (an event option constructed under the event's own guard on the owner's card pool, Colorful Philosophers' five, by `DecisionSurface.OptionsWithheldFromTheCharacter`, which names the character each is withheld from) - `generated` is admissible everywhere because a merge-gate row outranks any reason, and `not-on-the-route` only where nothing is derived; `coverage` fails an excusal in a class the map does not admit the way it fails a stale one, and an excusal whose prose names who produces the point carries that producer's id beside it (`Excusal.Producers`), held by `coverage` to the producers the map lists for the point (`DecisionCoverage.ProducersListedAt`) and printed as a misnamed producer where the map lists it nowhere there, because a build that moves a producer leaves the class true and the sentence false.
The two numbers are computed over one corpus and treat a recording the same way, and `RecordingStanding.Of` in `Sts2PilotTrainer.Replay` is the one reader both ask, of every recording, against the build under test: a recording made on another build - by `EnvironmentPreflight.Build`, the replay preflight's own `build_version`, `build_date_utc` and `content_hash` rule, refused in the preflight's own sentence - or whose `integrity` is not `complete` or whose `continuity` is `broken` holds nothing for `parity` and credits nothing to `coverage` - what it reached is projected all the same and tallied apart as reached and unverified, printed beside the row and never folded into its state, so a point only such a recording reaches stays uncovered or excused and an excusal it reached is not one it retires.
The one standing the two numbers read differently is the recorder's: a native recording whose `source.native.recorder_version` is below the recorder this build carries, or names none this build reads - missing, unparseable, or the unstamped `1.0.0.0` a recorder built before the version was stamped named itself with - is `OlderRecorder`, held to nothing by `parity` and counted in its denominator the way an older-schema journal is, because its journal is what that recorder got wrong, and still credited by `coverage` through `RecordingStanding.CreditsCoverage`, because its manifest replays on this build and a replayed history witnesses every point on it; the recorder's version is `RunmobileVersion.Current`, passed in by the command beside the build so the standing stays a pure function, and the two committed natives are such recordings.
The build under test is `GameIdentity.Read().Build`, read once per command and written into each artifact's header, because `Sts2PilotTrainer.Replay` stays game-free and a store spans builds once the game updates: before the standing asked it, every recording made before an update kept its coverage credit while `replay` refused it at preflight, so the release measurement over a player's store could read covered on another build's evidence; `CoverageTests` and `ParityTests` each hold a committed manifest relabelled to another build in a scratch corpus to crediting nothing and being named as another build's, never replayed and failing the parity bar as the replay's refusal always did, because a recording this build cannot replay is unproven on it.
A manifest this build cannot read is read through `RecordingCorpus.Read`, the one place both commands take the parser's words from, and is named in the totals and the artifact with the rest of the corpus still counted; neither number aborts on one file.
A decision on a discarded branch (`source.native.discarded[].actions`) is projected beside the continued history, because a point the player reached and chose is exercised whether or not the game's own rollback undid it.
The bar the command exits on is that nothing is uncovered; `scripts/decision-coverage.txt` is the denominator with its excusals on this build, regenerated by `./scripts/arbiter coverage --corpus manifests --update` - written at the worktree root wherever the command is run from - and held by `DecisionSurfaceTests`, and `CoverageTests` holds the command over `manifests/` and over a corpus holding a broken recording and an unreadable manifest.
An excusal's "no committed recording reaches it" is a placeholder the retail soak retires, and is not an acceptable excuse at release for a point a player can reach on this build; an excusal that names `GeneratedCoverageTests` is held by it: `SyntheticFixtureGenerator.WalkTheAct` takes a `WalkPolicy` that points the whole-act journey at one decision - decline a card reward on its own screen, a named rest option, a named shop shelf, a potion drunk or discarded on the map, the chest taken or skipped, an elite's relic reward claimed - and each row plays that walk through the real recorder, replays what it wrote, holds the replay to parity through `TraceParity` and asserts the recording projects to the point, on every merge; a walk stops at the end of the floor its ask was met on, because the journey's survival rules were tuned for its own choices.
A policy can name a relic as well as a decision - `NeowRelic`, the blessing that grants it, `BagRelic`, the relic the run's own bag deals at the chest, the merchant's shelf or an elite's loot, or `AncientRelic`, the relic an act's ancient offers - and the producer rows of the same class are one per relic the map says Neow, a chest or a shop deals, each on a seed hunted so the run deals it (`SeedHunt`, whose reading is the opening event's offer and each end of each rarity's bag, both fixed at the run's start), obtaining the relic through the recorded decision that deals it, answering what it adds, and asserting the recording reaches every seam the map lists the relic under; the table is held to the map's own producers, less Massive Scroll, whose `IsAllowed` admits only a run with another player in it and which `DecisionSurface.OfferedOnlyWithAnotherPlayer` reads off the IL.
The ancient rows are the same class on a run of act 2 or act 3 alone: the engine builds a run whose acts list is one act the way it builds the won-run proof's one-act run, and such a run opens on that act's ancient (`DecisionSurface.ActAncients`) rather than on Neow, so `SyntheticFixtureGenerator.OpenTheRun` answers the opening as the recorder writes it - `ChooseNeowBlessing` on Neow's room, `ChooseEventOption` naming the ancient and the relic's key on an ancient's - and one row per option of each of the six, every one a relic, on a seed hunted so the ancient is rolled and offers it, retires the ancient, the option, the relic's seams and what the relic adds.
A generated-only acts list is admissible as coverage evidence on the footing of the won-run proof's one-act run, is never listed or shared, and every excusal a row of it holds says so in its own words (`DecisionExcusals.GeneratedByAnActFirstRow`); no declared or fake starting inventory is built, because every producer is reachable from state the game itself produced.
Two recorder defects were found that way and fixed beside it: the loot screen's Skip read as "not taken and unchanged" and was dropped with its held answer left for the next click, and a chest skip stopped the recording because `SkipRelicLocally` is `PickRelicLocally(null)` on this build.
The producer rows found two more: a purchase, a rest or a claim whose own work offers a rewards set was refused as unsettled and replayed by blocking, and a potion thrown at one of two enemies was recorded without its target and refused at replay.
The ancient rows found a fifth: the purchases Lord's Parasol makes for itself as the merchant is entered, through the purchase member with `ignoreCost` set, were recorded as the player's, ahead of the move that opened the shop, and refused at replay in the room the move left; a purchase the engine makes for itself is not a decision and `RunRecorder.ShopPurchased` records none, proven headlessly only - in the retail client those purchases run on scene-tree timers the move's settle does not wait on, a known limitation owed to the retail-timing follow-up.
The event rows are the same class again, one per option of every event a question mark opens on this build, 148 rows over 46 events: a policy can name an event and an option key (`EventId`, `EventOptionKey`, with `EventOptionsOnTheWay` for a page past the first), the walk routes to the act's first question mark on a seed hunted so the mark rolls an event and the act's shuffled set deals that one (`SeedHunt.Opening.OpensAtTheFirstQuestionMark`, read the way `RoomSet.NextEvent` and `UnknownMapPointOdds.Roll` deal it), answers the pages by key through the recorded `ChooseEventOption`, fights where an option fights and resumes the event the fight was fought in (`RunDriver.ResumeTheEventTheFightWasFoughtIn`, the engine half of the retail proceed off that fight's loot screen), and claims what an option offers; the acts lists are the default progression, its Underdocks variant, and the Hive or Glory alone on the ancient rows' footing, and the thief row beside them kills the Thieving Hopper holding the card it stole and claims the special card.
An option's own work is read the way the recorder reads it: `EventOptionWork` takes the task `EventSynchronizer.ChooseLocalOption` keeps to itself, the driver waits for it or for the hand-over to the player the recorder's `HandedToThePlayerDuring` names, and refuses a fault in it by name, because the game's `TaskHelper.RunSafely` swallows one and leaves the event on the same page; five events fault headlessly on a scene-tree singleton, and `PresentationStandIns` rewrites those calls at the call site in event bodies only, because a stand-in at the getter breaks the engine's own null-conditional reads (`docs/headless-fidelity.md` owns both).
The event rows found the act-2 opening move: every act after the first opens on the map with nothing visited and the map screen lights the starting point alone, so `MapTravelRule.TravelableFrom` answers a run standing on no node with that point and the driver had refused every recording past a first act at its second act's own opening move; `ReplayRefusalRegressionTests` holds it on the fixture seed.
An option an event constructs locked is derived as not choosable (`DecisionSurface.NotChoosableOptions`, twenty on this build) rather than excused by hand.
The blessing rows are one per relic Neow deals that produces no seam, sixteen on this build, each on a seed hunted so Neow offers it, and with the producer rows they are held to every blessing the build offers a singleplayer run.
A run lost outside a fight is proved beside them: the walk holds on to the Slippery Bridge past its warnings (`WalkPolicy.ChooseItUntilTheRunEnds`) until the hold kills the player, the game ends the run from inside the option's own work through `CreatureCmd.Kill` - a block the headless flag skips whole and `RunDeath` puts the engine half of back, `OnEnded(false)` at the same instant - and the recorder finishes there with the killing option as the last decision; `RunEnding` reads that ending and a win's, and not a loss with a fight live, which is read at the fight's end as the recorder reads it; `docs/headless-fidelity.md` owns the patch.
**The whole-act journey plays the measured rule, and the rows on a run of an act alone play the earlier one.**
`SyntheticFixtureGenerator.SurvivingIndex` is the journey's fight rule and `WalkPolicy.Rule` says which of two it plays: `SurvivalRule.BlockWhenThreatened`, the default - while the enemies' displayed attack damage exceeds the block held, the first playable card the game says gains block, otherwise the first playable attack aimed at the living enemy with the least health, a card reward's card by block-or-attack first, then canonical cost, then offer order - measured on 2026-09-19 over 300 hunted candidates per character and act-one route to beat the act's boss on roughly twice as many seeds as attack-first did (`docs/release-bar.md`, "The character rows"); and `SurvivalRule.AttackFirst`, the rule before it, which the ancient rows and the event rows on a run of the Hive or Glory alone name, because their fights are a later act's enemies against a starter deck and such a fight is lost under the measured rule on 63 of 64 hunted Glory seeds.
Both read the hand, the intent number the game draws over each enemy and the player's own state, and nothing else; neither is a claim about how to play, and the first-fight journey keeps its own rule and its committed digests.
A journey that dies fails naming the floor, the encounter and the character (`FightLostException`, rethrown by `FightAndTakeTheLoot` with where it stood), which is what a seed is hunted on.
The character rows are `GeneratedCoverageTests.CharacterRows`: a whole first act of every character on each act-one route the measurement verified a seed for, and the Ironclad at ascension 10, each on its pinned seed with the ancient its second act opens on (`SeedHunt.Opening.NextActAncientId`, rolled as the run is generated) as the criterion beside the survival, played to that ancient's page through the recorder and replayed to parity on every merge; `RecordedActWalk.Walk` takes the character and the ascension, and its defaults keep every earlier row.
`coverage` prints one line per axis - characters, variants (the acts list) and ascensions - for the crediting recordings and writes the same under `axes` in `coverage.json`, so the number says what it is evidence about; `CoverageTests` holds the committed corpus's lines.
What no generated walk reaches is excused in `DecisionExcusals` by what its producer is: the two screens the headless host has none for and no walk answers (the bundle screen is stood in for and the Scroll Boxes row answers it through the stand-in), the undo of an ended turn and the mend, which the client offers only to a run with another player in it, the reroll the driver refuses, the Architect and its options and seams, which the won-run proof reaches, the hatch the Byrdonis Egg adds (a relic granted outside a recorded decision is state no replay of the recording reproduces), and every point only the second act on deals past its opening room: the eleven options of Darv the Ironclad's default-progression row does not take (its second act opens on Darv and it takes the option offered first, Calling Bell), the nine events the game allows only from the second act on or in the second act alone (`IsAllowed`), and the card-removal reward, which only a Necrobinder's Forbidden Grimoire puts on the loot screen; two options are off the route for their own reasons (Zen Weaver's acupuncture at 250 gold on a Hive-alone run, Self-Help Book's page with nothing to enchant), Colorful Philosophers' Ironclad is derived as withheld from the Ironclad the event rows that reach it play, War Historian Repy is reached only by a second question mark after the Lantern Key, and the Grave of the Forgotten needs a power in the deck.
Those are owed to the sixth stage - a walk on from the second act's opening room, on a seed hunted so the act deals the event or Darv offers the option - or to the retail soak.
**`EngineCommands.VerifyLedger()` asks a third question, the other way round: every way a decision can reach the game on this build is claimed by a row or excused in writing.**
It is a separate method from `Verify()` because it walks the IL of every method body in the game assembly: `engine-commands` and `DecisionLedgerTests` ask it, and `RecorderModule.Examine`, the recorder's install gate inside the retail client, asks only `Verify()`'s two questions; a walk the build defeats is a sentence out of `VerifyLedger`, never an exception.
`DecisionLedger` is that account: the candidates are `DecisionSurface`'s ledger kinds - each game action a net action becomes, each choice the client syncs and the member that syncs it (its kind read off the `PlayerChoiceResult` factory that member constructs), each message and the member that sends it, each overlay screen, each room type and room class - and the claims are `EngineCommand.Observes`, the observations every row of the table carries beyond the member it is mapped onto, plus `NetActionClaims` for the net actions and `DecisionLedger.Excused` for the candidates no row watches, each with a class (`outcome-of`, `transport`, `flavor`, `lobby`, `sync`, `checksum`, `multiplayer-only`, `ending`, `engine-driven`, `non-standard`, `no-room`) and a reason.
A candidate accounted for neither way is `UNCLASSIFIED`, which is what a game update's new message or screen is before anybody looks at it, and `engine-commands` reports DRIFTED on it; an excusal naming a candidate no walk produces, or one a row already claims, is stale and refused the same way.
A member that syncs a choice through a `PlayerChoiceResult` factory the walk does not know is listed under that factory's name, and one whose own body constructs no result under `DecisionSurface.UnreadChoiceKind`, so a game update's new synced choice is an `UNCLASSIFIED` line rather than a silent drop.
Every type in `scripts/unreadable-choice-scan-bodies.txt` - a body the IL reader could not read whole, or a type the runtime could not load - is a place a send or a sync could hide from the walks, so `VerifyLedger` holds that whole set to its committed record wherever `WorktreeLocator.Find` finds one and names each line that differs, which is the guard for a type the walks never reached - the packaged arbiter has no worktree above it, and a record not on hand is counted and said to be so rather than reported as drift; of that set, the sender or syncer types the walks did reach are `DecisionSurface.UnreadableLedgerBodies`, each with its body count and a written reason in `DecisionLedger.UnreadableExcused` that its sends and syncs are all on the ledger - read off the decompiled build - and `VerifyLedger` names one excused neither way or excused for nothing; the ledger record carries the set's size and those types beside the candidates.
`scripts/decision-ledger.txt` is the ledger on this build, regenerated by `./scripts/arbiter engine-commands --update` and held by `DecisionLedgerTests`; it is about what the recorder watches, where `scripts/decision-coverage.txt` is about what the corpus reaches, and the two stay separate files because a claim about code and an excusal about recordings are different facts.
The ui-reach walk of the enumeration plan is not built; it is the expensive one and goes in on its own.

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
wherever a fight is live there. Where one is not, the measurement said nothing, so
`FloorEntrySnapshotEligibility` refuses it by rule rather than leaving it to be noticed;
that arrival now reads the same restored as live, and measuring whether it restores field
for field is its own change.
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
`CanonicalStateProjection.ProjectCombat` emits without a migration - that moves every
committed boundary digest, which is what manifest format 7 was.

**Refuse rather than approximate.** An unknown action verb, a card that is not where
the manifest says, a mismatched environment: each of these fails loudly. A replay
that quietly does something plausible is the failure mode this whole project exists
to prevent.

**What CI cannot run is recorded by name.** On a runner without the game, the 184
tests named in `scripts/expected-hosted-skips.txt` skip out of the 290 cases
`Sts2PilotTrainer.Arbiter.Tests` reports there, and the job still reports success.
Both figures are what a game-free run prints and neither can be arrived at by adding
up attributes: a `[GameTheory]` skipped there is one case and expands into a row per
datum where it runs, so a run with the game reports more cases than 290.
`./scripts/assert-expected-skips.sh` asserts the skipped set against that list, so
adding a `[GameFact]`, moving a test behind one, or deleting one fails CI until the
list is regenerated with `--update` in the same commit. It catches structural drift
only. A test that skips there and is broken inside is caught by the local gate, which
runs everything.

**Every engine run the command line makes is a fresh process, and none outlives the command that made it.**
`SelfProcess` in `Sts2PilotTrainer.Cli` is the one place a child is started: a terminated parent kills every live child's tree before the signal ends it, and a child started there watches its parent through `ParentProcess` and stops when the parent is gone, because a parent killed outright never gets to say so.
A gate stopped twelve seconds in once left its negative-controls child replaying for a quarter of an hour into the gate's evidence directory; `ChildProcessLifetimeTests` holds both halves against the real gate.
Do not start a child of this tool any other way.

**Read [docs/environment-identity.md](docs/environment-identity.md) before touching run setup or preflight.**
Two fields on that list are there because a replay looked correct and was not: the act variant and the player's unlock state.
Both change every fight in a run while leaving the map identical.
The document also owns the distinction between a runtime reading and an explicitly supplied headless progress model.
`LocalEnvironment` owns the v0.111.0 adapter, `EnvironmentPreflight` owns the game-free rules, and neither path writes.
What a mod *says* about itself and what it *did* are two readings, not one: `HarmonyRoster` takes the second from Harmony's own registry, the recorder captures it into `environment.mods.patch_roster` at run start and run end, and the preflight judges both beside the declaration rule rather than in place of it.
A changed roster means the run had no stable mod environment and refuses publication; [docs/environment-identity.md](docs/environment-identity.md) owns the comparison and the migration exception for recordings made before the end reading existed.
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
deliberately small. The bootstrap declares no IL patch on this build and the prepared
`sts2.dll` hashes the same as the installed one: the one it declared until 2026-09-19
returned the game's own end-of-turn wait at once and let the turn loop reach a fight's
end before the executor had popped the ended turn, which is what refused every Defect
walk the Lightning orb ended a fight in; that document owns the mechanism and
`DefectOrbFightEndTests` holds the fight. A game body naming a Godot member the vendored
stubs have not got fails silently inside the game's own `TaskHelper.RunSafely`, and
`scripts/unreadable-choice-scan-bodies.txt` is the list of where to look: `Mathf.Log`
stalled every headless walk into the Phantasmal Gardeners elite and
`Callable.From<TResult>(Func<TResult>)` faulted every fight ended by an end-of-turn
kill before both were added (`third_party/godot-stubs/CHANGES.md`). `TestMode` in particular reaches further than its name suggests:
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

**A retail client is started by `./scripts/retail-client.sh` and by nothing else, never through Steam.**
Asking Steam to launch beside an existing client is refused with `Game already running`, and the executable opened by hand stops on `No appID found`; both recurred until the launch had one owner.
The helper refuses while any Slay the Spire 2 client exists, launches the retail executable with MegaCrit's own `--force-steam=off` and an explicit `--clientId` from an empty working directory - the launch proved on v0.111.0 to initialise no Steamworks, touch no Steam save tree and disturb no Steam session elsewhere - and writes the one ownership record every worker on the machine reads.
`--headless` appends the engine's own flag, measured to run the whole scene tree with no window at parity; the retail soak launches under it.
`release` sends TERM to exactly that process and waits, because the client's teardown is anywhere from seconds to forty minutes; nothing here force-kills, and nothing signals a client the record does not name.
`RetailClientLaunchTests` holds both failure paths against a stand-in executable without the game; [docs/in-game-host.md](docs/in-game-host.md), "Launching the retail client", owns the procedure and its evidence.

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
The strip's floor markers are the run-history screen's own icons through `FloorMarkerArt`, at the history entry's own size, and `LibraryPaneArt.CellGeometry` is the one place a cell's icon, numeral, badge, ring and bookmark tab are placed so they cannot collide; `LibraryPaneArt.Lay` is the same one place for the pane - the pane's way forward is the panel's own primary ribbon, the plate's ribbons keep the foot side by side, the strip keeps the history entry's marker, the relic rows and the deck's tile rows page at the run-history holder's own size read through `RelicHolderArt`, and a pane short of one row of each refuses rather than overlapping.
Every ribbon the library duplicates from the popup's own button carries its own copy of the two materials the retail button animates, through `LibraryRibbonArt.OwnMaterials`; shared, one hover lit every ribbon on the screen.
An online row's identity is its share id and code, while `RunId` remains the manifest's identity; two submissions of one run are two rows and each resolves through its own share.
Where a player can be stood is the recording's own `boundaries[]`, read through `RunView`, so no row can offer somewhere `RecordedFightEntry` would refuse; `RecordedFightRun.Start` takes the plan, because there is still one playback path.
A boundary existing and a boundary being reachable are two facts and a row needs both: `RunViewPosition.Reachable` asks `RetailPlayback.RouteTo` whether this client has a route there - walking the recording's own decisions, or restoring the game's own save from a floor arrival with a live fight - and `Playable` folds it in beside the run's start and an unfinished fight, so every surface that reads that rule - the play-from row, Continue, Start the run over, both strips and the run-history plate - refuses in the same words rather than constructing a run and aborting mid-journey.
On this build that leaves the first fight reachable by walking, every later fight whose arrival dealt it reachable by restoring, and every floor between fights refused by name, because reaching one means playing the fight before it.
`OwnRunPlaybackTests` drives every row the library offers on both committed native recordings to the engine's own entry, so the offer and the entry cannot disagree behind a green suite.
`RunProgress` under the store holds fight ordinals and nothing resumable - it is the pips and Continue's number, never a save.
What a player reads is `LibraryCopy`; what is drawn is `LibraryScreen`, and [docs/in-game-host.md](docs/in-game-host.md) owns the accepted parchment design it draws.

**The mod a player installs is `Runmobile`, and recorded fights, recording and the run library are its modules; the retail soak is its one instrument.**
`RunmobileMod` is the shell and `IRunmobileModule` the line between it and a feature; a module says whether it can run, installs its own patches and contributes its own surfaces, and one that refuses does not take the rest of the mod with it.
`RetailSoakModule` draws nothing and is off unless an isolated profile's `settings.json` carries a `retail_soak` plan, which only `scripts/retail-soak.sh` writes: it arms at the main menu, adopts the game once it has a plan, starts standard runs through `NGame.StartNewSingleplayerRun` with the recorder attached, plays them through what a click issues - `TryManualPlay`, an enqueued `EndPlayerTurnAction`, `EnqueueManualUse`, under `SurvivalPlayRule` - and the game's own screen and room handlers, replaces the game's cheating combat and event handlers with its own state-driven loops, paces every decision on `RunRecorder.Settled`, gives a run up through the pause menu at any state it does not know, writes `soak-done` and calls the game's own quit; `RetailSoakGuardTests` refuses by IL every call to `PowerCmd`, `CreatureCmd.Kill`, `CardSelectCmd.UseSelector`, `CardCmd.AutoPlay`, the seed override, an FTUE, epoch or preference write.
[docs/in-game-host.md](docs/in-game-host.md), "The retail soak", owns it and [docs/release-bar.md](docs/release-bar.md) owns its figure.
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
Such a recording is kept and nothing more - neither played from nor shared - and `NativeSource.KeptOnly` is the one reading every offering surface makes of that: the mirror of what the validator refuses on continuity and integrity, held to the validator's own verdict by `RunCaptureTests`, carried on every `RunViewPosition` so `Playable` refuses it, and said in the one sentence `LibraryCopy.KeptOnly` on the run-history plate, the Mine pane and the run view alike.
Do not add a second reader of that question or a sentence per cause; [docs/in-game-host.md](docs/in-game-host.md) owns the rule and why the plate once offered what the entry refused.
`RunRecorder` in the mod owns only what a pure class cannot: which game member is which decision, what its arguments are, and when the engine has settled enough to read.
When a continued run is read is `RunRecorder.HasEnteredItsRoom`: standing in its room, with a fight that is opening open, because a continued run carries its floor count on the save before it has a room and the act floor reads 0 until the room is re-entered - an honest Continue read before that resumed as a broken watch; `RecorderContinueTests` holds both Continues.
Inside a fight it hands over to the same `PlayerFightObserver` the recorded-fight journey uses, through `IFightSampleSink`.
The recorder refuses a run whose start it did not witness and marks `continuity = broken` when a resumed session's live state is one its journal cannot place, with two exceptions: the game's own return to the latest save it took, and a reload that rewound the run to an earlier decision the journal holds.
Placing it compares the complete digest and nothing weaker, because the two readings are the same state: **the projection carries nothing of a fight outside a live one.** `CanonicalStateProjection.ProjectCombat` emits `combat.in_progress` and `combat.outcome` and nothing else into the digest once the combat manager says no fight is running - the outcome read off the room the run stands in, `victory` in the finished fight's own room and `none` past it, `defeat` off a dead player - never off the finished `PlayerCombatState` the engine keeps until the next fight, which the game's own save has no representation of.
One reading of a finished fight is sampled beside the digest and never hashed into it: `combat.ended_on_side`, which side's turn the fight ended in, through `CanonicalState.Builder.AddOutsideTheDigest`, because the step that ends a fight samples no roster afterwards and an enemy leaves a fight alive only during its own side's turn - so `CombatProjection` credits a fight-ending step's enemy health only where the sample says `player`, and leaves the turn without a number where it says `enemy` or says nothing; `FinishedFightProjectionTests` holds that timing rule to the engine's own escape call sites. Through manifest format 6 it emitted that finished fight into every reading until the next one, so a run continued from a save read as a different state at every shop, rest, event or loot screen after a fight and failed publication at its next arrival; format 7 is that projection, and `FinishedFightResidue` in `Sts2PilotTrainer.Replay` owns what a format-6 file carried and this build does not - `ManifestJson` reads such a file with those expectations taken away and every boundary marked with the projection its digest was hashed under (`ReplayBoundary.Projection`, a fact about the digest and never read off the file's own version or migration note), `gate` names a boundary this build cannot reproduce off that mark, and `migrate-manifest --derive-boundaries` re-derives exactly those from a verified replay and stamps every boundary it verified as this projection's. Do not put anything of a finished fight back into the projection, and do not add a second comparison beside the complete digest; [docs/in-game-host.md](docs/in-game-host.md) owns the rule.
**Where the game saved is recorded, and the game's own rollback is the return to the latest of those saves, whatever room it was taken in.**
The game saves at every map-point arrival, every fight won and every ancient event finished, and Continue restores the latest, so a quit after a claimed reward, a purchase, a rest or an event page comes back with that decision undone and re-offered: that is the game working as designed and never a hole.
`RunRecorder.RunSaved` observes `SaveManager.SaveRun`, places the save on the decision it holds the state after, and writes a `save_point` line once the game's own save task has completed and not before, because Continue restores what is on the disk; `RunCapture.MarkSavePoint` is the one writer, and the run-start save is never written and is what a rollback to the opening reading returns to.
Which `SaveManager.SaveRun` overloads this build declares, which members reach each, and which types it subclasses `AncientEventModel` with, are read from the assembly rather than assumed: `RunRecorderTests.TheGamesSaveContractIsTheRecordedOne` holds both to `scripts/save-points.txt`, regenerated with `./scripts/save-points.sh --update` in the change that adopts a build, so a game update that adds a way to save, moves where the run saves or changes which ancient events exist shows up as a diff rather than as a save/resume test that keeps passing against a set the game no longer has.
That record cannot see an act change, because a new or alternative act keeps every `SaveManager.SaveRun` call site and every ancient-event subclass where they were, so `ActTopology` in `Sts2PilotTrainer.Engine` is the reading that changes instead: every act `ModelDb.Acts` ships at each index, whether it is the index's default, the epochs its own `IsUnlocked` names, its room and floor counts, and every `ActModel` subclass the database leaves out.
`RunRecorderTests.TheGamesActTopologyIsTheRecordedOne` holds it to `scripts/act-topology.txt`, regenerated with `./scripts/act-topology.sh --update` in the same change, so a build that adds an act, moves one or changes the default shows up as a diff of that record.
`TheFixturesProgressionIsTheGamesDefaultOne` holds `RecordedActWalk.Acts` alone to the default act at each index; the record diff is what flags every other fixture.
When that record diffs, the other fixture progression literals must be updated by hand as well - `SyntheticFixtureGenerator` and its `WholeAct` and `ScreenAtBoundary` partials, `RetailBranchProbe`, `HeadlessRuns`, `HeadlessGameplayCaptureTests`, `RecorderContinueTests`, `RecorderTimingTests`, `RecorderSeamDefaultTests`, `CardPromptCaptureTests` and `PlayerFightObserverTests` - because they do not yet share one owner and `StartRun` accepts any shipped act list.
The step that ends a fight the run survives is read once the engine has settled, through the pump, because the game rolls the rewards after the killing action finishes and a reading taken before that names a state no replay holds; the retail client rolls them later still, on its own clock, so a resume matches that one entry through the next decision's `before_digest` and never through its own (`RunCapture.MatchesTheRestoredLootScreen`), and no other entry gets that latitude.
A decision made before the recorder has read after the one before it is closed on the new decision's own before-reading where the earlier one's work is done and the engine quiet, and refused by name where it is not (`RunRecorder.CloseStrandedDecisions`, the run-level `CloseStrandedFightStep`); a fight's first action does the same at `RequestEnqueue` and attaches the observer ahead of the executor (`RunRecorder.WatchTheFightBefore`), because inside that poll-wide window a play, a potion and a rest option each went unrecorded or misattributed with `integrity = complete`; `RunRecorder.Settled` is the one reading a driver paces on and `RecorderPacingTests` holds all three shapes.
Every other decision is read once the engine's own work for it is finished, with one exception four decisions' work opts into - the event option's, the purchase's, the rest option's and the reward claim's: a run handed to the player inside that work - a rewards set on offer, the Crystal Sphere's screen up - which `RunRecorder.HandedToThePlayerDuring` owns, asked of that decision and never of the run, because a relic bought at the shop, a heal under Tiny Mailbox or a relic claimed off Neow's Bones offers a set from inside its own task, which finishes only once the set is answered, and the driver replays each the same way through `RunDriver.SettleOrHandOver` rather than by awaiting a task the answering decisions cannot reach; an event option's work is a task the synchronizer keeps to itself, read on its way past through `EventOption.Chosen`, because a decision settled on the action queue alone was read during the hit animation Brain Leech's RIP awaits before it rolls its reward.
A loot set the map move declines is read from the move's own before-reading on both sides, never at the funnel, because the move has advanced the act floor and the coordinate before it leaves the room and the driver declines the set before the move; `RecorderTimingTests` holds both readings to `TraceParity`, and [docs/in-game-host.md](docs/in-game-host.md) owns each mechanism.
That rollback stays continuous, keeps the undone decisions as discarded evidence in the journal and manifest - `source.native.save_points` beside them, captured, present and empty where the game saved only at run start - and resumes the replayable history at the save's decision; the gate replays every branch from that decision and holds its state either end, exactly.
`save_points` lists the continued history's saves only, and a branch of the game's own rollback carries the save it returned to as its own `save_point`, because a later reload behind it takes that save off the list; the validator holds the branch to its own save and to the reload that removed it, never to a list shortened after the fact.
A run continued outside a fight after a won one is therefore continuous, validates, is the player's to play from and passes `gate`; `RecorderContinueTests` holds it to the headless equivalent of that gate, from the game's own saves through the whole replay twice to one digest, every checkpoint, every declared boundary and every branch, short of the retail preflight a headless recording's patch roster rightly fails; the reload is the `rewound` case below, and no other mismatch is repaired.
Every way of leaving a run and coming back to it, or giving it up, is the save-Continue-give-up matrix in that same class: one `[GameTheory]` of 24 rows, each driven through the game's own saves and restore - the run-start save, an arrival, a fight won and an ancient event finished among them - held to the recorder's contract at every Continue and to one of four verdicts once the run is given up, with a refused Continue failing on the fields the restored run differed in.
A new way in or out is a row there, never a second harness; it is game-gated, so the local gate runs it and a hosted runner never does.
**A hole in the account of a run and a recorder that has stopped watching it are two things, and `RunRefusal` is where a refusal says which it is.**
A decision the recorder saw and could not name is a third, and `RunRecorder.StopAt` is its path: it calls `RunCapture.MarkUnmapped` with the member or screen it was met at and the game's own values as strings, so the recording ends `integrity = unmapped` with its continuity untouched and the validator names the decision, where `RunRecorder.Refuse` calls `MarkBroken` for a decision that went by unread and gets the sentence about a watch that stopped and started again.
Past a stop nothing is recorded and nothing is refused; [docs/in-game-host.md](docs/in-game-host.md) owns the ordering and the two entry points.
A decision that arrives by no member the recorder patches is stopped at one of three seams rather than dropped: `RunRecorder.ActionRequested` on `ActionQueueSynchronizer.RequestEnqueue`, the local origin of every game action, classifying each instance once against `NetActionClaims` in the engine (claimed, the engine's own, the console's, or a stranger stopped at the `net_action` seam by type); the fight observer's default for a player-driven action that is none of the five a fight is made of; and `ChoiceSynced`'s default for a synced choice no open prompt, screen, game-owned selector or announced decision claims, at the `player_choice` seam by kind.
`NetActionClaims.Verify` holds the table to `DecisionSurface.NetActions` in both directions; `RecorderSeamDefaultTests` drives a stranger through the seams headlessly; [docs/in-game-host.md](docs/in-game-host.md) owns what each default excludes and why.
A resume the recorder can place in its own history behind the game's latest save is a reload that rewound the run behind what it had recorded - an older backup, a cloud copy - and it costs the recording its sharing and the watch nothing: the decisions the reload abandoned go where the game's own rollback puts what it undid, a discarded branch marked `reload` from the decision the game came back to, the replayable history resumes there, and the recorder goes on recording.
That recording is `continuity = rewound`: whole, so the validator takes it and the run is the player's to play from, and never theirs to share, which the run-history plate's and the Mine pane's submit rows, `RunSharing`'s seal and the gate's `continuity` condition all refuse - the validator cannot, because it is what `RecordedFightEntry` asks before it stands anybody in a fight.
A branch replays from the history as it stood when the branch was made, never from the continued history as it stands now: a later reload that rewound behind the branch's return point remade the decisions the branch was played from, and `DiscardedBranchHistory` is the one reader of which branch holds each of them, for the arbiter's replay and the validator alike.
A hole outranks a rewind: a reload the recorder can place after a resume it could not leaves the recording `broken`.
A resume it cannot place stops the watch, because nothing establishes what the run is from there.
The class is on the journal line as `watch_continues` rather than derived from the sentence, so a session after this one keeps the disposition rather than reading the words and guessing; a line without it is a refusal that stopped the watch, which is every refusal an older journal holds.
`source.native.integrity` is the one field that says whether a recording may ever be published, and it is required from format v6: `complete`, `non-standard` for a run the console was used in, or `unmapped` for a recorder that stopped at a decision it could not name, with what it met in `source.native.unmapped` - `RunCapture.MarkNonStandard` and `RunCapture.MarkUnmapped` are the only writers.
A version-5 file states none, and `ManifestJson.MigrateFromVersion5` reads it as `complete` with `migrated_from_version = 5` beside it; that note is what excuses the `option_key` a version-5 recorder never read, and nothing else is ever excused by it.
A version-5 recorder sampled no map coordinate either, so the same reading derives the arrival checkpoint at every floor arrival the file declares, through `FloorArrival` and marked inferred, because the validator refuses a floor arrival nothing proves and a player's own recording in the store is the file the recorder wrote; `OwnRunPlaybackTests` drives the library's offer from that on-disk form as well as from the migrated copy.
Every decision in a journal carries the reading it began from as well as the one it settled into, because the comparison instant is before each decision; `RunJournal.Schema` is v6 - v2 added the before-reading, v3 the bookmark line, v4 the save-point line and the promise that every save the game asked for is on the file, v5 the promise that no reading carries a finished fight, and v6 the run-end patch-roster receipt - and a journal declaring any older schema is refused on resume rather than repaired, because continuing without the current projection and receipts would make one file claim two recording contracts.
**Every integrity claim it makes is derived from what was observed, never from what a component assumes it observed.**
Continuity, a witnessed start, the mod set a run was played under, the controls a verdict rests on: each of these is a claim about what happened, and a component that reports one it did not establish produces evidence nobody can check while every value in it is individually true.
Four such claims were shipped on this branch and caught in review; [docs/in-game-host.md](docs/in-game-host.md) names them, so the rule is checkable rather than an abstraction.
It writes an append-only `RunJournal` after every decision so a crash keeps the prefix, never raises `ProfileWriteBarrier` - the player's own run saves normally - and never writes a Steam id, a machine path, a profile id or hardware.
Every write goes through `RunmobileStore`. [docs/in-game-host.md](docs/in-game-host.md) owns the detail.
The whole recorder runs headlessly in the merge suite: `HeadlessGameplayCaptureTests` attaches it to a headless engine, plays a natural run through it and its observer, and replays the recording through `Arbiter.ReplayStartedRun` - the CLI's own loop past the retail preflight, which a headless roster rightly fails.
Two attach-site seams make that possible and are the only ones - `RunRecorder.GameIdentitySource` and `SettleClock` through `RunRecorder.Clock`; [docs/headless-fidelity.md](docs/headless-fidelity.md) owns them.
The recorder's one persistent surface is a row of the game's own version overlay rather than a plate or a chip: `RecorderPresence.For` in `Sts2PilotTrainer.Trainer` derives it from exactly `RunRecorder.Active`, `RunCapture.State` and `RunCapture.Integrity`, and `RecorderPresenceRow` in the mod draws it as a sibling label under the overlay's own MODDED row, installed apart from the watch above so a renamed overlay costs the row rather than the recorder. [docs/in-game-host.md](docs/in-game-host.md) owns the detail.
**A bookmark is a declared fact in the recording, pressed at the moment a fight ends, and nothing else writes one.**
`source.native.bookmarks` is one optional list keyed by fight ordinal, written by `RunCapture.MarkBookmark` through a journal line per press so a crash keeps whatever was pressed, and emitted by `ToManifest` for the marks that are on; the validator holds every entry to a fight with a `combat_start`, declared, in order, pressed at or after that fight and never past the history's end.
The control is `FightMark.For` in `Sts2PilotTrainer.Trainer`, total and pure over `RunRecorder.FightMarkFacts` - a recorder attached, `RunCapture.State`, `RunCapture.LastEndedFight`, `MovedOnFromLastFight`, whether a map move is announced to the recorder and not yet recorded, and whether that fight is marked, so a recording that stopped offers no tag and one recorded to its end still does - and `FightMarkTag` draws it under the top bar on the transport's own anchor, from a fight's last action to the node press that leaves its floor, which covers the loot screen, the card screen behind it and the game's death screen with one derivation; the pending move is read because the capture records a move only once the engine has settled at the other end, by which time the next fight is on screen.
On a loss the manifest is on disk before that screen is drawn, so `RunRecorder.Bookmark` writes it again through the same path and gate `Finish` used, and that rewrite changes nothing but the field.
A lost run is finished by `RunRecorder.End` only once the engine has ended the fight it was lost in: the game calls `RunManager.OnEnded` from inside the killing enemy turn and raises `CombatEnded` afterwards, so a recording finished at the first left the killing turn open and the lost fight without an end, a boundary or a line, and the death screen with nothing to offer; `RunTornDown` is the safety net that finishes it anyway.
A won run is finished inside the decision it was won on: the Architect's PROCEED calls `RunManager.WinRun`, which calls `OnEnded(true)` within that press, before the pump has polled once under Instant fast mode and before the game kills the player creature for the ending, so `RunRecorder.Finish` reads the one decision still pending where the engine is quiet rather than stranding it, and the headless host reads the same instant through `RunEnding` so a replay's last after-state is the recorder's; `docs/headless-fidelity.md` owns that patch, and `AWonRunFinishesContinuousWithTheDecisionItWasWonOnAndReplays` holds a won run from its first decision to a verified replay.
The last act's `ProceedToNextAct` opens the victory room in place of an act, and `RunDriver` takes it there.
The strip draws the mark as the gold tab off a cell's top-left corner - `RunStripCell.Bookmarked`, read once in `RunView.PositionsIn` from the recording, so the opened run and both browser tabs agree - opposite the teal played tick, because a bookmark is about the run and the tick is about the person; the whole-run save the browser design still owes takes a keep glyph, never the bookmark and never a star.
Combats only, in the moment only; a bookmark added later from the library would be the one edit of a finished recording the mod makes outside the recorder, and is deferred rather than built.

**A card prompt is observed at the `CardSelectCmd` entry point that asks it, never at the screen that draws it.**
`CardPrompts` in the mod is the shell's one owner of that: a prefix on each of the ten entry points that reach a screen reads what the prompt was asked, `CardPromptOffers` in `Sts2PilotTrainer.Engine` derives the list it offers from those arguments by the entry point's own expression, and a postfix announces what came back once the entry point's task has settled; the five entry points that forward to one of the ten are not patched, because a forwarder patched as well would announce one prompt twice.
The screens were what the recorder used to watch, and three of the five prompt shapes went unrecorded or misrecorded there - the combat-pile screen keeps an empty list of its own, the hand prompt draws no screen, the choose-a-card screen is no grid - because a recording's `option_index` is a position in the list the engine hands its own seam, which no screen holds.
`CardPromptOffers` is duplicated game logic and says so; `CardPromptOfferTests` holds every derivation to the list the engine handed a recording selector, element for element, and that test is what permits the duplication.
Whether that funnel still holds on a build is read from the game assembly's own IL by three facts in `RunRecorderTests`, beside the per-verb one: every public choice entry point of `CardSelectCmd` and `RelicSelectCmd` is patched or excused in `CardPrompts.Forwarders` with the entry point its own body calls; every method body that creates or shows a card-selection screen or puts the hand into selection is `CardSelectCmd`'s or excused by name; and which game types reach each entry point is `scripts/choice-entry-points.txt`, regenerated with `./scripts/choice-entry-points.sh --update` in the change that adopts a build.
That scan reads the game against the vendored Godot stubs, so a body whose signature or call site names a member the stubs have not got cannot be read, and a type whose shape needs one cannot be loaded; a fourth fact holds both sets - types with unreadable bodies, and the game's own types the runtime could not load, found by reading the assembly's type table from its metadata and taking away what loaded, because the loader names the Godot member it missed and not the type that needed it - to `scripts/unreadable-choice-scan-bodies.txt`, regenerated by the same `--update`, so a stub gap fails as a diff naming the type rather than thinning the enumeration or hiding a body that opens a screen.
The funnel rule is by namespace and not by name: every `Create` or `ShowScreen` on any type in `Nodes.Screens.CardSelection` is a call the scan judges, and the reward screen's own button is excused in the caller table with its reason.
They need the prepared game and the built CLI, so the local merge gate runs them and a hosted runner never does; the script refuses on the same condition rather than reporting a skipped test as a check.
When the list is read is the engine's timing and not a choice: an entry point with a `PlayerChoiceContext` reads after the context has paused the action, which a hook's context does on a later frame, so those prompts are derived at `ActionQueueSet.PauseActionForPlayerChoice` and every other in the prefix.
A selector of the game's own on the stack is the engine answering itself in both hosts and opens no prompt; an engine-side early return is derived to no decision.
The recorder subscribes and `RunRecorder.HoldCardPromptAnswers` decides once what an answer is: a prompt that asked for exactly N is written as N `SelectCardFromScreen`, a prompt that asked for a range - `MinSelect` below `MaxSelect`, which every choose-a-card screen is - as its picks and then one `ConfirmCardScreen` with their count, zero for a prompt declined, and a count outside what the prompt asked for, a prompt that settled before the engine paused for it, or two prompts open at once each stop the recording `unmapped` at the decision that opened it, naming what was met.
`ManifestCardSelector` reads a range prompt's count from that record, refuses picks that stop short of the maximum and simply end, and refuses an exact prompt's confirmation by name; the validator holds a count to the picks before it and nothing more, because which prompts take one is the engine's knowledge.
The verb is inside format v6 and a recording written before it is not edited to carry one: picks that reach the maximum are the whole answer with or without a confirmation, which is the one form such a recording can hold, so it still replays; [docs/headless-fidelity.md](docs/headless-fidelity.md) owns the rule.
`CardScreensUp` keeps the count both settles read and the card reward's screen, and hands nobody a screen's list; `RecordedCardScreen` lights the recording's card off the open prompt's list and refuses the prompts in `RecordedCardScreen.CannotLight` by name.
A prompt is counted only from the moment it is offered to the moment its task settles, never from the call: a hook's prompt whose action the fight ended before it ran is asked, never offered, and never settles, and counted from the call it would hold every later settle for the rest of the process; the same prompt is dropped as the open one when the fight ends and when the run is torn down, so it is not the conflict the next prompt meets.
The count is the current run's and `CardScreensUp.RunTornDown` starts a fresh one at zero, because the hand's teardown completes its prompt instead of cancelling it and the entry point then waits for ever on a queue the teardown reset; a prompt gives back to the run it was counted in, so a torn-down run's prompt can neither hold the next run's count up nor take it below zero.
`CardPromptCaptureTests` is the Headbutt break as a harness: the retail path headlessly - no selector on the engine's stack, the action paused, the local seam answering - recorded and replayed through the driver to the same digest.
A test that asserts how an answer is handled obtains the list it answers from the engine's own producer - `CardRewardAlternative.Generate` over a reward the engine put on a loot screen, `CardPromptOffers` held to the engine - and never writes one by hand, because the card-reward Skip defect lived behind a test whose hand-built list held the one alternative the handler happened to expect; `FixtureProvenanceTests` is the lint that keeps it, reading the compiled tests' IL through the engine's one reader so every spelling of a construction is one `newobj`, and `CardRewardAlternativeTests` holds the handler to every `PostAlternateCardRewardAction` the build produces, each obtained from the relic that produces it.
The headless host's Harmony patches are applied once per process, however many times a test session starts the engine, because re-detouring a method the engine is calling races with the runtime's own recompilation of it.

**A fight a person plays is captured, never re-read.**
`FightCapture` in `Sts2PilotTrainer.Replay` is the one owner of turning what the game's own action executor announces into the same `ReplayTrace` the headless arbiter produces; `PlayerFightObserver` in the mod only decides when a sample is taken.
An action the engine announces a second time because it paused for the player's choice is the same decision resuming, told to the sink as `ResumeStep` and never opened as a step of its own; [docs/in-game-host.md](docs/in-game-host.md) owns the mechanism.
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

# sts2-pilot-trainer

A deterministic replay arbiter for Slay the Spire 2: reconstruct a run from a video,
replay it through the real game engine, and check the result against what the video
shows. Intended to become an open-source mod. See [README.md](README.md).

## Build / test / run

```bash
./scripts/build.sh          # bootstrap the game assembly copy, then build everything
./scripts/install-mod.sh    # build the in-game mod and install it into the game's mods directory
dotnet test sts2-pilot-trainer.sln -c Release
./scripts/arbiter gate manifests/navegreed-OJ-6QXhNgdg.replay.json   # the whole standard, one verdict
./scripts/arbiter <command> # gate | validate | preflight | preflight-live | adopt-live |
                            # verify-seed | replay | determinism | negative-controls |
                            # combat-snapshot | combat-compare | enter-fight | recorded-fight |
                            # snapshot-restore-probe
./scripts/bootstrap.sh --archive build/archive   # keep the receipted prepared set under its build
```

`dotnet test` works without the game: the integration suite skips with an explanation
and the pure suite still runs. `scripts/arbiter` goes through `dotnet <dll>` rather
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

**Provenance is not decoration.** Every value in a manifest records whether it was
observed, inferred, engine-produced or declared, and observations carry the video
timestamp that lets someone re-check them. The validator enforces the parts it can.
Do not add a field without deciding which of those it is.

**Real-engine reproduction is the publication standard.** `gate` is where it is
written down and computed. No condition may be satisfied by a cheaper proxy - not
reader confidence, not arithmetic over the footage, not a screenshot of a mod list.
Those are filters worth having and they are not evidence: four of the ten history
corruptions pass every arithmetic check the frames allow.

**A boundary is re-derived, never deserialized.** `./scripts/arbiter
snapshot-restore-probe` measured the game's own save round trip at a combat-start
boundary on v0.111.0: `SerializableRun` carries the run exactly and the fight not at
all, and re-entering the room to recover it generates a different fight. The answer,
its numbers and what it does not refuse are in
[docs/native-replay-format.md](docs/native-replay-format.md). Do not add a cache that
stores a serialized run in place of the history that produces it.

**Refuse rather than approximate.** An unknown action verb, a card that is not where
the manifest says, a mismatched environment: each of these fails loudly. A replay
that quietly does something plausible is the failure mode this whole project exists
to prevent.

**Read [docs/environment-identity.md](docs/environment-identity.md) before touching run setup or preflight.**
Two fields on that list are there because a replay looked correct and was not: the act variant and the player's unlock state.
Both change every fight in a run while leaving the map identical.
The document also owns the distinction between a runtime reading and an explicitly supplied headless progress model.
`LocalEnvironment` owns the v0.111.0 adapter, `EnvironmentPreflight` owns the game-free rules, and neither path writes.
Every prerequisite it refuses is remediated by playing the game.
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

**Where this is going, and what is runnable at each step, is
[docs/proof-of-concept-path.md](docs/proof-of-concept-path.md).** Read it before
planning work toward the first tryable proof; it names the remaining slices in
dependency order and what is deliberately not built.

**Some checks cannot be moved downstream.** A run resumed from run history matches on
seed, build, content hash and acts, and replays perfectly — it is simply not the run
its history describes. That is caught at ingestion, on the recording, or not at all.
Same for the end-of-run reading: one reading of the environment cannot catch its own
drift. Do not weaken `source.run_start` or `source.run_summary` on the grounds that
the replay would catch it.

**Read [docs/headless-fidelity.md](docs/headless-fidelity.md) before changing what
the host patches or stands in for.** Each patch has a stated reason and the set is
deliberately small. `TestMode` in particular reaches further than its name suggests.
Two screens have no engine command at all - the loot a won fight offers, and the card
screens a reward or an enchantment opens - so the host drives the first and answers
the second from the manifest. Neither decides anything, and both refuse where the
manifest is silent.

**Read [docs/in-game-host.md](docs/in-game-host.md) before touching anything that runs
inside the retail client.** `Sts2PilotTrainer.Mod` is the only project loaded into the
player's game; `EngineHost.Start` must never run there, and `AdoptRunningGame` is the
way in. Two traps in that process cost a crash each and are written down there: mod
initialization runs before the game has a model database, and Godot does not load the
game into the default assembly load context. `./scripts/install-mod.sh` is the one
script here that writes inside a Slay the Spire 2 installation.
Its final state is exactly `CombatTrainer` under the selected supported game mod directory (`mods` or `mods_STEAMTEST`); upgrades use temporary siblings there to replace the complete artifact without mixing versions.

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
Keeping construction in the engine owner lets `./scripts/arbiter enter-fight` exercise the journey without a scene tree.
The run is generated against a supplied complete unlock state and can persist nothing:
`shouldSave: false` plus the mod's `ProfileWriteBarrier`, which is installed at mod
start and inert unless a trainer run is live. Do not weaken either, and do not add a
path that writes what the barrier suppresses.

**A fight a person plays is captured, never re-read.**
`FightCapture` in `Sts2PilotTrainer.Replay` is the one owner of turning what the game's own action executor announces into the same `ReplayTrace` the headless arbiter produces; `PlayerFightObserver` in the mod only decides when a sample is taken.
A projection is handed over only once the fight ended inside a sampled action, and a gap between two samples is refused rather than bridged.
The recording's side of the in-game comparison is `manifests/<id>.recorded-fight.json`, produced by `./scripts/arbiter recorded-fight` from a fresh replay and bound to the manifest by run id, history hash and combat-start digest; regenerate it in the same change that edits the manifest's fight.
Do not add a second capture path, a turn-level reset, a score or a verdict; `docs/comparison-direction.md` owns why.

**Player-facing wording is a template, never a recording.** Everything the mod says
lives in `Sts2PilotTrainer.Trainer`, and every recording-specific value in it is
interpolated - the creator from `source.video.channel_name`, the blessing and the node
from the run the decision is about to act on. A sentence that names NaveGreed, the
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

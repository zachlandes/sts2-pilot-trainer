# sts2-pilot-trainer

A deterministic replay arbiter for Slay the Spire 2: reconstruct a run from a video,
replay it through the real game engine, and check the result against what the video
shows. Intended to become an open-source mod. See [README.md](README.md).

## Build / test / run

```bash
./scripts/build.sh          # bootstrap the game assembly copy, then build everything
dotnet test sts2-pilot-trainer.sln -c Release
./scripts/arbiter gate manifests/navegreed-OJ-6QXhNgdg.replay.json   # the whole standard, one verdict
./scripts/arbiter <command> # gate | validate | preflight | verify-seed | replay |
                            # determinism | negative-controls | combat-snapshot |
                            # combat-compare
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

**Nothing from the game or from a video is ever committed.** No assemblies, no
localization tables, no frames, no stills. `.gitignore` blocks the file types; the
judgement is yours. Facts read from a video are fine and are what `manifests/` holds.

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
Those are filters worth having and they are not evidence: two of the four history
corruptions pass every arithmetic check the frames allow.

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

## Maintaining this file

Keep this short and true. Record only what almost every future session needs: how to
build and test, the invariants above, and where the real explanations live. Prefer a
pointer to the authoritative file over a copy of its contents — a duplicated
explanation is one that will drift. When something here stops being true, fix it in
the same change that made it untrue.

<!-- CLAUDE.md imports this file. Edit AGENTS.md, never CLAUDE.md. -->

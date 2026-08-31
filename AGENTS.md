# sts2-pilot-trainer

A deterministic replay arbiter for Slay the Spire 2: reconstruct a run from a video,
replay it through the real game engine, and check the result against what the video
shows. Intended to become an open-source mod. See [README.md](README.md).

## Build / test / run

```bash
./scripts/build.sh          # bootstrap the game assembly copy, then build everything
dotnet test sts2-pilot-trainer.sln -c Release
./scripts/arbiter <command> # preflight | verify-seed | replay | determinism |
                            # negative-controls | snapshot-lines
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

**Provenance is not decoration.** Every value in a manifest records whether it was
observed, inferred, engine-produced or declared, and observations carry the video
timestamp that lets someone re-check them. The validator enforces the parts it can.
Do not add a field without deciding which of those it is.

**Refuse rather than approximate.** An unknown action verb, a card that is not where
the manifest says, a mismatched environment: each of these fails loudly. A replay
that quietly does something plausible is the failure mode this whole project exists
to prevent.

**Read [docs/environment-identity.md](docs/environment-identity.md) before touching
run setup.** Two fields on that list are there because a replay looked correct and
was not: the act variant and the player's unlock state. Both change every fight in a
run while leaving the map identical.

**Read [docs/headless-fidelity.md](docs/headless-fidelity.md) before changing what
the host patches.** Each patch has a stated reason and the set is deliberately small.
`TestMode` in particular reaches further than its name suggests.

## Maintaining this file

Keep this short and true. Record only what almost every future session needs: how to
build and test, the invariants above, and where the real explanations live. Prefer a
pointer to the authoritative file over a copy of its contents — a duplicated
explanation is one that will drift. When something here stops being true, fix it in
the same change that made it untrue.

<!-- CLAUDE.md imports this file. Edit AGENTS.md, never CLAUDE.md. -->

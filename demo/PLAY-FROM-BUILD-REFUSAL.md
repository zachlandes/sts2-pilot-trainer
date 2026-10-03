# A build refusal at the play-from click

This demonstration uses the prepared game assembly and Godot stubs, not a retail window.
No client was launched, no display was used and no player profile was opened.

`PlayFromCompatibilityTests` calls the client's real `RecordedFightRun.Start` with a committed native run, changing one build identity field in memory.
Harmony stands in for the popup to capture its sentence and for the return to the menu to fault as though it failed after fading out.
The snapshot and launch calls are also stopped if reached, so the test cannot start a run or a subprocess.
This proves the entry's ordering and the sentence handed to the existing popup, not its rendered appearance.

## Reproduction before the fix

The version-mismatch case reached the return to the menu before showing its refusal.
When that return faulted, the popup received no sentence.
The regression run against the old entry ended with:

```text
Assert.Single() Failure: The collection was empty
TEST SESSION FAILED - dotnet test exited 1.
```

## Run the demonstration

Prepare the assemblies with `./scripts/build.sh` first if `build/lib` is absent.
Then run:

```bash
./scripts/test-session.sh tests/Sts2PilotTrainer.Mod.Tests/Sts2PilotTrainer.Mod.Tests.csproj --filter FullyQualifiedName~PlayFromCompatibilityTests --nologo
```

The cases refuse a changed version, UTC build date and content hash, each at the first fight's walk route and a later fight's restore route.
They assert that the popup receives the existing lookup heading and sentence, no return or snapshot materialisation occurs, the journey stays idle and its write barrier stays down.
A matching-build control reaches the launch call instead; its injected failure still takes the existing teardown path.

Captured output after the fix:

```text
TEST SESSION PASSED - the session ran to completion and every test in it passed.
```

The version case reuses this settled heading and template, with the two versions read from the preflight:

```text
Not playable on your version

This run exists. It was recorded on v0.110.0 and your game is v0.111.0.
```

For a changed date or hash under the same version, it reuses:

```text
This run exists. It was recorded on your build, and your game no longer matches what it was recorded under.
```

No new player-facing wording or wording placeholders were added.
The existing diagnostic is still written to the game's log.

The game-free tests also hold the play-from sentence to the direct-lookup sentence and confirm that ordinary browsing still hides the incompatible run:

```bash
./scripts/test-session.sh tests/Sts2PilotTrainer.Trainer.Tests/Sts2PilotTrainer.Trainer.Tests.csproj --filter 'FullyQualifiedName~RefusalCopyTests|FullyQualifiedName~RunBrowserTests' --nologo
```

```text
TEST SESSION PASSED - the session ran to completion and every test in it passed.
```

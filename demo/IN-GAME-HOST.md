# The Combat Trainer mod, running in the shipped game

*2026-09-02T01:52:21Z by Showboat 0.6.1*
<!-- showboat-id: 81df328a-8ac3-4a0b-9662-6bab68b7daaf -->

This document runs the in-game host and records what it actually printed and showed.
Every code block below was executed; the output under it is that run's output.
`showboat --workdir .. verify IN-GAME-HOST.md` re-runs the lot and diffs; the blocks are
repo-root commands, which is what the working directory is for.

**The claim being tested.** Can a mod load in the shipped Slay the Spire 2 client, ask
the arbiter's own preflight about the player's real game, and tell them - in the game,
in the game's own furniture - whether it can play NaveGreed's Floor 2 Sludge Spinner as
recorded, and what to go and play if it cannot?

It answers that and stops there.
Entering the fight is [its own document](RECORDED-FIGHT-ENTRY.md); nothing here claims any part of it.

The two screenshots are the one kind of image this repository keeps: our own screen,
drawn by our own mod, inside the player's own client. No frame of the source recording
is stored anywhere here; see [docs/in-game-host.md](../docs/in-game-host.md).

## Building and installing it

One project-owned command builds the mod and puts it where the game looks. It is the
only script in this repository that writes inside a Slay the Spire 2 installation.
Its final state is exactly one directory in the game's own mod surface, the same one Steam Workshop installs into; upgrades stage the complete named file set in a temporary sibling and replace the old directory rather than overlaying it.
`--uninstall` removes the final directory and nothing else.

```bash
set -o pipefail; ./scripts/install-mod.sh | sed -E 's/Time Elapsed .*/Time Elapsed <elapsed>/'
```

```output

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed <elapsed>
installed    : 6 files -> ~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods/CombatTrainer
next         : launch Slay the Spire 2, allow mod loading, then Singleplayer
```

## The refusal, from a process that is not a running game

Before the host reads anything it asks whether this process is a game whose state can
be read honestly. A console process is not, and saying so is the point: the same
refusal is what the mod gets if it asks during mod loading, which the game runs before
it has built its model database. Asking then does not return a wrong answer - it ends
the process. So the gate has teeth, and this is where they are exercised without the
game running.

The refusal names the assembly it read, and whether it was the only one. A host that
had bound to a second copy of the game assembly would report an empty, unstarted world
with total confidence, and the report would look exactly like a game that had not
started yet.

```bash
./scripts/arbiter adopt-live 2>&1 | grep -v '^SentryGodotInitializer' | sed -E 's|/[^ ]*/(sts2\.dll)|.../\1|'
```

```output
startup phase : None
This process is not a game whose state can be read honestly; refusing to report on it:
  - the game's startup phase is 'None', not one where it has a model database and an id-serialization cache to read. Adopt it from a surface the player can reach, not from mod loading, which the game runs before either exists. Read from .../sts2.dll.
```

## The game loads it

Launched through Steam, the game finds the manifest, loads the assembly, calls the
initializer, and reports the mod loaded. The mod reads nothing at this point: mod
initialization runs one startup phase before the game has a model database, so it
loads its embedded recording, installs one Harmony patch, and waits.

> [INFO] Found mod manifest file .../mods/CombatTrainer/CombatTrainer.json
> [INFO]   3: Combat Trainer (CombatTrainer)
> [INFO] Loading assembly DLL .../mods/CombatTrainer/CombatTrainer.dll
> [INFO] Calling initializer method of type Sts2PilotTrainer.Mod.CombatTrainerMod for CombatTrainer
> [INFO] [CombatTrainer] loaded; the mode card is added when the singleplayer menu opens
> [INFO] Finished mod initialization for 'Combat Trainer' (CombatTrainer)

Opening Singleplayer is the first moment there is demonstrably a running game, so that
is where the host adopts it - and the adoption says what it found:

> [INFO] [CombatTrainer] adopted the running game: 1660 models registered

Those lines are from the game's own log at
`~/Library/Application Support/SlayTheSpire2/logs/godot.log`, with the installation
path shortened. They are quoted rather than executed because launching the retail
client is not a thing a document can re-run unattended.

## The mode card

A fourth card beside Standard, Daily and Custom. It is a duplicate of the game's own
Custom Run card with two labels replaced and its released signal pointed somewhere
else, so the panel, the hover tween, the focus behaviour and the controller navigation
are the ones MegaCrit authored rather than a lookalike. The row is re-centred by the
step measured between two of the game's own cards, so four sit where three did.

The card keeps the icon it was duplicated from. Art of its own needs a resource pack,
and the packaging contract in [distribution](../docs/distribution.md) deliberately does
without one.

```bash {image}
![The Slay the Spire 2 singleplayer menu with four mode cards: Standard, Daily, Custom, and Combat Trainer, whose description reads "Fight NaveGreed's Floor 2 Sludge Spinner exactly as recorded, then compare your fight with the recording. Reads your game; never writes to it."](in-game-mode-card.png)
```

![The Slay the Spire 2 singleplayer menu with four mode cards: Standard, Daily, Custom, and Combat Trainer, whose description reads "Fight NaveGreed's Floor 2 Sludge Spinner exactly as recorded, then compare your fight with the recording. Reads your game; never writes to it."](96b6ed6d-2026-09-02.png)

## The screen it opens

One screen, built from the game's own modal popup, created and configured through the
game's own factory in the same order its confirmation popups use.

Everything on it is derived. The subtitle and the recording line come from the
manifest; each row's label is built from the manifest and the reading; each row's
colour is a `PreflightField` the gate produced; and every sentence under a row is that
field's own diagnostic, word for word. Unmet rows are ordered first, because what a
player has to act on should not sit below what already passed.

This is the captain's real modded profile, which is at ascension 9, and the screen was
captured while the host still asked its question about that profile: the ascension row
is red and carries the engine's sentence about what raises it, unedited, including the
promise that the tool will not do it for him.

That is no longer the question the host asks. Once the trainer constructs the run
itself, the unlocks, the acts and the ascension are supplied for that run and the row
states a requirement of the fight on offer rather than of a run nobody starts by hand
- so on this same profile the row is now met. The rule and the label are unchanged;
only the reading they are asked about is. The shot above is kept as what the S3 host
showed, and [the entry document](RECORDED-FIGHT-ENTRY.md) owns what replaced it.

The green content-hash row still carries its qualifier. A matching hash rules out one
class of divergence and is never proof of environment parity, and the engine owns the
sentence that says so.

```bash {image}
![The Combat Trainer screen over the singleplayer menu. Headline: "Your game cannot play this fight as recorded yet." A red row reads "Ascension 10 available on Ironclad" followed by the engine remediation "This profile's highest available ascension for CHARACTER.IRONCLAD is 9, and the manifest records ascension 10..." Green rows below read "Build v0.111.0" and "Content hash 1568834832" with its scope sentence.](in-game-eligibility-screen.png)
```

![The Combat Trainer screen over the singleplayer menu. Headline: "Your game cannot play this fight as recorded yet." A red row reads "Ascension 10 available on Ironclad" followed by the engine remediation "This profile's highest available ascension for CHARACTER.IRONCLAD is 9, and the manifest records ascension 10..." Green rows below read "Build v0.111.0" and "Content hash 1568834832" with its scope sentence.](376ba49a-2026-09-02.png)

## Executable host boundaries

The boundary suite drives `adopt-live` as a real command and verifies that a console process refuses without changing the prepared game inputs or sandbox profile.
It also loads a second copy of `sts2` and verifies that adoption refuses before reading either copy's state, with the bound assembly named in the refusal.
A third test proves that adoption still refuses during essential initialization, before the model database and id-serialization cache have both finished.
The remaining declarative test parses the mod manifest and verifies the non-gameplay, DLL-only, packless contract that keeps the compared content hash meaningful.
There is no source-reference scan presented as behavioral evidence.

```bash
set -o pipefail; dotnet test tests/Sts2PilotTrainer.Mod.Tests/Sts2PilotTrainer.Mod.Tests.csproj -c Release --nologo --filter 'FullyQualifiedName~ModHostBoundaryTests' 2>&1 | grep -E 'Passed!|Failed!' | sed -E 's/Duration: [^ ]+ ms/Duration: <duration>/'
```

```output
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: <duration> - Sts2PilotTrainer.Mod.Tests.dll (net9.0)
```

And what the screen says, checked on a machine that does not own the game. The wording
and the row rules live in `Sts2PilotTrainer.Trainer`, which has no game code, so every
claim the screen makes - the pass headline, the fail headline, a row's value, the
ordering, a green hash row keeping its qualifier, a failing gate with no row still
being shown as its own sentence - is a test.

```bash
set -o pipefail; dotnet test tests/Sts2PilotTrainer.Trainer.Tests/Sts2PilotTrainer.Trainer.Tests.csproj -c Release --nologo 2>&1 | grep -E 'Passed!|Failed!' | sed -E 's/Duration: [^ ]+ ms/Duration: <duration>/'
```

```output
Passed!  - Failed:     0, Passed:    74, Skipped:     0, Total:    74, Duration: <duration> - Sts2PilotTrainer.Trainer.Tests.dll (net9.0)
```

## What this proves, and what it does not

**Proved.** The shipped client discovers, loads and initialises the mod through its own mod surface.
A fourth mode card appears in the game's own furniture.
Opening it runs `Preflight.EvaluateLiveHost` against the player's real installed build and the profile the modded game uses, and shows the result with the engine's own remediation.
The executable console refusal leaves its prepared game inputs and sandbox profile unchanged.
The two screenshots below were taken before the fight was offered at all, which is why neither shows the offer; what the offer does, and what has and has not been watched doing it, is in [the entry document](RECORDED-FIGHT-ENTRY.md).

**Not proved.**
Nobody has played the fight.
A green content-hash row is not environment parity, and says so.
In this preserved S3 screen the unlock rows described the modded profile, which the game forks from the unmodded one.
The current fight offer instead evaluates those capabilities against the supplied in-memory progress used to construct its run; [the entry document](RECORDED-FIGHT-ENTRY.md) owns that evidence.

**Not measured here.** Whether a controller can drive the screen was compared against
the game's own confirmation popup under synthetic input, and the two behaved
identically - which is the most this can claim, since the screen is that popup. It is
not a claim that either responds to a controller.

[docs/in-game-host.md](../docs/in-game-host.md) records the two traps that cost a crash
each: mod initialization running before the game has a model database, and Godot
loading the game into its own assembly load context.

# The in-game host: what it proves, and what it does not

This is S3 of [the proof-of-concept path](proof-of-concept-path.md): the smallest mod
that loads in the shipped Slay the Spire 2 client and gives a player an honest answer
to one question — can this game play the recorded fight?

It answers that question and nothing else.
Starting the fight, replaying the recording's choices, and comparing the two fights are S4 and S5.
No source-reference scan stands in for an executable proof that those absent features remain absent.

## What it proves

**The retail game loads the mod through its own mod surface.**
`mods/CombatTrainer` contains `CombatTrainer.json`, `CombatTrainer.dll`, and the four project-owned libraries the host uses: `Sts2PilotTrainer.Trainer.dll`, `Sts2PilotTrainer.Engine.dll`, `Sts2PilotTrainer.Replay.dll`, and `Sts2PilotTrainer.IO.dll`.
The game's recursive scan discovers the manifest, `ModManager` loads the mod, and `ModInitializerAttribute` initializes it.
The libraries ship together; there is no separately installed framework or runtime dependency, and no resource pack.

**The eligibility answer comes from the same owner the arbiter uses.**
`Preflight.EvaluateLiveHost` reads this process's game and judges it through
`EnvironmentPreflight`, which has no game code and is tested on machines that do not
own the game.
The screen computes nothing: every row's state is a `PreflightField` the gate
produced, and every sentence about a failure is that field's own diagnostic, shown
word for word.

**It reads and never writes.**
The player's save, profile, progress, unlocks and installed build are inputs.
The executable `adopt-live` boundary test verifies that a console process is refused without changing the prepared game inputs or sandbox profile.
The mod-manifest contract verifies that the shipped host is non-gameplay and carries no resource pack.
There is no source-reference scan presented as behavioural evidence.

**It refuses rather than approximating.**
Every condition that would make a reading untrustworthy is a refusal with a sentence:
the game's startup phase, test mode, an empty model database, a content hash over
nothing, a build that cannot identify itself, a profile that is not loaded.
`./scripts/arbiter adopt-live` exercises that refusal from the command line, where a
console process is correctly not a running game.

## What it does not prove

**Nobody has played the fight.**
The screen states eligibility. It has no button that enters combat, because entering
combat is S4.

**A green content-hash row is not environment parity.**
The row carries the engine's own sentence saying so, whether it is green or red.
The hash covers content contributed by mods that declare themselves gameplay-affecting; it says nothing about a mod that patches behaviour.
The same prerequisite reading therefore inspects every mod the game discovered, including failed states that may have left resources loaded, and refuses every active local mod except the known non-gameplay Combat Trainer host.

**The unlock rows describe the modded profile.**
The game forks a separate profile for modded play, and that is the one a modded
session reads. A player with a complete unmodded profile can fail these rows and be
right to be surprised, which is why the screen names the profile it read and points
at the game's own import.

**A pass is a claim about right now.**
Nothing is cached. The screen is computed when it opens.

## Two traps that cost a crash each

Both are the same shape: the mod ran in a process it did not own, and assumed it was
the process it owns in the headless host. Both are now refusals rather than
assumptions, and both are recorded here because the next person to load code into
this client will meet them.

**Mod initialization runs before the game exists.**
The game calls mod initializers from `OneTimeInitialization.ExecuteVeryEarly`, one
phase before `ExecuteEssential` builds the model database and the id-serialization
cache. Reading the game there does not return a wrong answer; it ends the process
with a segmentation fault inside a static constructor. So the mod reads nothing at
initialization: it loads its embedded recording, installs one Harmony patch, and
adopts the running game later, from the singleplayer menu, which cannot exist before
startup has finished. `EngineHost.AdoptRunningGame` refuses unless the game's own
startup phase says otherwise.

**Godot does not load the game into the default load context.**
A mod's sibling assemblies have to be resolved on the load context the mod itself was
loaded into. Resolving them on `AssemblyLoadContext.Default` succeeds, and then their
reference to `sts2` is satisfied by the runtime probing the game's assembly file
again — a second copy, with the right path and an empty world. Everything reads
plausibly and nothing is initialised. Every refusal now names the assembly it read
and says when more than one is loaded, because "the game says no" is only meaningful
once it is clear which game was asked.

## The surfaces, and why they are the game's own

**The mode card is a duplicate of the game's Custom Run card**, renamed and rewired.
That is what makes it native: the panel, the shader, the hover tween, the focus
behaviour, the hotkey icon and the controller navigation are the nodes MegaCrit
authored. Only the two labels and the released signal are ours. The row is
re-centred by the step measured between two of the game's own cards, so four cards
sit where three did.

**The screen is the game's own modal popup**, created through `NGenericPopup.Create`
and added through `NModalContainer`, in the same order the game's own confirmation
popups use.

Two consequences of that choice, stated rather than hidden. The card keeps the icon
it was duplicated from, because art of its own would need a resource pack and the
packaging contract in [distribution](distribution.md) deliberately does without one.
And the popup's body scrolls when the evidence is longer than the panel, which is why
unmet rows are ordered first: what a player has to act on is above the fold, and the
rows that already passed are below it.

## Running it

```bash
./scripts/build.sh                       # bootstrap the game assembly copy, build everything
./scripts/install-mod.sh                 # build the mod and install it into the game's mods directory
./scripts/install-mod.sh --uninstall     # remove it again
./scripts/arbiter adopt-live             # the refusal, from a process that is not a running game
```

`install-mod.sh` is the one script in this repository that writes inside a Slay the Spire 2 installation.
Its final state is exactly `mods/CombatTrainer`; an upgrade stages the complete named file set in a temporary sibling under `mods` and replaces the old directory rather than overlaying it.
That is the game's own mod surface — the same directory Steam Workshop installs into — and the game offers no user-data alternative, because it derives the path from its executable's location.

[demo/IN-GAME-HOST.md](../demo/IN-GAME-HOST.md) has the mod card and the eligibility
screen as they appear in the shipped client.

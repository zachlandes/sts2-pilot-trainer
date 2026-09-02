# The in-game host: what it proves, and what it does not

This is the mod that loads in the shipped Slay the Spire 2 client: S3 of
[the proof-of-concept path](proof-of-concept-path.md) answers one question — can this
game play the recorded fight? — and S4 adds the button that enters it.

Comparing the player's fight with the recording's is S5 and is not here.

## What it proves

**The retail game loads the mod through its own mod surface.**
`CombatTrainer` under the selected game mod directory contains `CombatTrainer.json`, `CombatTrainer.dll`, and the four project-owned libraries the host uses: `Sts2PilotTrainer.Trainer.dll`, `Sts2PilotTrainer.Engine.dll`, `Sts2PilotTrainer.Replay.dll`, and `Sts2PilotTrainer.IO.dll`.
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

A refusal also has to be about the right thing. The mod-environment gate tells this
host's own failure apart from somebody else's mod being present: a game whose only
active mod is Combat Trainer, failed, is told that Combat Trainer failed to load, not
to go and disable mods it does not have. The longer explanation about what a content
hash cannot settle is kept for the case it describes, which is another active or
failed mod actually being there.

## Standing in the recorded fight

**The journey is the recording's, and the host only decides when.**
`RecordedFightEntry` in `Sts2PilotTrainer.Engine` constructs the run, makes the
recording's decisions in order, and proves the fight at the end of them is the
recorded one. `RecordedFightRun` in this mod owns when each of those happens relative
to the game's frames and what a player sees while they do, and nothing else. That
split is why the same journey runs on the command line with no scene tree, which is
where it is actually tested: `./scripts/arbiter enter-fight`.

**The run is constructed the way the game constructs its own.**
`GameSession.PrepareRunInRunningGame` reproduces the first half of the client's
`NGame.StartNewSingleplayerRun` — build the run state, `SetUpNewSingleplayer` with
`shouldSave: false` — and stops. The second half is the game's own private
`StartRun`, reached by name, which preloads the run's assets, finalises the starting
relics, launches, puts the scene on screen and enters the first act. Exactly one
input is substituted: the unlock state, which the retail path derives from the
player's save progress and which this run needs to be the recording's complete one.
See [environment identity](environment-identity.md) on why a supplied progress model
is not a reading of anybody.

**Nothing this run does can be persisted.**
`shouldSave: false` gates the run save and everything at the end of a run, and it
does not gate two writes this fight's path reaches: winning a combat calls
`SaveManager.UpdateProgressAfterCombatWon` and then `SaveProgressFile`, and an event
room saves the run with `saveProgress` defaulted on. `ProfileWriteBarrier` stops the
writes themselves. It is installed at mod start rather than when a run begins, and it
does nothing at all unless a trainer run is live — so a crash, a forced exit and a
quit are all covered by the write never happening, and a player's own runs go on
saving normally. It comes down on `RunManager.CleanUp`, which is where a run stops
existing on every path there is; a barrier left raised after the recorded fight would
silently stop saving the next run the player started, which is the same defect
pointed the other way.

**The recording owns every decision before the fight.**
Enforced on the two commands those decisions reach — `EventSynchronizer.ChooseLocalOption`
and `RunManager.EnterMapCoord` — rather than on the buttons that usually reach them.
A screen with its buttons hidden is a screen a controller, a hotkey or another mod can
still drive; the command is the thing that would actually change the run.

**The rows and the offer answer the same question.**
Every rule is the one S3 shipped and every row label is the one it approved; what
changed is which reading they are asked about. The screen asks
`Preflight.EvaluateLiveHost` for the progress model the run is actually generated
against - `RecordedFightEntry.SuppliedProgress`, the same constant the construction
uses - so each row states a requirement of the fight being offered rather than of a
run nobody starts by hand. The unlocks, the acts and the ascension are supplied for
that run, so they pass; the build, the build date, the content hash and the mod
environment are read from this installation, because those are the ones no host can
supply, and they still refuse.

The row this matters for is the ascension. A profile whose ceiling is below the
recording's does not stop the trainer constructing the run at the recording's
ascension - `EnvironmentPreflight.EvaluateAscensionCeiling` said so before there was
anything to offer, because a host constructing a run directly never consults that
ceiling. Reporting it as unmet would put a red row above an offer it does not stop,
which is a warning about nothing.

The profile note goes with the reading it describes. It names the profile the rows
were measured against, so it is shown only where a profile was read; over rows the
host supplied, it would send a player to import progress that nothing here consults.

**The fight is proved before it is handed over.**
`CombatStartEquality` compares the live state against both readings of the boundary:
every value the recording observed there, and the cached combat-start snapshot's
digest, which covers the run-persistent random streams and the draw pile's order that
no video can show. A boundary that disagrees on either abandons the run and says why.

## What it does not prove

**Nobody has played the fight.**
The journey above has never been watched running in the retail client. Its owner is
exercised end to end headlessly and its refusals are tested there; the mod's own side
of it — the launch through the game's continuation, the popup over the game's
screens, the deviation lock, the barrier under a real save path — is written and has
not been observed. `demo/RECORDED-FIGHT-ENTRY.md` says so where it would otherwise be
read as a claim.

**The watching screens are the game's popup, not the mockup's bar.**
The approved journey shows a chip and a control bar over the game's own screens. What
is built is the game's own popup with no backstop, so the screen underneath stays
lit, carrying the same three things and the same two controls. It uses only the
approved wording and the furniture this mod has already been seen to draw correctly;
the difference in layout is a deviation from the mockup and is recorded here rather
than presented as the design.

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
./scripts/arbiter enter-fight <manifest> # the journey into the recorded fight, without a scene tree
```

`install-mod.sh` is the one script in this repository that writes inside a Slay the Spire 2 installation.
Its final state is exactly `CombatTrainer` under the selected supported game mod directory, either `mods` or the game's Steam test-branch variant `mods_STEAMTEST`.
An upgrade stages the complete named file set in a temporary sibling there and replaces the old directory rather than overlaying it.
That is the game's own mod surface — the same location Steam Workshop installs into — and the game offers no user-data alternative, because it derives the path from its executable's location.

[demo/IN-GAME-HOST.md](../demo/IN-GAME-HOST.md) has the mod card and the eligibility
screen as they appear in the shipped client, and
[demo/RECORDED-FIGHT-ENTRY.md](../demo/RECORDED-FIGHT-ENTRY.md) has the journey into
the recorded fight with its real output.

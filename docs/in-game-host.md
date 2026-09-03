# The in-game host: what it proves, and what it does not

This is the mod that loads in the shipped Slay the Spire 2 client: S3 of
[the proof-of-concept path](proof-of-concept-path.md) answers one question — can this
game play the recorded fight? — S4 adds the button that enters it, and S5 captures the
fight the player then plays and shows it beside the recording's.

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
The installed build, discovered mods, and supplied in-memory progress model are inputs to the fight offer; the player's saved profile is not.
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
`RecordedFightEntry` in `Sts2PilotTrainer.Engine` constructs the run, makes the recording's decisions in order, and proves the fight at the end of them is the recorded one.
`RecordedFightRun` in this mod owns when each of those happens relative to the game's frames, what a player sees, and the retail-only deviation and lifecycle safety around the journey.
That split is why the same construction and boundary proof run on the command line with no scene tree: `./scripts/arbiter enter-fight`.

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
every value the recording observed there, and the manifest's engine-produced
combat-start snapshot digest, which covers the run-persistent random streams and the
draw pile's order that no video can show. A boundary that disagrees on either abandons the run and says why.

## The player's fight, captured and compared

**The capture is an observation, and the game's own executor is what it observes.**
Every action a player takes in a fight - a card, a potion, a discard, an ended turn -
reaches the engine as one of the game's own actions through `ActionExecutor`, which
announces each one before it runs and after it finishes.
`PlayerFightObserver` subscribes to those two announcements and to the combat
manager's own `TurnStarted` and `CombatEnded`, and to nothing else.
It issues no command and patches nothing; `FightCapture` in `Sts2PilotTrainer.Replay`
owns every rule about what the samples mean, and it is the same canonical projection
the headless arbiter samples, filtered by the same `ReplayTrace.Sample`.

**When the after-sample is taken is the one thing the observer owns.**
An action finishing is not the engine settling: a card's effects run on the queue
after the card's own action reports finished, and an ended turn hands the whole enemy
turn to the combat manager with the player's next turn beginning frames later.
So the after-sample waits for the moment the headless driver's drain reaches: the queue empty and the executor idle.
For an ended turn, it waits for the player's next `TurnStarted`.
The entire settlement wait is bounded to the headless driver's 30-second budget; if either the queue or executor does not settle in time, the capture becomes incomplete without taking an after-sample.
If the combat manager already regards the fight as over or ending, the sample is left to `CombatEnded`, which closes the open action with the final state.
That is how a capture completes at all: the killing blow, or the enemy turn the
player did not survive, is the action the fight ended inside.
Waiting uses only what the section below records as working here.

**A gap is refused, not bridged.**
The capture checks that the state an action begins from is the state the previous
one left.
A change no action accounts for makes the trace not a record of the fight, and the
panel then shows the capture's own sentence instead of a comparison.
A bridged gap would attribute its damage to nothing and the projection would quietly
under-count, which is exactly the plausible wrong answer this project refuses.

**The recording's side travels with the manifest.**
The client cannot replay - one process, one run, and it is the player's - so the
recording's line is produced headlessly by `./scripts/arbiter recorded-fight` and
embedded as `manifests/navegreed-OJ-6QXhNgdg.recorded-fight.json`.
`RecordedFight.Bind` refuses it at mod start unless its run id, its history hash
through the fight's end and its combat-start digest are the shipped manifest's, and a
test regenerates it in a fresh process and compares.

**The result uses the game's popup after the fight stops.**
For a completed fight, the screen is computed the moment `CombatEnded` fires and drawn two seconds later, over the loot on a win or the death screen on a loss.
Computed first on purpose: on a loss the game's own flow tears the run down on its way to the death screen, and the entry with it.
Leaving through the game's menu instead records the abandoned notice during cleanup and shows it over the main menu once the return finishes.
The panel is `FightResultScreen`'s approved wording - the summary as a table with the
player's column first, then the turn detail under its own heading, then the two notes -
and a row whose two sides differ is drawn plain while a row that agrees is dimmed.
That is the only visual distinction, because a difference is not a verdict.

**A card the game plays for the player is sampled as an action of its own.**
Hellraiser plays a Strike automatically when one is drawn, and in the client that play
reaches the executor as a card action like any other, so the capture records it as a
step; headlessly the same play resolves inside the ended turn that drew it.
The first retail session sampled twelve actions where the recording's history has
nine, for this reason.
Both are attributed to the turn they happened in, and the turn totals agree; the
difference is in how many steps a turn is made of, not in what happened.

**The result is text on a modal, and that is a recorded limit.**
The captain read the first retail comparison and reported that a text-led list of
differences on a large popup is not good enough for the next interface.
That is observed product evidence, kept here on purpose; the screenshot-backed
playback interface it points at is a follow-up owned by interface design, and nothing
in this host presumes its shape.

**Only a won, completed, uninterrupted fight is compared.**
A lost fight, a fight left through the game's own menu, a capture that could not be
completed and a comparison that refused each show one sentence and a Done button.
Done discards the run the way a refused entry does, and `RunManager.CleanUp` is what
lowers the write barrier on every one of those paths.

## What it does not prove

**A captured line is not a replayed one.**
The recording's line can be re-derived from its history in a fresh process; the
player's is what the capture saw of a fight that happened once.
What the headless test pins is that the recording's own actions, played through the
same capture, project to a line identical to the recording's replay on every field -
so a line that came through the capture and differed would be a defect in the capture
rather than a difference in the fight.

**The fight was played through and compared in the client by one person, once.**
[demo/PLAYER-FIGHT-COMPARISON.md](../demo/PLAYER-FIGHT-COMPARISON.md) has that
session.
It is one fight; the paths the panel takes for a loss, a quit and a refused capture are
proved on the game-free capture and screen, not in the client.

**Three of the recording's steps are screen commands, not engine ones.**
Each was found by running it, and each has the same shape: the engine call is the
middle of what a click does. A map move is `NMapScreen.TravelToMapCoord`, which fades
the screen around `RunManager.EnterMapCoord`; doing only the middle leaves the client
on the map with the next room built behind it. An event screen's continue is not in the
event model's option list at all and is driven through `NEventRoom.OptionButtonClicked`.
`GameScreenCommandTests` pins both, and `EventOption.IsProceed`, so a build that renames
one fails at build time rather than in a retail session.

**Waiting in this process is not what it looks like.**
Read this before writing anything here that waits. Nothing this mod ticks has ever been
observed running: an await on the scene tree's `ProcessFrame` signal, a delegate on that
signal's C# event, and a node's own `_Process` all leave a wait registered and never
resumed, with no timeout and nothing logged. Worse, a deferral that re-defers itself is
not a frame loop either - Godot drains its deferred queue until empty, so it spun seven
thousand times in eight seconds without the game drawing, and starved the fight it was
waiting for. Three things do work: awaiting a task the game itself completes,
`CallDeferred` once for end-of-frame, and awaiting a `SceneTreeTimer`. `RecordedFightRun`
uses only those.

**The deviation lock has to cover a whole step, not a call.**
A screen's command does most of its work after an await, so an authorisation that ended
when the starting call returned had already lapsed - and the lock refused the
recording's own map move. It is held across the step now.

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

**A profile reading describes the modded profile.**
The game forks a separate profile for modded play, and that is the one a modded session reads.
The screen names that profile only when a row was actually measured against it.
The fight offer instead asks about the supplied progress model used to construct the trainer run, so unlock, act, and ascension rows do not report a saved profile shortfall that cannot block the offer.

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
./scripts/arbiter enter-fight <manifest> --play   # and the fight played through the capture and compared
./scripts/arbiter recorded-fight <manifest> --out manifests/<id>.recorded-fight.json
                                         # regenerate the recording's shipped line after the manifest changes
```

`install-mod.sh` is the one script in this repository that writes inside a Slay the Spire 2 installation.
Its final state is exactly `CombatTrainer` under the selected supported game mod directory, either `mods` or the game's Steam test-branch variant `mods_STEAMTEST`.
An upgrade stages the complete named file set in a temporary sibling there and replaces the old directory rather than overlaying it.
That is the game's own mod surface — the same location Steam Workshop installs into — and the game offers no user-data alternative, because it derives the path from its executable's location.

[demo/IN-GAME-HOST.md](../demo/IN-GAME-HOST.md) has the mod card and the eligibility
screen as they appear in the shipped client,
[demo/RECORDED-FIGHT-ENTRY.md](../demo/RECORDED-FIGHT-ENTRY.md) has the journey into
the recorded fight with its real output, and
[demo/PLAYER-FIGHT-COMPARISON.md](../demo/PLAYER-FIGHT-COMPARISON.md) has the fight
played through and its comparison.

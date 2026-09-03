# The in-game host: what it proves, and what it does not

The mod a player installs is **Runmobile**, and the Combat Trainer is one module
inside it. `RunmobileMod` is the shell - the assembly resolver, the Harmony instance,
adopting the running game, the write barrier and the store - and `IRunmobileModule`
is the line between it and a feature. A module says whether it can run, installs its
own patches and contributes its own surfaces; one that cannot establish what it needs
is skipped by name in the game's log and the rest of the mod loads without it.
Drawing the singleplayer-menu cards is the shell's: a module contributes `MenuCard`
entries and `ModeCard` is a shell patch class, so a module that refuses cannot take
another enabled module's card down with it.
That promise is about a module which declares itself disabled: a module whose `Install` throws propagates out of the loop, aborts `Start` before the shell is marked started, and may leave its partial patches applied, which is a broken-build condition rather than a runtime one, and the failure-isolation lifecycle that would contain it arrives with the second module.
`CombatTrainerModule` is the only one built. The recorder and the run library are the
other two.

The retail proof below was gathered on the pre-rename `CombatTrainer` artifact.
S3 of [the proof-of-concept path](proof-of-concept-path.md) answers one question — can this game play the recorded fight? — S4 adds the button that enters it, and S5 captures the fight the player then plays and shows it beside the recording's.
That evidence establishes those Combat Trainer behaviors, but it establishes nothing about discovery, initialization or a complete session for the renamed `Runmobile` artifact.

## What it proves

**Retail loading of the renamed artifact is pending and not yet established.**
The build and installer produce `Runmobile` under the selected game mod directory with `Runmobile.json`, `Runmobile.dll`, and the four project-owned libraries the host uses: `Sts2PilotTrainer.Trainer.dll`, `Sts2PilotTrainer.Engine.dll`, `Sts2PilotTrainer.Replay.dll`, and `Sts2PilotTrainer.IO.dll`.
A retail session still has to show `Runmobile` in the game's mod list as the only enabled mod, show that the game's recursive scan discovers its manifest and `ModInitializerAttribute` initializes it, exercise the Combat Trainer through the renamed shell, and finish with a clean protected-files ledger outside `user://Runmobile/`.
Until that session is recorded, the existing `CombatTrainer` screenshots and ledger are not evidence for the renamed package.
The libraries are built to ship together; there is no separately installed framework or runtime dependency, and no resource pack.

**The eligibility answer comes from the same owner the arbiter uses.**
`Preflight.EvaluateLiveHost` reads this process's game and judges it through
`EnvironmentPreflight`, which has no game code and is tested on machines that do not
own the game.
The screen computes nothing: every row's state is a `PreflightField` the gate
produced, and every sentence about a failure is that field's own diagnostic, shown
word for word.

**It reads the game and never writes to it.**
The installed build, discovered mods, and supplied in-memory progress model are inputs to the fight offer; the player's saved profile is not.
The executable `adopt-live` boundary test verifies that a console process is refused without changing the prepared game inputs or sandbox profile.
The mod-manifest contract verifies that the shipped host is non-gameplay and carries no resource pack.
There is no source-reference scan presented as behavioural evidence.

**What it writes, it writes in one place.**
The mod now has files of its own - a player's own recordings, their progress through one, a derived boundary cache - and `user://Runmobile/` is where they go.

It is scoped the way the game scopes its own saves rather than flat: beneath `user://Runmobile/` sits the platform, account and profile scope the game resolved for itself, so `user://Runmobile/steam/<account>/profile1/`, and `.../modded/profile1/` for a modded session.
The scope is `UserDataPathProvider.GetProfileScopedBasePath` - the game's own answer - taken whole and re-rooted, never reassembled here from parts, so there is no second account-identity mechanism to drift.
Two accounts on one machine, and two profiles on one account, therefore do not share a library.
Those identifiers are local path scoping and nothing else: no platform directory, account id or profile number belongs in an exported manifest, an upload or a shared recording's identity.

`RunmobileStore` is the only thing in the mod that writes at all: it takes the root from the game's own `ProjectSettings.GlobalizePath`, requires that root to resolve inside `user://` by the same containment rule, so a `Runmobile` directory that is a symlink elsewhere is refused rather than followed out of the ledger's reach, checks every path against that root with `PathContainment.RequireContained`, refuses any path with a `Steam`, `steamapps` or `Slay the Spire 2` component, and writes a whole file through a temporary sibling and a move so a crash leaves the previous file rather than half of a new one.
`PrepareForWrite` is the containment gate rather than the atomic writer, because not every write is a whole file - the recorder appends to a journal - and the point is one place that decides where this mod may write, not one way of writing.
Each component is judged by the name it actually has on disk, after the path itself is resolved, so neither a symlink into an installation nor an alias spelling on a case-insensitive volume gets past it.
`Steam` is then matched exactly: the game's own user data has a lower-case `steam` platform level, which is where this store lives.
A traversal, an absolute path and a sibling directory whose name merely starts with the root's are all refused before anything is opened.
Nothing else ever goes there: not a save, a profile, run history, settings, an unlock, another mod's files or anything read out of the game.
It is not `ProfileWriteBarrier` and does not replace it - the barrier suppresses the *game's* writes while a trainer run is live, and has nothing to say about this mod's own files.
`./scripts/protected-files.sh snapshot <ledger>` and `compare <ledger>` are how that is measured rather than asserted: they hash everything under the game's user-data directory and its mods directory, read-only, and report added, removed and changed paths in three sections.
Protected files must not change, and one that did is the only thing that makes the comparison fail.
The `user://Runmobile/` subtree is the mod's own store, where a change is the mod working rather than the mod failing.
The third is the game's own churn - its log, its shader caches, its crash-reporter state - which is written on any launch with or without this mod; it is named in the script, always reported by name, and never hidden, and what establishes it is a control launch with no trainer run, the same control the first 154-file measurement used.
That first measurement was a hand-picked list of 154 files; this tool covers both roots whole, which on this machine is 352.

**It refuses rather than approximating.**
Every condition that would make a reading untrustworthy is a refusal with a sentence:
the game's startup phase, test mode, an empty model database, a content hash over
nothing, a build that cannot identify itself, a profile that is not loaded.
`./scripts/arbiter adopt-live` exercises that refusal from the command line, where a
console process is correctly not a running game.

A refusal also has to be about the right thing. The mod-environment gate tells this
host's own failure apart from somebody else's mod being present: a game whose only
active mod is Runmobile, failed, is told that Runmobile failed to load, not
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
against - `RecordedFightEntry.SuppliedProgressFor`, the same rule the construction
uses, which is the recorded player's own state where the recording carries one and
the complete state where it does not - so each row states a requirement of the fight
being offered rather than of a run nobody starts by hand. The unlocks, the acts and
the ascension are supplied for that run, so they pass; the build, the build date, the
content hash and the mod environment are read from this installation, because those
are the ones no host can supply, and they still refuse.

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
`BoundaryEquality` compares the live state against both readings of the boundary:
every value the recording observed there, and the digest on the manifest's
`combat_start` boundary for that fight, which covers the run-persistent random streams
and the draw pile's order that no video can show. A boundary that disagrees on either
abandons the run and says why. The rule is one rule for every boundary kind; only the
sentence a refusal is written in differs, because what a player is being told they did
not get differs.

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

**Two actions can begin with no frame between them, and that is now recorded rather
than refused.** In this client a number key selects a card and a click plays it, so a
click on End Turn while a card is held plays the card and ends the turn together. Seen
first as a refused fight: the card's after-sample had not been taken when the ended turn
began. The capture now closes the open action with the next one's before-sample where the
executor had already reported that action finished - the executor runs its actions in
order, so that sample is exactly the finished action's after-state and nothing is
invented. Where the open action had not finished the two genuinely overlap and the capture
refuses as it always did, and a change no action accounts for is still a gap either way.
Reproduced in the client before and after: the same gesture that refused a whole fight now
produces a comparison.

**The recording's side travels with the manifest.**
The client cannot replay - one process, one run, and it is the player's - so the
recording's line is produced headlessly by `./scripts/arbiter recorded-fight` and
embedded as `manifests/navegreed-OJ-6QXhNgdg.recorded-fights.json`.
`RecordedFights.Bind` refuses it at mod start unless its schema and run id are the
shipped manifest's, and then refuses per fight unless that fight's history hash
through its end and its combat-start digest are the manifest's boundary of the same
ordinal - so a file carrying five fights refuses on the one that drifted. A test
regenerates it in a fresh process and compares.

**The result is a panel this mod draws, after the fight stops.**
For a completed fight, the result is computed the moment `CombatEnded` fires and drawn two seconds later, over the loot on a win or the death screen on a loss.
Computed first on purpose: on a loss the game's own flow tears the run down on its way to the death screen, and the entry with it.
Leaving through the game's menu instead records the abandoned notice during cleanup and shows it over the main menu once the return finishes.
`FightResultPanel` draws it: the summary as figures in two columns, the turn chronology as the game's own card and potion art in the order they were played, and the chart of what each turn cost either side.
The two lines are told apart by colour and by the shape of their chart markers, and the same two colours run through the columns, the card borders and the lines, so a column, an icon and a line read as one fighter.
A figure whose two sides agree is dimmed and one that differs is not; that is the only emphasis there is, because a difference is not a verdict.
It is added into `NModalContainer` rather than built on `NGenericPopup`: the container's own backstop dims and blocks the screen underneath, and its `Clear` takes the panel away on every path that already clears a popup.
The panel is stock Godot nodes, because this assembly compiles without Godot's source generators and a `Control` subclass of ours would have no generated bridge - which is also what lets the whole panel be assembled and asserted on node by node in a process with no game.

**A card the game plays for the player is sampled as an action of its own.**
Hellraiser plays a Strike automatically when one is drawn, and in the client that play
reaches the executor as a card action like any other, so the capture records it as a
step; headlessly the same play resolves inside the ended turn that drew it.
The first retail session sampled twelve actions where the recording's history has
nine, for this reason.
Both are attributed to the turn they happened in, and the turn totals agree; the
difference is in how many steps a turn is made of, not in what happened.

**The chart is derived and never inferred.**
`FightResultChart` in `Sts2PilotTrainer.Trainer` reads it out of the comparison and
nothing else: one point per turn per line for each measure, one ceiling both plots and
both lines are drawn against, and the potions marked at the turn they were spent. A
turn one line never reached has no point on that line rather than a point on the axis,
and the chronology says so in the panel's own words - a zero there would claim the turn
was fought and cost nothing. A card play that carries no card id is refused rather than
drawn as a blank icon.

**The panel is not registered as the game's modal screen.**
`NModalContainer.Add` casts what it is given to `IScreenContext`, which is a game
interface a stock node cannot implement, so the panel is added as a child instead. The
container's backstop still blocks the mouse and the Done button is focused explicitly,
but `ActiveScreenContext` still regards the screen underneath as current. The retail
session drove the panel by mouse throughout and did not exercise a controller, so what
that means for one is still unmeasured.

**Two layout rules were learned on the screen rather than reasoned about.**
Both have the same shape, and both drew over half the panel before they were caught. A
Control's size is clamped up to its minimum size, so a label given its width before it is
told to wrap is widened back to its whole unwrapped line, and a texture rect given its
size before it is told to ignore its texture is grown to the size of the card art. Order
the calls the other way round. `FightResultPanelTests` pins both, against the longest
refusal this panel ever draws and against a card's own portrait.

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

**The fight was played through and compared in the client twice, once by a person and
once by an agent driving it.**
[demo/PLAYER-FIGHT-COMPARISON.md](../demo/PLAYER-FIGHT-COMPARISON.md) has the first,
where the recording's own line was played and every figure agreed;
[demo/VISUAL-COMPARISON.md](../demo/VISUAL-COMPARISON.md) has the second, where a
deliberately different line was played so the two sides of the panel differ.
The second session also produced, in the client for the first time, a fight left before it
ended and a capture that could not be completed.
A lost fight has still only been proved on the game-free capture and screen.

**A trainer run leaves nothing behind, and that is measured rather than argued.**
Over 154 files - every profile, progress, prefs, save, run-history and replay file, every
mod config, and every file of the other mods installed here - hashed before the mod was
installed and after the game was quit: all 154 byte identical, across entering the fight,
playing it, reading the result and leaving.

Getting there found two writes the barrier did not cover, each established by
reproduction. A clean launch and quit with no trainer run changes nothing, so neither was
the game's own ordinary behaviour.

The first is not a write at all. A run marks its own relics seen as it starts and its
rewards seen as they are offered; those calls only mutate the progress the game holds in
memory, so the barrier never saw them - and the mutation outlived the run. The game then
wrote it out itself at `NGame.Quit`, with no trainer run live, by a path the barrier must
not stop. It showed as a rotated `progress.save.backup` whose content happened to match,
because this profile had already seen the trainer's relic; on a profile that had not, the
same path writes a discovery the player never made. `SaveManager.MarkCardAsSeen`,
`MarkRelicAsSeen` and `MarkPotionAsSeen` are on the barrier's list for that reason: state
that will be written is a write that has not happened yet.

The second is the combat replay the engine writes at the end of every fight, into the
player's own profile directory, where it is the replay of the last combat they fought.
Suppressing `RunManager.WriteReplay`, which only hands the writer a path, left the file
changed anyway; the barrier covers `CombatReplayWriter.WriteReplay` itself.

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

**The watching journey is one long-lived transport, not a popup per step.**
The popup this started with was created and torn down around every decision, so it could not carry a position across the map-to-combat transition and it covered the screens the player is there to look at.
`PlaybackTransportStrip` replaces the whole set with one node, `PlaybackTransportDock` parents it to `NRun.GlobalUi` - the run's own persistent interface, which the room is swapped underneath - and `PlaybackTransport` in `Sts2PilotTrainer.Trainer` owns every word it says.
It docks in the band under the game's own top bar, measured off `NGlobalUi.TopBar` rather than written down, because that band is the one part of every screen this journey walks past that the game leaves empty.
`PrefightScreen` keeps only the two things a popup is actually for: a refusal, and the result of the player's fight.

**Reveal, hold, commit, and Back is none of them.**
`RecordedFightReveal` applies the game's own selected state to what the recording is about to choose and never its click path: `GrabFocus` is what a control's own `OnFocus` runs off, and on the map `NSelectionReticle.OnSelect` lights the ring directly so it survives the player moving focus to the transport.
The hold is the strip waiting - for the player under Forward, for a `SceneTreeTimer` under Play, shorter on the map because the game supplies a second of its own before the fade.
The commit is `RecordedFightEntry.AdvanceOneStep`, unchanged.
Back re-shows a decision already made from what the host wrote down at the moment it was revealed; there is no path that uncommits one, and the run is never rewound to answer.
A target the host cannot resolve - no screen, a coordinate this act does not draw, an option row granting a different relic - ends the attempt with the reason rather than committing a decision unseen.

What these surfaces should *look* like is not settled and is not recorded here; [mod-ui-direction.md](mod-ui-direction.md) carries that brief, and the strip's palette and layout are provisional until it is answered.

**The strip has to be reachable and has to be out of the way.**
Its root and everything on it except the buttons ignore the mouse, so the map, the event and the player's own fight keep every click that is not on a control.
Its buttons take focus, so a controller can reach them.
During the player's own fight it collapses to a chip carrying the trainer's name and nothing else.

**A green content-hash row is not environment parity.**
The row carries the engine's own sentence saying so, whether it is green or red.
The hash covers content contributed by mods that declare themselves gameplay-affecting; it says nothing about a mod that patches behaviour.
The same prerequisite reading therefore inspects every mod the game discovered, including failed states that may have left resources loaded, and refuses every active local mod except the known non-gameplay Runmobile host.

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

## Three more surfaces, and the hook each one needs

Read out of v0.111.0 in a scratch decompile, ahead of building anything on them.
Mechanism only: node paths and the lifecycle method a `[HarmonyPatch]` postfix would
follow, in the shape the mode card already uses. Nothing here is a decision about what
to draw.

**A card in the Compendium.** `NCompendiumSubmenu._Ready` is the hook. It resolves
every entry by Godot unique name: a top row of four `NShortSubmenuButton`s
(`%CardLibraryButton`, `%RelicCollectionButton`, `%PotionLabButton`, `%BestiaryButton`)
and a bottom row of three `NCompendiumBottomButton`s (`%LeaderboardsButton`,
`%StatisticsButton`, `%RunHistoryButton`), wires each one's
`NClickableControl.SignalName.Released`, and then assigns the focus neighbours
explicitly, index by index. A duplicated card therefore has to be added *and* joined to
that focus chain, or it will exist and be unreachable on a controller.
`NCompendiumSubmenu.OnSubmenuOpened` is the second hook, and the honest one for
visibility: the game hides `%LeaderboardsButton` there unconditionally and decides
`%RunHistoryButton` and `%BestiaryButton` per run, so a card that should only appear
when a recording is installed belongs there rather than in `_Ready`.

**The Settings screen's mods surface.** There is no Mods tab to extend.
`NSettingsTabManager._Ready` builds exactly four tabs, by plain node name -
`General`, `Graphics`, `Sound`, `Input` - pairing each `NSettingsTab` with a
`NSettingsPanel` resolved by unique name (`%GeneralSettings`, `%GraphicsSettings`,
`%SoundSettings`, `%InputSettings`), and connects each tab's
`NClickableControl.SignalName.Released` to its private `SwitchTabTo`. A fifth tab means
duplicating a tab node and a panel, adding both to the private `_tabs` dictionary, and
connecting to that private method - all three reflectively. What the game does have is
`NSettingsScreen._Ready`, which resolves `%ModdingButton` (an
`NOpenModdingScreenButton`) along with `%Modding` and `%ModdingDivider`, and makes them
visible only when modding is enabled. That is the game's own modding entry point and
the cheaper hook by a wide margin.

**A run-history entry's `Released`.** `NMapPointHistoryEntry` is an
`NClickableControl`, so it already emits `Released`; nothing in the game connects it.
The hook is `NMapPointHistoryEntry._Ready`, which is where the entry calls
`ConnectSignals` and resolves `%Icon`, `%Outline` and `%QuestIcon`. The entries are
built by `NActHistoryEntry.Create`, one per floor, under `NRunHistory`'s
`%MapPointHistory` in its `%Acts` container, and each carries a public `FloorNum` - the
floor identity a "play this fight" action would need - alongside its private
`MapPointHistoryEntry`, which is where the room type and encounter live.

## Running it

```bash
./scripts/build.sh                       # bootstrap the game assembly copy, build everything
./scripts/install-mod.sh                 # build the mod and install it into the game's mods directory
./scripts/install-mod.sh --uninstall     # remove it again
./scripts/protected-files.sh snapshot before.ledger   # hash everything the mod must not change
./scripts/protected-files.sh compare  before.ledger   # ... and say what a session changed
./scripts/arbiter adopt-live             # the refusal, from a process that is not a running game
./scripts/arbiter enter-fight <manifest> # the journey into the recorded fight, without a scene tree
./scripts/arbiter enter-fight <manifest> --play   # and the fight played through the capture and compared
./scripts/arbiter recorded-fight <manifest> --out manifests/<id>.recorded-fights.json
                                         # regenerate the recording's shipped lines after the manifest changes
```

`install-mod.sh` is the one script in this repository that writes inside a Slay the Spire 2 installation.
Its final state is exactly `Runmobile` under the selected supported game mod directory, either `mods` or the game's Steam test-branch variant `mods_STEAMTEST`.
It also removes a `CombatTrainer` directory left there by a build from before the rename, on install and on `--uninstall`, because two directories declaring this mod would be reported to the player as a duplicate.
An upgrade stages the complete named file set in a temporary sibling there and replaces the old directory rather than overlaying it; the pre-rename removal is part of that same replacement, so a failure anywhere in it leaves the previous installation as it was.
That is the game's own mod surface — the same location Steam Workshop installs into — and the game offers no user-data alternative, because it derives the path from its executable's location.

[demo/IN-GAME-HOST.md](../demo/IN-GAME-HOST.md) has the pre-rename mod card and eligibility
screen as they appeared in the shipped client,
[demo/RECORDED-FIGHT-ENTRY.md](../demo/RECORDED-FIGHT-ENTRY.md) has the journey into
the recorded fight with its real output, and
[demo/PLAYER-FIGHT-COMPARISON.md](../demo/PLAYER-FIGHT-COMPARISON.md) has the fight
played through and its comparison.

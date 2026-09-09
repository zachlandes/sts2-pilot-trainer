# The in-game host: what it proves, and what it does not

The mod a player installs is **Runmobile**, whose modules are recorded fights,
recording and the run library. `RunmobileMod` is the shell - the assembly resolver,
the Harmony instance, adopting the running game, the write barrier and the store -
and `IRunmobileModule` is the line between it and a feature. A module says whether
it can run, installs its own patches and contributes its own surfaces; one that
cannot establish what it needs is skipped by name in the game's log and the rest of
the mod loads without it.
`SingleplayerMenuRetention` is a shell patch because retention applies even when
nothing else can draw.
So is `CardScreensUp`, whose two patch classes count the card screens up in front of
the player: a screen being up is a fact about the game rather than about either
feature, and both settles read it - the recorder's, to keep a reading off a decision
somebody has not finished making, and the recorded-fight journey's, so a prompt a played card
opens does not spend the engine's budget. Behind one feature's patches it would stop
counting on a build that feature declines to watch, which is the build the other is
meant to carry on through. What a screen answered is a feature's own business, and a
module subscribes rather than patching the screens a second time.
That promise is about a module which declares itself disabled: a module whose `Install` throws propagates out of the loop, aborts `Start` before the shell is marked started, and may leave its partial patches applied, which is a broken-build condition rather than a runtime one, and the failure-isolation lifecycle that would contain it is still not built.
Shell ownership is paid for at the other end: `InstallShellPatches` treats a patch class Harmony cannot resolve as a broken build and lets it throw out of `Start`, so a game update that renames `NCardGridSelectionScreen.CardsSelected` or `NCardRewardSelectionScreen.OptionSelected` takes the whole mod down, where the same rename behind a module's own patches would disable only that module.
`RecordedFightModule` and `RecorderModule` are built. The run library is the third.

The retail proof below, up to and including S5, was gathered on the pre-rename `CombatTrainer` artifact.
S3 of [the proof-of-concept path](proof-of-concept-path.md) answers one question — can this game play the recorded fight? — S4 adds the button that enters it, and S5 captures the fight the player then plays and shows it beside the recording's.
That evidence establishes those Combat Trainer behaviors, and on its own establishes nothing about discovery, initialization or a complete session for the renamed `Runmobile` artifact.
S7's session did run the renamed shell, which is what establishes the row below; [demo/PLAYBACK-TRANSPORT.md](../demo/PLAYBACK-TRANSPORT.md) is that session.

## What it proves

**Retail loading of the renamed core artifact is established, mod list included.**
The build and installer produce `Runmobile` under the selected game mod directory with `Runmobile.json`, `Runmobile.pck` carrying the wagon icon shared by the mod list and Compendium entry, `Runmobile.dll`, the four project-owned libraries the host uses, and the self-contained local publication arbiter under `arbiter/`.
The S7 transport session predates the packaged arbiter: it installed the core Runmobile payload with `install-mod.sh`, launched the shipped client with it as the only enabled mod, and ran the whole watched journey through it.
That session establishes discovery, initialization and a complete journey through the renamed shell, and its protected-files ledger is clean outside `user://Runmobile/` apart from the mod's own installed assemblies, which carry the install's own timestamp; it does not establish the newer publication package in retail.
The game's own mod line naming `Runmobile` is photographed in that session's record, so the row no longer rests on the pre-rename `CombatTrainer` screenshots.
The libraries and arbiter are built to ship together; there is no separately installed framework or runtime dependency.
The one-resource pack contains only the wagon icon shared by the mod list and Compendium entry.

**It reads the game and never writes to it.**
The installed build, discovered mods, and supplied in-memory progress model are inputs to the fight offer; the player's saved profile is not.
The executable `adopt-live` boundary test verifies that a console process is refused without changing the prepared game inputs or sandbox profile.
The mod-manifest contract verifies that the shipped host is non-gameplay and declares its one-resource icon pack.
There is no source-reference scan presented as behavioural evidence.

**What it writes, it writes in one place.**
The mod now has files of its own - a player's own recordings, their progress through one, a derived boundary cache - and `user://Runmobile/` is where they go.

It is scoped the way the game scopes its own saves rather than flat: beneath `user://Runmobile/` sits the platform, account and profile scope the game resolved for itself, so `user://Runmobile/steam/<account>/profile1/`, and `.../modded/profile1/` for a modded session.
The scope is `UserDataPathProvider.GetProfileScopedBasePath` - the game's own answer - taken whole and re-rooted, never reassembled here from parts, so there is no second account-identity mechanism to drift.
Two accounts on one machine, and two profiles on one account, therefore do not share a library.
Those identifiers are local path scoping and nothing else: no platform directory, account id or profile number belongs in an exported manifest, an upload or a shared recording's identity.

`RunmobileStore` is the only thing in the mod that writes at all: it takes the root from the game's own `ProjectSettings.GlobalizePath`, requires that root to resolve inside `user://` by the same containment rule, so a `Runmobile` directory that is a symlink elsewhere is refused rather than followed out of the ledger's reach, checks every path against that root with `PathContainment.RequireContained`, refuses any path with a `Steam`, `steamapps` or `Slay the Spire 2` component, and writes a whole file through a temporary sibling and a move so a crash leaves the previous file rather than half of a new one.
Removing is a write and goes through the same gate: `RunmobileStore.Remove` names one file at a time and refuses a directory, so the one operation here that cannot be undone can never reach the store's own root.
Reading goes through it too, `RunmobileStore.SizeOf` included - the settings row's size figure is a sum over entries the store named, not over a directory somebody listed.
Which files it is asked for is `RecordingRetention`'s, deliberately somewhere else - see "Keeping runs, and removing them" below.
One call in the mod reaches a game API that can write inside the player's *save* directory, and it is `ContinuableRun`: `SaveManager.LoadRunSave` goes through `MigrationManager.LoadSave`, which renames a corrupt run save to a `.corrupt` path and leaves a `.pre-repair` sibling where it repairs JSON.
It is safe here for a reason that is about *when* it is called rather than what it does, and that reason is written out in `ContinuableRun`'s own docstring; a second caller does not inherit it.

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

Suppressing `SaveProgressFile` is not the whole of it, because not every progress write goes through it.
`ProgressSaveManager.MarkFtueAsComplete` writes the progress file itself, so `MarkFtueAsComplete`, `SetFtuesEnabled` and `ResetFtues` are named in `ProfileWriteBarrier.SuppressedWrites` directly.
The trainer's own path reaches the tutorial marks from `NMapScreen` and `NEndTurnButton` among others, and it measured clean only because every profile it was measured on had tutorials off - `SeenFtue` short-circuits when they are.
A player who left them on, which is the default, is the case that was never run.
One consequence of suppressing the siblings is deliberate: the settings screen's reset-tutorials does nothing while a trainer run is live.
A second is deliberate too: suppressing `MarkFtueAsComplete` suppresses the in-memory mark with it, so `SeenFtue` keeps returning false for the whole trainer run and a tutorial the player has not already dismissed can show again later in the same journey.
It is bounded: `SeenFtue` reads `FtueCompleted` as loaded from the player's own progress file, and the barrier only stops additions to it, so a tutorial they dismissed in ordinary play never reappears.
The fix has to answer `SeenFtue` true for the duration of a trainer run without writing or leaving a mark in the player's stored `Progress`: a run-scoped overlay dropped when the run ends.
Marking it in the real `Progress` object would not do, because that mark survives the run, and the next ordinary write after the barrier lowers - `NGame.Quit` calling `SaveProgressFile` - would persist a tutorial mark made inside somebody else's run, the same measured sequence the seen-marks record.
That is why it is a new mechanism rather than a named write, and it is left to a separate change.
`NGameOverScreen` is the known gap, recorded in the barrier's own docstring: it dirties `Progress.CurrentScore` and the badge state in memory before a `SaveProgressFile` the barrier suppresses, so the mutation outlives the run and the next ordinary write persists it.
Nothing reaches it while the trainer is fight-scoped; a whole-run replay would, and it is answered on that list when it does.

**The recording owns every decision before the fight.**
Enforced where those decisions change the run: `EventSynchronizer.ChooseLocalOption`, `RunManager.EnterMapCoord`, and the card-click handlers on both deck-selection screens.
The first two are commands; the card screen has no engine command, so its own handler is the mutation point.
Patching those points rather than the buttons that usually reach them keeps a controller, a hotkey or another mod from driving a hidden control around the lock.

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
The entire settlement wait is bounded to the headless driver's 30-second budget, and that budget measures only the engine's own time: while a card screen the action opened is up in front of the player the budget is discarded, and the engine gets the whole of it again from the moment the last screen closes, however many one action puts up.
If either the queue or executor does not settle in time, the capture becomes incomplete without taking an after-sample.
The wait is `RunRecorder.WaitForTheEngine`, asked here through `PlayerFightObserver.WaitUntilSettled`, so the fight and the run settle by one rule; the screen count it reads is the shell's `CardScreensUp`.
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

**A recorded comparison line is bound to one manifest, and native recordings do not have one yet.**
The client cannot replay - one process, one run, and it is the player's - so the included reconstruction's line is produced headlessly by `./scripts/arbiter recorded-fight` and embedded as `manifests/navegreed-OJ-6QXhNgdg.recorded-fights.json`.
`RecordedFights.Bind` refuses it at mod start unless its schema and run id are the shipped manifest's, and then refuses per fight unless that fight's history hash through its end and its combat-start digest are the manifest's boundary of the same ordinal - so a file carrying five fights refuses on the one that drifted.
A test regenerates it in a fresh process and compares.
Before `RecordedFightRun` reads a projection after a fight, it requires that embedded file to name the run being played and contain that fight ordinal.
A native recording has no matching line in this build, so its result screen states that there is no recorded comparison line for the run yet; it never reads the included reconstruction's line and never names that other run in the notice.
Producing a native recording's comparison line is deliberately deferred rather than inferred from the captured fight.

**The result surface is drawn only when asked for.**
For a completed fight, the result is computed the moment `CombatEnded` fires and held; two seconds later, once the game has drawn its ending, the journey moves to `Ended` and the post-fight choice opens under the chip - Show the comparison, Fight it again, Leave - in place of a result drawn unbidden.
`PostFightChoice` in `Sts2PilotTrainer.Trainer` derives the rows and `RecordedFightRun.ChoosePostFight` presses them; the first draws the comparison panel when the run has a bound recorded line or the no-line notice when it does not, and Done returns to the choice.
Where a recorded line exists, a lost fight is compared, Lost against Won: the projection treats a defeat as finished and the comparison carries the outcome row, so defeat alone never substitutes a notice for that comparison.
Computed first on purpose, and held apart from the run in `RecordedFightRun.EndedFight`: on a loss the game's own flow may tear the run down on its way to its ending, and the choice is owed either way.
`TrainerRunTeardown` therefore releases the run and keeps the ended fight when the fight is over, and the return to the main menu - the choice's Leave or the game's own Continue on its ending - is what finishes the journey.
The loot a won fight offers stays visible under the choice and `LootLock` keeps it where it is: the reward buttons and the game's proceed are prefixed to do nothing while the fight is over, because those rows are the recording's next decisions and the choice is the only way on.
Whether the run's persistent interface survives the game's ending decides where the choice is drawn - under the chip where it does, in the game's modal container where it does not - and `RecordedFightRun.OfferTheChoice` reads that live and logs which happened rather than assuming either.
What the sitting remembers is `RecordedFightModule`'s in-memory set of (run, fight) pairs shown this sitting, written nowhere: it draws the dot on a post-fight row already taken and the hollow-eye mark on the fight's row in the run view, and gates nothing.
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

## Recording the player's own run

**The recorder is that observer widened to a whole run, and it shares its parts.**
`RunRecorder` in `Sts2PilotTrainer.Mod` attaches when a run starts, watches every decision the player makes, and writes a v6 native manifest under `user://Runmobile/recordings/` when the run ends.
Inside a fight it hands the run to the same `PlayerFightObserver` the recorded-fight journey uses, through `IFightSampleSink` in `Sts2PilotTrainer.Replay`: the recorded-fight journey's sink is a `FightCapture` and the recorder's is an adapter onto the `RunCapture` that keeps the whole run.
There is one observer, one settle rule and one set of rules about what a sample means, whichever feature is watching.
The one question the two sinks answer differently is an action whose argument the observer could not resolve, which is why `IFightSampleSink` asks it rather than the observer deciding: a history missing an argument the format requires is a run nobody can replay, so the recorder refuses and keeps nothing for that action, while a fight being compared never reads that argument and the capture keeps the step.

**It records singleplayer runs only, and the reading is taken rather than the member's name trusted.**
The recorder attaches through `RunManager.SetUpNewSingleplayer` and `SetUpSavedSingleplayer`, and a member whose name says "singleplayer" is a claim about what that member is for rather than a reading of what this session became.
`LiveRun.ReadSession` is the reading: `NetService.Type` for the network game, and the run's player count for the half `RunManager.IsSingleplayerOrFakeMultiplayer` folds together.
`RunSession` in `Sts2PilotTrainer.Replay` owns what a `RunSessionKind` permits, so both rules are checkable without the game.
Two players sharing one client is a multiplayer run here even though the game's own property calls it singleplayer: what that property is about is whether anything goes over a wire, and what this is about is whose decisions the history holds.
A reading that could not be taken is refused exactly like a multiplayer one - a run nothing established anything about is not a singleplayer run.

**A multiplayer game gets no mod surface at all, which is a stronger rule than recording nothing.**
`GameSessionWatch` is the shell's, installed however the modules answer, and `RunmobileMod.MayDraw` is the one gate every surface passes: the menu cards go through it, and so does a surface a module draws from its own Harmony patches, which is where the cards' gate never looks.
The run library's four are all of that kind - `MainMenuLibraryRow.Visible` decides whether its main-menu row is there, `CompendiumCard.ShowsButton` decides whether the Compendium button is there, `MyRunsSettings.AddRow` decides whether its settings row is added, and `RunHistoryPlateHost.PlateFor` decides whether a history-row press opens anything - and each asks the shell rather than reading the session, so a module knows only that the shell said no.
What a module may not do is read the session to decide whether to draw; the reading behind that decision is the shell's and the module's question is "may I draw this".
Reading it to decide whether to *record* is a different question with a different answer, and `RunRecorder.Attach` asks it directly through `GameSessionWatch.Observed` and `RunSession.MayBeRecorded` - the two permissions are separate, which is why `RunSession` carries both.
Silence is silence rather than a degraded surface: nothing is drawn, not even a refusal saying why, because a refusal is itself this mod speaking.
An indicator saying a run is *not* being recorded would still be this mod drawing in a game somebody else is also playing, and one of them never installed it - so the suppression is of every surface rather than of the recorder.
It is two observations rather than one. `LiveRun.ReadSession` reads the run in progress; the two patches on `RunManager.SetUpNewMultiplayer` and `SetUpSavedMultiplayer` latch the moment the game is asked to set a multiplayer session up, which covers the stretch where there is nothing yet to read - continuing a saved multiplayer run is asynchronous, and the method that starts it returns long before the run exists.
The latch is cleared by `RunManager.CleanUp`, because a client that played a multiplayer game and then started a singleplayer one is in a singleplayer game.
Nothing is recorded because the reading at attach refuses before a journal exists, so no recording is begun and no artifact is created: the latch is the whole of the safety net.
It is set from a prefix on purpose - being early is what closes the window - and a setup the game then refuses would strand it, because the `CleanUp` that clears it only happens where a run existed; `GameSessionWatch.MultiplayerSetupRefused`, a finalizer on `SetUpNewMultiplayer`, is the way back, and the saved setup has none because it is asynchronous and carries its refusal in the task it returns.
What this build cannot do is mark a recording that is already live: nothing here reaches one mid-run, and no path was found that creates a partial artifact, so that is a limit rather than a guarantee.
The shell has no refusal to fall back on the way `RecorderModule` does, so `GameSessionWatchTests` asserts every member it attaches to is on this build rather than assuming it.

**A run the console was used in is kept, complete, and never publishable.**
Installing this mod turns the game's full console on: `NDevConsole` reads `ModManager.IsRunningModded()` when it decides whether to register the debug commands, so this is a reachable state in an ordinary modded session rather than a developer-only one.
`RunCapture.MarkNonStandard` sets `source.native.integrity = "non-standard"` and changes nothing else - not the state, not the continuity, not the history - and the validator refuses the manifest for publication where the field says anything but `complete`.
The recording is still written to `user://Runmobile/recordings/`, because it is what the player played; what it is not is evidence anybody else can act on, since what a console command did to the state is not among the decisions the history holds.
From format v6 the field is required, and `unmapped` is its third value: a recorder that met a decision it could not name stops there through `RunCapture.MarkUnmapped`, writes what it met to the journal as a stop line and to the manifest as `source.native.unmapped`, and records nothing past it - a prefix that skipped a decision and carried on would replay into a run that never made it.
A version-5 recording states no integrity; the migration reads it as `complete` and says so in `migrated_from_version`, because a version-5 recorder refused rather than stopping at anything it could not name.
The runtime that decides a decision is unmapped is not in this build; the format, the capture and the journal line are, so that build changes no file shape.

**Every decision is read either side.**
The journal's schema is v2 and every decision line carries `before` and `before_digest` beside the settled `state` and `digest`: the reading taken in the prefix of the member the decision went through, which is the state the player made it from and the instant a comparison at verification asks about.
Inside a fight that reading is the observer's before-sample and coincides with the previous after-sample, and `FightCapture` still refuses a gap between them; outside one, the reward screen after a fight is generated on the client's clock between two decisions, so `RunCapture` carries the reading that was taken rather than refusing the gap.
A v1 journal on a player's disk is refused on resume, exactly as any schema this build does not read is, and the run is simply not continued as a recording.

**The seam is the console's own funnel, not the action queue.**
`DevConsole` has two public entries - `ProcessCommand(string)` for a command typed on this client and `ProcessNetCommand` for one a peer sent - and both reach the private three-argument `ProcessCommand`, so one patch covers both, for the same reason `SkipRewardsSet` is watched rather than the call the driver makes.
The queue is deliberately not it. A console command reaches the queue only in a networked game, and as two types rather than one: the game's own generated `INetActionSubtypes` list holds the eleven `Net*` structs, of which the console's is `NetConsoleCmdGameAction`, and the action it builds and puts on the queue is `ConsoleCmdGameAction`. In singleplayer `DevConsole.ProcessCommand` takes its local branch and builds neither.
Since singleplayer is the only kind of run this records, a watch on the queue would have seen a console command in exactly the runs that are never recorded and in none of the runs that are.
Every command the console accepted counts, read off the game's own `CmdResult.success` so a typo is not one; a command that only printed something counts too, because which of the game's commands change a run is not a judgement this mod is in a position to make and the cheap direction to be wrong in is the one that keeps the recording on the player's disk and off the gate.
A command used between the run starting and the recorder attaching is held and applied to the capture at attach, because it is in that run's history and nothing later could recover it - held unconditionally rather than only while there is a run to read, since continuing a saved run is asynchronous and there is nothing to read for the whole of that load.
The hold is cleared when the next run starts, so a command typed at the main menu is never carried into the run that follows it.
Nothing the player typed is kept: the journal line carries the mark and nothing else, `RunJournal.NonStandard` reads back as whether the mark is present, and `source.native.integrity` is the whole of what a recording states.

**What it watches is `EngineCommands` read from the other end.**
The driver calls those members to make a recorded decision; a player clicking makes the game call the same members.
`RunRecorder.RecordedVerbs` has to equal the table's mapped set, and `RunRecorderTests` asserts it - a verb one side has and the other does not is either a recording nothing can replay or a replay of a decision nothing can record, and both are silent until somebody tries.

**Every patch reads and returns.**
Nothing here issues a command, changes an argument, or changes what the game decides.
Arguments are read in a prefix, while the shelf still holds the thing that was bought and the hand still holds the card that was played; the state is read at the other end of a settle.
The one exception to "postfix and return" is the two card screens, whose returned task is handed back unchanged having been looked at on the way past: the engine pulls a card screen's answer through a seam the player's client fills, so there is no command anything else could observe.
Those two patches are the shell's `CardScreensUp` rather than the recorder's, because the count they keep is read by both settles; the recorder subscribes to what a screen answered, which is its own business and nobody else's.
`NCardGridSelectionScreen.CardsSelected` covers every screen over a deck, a pile or the hand, because they share that base and it holds both halves of the answer - the list offered and the cards that came back.
`NCardRewardSelectionScreen.OptionSelected` covers the card reward, whose screen answers with a position into the list `ShowScreen` was given.
A subscriber owns what its own failure means: an answer the recorder cannot read marks the recording broken and writes the reason into the journal, and the shell's own catch around a subscriber is there only so one of them cannot break the game's card-screen path or stop another running.

**The recorder never raises the write barrier.**
The player's own run saves normally; suppressing that would take the run away from them in order to describe it.
The barrier is the other direction: while a trainer run is live it is raised, and the recorder declines to attach at all - a trainer run is this mod's own construction, not the player's, and recording it would publish somebody else's recording back as the player's own.
`RunRecorderTests` checks that by doing it rather than by asserting a comment.

**A run is identified by its seed and the moment it began, and both survive a reload.**
`LiveRun.RunStartedUtc` reads the game's own run start time, so a session continued tomorrow resolves to the journal it was being written into.
That is also why there is one attach path for a new run and for a continued one: which it is is not the recorder's question, and the answer is whether a journal for that run is already on disk.
Nothing in a recording's name says whose game it was.

**The journal is what survives a crash.**
`RunJournal` is a header and one line per decision, appended as the run is played, so finishing a write means finishing a line.
A crash leaves a prefix that is a real recording of the part of the run that happened rather than half of a document describing all of it, and `RunJournal.Parse` drops a truncated final line and refuses anything else.
A crash mid-append also leaves that final line with no newline after it, so before the resumed session appends anything `RunJournal.RepairTruncatedTail` brings the file back to a line boundary: a record that lost only its newline is terminated, a fragment is cut back to where it began, and nothing before it is touched.
Appending onto an unterminated line instead fuses two records into one unreadable line, which the writing session can still resume past - it is last - and every session after it cannot, because one more decision leaves that line in the middle where `Parse`'s truncation rule does not apply.
A crash that cost one decision would then cost every decision after it.
Whether that final line is a record or a fragment is one predicate both the repair and `Parse` ask, so the repair cannot delete what the reader would have kept, and a fragment cut back marks the recording broken with the decision it lost named.
On resume, `RunCapture.Resume` rebuilds the capture from the journal and compares the state the game came back in against the state the journal last recorded.
Equal means nothing happened in between that the recorder missed.
The one unequal state that remains continuous is the game's own save behavior after a quit during a live fight: the live digest must equal that same open fight's room-entry decision in the journal, and the journal must contain decisions observed after it.
Those decisions remain in an append-only rollback record and in `source.native.discarded`, while the replayable history resumes at the room-entry decision; the fact that Continue was pressed establishes none of this.
For publication, `gate` replays every discarded branch from that verified room-entry state and requires its final engine state to match the final state the recorder captured.
Any other mismatch marks the recording `continuity = broken` and it is refused for publication, because a history missing decisions replays into a different run while every value in it is individually true.
Every rollback and refusal is appended to the journal as a line of its own, the moment it is established, and `Resume` applies each one back.
Without that the break lives only in the session that decided on it: quit and continue once more and the next session finds a journal whose last digest is exactly the live one, sees nothing wrong, and publishes `continuity = continuous` over a hole - which is the one claim nothing downstream could check.

**Where the recorder stops, it says so.**
A reward kind the format has no verb for, a card reward answered with one of its alternatives, a screen whose offered list this build no longer exposes, an engine that did not settle: each marks the recording broken with a sentence rather than writing a value it guessed.
The recording is still written, because it is what happened; what it is not is publishable, and the validator and `./scripts/arbiter gate` are what say so.

**An integrity claim is a reading, and four of them were assumptions before review caught them.**
`AGENTS.md`'s recorder invariant states the rule; these are what it is made of, because a rule with no instances is one nobody can check themselves against.

- **A verdict about controls that were never applied.** `gate`'s `rejection` condition runs `negative-controls --require-all-controls`, and three of the ten controls damage a decision only where the history nominates the alternative they take. The recorder wrote none of them, so no native recording could ever pass - and two of the three that did apply were satisfied by the driver refusing on argument shape rather than on the run diverging, which is a control counted as rejected having demonstrated nothing. The recorder derives all four nominations now, from what the decision itself offered, and omits one where the decision genuinely had no alternative.
- **A mod list wider than the rule that reads it.** `EnvironmentPreflight` refuses a native recording if any mod in it declares itself gameplay-affecting, while the sibling rule about the same installation drops the disabled ones first. The recorder wrote every mod the game had discovered, so a recording could be refused for a mod that never loaded and never touched the run. `LocalMod.Loaded` is the one place that decides now, and both rules ask it.
- **A continuous watch of a fight nobody was watching.** A session continued while the last recorded decision left a fight live came back with that fight still open in the capture and no observer attached, so every card play and ended turn left in it went unrecorded while the recording still reported `continuity = continuous`. The recorder asks whether a fight needs watching the moment it attaches, and refuses the recording where it holds one open and cannot watch it.

- **A build read off a copy on disk.** `LiveRun.ReadIdentity` reads the running client only once the engine layer has adopted it, and otherwise falls back to `build/lib/prepared-assembly.json`. The recorder relied on the mode card having adopted the game first, so a build on which the Combat Trainer refused - contributing no card, and the recorder contributes none - left the recorder writing a build, build date and content hash captured from a prepared copy on the developer's disk into a manifest that says it read them off this client. The recorder asks `EnsureAdopted` itself now and declines the run where it cannot.

Each was a component reporting a state it had not established, and in each the recording read as trustworthy precisely because every individual value in it was true.

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

**An await here can outlive the journey that started it, and two kinds of continuation
need opposite treatment.** The player can abandon from the game's own pause menu on any
frame, and the teardown clears the entry, so a continuation can wake into a different
journey or into none. One that would act on a run - move it, reveal on it, hand a fight
over, attach a surface, or refuse - must stop, and stop silently: `RecordedFightRun`'s
own `StillOurs` is the single predicate for that, because the expression it replaced
compared references alone and two nulls compare equal, so an ended journey looked
current. One that only delivers something already computed must not stop; the result of
the player's fight is computed before its wait for exactly that reason, since on a loss
the game's own flow tears the run down during the wait and a guard there would drop the
comparison the fight was played for.

**The deviation lock has to cover a whole step, not a call.**
A screen's command does most of its work after an await, so an authorisation that ended
when the starting call returned had already lapsed - and the lock refused the
recording's own map move. It is held across the step now.

**The watching journey is one long-lived tag, not a popup per step.**
The popup this started with was created and torn down around every decision, so it could not carry a position across the map-to-combat transition and it covered the screens the player is there to look at.
`PlaybackTransportStrip` replaces the whole set with one node, `PlaybackTransportDock` parents it to `NRun.GlobalUi` - the run's own persistent interface, which the room is swapped underneath - and `PlaybackTransport` in `Sts2PilotTrainer.Trainer` owns every word it says.
It hangs from an anchor measured off the game's own furniture: the bottom of the top bar's HP and gold widgets, and the right edge of the deck button.
Both halves are load-bearing. `NTopBar` is a full-screen control whose rect ends at the bottom of the viewport, so measuring the node itself puts the tag off the screen; and the band's left carries the run's relic inventory, which grows, so a centred or left-hung surface covers relics by about the ninth one.
`PrefightScreen` keeps only the two things a popup is actually for: a refusal, and the result of the player's fight.

**Consider, reveal, commit, and Back is none of them.**
`RecordedFightReveal.Arrive` establishes that the screen has arrived and holds the thing the recording chose, lights nothing, and answers how many options the screen offers; `RecordedFightRun.ArriveNext` puts the transport on the arrived screen with nothing lit, and that is the consider hold.
A screen with one option has no consider hold and arrival goes straight to the reveal; the count is the map's own `Travelable` points or the event layout's option buttons, read on arrival.
`RecordedFightReveal.Reveal` then applies the game's own selected state to what the recording is about to choose and never its click path: `GrabFocus` is what a control's own `OnFocus` runs off, and on the map `NSelectionReticle.OnSelect` lights the ring directly so it survives the player moving focus to the transport.
Each hold is the strip waiting - for the player under Forward, for a `SceneTreeTimer` under Play, shorter on the map because the game supplies a second of its own before the fade - so Forward is pressed twice per decision and under Play the two holds drain one after the other on the same timer.
`Relight` puts a reveal back only once there has been one; during the consider hold pressing a control lights nothing.
The commit is `RecordedFightEntry.AdvanceOneStep`, unchanged.
Back re-shows a decision already made from what the host wrote down at the moment it was revealed; there is no path that uncommits one, and the run is never rewound to answer.
A target the host cannot resolve - no screen, a coordinate this act does not draw, an option row granting a different relic - ends the attempt with the reason rather than committing a decision unseen.

What these surfaces look like is [mod-ui-direction.md](mod-ui-direction.md); this file records only how they behave.

**A field can stop the whole mod loading, one phase before any of its code runs.**
The game finds the initializer by enumerating this assembly's types, which happens before `SiblingAssemblies` has taught the runtime that `Sts2PilotTrainer.*` sit beside the mod rather than beside the game.
Enumerating a type resolves the types its fields are *built from*, so a field built from a sibling resolves that sibling, fails, and the mod loads not at all with a `ReflectionTypeLoadException` naming it.

**The rule, stated so a new field can be checked against it rather than compared to a previous casualty.**
A field's type may *be* a sibling type - a plain reference is a pointer and several have always existed.
A field's type may not be a *generic type built over* a sibling: not inside a nullable, not inside a tuple, not as a delegate's type argument, not as a collection's element.
Hold that state as a plain reference, or as an `int` you cast back on use, and read the real thing where you need it.
`_speedIndex`, `RecordedFightRun._phase`, `PlaybackTransportStrip._openMenu` and the tag's tooltip fields are all that shape, and each says so where it is declared.
A module initializer does not rescue this, measured: type enumeration does not trigger one.

**`ModAssemblyLoadOrderTests` is the arbiter, not this paragraph.**
It loads the built mod in a context that refuses to resolve the siblings and calls `Module.GetTypes()`, which is exactly what the game does at that moment, and it names the loader's own complaint when it fails.
Run it rather than reasoning about a field's shape; it takes a second and it fails the way the game fails.
It exists because this trap has fired three times - `IReadOnlyList<MenuRow>` cost a startup, a `PlaybackSpeed` field is why `_speedIndex` is an `int`, and a `(Control, Func<ElementSurface>)?` field reached a green pull request with passing CI and a mod that loaded not at all.
The comment warning about the trap was fifteen lines from that third field. An accurate comment that has to be recognised is not a check.

**The strip has to be reachable and has to be out of the way.**
Its root and everything on it except the buttons ignore the mouse, so the map, the event and the player's own fight keep every click that is not on a control.
Its buttons take focus, so a controller can reach them.
During the player's own fight it collapses to a chip carrying the mark and the creator's name, silent until it is pressed and offering two directions when it is.
Silent and pressable at once is a distinction the strip has to be able to make: a Godot control that is not visible receives no input, so a chip drawn by hiding the tag's controls has nothing left that can be pressed.
`Presence.Silent` is what says it - present, taking input over the whole plate, drawing nothing but the hover and focus rim.

**A green content-hash row is not environment parity.**
The row carries the engine's own sentence saying so, whether it is green or red.
The hash covers content contributed by mods that declare themselves gameplay-affecting; it says nothing about a mod that patches behaviour.
The same prerequisite reading therefore inspects every mod the game discovered, including failed states that may have left resources loaded, and refuses every active local mod except the known non-gameplay Runmobile host.
What a mod actually patched is a separate reading, taken from Harmony rather than from anybody's manifest and captured into a recording as `environment.mods.patch_roster`; [environment-identity.md](environment-identity.md) owns it and the two refusals over it.

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
adopts the running game later, once startup has finished.
`EngineHost.AdoptRunningGame` refuses unless the game's own startup phase says
otherwise.
`RunmobileMod.EnsureAdopted` is the mod's one adoption entry and remembers its answer,
and every feature that reads the engine asks it for itself at the first moment it
demonstrably has a running game - the singleplayer menu for the mode card, the recorder
when a run has entered its first room. No feature's correctness rests on another having
asked first, and a refusal is the caller's to act on: no mode card, and no recording.
The singleplayer-menu postfix asks for adoption only where a module contributed a card
it is about to draw, so on a build where every module declined it is never reached from
there at all. The retention duty above it is: that runs first and unconditionally, so a
purge is honoured on such a build, and on one where the adoption it does attempt is
refused.

**Godot does not load the game into the default load context.**
A mod's sibling assemblies have to be resolved on the load context the mod itself was
loaded into. Resolving them on `AssemblyLoadContext.Default` succeeds, and then their
reference to `sts2` is satisfied by the runtime probing the game's assembly file
again — a second copy, with the right path and an empty world. Everything reads
plausibly and nothing is initialised. Every refusal now names the assembly it read
and says when more than one is loaded, because "the game says no" is only meaningful
once it is clear which game was asked.

## Two more the client found, both about when rather than what

Neither of these is visible in a process that never draws a frame, and both were live in
a build whose tests were green.

**A wait for a length of time is not a wait for a thing to happen.**
The map move's own engine task completes when the combat room is built, and the opening
hand is dealt over the frames after that. The hand-over waited a flat two seconds and
then read the boundary whatever the game was doing with them. On one machine two seconds
landed inside the Battle Start banner: the boundary read one card of the recording's five
in hand and ten of its six in the draw pile, and refused a correct entry - twice,
deterministically. `RecordedFightEntry.IsReadyForThePlayer` already existed for exactly
this, carried a comment saying exactly this, and nothing called it. The wait now polls
it, and the old constant is the deadline rather than the answer. The general rule, which
`PlayerFightObserver` already followed and this did not: wait for the engine's own signal
and keep the budget for giving up.
A deadline that is reached refuses on its own rather than reading the boundary anyway, and which of the two refuses decides whether the sentence is true.
The boundary compares card by card, so a half-open fight is refused with the recording's five-card hand against the one card dealt so far - true about the comparison, and it reads to a player as a broken recording.
The timeout's own refusal says the fight did not finish opening and nothing more; `DescribeCombatReadiness`'s reading - the room, the combat manager, the player's combat state and the turn - is logged at the moment the wait gave up, because that reading is for whoever is diagnosing it and not for the player.

**Returning to the main menu frees the popup that explains why.**
A refusal is the one thing this journey says in a popup, and the popup lives in the
game's own modal container. `Abandon` put it up and then called `NGame.ReturnToMainMenu`,
which takes the container's contents with it - so the refusal was created, freed with the
run it was explaining, and the client's own deferred focus grab threw
`ObjectDisposedException` on a disposed button. The player was returned to the main menu
with no account of what had happened at all, and nothing in the mod's own log said the
popup had failed, because it had not: it was shown and then destroyed. The return is now
awaited - it is a `Task` - and the refusal goes up on the far side of it.

## Three the recorder's own evidence run found, all in what it watched

None of these is visible without playing a run, and all three were live on a branch whose
suite was green and whose review had run twelve rounds.
Together they meant no recording the recorder made could reproduce, so `gate` could never
return `PUBLISHABLE` for one - the phase's own completion criterion.

**The same "wait for a thing, not a length of time" trap, in the other half.**
The section above records it for `RecordedFightEntry`, which now polls
`IsReadyForThePlayer`.
The recorder had it too and nobody carried the lesson across: its settle waited for the
action queue to drain, and a map move into a combat room drains its queue before the
opening hand is dealt.
So the after-state it recorded for a room entry read `combat.in_progress=true`,
`combat.turn=1`, `combat.energy=0` and an empty `combat.hand` - a real state no player
ever acted from - and that sample is the digest of the `combat_start` boundary anchored
to it.
`gate` printed the consequence plainly: `combat.hand observed ''`, engine produced five
named cards.
The predicate now has one owner, `LiveRun.ReadyForThePlayer`, and both callers ask it.

**The member the driver calls is not always the member the client goes through.**
`EngineCommands` maps `SkipRewards` onto `RewardsSetSynchronizer.SkipLocalRewardsSet`,
which is correct for the driver and wrong for a recorder.
A post-combat loot screen is *terminal*, so pressing Skip takes `NRewardsScreen`'s
`ProceedFromTerminalRewardsScreen` branch and never calls `SkipLocalRewardsSet`; what
declines the leftovers is `BeforeLeavingRoom` on the way out of the room.
The recorder watched the driver's member and saw nothing, and a skipped reward is not a
detail: the arbiter refuses the following map move rather than walk away from an open
set, so the run stops reproducing there.
Both paths funnel through the private `SkipRewardsSet`, which is what is watched now.
`RunRecorderTests` carries that divergence in a named list with its reason, because it is
the one place the two halves do not meet at the same member.

**A patch on a base method does not fire for a subclass that shadows it.**
`MerchantCardRemovalEntry` declares its own three-argument `OnTryPurchaseWrapper` beside
`MerchantEntry`'s non-virtual two-argument one.
That is a shadow rather than an override, C# binds it statically, and the client calls the
derived method - so a patch on the base never fired.
The player paid gold, lost a card, and the recording said nothing about either while
staying structurally valid.
The general check is now a test rather than a note:
`APatchOnABaseMethodCoversEverySubclassThatShadowsIt` walks every patch this module
declares, finds every subclass that re-declares the patched name in a new slot, and fails
naming it.

**A fourth, and this one was caused by fixing the other three.**
The skip above is announced from `BeforeLeavingRoom`, which the engine runs as *part of*
the map move, and the sampling fix above makes the settle wait for the next fight to be
ready for the player.
Together they gave the skip the state of the room *after* the one it happened in -
byte-identical to the map move's own reading.
`RunCoverage` starts a fight at the first step whose after-state reports combat in
progress, so it attributed every fight's start to the skip rather than to the room entry:
`fight 1 after_seq=3 verb=MapMove` was right and fights 2 to 5 pointed at a `SkipRewards`.
`gate` then refused, reporting the recorder observing a fight the engine said had not
begun.
The fix is on the sample rather than on the boundary, deliberately.
`RunCoverage` reads combat from the state and not from the verb - the comment beside its
turn rule says why, and a fight is not always entered by a `MapMove` - so teaching it to
look for one would be wrong the first time an event starts a fight.
A decision the engine performs synchronously inside another's work has no engine work of
its own, so it is read where it happens: `RunRecorder.AnnounceAsAlreadyFinished` carries
the reading, and the pump does not settle a decision that arrives with one.
The pump's own docstring had already named the failure - "a batch settled together would
give two decisions one state and put the second one's effects on the first" - and this
arrived by a route the pump could not see.

## One the arbiter learned and the host did not

Different in kind from the traps above and worth its own heading, because nothing about
the client caused it.
The headless driver learned to answer the card screen an opening blessing opens - from the
recording's own selections, correctly - and the verbs the in-game host issues stayed as
they were.
So a recording whose blessing removes, transforms or upgrades a card began to replay,
verify and pass the publication gate, and then aborted in the retail client at the step
after the player had watched the blessing being made.
Nothing was fixed by that change and nothing was broken by it: the refusal simply moved,
from publication - before anybody had invested anything - to the worst place a refusal can
happen.

Two things follow from it that are not about card screens.

The first is that a verification which photographs behaviour the host cannot produce is a
check that cannot fail.
Whatever the two hosts do differently has to be stated as a difference somewhere.
`RetailPlayback.Verbs` declares what the running client issues, while `RunDriver.AnswersShownOnTheGamesOwnScreen` separately declares the answers for which it draws a screen.
`RunDriver.VerbsIssuedInsideARunningGame` exposes the first set without copying it, and `RecordedFightVerbAgreementTests` holds both against the committed fixture's walk to its first fight and proves the known `SelectCardFromScreen` shape remains covered by each.
Issued and drawn are two claims, held separately on purpose: a verb issued without a
screen would be committed with nothing to point at, and a screen drawn without the verb
would light the recording's card and never press it.
Neither establishes that every possible first-fight prefix is supported; bundle and relic
answers remain refused in the client because their prompt stand-ins are headless-only.
The tests need no game installed, which is the point - the run that would otherwise catch
this is one only a person with the client can make.

The same refusal in its other place is a surface offering a boundary the walk cannot
reach, and that is not a card-screen shape at all: standing a player at any boundary past
the first fight means replaying the fight before it, which is a card played and a turn
ended and loot claimed, none of which this client issues. A run library that offered every
boundary the recording proved therefore built the run, showed a decision or two and then
aborted, in the same worst place. So the verb set is declared once in
`RetailPlayback`, in the assembly that carries no game, and the library asks it of a
boundary's prefix before it draws the row: `RunViewPosition.Reachable` is that answer and
`Playable` folds it in, so a refused place keeps its row and says why. `RunDriver` is
still what enforces it, and the two cannot drift because there is no second copy of the
set. Widening what the client issues widens what the library offers in the same commit,
which is the property that was missing.

The second is the rule the fix was eventually written to, which is not the rule the first
fix was written to.
That one said a screen the recording opened and the recording answered is not the
player's, and pushed the engine's own selector for the one step that queued an answer -
which made the two hosts agree and left the player watching a blessing being taken and
never seeing the card it took.
The rule that replaced it is that a screen the recording opened is still a screen, and a
watcher is owed the sight of it.
So in the client nothing of the driver's is on the engine's stack: the game draws its own
card screen for that relic, `RecordedCardScreen` finds the recording's card on it, the
reveal lights it with the game's own focus and the commit presses it - the same consider,
reveal, commit as a map node, on a screen that has no engine command at all.
The screen's own preview of what was picked is confirmed after a hold rather than on the
frame it appears, because confirming it at once replaces it with a flicker; that is a
screen transition and not a decision, the same as the event screen's proceed.
`docs/headless-fidelity.md` owns the mechanism and what the two hosts now differ about,
which is whether the screen is drawn rather than when its answer arrives.

Two things about that screen were only learnable in the client, and both cost a refused
run in front of a watcher before they were.
The first is that it does not arrive on a screen transition: the blessing's own work
awards the relic and animates it onto the belt before opening anything, which is longer
than the settling budget every other screen on this journey needs. So the arrival waits
for `CardScreensUp.Count` - the shell's count of card screens the engine has opened and
is waiting on - rather than for a length of time, which is this page's oldest rule.
The second is that "the card screen" is not one screen. A removal opens
`NDeckCardSelectScreen` through `FromDeckGeneric`; a transform opens
`NDeckTransformSelectScreen` through `FromDeckForTransformation`, and its preview's
confirm has a different node name. A driver written to the first refused the second while
it stood open and drawn in front of the player, with a sentence saying it had not opened.
Both are `NCardGridSelectionScreen`, which owns the grid, the offered list and the click,
so that is what `RecordedCardScreen` is written to, and the confirm is found by type.
Whatever a build calls its screens, the base and the count are the two things worth
depending on.

Drawing that screen brought back the page's oldest trap in a new place, which is the
third thing to take from this.
`EventSynchronizer.ChooseOptionForEvent` does not run the option's work; it starts it as
a task, and that task suspends on the card screen. So on the frame the blessing is
committed the event screen underneath is mid-transition, and what it will show next is
not settled - while the journey's own
`CarryOnPastAnyScreenWaitingToProceed` runs at the end of every step and presses that
screen's proceed wherever it is the only button left. Pressing it there would dismiss
the event out from under a decision the recording has not made yet.
Which of the two the engine does first is knowable and was not worth knowing: one guard
is right either way, so the journey carries on past nothing while
`RecordedFightEntry.NextStepAnswersAScreenAlreadyOpened`. That property is host-blind on
purpose - it asks whether the run is still inside a screen, which is true of both hosts,
and is a different question from whether this host draws it.
Under the scoped selector this could not happen, because no screen was ever drawn to be
inside. It is the same lesson as the rest of the page in a new costume: what looks like a
length of time or an ordering is really a question about what the engine has finished,
and the honest answer is to ask rather than to assume.

**What is deliberately not built there: nothing scrolls the card grid.**
`NCardGrid` keeps holders for a sliding window of rows and reassigns them from the list
it was given as it scrolls, so a deck taller than that window has cards with no holder at
all until somebody scrolls to them. A recording that takes such a card is refused rather
than answered, in a sentence that says which of the two causes it is - the grid still
laying rows in, or the card being outside the window - because they want opposite
responses and the retry only helps the first. Driving the game's own scroll to bring a
card into the window is the missing piece, and it is not worth writing blind: whether the
grid has settled after a scroll is exactly the class of question this page exists about,
and it cannot be answered without the client. The decks a first fight is reached with fit
on one screen, so nothing today meets it.

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

Two consequences of that choice, stated rather than hidden.
The retired singleplayer mode card kept the icon it was duplicated from; the current Compendium entry reuses the wagon in Runmobile's one-resource pack.
And the popup's body scrolls when the evidence is longer than the panel, which is why
unmet rows are ordered first: what a player has to act on is above the fold, and the
rows that already passed are below it.

**The recorder's row is a fourth label in the game's own version overlay, not a
plate.** `NDebugInfoLabelManager.UpdateText` fills a `VBoxContainer` of three
right-aligned labels sharing one font and size - build, date and seed, then MODDED -
and `RecorderPresenceRow.VersionOverlay` postfixes that method to add a sibling label
under the MODDED one, copying its font and size rather than styling one of its own.
That copy is now every surface's rule rather than this row's exception: `GameText` in
the mod reads each native role's font and size, and `docs/mod-ui-direction.md` owns
which element each Runmobile element asks.
`RecorderPresence.For` derives what it says and its colour from exactly
`RunRecorder.Active` and `RunCapture.State`; the row is re-derived every frame, because
the recorder attaches after the overlay is built and a watch can break at any decision.
It follows the MODDED label's own visibility, which is how hiding the overlay from the
menu that put it up hides this with it, and it asks `RunmobileMod.MayDraw` the same way
every other surface does. It is installed apart from `RunRecorder.PatchClasses` -
`RecorderModule.PresencePatchClasses` names it on its own - because the two fail
differently: a decision method this build renamed refuses the whole module, but an
overlay this build renamed only costs the row, logged and nothing else. The game's
fields here are private and one starts with an underscore of its own
(`_moddedWarning`), so Harmony's own `___`-prefixed injected-field parameter needs a
fourth underscore to reach it - `____moddedWarning`, not the `___moddedWarning` the
marker alone would suggest - caught here only by checking the decompiled name rather
than guessing it.

## The run library

The third module, and the only one with a surface a player browses. What it offers and
what it refuses is `Sts2PilotTrainer.Trainer`'s - `RunBrowser`, `RunView`,
`MainMenuRow`, `RunHistoryPlate` and `LibraryCopy` - and every one of those is pure and
tested without a game. What runs inside the client is the four patches below plus the drawing classes behind them.

**The hooks are each the honest one for their question.**
`MainMenuLibraryRow` follows `NMainMenu._Ready`, which is where the game builds its button column and wires each button's focus behaviour; `NMainMenu.OnSubmenuStackChanged`, which is where the game re-decides visibility each time the column returns; and `NMainMenu.RefreshButtons`, which is where enabled states are settled at the end of `_Ready` and after a run is abandoned.
`CompendiumCard` follows `NCompendiumSubmenu._Ready`, which is where the row is built and where every focus neighbour is assigned index by index, so a button added anywhere else exists and is unreachable on a controller; and `OnSubmenuOpened`, which is where the game re-decides per-visit visibility, so the shell's permission to draw is asked each time rather than once.
`MyRunsSettings` follows `NSettingsScreen._Ready` and places its row beside `%ModdingButton`, the game's own modding settings entry point.
`RunHistoryPlateHost` follows `NMapPointHistoryEntry._Ready` and connects the `Released` that entry already emits and nothing in the game listens to.
One patch there rather than two: the entry carries both its own `FloorNum` and the `RunHistory` it belongs to, so nothing has to follow the screen's own selection to know which run a press is about.

**Which recording is this run's is matched on four values, and ambiguity answers none.**
The game's history and a recording both carry a seed, a character, an ascension and a
build, and both mean them the same way. Two runs a player started on the same seed with
the same character at the same ascension on the same build are indistinguishable, and the
honest answer is no recording rather than the first of them - a plate offering the wrong
run's fights would stand somebody in a fight they never had.

**The parchment browser is one measured page with a list and selected-run pane.**
The browser duplicates the popup's own second ribbon for its tabs and rows, then gives each duplicate's hotkeys up immediately.
For widened rows, `LibraryRibbonArt` replaces the ribbon's image and outline with nine-patches before the button's `_Ready` caches those nodes and their materials.
Their outer quarters retain native horizontal resolution while the middle stretches; narrow rows reduce the margins to fit.
The retail textures have different atlas padding, which `NinePatchRect` omits, so `LibraryRibbonArt` first reconstructs each logical texture in memory with its padding intact.
Those derived textures are reused for the source texture's lifetime and never written to disk.
The retail HSV and outline blend materials still receive the button's focus and press animations, and the selected run keeps its separate ink outline.
`NHotkeyManager` is a stack, so five rows all binding confirm would mean the key pressing whichever was pushed last rather than the one a player is looking at.
The keys stay with the panel's ribbon and the rows are reached by focus.
`LibraryScreen` measures the band, the list, the divider, the selected-run pane and the flat plate from the game's own panel nodes, so a build that changes the popup's layout moves the library with it.
`LibraryPaneArt` owns both the browser pane and the opened-run pane, including the run strip, relic icons and card portraits.

**Long lists and run strips page rather than becoming unreadable or leaving the panel.**
Browser rows are absolutely positioned siblings, not a scrolling list, so `ScreenPage` in `Sts2PilotTrainer.Trainer` derives the visible slice from the row count and measured room.
The last two places of a paged list become Previous and Next rows, and focus joins only the controls on screen.
The scrolling body is bounded first to reserve the measured controls below it, and room for fewer than a page is refused rather than allowing a row to overlap the popup's ribbons.
A browser with no listed runs still keeps its tab band, compatibility control and direct code lookup without inventing empty rows.
The run strip uses the same paging contract with a readable minimum cell width.
It opens on the page containing the selected floor, or the last replayed floor when no floor is selected.
Its Previous and Next controls take focus and explicitly accept the game's confirm and select actions, so the retail keyboard and controller bindings page the strip as well as a mouse press.
Paging is presentation: `RunBrowser` and `RunView` still return every row and floor, while `LibraryScreen` and `LibraryPaneArt` decide which page is visible.

**The plate's marks and rows are derived, then drawn without re-deciding them.**
`RunHistoryPlate` supplies a warning mark only for a refusal that has a status heading; the ordinary recorded state has neither a repeated heading nor a mark.
`RunHistoryPlateArt` draws that heading and mark above the flat rows, using the eligibility red for another-build refusals and muted ink for other warnings.
It also draws the chevron on Choose another floor, while every sentence, enabled state and row choice remains the pure derivation's answer.

**A brand-new player cannot reach the Compendium at all, and the main-menu row is the answer.**
`NMainMenu.SingleplayerButtonPressed` opens character select directly rather than the singleplayer submenu when `SaveManager.Instance.Progress.NumberOfRuns == 0`, `NMainMenu.RefreshButtons` sets `_compendiumButton.Visible` from `SaveManager.IsCompendiumAvailable`, and that returns false at zero runs in a release game.
`NPauseMenu` applies the same gate, so the first run's pause menu has no Compendium either.
The Compendium card and the run-history plate therefore both hang off surfaces the player who has never finished a run never sees, and that is exactly the player a trainer is worth the most to.
`MainMenuLibraryRow` adds a ninth button to the game's own column, duplicated from `MainMenuTextButtons/CompendiumButton` so its font, colours, reticle animation and disabled treatment are MegaCrit's, placed directly under it, and connected to the same `RunBrowserScreen.Open` the card connects to.
There is one library and one playback path; this is a second way in, not a second feature.

**This is the surface that must not adopt the running game where it is built, and it is a sixth trap of the same family as the five above.**
The main menu is constructed while the startup phase is still `Essential`: there is no model database and no id-serialization cache, so `EngineHost.AdoptRunningGame` refuses at `NMainMenu._Ready`.
It refuses once. `RunmobileMod.Adopt` latches `_adoptionAttempted` whatever the outcome, so a refusal taken at main-menu construction is the answer every later surface gets for the rest of the process - the Compendium card would stop adding itself and the recorder would stop recording, from one call in a feature that has nothing to do with either.
A build that asked there was green in every test and dead in the client, and the game's log named it: `refusing to report on this game ... startup phase is 'Essential'`, under `MainMenuLibraryRow.AddButton`.
The Compendium card can ask in its own `_Ready` because its submenu is built when a player pushes it, which is later; this row is built alongside the menu itself, so its first honest moment is the press, and `MainMenuLibraryRow.Open` is where it asks.
Building the row needs none of it: whether it belongs on the menu is the settings file and `SaveManager.Progress`, both of which the game's own `RefreshButtons` reads in the same method.
A refusal at the press takes the row off the menu rather than leaving a control that does nothing, and the shell's latched refusal keeps later visibility passes from restoring it - the same outcome the card reaches by never adding itself, one moment later.

Three things about the copy have to be done by hand because the duplicate came from a localized button.
Its label is set on the `MegaLabel` directly, since Runmobile ships no localization table and asking for a key that does not exist would put a key on the player's main menu.
Its private `_locString` is cleared with it, because `NMainMenuTextButton._Notification` re-reads that on every translation change and would otherwise put "COMPENDIUM" back when the player changes language.
Its label pivot is set deferred, the way the game sets it, because the hover animation scales the label about that pivot and Godot has not laid the new word out in the frame the text is assigned.

`ConnectMainMenuTextButtonFocusLogic` runs inside `_Ready`, before a postfix can add anything, so the row connects `Focused` and `Unfocused` itself - dispatched through the menu's own Godot method table, because both handlers are private, and the focused one deferred exactly as the game defers it.
The neighbour rewiring is conditional: where `FocusNeighborBottom` is empty this build's column is navigated geometrically and a new sibling is already in it, and writing a path in would replace a working answer with a brittle one.

**Whether the row is there is `MainMenuRow.ShownWhen`, and nothing else derives it.**
The player's stored `show_main_menu_row` where they have written one, and this profile's run count where they have not - so a player who has finished no run gets the row and a player with run history does not, and either of them can say otherwise on the settings page.
Once they have, that sentence holds: finishing a first run does not take away a row they turned on.
`RunsFinished.Any` is the one reader of the game's own `Progress.NumberOfRuns`, which is the same number the game reads to decide the two gates above, so the row appears exactly where the game's own route does not.
A settings file this build cannot read says nothing rather than "off", because answering "off" would take the library's only entrance away from a new player over a file fault.
`MyRunsRow` states the same answer on the settings page from the same owner, and `MyRunsSettingsRow` draws it, so the control and the menu cannot come apart.
Whether it may be pressed mirrors the Compendium button, because the game disables its own destinations while an undiscovered epoch is waiting and a mod row that stayed live through that would be a way around a gate the game put up.
The shell's permission to draw is asked first and a no draws nothing at all - not a greyed row.

**The Compendium entry is present whenever the shell may draw.**
It occupies the authored slot the game leaves when it hides Leaderboards, so the three visible bottom destinations remain inside the viewport and in the game's controller focus chain.
It duplicates the Run History treatment, replaces its label, and loads the same packaged wagon used by the mod list.
The browser is the only route to automatic index retrieval and direct run-code lookup, so an empty local library and a disabled `Fetch the run index` setting cannot hide it.
It reads no manifest and performs no network request merely to decide visibility.
`RunLibrary.RecordingFor` resolves one run from the recorder's directory index - the id names the recording in the index, so pressing a row costs that recording's manifest and no other's, and a manifest whose own run id disagrees with its name answers nothing rather than answering with the wrong run.

The browser still judges every run before showing it, and an empty or unavailable index is shown on that surface rather than represented by removing the way in.
Its build reading comes from `GameIdentity.ReadForCurrentEngine`: the running client is its own authority in retail, while only a headless process consults the receipted prepared copy.
Its player-facing tabs are Others and Mine; the Featured and Recent groups sit under Others.
The visible `Compatible with your game version` filter defaults on and hides incompatible runs during ordinary browsing.
Turning the filter off reveals incompatible runs as disabled rows.
An exact code does that automatically, selects its run in the sorted position, and shows both the required build and the current build.
Established multiplayer runs and incompatible runs still hidden by the filter remain counted.
A transport failure is stated inside the browser while direct run-code lookup remains available.
Nothing is remembered about a verdict now; `RunBrowser`'s list and the run-code lookup judge live every time they open.

**One state the design names is derived and not reachable, for a reason outside this
module.** The plate's console-command state - play rows offered, Submit refused, "A
console command was used, so it can't be submitted." - is not that state any more: the
recorder writes `source.native.integrity`, and `RunHistoryPlateHost.FactsFor` reads it
through `NativeSource.StatesSomethingOtherThanComplete`, which owns the comparison.
That reading answers `ConsoleUsed` null where a recording states no integrity at all, because
absent is not a clean run under another name and a plate reporting one it never checked is the
claim `AGENTS.md` forbids. From format v6 the field is required and a version-5 file reads as
`complete` through the migration, so no manifest this build parses reaches that answer.
The browser's multiplayer rule is the state that is still unreachable.
`LibraryRun.Listed` hides an established multiplayer run and `RunBrowser.Lookup` answers a
run code for one with the multiplayer body, both correctly, and nothing supplies the fact:
`LibraryRun.Multiplayer` is null on every run the library builds, so neither arm is
reached.
Null rather than false for the same reason the console fact is null where it is - a host
reporting "single-player" it never established is the claim `AGENTS.md` forbids, and the
hidden rule hides what was established and never a question nobody asked.
The reading it waits on is a recording that says which kind of run it was, and no manifest
field carries one: the recorder attaches to singleplayer runs only, so nothing writes a
session kind for the library to read.
Both arms become reachable when something supplies that fact, with no change here.

The Submit row shows and is available only when this profile's `settings.json` names an authorized `sharing_service_url`; without one, the row stays in place refused and the browser says online sharing is unavailable.
There is no built-in endpoint and no setting that automatically shares a run.
A configured endpoint must be absolute HTTPS without embedded credentials, a query, or a fragment, and any other value makes no network request.
The single popup shows the run's identity and integrity seals, takes a required name of at most 40 characters and an optional description of at most 200, and requires a display name for submission.
It says no other personal information travels, requires explicit CC0 consent, and says validation runs locally before anything is sent.
Only after that local publication gate passes does submitting send the complete manifest and the entered name, description, display name, and consent.
Index fetching and exact-code lookup use that same configured service boundary; the `fetch_run_index` setting defaults on and controls index retrieval only.

**The accepted parchment design is the implemented furniture.**
The browser draws parchment tabs, headed groups, the two-line filter header, character portraits, the selected run's act reached, the run strip, relic icons and the recorded deck count.
The opened run uses the same pane drawing, adds deck tiles where the recording carries them, and keeps the selected floor ringed and loaded floors ticked.
`RunHistoryPlateArt` hangs the flat plate beneath the game's own history pane.
The list keeps the settled compatibility filter and online sharing behavior rather than introducing another owner for either.

**A screen opened from another goes back to it.**
The container holds one modal, so every step replaces the last, and the ribbon would otherwise drop a player out of the library from wherever they had got to.
Each `LibraryPage` carries its way back rather than relying on a modal stack: an opened run returns to the tab and selection that opened it, and the submit flow returns to its selected Mine run.
Only the browser itself, which is the screen a player enters on, closes the library.
The tab travels as a bool because the way back ends up in a lambda's captured fields, and a captured `LibraryTab` has stopped this mod loading once already.

## Four surfaces, and the hook each one needs

Read out of v0.111.0 in a scratch decompile, ahead of building anything on them.
Mechanism only: node paths and the lifecycle method a `[HarmonyPatch]` postfix would
follow, in the shape the mode card already uses. Nothing here is a decision about what
to draw.
All four hooks below are the ones the run library now uses.

**A row on the main menu.** `NMainMenu._Ready` is the hook. It resolves each button by
plain path under `MainMenuTextButtons` - `ContinueButton`, `AbandonRunButton`,
`SingleplayerButton`, `MultiplayerButton`, `CompendiumButton`, `TimelineButton`,
`SettingsButton`, `QuitButton` - wires each one's
`NClickableControl.SignalName.Released`, and then calls
`ConnectMainMenuTextButtonFocusLogic`, which walks the column's children and connects the
two private reticle handlers. A button added afterwards therefore has to connect those
itself. `NMainMenu.OnSubmenuStackChanged` is the per-visit visibility hook because it runs whenever the button column returns from a submenu.
`NMainMenu.RefreshButtons` is the third hook: it is called at the end of `_Ready` and again after a run is abandoned, and it is where `_compendiumButton.Visible` and the epoch-gated enabled states are decided.
`NMainMenu.MainMenuButtons` is a fixed array of the game's own eight, and
`DefaultFocusedControl` picks the first visible enabled one out of it - so a ninth button
joins the column without ever becoming the menu's default focus.

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
`%RunHistoryButton` and `%BestiaryButton` per run.
Runmobile uses that vacant Leaderboards slot and remains present whenever the shell may draw, including with an empty local library, because direct run-code lookup begins behind it.

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
visible only when modding is enabled. That is the game's own modding entry point and the hook `MyRunsSettings` uses.

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
./scripts/package-mod.sh                 # build the distributable package without game content
./scripts/install-mod.sh                 # package, prepare, and install the mod
./scripts/install-mod.sh --uninstall     # remove it again
./scripts/protected-files.sh snapshot before.ledger   # hash everything the mod must not change
./scripts/protected-files.sh compare  before.ledger   # ... and say what a session changed
./scripts/arbiter adopt-live             # the refusal, from a process that is not a running game
./scripts/arbiter enter-fight <manifest> # the journey into the recorded fight, without a scene tree
./scripts/arbiter enter-fight <manifest> --play   # and the fight played through the capture and compared
./scripts/arbiter recorded-fight <manifest> --out manifests/<id>.recorded-fights.json
                                         # regenerate the recording's shipped lines after the manifest changes
```

### Producing a recording, and checking it

The recorder needs no setting up: it is on unless `settings.json` in the store says otherwise, and it attaches to every run the player starts or continues.
There is no screen for that setting yet, so the file is the whole of the surface and it has to be written in full - the schema member included, because a file missing it is one this build cannot read:

```json
{"schema": "sts2-pilot-trainer/runmobile-settings/v1", "record_my_runs": false}
```

It goes beside the recordings, at `<the store>/settings.json`; step 6 below says where the store is.
A file that is not there means recording is on.
A file that is there and cannot be read - a schema this build does not know, a missing member, anything that is not this JSON - means recording is off, and `godot.log` says which file and why.
That is the direction it fails in on purpose: the only thing this file can say is "off", so a recorder that carried on through a sentence it could not read would be recording without consent.
The same file says how many runs are kept and how to remove them all; "Keeping runs, and removing them" below has both.

1. `./scripts/protected-files.sh snapshot before.ledger`, so what the session changed can be measured rather than asserted.
2. `./scripts/install-mod.sh`, then launch the game **through Steam** - `open "steam://rungameid/2868840"` or the library - because launched on its own the client cannot initialise Steam and stops on an error popup.
3. Check the game's log says the mod is there: `[Runmobile] Recorder installed` and `--- RUNNING MODDED! --- Loaded 1 mods`. The log is `~/Library/Application Support/SlayTheSpire2/logs/godot.log`.
4. Play a run. `[Runmobile] recording this run as native-<seed>-<date>-<time>` says it attached.
   The gate's rejection condition requires every one of the ten negative controls to find the decision it damages, so a run meant as evidence has to have made each of them: the opening blessing, a card screen opened by one of exactly two options - the rest site's **Smith**, or the **Waterlogged Scriptorium** event's third option, its 99-gold enchantment - holding a second copy of the card that gets picked, which an early deck satisfies because the starting deck holds several Strikes and Defends, a map move from a node with more than one child, a turn **opened with two plays** that differ in the card played or the enemy it is aimed at, one of them aimed at an enemy while another was alive and one made from a hand holding another card of the same energy cost that aims the same way - both attacks, or neither, a claimed gold or potion reward, and a card reward that offered more than one card.
   Two of those are narrower than they look and both cost a whole run to learn the hard way.
   Any *other* card screen - a different event's, a shop's card removal, another rest-site option - leaves `enchant-a-different-card` inapplicable, because only those two options are established to *mark* the card they pick rather than remove it, and a screen that removes it corrupts into a run identical to the honest one.
   `reorder-plays` is the other: it verifies its pair against the hand the turn's own checkpoint recorded, so the pair has to be the *first two* plays of a turn, and any turn whose first two plays are the same card at the same target is passed over.
   A potion or a card screen in between disqualifies that turn too, because the second play's index was then recorded against a hand the first play never saw.
   Any turn in the run can supply the pair, so this is usually satisfied by accident; it is only worth thinking about if you find yourself opening every turn with the same card.
   A run that misses either finishes normally and then gets `NOT PUBLISHABLE` for coverage, with nothing wrong with the recording.
   The recorder writes the alternative each of the last three offered, and omits it where the decision genuinely had none - a run that never met one of them records honestly and is not publishable.
5. End it - won, dead, or given up from the pause menu. `[Runmobile] recorded <id>: <outcome>, N decision(s), M boundary/boundaries, continuity continuous, integrity complete, written to recordings/<id>.replay.json` says it finished, and a line after it says so if the recording does not validate.
6. The recording is under the store: `~/Library/Application Support/SlayTheSpire2/Runmobile/<the game's own profile scope>/recordings/`. The scope mirrors what the game resolved for its own saves, so two accounts and two profiles do not share a library.
7. `./scripts/arbiter gate <that file>` is the verdict, and `./scripts/arbiter enter-fight <that file> --fight 2` stands the arbiter in its second fight.
8. `./scripts/protected-files.sh compare before.ledger` reports what the session changed. The game's own saves, profile and run history are expected to change - the player really played a run - and everything of this mod's is under `user://Runmobile/`.

To exercise continuity, quit to the main menu part way through a run and continue it from the game's own Continue.
Outside a fight, `[Runmobile] continuing the recording of <id> at decision N; continuity continuous` is the pass.
During a fight, Continue returns to that fight's room-entry boundary, the journal records the intervening decisions as discarded, and the same continuous line names the boundary's next decision.
A `continuity broken` line names any other mismatch, and the recording is then refused for publication rather than repaired.

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
[demo/RUNMOBILE-MAIN-MENU.md](../demo/RUNMOBILE-MAIN-MENU.md) has the main-menu row on a
zero-run profile, the library it opens, the settings control that hides it, and a
progressed profile with no row and its Compendium card intact.

### Keeping runs, and removing them

The recorder writes two real files per run into the player's own user directory and nothing else ever removed one, so how many are kept is a setting and removing them all is something a player can ask for.
Both live in the same `settings.json`, and both are the same operation with a different number - `RecordingRetention` applies the policy, `RecordingLibrary.Cull` decides which recordings it names:

```json
{"schema": "sts2-pilot-trainer/runmobile-settings/v1", "keep_recent_runs": 50, "purge_my_runs": false}
```

`keep_recent_runs` is a standing policy and defaults to 50, which is roughly the size of a screenshot folder.
Older runs go the next time the player reaches the singleplayer menu with a save profile chosen, and the count is of runs rather than of files: a run's journal and its manifest go together or not at all.
Zero keeps none.
A negative number is refused with a logged sentence naming the file and the value, and the default is applied instead: a player who wants more keeps writes a larger number, and there is no way to ask the file for unbounded growth.
Removing nothing survives only as an internal answer for a settings file this build cannot read - a sentence nobody could read is not somebody asking for their runs to be deleted, so recording off and deleting nothing fail in the same direction.

`purge_my_runs` is the one-shot act: every recorded run is removed, and then the mod writes the member back to `false` so a purge is something a player did rather than a state they are left in.
`keep_recent_runs`, `purge_my_runs`, `fetch_run_index` and `show_main_menu_row` are the four members the mod writes, and each write edits the member it names: the file is edited in place rather than re-serialised, so every other member survives exactly as the player typed it - a refused negative `keep_recent_runs` included.
A file that is there and is not a settings object is refused rather than written over, because reading one already means "record nothing" and overwriting it would discard what the player wrote in order to store what they meant to add to it.
`godot.log` carries the receipt either way - `purged your recorded runs: N removed` for the act, `keeping your 50 most recent runs: N older one(s) removed` for the policy.

Three properties hold, and each is asserted rather than described.
Every file removed came back from `RecordingLibrary` as part of a recording written under the name this build writes, so a player's own file in that directory, a manifest they copied in, and this mod's `settings.json` all survive a purge.
Every removal goes through `RunmobileStore.Remove`, which refuses a path outside the store, a path inside a game installation and a directory, exactly as a write does.
And the moment is the singleplayer menu: the shell's own patch asks for retention first and unconditionally, ahead of any question about which modules contributed a card and ahead of the adoption it attempts only where there is a card to draw, because keeping and removing a player's files is the shell's duty.
`RunmobileMod.EnsureAdopted` asks for it again, so the recorder's own first adopted moment is covered too, and asking twice costs nothing: it is applied once per save profile whoever asks.
No journal is being appended to when it runs - the recorder opens one only after passing that same adoption gate, and it has let go of the run it was recording before the singleplayer menu can be reached again - so a removal can never race a journal.
Retention runs whether or not the adoption succeeded, and where adoption is never attempted at all: its condition is the store's, a chosen save profile, and not the engine layer's verdict on whether this game can be read.
So a build where the run library and the recorder both decline, and a build the engine layer refuses to adopt, both still honour a purge and still enforce `keep_recent_runs`.
The policy is applied once per save profile rather than once per process: the store is resolved per operation and two profiles do not share a library, so a player who switches profile has their second profile's `settings.json` honoured against their second profile's recordings.
It cannot be mod start: the game has no chosen save profile then, so the store cannot yet say whose files these are.

### The settings row, and the size figure

All four members with a control are one row: `MyRunsSettingsRow` in the mod, drawn from `MyRunsRow` in `Sts2PilotTrainer.Trainer`, wired to the disk by `MyRunsSettings`.
It is a row and not a section.
The retention policy, the removal act and the index-fetch choice are about the player's own runs; `show_main_menu_row` is about where Runmobile can be found.
It is in the same row rather than a section of its own for the reason the section exists at all: this mod contributes one place a player configures it, and a second one would be a second thing to find.
Where Runmobile's settings section hangs - `%ModdingButton` is the game's own modding entry point, and there is no Mods tab to extend - belongs to the run library along with everything else in it; this is one thing that section places, built whole so that placing it is all there is to do.
Keeping it apart is also what lets it be assembled and asserted on in a process with no game, which `MyRunsSettingsRowTests` does node by node.

What it reads is a sum, and the sum goes through the containment gate.
`RunmobileStore.SizeOf` measures one entry through `PathOf`, exactly as `Read` does, and refuses a directory the way `Remove` does; a file that is not there occupies nothing, so a run removed between the listing and the measuring is not a hole in the figure.
`RecordingRetention.OnDisk` is what sums it, because that is already the one place that knows where recordings live and which files each is made of - a surface that listed the directory for itself would be a second thing to keep in step with the removal.
It measures only what `RecordingLibrary` recognises, so the figure is what this mod's own runs take rather than what is in the directory.

`MyRunsRow.For` derives every line, the same way `PlaybackTransport.For` derives the transport, and for the same reason: the policy's number, the disk's number and what a removal just did are three facts that can disagree, and a surface where each control set its own label would eventually show a reading taken before an act beside a receipt taken after it.
The reading after a removal is re-taken from the disk rather than predicted, so a purge that left the continuable run's journal behind reads as the one run it actually left.
A failure - the store not ready, a settings file this build will not write over, a disk that refused - goes to the log and leaves the row saying what it said, because a receipt is a claim that something happened.

The main-menu line is the one thing on this row that is not about the disk, and it is asked separately for that reason.
Where the recordings directory refuses, `MyRunsSettings` still asks `MainMenuLibraryRow.Shown` - the settings file and the game's run count, neither of which is that directory - because a row reporting "off" because no save profile was chosen would be stating something about a menu it never asked.
Pressing it writes `show_main_menu_row` and nothing else, and moves nothing on screen: leaving settings pops the submenu stack, and the menu behind this screen re-decides in `OnSubmenuStackChanged`.
The row is redrawn from the disk instead, so what the control says is what the file now holds, and a write that failed leaves it saying what is still true.

Pressing Remove is `RecordingRetention.PurgeNow`, which writes the request to the file first and removes second, so a game that stops in between finishes at the next main menu.
It is not behind the once-per-profile latch and does not set one: that latch exists so a standing policy is applied once as a profile is entered, and this is a person pressing a control.

Moving the policy removes nothing where it stands, and forgets that latch for the profile this game is running as - `RecordingRetention.ReapplyPolicyAtNextMenu`, which is the retention owner's because the latch is.
This profile only: another profile's policy was applied against its own recordings and a write made while playing as this one says nothing about it.
Without that the row's own second line would be describing the next launch rather than the next main menu, because this profile's turn at the latch is already taken by the time a player can reach the control.

**Where the row is parented is the column, not the modding entry.**
`%ModdingButton` sits in a `MarginContainer` named `Modding`, beside the heading label, and a MarginContainer lays every child out in the same rectangle - so adding the row there made it a third thing drawn on top of the heading and the button rather than a third row.
That container is one entry in the column's `VBoxContainer`, and the column is where a new entry belongs: a VBoxContainer stacks what it holds from each child's minimum size, which `MyRunsSettingsRow` already carries, so everything below moves down on its own.
Nothing is repositioned by hand, and growing the parent's minimum size reserves nothing here - the fix that looks obvious and does not work.
`MyRunsSettings.Attach` places it directly after the modding entry so Runmobile's settings sit with the modding ones a player came there to find.

The store refuses until the game has chosen a save profile, and the settings section hangs off the main menu's own modding entry point, which is reachable before one is.
`MyRunsSettings.Build` therefore catches and logs the way its other members do, and hands the derivation a `MyRunsDisk` fact of its own rather than a count of zero - no runs yet and cannot tell yet are different sentences.
That fact has three answers, not two, because only one failure has a cause the row may name.
`RunmobileStore.StoreNotReadyException` is the store's own refusal to say where it is, and it alone gets the line about choosing a save profile - a state the player resolves by choosing one.
Every other fault gets a line that names no cause, because the row has not established one and the log has the exception; a fault told as the save-profile sentence would be false about a state the player cannot act on.
Neither answer says anything about `settings.json`: a read that never ran establishes nothing about that file, so `SettingsReadable` goes back unestablished rather than as a refusal nobody made.
Either way the row is that one line with every control refused, because each of them writes into a file this build cannot yet name.

The second line predicts what retention will actually do rather than what the subtraction says.
`Apply` never removes the run the game can currently Continue, so `OnDisk` asks `ContinuableRun` the same way `Apply` does - the game's own answer matched against the recordings the library named - and hands the row `ContinuableRunWouldBeLeft`.
A player only sees the difference under a hand-written `keep_recent_runs` of zero, where three runs on disk take two rather than three, and without it the row would break that promise every time.
That makes the settings row `ContinuableRun`'s third caller, and its docstring names it: `LoadRunSave` is not a pure read, and what makes it safe is being called at the main menu, after the game's own `RefreshButtons` has made the same call.
A game that cannot say which run it can continue refuses there as it does anywhere else, and the row shows the same could-not-be-read line rather than a pending count it could not compute.
The confirmation is the game's own `NGenericPopup`, the one the eligibility screen uses, with the way out focused.

Three deliberate departures from the design, all presentation rather than wording.
The keep control is a stepper where the design says slider: Godot draws a slider's grabber from a theme *icon*, so a slider here would wear the engine's default grey on a screen made of torn stone or need art this mod does not ship, and the game's own `NSettingsSlider` cannot be had outside the settings scene it lives in.
The removal is a stock Godot `Button` carrying the game's own red where the design says the game's red ribbon, and it is the same case: the game assembly ships no ribbon node at all - *ribbon* is the design's word for the button bar the game's own popup draws - and the game's settings furniture, `NSettingsButton` and its siblings, is scene-resident and cannot be instantiated standalone, so a hand-rolled section has nothing to duplicate.
The design's ribbon material is where it actually exists: the confirm behind that button is the game's own `NGenericPopup`, with its two real ribbons.
The label and the numeral are exactly as the design settles them.
And the numeral shows what the player's file says even where the control cannot reach it: a file that keeps zero is a standing purge and a row reading "1" over it would misstate the policy, so the numeral is the truth and the control is only how far a press reaches.

The policy has a minimum of one and no maximum, and one press moves it by one from wherever the file already stands.
There is no top to refuse at: a press that reported two hundred over a file saying five hundred would take three hundred runs nobody asked to lose, which is the whole of what a cap here would ever do.

A settings file this build cannot read is the one case where the numeral is not the player's own sentence, and the row says so rather than showing it.
`RunmobileSettings.Read` answers an unreadable file with its "remove nothing" sentinel, which is a number no player wrote and no control can put right, so `RecordingRetention.OnDisk` carries the readable-or-not as a fact of its own on `MyRunsFacts` and hands the row the default in place of it.
That number is shown and nothing is inferred from it: under an unreadable file no policy is in force at all, because `RecordingLibrary.Cull` names nothing for the sentinel, so the second line says exactly that - `settings.json` could not be read, so no runs are removed automatically until it is - rather than claiming the usual policy stands.
Every control that would write into that file is refused with it: both ends of the stepper, the removal, the index-fetch switch and the main-menu switch, because `PurgeNow` records the request in the same file before it takes anything and an act that cannot be recorded is one that must not be offered.
The second line explains the shared refusal, so none of them is a dead control with no reason on screen.
`RunmobileSettings.Set` refuses through `Read` itself rather than through a second set of rules of its own, so a document can never be unreadable to the row and writable by the control beside it; an absent file is not an unreadable one and still gets the defaults with the one member set.

The one recording retention never names is the run the game can currently Continue, and neither a cap nor a purge removes its journal.
A run whose journal went missing under it is one the recorder picks up again at its next room and records as a run it watched from the start, which is a claim about what was observed that nobody established - and that is worse than a purge which leaves one file, so the file stays and the log says it was left.
Which run that is comes from the game: `ContinuableRun` asks `SaveManager` whether there is a run save and reads the run's start time out of it, matching the recording by the same start time `RecordingLibrary.Name` writes into its name, and refuses where the game has a run save it cannot read rather than guessing.

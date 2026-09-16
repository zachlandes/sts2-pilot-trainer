# Running the real engine headless, and what that costs

The arbiter loads the actual shipped `sts2.dll` and drives the actual `RunManager`.
It does that in a plain console process with no Godot scene tree, no renderer, no
audio, no input and no frame loop. This is the list of everything that had to be
changed or stood in for to make that work, and the argument — with evidence where
there is evidence — that none of it changes what the game decides.

Nothing here is hidden from the output: every verification report carries these as
caveats, on passes as well as failures.

## The shape of the thing

- `third_party/godot-stubs` compiles to `GodotSharp.dll`: managed no-op stand-ins for
  the Godot API surface the game links against. Vendored from `wuhao21/sts2-cli`
  (MIT), plus a small addition of our own. See its `CHANGES.md`.
- `tools/Sts2PilotTrainer.Bootstrap` copies the player's own installed assemblies
  into `build/lib` and applies one IL patch to the **copy**. The installation is
  hashed before and after and the run fails if a byte moved.
- `src/Sts2PilotTrainer.Engine` is the only project that knows anything about a
  specific game version. When the game ships a new build, it is the only one that
  should need to change.

## What is neutralised, and why

### One IL patch, applied to the private copy

`CombatManager.WaitUntilQueueIsEmptyOrWaitingOnNonPlayerDrivenAction` returns a
completed task.

The host drains the game's action queue inline on a synchronous synchronization
context, so the queue is already empty by the time this wait is awaited. Left
intact, the await never resumes — there is no frame loop to pump it. The patch
changes *when the caller resumes*, not which actions ran or which RNG streams
advanced.

The bootstrap tool **requires** this patch to match at least one site. A patch that
silently stops matching is version drift, and the only safe response is a loud
failure.

### Runtime patches, applied with Harmony

Harmony is what the game itself ships and loads for mods, so this is the supported
mechanism rather than an unsupported hook.

| Patched | Why |
|---|---|
| `Cmd.Wait`, `TalkCmd.Play` | Animation sleeps and speech-bubble effects. Both block or throw with no scene tree. |
| `RunManager.FadeIn/FadeOut/ClearScreens/UpdateRichPresence` | Screen transitions and platform presence. Pure presentation. |
| `PreloadManager.Load*Assets` | Texture, audio and animation preloading. There is no renderer to want them, and the loaders dereference stub properties while assembling their asset lists. |
| `SaveManager.SaveProgressFile`, `SavePrefsFile`, `SaveProfileFile` | **The player's save directory is a read-only input.** The run is created with `shouldSave: false`, but the engine still reaches for the save subsystem on room entry. |
| `SaveManager.SaveRun` | The same refusal with one addition — see [the run save, collected rather than dropped](#the-run-save-collected-rather-than-dropped). |
| `RunManager.OnEnded`, `RunManager.CleanUp` | Postfixes that read and change nothing — see [the run's end, read where the game ends it](#the-runs-end-read-where-the-game-ends-it). |
| `LocManager.GetTable`, `LocString.GetFormattedText/GetRawText`, `LocTable.*` | Localization is stubbed with no data at all — see below. |
| `MerchantPotionEntry.CalcCost`, `Cauldron.GenerateRewards`, `CallingBell.GenerateRewards`, `ScrollBoxes.GenerateRandomBundles` | **The opposite of the rest of this table.** These run with the headless flag turned off for the duration of the call, because the flag changes what the game *generates* at these four sites — see [four places the flag changes what the game generates](#four-places-the-flag-changes-what-the-game-generates). |
| `CardSelectCmd.FromChooseABundleScreen`, `RelicSelectCmd.FromChooseARelicScreen`, `NCrystalSphereScreen.ShowScreen` | Prompts no seam answers, stood in for at the prompt itself — see [four screens the host has to stand in for](#four-screens-the-host-has-to-stand-in-for). Installed only where a driver is answering; with none, the game's own path runs. |

### The run save, collected rather than dropped

`SaveManager.SaveRun` is patched rather than neutralised, and the difference is one
branch. Nothing is written where the game would write, in either case.

With nothing collecting, the prefix returns a completed task and calls nothing — byte for
byte the neutralize the other three savers get.
With a collector installed, it first asks the game for the object it was about to save,
through `RunManager.ToSave(preFinishedRoom)`, at the game's own call site, with the game's
own argument, and hands it over.

That is the only way to obtain the save the retail client takes at a floor arrival, which
`RunManager.EnterMapPointInternal` writes after setting the map coordinate and before
rolling the room type — so the game's own save is a floor-entry snapshot by construction.
`RunSaveInterception` owns it and `docs/native-replay-format.md` owns what may be done
with the result.

One thing it does not reproduce: `SaveManager.SaveRun` is what reaches
`RunSaveManager.SaveRun`, and it is the latter that consults `RunManager.Instance.ShouldSave` before writing.
The prefix sits upstream of that gate, and every run in this host carries `shouldSave: false` anyway — a started run is created with it, and `GameSession.RestoreSavedRun` puts a run continued through the retail path back to it.
What is collected is therefore what the game *asked* to save.
For a snapshot produced from a replay that is the same object; reading a save a player's own client wrote is a different path.

A missing `SaveRun` in a future build is a startup **failure**, as every name in this
patch set is: a host that silently stopped intercepting it would write no save and
collect none either, and both silences look like success.

### The run's end, read where the game ends it

The game ends a run inside a decision.
The Architect's PROCEED calls `RunManager.WinRun`, which calls `OnEnded(true)` with the run's final state and then `GuaranteeKillAllPlayers`, which kills the player creature so the client can draw the ending.
The recorder reads the decision the run ended on at `OnEnded`, before that kill, because it is the last reading of the run as it was played, and a replay that sampled the same decision once the driver's call returned would read a dead player, a lost fight and a game over, and refuse the recording it was made from.
So `RunEnding` holds the projection a postfix on `OnEnded` takes, the first one of the run because the kill reaches `OnEnded` a second time with the player dead, and `Arbiter.ReplayStartedRun` takes it as the after-state of the action the run ended on, evaluates that action's checkpoints against it, and reports it as the final state.
A postfix on `CleanUp` forgets it, so the next run replayed in the process starts with no ending.
`RunDriver.Apply` refuses every action after it, because a history that goes on past the run's end is not this run's.
Both members missing in a future build are startup failures, as every name here is.

### Four screens the host has to stand in for

The engine does not take a command for everything a player does. Four of its surfaces
are driven by the UI, and there is no UI here.

**The loot screen a won fight puts up.** `NCombatUi.ShowRewards` waits out the death
animations and calls `CombatRoom.OfferRoomEndRewards`, which is what generates the
gold, rolls for a potion and builds the card reward. Nothing else calls it, so a
headless replay that walked out of a won fight simply never earned its loot. The
driver calls the same method at the same point - immediately after the action that
ended the fight has drained - and generates nothing itself.

Offering is not a decision, so it is not an action. Taking is, and until the manifest
says so the set sits open. `RewardsSet.Offer` hands the set to
`RewardsSet.testSelector` when test mode is on, in place of showing the screen; the
driver's delegate parks the set there rather than resolving it, and clears
`ThrowInTestIfRewardsNotTaken`, which is a test-only assertion read at exactly one
site - the line after that delegate returns - and exists to catch a test that forgot
to answer a reward screen. Here the answer arrives from the next action instead.

The consequence worth knowing: a map move that would leave the room with rewards
still on offer is **refused**. The engine skips a leftover set on the way out
(`RewardsSetSynchronizer.BeforeLeavingRoom`) and says nothing, so a history that
simply omitted a reward would replay identically to one that declined it on purpose.
Declining is written down as `SkipRewards`.

**The card screens a reward or an enchantment opens.** Both pull the player's answer
synchronously from inside the call that opened them, through `CardSelectCmd.Selector`
- the `ICardSelector` seam the game's own tests use. The driver installs a selector
that answers only from the manifest and refuses otherwise. It records the refusal
rather than throwing, because the engine runs both callbacks inside fire-and-forget
tasks that swallow exceptions; the driver raises it after the action instead, where
it can actually stop the replay.

Because the answer is pulled from inside the opening call, the actions that record
those clicks - which sit after it in the history, because that is when the player made
them - are handed to the selector before the call is made. Only a contiguous run of
screen answers - `SelectCardFromScreen`, `ConfirmCardScreen`, `SelectBundleFromScreen`,
`SelectRelicFromScreen`, which `CardScreenAnswers.Verbs` lists - immediately after the
opening action is ever read, and a selection no screen consumed is refused.

How many picks a card prompt takes depends on what it asked for, and the selector reads it two ways.
A prompt that asks for exactly N - a removal, an upgrade, the Scriptorium's two enchantments - is answered by exactly N `SelectCardFromScreen` records; fewer is refused, because the engine would choose the rest, and a `ConfirmCardScreen` there is refused by name, because only a range leaves the count to the player.
A prompt that asks for a range - `MinSelect` below `MaxSelect`, which is Purity's "up to 3", Guards, Neow's Fury and every choose-a-card screen, whose seam asks for zero to one - is answered by its picks and then one `ConfirmCardScreen` whose `count` is how many there were, zero for a prompt declined; the selector takes picks until the confirmation, holds its count to the picks and to the range, and refuses picks that stop short of the maximum and simply end, because those cannot be told from a recording cut short.
Picks that reach the maximum are the whole answer with or without a confirmation, because nothing more could have been picked, and that one form is read without one.
The recorder writes the confirmation from the same prompt the picks came from, so the two sides cannot disagree about which prompts take one.
The format's addition is inside version 6 and is why the legacy form is read: a manifest written before it - the previous recorder wrote a choose-a-card pick and nothing after it, and the committed whole-act fixture holds a potion's - carries no `ConfirmCardScreen`, is not edited to, and replays exactly as it did, while a build before it refuses a manifest that carries one at ingestion, as an unknown verb, rather than reading past it.
`CardPromptCaptureTests` holds both forms: the legacy pick replays to the digest the confirmed one does, and the same recording cut short of its maximum is refused by name.

A card reward's alternative is answered through the same seam and is not one of those
followers. `ICardSelector.GetSelectedCardReward` hands back either a card or a
`CardRewardAlternative`, and what the alternative does to the reward is its own
`AfterSelected`, which the driver reads as the engine reads it rather than assuming.
On v0.111.0 `CardRewardAlternative.Generate` puts two kinds on the screen: the loot
screen's own Skip, first on every card reward that can be skipped, which ends the
selection and leaves the reward unclaimed on the loot screen, where it can be opened
again or left behind by `SkipRewards`; and a relic's alternative - Pael's Wing's
sacrifice - which ends the selection and completes the reward.
So the loot-screen click is written as `TakeCardRewardAlternative` in place of `TakeCard`,
naming the alternative's own id and the position the screen reports for it, and the
driver answers the seam with it and holds the reward to what that alternative does.
A player who presses Skip and opens the same reward again is two clicks, and the
recorder writes each as its own decision with its one answer; a completed loss from a
player's store was refused at exactly that until the driver stopped reading the
unclaimed reward as the engine refusing.
The reroll keeps the selection open for an answer the recorder never writes - it
refuses a second answer to one reward - so the selector refuses it by name and answers
nothing, which is the engine's own way out of the selection.

**Three prompts the seam does not reach.** `CardSelectCmd.FromChooseABundleScreen`,
which Scroll Boxes opens, takes its first bundle without asking when the headless flag
is on; `RelicSelectCmd.FromChooseARelicScreen` has no test branch and would show a
scene; the Crystal Sphere's minigame pushes `NCrystalSphereScreen` and waits on a
completion source the screen's clicks drive. `ScreenStandIns` patches each at the
game's own entry point, headlessly only, and only answers while a `RunDriver` is the
current answerer: the bundle and relic prompts are answered from the same selector
as a card screen, by a queued `SelectBundleFromScreen` or `SelectRelicFromScreen`
whose identity - the bundle's cards joined in the order the prompt listed them, or the
relic's id - is checked beside the position; the Crystal Sphere's screen is not shown
and the minigame it would have drawn is captured for the driver, which sets the tool
the recording names and clicks the cell it names through the minigame's own
`SetTool` and `CellClicked`. Which cells hold what was rolled by the engine when the
minigame was built, from the event's own stream. Nothing on v0.111.0 opens the relic
screen, so a recorded relic pick refuses as an answer no screen consumed, with that
sentence.

Where the manifest is silent each of the three refuses, as the card seam does.

**The chest a treasure room puts in front of the player.** `NTreasureRoom.OpenChest`
is what calls `TreasureRoom.DoNormalRewards` and `TreasureRoom.DoExtraRewardsIfNeeded`,
and nothing else does, so a headless replay that walked into a treasure room would
find an unopened chest and refuse every decision about it.
The driver calls the same two methods and generates nothing itself: the relics were rolled by
the engine's own `BeginRelicPicking` when the room was entered, and the gold and any
extra rewards are the engine's too.
It calls them at the first decision about what the chest holds - the relic, or a reward the chest put up - and never inside the map move that entered the room, because the client opens the chest on a click after the arrival.
`RunDriver.Approach` is where a replay that reads the state before each decision opens it, so the reading before the relic pick is taken with the chest open, as the recorder's is; `Apply` opens it too for a caller that takes no such reading.
The chest's gold therefore lands after the game's arrival save and after the reading a recorder takes of the arrival, in both hosts.
Opened inside the map move, as it was, that gold was in the arrival reading headlessly and not in the client's: a Continue at a treasure-room arrival read as a hole headlessly (matrix row S13 in `RecorderContinueTests`), and the floor-entry boundary a retail recording declares there was one no replay could reproduce.

Opening is not a decision. Taking the relic is `TakeChestRelic` and leaving it is
`SkipChestRelic`, for exactly the reason `SkipRewards` exists: the engine discards an
undecided relic when the room is left and says nothing, so a history that omitted the
decision would replay into the state of one that declined it. A map move or an act
transition that would leave either decision unmade is refused.

**The headless stand-ins are not installed for the run's lifetime inside the retail client.**
The same `RunDriver` runs there, walking a constructed run through the recording's decisions before its fight, and most of these surfaces are on a player's screen: answering one would take a decision away from somebody who was looking at it, and the client opens its own chest through `NTreasureRoom.OpenChest`.
So the driver installs no rewards delegate, chest opening, `ScreenStandIns` or card selector there, and narrows itself to the opening blessing, an event option, a map move and `SelectCardFromScreen`.
Bundle and relic selections still refuse because the prompt stand-ins that consume them are headless-only.
Every other verb refuses there, including the combat ones, because the fight is the player's.
See [the in-game host](in-game-host.md).

**The card screen is not stood in for in the client at all: it is drawn, and driven.**
It took a shipped defect to find the screen and a second change to answer it properly.
The driver learned to queue those selections for the headless host without the client learning to issue them, so a recording whose opening blessing removes, transforms or upgrades a card replayed, verified, passed the gate and then aborted in the client at the step after the player had watched the blessing being made.
The refusal had not gone away; it had moved from publication to the worst place a refusal can happen.
The first fix pushed the seam's own selector for that one step, which made the two hosts agree and left the player watching a blessing whose card they never saw.
What replaced it is the product answer: nothing of the driver's is on the engine's stack in the client, the `CardSelectCmd` entry point for that relic takes its `Selector == null` branch, and the game puts up its own card screen in front of the player.
Which screen depends on the relic and it is not one type: `FromDeckGeneric` opens `NDeckCardSelectScreen` for a removal and `FromDeckForTransformation` opens `NDeckTransformSelectScreen` for a transform, and they name their confirm buttons differently.
Naming one of them is how the driver first refused a screen that was open in front of the player, so it is written to `NCardGridSelectionScreen` - the base that owns the grid and the click - and finds the preview's confirm by type; the list the recording's `option_index` indexes is the open `CardPrompts` prompt's rather than the screen's, which [docs/in-game-host.md](in-game-host.md) owns.
The recording's card is then found on that screen, lit with the game's own focus and pressed, by `RecordedCardScreen` - one owner, so the card the reveal lights is the card the commit presses.
There is no engine command for any of it, which is why `RunningGameCommands.SelectCard` is supplied by the host exactly as a map move is.

Two things follow, and neither is about card screens.

Where the two hosts differ is now *whether the screen is drawn*, not when its answer arrives.
That changes what a watcher is shown, so it changes what is counted and captioned: headlessly a card selection is executed with no reveal, no hold and no number, and `PrefightChoice.Blessing` carries the cards so the caption is the only place they are said; in the client the pick is its own decision with its own caption and its own card art, and the blessing's caption says only the relic.
`RunDriver.ShowsTheAnswerBeingGiven` is the single owner of which host is which, and `RecordedFightEntry` asks it for every one of those three questions rather than deciding any of them itself.

The recorded `option_index` is a position in the list the engine handed the screen, and the screen sorts that list before it draws it.
So `RecordedCardScreen` reads the offered list off the screen rather than the grid, and checks the recorded card id against the position the same way `ManifestCardSelector` does headlessly.
Matching by name on the drawn grid would pick an arbitrary one of a deck's four Strikes, which is a different run and would not always be a failing one.

It also stops draining. The headless host drains the engine to idle after every
action because it owns the process and there are no frames to do it; the retail
client's action executor runs on the frame loop, on the thread the call arrives on,
so waiting for it there wedges the game rather than settling it. The driver hands the
engine's task back through `RunDriver.Pending` and the in-game host waits for it on
the game's own frames.

### Recording a run headless, and the two seams it needs

The recorder and its fight observer run in the retail client, and two things they lean
on there are absent headless. Both are seams the attach site supplies rather than
constants named inside the recorder, so the merge suite can drive a whole recorded run
without a client - which is what `HeadlessGameplayCaptureTests` in
`Sts2PilotTrainer.Mod.Tests` does, and why the recorder screen-coverage failure class is
caught before a merge rather than by playing.

- **The attach identity.** `RunRecorder.Attach` records the build and content the run is
  played on, and in the client it takes them from adopting the running game -
  `RunmobileMod.EnsureAdopted`, which `EngineHost.AdoptRunningGame` grants. That refuses a
  process that started its own headless engine, by design, so `RunRecorder.GameIdentitySource`
  is the seam: a headless attach supplies a source that says the engine is up, and the
  identity `LiveRun` writes then comes from the prepared copy - honestly, because that is
  the engine the process runs.
- **The settle clock.** Both the recorder's between-decision settle and the observer's
  after-action settle wait for the engine to go quiet on a clock, and in the client that
  clock is the scene tree's timer (`RecordedFightRun.LetTheGameRun`), the only thing this
  process's [in-game host](in-game-host.md) has for waiting. There is no scene tree
  headless, so `SettleClock` is the seam and `RunRecorder.Clock` carries it: the retail
  client's is `SettleClock.SceneTree`; a headless attach supplies one driven by the
  arbiter's drain instead of by frames. The observer binds its clock at
  `PlayerFightObserver.Start`, so the clock is chosen when the fight begins, not swapped
  under it.

A recording captured headlessly is a real recording, and it is not a retail-clean one:
its patch roster names the headless host's own patches, which the environment preflight in
front of `./scripts/arbiter replay` refuses - correctly, since that gate is about a clean
retail game. So the merge-suite proof replays a natural run's own actions in-process, past
that gate, to show the recorded actions reproduce every declared boundary digest; the
retail-cleanliness the CLI checks is a separate question.

### The headless flag

`TestMode.IsOn` is set. This is the switch the game's own automated tests use, and
the only supported way to make room, card, creature and banner constructors return
null instead of loading a scene.

It is **not** free of gameplay reach, and this is the one place where care was
needed. `RunManager.ShouldApplyTutorialModifications` reads, in order: an override,
then this flag (returning false when it is on), then the game mode — and for a
standard run in retail it returns **true, always**, not only on a first run. So
switching the headless flag on silently disables a behaviour every standard retail
run has, and `GenerateRooms` uses it.

The host therefore sets `RunManager.ForceDiscoveryOrderModifications` back to true
for standard runs, which is exactly what retail computes for them. Daily and custom
runs return false from the same method and get false here.

This initialization belongs only to the headless arbiter.
The in-game host calls `Preflight.Evaluate` before it constructs a recorded run, and it must not embed this entry point, because `EngineHost.Start` enables test mode and installs the headless patches above inside its process.
It calls `EngineHost.AdoptRunningGame` instead, which takes the engine the client already has and refuses anything it cannot read honestly.
Executable boundary tests verify that a console process and duplicate game assemblies are refused without changing the prepared game inputs or sandbox profile.
The removed source-reference scan is not treated as evidence that the mod avoids `EngineHost.Start`.
See [the in-game host](in-game-host.md).

That was found by reading the method, not by noticing a symptom.

### Four places the flag changes what the game generates

`ShouldApplyTutorialModifications` was not the only one, and the rest were found by sweeping the class rather than by chasing a symptom.
All 393 reads of `TestMode` in `sts2.dll` v0.111.0 were enumerated; 273 are in the presentation, asset, modding, localization, platform, logging and multiplayer namespaces; of the 120 in gameplay code, most skip an animation wait, a scene node, a tween, an audio cue, a log line, a history or statistics write, or a test-only assertion, and none of those can move a random stream because the file they are in never touches one.
Four do move one, and `HeadlessPatches.RestoreRetailBranches` puts each of them back by turning the flag off for the duration of that one call and letting the engine's own retail branch run.
Nothing there reimplements a cost, a roll or a pool.
The first sweep counted three; the fourth, `ScrollBoxes.GenerateRandomBundles`, was found when the bundle screen it feeds was mapped, which is the sweep the rule above asks for when a site is added.
It hands the Deprived three copies of Claw under test mode where retail draws every bundle from the rewards stream, so it is the same shape as the other three and is restored the same way.

| Restored | What test mode was doing | What it cost |
|---|---|---|
| `MerchantPotionEntry.CalcCost` | Skipping `PlayerRng.Shops.NextFloat(0.95, 1.05)`, which prices one potion slot | Three slots per merchant, so `player.rng.Shops` fell three draws behind the recording's from the first shop and stayed there |
| `Cauldron.GenerateRewards` | Handing back five hard-coded potions instead of constructing rewards that populate themselves | The potions the run received, and `PlayerRng.Rewards`' position |
| `CallingBell.GenerateRewards` | Handing back Anchor, Gremlin Horn and Mummified Hand instead of pulling one relic per rarity | The relics the run received, and the position of the run's own relic grab bag |
| `ScrollBoxes.GenerateRandomBundles` | Handing the Deprived three copies of Claw instead of drawing each bundle from the rewards stream | The bundles the run was offered, and the stream's position, for that character |

The merchant one was live and measured.
Two native recordings of whole runs, replayed against the engine, disagreed at eight of nineteen and eight of twenty-six boundaries — every disagreement from the shop's own floor entry onward, every recorded digest reproduced by `Shops+3` and by nothing else, and `replay` exiting 0 throughout.
`ReplayTests.NativeRecordingReproducesEveryBoundaryItCaptured` replays both and asserts every captured boundary is reproduced; it is the only check here that can see a bias this host has, because a synthetic fixture's expected values were produced by this same host.

The two relics are the same defect at a site no recording or fixture has reached, so nothing above measures them.
Scroll Boxes is the same again, and its retail branch is reachable only for the Deprived holding that relic; no probe exercises it yet, and that gap is stated here rather than closed by a measurement nobody took.
`./scripts/arbiter engine-commands` measures the first three the same way: it exercises each site through the engine's own construction and reads a consequence retail has and test mode does not - the Shops stream advanced by one, the potion and relic rewards still unpopulated and therefore still to be drawn - and then checks the headless flag is back on.
It pins no price and no relic, because those are the game's to choose and a measurement that pinned one would fail on the next build for the wrong reason.
It rides on that command rather than a verb of its own because it is the same patch-day question the command table already asks: does the host's account of this build still describe it.
With the patches removed all three sites report FAIL and the command exits non-zero, which is what makes its pass mean something; `ReplayTests.EveryRestoredRetailBranchTakesRetailsPath` is what runs it.

The card and relic merchant entries take the same price draw with no guard at all, which is why only the potion slots drifted.
A name in that list that stops matching a future build is a startup **failure**, not a warning: a fidelity patch that silently stops applying is a host that reproduces nothing and says so nowhere.

### Localization: stubbed empty

The real translation tables live inside the game's Godot resource pack, which a
headless process has no reader for. Rather than extract and redistribute MegaCrit's
text — which this project will not do — every lookup is patched to succeed and
return its own key.

This is safe because nothing the project compares is localized: the canonical state
is model ids and numbers, and display text is on the canonical form's excluded list
by design. It costs legibility in a raw dump, which is what model ids are for.

**One honest caveat.** The game can pick a random string from a table
(`LocString.GetRandomWithPrefix`), and against empty tables there is nothing to pick
from. If any gameplay path drew from a run-persistent stream to choose flavour text,
the stub would move that stream. The proof this milestone runs would catch such a
divergence — it compares generated map topology and combat outcomes against a real
video — but that is a bound, not a proof of absence.

### No mods

The mod loader is declared finished with nothing loaded. This is deliberate and it
is what makes the content hash meaningful: the hash this host reports is the base
game's, so a match against a video's overlay says the video's environment agreed
with the base game about what content exists.

### Writes are confined

`OS.GetUserDataDir` and `ProjectSettings.GlobalizePath` are routed through a sandbox
directory the process owns, and any filesystem operation that resolves inside a
Steam or Slay the Spire 2 path throws rather than proceeding. The player's install
and saves are inputs, and the engine has no idea that is the arrangement.

## The evidence that generation is unaffected

Arguments about which patches are "only presentation" are worth exactly as much as
the measurements behind them. These are the measurements.

**Act 1 map topology matches the source video exactly** — all 61 transcribed nodes
across 15 of the map's 16 rows, read from five frames at source resolution. Map
generation runs through the act model, the room set, and the path pruning and
post-processing passes. It comes out identical to what a retail client on a modded
install produced.

**The headless experiment matched 141 independently observed VOD values** — including the enemy state, ordered hand, pile counts, energy, block, gold, the potion belt, the deck size, and the outcome of every turn of two whole fights.
This is evidence that the experiment followed the same path through the covered prefix: two combats from their opening hands to their killing blows, the loot each of them offered, an event that spent 99 gold enchanting two cards, and the first two turns of a third fight.
The source's three visible-build utilities are non-gameplay tooling, but the target-level BaseLib probe demonstrates a behavior difference for a player-applied custom debuff.
A separate history-bound probe therefore records every `PowerCmd.Apply` call in the reconstructed actions and must prove that branch unreachable with an injected affected-call negative control.

**The replay machinery has independent synthetic evidence** — a mechanically generated fixture uses a seed and action sequence absent from the VOD artifacts and pins its engine-produced checkpoints.
Fresh-process determinism, corruption rejection, and snapshot restore are exercised against that fixture, so those checks do not borrow their expected values from the ineligible VOD trace.

Regenerating a committed fixture is two commands, in order, and each committed fixture has its own journey.
Every pair below is complete on its own: run one pair to regenerate one fixture, or paste the whole block to regenerate all three.

```bash
D=src/Sts2PilotTrainer.Replay/Fixtures

./scripts/arbiter generate-synthetic-fixture --out $D/synthetic-v0111-pilot-trainer.replay.json --journey first-fight --line reference
./scripts/arbiter migrate-manifest $D/synthetic-v0111-pilot-trainer.replay.json --derive-boundaries

./scripts/arbiter generate-synthetic-fixture --out $D/synthetic-v0111-whole-act.replay.json --journey whole-act
./scripts/arbiter migrate-manifest $D/synthetic-v0111-whole-act.replay.json --derive-boundaries

./scripts/arbiter generate-synthetic-fixture --out $D/synthetic-v0111-screen-at-boundary.replay.json --journey screen-at-boundary
./scripts/arbiter migrate-manifest $D/synthetic-v0111-screen-at-boundary.replay.json --derive-boundaries
```

Two ways to get this wrong, and neither one announces itself.
`--journey` defaults to `first-fight`, so omitting it on either of the other two writes a first-fight history at the whole-act or screen-at-boundary path, and the tests that expect nine fights or a card screen at a boundary then fail against a file that is perfectly well formed.
And generation cannot know a boundary - only a replay through the real engine produces one - so a fixture written without the `migrate-manifest --derive-boundaries` step carries no `boundaries` at all, the validator does not catch it, and every game-free test that selects one out of it fails with no hint of why.

## What is still not established

- **This is not the retail client.** Everything above is agreement at points a video
  could show. It is strong evidence and it is not the same as running the game.
- **The source had three non-gameplay utilities loaded and this host has none.** The content
  hash matches, which rules out gameplay-declared content differences and nothing
  else. See [environment identity](environment-identity.md).
- **Unlock state is assumed complete.** It demonstrably changes generated content,
  and it is not observable from a video. See the same document.
- **Only the transcribed prefix is covered.** Every claim here is about the part of the
  run that was transcribed: run start through the opening of the floor-5 fight's third
  turn, which is two complete fights, the loot each of them offered, one event, and
  the first two turns of a third fight. Nothing after that boundary is transcribed.

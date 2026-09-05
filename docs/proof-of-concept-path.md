# The path to a proof of concept the captain can try

This document names the direct path from the merged replay arbiter to a first proof of
concept somebody can personally run against one recent NaveGreed VOD, and what is
actually runnable at each step along the way.

It is a plan, not a contract.
[Comparison direction](comparison-direction.md) owns what a replay result has to keep
and where a replay may start; [environment identity](environment-identity.md) owns
what makes two runs the same run.
Nothing here overrides either.

## The loop the first proof has to close

1. Establish and verify the run's identity: the exact seed, the game mode, the build
   the VOD was recorded on, Ascension 10, and the unlock prerequisites - against the
   player's own installed game and profile.
2. Start, or reset to, the entire captured combat.
3. Let the player fight it to the end.
4. Produce a useful whole-combat comparison against the VOD's solution to that same
   fight.

The unit is the whole fight, and the boundary is combat start.
That is a decision with teeth and it rules out a turn-level reset, a pre-turn branch,
and a solver; see [comparison direction](comparison-direction.md).

## What is already proven

Each of these is computed rather than concluded, and `gate` is where they are
assembled into one verdict.

- **The seed, established without reading it.** `verify-seed` regenerates each
  candidate's Act 1 map through the real engine and compares topology against the map
  transcribed from the video.
- **Environment identity and player prerequisites.** `preflight` compares build,
  content hash, acts, unlock categories and - reading a real profile - ascension
  availability, and refuses with in-game remediation rather than replaying into a
  mismatch. It never writes to a save, a profile or the install.
- **Headless replay of the covered history through the real shipped engine**, with 141
  VOD-observed values reproduced and every checkpoint compared field by field.
- **Determinism across fresh processes**, and rejection of ten deliberately corrupted histories, all of which the publication gate requires to apply; four pass every arithmetic check the frames allow.
- **The combat-start snapshot**, derived, cached under a key bound to the history that
  produced it, and re-derived in a fresh process to be read at all.
- **A stored result that keeps the shape of the fight**, not only its last frame:
  `VerificationReport.Trace` samples canonical state either side of every action.
- **That the relevant mods do not alter deterministic RNG.** `baselib-reachability`
  instruments every `PowerCmd.Apply` in this exact history and proves its detector with
  an injected negative control. This is the one-time conclusion; there is no recurring
  mod-by-mod inference machinery and none is wanted.
- **A fight a person plays, captured as the same trace.** `FightCapture` samples the
  canonical state either side of every action the game's own executor announces, and
  refuses a trace that is not a continuous record of the fight. The recording's own
  actions played through it project to a line the comparison reports as identical to
  the engine's replay of them.

## What this change established, with direct evidence

Three things were unknown, and driving the real engine settled them.

**A whole combat does complete in the headless host.** Driven from run start through
Neow and the first map node, the opening fight of seed `P1L0TTRA1NER` resolves to a
victory on turn 5 with the player at 64 of 80 health, and the run continues onto the
next floor with its combat rewards. Before this, no fixture had ever carried a fight
past its opening turn, and `docs/comparison-direction.md` said so.

**The arbiter could not see that it had happened.** `combat.in_progress` was projected
from `PlayerCombatState is not null`, and that object outlives the fight: after the
enemy died it still read `true`, with the player's turn phase at `None`.
Every quantity the comparison needs - total turns, net health change, final health - is defined over a *completed* fight, so a result that cannot tell a finished fight from a live one cannot carry any of them honestly.
`CombatManager.IsInProgress` is the signal that
actually tracks the fight, and it read `false` with `IsOverOrEnding` true at exactly
the right moment.

**The engine's own combat history cannot be the store.** `CombatManager.History` does
carry round-stamped, attributed entries while a fight is running - damage received with
its dealer and card source, potions used, monster moves. It is also cleared when the
combat ends: read after the victory, it held zero entries. So it is not a source a
stored result can consult afterwards, and the comparison contract derives from the
replay trace, which is kept.

## The slices, in dependency order

### S1 - Complete-combat replay, and the comparison contract - done

Make a finished fight observable and give its two projections one owner.

- `combat.outcome` in the canonical state, sourced from the engine's own combat
  lifecycle, and `combat.in_progress` corrected to mean the fight is live.
- The synthetic engine fixture carried through to the end of its first combat, so
  there is a real completed fight to compute over.
- `CombatProjection` in `Sts2PilotTrainer.Replay`: the combat summary and the turn
  detail, derived from the trace, kept apart, computing nothing that ranks a line.
- `CombatComparison`: two completed fights whose complete canonical combat-start
  snapshot digests match, put side by side.
- `combat-compare`, which does all of that from the command line.

**Runnable now:** `./scripts/arbiter combat-compare <a> <b>` prints the summary and
turn detail for two completed fights and their differences, and `combat-snapshot` no
longer calls a finished fight an active one.
This was step 4 of the loop, with engine-produced lines standing in for a human's, which was all that could be honest before the mod host existed.
S2 added the recording's own fight as another completed side; S5 later added the captured player's side.
[demo/DEMO.md](../demo/DEMO.md) has the engine-replayed comparison with its real output, and [demo/PLAYER-FIGHT-COMPARISON.md](../demo/PLAYER-FIGHT-COMPARISON.md) has the captured player's side.

### S2 - The VOD's complete first combat in the manifest - done

The shipped manifest covered five actions: Neow, the map move, two cards and the end
of turn one.
A comparison against "the VOD solution" needs the VOD's whole fight, read from the
video the same way those five were, with the same provenance and timestamps.

- Six more actions, read from the recording at 3840x2160 and carrying the timestamp
  that lets anyone re-check them: two Defends on turn 2, the second Hellraiser on turn
  3, the two turns those ended, and the Bash that killed the enemy on turn 4. Eleven
  actions, four turns, one victory.
- Six more checkpoints, and 47 observed values compared field by field, up from 20.
  The ones that carry the reconstruction are the two turn-start hands: Hellraiser
  plays every Strike the draw turns up, so the hand a turn opens with is a fact about
  where the shuffle put five cards, and the video shows it.
- `covered-fight` in the publication gate: the reproduced history has to cover a
  fight from its combat start to the end of that fight. Read through
  `CombatProjection.CoverageOf`, the same reader the projection refuses on, so the
  gate and the comparison cannot disagree about whether a fight ended.
- The negative controls nominate the turn-1 Defend. Without a nomination they damage
  the last play, which in a fight replayed to its end is the killing blow - and
  omitting the killing blow leaves a shorter history that is self-consistent.

This was authoring one manifest, not building an ingestion pipeline, and it stayed
that way. No code in this repository reads a video: the frames were read at source
resolution and what they show was written into the manifest by hand, each value with
the timestamp that lets anybody open the public recording and disagree.

**Runnable now:** `./scripts/arbiter gate manifests/navegreed-OJ-6QXhNgdg.replay.json`
returns `PUBLISHABLE` over a complete fight, and `combat-compare` puts the VOD's real
solution on one side.
At S2, the only second line available was the recording itself, so that demonstration compares the recording against itself and says so.
S5 adds the independent line captured from a fight played in the retail client.

### S2.5 - The prefix to the two-enemy window - done

S2 stopped at the first fight's victory because the driver stopped there: four verbs,
and the next thing the recording shows is a loot screen. Reaching any later fight
needed the decisions between them, and the decisions between them needed verbs.

- Five more verbs, each mapped to the engine's own command for it: `ClaimReward` and
  `TakeCard` and `SkipRewards` through `RewardsSetSynchronizer`, `ChooseEventOption`
  through `EventSynchronizer`, `SelectCardFromScreen` through the `ICardSelector` seam
  the game's own tests use. Nothing was invented and nothing was approximated; where
  the engine had no command - the loot screen appearing, a card screen asking - the
  host stands in for the UI and the manifest still makes every decision. See
  [headless fidelity](headless-fidelity.md).
- Five, not the six this path was thought to need. `ProceedToMap` was not implemented,
  because returning to the map is presentation: the state change is entering the next
  node, which `MapMove` already is, and a verb standing for a screen transition would
  be a decision the run does not contain. The verb names a reconstruction needs are
  settled by asking the engine which command each click reaches, not by naming the
  screens a viewer sees.
- Thirty-five more actions and eleven more checkpoints, read off the recording the
  same way the first eleven were: floor 2's loot and the card taken from it, the
  Waterlogged Scriptorium and the two cards it enchanted with Steady, the whole
  five-turn floor-4 fight against two Toadpoles, floor 4's loot and the card reward
  declined, and the first two turns of the floor-5 fight against two Corpse Slugs.
  141 observed values compared field by field, up from 47.
- `player.deck_count` in the canonical state. The ordered deck is not readable from a
  video - the deck screen sorts - and the badge in the top bar is on every frame.
- One value deliberately not checkpointed. Each Corpse Slug carries a status badge
  reading 5, drawn as an icon with no text, and the recording never hovers it. The
  count is legible and the power's identity is not, so `combat.enemy.N.powers` is
  absent from that boundary rather than filled in from the engine.
- Six more negative controls, one for each newly reachable kind of decision: a claimed
  reward declined, a different card taken from the reward, a different copy of the
  same card enchanted, a different event option, a different enemy targeted, a
  different map node. Four of the ten controls now pass every arithmetic check the
  frames allow.

**Runnable now:** `./scripts/arbiter replay manifests/navegreed-OJ-6QXhNgdg.replay.json`
reaches the floor-5 two-enemy fight and reproduces the hand the recording shows at the
opening of the 209-215 second turn - `Bash` carrying Retain, two Strikes and two
Hellraisers - along with both enemies' health, the player's, the gold and the Frail
stack. That is the trusted prefix an engine-constrained candidate search over that
window would have to start from; the search itself is not built and is not next.

### S3 - The in-game mod host - done

`Preflight` was already the API a host must call before showing a player anything, and
nothing had ever called it from inside the retail process. `preflight-live`
demonstrates it in the headless sandbox and refuses by design, because the sandbox
profile is empty.

This slice produced the pre-rename `CombatTrainer` mod that loaded in the shipped game and called it against the player's real profile and run, refusing with the same actionable remediation when the game could not faithfully represent the VOD.

- `CombatTrainer`, a DLL-only mod the game discovered, loaded and initialised through its own mod surface.
  No framework, no dependency, no resource pack.
- A fourth mode card beside Standard, Daily and Custom, duplicated from the game's own
  card so the panel, focus, hover and controller navigation are MegaCrit's rather than
  a lookalike. It opens one screen, built from the game's own modal popup.
- `EngineHost.AdoptRunningGame`: the engine can now be *taken* as well as built.
  It refuses anything it cannot read honestly and names the assembly it read.
  One of the four boundary tests drives the console refusal and verifies that the prepared game inputs and sandbox profile remain unchanged.
  Another loads a duplicate game assembly and proves that state refuses before adoption.
  A third proves that adoption still refuses during essential initialization, before the model database and id-serialization cache have both finished.
  The fourth parses the mod manifest and verifies its non-gameplay, DLL-only, packless contract; no source-reference scan is presented as behavioural evidence.
- `EnvironmentPreflight.LiveGame` and `Preflight.EvaluateLiveHost`: the same two gates,
  kept separable, so "you have not started the run yet" is distinguishable from "your
  install cannot play this" without softening either.
- `Sts2PilotTrainer.Trainer`: what the screen says, with no game code, so every row and
  every sentence has a test on a machine that does not own the game.

**Established by this slice:** the captain was told, in game, whether his install and profile could represent this VOD - and if not, exactly what to play to fix it.
S4 extended that same pre-rename host with the current fight offer and asked the supplied run model about capabilities the trainer provided in memory.
The `CombatTrainer` session establishes nothing about the renamed `Runmobile` artifact; S7's session is where that was exercised, and [docs/in-game-host.md](in-game-host.md) says what it left unproved.
[docs/in-game-host.md](in-game-host.md) records the current code and its limits; [demo/IN-GAME-HOST.md](../demo/IN-GAME-HOST.md) preserves the historical S3 `CombatTrainer` evidence.

### S4 - Start or reset the captured combat, in the live game - done

Construct the run at the VOD's identity inside the retail process and enter the
captured fight at the combat-start boundary.
The snapshot machinery already defined that boundary; this is what walks a run to it
and proves it arrived.

- `RecordedFightPlan` and `BoundaryEquality` in `Sts2PilotTrainer.Replay`: the
  recording's decisions before its fight, the boundary they end at, and the two
  readings a live entry is compared against there.
  Pure, so both have tests on a machine with no game.
- `RecordedFightEntry` in `Sts2PilotTrainer.Engine`: the one owner of standing
  somebody in that fight.
  It constructs the run, makes the recording's decisions in order, and refuses three
  ways - over a run that already exists, past a decision the plan does not authorise
  at that point, and into a fight whose combat start is not the recorded one.
- The progress the run is generated against is supplied rather than read.
  The recording requires the complete unlock state its content came from, and the
  player's profile is not it; `Preflight` already distinguishes a supplied model from
  a reading, and `EnvironmentPreflight.EvaluateAscensionCeiling` already says a host
  constructing a run directly never consults the profile ceiling.
  That is what lets the captain stand in an Ascension 10 fight from an Ascension 9
  profile without a byte of his progress changing.
  The eligibility screen is asked about that same supplied model, so every row states
  a requirement of the fight on offer rather than of a run nobody starts by hand -
  otherwise the ascension row would sit in red above an offer it does not stop.
- `ProfileWriteBarrier` in the mod: `shouldSave: false` covers the run save and
  everything at the end of a run, and it does not cover the two writes on this
  fight's path - winning a combat rewrites the progress file, and an event room saves
  the run with progress saving defaulted on. The barrier stops the writes themselves,
  is installed at mod start and does nothing unless a trainer run is live, so a
  crash, a forced exit and a quit are all covered by the write never happening. It
  comes down on the game's own end-of-run path, because one left raised would stop
  saving the player's next run.
- A deviation lock on the two commands the recording's decisions reach, rather than
  on the buttons that usually reach them: a screen with its buttons hidden is one a
  controller, a hotkey or another mod can still drive, and the command is the thing
  that would change the run.
- `source.video.channel_name`, so a host names whose recording this is from the
  manifest.
  Every sentence the journey shows is a template over what the run is standing in
  front of - the relic the blessing grants, the kind of node the move enters, how
  many decisions there are - and nothing about NaveGreed, the Underdocks or a Sludge
  Spinner is written into the wording.

**Runnable now:** `./scripts/arbiter enter-fight manifests/navegreed-OJ-6QXhNgdg.replay.json`
constructs the run, walks it through Neow's blessing and the map move with the
captions the in-game screens use, and reports the fight it lands in as the recorded
one on all thirteen values the recording observed and on the manifest's
engine-produced combat-start snapshot digest - with the profile reading and every byte of the profile store
unchanged either side. `--control wrong-opening-choice` damages one recorded decision
and the entry is refused.
[demo/RECORDED-FIGHT-ENTRY.md](../demo/RECORDED-FIGHT-ENTRY.md) has it with its real
output.

**Demonstrated in the retail client with the pre-rename `CombatTrainer` artifact.**
With only Combat Trainer enabled, the screen offered the fight; pressing it constructed the recording's run, walked it through Neow's blessing and the map move on the game's own screens, and stood the player in the recorded fight - the Sludge Spinner at 42 of 42, the opening hand the recording shows, turn 1 at Ascension 10.
The canonical state at that boundary was the same digest the headless host derived for the combat-start snapshot, so the agreement covered the run's random streams and the draw pile's order and not only what a screenshot showed.
S7's session repeated the entry through the renamed `Runmobile` package with only it enabled and a clean protected-files ledger; nothing here is claimed for that package on this slice's evidence.
[demo/RECORDED-FIGHT-ENTRY.md](../demo/RECORDED-FIGHT-ENTRY.md) has the historical `CombatTrainer` screenshots.

Running it in the client is what found the three screen-owned transitions the manifest
has no verbs for, and the fact that this mod cannot tick a frame;
[docs/in-game-host.md](in-game-host.md) records both.

### S5 - The player's own fight, compared - done

Capture the player's completed fight as a trace through the same projection, feed both
sides to the S1 contract, and show him the result.

- `FightCapture` in `Sts2PilotTrainer.Replay`: the player's fight sampled either side
  of every action into the same `ReplayTrace` the headless arbiter produces, so it
  goes through the same `CombatProjection` and the same `CombatComparison` rather
  than a second reading.
  It is a lifecycle and it refuses to be read early: a projection is handed over only
  once the fight ended inside a sampled action.
  A fight that was left, or whose state moved between two samples with no action in
  between, is refused with a sentence rather than bridged; the trace is kept either way.
  Pure, so every rule has a test on a machine with no game.
- The samples come from the same canonical projection the checkpoints and the
  headless trace read, filtered by `ReplayTrace.Sample`, which is now the one owner of
  what a trace keeps.
- `RecordedFightEntry.BeginCapture` starts capturing at the boundary just proved and
  from nowhere else, carrying the digest the comparison then requires to be the
  recording's.
  `PlayRecordedFightHeadless` plays the recording's own actions through that capture
  so the command line can exercise the whole loop with the recording standing in for
  the player.
- The recording's side cannot be replayed in the client - one process, one run, and
  it is the player's - so it is produced headlessly by `./scripts/arbiter
  recorded-fight` and shipped inside the mod as
  `manifests/navegreed-OJ-6QXhNgdg.recorded-fights.json`: the engine-produced trace
  through the end of each cut fight, bound by run id and, per fight, history hash and
  combat-start boundary.
  `RecordedFights.Bind` refuses it at mod start unless it is the replay of exactly the
  shipped manifest, and a test regenerates it in a fresh process and compares.
- `PlayerFightObserver` in the mod subscribes to the game's own action executor,
  which announces every action before it runs and after it finishes, and to the
  combat manager's own turn-started and combat-ended events.
  It issues no command and patches nothing.
  What it owns is when the after-sample is taken: when the queue is empty and the
  executor idle, which is the moment the headless drain reaches; for an ended turn,
  once the player's next turn has started; and for the action the fight ended
  inside, the combat manager's own event closes it with the final state.
- `FightResultScreen` in `Sts2PilotTrainer.Trainer`: the approved wording over a
  comparison, and the one sentence shown instead when there is none - a fight left,
  a capture that could not be completed, a fight not won, or a comparison that
  refused, shown in its own words.
  A lost, abandoned or incomplete fight never produces a comparison.
- Done discards the run the way a refused entry does, and the game's own end-of-run
  path lowers the write barrier; a fight left through the game's own menu abandons
  the capture the same way.

**The result the first retail session asked for is what is here now.**
The captain played the fight, read the text-led list of differences on a large modal
and reported that prose describing how his fight differed from the recording's was not
the interface. The panel it became is drawn: the summary as figures in two columns,
the turn chronology as the game's own card and potion art in the order it was played,
and the chart of what each turn cost either side, with the two lines told apart by
colour and by marker shape. `FightResultPanel` in the mod draws it and decides
nothing; `FightResultChart` derives the chart from the comparison and leaves a gap
where a value cannot be derived honestly.

[demo/VISUAL-COMPARISON.md](../demo/VISUAL-COMPARISON.md) has the drawn panel from the
retail client over a deliberately different line, the two no-comparison paths it also
produced there, and what that session changed on disk.

**Runnable now:** `./scripts/arbiter enter-fight manifests/navegreed-OJ-6QXhNgdg.replay.json --play`
stands in the fight, plays the recording's nine actions through the player-side
capture, and prints the result panel as far as a terminal can draw it - the figures,
the chronology and the chart's own numbers - every row the same on both sides because
the recording stood in for the player.
[demo/PLAYER-FIGHT-COMPARISON.md](../demo/PLAYER-FIGHT-COMPARISON.md) has it with its
real output, and the retail client's own result over a fight a person played.

### S6 - The whole run, replayed headlessly - done

S1 through S5 close the loop over one fight.
Everything after that fight - a rest site, a shop, a treasure room, the act boss, the
act transition - was outside the alphabet the driver could replay, so a history that
contained any of them refused.
Phase 4, standing a player in any fight or floor, needs the run replayed past the
first fight before it can offer a later one.

- The rest of the decision alphabet, each verb mapped onto the game's own command for
  it: rest sites, shops, potions used and discarded, treasure rooms and act
  transitions. Nothing was invented; where this build has nothing to map a verb onto,
  the verb stays unimplemented and says why.
- `EngineCommands` in `Sts2PilotTrainer.Engine`: which member of the game each
  recorded decision reaches, in one table rather than spread across the driver's
  handlers. The driver's refusal for an unimplemented verb is derived from it, and the
  recorder reads the same table from the other end - a decision the driver issues is a
  decision a running game announces. `./scripts/arbiter engine-commands` prints it,
  including the three verbs that map onto nothing here and the reason beside each.
- A digest at every boundary the history passes, not only at its first fight.
  `RunCoverage` derives *where* the boundaries are, as a rule over the history with no
  engine; what each one holds needs a replay, so `migrate-manifest --derive-boundaries`
  writes the digest that replay produced and refuses if the history does not reproduce.
  The validator holds `boundaries[]` to the closed set of kinds a host dispatches on.
- Entry at any boundary. `BoundarySelector` is the one reader of a boundary
  coordinate and the one place a coordinate becomes a plan, however it was spelled:
  `combat-snapshot --boundary combat_start:2` or `floor_entry:5`, and `enter-fight
  --fight <n>` or `--floor <n>`. A floor arrival is proved by where the run stands, so
  entering one needs a checkpoint there naming `run.total_floor` and `run.map_coord`.
- Two committed engine-generated fixtures to exercise it against, because no
  transcribed video reaches any of this. The whole-act history is 225 actions through
  a whole Act 1 to `ProceedToNextAct`, with 67 boundaries - nine fights, sixteen floor
  arrivals and forty-two turns - each carrying the digest a replay produced. The
  screen-at-boundary history walks the same act and stops at the first turn whose own
  action opens a card screen, which is the one case no other history here reaches.
- The `exact` unlock arm, which is how a recording made inside a player's own game
  says which state its content was generated against, rather than requiring a complete
  one. It is present and deliberately unfinished: the preflight checks that this build
  ships every epoch and encounter id the recording names and reports the run count, and
  nothing produces an `exact` recording yet, so the arm is inert until the recorder
  does. [Environment identity](environment-identity.md) owns what such a state is made
  of and what "exact" can mean.

**Runnable now:** `./scripts/arbiter replay
src/Sts2PilotTrainer.Replay/Fixtures/synthetic-v0111-whole-act.replay.json` reproduces
a whole act through the real engine, and `./scripts/arbiter enter-fight
src/Sts2PilotTrainer.Replay/Fixtures/synthetic-v0111-whole-act.replay.json --floor 5`
stands the run at that floor's arrival with the digest that boundary records.
The shipped video reconstruction records no map coordinate anywhere, so its floor
boundaries are declared but not enterable and `--floor` refuses on it; `--fight` works
on both.

<<<<<<< HEAD
### S7 - One playback transport, in the retail client - done

Replace the per-step popup with one long-lived transport that carries the watched
journey through the map-to-combat transition, and prove in the client the three
things a wider playback design depends on.

- `PlaybackTransport` in `Sts2PilotTrainer.Trainer`: the one owner of what the
  transport says at each moment - the chip, the counter, the caption, the once-only
  sentence, and the three controls with whether each is offered.
  `PlaybackTransport.For(phase, facts)` is the only way to get one, and it is total:
  every phase a journey can be in has an answer, null included for the two that put
  nothing on screen. `Surface` is the per-element table the strip draws.
  Pure, so every state has a test on a machine with no game.
- `PlaybackTransportStrip` in the mod draws it from stock Godot nodes, so it is
  asserted on node by node in a process with no game too, and `PlaybackTransportDock`
  parents it to `NRun.GlobalUi` - the run's own persistent interface, which the game
  swaps rooms underneath - and docks it in the band under the top bar, measured off
  the top bar rather than written down.
- `PrefightTarget` in `Sts2PilotTrainer.Engine` says where the recording's next
  decision lands on the game's own screen, beside `PrefightChoice` which says what it
  was; the coordinate is the game's own type, which is why the two are separate.
- `RecordedFightReveal` applies the game's own selected state to that target and
  never its click path.
  It refuses a screen it cannot drive, a coordinate this act's map does not draw and
  an option row granting a different relic from the recorded one, rather than
  committing a decision unseen.
- Forward commits one recorded action, Play runs the sequence with a hold on each -
  shorter on the map, where the game supplies a second of its own - and Back re-shows
  a decision already made without rewinding anything.
  During the player's own fight the strip collapses to a chip that says nothing until
  it is pressed, and offers two directions when it is: back to the proven combat
  start, or to the end of the attempt. Both leave the attempt and both confirm first,
  and both are refused - silently - once the fight has ended and its result is waiting
  to be shown.

**Runnable now:** `./scripts/arbiter enter-fight manifests/navegreed-OJ-6QXhNgdg.replay.json`
prints, for each recorded decision, exactly what the transport says and what it would
light on the game's own screen, and refuses a target it cannot name before anything is
committed.

**Runnable now, in the retail client.** `./scripts/install-mod.sh`, then launch with
only `Runmobile` enabled: the strip appears over Neow with the blessing ringed,
Forward commits it and reveals the map node, and the same strip is still there in the
fight, collapsed to a chip.
[demo/PLAYBACK-TRANSPORT.md](../demo/PLAYBACK-TRANSPORT.md) has it with the
screenshots.
=======
### S7 - The recorder, in the player's own game - built, not yet proved by play

S1 through S6 close the loop over somebody else's recording, transcribed from a video
by hand. This is the other direction: the player's own runs become recordings of the
same kind, without a video and without a transcriber.

- `RunCapture` in `Sts2PilotTrainer.Replay`: the whole-run counterpart of
  `FightCapture`, delegating the inside of each fight to one so there is a single
  capture path. It records the run's identity as captured facts, one captured action
  per decision, and a captured checkpoint and digest at every boundary `RunCoverage`
  finds. It refuses a run whose start it did not witness, marks `continuity = broken`
  when a resumed session's live state is not the state its journal last recorded, and
  never truncates.
- `RunJournal`: a header and a line per decision, appended as the run is played, so
  finishing a write means finishing a line. A crash leaves a real recording of the
  part of the run that happened. `RunCapture.Resume` rebuilds the capture from it, so
  a continued session publishes exactly what an uninterrupted one would have.
- `RunRecorder` and `RecorderModule` in the mod: the hooks, the settle rule, and the
  translation from what the game announces into what the format records. Inside a
  fight it hands over to the same `PlayerFightObserver` the Combat Trainer uses,
  through `IFightSampleSink`. It never raises the write barrier - the player's own run
  saves normally - and it declines to attach while a trainer run is live.
- `LiveRun` in the engine: the canonical state, its digest, the run's clock and start
  time, and the run's identity including the exact unlock state it was generated
  against, read out of the run itself rather than out of a profile that can change
  while it is being played. That is what makes S6's `exact` arm live.
- `gate` gains a native arm. The four conditions that read a public video are absent
  for a recording made inside the player's own game rather than reported as met, and
  the artifact records why. Every condition that replays the history through the real
  engine applies to both kinds.

**Runnable now:** the whole pure half. `dotnet test` exercises the capture, the
journal, the continuity rule and the validator's acceptance of what the recorder
produces without the game, and the game-dependent tests pin every member each reading
goes through and every method the recorder patches.

**Not yet proved:** a recording produced by play. The client loads the mod and reports
`Recorder installed`, and nothing beyond that has been shown - no run has been recorded
by a person, so no recording has been through `gate`, and the settle rule, the argument
readings and the boundary digests have not been checked against a replay of a real
one. The completion bar is unchanged and unmet: a recording produced by play, not by an
agent, `PUBLISHABLE` on the machine that made it. [The in-game
host](in-game-host.md#producing-a-recording-and-checking-it) has the steps.
>>>>>>> 197508f (docs: name what the recorder is proved to do and what it is not)

## Known limits that no slice above removes

**The source game mode is not identified.** Standard and custom-with-no-modifiers agree
at every observed checkpoint, and every single modifier of the build's 17 changes one.
The gate reports path-specific parity across the enumerated configurations, not an
identification, and it says so.

**Damage absorbed by block is not included in health lost.** Enemy health lost is the
decrease in enemy hit points, so enemy block depletion is not counted. Player block
is reset at the start of a turn and the trace samples either side of an action rather
than inside one, so player health lost likewise reports only the damage that got
through.

**The player's line is captured, not replayed.** The recording's line is reproduced by
the engine from a history that can be replayed again; the player's line is what the
capture saw of a fight that happened once. The two go through the same projection and
the same comparison, and the capture refuses a trace with a gap in it, but a captured
line cannot be re-derived the way a replayed one can. What can be shown is that the
recording's own actions, played through the capture, project to a line identical to
the recording's replay - and that is what the headless test pins.

**Only a won fight is compared.** The recording's fight was won, and a lost or
abandoned fight has no completed line to set beside it; the panel says so and shows
nothing else. Comparing two losses is not a thing the comparison refuses, it is a
thing no recording here has.

**The transport carries only the two decision kinds this path uses.** An opening
blessing and a map move are what the transcribed prefix contains, and they are what
the reveal can point at. Every other screen between fights - loot, card rewards,
rests, shops, treasure, act transitions - is refused by the reveal for the same
reason the driver refuses its verb, and the transport says so rather than skipping it.

**Only a prefix of the recording is transcribed.** Run start through the opening of
the floor-5 fight's third turn, which is two whole fights, the loot each of them
offered, one event, and two turns of a third fight. Everything after that boundary -
including the rest of that fight and the forty-four floors beyond it - is not
transcribed and nothing here is a claim about it.

## Deliberately not built

No turn-level reset or branching. No solver. No generalized VOD ingestion and no
multi-VOD support. No presentation designed around rare permanent card removal. No
candidate search: S2.5 built the prefix one would need and deliberately stopped there.

Three verbs the format names are still not mapped, because this build has nothing to map them onto - `SelectHandCards`, `CloseShop` and `ProceedToMap` - each with its reason written beside the table in `EngineCommands` and printed by `./scripts/arbiter engine-commands`.

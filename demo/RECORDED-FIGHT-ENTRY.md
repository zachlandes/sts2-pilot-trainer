# Standing in the recording's fight

*2026-09-02T21:49:26Z by Showboat 0.6.1*
<!-- showboat-id: 854040aa-6ac4-4efd-9cf3-d7707b49a970 -->

This document runs the journey into the recorded fight and records what it actually
printed. Every code block below was executed; the output under it is that run's
output. `showboat --workdir .. verify RECORDED-FIGHT-ENTRY.md` re-runs the lot and
diffs; the blocks are repo-root commands, which is what the working directory is for.

**The claim being tested.** Can a run be constructed at a recording's identity, walked
through the recording's own decisions on the way to its fight, and shown to be standing
in *that* fight - on everything the recording observed and on the hidden state no
recording could - without a byte of the player's progress changing?

This is S4 of [the proof-of-concept path](../docs/proof-of-concept-path.md). What runs
here is the headless host, and it runs the same owner the in-game mod runs:
`RecordedFightEntry` constructs the run, makes the decisions and proves the boundary,
and the mod adds only the frames and the popup. The last section says plainly which
part of that has been watched running in the retail client, and which has not.

## The run, constructed and walked to the fight

One command. It reads the recording, constructs the run the recording describes, makes
the two decisions the recording made before its fight - Neow's blessing, then the map
move - and compares where it lands against the recording's own observation of that
moment and against the cached combat-start snapshot.

Three things in the output are worth reading closely.

The **captions** are not written down anywhere. "NaveGreed" comes from the manifest's
`source.video.channel_name`; "Leafy Poultice" is the relic the event option the
recording took actually grants, read off that option before it is taken; "the Monster
node, centre column" is the kind of node at row 1, column 3 of the map this seed
generated, and where column 3 sits on an act seven columns wide. A different recording,
by somebody else, past a different node, produces different sentences from the same
templates.

The **profile lines** are a measurement, not a promise. They bracket the whole journey:
the unlock inventory and ascension ceiling this process's profile reports, and a hash
over every byte of the profile store. The run is generated against a complete unlock
state supplied in memory for that run - which is why the ascension ceiling can read 0
while the run is at Ascension 10.

The **snapshot line** is the part no recording could support. The thirteen values above
it are what a person read off the video; the digest below covers the whole canonical
state, including every run-persistent random stream's position and the order of the
draw pile.

The combat-start snapshot has to exist for that digest to be compared against
anything, so this materialises it first if nothing has. That is `combat-snapshot`'s job
and not this command's: a snapshot is a derived cache, and the only way to read one is
to reproduce it.

```bash
set -o pipefail; ./scripts/arbiter combat-snapshot manifests/navegreed-OJ-6QXhNgdg.replay.json >/dev/null 2>&1; ./scripts/arbiter enter-fight manifests/navegreed-OJ-6QXhNgdg.replay.json 2>&1 | grep -vE '^SentryGodotInitializer|^\[INFO\]|^\[WARN\]|^\[ERROR\]|^   at '
```

```output
recording       : navegreed-OJ-6QXhNgdg
creator         : NaveGreed
progress        : AllUnlocked - UnlockState.all, supplied by the host in place of the source player's profile

profile before  : ascension ceiling 0 for CHARACTER.IRONCLAD; characters 1/5, cards 232/596, card_pools 8/12, character_card_pools 1/5, relics 254/299, potions 45/66, shared_ancients 0/1, epochs 0/57

NaveGreed's decisions before the fight: 2, combat starts after action 1
  NaveGreed's choices are shown as recorded. This shows what was chosen, not why.

  [Watching NaveGreed]  1 of 2   NaveGreed took Leafy Poultice
      action 0 ChooseNeowBlessing option_index=2
  [Watching NaveGreed]  2 of 2   NaveGreed moved to the Monster node, centre column
      action 1 MapMove act=0 row=1 column=3

combat start    : checkpoint 'floor2-combat-start', 13 observed value(s)
  ok   combat.block               recording=0                                                        game=0
  ok   combat.discard_pile_count  recording=0                                                        game=0
  ok   combat.draw_pile_count     recording=6                                                        game=6
  ok   combat.enemy.0.hp          recording=42                                                       game=42
  ok   combat.enemy.0.intent      recording=Attack:9+Debuff                                          game=Attack:9+Debuff
  ok   combat.enemy.0.max_hp      recording=42                                                       game=42
  ok   combat.enemy_count         recording=1                                                        game=1
  ok   combat.energy              recording=3                                                        game=3
  ok   combat.hand                recording=CARD.STRIKE_IRONCLAD|CARD.HELLRAISER|CARD.STRIKE_IRONCLAD|CARD.BASH|CARD.DEFEND_IRONCLAD game=CARD.STRIKE_IRONCLAD|CARD.HELLRAISER|CARD.STRIKE_IRONCLAD|CARD.BASH|CARD.DEFEND_IRONCLAD
  ok   combat.max_energy          recording=3                                                        game=3
  ok   combat.player_hp           recording=64                                                       game=64
  ok   combat.turn                recording=1                                                        game=1
  ok   player.max_hp              recording=68                                                       game=68

snapshot        : cache hit, v0.111.0_standard_CHARACTER.IRONCLAD_a10_SFXT47K77RFK_1568834832_seq1_d0cf798421262bced5ac23bd9d1a3e6457889d455cb638089bc95ede4c1664ec
  recorded      : sha256:979ba9de5e67882643dbd3f45b6eee6ae7d7412441e52b760f040e461752baae
  this game     : sha256:979ba9de5e67882643dbd3f45b6eee6ae7d7412441e52b760f040e461752baae

profile after   : ascension ceiling 0 for CHARACTER.IRONCLAD; characters 1/5, cards 232/596, card_pools 8/12, character_card_pools 1/5, relics 254/299, potions 45/66, shared_ancients 0/1, epochs 0/57
profile writes  : none - the reading and every byte of the profile store are unchanged

ENTERED - this game is standing in NaveGreed's fight, at the recorded combat start.

report: build/evidence/enter-fight.json
```

## The refusal, when one recorded decision is different

Entering a fight that agrees with the recording is only evidence if entering one that
does not is refused. So one of the recording's own decisions before the fight is
damaged - with the project's own negative control, not a drift injector written for
this - and the entry is run again. The rows that agreed are left out below; the three
that did not are the whole point.

The fight it lands in is a perfectly real, perfectly valid fight. It is simply not the
recorded one, and it is not entered. Read the caption on the first step: the control
took a different blessing, and the caption says which, because it is read from the
option rather than written down.

```bash
set -o pipefail; ./scripts/arbiter enter-fight manifests/navegreed-OJ-6QXhNgdg.replay.json --control wrong-opening-choice 2>&1 | grep -vE '^SentryGodotInitializer|^\[INFO\]|^\[WARN\]|^\[ERROR\]|^   at ' | grep -vE '^  ok '
```

```output
control         : wrong-opening-choice - Takes a different blessing at the run's opening event.
recording       : navegreed-OJ-6QXhNgdg+wrong-opening-choice
creator         : NaveGreed
progress        : AllUnlocked - UnlockState.all, supplied by the host in place of the source player's profile

profile before  : ascension ceiling 0 for CHARACTER.IRONCLAD; characters 1/5, cards 232/596, card_pools 8/12, character_card_pools 1/5, relics 254/299, potions 45/66, shared_ancients 0/1, epochs 0/57

NaveGreed's decisions before the fight: 2, combat starts after action 1
  NaveGreed's choices are shown as recorded. This shows what was chosen, not why.

  [Watching NaveGreed]  1 of 2   NaveGreed took Arcane Scroll
      action 0 ChooseNeowBlessing option_index=0
  [Watching NaveGreed]  2 of 2   NaveGreed moved to the Monster node, centre column
      action 1 MapMove act=0 row=1 column=3

combat start    : checkpoint 'floor2-combat-start', 13 observed value(s)
  FAIL combat.draw_pile_count     recording=6                                                        game=7
  FAIL combat.hand                recording=CARD.STRIKE_IRONCLAD|CARD.HELLRAISER|CARD.STRIKE_IRONCLAD|CARD.BASH|CARD.DEFEND_IRONCLAD game=CARD.STRIKE_IRONCLAD|CARD.DEFEND_IRONCLAD|CARD.DOMINATE|CARD.STRIKE_IRONCLAD|CARD.DEFEND_IRONCLAD
  FAIL player.max_hp              recording=68                                                       game=80

snapshot        : none cached under v0.111.0_standard_CHARACTER.IRONCLAD_a10_SFXT47K77RFK_1568834832_seq1_2344a767ba16a6daf98ee6954747281ae5f2accfa113a08d5ac282922012103f; run combat-snapshot to materialise one. Only the values the recording observed are compared.
  recorded      : none cached
  this game     : sha256:8a65e216ecf167ee26e391bd7af6782029bdafd6988752cacb4c52d7e824063f

profile after   : ascension ceiling 0 for CHARACTER.IRONCLAD; characters 1/5, cards 232/596, card_pools 8/12, character_card_pools 1/5, relics 254/299, potions 45/66, shared_ancients 0/1, epochs 0/57
profile writes  : none - the reading and every byte of the profile store are unchanged

REFUSED - This fight did not open the way the recording's did, so it was not entered. At checkpoint 'floor2-combat-start': combat.draw_pile_count: the recording shows '6', this game produced '7'; combat.hand: the recording shows 'CARD.STRIKE_IRONCLAD|CARD.HELLRAISER|CARD.STRIKE_IRONCLAD|CARD.BASH|CARD.DEFEND_IRONCLAD', this game produced 'CARD.STRIKE_IRONCLAD|CARD.DEFEND_IRONCLAD|CARD.DOMINATE|CARD.STRIKE_IRONCLAD|CARD.DEFEND_IRONCLAD'; player.max_hp: the recording shows '68', this game produced '80'. Something before the fight differed, and a fight that starts somewhere else cannot be compared against the recording's.

report: build/evidence/enter-fight.json
```

## The boundary is not somewhere a host may arrive early

The other way to be wrong is to stop halfway and call it the fight. With one of the
recording's decisions still unmade, asking whether this is the recorded combat start is
refused rather than answered - and the counter says how far in it got.

```bash
set -o pipefail; ./scripts/arbiter enter-fight manifests/navegreed-OJ-6QXhNgdg.replay.json --step 2>&1 | grep -vE '^SentryGodotInitializer|^\[INFO\]|^\[WARN\]|^\[ERROR\]|^   at ' | tail -8
```

```output
NaveGreed's decisions before the fight: 2, combat starts after action 1
  NaveGreed's choices are shown as recorded. This shows what was chosen, not why.

  [Watching NaveGreed]  1 of 2   NaveGreed took Leafy Poultice
      action 0 ChooseNeowBlessing option_index=2

--step stops after one decision. The fight is not entered, and asking whether it started where the recording's did is refused rather than answered:
  1 of the recording's decisions before the fight have not been made yet, so there is no combat start to compare against.
```

## The wording, and where every word of it comes from

Nine sentences were approved for this journey. None of them names this recording: each
is a template over what the run is standing in front of, so a second manifest reaches
the screens without a line of copy being edited. The tests below assert the approved
sentence character for character for the shipped recording, and then assert the same
templates saying something else about a different creator, a different blessing and a
different node.

They run on a machine with no game installed, which is the point of keeping the wording
in `Sts2PilotTrainer.Trainer`: what the mod says is answerable by reading one file and
checkable without owning Slay the Spire 2.

```bash
set -o pipefail; dotnet test tests/Sts2PilotTrainer.Trainer.Tests/Sts2PilotTrainer.Trainer.Tests.csproj -c Release --nologo 2>&1 | grep -E 'Passed!|Failed!' | sed -E 's/Duration: [^ ]+ ms/Duration: <duration>/'
```

```output
Passed!  - Failed:     0, Passed:    42, Skipped:     0, Total:    42, Duration: <duration> - Sts2PilotTrainer.Trainer.Tests.dll (net9.0)
```

## The regression coverage around the entry

Seven tests drive `enter-fight` as a real command, each in its own process, because the
engine keeps static state and a claim about what a fresh host does has to be made by a
fresh host. Between them they cover construction at the recording's identity, the
ordered decisions and their captions, the supplied progress reaching the run and never
the profile, equality against both readings of the boundary, the drift refusal, a
control that misses the boundary being refused before the run is built, and the
boundary refusing to be judged early.

```bash
set -o pipefail; dotnet test tests/Sts2PilotTrainer.Arbiter.Tests/Sts2PilotTrainer.Arbiter.Tests.csproj -c Release --nologo --filter 'FullyQualifiedName~RecordedFightEntryTests' 2>&1 | grep -E 'Passed!|Failed!' | sed -E 's/Duration: [^ ]+ (ms|s)/Duration: <duration>/'
```

```output
Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7, Duration: <duration> - Sts2PilotTrainer.Arbiter.Tests.dll (net9.0)
```

## Nothing this run does can be persisted

Setting a run up with saving off is the first defence and not a sufficient one.
`RunManager.ShouldSave` gates the run save and everything at the end of a run, and two
writes on this fight's path sit outside it: winning a combat calls
`SaveManager.UpdateProgressAfterCombatWon` and then `SaveProgressFile`, and an event
room saves the run with `saveProgress` defaulted on. The trainer's run stands in an
event room and wins a fight, so both are reachable, and a player's progress file would
be rewritten from a run that was never theirs.

`ProfileWriteBarrier` stops the writes themselves rather than the flags that reach
them. It is installed once at mod start and does nothing unless a trainer run is live,
which is what makes a crash, a forced exit and a quit all covered: the write never
happens, rather than being undone afterwards. It comes down on `RunManager.CleanUp`,
where a run stops existing on every path there is - a barrier left raised after the
recorded fight would silently stop saving the player's next run, which is the same
defect pointed the other way.

The tests below check the part that would otherwise rot in silence - that every write
the barrier names is a real method on this build, and that it returns nothing or a task
so the barrier can answer its callers without inventing a value - and that with no
trainer run live every one of them is let through, which is what keeps a player's own
runs saving normally with the mod installed.

```bash
set -o pipefail; dotnet test tests/Sts2PilotTrainer.Mod.Tests/Sts2PilotTrainer.Mod.Tests.csproj -c Release --nologo --filter 'FullyQualifiedName~ProfileWriteBarrierTests' 2>&1 | grep -E 'Passed!|Failed!' | sed -E 's/Duration: [^ ]+ ms/Duration: <duration>/'
```

```output
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: <duration> - Sts2PilotTrainer.Mod.Tests.dll (net9.0)
```

## In the retail client

The sections above run the journey's owner headlessly. This section is the same owner
inside the shipped game, driven by hand: Steam running, the mod installed, and every
mod except Combat Trainer disabled through the game's own Mod Settings screen.

Three things below are real retail screenshots. The fourth thing - the player standing
in the fight - is not here, and the section after this one says why.


The eligibility screen, offering the fight. Every row is measured against the state
the run will actually be generated against, which is what the trainer constructs it
with - so the rows state requirements of the fight on offer rather than of a run
nobody starts by hand. The subtitle, the recording line and the creator's name all
come from the manifest.

```bash {image}
![The Combat Trainer screen over the singleplayer menu. Subtitle "NaveGreed · Ironclad · Ascension 10 · Floor 2 · Sludge Spinner", "Recorded on v0.111.0 (2026.08.14)", headline "Your game can play this fight as recorded.", green rows for build and content hash with its scope sentence, and two buttons: Back and Enter the fight.](in-game-fight-offered.png)
```

![The Combat Trainer screen over the singleplayer menu. Subtitle "NaveGreed · Ironclad · Ascension 10 · Floor 2 · Sludge Spinner", "Recorded on v0.111.0 (2026.08.14)", headline "Your game can play this fight as recorded.", green rows for build and content hash with its scope sentence, and two buttons: Back and Enter the fight.](7ec98a92-2026-09-03.png)

Scrolled down the same screen, to the row this reconciliation was about. The captain's
modded profile is not what the run is generated against, so "Ascension 10 available on
Ironclad" is met - and the fight below it is offered rather than warned about. The note
that the fight is not saved is shown with the offer and only with it. There is no
profile note, because no profile was read.

```bash {image}
![The same screen scrolled down. Green rows read "Characters: 5 of 5" through "Epochs: 57 of 57", "Act: Underdocks unlocked", "Act: Hive unlocked", "Act: Glory unlocked" and "Ascension 10 available on Ironclad", above the sentence "This fight is not saved and does not count toward your run history." and the Back and Enter the fight buttons.](in-game-supplied-rows.png)
```

![The same screen scrolled down. Green rows read "Characters: 5 of 5" through "Epochs: 57 of 57", "Act: Underdocks unlocked", "Act: Hive unlocked", "Act: Glory unlocked" and "Ascension 10 available on Ironclad", above the sentence "This fight is not saved and does not count toward your run history." and the Back and Enter the fight buttons.](b806e586-2026-09-03.png)

Pressing "Enter the fight" constructs the recording's run inside the retail client and
stops on the first decision the recording made. Everything in this shot is the game's
own: Neow's screen with all three blessings still legible behind the panel, the top bar
at 64 of 80 health with 99 gold, and the version overlay reading v0.111.0, seed
SFXT47K77RFK, MODDED (1). The panel over it is the game's own popup with no backstop.

The caption is derived, not written down. "NaveGreed" comes from the manifest's
`source.video.channel_name` and "Leafy Poultice" is read off the event option the
recording's action is about to take - which is why the same code says "Arcane Scroll"
when the negative control takes a different one, as the refusal section above shows.

```bash {image}
![Neow's event screen in the retail client with a panel over it titled "Watching NaveGreed". The body reads "1 of 2   NaveGreed took Leafy Poultice" and "NaveGreed's choices are shown as recorded. This shows what was chosen, not why." Two buttons: "Skip to the fight" and "Next". The top bar shows 64/80 health, 99 gold and an Ascension 10 badge; the overlay reads v0.111.0, SFXT47K77RFK, MODDED (1).](in-game-watching-neow.png)
```

![Neow's event screen in the retail client with a panel over it titled "Watching NaveGreed". The body reads "1 of 2   NaveGreed took Leafy Poultice" and "NaveGreed's choices are shown as recorded. This shows what was chosen, not why." Two buttons: "Skip to the fight" and "Next". The top bar shows 64/80 health, 99 gold and an Ascension 10 badge; the overlay reads v0.111.0, SFXT47K77RFK, MODDED (1).](aa5241eb-2026-09-03.png)

## What this proves, and what it does not

**Proved headlessly.** A run is constructed at the recording's identity, through the same
construction the retail client uses, against a complete unlock state supplied in memory
for that run. The recording's two decisions before its fight are executed in its order
and captioned from what the run is standing in front of. The fight that opens is the
recorded one on all thirteen values a person read off the video and on the complete
canonical state the cached combat-start snapshot holds - the run's random streams and
the draw pile's order included. Damaging one recorded decision produces a valid fight
that is refused. Stopping a decision short refuses to be called the boundary. The
profile reading and every byte of the profile store are unchanged either side of all of
it.

**Proved in the retail client.** With Steam running, the mod installed and every other
mod disabled through the game's own screen, the client loads Combat Trainer alone and
installs the write barrier over ten real write sites. The screen offers the fight.
Pressing it constructs the recording's run at the recording's identity, and the
recording's two decisions are then made in order, on the game's own screens, captioned
from the run itself. The player ends up standing in the recorded fight, and the whole
canonical state at that boundary is the same digest the headless host derives for the
combat-start snapshot. That is the loop's second step, closed in the client.

**What the run found that no headless test could.** Three of the recording's steps turn
out to be screen commands rather than engine ones, and the engine call is the middle of
each: a map move is `NMapScreen.TravelToMapCoord`, which fades the screen around
`RunManager.EnterMapCoord`; an event screen's continue is not in the event model's
option list at all. Each was invisible until a real client sat there waiting, and each
is now pinned by a test so a rename fails the build instead of a session.

**And one that changes how anything here should wait.** A deferral that re-defers itself
is not a frame loop: Godot drains its deferred queue until empty, so the loop ran seven
thousand times in eight seconds without the game drawing once - and what it was waiting
for was the fight opening, which needs those frames. The wait was what prevented it.
Handing the frames back on the scene tree's own timer is what let the player's turn
begin. Nothing this mod ticks has ever been seen running in that process; awaiting the
game's own tasks and its own timer are what work.

**The captain's saved progress is unchanged.** SHA-256 over 143 save files before the
session and after it: `progress.save`, `profile.save` and every run-history file are
byte identical. Two files differ and neither is progress - `settings.save`, which is
the mod enable toggles set through the game's own screen, and the game's own combat
replay scratch file.

**Not measured here.** Whether the popup that carries the recording's decisions is
reachable from a controller. It is the same popup the eligibility screen uses, which
was compared against the game's own confirmation popup under synthetic input; that is
the most this can claim, and it is not a claim that either responds to a controller.

[docs/in-game-host.md](../docs/in-game-host.md) records the boundary between what runs
here and what runs in the client, and
[docs/proof-of-concept-path.md](../docs/proof-of-concept-path.md) has S4 in the context
of the loop it closes a step of.

The map, reached. The recorded blessing has been taken, the game's own event screen has
been dismissed through the button the game itself would have had the player press, and
the map is behind the panel with the run standing on its starting node. The step is
numbered where it falls.

```bash {image}
![The Act 1 map in the retail client with a panel over it titled "Watching NaveGreed", reading "2 of 2   NaveGreed moved to the Monster node, centre column", with "Skip to the fight" and "Next" buttons. The top bar reads 64/68 health after the recorded blessing.](in-game-watching-map.png)
```

![The Act 1 map in the retail client with a panel over it titled "Watching NaveGreed", reading "2 of 2   NaveGreed moved to the Monster node, centre column", with "Skip to the fight" and "Next" buttons. The top bar reads 64/68 health after the recorded blessing.](140b3d23-2026-09-03.png)

And the fight, entered. The player is standing in it: Sludge Spinner at 42 of 42 with a
9-damage attack intent, the opening hand of Strike, Hellraiser, Strike, Bash and Defend,
3 of 3 energy, six cards in the draw pile and none discarded, turn 1, at Ascension 10 on
floor 2 of seed SFXT47K77RFK.

Every one of those is a value a person read off the recording, and the mod checked all
thirteen of them before it let go. It also compared the whole canonical state, which is
the part no screenshot can show: `sha256:979ba9de5e67882643dbd3f45b6eee6ae7d7412441e52b760f040e461752baae`
in the retail client, and the same digest for the combat-start snapshot the headless
host derives and re-derives in a fresh process. The random streams and the draw pile's
order agree, not just the faces on the cards.

```bash {image}
![The recorded fight in the retail client. The Ironclad at 64/68 faces a Sludge Spinner at 42/42 with a 9-damage attack intent. The hand is Strike, Hellraiser, Strike, Bash, Defend; energy reads 3/3, the draw pile 6 and the discard 0; the button reads "End Turn 1". The overlay reads v0.111.0, SFXT47K77RFK, MODDED (1) and the badge reads Ascension 10.](in-game-recorded-fight.png)
```

![The recorded fight in the retail client. The Ironclad at 64/68 faces a Sludge Spinner at 42/42 with a 9-damage attack intent. The hand is Strike, Hellraiser, Strike, Bash, Defend; energy reads 3/3, the draw pile 6 and the discard 0; the button reads "End Turn 1". The overlay reads v0.111.0, SFXT47K77RFK, MODDED (1) and the badge reads Ascension 10.](3cadd4e8-2026-09-03.png)

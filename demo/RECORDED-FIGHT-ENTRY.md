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
Passed!  - Failed:     0, Passed:    40, Skipped:     0, Total:    40, Duration: <duration> - Sts2PilotTrainer.Trainer.Tests.dll (net9.0)
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

## What this proves, and what it does not

**Proved.** A run is constructed at the recording's identity, through the same
construction the retail client uses, against a complete unlock state supplied in memory
for that run. The recording's two decisions before its fight are executed in its order
and captioned from what the run is standing in front of. The fight that opens is the
recorded one on all thirteen values a person read off the video and on the complete
canonical state the cached combat-start snapshot holds - the run's random streams and
the draw pile's order included. Damaging one recorded decision produces a valid fight
that is refused. Stopping a decision short refuses to be called the boundary. The
profile reading and every byte of the profile store are unchanged either side of all of
it.

**Not proved.** Nobody has been shown standing in the fight inside the retail client.
The mod's own side of the journey - launching through the game's start-run
continuation, the popup over Neow's screen and the map, the deviation lock on the two
commands those screens reach, and the write barrier under a save path the game actually
calls - is written and has not been watched running. There are no screenshots in this
document for that reason, and none of the checks above stands in for one.

**What would settle it.** Installing this build into the game
(`./scripts/install-mod.sh`) and opening Singleplayer with only Combat Trainer enabled.
That writes inside a Slay the Spire 2 installation and needs the game launched, so it
is the captain's to do rather than this task's.

**Not measured here.** Whether the popup that carries the recording's decisions is
reachable from a controller. It is the same popup the eligibility screen uses, which
was compared against the game's own confirmation popup under synthetic input; that is
the most this can claim, and it is not a claim that either responds to a controller.

[docs/in-game-host.md](../docs/in-game-host.md) records the boundary between what runs
here and what runs in the client, and
[docs/proof-of-concept-path.md](../docs/proof-of-concept-path.md) has S4 in the context
of the loop it closes a step of.

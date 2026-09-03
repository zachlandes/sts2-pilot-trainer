# The player's fight, compared

*2026-09-03T04:58:45Z by Showboat 0.6.1*
<!-- showboat-id: 89255b3f-990d-41b7-ba05-34e332c90b81 -->

This document runs the S5 loop of [the proof-of-concept path](../docs/proof-of-concept-path.md)
and records what it actually printed. Every code block below was executed; the output
under it is that run's output. `showboat --workdir .. verify PLAYER-FIGHT-COMPARISON.md`
re-runs the lot and diffs; the blocks are repo-root commands, which is what the working
directory is for.

**The claim being tested.** Can a fight somebody plays in the retail client be captured
as the same trace the headless arbiter produces, derived through the same projection,
compared with the recording's line by the same whole-combat comparison, and shown to
them - with a fight that was lost, left or not captured whole producing no comparison at
all?

Two halves. The first runs headlessly, with the recording standing in for the player:
its own nine fight actions are played through `FightCapture`, the capture the in-game
host observes a person with, and the result is compared with the engine's replay of the
same actions. The second is the retail client, with a person playing.

## The recording's side, and the proof it is this recording's

The client cannot replay - one process, one run, and it is the player's - so the
recording's line is produced here from a fresh replay and shipped inside the mod as
`manifests/navegreed-OJ-6QXhNgdg.recorded-fight.json`. Every value in it is
engine-produced. The block regenerates it into `build/evidence` and compares it with
the shipped file byte for byte after canonical serialisation: the shipped line is the
engine's own replay, or this fails.

```bash
set -o pipefail; ./scripts/arbiter recorded-fight manifests/navegreed-OJ-6QXhNgdg.replay.json --out build/evidence/regenerated.recorded-fight.json 2>&1 | grep -vE '^SentryGodotInitializer|^\[INFO\]|^\[WARN\]|^\[ERROR\]|^   at '; python3 -c "
import json
a=json.load(open('manifests/navegreed-OJ-6QXhNgdg.recorded-fight.json'))
b=json.load(open('build/evidence/regenerated.recorded-fight.json'))
print('shipped == regenerated:', a==b)"
```

```output
recording       : navegreed-OJ-6QXhNgdg
fight           : ENCOUNTER.SLUDGE_SPINNER_WEAK
covered         : actions through 10, 12 sampled step(s)
history hash    : sha256:da8eee5bce087da0b5d51c2e5fd445b730d37ebdc7d7a88ef23bcf731da6d35c
snapshot digest : sha256:979ba9de5e67882643dbd3f45b6eee6ae7d7412441e52b760f040e461752baae
outcome         : victory on turn 4, 64 -> 57 health

recorded fight: build/evidence/regenerated.recorded-fight.json
shipped == regenerated: True
```

## The loop, headlessly, with the recording standing in for the player

One command. It constructs the recording's run, makes the two decisions before the
fight, proves the boundary, and then - `--play` - plays the recording's own nine fight
actions through the player-side capture: the canonical state is sampled either side of
each one exactly as the in-game observer samples a person's, the capture completes
when the fight ends inside the killing blow, and the projection it hands over is
compared with the shipped recorded fight.

Three things in the output are worth reading closely.

Every summary **row is the same on both sides and every turn line reads the same
twice**, because the recording stood in for the player. That is the point rather than
a limitation: the capture, the projection and the comparison are the same code the
client runs, and a line that came through them and did not match the engine's own
replay of the same actions would be a defect in the capture.

The **panel** is printed word for word as the client shows it - the title, the two
columns, the seven rows, the turn detail, the two notes and the button. All of it is
the approved wording from `TrainerCopy`, produced from the comparison's numbers and the
manifest's creator name; nothing in it is written down for this recording.

The **sandbox line** is a hash over every byte of this process's sandbox profile store
before and after the played fight. It is printed because the headless host has no
write barrier, so whatever a won fight writes here is written; what it reads is a
measurement of this sandbox and not a claim about the client, where
`ProfileWriteBarrier` stops the writes a won fight reaches.

```bash
set -o pipefail; ./scripts/arbiter enter-fight manifests/navegreed-OJ-6QXhNgdg.replay.json --play 2>&1 | grep -vE '^SentryGodotInitializer|^\[INFO\]|^\[WARN\]|^\[ERROR\]|^   at '
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

played          : NaveGreed's own 9 fight action(s), through the player-side capture
capture         : Completed
sandbox writes  : none during the fight - measured because the headless host has no write barrier; in the client ProfileWriteBarrier stops what a won fight would write

[Your fight and NaveGreed's]
                        You           NaveGreed
  Outcome               Won           Won
  Turns                 4             4
  Health at the start   64            64
  Health at the end     57            57
  Net health change     -7            -7
  Potions used          none          none
  Cards removed         none          none

  Turn by turn
  Turn 1: you took 8 off the enemy and lost 4; NaveGreed took 8 off and lost 4
  Turn 2: you took 24 off the enemy and lost 2; NaveGreed took 24 off and lost 2
  Turn 3: you took 6 off the enemy and lost 7; NaveGreed took 6 off and lost 7
  Turn 4: you took 4 off the enemy and lost 0; NaveGreed took 4 off and lost 0

  This states differences. It does not say which fight was better.
  Health lost counts only health that came off. Damage absorbed by block is not counted.
  [Done]

report: build/evidence/enter-fight.json
```

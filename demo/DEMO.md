# Deterministic replay of a Slay the Spire 2 run, checked against the video

*2026-08-30T23:49:27Z by Showboat 0.6.1*
<!-- showboat-id: e6c021fc-420a-425d-b6aa-89b0dff77747 -->

This document runs the prototype and records what it actually printed. Every code
block below was executed; the output under it is that run's output, not a
transcription. `showboat verify DEMO.md` re-runs the lot and diffs.

**The claim being tested.** Given a video of somebody else's run, can that run be
reconstructed and replayed through the real game engine so exactly that the engine
agrees with everything the video shows — including hidden state no video can show?
And when the reconstruction is wrong, does the arbiter say so, rather than producing
something plausible?

The subject is one NaveGreed run, video `OJ-6QXhNgdg`, on Slay the Spire 2
`v0.111.0`. No footage from it is stored anywhere in this repository. What is stored
is facts read from it, each carrying the timestamp that lets anyone open the public
video and check.

Two premises this work started from turned out to be false. Both are shown below
being caught, rather than quietly corrected. The last section separates what is
proved from what is assumed, and the assumptions do real work.

## Setting up

The game is a read-only input. The bootstrap copies the installed assemblies into a
gitignored working directory, hashes the installation before and after, and fails if
a byte moved. No game content is committed here, and none of this works without
owning the game.

A note on the output below. Loading the real game assembly means the game's own
logger writes to the console — save-format versions, asset-cache misses, a Sentry
initialiser that finds no native extension. None of it is the arbiter's report, so
the commands filter it out, and the filter is visible in each command rather than
applied silently.

## The environment has to be the right one

A replay in the wrong environment does not fail. It succeeds at producing a
different run, and everything checked afterwards then compares the wrong things
confidently. So the first thing the arbiter does is refuse.

```bash
./scripts/arbiter preflight manifests/navegreed-OJ-6QXhNgdg.replay.json 2>&1 | grep -vE '^\[INFO\]|^SentryGodotInitializer|^\[WARN\] Asset not cached'
```

```output
manifest : navegreed-OJ-6QXhNgdg

  ok   build_version    manifest=v0.111.0       local=v0.111.0
  ok   build_date_utc   manifest=2026.08.14     local=2026.08.14
  ok   content_hash     manifest=1568834832     local=1568834832
  ok   seed_alphabet    manifest=legal          local=legal
  ok   game_mode        manifest=standard       local=standard

acts this build ships:
  0:ACT.OVERGROWTH (default)
  0:ACT.UNDERDOCKS
  1:ACT.HIVE (default)
  2:ACT.GLORY (default)

environment matches; replay may proceed
```

Look at the act list. This build ships **two acts at index 0**, and
`ACT.OVERGROWTH` is the default.

That is the first false premise. The video's map screen is titled *Underdocks*.
Taking "the default act at each index" — the obvious thing, and what this code did
at first — produced a run that generated **the same Act 1 map, node for node**, and
a completely different set of encounters: a 59-health enemy where the video shows a
42-health one.

The map was identical because map topology is not generated from the run's shared
random stream at all — the act map is built from a **fresh generator seeded from the
run seed**. So the map cannot detect a substituted act, and a cross-check that looks
decisive can be blind to the thing that matters. The act is now part of the
environment's identity, read from the name the game prints on the map screen.

## Verifying the seed without reading it

The seed reaches us as text somebody read off a low-contrast overlay. An earlier
optical pass on this video returned `SEXT47K77REK` with per-character confidence
`1.0`. That is the second false premise: it is wrong in two places. Agreement
between readings that share a source and an engine is not evidence of accuracy.

The check that reads nothing: regenerate each candidate seed's Act 1 map through the
game and compare its topology against the map the video shows. Map generation is
seeded from the run seed alone, so this tests the seed directly.

The four candidates are the readings that are visually indistinguishable — `E` and
`F` are not separable in either position at the resolution the video offers.

```bash
./scripts/arbiter verify-seed manifests/navegreed-OJ-6QXhNgdg.map-observation.json --candidates SEXT47K77REK,SFXT47K77RFK,SEXT47K77RFK,SFXT47K77REK --out build/evidence 2>&1 | grep -vE '^\[INFO\]|^SentryGodotInitializer|^\[WARN\] Asset not cached'
```

```output
SEXT47K77REK: MISMATCH  12/61 observed nodes agree, 67 problem(s)
    row 1 column 0: observed Monster, generated nothing
    row 2 column 0: observed Monster, generated nothing
    row 2 column 2: observed Monster, generated nothing
    row 2 column 4: observed Unknown, generated Monster
    ... and 63 more
SFXT47K77RFK: MATCH    61/61 observed nodes agree, 0 problem(s)
SEXT47K77RFK: MISMATCH  19/61 observed nodes agree, 71 problem(s)
    row 1 column 0: observed Monster, generated nothing
    row 2 column 0: observed Monster, generated nothing
    row 2 column 2: observed Monster, generated nothing
    row 2 column 4: observed Unknown, generated nothing
    ... and 67 more
SFXT47K77REK: MISMATCH  16/61 observed nodes agree, 71 problem(s)
    row 1 column 0: observed Monster, generated nothing
    row 1 column 3: observed Monster, generated nothing
    row 2 column 0: observed Monster, generated nothing
    row 2 column 2: observed Monster, generated Shop
    ... and 67 more

candidates tested : 4
matching          : SFXT47K77RFK
resolved seed     : SFXT47K77RFK
summary           : build/evidence/seed-verification-summary.json
```

The verifier draws what it compared. The left grid is the transcription — 61 nodes
read by hand from five frames at source resolution, across 15 of the map's 16 rows.
The right grid is what the engine generated. Nothing here is a screenshot of the
video: the video belongs to its creator and none of it is reproduced, so what is
drawn is the transcription, which is also what the comparison actually used.

```bash {image}
![Act 1 map topology for seed SFXT47K77RFK: the transcription and the engine-generated map, node for node identical across all 61 observed nodes](map-topology-match.png)
```

![Act 1 map topology for seed SFXT47K77RFK: the transcription and the engine-generated map, node for node identical across all 61 observed nodes](e4d44337-2026-08-30.png)

And the same drawing for the seed the optical pass reported with full confidence.
Mismatches are ringed.

```bash {image}
![The same comparison for SEXT47K77REK: 12 of 61 nodes agree, with mismatches ringed](map-topology-mismatch.png)
```

![The same comparison for SEXT47K77REK: 12 of 61 nodes agree, with mismatches ringed](8d139fb3-2026-08-30.png)

Row 0 appears only on the generated side. It is the run's starting node, which sits
below the visible area in every frame that was read, so it was left out of the
transcription rather than assumed.

**What this proves, and what it does not.** It proves the seed. It says nothing
about the position of the run's shared random stream, because the map does not come
from that stream — which is exactly how the act-variant mistake survived this check.

## Replaying the run

Five actions, reconstructed by hand from the video: the opening blessing, the move
to the first map node, the two cards played on turn 1, and ending that turn. Each
carries the timestamp it was read at. Four checkpoints hold 21 values the video
shows, and the replay has to reproduce every one.

```bash
./scripts/arbiter replay manifests/navegreed-OJ-6QXhNgdg.replay.json 2>&1 | grep -vE '^\[INFO\]|^SentryGodotInitializer|^\[WARN\] Asset not cached'
```

```output
manifest       : navegreed-OJ-6QXhNgdg
actions        : 5
status         : VERIFIED

  ok   checkpoint floor2-combat-start (after action 1)
        combat.block                 observed=0                      engine=0
        combat.discard_pile_count    observed=0                      engine=0
        combat.draw_pile_count       observed=6                      engine=6
        combat.enemy.0.hp            observed=42                     engine=42
        combat.enemy.0.intent        observed=Attack:9+Debuff        engine=Attack:9+Debuff
        combat.enemy.0.max_hp        observed=42                     engine=42
        combat.enemy_count           observed=1                      engine=1
        combat.energy                observed=3                      engine=3
        combat.hand                  observed=CARD.STRIKE_IRONCLAD|CARD.HELLRAISER|CARD.STRIKE_IRONCLAD|CARD.BASH|CARD.DEFEND_IRONCLAD engine=CARD.STRIKE_IRONCLAD|CARD.HELLRAISER|CARD.STRIKE_IRONCLAD|CARD.BASH|CARD.DEFEND_IRONCLAD
        combat.max_energy            observed=3                      engine=3
        combat.player_hp             observed=64                     engine=64
        combat.turn                  observed=1                      engine=1
        player.max_hp                observed=68                     engine=68
  ok   checkpoint after-hellraiser (after action 2)
        combat.energy                observed=1                      engine=1
        combat.hand_count            observed=4                      engine=4
  ok   checkpoint after-defend (after action 3)
        combat.block                 observed=5                      engine=5
        combat.energy                observed=0                      engine=0
        combat.hand_count            observed=3                      engine=3
  ok   checkpoint turn2-start (after action 4)
        combat.player_hp             observed=60                     engine=60
        combat.turn                  observed=2                      engine=2

final state digest : sha256:ad7d3d164d73b5cefdf47622ee51089a66b44bf610b122142f972401d5815cff
action history hash: sha256:59bbd9b144eb85d53eebadd2784fb1f456d7a8f4fa0367f55f6d007c01a0367a
```

Three of those lines are worth stopping on.

**The hand, in order.** `Strike, Hellraiser, Strike, Bash, Defend` — and there are
two Hellraisers in this deck because the opening blessing transforms one Strike and
one Defend into *random* cards, and this run famously drew the same rare card twice.
Reproducing that is reproducing a specific random outcome, not a deterministic
consequence of the seed.

**The enemy's telegraphed intent, `Attack:9+Debuff`.** This is the one checkpoint
the engine prompted a second look at. It was first transcribed as `Attack:9` — the
number and the attack arrow were read, and the debuff glyph beside them was missed.
The engine reported a second intent component, that frame was re-read at source
resolution, and the glyph is plainly there. The manifest records that sequence in
the checkpoint's own note, because a reader should be able to weigh it. It is
corroborated independently: the player is carrying Weak at the start of turn 2.

**`combat.player_hp 60` after ending the turn.** That is the whole enemy turn
agreeing. Intent 9, less the 5 block from the Defend, is the 4 damage the video
shows.

## Determinism

Fresh processes, not fresh sessions. The engine keeps a great deal of static state,
and a determinism claim that only holds inside one process is not the claim anyone
wants.

The canonical state compared here is built by an explicit allowlist projection — 
including the position of all fifteen random-number streams and the full order of
the draw pile, neither of which any video can show. What is excluded is decided up
front and documented, so a digest mismatch can only be a real divergence and never
an artefact of running on a different afternoon.

```bash
./scripts/arbiter determinism manifests/navegreed-OJ-6QXhNgdg.replay.json --runs 3 --out build/evidence 2>&1 | grep -vE '^\[INFO\]|^SentryGodotInitializer|^\[WARN\] Asset not cached'
```

```output
run 0: sha256:ad7d3d164d73b5cefdf47622ee51089a66b44bf610b122142f972401d5815cff
run 1: sha256:ad7d3d164d73b5cefdf47622ee51089a66b44bf610b122142f972401d5815cff
run 2: sha256:ad7d3d164d73b5cefdf47622ee51089a66b44bf610b122142f972401d5815cff

all 3 fresh processes produced byte-identical canonical state
```

## Rejecting a history that is wrong

A checker nobody has fed a bad input to has never been shown to reject anything. So
the history is damaged in four specific ways, and each is replayed.

The two interesting ones are the corruptions that arithmetic on the footage alone
**accepts**: reordering two plays, and substituting a card of the same energy cost.
Energy conservation balances, hand accounting balances, and the damage arithmetic
balances — every check that can be done from the frames says yes. Those are the two
that justify owning an engine at all.

```bash
./scripts/arbiter negative-controls manifests/navegreed-OJ-6QXhNgdg.replay.json --out build/evidence 2>&1 | grep -vE '^\[INFO\]|^SentryGodotInitializer|^\[WARN\] Asset not cached'
```

```output
baseline (uncorrupted): VERIFIED

reorder-plays
  corruption   : Plays the same two cards in the opposite order, adjusting hand indices so both remain valid.
  video-only   : UNDETECTED - Energy spent is unchanged (1 + 2 = 2 + 1), the hand still goes from five cards to three, and the damage arithmetic is untouched. Nothing measurable in a frame distinguishes the two orders - yet order is exactly what the game's run-persistent RNG streams are sensitive to.
  arbiter      : REJECTED
  first divergence: combat.energy                observed=1                      engine=2
  end state       : IDENTICAL to the uncorrupted run

substitute-same-cost
  corruption   : Replaces the Defend with the Strike beside it. Both cost 1.
  video-only   : UNDETECTED - Energy conservation and hand accounting both balance, because the substitute costs the same. The damage arithmetic balances too unless the enemy's health is read frame by frame, which the earlier video-only pipeline did not do.
  arbiter      : REJECTED
  first divergence: combat.block                 observed=5                      engine=0
  end state       : differs from the uncorrupted run

omit-play
  corruption   : Drops the Defend entirely.
  video-only   : DETECTED - Energy would be left at 1 with nothing to account for it, and the hand would end at four cards instead of three. Included as a control on the control: an arbiter that rejected only the subtle corruptions and let this one through would be broken in an interesting way.
  arbiter      : REJECTED
  first divergence: combat.player_hp             observed=60                     engine=55
  end state       : differs from the uncorrupted run

wrong-opening-choice
  corruption   : Takes a different blessing at the run's opening event.
  video-only   : DETECTED - The chosen blessing changes maximum health on screen within seconds. Included because it corrupts the history far from the turn being checked, which tests that a divergence is caught where it surfaces rather than where it happened.
  arbiter      : REJECTED
  first divergence: combat.draw_pile_count       observed=6                      engine=7
  end state       : differs from the uncorrupted run

all 4 corrupted histories were rejected; the uncorrupted one verified
```

Read the `end state` line on the reordering. It is **identical to the uncorrupted
run**: for these two particular cards, playing them in the other order lands in the
same place, so comparing only the run's final digest would have accepted it. What
caught it was a checkpoint bound to a moment *inside* the turn — energy was 2 where
the video shows 1.

That is a real limit and it is pinned by a test so it cannot quietly stop being
true. Digest comparison alone is not sufficient; checkpoints need to be dense rather
than terminal. An arbiter that only compared end states would have a blind spot
exactly where the video-only checks have theirs.

## A verified snapshot, and two lines from it

This is the point of the whole apparatus. Once a mid-run position can be reproduced
exactly, it can be handed to a player, and two different lines can be played from
the identical position and compared.

The snapshot is a **derived cache**, never a source of truth. It is keyed by the
build, seed, content hash, game mode and the hash of the exact action history that
produced it — so it can never be served for a run that would not produce it. And
"restore" here means re-derive and verify: each restore replays the same prefix in a
fresh process and refuses unless the digest matches what was cached. That is slower
than loading a blob and much harder to get quietly wrong.

```bash
rm -rf build/snapshots && ./scripts/arbiter snapshot-lines manifests/navegreed-OJ-6QXhNgdg.replay.json --at 1 --line manifests/lines/streamer.line.json --line manifests/lines/aggressive.line.json --out build/evidence --cache build/snapshots 2>&1 | grep -vE '^\[INFO\]|^SentryGodotInitializer|^\[WARN\] Asset not cached'
```

```output
snapshot key   : v0.111.0_standard_SFXT47K77RFK_1568834832_seq1_bf2cbf0b27d9c546
snapshot source: materialised now
snapshot digest: sha256:437c6a9aab9529ab171b0a4500c13492b4963dd1a7ccbaef14f42ce8ea49aa31

line streamer.line  (3 action(s))
  restore verified against snapshot digest: yes
    PlayCard card_id=CARD.HELLRAISER hand_index=1
    PlayCard card_id=CARD.DEFEND_IRONCLAD hand_index=3
    EndTurn 
    delta combat.discard_pile              ->  CARD.DEFEND_IRONCLAD|CARD.STRIKE_IRONCLAD|CARD.STRIKE_IRONCLAD|CARD.BASH|CARD.STRIKE_IRONCLAD|CARD.STRIKE_IRONCLAD
    delta combat.discard_pile_count      0  ->  6
    delta combat.draw_pile               CARD.STRIKE_IRONCLAD|CARD.STRIKE_IRONCLAD|CARD.DEFEND_IRONCLAD|CARD.ASCENDERS_BANE|CARD.DEFEND_IRONCLAD|CARD.HELLRAISER  ->  CARD.HELLRAISER
    delta combat.draw_pile_count         6  ->  1
    delta combat.enemy.0.hp              42  ->  34
    delta combat.enemy.0.intent          Attack:9+Debuff  ->  Attack:12
    delta combat.enemy.0.next_move       OIL_SPRAY_MOVE  ->  SLAM_MOVE
    delta combat.hand                    CARD.STRIKE_IRONCLAD|CARD.HELLRAISER|CARD.STRIKE_IRONCLAD|CARD.BASH|CARD.DEFEND_IRONCLAD  ->  CARD.DEFEND_IRONCLAD|CARD.ASCENDERS_BANE|CARD.DEFEND_IRONCLAD
    delta combat.hand_count              5  ->  3
    delta combat.player_hp               64  ->  60
    delta combat.player_powers             ->  POWER.HELLRAISER_POWER:1|POWER.WEAK_POWER:1
    delta combat.round                   1  ->  2
    delta combat.turn                    1  ->  2
    delta player.hp                      64  ->  60
    delta run.rng.CombatTargets          0  ->  2
    delta run.rng.MonsterAi              0  ->  1

line aggressive.line  (3 action(s))
  restore verified against snapshot digest: yes
    PlayCard card_id=CARD.BASH hand_index=3
    PlayCard card_id=CARD.STRIKE_IRONCLAD hand_index=0
    EndTurn 
    delta combat.discard_pile              ->  CARD.BASH|CARD.STRIKE_IRONCLAD|CARD.HELLRAISER|CARD.STRIKE_IRONCLAD|CARD.DEFEND_IRONCLAD
    delta combat.discard_pile_count      0  ->  5
    delta combat.draw_pile               CARD.STRIKE_IRONCLAD|CARD.STRIKE_IRONCLAD|CARD.DEFEND_IRONCLAD|CARD.ASCENDERS_BANE|CARD.DEFEND_IRONCLAD|CARD.HELLRAISER  ->  CARD.HELLRAISER
    delta combat.draw_pile_count         6  ->  1
    delta combat.enemy.0.hp              42  ->  25
    delta combat.enemy.0.intent          Attack:9+Debuff  ->  Attack:12
    delta combat.enemy.0.next_move       OIL_SPRAY_MOVE  ->  SLAM_MOVE
    delta combat.enemy.0.powers            ->  POWER.VULNERABLE_POWER:1
    delta combat.hand                    CARD.STRIKE_IRONCLAD|CARD.HELLRAISER|CARD.STRIKE_IRONCLAD|CARD.BASH|CARD.DEFEND_IRONCLAD  ->  CARD.STRIKE_IRONCLAD|CARD.STRIKE_IRONCLAD|CARD.DEFEND_IRONCLAD|CARD.ASCENDERS_BANE|CARD.DEFEND_IRONCLAD
    delta combat.player_hp               64  ->  55
    delta combat.player_powers             ->  POWER.WEAK_POWER:1
    delta combat.round                   1  ->  2
    delta combat.turn                    1  ->  2
    delta player.hp                      64  ->  55
    delta run.rng.MonsterAi              0  ->  1

diagram: build/evidence/snapshot-lines.svg
```

```bash {image}
![Two lines played from the same verified snapshot, with objective state deltas for each and no verdict about which was better](snapshot-two-lines.png)
```

![Two lines played from the same verified snapshot, with objective state deltas for each and no verdict about which was better](6b741a12-2026-08-30.png)

Deltas and nothing else. No score, no ranking, no highlight on the "better" outcome —
which line is better is a question about a game, and answering it here would turn a
measurement into an opinion. A test asserts the report contains no score, rank or
verdict field.

The interesting differences are visible without any judgement being offered. The
streamer's line ends at 60 health with the enemy on 34 and a Hellraiser power in
play; the other ends at 55 with the enemy on 25 and Vulnerable applied. Note
`run.rng.CombatTargets` advancing by 2 on the first line only: that is Hellraiser
playing drawn Strikes at a random enemy, consuming a random stream the other line
never touches. Two lines from the same position are not in the same fight for long.

## The tests

Sixty of them. The pure suite needs no game at all and runs anywhere; the
integration suite drives the built command line, one process per test, and skips
with an explanation on a machine that cannot run it.

Every checker has a demonstrated negative input: the manifest validator has a
malformed input per rule, the preflight has a mismatched build and a mismatched
content hash, the map comparison has a wrong node, a missing node, an extra node and
a wrong grid size, the arbiter has four corrupted histories, and the cache key has
changes that must and must not invalidate it.

```bash
dotnet test sts2-pilot-trainer.sln -c Release --nologo -v quiet 2>&1 | grep -E "Passed!|Failed!|error" | sed -E 's/, Duration: [0-9.]+ (ms|s) - / - /'
```

```output
Passed!  - Failed:     0, Passed:    49, Skipped:     0, Total:    49 - Sts2PilotTrainer.Replay.Tests.dll (net9.0)
Passed!  - Failed:     0, Passed:    11, Skipped:     0, Total:    11 - Sts2PilotTrainer.Arbiter.Tests.dll (net9.0)
```

## What is proved, and what is not

**Proved, on this machine, against this video.**

- The seed is `SFXT47K77RFK`. The engine's own map generator reproduces all 61
  transcribed nodes; the seed an optical reader reported with full confidence
  reproduces 12. This does not depend on reading a character.
- Replaying five reconstructed actions from run start reproduces **21 independently
  observed values**, including the enemy's health and telegraphed intent, the ordered
  hand, energy at three points in the turn, block, and the player's health after the
  enemy's turn resolved. The random outcome of the opening blessing's transform is
  among them.
- The same manifest in three fresh processes produces byte-identical canonical
  state, including all fifteen random-stream positions and the full draw-pile order.
- Four corrupted histories are rejected, two of which every arithmetic check
  available from the footage accepts.
- A verified snapshot is keyed to the history that produced it, restores to a
  digest-checked identical state, and supports two lines being played from it with
  objective deltas and no verdict.

**Assumed, and doing real work.**

- **The source player had everything unlocked.** The game generates a run's content
  against the player's unlock state, and nothing in a video shows it. Measured, on
  this seed: changing only that assumption moves the shared random stream from
  position 412 to 370 and changes which encounters the act generates — while leaving
  the map byte-identical. Agreement on generated content is the evidence for this
  assumption; it is not independently established.
- **The game mode is standard.** Daily runs are ruled out — their seeds are
  date-derived and this one is not, and a daily shows modifier icons this run does
  not. Custom mode is not ruled out by direct evidence. It is the weakest link in the
  environment identity, and the manifest marks it as an inference rather than an
  observation.
- **The source environment's three mods changed nothing that matters here.** The
  content hash matches, and this host loads no mods, so a match rules out content
  contributed by mods that declare themselves gameplay-affecting. It rules out
  nothing about a mod that patches behaviour, and the game's own warning says the
  hash may omit ids. Every checkpoint that agrees is evidence against divergence at
  that point, not proof of parity across a run.

**Not attempted.**

- **This is not the retail client.** It is the real shipped assembly driven headless,
  with the presentation layer stubbed out. Everything above is agreement at points a
  video could show, which is strong and is not the same as running the game.
- **Only the opening turn is covered.** Every claim is about the part of the run that
  was transcribed by hand. Extending the transcription is ordinary work; nothing here
  suggests it would be *easy*, and the manifest says where it stops.
- **Nothing is automatically extracted from video.** The five actions and 61 map
  nodes were read by a person. Building the extractor is the next problem and was
  deliberately not started: an extractor is only worth building once there is an
  arbiter that can tell you when it is wrong.

**The two premises that were wrong** are the reason to trust the rest of this less
than a green result usually invites. Both were accepted facts at the start; both
produced runs that looked entirely healthy; both were caught only by comparing
against something the video independently shows. The act-variant one in particular
survived the map check, which is the most convincing-looking check in this document.

## Reproducing this

Build, then re-run every block above and diff the output:

    ./scripts/build.sh
    showboat verify demo/DEMO.md

You need the game and .NET 9. The images need nothing at all — they are drawn by the
tools from the comparison data, not captured from a screen. Both are emitted as SVG
into `build/evidence/` by the commands above, and the copies in this directory were
rasterised from them:

    magick -density 160 -background white build/evidence/seed-verification-SFXT47K77RFK.svg demo/map-topology-match.png
    magick -density 160 -background white build/evidence/seed-verification-SEXT47K77REK.svg demo/map-topology-mismatch.png
    magick -density 150 -background white build/evidence/snapshot-lines.svg demo/snapshot-two-lines.png


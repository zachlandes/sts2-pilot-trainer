# Character, route and ascension coverage

*2026-09-19T10:16:52Z by Showboat 0.6.1*
<!-- showboat-id: ac4acafc-5c6c-4626-8834-a1ea0e83ea1a -->

The recorder's coverage number is computed over recordings, and until this change every recording it was computed over - committed or generated on the merge gate - was an Ironclad's. The character rows put a whole first act of every character on the gate, on each act-one route the survival measurement of 2026-09-19 verified a seed for, and the Ironclad at ascension 10; each is played by the measured rule (`SurvivalRule.BlockWhenThreatened`: block while the enemies' displayed damage exceeds the block held, otherwise attack the lowest-health enemy; card rewards by block-or-attack, canonical cost, then offer order), recorded through the real recorder, replayed to parity, and its second act's opening ancient read at the run's start as the criterion beside the survival. `coverage` now says what its number is evidence about, one line per axis. Everything below is headless, on v0.111.0, from the repository root's `build/lib`.

```bash
cd .. && ./scripts/arbiter coverage --corpus manifests --out build/evidence/demo-coverage 2>&1 | sed -n "/represented by the crediting recordings/,\$p" | grep -v "coverage artifact:"
```

```output
  represented by the crediting recordings:
  characters: CHARACTER.IRONCLAD (3 recording(s))
  variants: ACT.OVERGROWTH,ACT.HIVE,ACT.GLORY (2 recording(s))  ACT.UNDERDOCKS,ACT.HIVE,ACT.GLORY (1 recording(s))
  ascensions: 0 (2 recording(s))  10 (1 recording(s))

points: 522  covered: 19  co-occurrence: 6  excused: 469  uncovered: 0  not projectable: 28  recordings: 3
COVERED - every point this build offers is reached by a recording, excused in writing, or one this format cannot count
```

The same three axes are in `coverage.json` under `axes`, additive to the artifact's existing schema, so a release measurement over a player's store says which characters, routes and ascensions its recordings span.

```bash
cd .. && python3 -c "import json; print(json.dumps(json.load(open(\"build/evidence/demo-coverage/coverage.json\"))[\"axes\"], indent=2))"
```

```output
{
  "characters": [
    {
      "value": "CHARACTER.IRONCLAD",
      "recordings": 3
    }
  ],
  "variants": [
    {
      "value": "ACT.OVERGROWTH,ACT.HIVE,ACT.GLORY",
      "recordings": 2
    },
    {
      "value": "ACT.UNDERDOCKS,ACT.HIVE,ACT.GLORY",
      "recordings": 1
    }
  ],
  "ascensions": [
    {
      "value": "0",
      "recordings": 2
    },
    {
      "value": "10",
      "recordings": 1
    }
  ]
}
```

The rows themselves: eleven whole first acts through the real recorder, each replayed to parity. The recorder's own log line names the recording, the outcome (abandoned at the second act's opening room, the way every generated row is finished), the decision count and the boundaries; a row that died would fail naming the floor, the encounter and the character instead.

```bash
cd .. && dotnet test tests/Sts2PilotTrainer.Mod.Tests -c Release --no-build --filter "FullyQualifiedName~ACharacterRowSurvivesTheFirstActOpensTheSecondAndReplaysToParity" --logger "console;verbosity=detailed" 2>&1 | grep -E "^\s*(Passed|Failed) |\[Runmobile\] recorded" | sed -E "s#written to .*#written to the store#; s#native-([0-9A-Z]{10})-[0-9]{8}-[0-9]{6}#native-\1-<stamp>#; s# \[[0-9]+ s\]##; s# \[[0-9]+ ms\]##; s#Sts2PilotTrainer.Arbiter.Tests.GeneratedCoverageTests.ACharacterRowSurvivesTheFirstActOpensTheSecondAndReplaysToParity#ACharacterRow…#" | sort
```

```output
  Passed ACharacterRow…(character: "CHARACTER.DEFECT", variant: "ACT.OVERGROWTH", ascension: 0)
  Passed ACharacterRow…(character: "CHARACTER.DEFECT", variant: "ACT.UNDERDOCKS", ascension: 0)
  Passed ACharacterRow…(character: "CHARACTER.IRONCLAD", variant: "ACT.OVERGROWTH", ascension: 0)
  Passed ACharacterRow…(character: "CHARACTER.IRONCLAD", variant: "ACT.OVERGROWTH", ascension: 10)
  Passed ACharacterRow…(character: "CHARACTER.IRONCLAD", variant: "ACT.UNDERDOCKS", ascension: 0)
  Passed ACharacterRow…(character: "CHARACTER.NECROBINDER", variant: "ACT.OVERGROWTH", ascension: 0)
  Passed ACharacterRow…(character: "CHARACTER.NECROBINDER", variant: "ACT.UNDERDOCKS", ascension: 0)
  Passed ACharacterRow…(character: "CHARACTER.REGENT", variant: "ACT.OVERGROWTH", ascension: 0)
  Passed ACharacterRow…(character: "CHARACTER.REGENT", variant: "ACT.UNDERDOCKS", ascension: 0)
  Passed ACharacterRow…(character: "CHARACTER.SILENT", variant: "ACT.OVERGROWTH", ascension: 0)
  Passed ACharacterRow…(character: "CHARACTER.SILENT", variant: "ACT.UNDERDOCKS", ascension: 0)
[INFO] [Runmobile] recorded native-47188SS8FQ-<stamp>: abandoned, 320 decision(s), 81 boundary/boundaries, continuity continuous, integrity complete, written to the store
[INFO] [Runmobile] recorded native-9P00EW4KB7-<stamp>: abandoned, 248 decision(s), 69 boundary/boundaries, continuity continuous, integrity complete, written to the store
[INFO] [Runmobile] recorded native-C1GAV23WHA-<stamp>: abandoned, 179 decision(s), 51 boundary/boundaries, continuity continuous, integrity complete, written to the store
[INFO] [Runmobile] recorded native-C1GAV23WHA-<stamp>: abandoned, 253 decision(s), 67 boundary/boundaries, continuity continuous, integrity complete, written to the store
[INFO] [Runmobile] recorded native-KNU8ZJM21D-<stamp>: abandoned, 280 decision(s), 70 boundary/boundaries, continuity continuous, integrity complete, written to the store
[INFO] [Runmobile] recorded native-KNU8ZJM21D-<stamp>: abandoned, 369 decision(s), 84 boundary/boundaries, continuity continuous, integrity complete, written to the store
[INFO] [Runmobile] recorded native-KNU8ZJM21D-<stamp>: abandoned, 387 decision(s), 94 boundary/boundaries, continuity continuous, integrity complete, written to the store
[INFO] [Runmobile] recorded native-S7LTRQKC10-<stamp>: abandoned, 341 decision(s), 86 boundary/boundaries, continuity continuous, integrity complete, written to the store
[INFO] [Runmobile] recorded native-X7KJSBLHQ6-<stamp>: abandoned, 215 decision(s), 58 boundary/boundaries, continuity continuous, integrity complete, written to the store
[INFO] [Runmobile] recorded native-X7KJSBLHQ6-<stamp>: abandoned, 223 decision(s), 60 boundary/boundaries, continuity continuous, integrity complete, written to the store
[INFO] [Runmobile] recorded native-Y80L04N50D-<stamp>: abandoned, 255 decision(s), 72 boundary/boundaries, continuity continuous, integrity complete, written to the store
```

A walk the rules do not carry through says where it died. The journey rethrows the lost fight with the floor, the encounter and the character, which is what a seed is hunted on; the test below holds that sentence on a run of Glory alone under the measured rule, which loses its first fight on all but one hunted seed in sixty - the reason the rows on a run of the Hive or Glory alone keep the earlier attack-first rule by name (`SurvivalRule.AttackFirst`), while every row on a first act plays the measured one.

```bash
cd .. && dotnet test tests/Sts2PilotTrainer.Mod.Tests -c Release --no-build --filter "FullyQualifiedName~AWalkThatDiesNamesTheFloorTheEncounterAndTheCharacter" --logger "console;verbosity=normal" 2>&1 | grep -E "^\s*(Passed|Failed) " | sed -E "s# \[[0-9]+ (s|ms)\]##; s#Sts2PilotTrainer.Arbiter.Tests.##" && grep -h "died on floor" src/Sts2PilotTrainer.Engine/SyntheticFixtureGenerator.WholeAct.cs | sed -E "s#^\s+##"
```

```output
  Passed GeneratedCoverageTests.AWalkThatDiesNamesTheFloorTheEncounterAndTheCharacter
                $"The act journey died on floor {floor} in {encounter}, a {name} fight, as " +
```

Three defects the measurement found stood between the rows and the gate, and each is closed with a regression that fails without its fix: the vendored Godot stubs had no `Mathf.Log`, so every headless walk into the Phantasmal Gardeners elite stalled at its first end of turn; the bootstrap's one IL patch returned the game's own end-of-turn wait at once, so a fight the Defect's Lightning orb ended from its end-of-turn passive reached the end of combat before the executor had popped the ended turn, and the recorder refused fourteen of seventeen Defect walks (the patch is removed, the prepared `sts2.dll` now hashes the same as the installed one, and the stubs gained the `Callable.From<TResult>` the engine's cancel path needs); and `RunDriver.PlayCard` read whether a play took effect off the hand, refusing Particle Wall and every 0-cost attack under Feral, which the engine plays in full and returns to the hand - it now reads the game's own combat history.

```bash
cd .. && dotnet test tests/Sts2PilotTrainer.Mod.Tests -c Release --no-build --filter "FullyQualifiedName~PhantasmalGardenerTests|FullyQualifiedName~DefectOrbFightEndTests|FullyQualifiedName~PlayReturnedToHandTests" --logger "console;verbosity=normal" 2>&1 | grep -E "^\s*(Passed|Failed) " | sed -E "s# \[[0-9]+ (s|ms)\]##; s#Sts2PilotTrainer.Arbiter.Tests.##" | sort && python3 -c "import json; r=json.load(open(\"build/lib/prepared-assembly.json\")); print(\"il_patches:\", r[\"il_patches\"], \"| prepared sts2.dll is the installed one:\", r[\"pristine_sts2_sha256\"] == r[\"prepared_output_sha256\"][\"sts2.dll\"])"
```

```output
  Passed DefectOrbFightEndTests.AFightTheLightningOrbEndsAtTheEndOfTheTurnEndsCleanly
  Passed DefectOrbFightEndTests.AnActionStillQueuedWhenTheFightEndsIsCancelledCleanly
  Passed PhantasmalGardenerTests.TheGardenersEliteEndsATurnAndOpensTheNext
  Passed PlayReturnedToHandTests.ADefectsZeroCostAttackUnderFeralGoesBackToTheHandAndThePlayIsEstablished
  Passed PlayReturnedToHandTests.ARegentsParticleWallGoesBackToTheHandAndThePlayIsEstablished
il_patches: [] | prepared sts2.dll is the installed one: True
```

What the rows retire is written into `DecisionExcusals` as generated excusals held by the rows - the seams a character's own cards and potions open that no Ironclad walk does, the gold a Fat Gremlin gives back on the Underdocks, and Darv with the one option the Ironclad's default-progression seed takes at its second act's opening - and the committed record shows each with its class. The eleven options of Darv the walk does not take, the act-2-only events and the card-removal reward keep theirs, worded as what lies past the second act's opening room.

```bash
cd .. && grep -E "^(EVENT.DARV|EVENT.DARV RELIC.CALLING_BELL|EVENT.DARV RELIC.ASTROLABE|reward-kind:gold @ AbstractModel.BeforeDeath|card-prompt:CardSelectCmd.FromHandForDiscard\(context, player, prefs, filter, source\) @ CardModel.OnPlay|EVENT.CRYSTAL_SPHERE)  " scripts/decision-coverage.txt | cut -c1-150
```

```output
EVENT.CRYSTAL_SPHERE  excused [not-on-the-route]: no committed recording reaches it and the generated walk's route does not pass its producer - allowe
EVENT.DARV  excused [generated]: reached by a GeneratedCoverageTests character row - a whole first act of one character on one act-one route, on the s
EVENT.DARV RELIC.ASTROLABE  excused [not-on-the-route]: no committed recording reaches it and the generated walk's route does not pass its producer - 
EVENT.DARV RELIC.CALLING_BELL  excused [generated]: reached by a GeneratedCoverageTests character row - a whole first act of one character on one act-
card-prompt:CardSelectCmd.FromHandForDiscard(context, player, prefs, filter, source) @ CardModel.OnPlay  excused [generated]: reached by a GeneratedCo
reward-kind:gold @ AbstractModel.BeforeDeath  excused [generated; names POWER.HEIST_POWER]: reached by a GeneratedCoverageTests character row - a whol
```

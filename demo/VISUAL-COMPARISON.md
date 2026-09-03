# The result, drawn

*2026-09-03T06:26:02Z by Showboat 0.6.1*
<!-- showboat-id: 3a3fbb41-c758-415f-a0b2-40a0d1f31c75 -->

This document is the S6 slice of [the proof-of-concept path](../docs/proof-of-concept-path.md):
the post-fight result, drawn. Every code block below was executed; the output under it is
that run's output. `showboat --workdir .. verify VISUAL-COMPARISON.md` re-runs the lot and
diffs; the blocks are repo-root commands, which is what the working directory is for.

**The claim being tested.** After the captain played the recorded fight in S5, he read the
result and reported that prose describing how his fight differed from NaveGreed's, on a
large modal, was not the interface. So: can the same comparison be shown as pictures - the
summary as figures, the turn chronology as the game's own card art in the order it was
played, and the chart `docs/comparison-direction.md` wrote down - without the presentation
inventing a single value the projection cannot derive?

Three parts. The chart's derivation, which is arithmetic over the comparison and is tested
on a machine with no game. The panel, which is assembled from stock Godot nodes and can
therefore be built and interrogated node by node in the same game-free process. And the
retail client, with a person playing a line that is deliberately not the recording's, so
the two sides of the panel are actually different.

## What the chart is, and what it refuses

`FightResultChart` derives the chart from a `CombatComparison` and nothing else: one point
per turn per line for enemy health lost and for player health lost, one ceiling both plots
and both lines are drawn against, and the potions marked at the turn they were spent by
their stable model ids. It lives in `Sts2PilotTrainer.Trainer` beside the wording rather
than in the comparison contract, because what a comparison *says* is still an interface
question and a chart baked into the contract would be an answer nothing could revisit.

The cases worth naming are the ones a chart is tempted to invent. A turn one line never
reached carries no point on that line - not a point on the axis, which would claim the turn
was fought and cost nothing. A card play that carries no card id is refused outright rather
than drawn as a blank icon. Both are tested, with no game present.

```bash
set -o pipefail; dotnet test tests/Sts2PilotTrainer.Trainer.Tests -c Release --nologo --filter "FullyQualifiedName~FightResultChartTests" --logger "console;verbosity=normal" 2>&1 | grep -E "^  Passed " | sed "s/ \[.*\]//" | sort
```

```output
  Passed Sts2PilotTrainer.Trainer.Tests.FightResultChartTests.AFightThatCostNeitherSideAnythingStillHasItsTurns
  Passed Sts2PilotTrainer.Trainer.Tests.FightResultChartTests.AScreenWithoutAComparisonHasNothingToDraw
  Passed Sts2PilotTrainer.Trainer.Tests.FightResultChartTests.ATurnALineNeverReachedHasNoValueRatherThanAZero
  Passed Sts2PilotTrainer.Trainer.Tests.FightResultChartTests.KeepsThePlayersLineAndTheRecordingsApart
  Passed Sts2PilotTrainer.Trainer.Tests.FightResultChartTests.MarksAPotionAtTheTurnItWasUsed
  Passed Sts2PilotTrainer.Trainer.Tests.FightResultChartTests.PlotsBothMeasuresForBothLinesAgainstTheTurn
  Passed Sts2PilotTrainer.Trainer.Tests.FightResultChartTests.ScalesBothMeasuresAndBothLinesAgainstOneCeiling
```

## The panel, built and interrogated without a game

`FightResultPanel` in the mod draws it, out of stock Godot nodes. That is not a stylistic
choice: this assembly compiles without Godot's source generators, so a `Control` subclass
of ours would carry no generated bridge and none of its overrides would ever be called.
The consequence worth having is that the whole panel can be assembled in a process with no
game and asked what it drew - the fixture below is three turns against two, with a potion
on the player's second turn and the recording's fight ending on its own second.

The assertions are about what is drawn rather than where. Coordinates are arithmetic over
whatever surface the panel is given, and pinning them would make every future spacing
change a test failure about nothing.

```bash
set -o pipefail; dotnet test tests/Sts2PilotTrainer.Mod.Tests -c Release --nologo --filter "FullyQualifiedName~FightResultPanelTests" --logger "console;verbosity=normal" 2>&1 | grep -E "^  Passed " | sed "s/ \[.*\]//" | sort
```

```output
  Passed Sts2PilotTrainer.Arbiter.Tests.FightResultPanelTests.AFightWithNoComparisonIsTheNoticeAndTheButton
  Passed Sts2PilotTrainer.Arbiter.Tests.FightResultPanelTests.CarriesTheApprovedWordingAndNothingElse
  Passed Sts2PilotTrainer.Arbiter.Tests.FightResultPanelTests.DrawsACardForEveryCardPlayedAndAPotionWhereOneWasSpent
  Passed Sts2PilotTrainer.Arbiter.Tests.FightResultPanelTests.DrawsEachPointsOwnNumeral
  Passed Sts2PilotTrainer.Arbiter.Tests.FightResultPanelTests.DrawsTheGamesArtworkWhereThereIsSomeAndTheNameWhereThereIsNot
  Passed Sts2PilotTrainer.Arbiter.Tests.FightResultPanelTests.FitsInsideTheSurfaceItIsGiven
  Passed Sts2PilotTrainer.Arbiter.Tests.FightResultPanelTests.KeepsTheTwoLinesApartByColourAndByShape
  Passed Sts2PilotTrainer.Arbiter.Tests.FightResultPanelTests.MarksAPotionOnTheChartAtTheTurnItWasSpent
  Passed Sts2PilotTrainer.Arbiter.Tests.FightResultPanelTests.PlotsAPointForEveryTurnALineReachedAndNoneWhereItDidNot
  Passed Sts2PilotTrainer.Arbiter.Tests.FightResultPanelTests.SaysSoWhereALineHadAlreadyFinished
  Passed Sts2PilotTrainer.Arbiter.Tests.FightResultPanelTests.TheOneControlIsTheButtonAndItIsWhatLeavesTheFight
```

## The same model, printed

The client draws the model; a terminal can only print it. `enter-fight --play` does that,
with the recording standing in for the player, so the whole loop below is the one the
client runs: the run is constructed, the recording's two decisions are made, the boundary
is proved against the shipped snapshot digest, the recording's own nine fight actions go
through the player-side capture, and the result is the panel's own model.

Both sides are identical here, and that is what this block is for rather than a limitation
of it - a line that came through the capture and did not match the engine's own replay of
the same actions would be a defect in the capture. The two sides being *different* is what
the retail session below shows.

```bash
set -o pipefail; ./scripts/arbiter enter-fight manifests/navegreed-OJ-6QXhNgdg.replay.json --play 2>&1 | grep -vE '^SentryGodotInitializer|^\[INFO\]|^\[WARN\]|^\[ERROR\]|^   at ' | sed -n '/Your fight/,$p'
```

```output
[Your fight and NaveGreed's]
  Both fights started from the same position.

                        You           NaveGreed
  Outcome               Won           Won
  Turns                 4             4
  Health at the start   64            64
  Health at the end     57            57
  Net health change     -7            -7
  Potions used          none          none
  Cards removed         none          none

  Turn by turn
  Turn   You                                 NaveGreed
     1   Hellraiser, Defend Ironclad         Hellraiser, Defend Ironclad
     2   Defend Ironclad, Defend Ironclad    Defend Ironclad, Defend Ironclad
     3   Hellraiser                          Hellraiser
     4   Bash                                Bash

  Health lost each turn
         Enemy health lost         Health lost               Potions used
  Turn   You          NaveGreed    You          NaveGreed
     1   8            8            4            4
     2   24           24           2            2
     3   6            6            7            7
     4   4            4            0            0

  This states differences. It does not say which fight was better.
  Health lost counts only health that came off. Damage absorbed by block is not counted.
  [Done]

report: build/evidence/enter-fight.json
```

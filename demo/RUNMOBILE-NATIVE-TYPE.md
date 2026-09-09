# Runmobile at the game's own font and size

The v0.111.0 retail client ran this branch, built and installed through `scripts/install-mod.sh`.
It was launched directly with `--force-steam=off` into the existing non-Steam test profile without driving the running Steam process.
The console lock state was read into a variable from `IOConsoleLocked` and was unlocked before the drive; the machine was held awake with `caffeinate` bound to the game process and released when it quit.
The installed `Runmobile.dll` had the same SHA-256 digest as the packaged build.
Commands start at the fullscreen main menu on a 1512 × 982-point display.

The library surfaces below draw their words in styles read from the popup's native heading, description, and button elements rather than styles synthesized by the mod.
The settings capture from this session predates the main-menu control and is not evidence for the current row.
A current settings capture is outstanding and must be retaken in the retail client after these changes.

## The run library, on the game's own parchment

```bash
cliclick c:610,799; sleep 4; cliclick c:764,718; sleep 4
screencapture -x runmobile-library-native-type.png
```

```output
```

![Others, drawn at the popup's own body copy](runmobile-library-native-type.png)

Body and supporting lines use the popup's own description style, buttons use its native ribbon label, and headings use its native header style.

```bash
cliclick c:535,327; sleep 3
screencapture -x runmobile-library-mine-native-type.png
```

```output
```

![Mine, with the run strip and the profile's retention summary](runmobile-library-mine-native-type.png)

The strip's floor numerals and the counts under card tiles use the popup description's native style rather than a smaller size synthesized by the mod.

## What this run did not cover

The playback tag and the fight result panel take their size from what the screen behind them draws ordinary text at, and neither was exercised here: both need a recorded fight walked in the client, which this session did not have the retail copy long enough to do.
Their headless containment is asserted across three surfaces and three text sizes by `FightResultPanelTests.FitsInsideTheSurfaceItIsGiven` and `PlaybackTransportStripTests.TheTagIsDrawnAtTheGamesOwnSizeAndGrowsWithIt`, which is a weaker claim than a capture and is stated as such.

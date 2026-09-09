# Historical Runmobile native-type capture

> These screenshots show an earlier popup-body-based iteration, not the current named-role implementation.
> A fresh in-client capture of the current implementation is outstanding.

The v0.111.0 retail client ran the earlier branch, built and installed through `scripts/install-mod.sh`.
It was launched directly with `--force-steam=off` into the existing non-Steam test profile without driving the running Steam process.
The console lock state was read into a variable from `IOConsoleLocked` and was unlocked before the drive; the machine was held awake with `caffeinate` bound to the game process and released when it quit.
The installed `Runmobile.dll` had the same SHA-256 digest as the packaged build.
Commands start at the fullscreen main menu on a 1512 × 982-point display.

The library surfaces below used the popup's heading, description, and button styles broadly before the current implementation assigned a named native role to each kind of text.
These captures are historical and are not evidence for the current library or settings typography.
Current library and settings captures must be retaken in the retail client.

## Earlier run-library iteration

```bash
cliclick c:610,799; sleep 4; cliclick c:764,718; sleep 4
screencapture -x runmobile-library-native-type.png
```

```output
```

![Historical Others view using the earlier popup-wide mapping](runmobile-library-native-type.png)

This screenshot records the superseded popup-wide mapping.

```bash
cliclick c:535,327; sleep 3
screencapture -x runmobile-library-mine-native-type.png
```

```output
```

![Historical Mine view using the earlier popup-wide mapping](runmobile-library-mine-native-type.png)

This screenshot predates the distinct floor-numeral, card-caption, fact, and footer roles.

## What this run did not cover

Neither the playback tag nor the fight result panel was exercised here.
Both need a recorded fight walked in the client, and fresh evidence for their current named-role mapping is outstanding.
Their headless containment is asserted across three surfaces and three text sizes by `FightResultPanelTests.FitsInsideTheSurfaceItIsGiven` and `PlaybackTransportStripTests.TheTagIsDrawnAtTheGamesOwnSizeAndGrowsWithIt`, which is a weaker claim than a capture and is stated as such.

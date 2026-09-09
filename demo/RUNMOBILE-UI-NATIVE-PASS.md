# Runmobile native UI pass, in the retail client

Captured on v0.111.0 with `--force-steam=off` into the isolated non-Steam profile, Runmobile the only mod loaded.
The self-contained review page with every state and caption is [RUNMOBILE-UI-NATIVE-PASS.html](RUNMOBILE-UI-NATIVE-PASS.html).

## Tabs and the locked Community tab

![Community unlocked: the game's own settings tab](runmobile-native-ui-community-unlocked.png)

![Community locked because the setting is off, with the plate at the head of the list](runmobile-native-ui-community-setting-off.png)

![The locked tab's tooltip names the setting and where it is](runmobile-native-ui-community-setting-off-tooltip.png)

![Community locked because no sharing service is configured](runmobile-native-ui-community-unavailable.png)

![The unavailable tooltip](runmobile-native-ui-community-unavailable-tooltip.png)

## A hover lights only the control it acts on

![Hovering a run row lights the row and nothing else](runmobile-native-ui-row-hover-only-the-row.png)

## The settings row

![Show community runs, and the keep stepper with room above and below](runmobile-native-ui-settings-row-on.png)

## The floor strip

![The run-history screen's own floor markers, the played badge and the floor-1 refusal](runmobile-native-ui-open-run-floor-strip.png)

## What these captures prove

The Community and Mine tabs are the game's `NSettingsTab`; Community wears the stats screen's lock in both locked states with a tooltip and a plate; a row hover no longer lights the tab or the pane's ribbon; the settings row reads "Show community runs" with the stepper standing off its dividers; and the strip's markers, badge, ring and numerals do not overlap.
Not captured: a controller walking the band, and the run-code lookup while locked, which needs a reachable service.

# Runmobile native-type capture

These screenshots show the current named-role typography in the v0.111.0 retail client at commit `70718c81`.
The installed archive matched the build's SHA-256 digest and ran fullscreen on the game's 1512 × 982 reference surface with `--force-steam=off`.

## Library Others tab

![Current Others view using named native text roles](runmobile-library-native-type.png)

The library heading, supporting text, tabs, input, list headings, filters, run rows, facts, numerals, and buttons use the mapped native roles for their kinds.

## Library Mine tab

![Current Mine view using named native text roles and native pagination art](runmobile-library-mine-native-type.png)

The Mine view shows the same named-role typography and the run strip pager's own gold arrow art from the game rather than a text chevron.

## Opened run pane

![Current opened run pane using named native text roles](runmobile-library-open-native-type.png)

The selected run's identity, description, floor numerals, facts, and action use their mapped native roles.

## What this run did not cover

The My Runs settings row did not render during this capture because its settings-button role lookup failed, so this evidence does not claim that surface.
The playback transport and fight-result panel, including chronology and ledger paging, were not exercised either.
Those surfaces still need a fresh in-client capture after the settings-row fix.
Focused headless tests assert their mapped styles and containment, but that is weaker evidence than a retail-client capture.

# Runmobile native-type capture

These screenshots show named-role typography in the v0.111.0 retail client.
The installed archives matched their builds' SHA-256 digests and ran fullscreen on the game's 1512 × 982 reference surface with `--force-steam=off`.

## Library Others tab

![Current Others view using named native text roles](runmobile-library-native-type.png)

The library heading, supporting text, tabs, input, list headings, filters, run rows, facts, numerals, and buttons use the mapped native roles for their kinds.

## Library Mine tab

![Current Mine view using named native text roles and native pagination art](runmobile-library-mine-native-type.png)

The Mine view shows the same named-role typography and the run strip pager's own gold arrow art from the game rather than a text chevron.

## Opened run pane

![Current opened run pane using named native text roles](runmobile-library-open-run-native.png)

The selected run's identity, description, floor numerals, facts, and action use their mapped native roles.

## Refusal popup

![Current refusal popup using named native text roles](runmobile-refusal-popup-native.png)

The mod's refusal popup uses its mapped R1 title, R2 body, and R3 button roles.

## What these captures prove

The retail captures prove the library parchment's Others, Mine, and opened-run states at named-role typography, the run strip's native gold arrow art, and the mod's refusal popup at its R1, R2, and R3 roles.

The My Runs settings row is not proven in the client yet.
It renders without throwing, but the latest client capture still shows it laid out at about a third of the native settings column.

The playback transport, fight-result panel, chronology paging, and look-back ledger paging are also not proven in the client.
Programmatic navigation was attempted and did not reach those surfaces, so they require manual navigation for a retail capture.
This change covers them with automated paging and layout tests instead.

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

## My Runs settings row

![Current My Runs settings row in its full-screen context](runmobile-settings-row-native-type.png)

![Current My Runs settings row beside the game's native settings rows](runmobile-settings-row-native-type-crop.png)

The row's label, readings, and controls take the full native settings column using the mapped roles for a settings row label, a settings value, and a button caption.
Its controls share the same right edge as the game's own controls on that screen.

## What these captures prove

The retail captures prove the library parchment's Others, Mine, and opened-run states at named-role typography, the run strip's native gold arrow art, the mod's refusal popup at its R1, R2, and R3 roles, and the My Runs settings row at its mapped native roles and full native column width.

The playback transport's hanging tag is captured in the client - `demo/RUNMOBILE-RESTORE-IN-CLIENT.md` holds it, in the fight a player was stood in by restoring their own recording.
The recorder's bookmark tag, which hangs from the same anchor in the same material, draws no text and asks no role; its in-client capture on v0.111.0 - on the loot screen, on the card-reward screen behind it, and on a real death screen after a lost fight - is `RUNMOBILE-BOOKMARK.md`, taken 2026-09-10.
Its expanded strip, the fight-result panel, chronology paging, and look-back ledger paging are not.
Programmatic navigation was attempted and did not reach those surfaces, so they require manual navigation for a retail capture.
This change covers them with automated paging and layout tests instead.
Those tests do not read the game's scene files, and the look-back ledger's role named a node v0.111.0 has not: the role table is now held against the shipped pack, which `docs/in-game-host.md` owns.

The restoring notice shown while a run is being restored is captured: `demo/RUNMOBILE-RESTORE-IN-CLIENT.md` holds it, taken a second and a half after Continue was pressed on the player's own recording, with the plate over the Compendium screen and its line at the native heading role.

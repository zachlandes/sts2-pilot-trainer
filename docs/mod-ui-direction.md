# What the mod's own surfaces look like

This began as a design brief, written when the first working transport was styled by the engineer who made it work and the captain's judgement was that the mechanism was right and the look was not.
The design phase that followed is closed: the accepted answer is the hanging tag described below, and it is what `PlaybackTransportStrip` now draws.
The full report, every state drawn over real captures, and the sources that regenerate them are in `data/sts2-playback-control-design/` in the fleet's own tree; this file is the part a future session in this repository needs.

What is settled is in "The design"; what a redesign must not break is in "What the retail client actually imposes".

## The captain's goal, in his terms

Three things at once, and the difficulty is that they pull against each other.

**It must not get in the way of any existing UI.**
A surface that covers a relic the player is fighting with, or a row they are about to choose, has failed regardless of how it looks.

**It should feel native to the application.**
Not a debug overlay dropped on top of Slay the Spire 2; something the game could plausibly have shipped.

**It must not read as part of the game.**
The player should be able to look at the screen and see two things: the game, and the mod.
The mod's controls are meta controls - they act on the recording, not on the run - and putting them at the same level of visual hierarchy as the hand, the map or the enemy intent is wrong even when it is pretty.
The distinction can come from styling, from placement, or from both; that is the design question.

**Iconography where text is doing an icon's job.**
Playback controls in particular: a transport that says "Forward" and "Play" in words is a form, not a transport.

**A reference, offered as an example rather than a target.**
The captain's closest example of a mod that gets the game-versus-mod distinction right is untapped.gg's, which surfaces pro tips on hover.
He was explicit that this is not a claim that it is the perfect implementation of anything, and that what this project is building is different.

## What the retail client actually imposes

Measured in the shipped client during the transport spike, not reasoned about.
A design that violates any of these does not work, however it looks.

**There is exactly one band the game leaves empty on every screen this journey walks past: under the top bar.**
Neow's event, the map and combat all keep their own choices at the bottom - the hand, the option rows, the proceed bar.

**That band is not wholly free.**
The run's relic inventory is drawn along its left, and it grows as the run does.
The build, seed and mod-count debug text sits at its far right.
The first chip drawn there covered relics, which is what prompted this brief.

**A surface that must outlive a room has one place to live.**
`NRun.GlobalUi` is the run's own persistent interface - the top bar, the relic inventory, the map screen - and the game swaps the room underneath it.
Anything parented to the room is destroyed at the map-to-combat transition.

**Input has to be given back.**
Everything the mod draws except its own controls must ignore the mouse, or the map, the event and the player's fight stop working underneath it.

**Controller and keyboard have to reach it.**
The mod's controls take focus. Focus is a single owner, so a control that takes it takes it away from whatever the game had highlighted.

**The game's own selected states can be driven programmatically.**
`GrabFocus` runs a control's own `OnFocus`, which is what plays the game's hover tween on an event row and scales a map node; on the map, `NSelectionReticle.OnSelect` lights the ring directly.
This is how the mod points at the recording's next decision without clicking it, and it is the strongest native-feeling thing the mod does - a designer should treat it as the anchor rather than as a detail.
Its cost is the focus rule above: pressing a mod control takes focus off the revealed target and the game puts its own highlight out, so the mod has to put it back.

## Native furniture available to build from

Reusing the game's own nodes is what made the eligibility screen and the mode card look like the game rather than like a lookalike of it.
The candidates a designer should know exist, in `MegaCrit.Sts2.Core.Nodes.CommonUi` unless noted:

| Node | What it is |
| --- | --- |
| `NGoldArrowButton` | The game's own gold arrow button. The nearest thing it ships to a directional control. |
| `NBackButton`, `NProceedButton` | The bordered back and proceed affordances the map and the rooms use. |
| `NCommonBanner` | A banner the game uses for its own headings. |
| `NHotkeyIcon` | The key or controller glyph the game draws beside a control, driven by `NHotkeyManager`. |
| `NTickbox`, `NDropdownContainer`, `NSearchBar` | Settings-screen furniture, for a catalogue rather than a transport. |
| `NGenericPopup` + `NVerticalPopup` | The modal the trainer's refusal and eligibility screens already use. The captain's judgement is that the modals are the part that already feels right. |
| `NSelectionReticle` (`Nodes.Combat`) | The ring the game puts round a controller-focused map node. |
| `NSettingsTab` (`Nodes.Screens.Settings`, scene `scenes/screens/settings_tab.tscn`) | The squared tab the settings screen and the stats screen's Statistics and Achievements tabs are: stroked outline on the selected one, half-cream label going cream on selection and gold on hover, a hover scale on the whole plate. Instantiable from its scene, which the library's tabs now are. |
| `submenu_lock.png` (`images/packed/main_menu`) | The lock the stats screen lays over its disabled Achievements tab, centred and overhanging the plate. The library's Community tab wears it while it is short of a service or the setting. `images/ui/main_menu/submenu_lock.png` is a different file with the same name, the chained lock a submenu card wears, and `LibraryNativeFurnitureTests` holds the tab to the one `stats_screen.tscn` binds. |
| `images/ui/run_history/<room>.png` + `_outline.png` | The run-history screen's floor icons, drawn by `NMapPointHistoryEntry` at 0.7 of a 60 box with the outline behind at a quarter of black. The library's run strip wears them per floor kind. |

**There is no playback iconography in the game's own art.**
No play, pause, step or skip glyph family appears in the resource paths the assembly references.
Whatever the transport's controls become has to be drawn, which is precisely why it is a design job and not a styling tweak.

## The design, as built

**The hanging tag.** 378 by 56 in the design's reference units, hung from the top bar's torn edge and right-aligned to the deck button, which puts it under the game's own meta cluster rather than over any gameplay furniture.
It does not clear the version overlay, whose seed text starts further right than the deck button ends; that overlay is the game's own debug label and a player toggles it off from the menu that put it up.
Flat charcoal at 94%, an inked gold edge, an inner hairline, two gold pins, a chamfered foot.
Same palette as the game, different material: the game's own furniture is torn stone and parchment, so a flat plate reads as not-the-game without being loud about it, and it hangs under the game's own meta cluster where controls that act on the recording belong.

**Contents, left to right.** The mark (the selection reticle the reveal lights, shrunk to a glyph); the identity block (the recording's credit over its video title where it has one, pressable only for a video and opening it at the decision's own observed timestamp); the counter as numerals with pips; the speed chip; three 30-unit glyph controls - look back, play or pause, step.

**Icon only, tooltips for words.** The captain's ruling: progressive disclosure is the game's own principle.
There is no always-visible caption line; step's tooltip names the decision it is about to make.

**The glyph family is the mod's own art**, because the game ships none - no play, pause, step or skip shape appears in any resource `sts2.dll` references.
One rule carries meaning rather than decoration: **a filled shape moves the run, a hollow shape only looks.**

**States.** Considering, with the game's screen arrived and nothing lit: the viewer sees every option the recording saw and decides in their head, and step reveals rather than commits.
Two drawn signals say which hold the tag is in, and nothing on the tag may say what the recording chose before the reveal: the mark loses its centre dot while there is nothing to point at, and the current pip is hollow until the reveal fills it.
Step's tooltip there is "Show" with the counter and no caption; the caption naming the decision belongs to the second press.
A screen with one option has no consider beat - the rule is on the decision's option count, read from the screen on arrival, and arrival is the reveal there.
Holding, with the target lit by the game's own selected state, exactly as before; the once-per-run note appears at the first reveal and not before it.
Playing, with each hold drawn as a line draining along the tag's foot, the consider hold and the reveal hold one after the other on the same timer.
Looking back, with a ledger of the decisions already made hung beneath - it exists because those screens are gone, and the run must never be rewound to answer for them.
Step is offered there and walks the view forward through the ledger rather than committing, so its tooltip drops the sentence promising a commit; the counter and the caption still say which decision is on screen.
A control's tooltip is part of what it is: one that names an action it does not perform is the same defect as one that cannot be pressed.
Opening, between the last recorded choice and the fight it leads to: the tag stays exactly where it was and everything that would move the run is refused, because a run with nothing left to commit must not still be offering to commit it. The speed control is not refused there - it does not move the run.
On a run restored to a floor rather than walked there, Opening is the first state the tag is in, and its counter reads the floor - `Floor 3` - with no pips, because a count of decisions would be the tag claiming somebody watched them.
The window stays wordless, as it is after a walk: the floor is the whole of what it says.
Preparing, before that, is the wait for the save to be materialised, and the tag draws nothing there because there is no run for it to hang under yet.
What is drawn instead is not the tag at all but a plate of this mod's own: `RestoringNotice` in `Sts2PilotTrainer.Trainer` derives the headline from the phase, and `RestoringOverlay` in the mod parents a scrim and one centred line under `NGame` rather than under a run, at the native heading role, with an ellipsis that animates.
One drawing, and this is it.
The client's own `res://scenes/screens/main_menu/loading_overlay.tscn` was borrowed here for a while and is not any more: that scene ships hidden - its root is saved with `visible = false` - so borrowing it drew a present-but-invisible surface over the whole wait, which is the blank screen this surface exists to remove, and it was the half no test process could execute.
What was dropped with it is the native loading-screen backdrop; a follow-up may borrow the scene again with its visibility handled and an in-client capture behind it.
That plate is the one surface here that will draw its line at Godot's own size rather than refuse, and only where the native reading itself fails; every other surface refuses instead, and this one refusing would be the blank screen again.
The indicator is indeterminate on purpose: the packaged arbiter prints its floor arrivals only once the replay it is doing has finished, so a floor-by-floor bar would need a channel out of that subprocess that this build does not have.
Between screens, the window between committing one decision and revealing the next: look back, play and step are all refused, on the same rule and for the same reason - a press there acts on a state nobody has been shown, and the reveal that follows discards it a frame later.
The one exception is play once it is already running: it is drawn as pause there and pause is never refused, because it stops the run rather than moving it.
The speed control is offered, as it is in Opening.
The two ways look back can be refused stay distinguishable in the model, because nothing behind yet and not yet are different answers to somebody who pressed.
The chip during the player's own fight: the mark and the recording's credit, silent until pressed.
It stays exactly as it is for the couple of seconds the game takes to draw its own ending, drawn and pressable, with both of its rows refused - both act on a fight that no longer exists.
Ended, once the ending is drawn: the chip stays and the post-fight choice hangs under it in the chip menu's own shape, opened on its own and re-opened by pressing the chip.
The rows are the teaching's order - look, then act - and are present or absent, never disabled: Show the comparison, Fight it again and Leave on this build, with Watch {creator}'s fight and Continue as you absent until the phases that add them merge, and Continue as you always absent on a loss.
Nothing about the recording's line is drawn unbidden; the first row draws the comparison panel when this build carries a line bound to that run, or a plain no-line notice when it does not, and Done returns to the choice.
The glyph rule extends by one shape there: **a hollow eye is a reveal that only looks.**
A row already taken this sitting carries the teal dot the speed menu uses for "the one you are in", and the same hollow eye marks the fight's row in the run view; that state is in memory for the sitting, never written, and gates nothing.
On a win the menu hangs over the loot screen, which stays visible and locked; on a loss it hangs the same way over the game's own ending, from the run's persistent interface, and where that interface did not survive the ending it is drawn in the game's modal container instead.
Refused: the mark becomes the warning glyph and every control is drawn and refused, the speed control included - in the model.
It is not on screen today: the teardown applies the refused state and detaches the tag inside one call stack, so no frame is ever drawn with it.
The state stays because it is what keeps the derivation total - every phase a journey can be in has an answer - and the sentence a player actually reads on a refusal is the popup's.
That it is never drawn is settled, not pending: decided by the project's coordinating owner under the captain's explicit delegation.
The refusal a player reads is the game's own popup, and making the tag visible there would mean keeping it alive across a return to the main menu that its parent interface does not survive.

**The chip offers two directions and no third.** Jump to the beginning rebuilds the run to the proven combat start; jump to the end finishes the attempt where it is.
Both leave the attempt, so both ask through the game's own confirmation popup first, and both are refused once the fight has ended.
**A refused menu row says nothing** - no reason text, no tooltip.
Decided by the project's coordinating owner: the only refused row that exists is jump to the end before anything has been played, refused because there is no result until the player has taken an action of their own - one card is enough, it is not a completed turn - and that clears through the very action the player is already there to take.
A permanent explanation for a state that resolves itself in seconds costs more attention than it saves, and drawing one would mean inventing a layout for reason text in a menu row that nobody has approved.
A tooltip was weighed as a middle path and rejected: a tooltip answers a player who already suspects something is broken, and nothing here is broken.
A refused *control* is the other case and keeps its tooltip, which says why where a reason has been written for it.
There is no watch row and no comparison inside a fight: the captain's ruling is that a player diverges from the recorded line almost at once, so the comparison points are the whole recorded fight watched and the finished fight's result.

**Refusals read as a player's sentence**, with the engine's exact diagnostic behind a details fold and always in the log.
The refusal is not softened; only the sentence a player reads changes.
A refusal the mod raises with no engine diagnostic to fold - the fight that never finished opening - is the player's sentence alone, and the state it saw is logged rather than folded; `docs/in-game-host.md` owns that case.

**One measure for everything that hangs below the tag.** The note, the look-back ledger, the speed menu and a tooltip all hang under it, and the plates are translucent because the game is meant to show through them - so two of them on the same band are not one covering the other, they are both legible at once and neither readable.
Each hangs below whatever is already there. The client drew the speed menu straight over the ledger before this rule existed.

## One derivation, and three questions per element

**What the tag is at any moment is derived in exactly one place, from the phase and the run's facts.**
`PlaybackTransport.For(phase, facts)` is total and pure: every phase has an answer, null included for the two that put nothing on screen, and there is no other way to construct a state.
`RecordedFightRun.Transition` is the only thing that changes the phase, and it re-derives; every fact that can change - a decision revealed, a card played, a speed chosen - re-derives too.
This rule is not tidiness. Four defects on this surface came from a state built by hand at the site that changed it and then never re-derived: a menu still hanging under a chip, a chip with no press target, an opening window stating a speed that was not in force, and a chip whose one remaining offer stated a reason that had stopped being true at the first card.

**Every element answers three questions separately: is it present, is it drawn, can it be pressed.**
`TransportSurface` is that table, one column per mode, and `PlaybackTransportStrip` projects it without reading the mode at all.
The three cannot be one answer, because in Godot a control that is not visible receives no input: "present but silent" is the chip's press target and is unsayable while one boolean decides both.
Absent is the only state that hides a node, and an absent element is never hit-tested, never tooltipped and carries no handler.

**The same rule now carries a second surface.**
The settings row about the player's own runs - keep, size, remove - is derived in one place too, by `MyRunsRow.For(facts)` in `Sts2PilotTrainer.Trainer`, and `MyRunsSettingsRow` in the mod projects it without working anything out.
It is there for the reason the transport's rule is there: the number the policy keeps, the number on the disk and what a removal just did are three facts that can disagree, and a row where each control set its own label would show a reading taken before an act beside a receipt taken after it.
Where that row hangs, what it reads, and where its drawing departs from the accepted design are in [in-game-host.md](in-game-host.md) under "The settings row, and the size figure".

## Type: the game's own, at the game's own size

**No surface in this mod writes down or derives a font size.**
`GameText` reads one named native node for each text role, including the node's locale-substituted font and its design size.
An auto-sizing native label contributes `MaxFontSize`; a fixed-size label contributes its own `font_size` override.
There is no median, ratio, fallback size, or first-descendant search.
Missing native furniture refuses the surface rather than substituting Godot's default or deriving a related size.
Each role's node path is a claim about the shipped build, checked against the game's own pack and swept once at adoption; [in-game-host.md](in-game-host.md) owns that check and what it does where the pack and the prepared assemblies disagree.

**Each piece of text has the native role of the same kind.**
The settings row reads its row label, value, stepper numeral, and button caption from the named live controls on that settings screen; its supporting line uses the mapped native secondary-row role.
Its Runmobile heading is at the button-caption role too, and its Remove caption copies the native settings button's outline thickness through `GameTextOutline`, with the colour turned to the button's own hue by the rule [in-game-host.md](in-game-host.md) owns under "The settings row, and the size figure".
The run-history plate uses the history screen's fact role for its status, reason, and note, and the native button-caption role for pressable rows.
The transport uses the profile identity roles for creator and title, the portrait-tip numeral for its counter, the dropdown value and item roles for speed and menus, the map-point reward role for its ledger, and the hover-tip title and body roles for its tooltip and note.
The result panel resolves every role from named scene resources before it is attached: popup heading and body, list headings and numerals, score labels and values, section headings, chart numerals, card captions, and the button caption.
The library likewise distinguishes list headings, row numerals, secondary lines, facts, floor numerals, card captions, fields, tickboxes, and footer counts rather than treating the popup body as all of them.

**A surface whose boxes were measured around its own text scales with the native reading.**
The tag and the result panel scale their geometry with the native reading rather than growing words outside fixed boxes.
The result chronology measures how many native-height turn rows fit beside the chart and pages the rest through the game's paginator arrows, so no supported surface compresses or clips a turn.
The look-back ledger measures its page from the viewport in the same way, and the library strip uses the run-history screen's own arrow image.

**A surface that sits inside the game's own container is a child of it, and asks it for height only.**
The settings row is inserted into the modding entry's parent column immediately after that entry.
It asks the container for height and never for width, then lays itself out again after the container sorts because the settings screen has not been laid out when its `_Ready` runs.
Adding it also leaves the screen's scroll extent short until the panel that owns the extent is asked to measure again; [in-game-host.md](in-game-host.md) owns why and how `MyRunsSettings.RefreshExtent` asks.
`demo/RUNMOBILE-NATIVE-TYPE.md` records current retail captures of the library, refusal popup, and settings row, and names the surfaces that still require manual navigation for an in-client capture; `demo/RUNMOBILE-SETTINGS-SECTION.md` holds the settings section under its heading, with its native controls and the corrected scroll extent.

## The library's borrowed furniture

The captain's judgement on the first library was that its tabs read as buttons and its floor strip as a row of specks, and that the game already had the shapes.
Three of them are borrowed rather than drawn, and the rule for each is the same: the game's own node or image where one exists, instantiated or loaded and never redrawn; the mod's own glyph only where the game has none.

**The tabs are `NSettingsTab`**, the scene behind the stats screen's Statistics and Achievements tabs, at the scene's own 256:90 proportions and the ribbon's height, or shorter where a narrow window caps the width, because the proportions outrank the height.
Selected and deselected through the tab's own `Select` and `Deselect`, exactly as `NStatsTabManager` does it, so the outline, the label weight and the hover are the game's.
The current tab is selected and takes no press; it still hovers, as the game's do, but it is not a focus stop, and the other tab is - this band has no shoulder hotkeys, so focus is the only controller route to it.

**The lock is the stats screen's**: `submenu_lock.png` over the Community tab, in the proportions `stats_screen.tscn` gives it over the Achievements tab, with the reason in the tab's tooltip.
A locked tab here is still pressable, which is the one departure from the stats screen and is deliberate: what is behind it - the runs included with Runmobile and any run looked up by code - is still there, and the list is drawn as usual under it.
The lock is the icon, its hover tooltip and one line over the tabs saying in plain words what is missing and where the setting is; nothing is laid over the list, because a player who cannot see the runs cannot press them.
`CommunityLock.For` is the one derivation of both sentences.

**The floor markers are the run-history screen's** icons, per floor kind, with the outline behind at the history entry's own quarter of black; the mod's hollow ring stands in only for a kind nothing established or an icon a build has not got.
The played tick is the mod's own, filled and on a disc, hung off the icon's top-right corner where the history entry hangs its quest badge, and the numeral is under the marker with clear space rather than on it.
`LibraryPaneArt.CellGeometry` is the one place the four parts of a cell are placed and the tests hold them apart.

**Every duplicated ribbon owns its materials.**
The retail popup button lights up by writing to its image's HSV shader and its outline's blend mode, and a `Duplicate` shares both with its prototype; one hover then lit the tab, the row and the pane's ribbon at once.
`LibraryRibbonArt.OwnMaterials` copies both before the duplicate enters the tree, so a highlight is on the control it acts on and nowhere else.

`demo/RUNMOBILE-UI-NATIVE-PASS.html` is what all of this looks like running in the player's client: every state captured, with a caption, in one self-contained page.

## What a redesign owns, and what it does not

Change what `PlaybackTransportStrip` draws.
Leave `PlaybackTransportDock` (where the tag lives and what it is anchored to), `RecordedFightReveal` (the game's own selected states) and `PlaybackTransport` (what it says, and the one derivation of what it is) alone - the last of these owns the words and is the one place to change them.
A redesign that needs an element to appear, disappear or refuse somewhere new changes a cell in that table, and the strip follows.

## Carried forward, not built

- **Jump to the end adds no comparison kind.** It ends the attempt and the existing result surface says what it already says about a fight left before it ended.
  A partial player line - "left at turn N", the turns played kept and the chart's line stopping there - is a change to the comparison contract and belongs to the comparison owner; see `docs/comparison-direction.md`.
- **The video title** is `source.video.title` in the manifest, filled at ingestion. Until a recording carries one the identity block shows its resolved credit alone.
- **Screens the journey does not yet walk** - loot, card rewards, shops, rests - have no caption owner. The tag is built to carry them unchanged; the reveal refuses them.
- **No new hotkeys.** On-screen controls only, so the controls carry no hotkey glyph: one would name a key that does nothing.
- **The tag's anchor is measured once**, in `PlaybackTransportDock.Attach`, and nothing remeasures it, so a relic row that grows past the measured band or a window resized mid-journey leaves the tag where it was.
  A remeasure method existed and was removed unwired rather than shipped down a path that had never run in the retail client; the gap it described is still real.

`docs/in-game-host.md` records the behaviour and the traps; this file records what the surfaces are; `demo/PLAYBACK-TRANSPORT.md` is what they look like running in the player's client.

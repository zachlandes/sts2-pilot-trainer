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

**There is no playback iconography in the game's own art.**
No play, pause, step or skip glyph family appears in the resource paths the assembly references.
Whatever the transport's controls become has to be drawn, which is precisely why it is a design job and not a styling tweak.

## The design, as built

**The hanging tag.** 378 by 56 in the design's reference units, hung from the top bar's torn edge and right-aligned to the deck button, which puts it under the game's own meta cluster rather than over any gameplay furniture.
It does not clear the version overlay, whose seed text starts further right than the deck button ends; that overlay is the game's own debug label and a player toggles it off from the menu that put it up.
Flat charcoal at 94%, an inked gold edge, an inner hairline, two gold pins, a chamfered foot.
Same palette as the game, different material: the game's own furniture is torn stone and parchment, so a flat plate reads as not-the-game without being loud about it, and it hangs under the game's own meta cluster where controls that act on the recording belong.

**Contents, left to right.** The mark (the selection reticle the reveal lights, shrunk to a glyph); the identity block (creator over video title, pressable, opening the video at the decision's own observed timestamp); the counter as numerals with pips; the speed chip; three 30-unit glyph controls - look back, play or pause, step.

**Icon only, tooltips for words.** The captain's ruling: progressive disclosure is the game's own principle.
There is no always-visible caption line; step's tooltip names the decision it is about to make.

**The glyph family is the mod's own art**, because the game ships none - no play, pause, step or skip shape appears in any resource `sts2.dll` references.
One rule carries meaning rather than decoration: **a filled shape moves the run, a hollow shape only looks.**

**States.** Holding, with the target lit by the game's own selected state. Playing, with the hold drawn as a line draining along the tag's foot.
Looking back, with a ledger of the decisions already made hung beneath - it exists because those screens are gone, and the run must never be rewound to answer for them.
Opening, between the last recorded choice and the fight it leads to: the tag stays exactly where it was and everything that would move the run is refused, because a run with nothing left to commit must not still be offering to commit it. The speed control is not refused there - it does not move the run.
Between screens, the window between committing one decision and revealing the next: look back, play and step are all refused, on the same rule and for the same reason - a press there acts on a state nobody has been shown, and the reveal that follows discards it a frame later.
The speed control is offered, as it is in Opening.
The two ways look back can be refused stay distinguishable in the model, because nothing behind yet and not yet are different answers to somebody who pressed.
The chip during the player's own fight: the mark and the name, silent until pressed.
It stays exactly as it is for the couple of seconds between the fight ending and the result panel arriving, drawn and pressable, with both of its rows refused - both act on a fight that no longer exists.
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

## What a redesign owns, and what it does not

Change what `PlaybackTransportStrip` draws.
Leave `PlaybackTransportDock` (where the tag lives and what it is anchored to), `RecordedFightReveal` (the game's own selected states) and `PlaybackTransport` (what it says, and the one derivation of what it is) alone - the last of these owns the words and is the one place to change them.
A redesign that needs an element to appear, disappear or refuse somewhere new changes a cell in that table, and the strip follows.

## Carried forward, not built

- **Jump to the end adds no comparison kind.** It ends the attempt and the existing result surface says what it already says about a fight left before it ended.
  A partial player line - "left at turn N", the turns played kept and the chart's line stopping there - is a change to the comparison contract and belongs to the comparison owner; see `docs/comparison-direction.md`.
- **The video title** is `source.video.title` in the manifest, filled at ingestion. Until a recording carries one the identity block shows the creator alone.
- **Screens the journey does not yet walk** - loot, card rewards, shops, rests - have no caption owner. The tag is built to carry them unchanged; the reveal refuses them.
- **No new hotkeys.** On-screen controls only, so the controls carry no hotkey glyph: one would name a key that does nothing.
- **The tag's anchor is measured once**, in `PlaybackTransportDock.Attach`, and nothing remeasures it, so a relic row that grows past the measured band or a window resized mid-journey leaves the tag where it was.
  A remeasure method existed and was removed unwired rather than shipped down a path that had never run in the retail client; the gap it described is still real.

`docs/in-game-host.md` records the behaviour and the traps; this file records what the surfaces are; `demo/PLAYBACK-TRANSPORT.md` is what they look like running in the player's client.

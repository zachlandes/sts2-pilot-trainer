# What the mod's own surfaces should look like

This is a design brief, not a record of what is built.
It exists because the first working playback transport was styled by the engineer who made it work, and the captain's judgement on seeing it in the client was that the mechanism is right and the look is not.
It captures his goal, the constraints the retail client actually imposes, and the native furniture a designer can build from, so that the next pass is a design decision rather than another set of hand-picked colours.

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

## What is provisional in the code today

`PlaybackTransportStrip` in `Sts2PilotTrainer.Mod` draws the transport, and every colour, size and position in it is provisional.
Its palette, its font sizes and its docked band are the engineer's, chosen to be legible in the client and to keep off the relics; none of it is a design decision anybody made.
What is not provisional is underneath it: the strip is one long-lived node, it is parented to the run's persistent interface, it lets clicks through everywhere but its controls, its controls take focus, and it collapses during the player's own fight.
A redesign should change what `PlaybackTransportStrip` draws and leave `PlaybackTransportDock`, `RecordedFightReveal` and `PlaybackTransport` alone - the last of these owns the words and is the one place to change them.

The transport's four control labels - `ForwardButton`, `PlayButton`, `PauseButton` and `PreviousStepCounter` in `TrainerCopy` - are provisional with the same force, and are marked as such where they sit.
They are placeholders for controls, not approved copy: the iconography direction may remove three of them outright.
Everything else in `TrainerCopy` is approved wording and is not the design's to change.

`docs/in-game-host.md` records the behaviour and the traps; this file records only what the surfaces should become.

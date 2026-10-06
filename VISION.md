# Vision

`Runmobile` exists so that Slay the Spire 2 players can keep, share and play from real runs, the way chess players keep and pass around games and problems.
It records every run a player plays, lets them mark the fights worth someone else's time, and stands anyone who opens that run in those fights exactly as they happened, to play themselves and then compare with the line that was played.
It serves players who learn from strong players' runs and players who want their own runs seen, and its runs come from players recording and uploading their own play.
It owns exactly one thing: a recorded run, verified by the real game engine, as something a player can keep, share and play from inside their own retail client.

## Exact or refused

A player is stood in the recorded fight, proven by the real game engine, or in the engine's own consequence of a choice they made differently from the recording, or the fight is refused with a sentence saying why.
Fidelity to the original is perfect, down to every pseudo-random outcome that affects play: draw order, shuffles, enemy moves, offers, rolls and the position of every random stream.
A shared fight is only worth playing if it is the fight that happened, so an unknown verb, a missing card, another build or a drifted state is refused, never approximated.
A game update does not retire a recording, and keeping it playable never means playing it approximately on a build it was not made on; the way to keep it is to reach the build it was made on, such as an older game version the player keeps and switches to when a recording needs it, and the mod fetches or keeps such a version only if the player opts in.
The real engine is the only evidence that a recording is exact.
Arithmetic over video frames, reader confidence, a green test suite and a screenshot are filters worth having, and none of them stands in for a replay through the shipped game.
Every value in a recording says whether it was observed, inferred, engine-produced, declared or captured, with the coordinates to re-check it.
A claim about integrity, continuity or the mod environment is derived from what was observed, never from what a component assumes.
Another mod may run beside a recorded fight when what it actually patched is observed to touch presentation only; one that touches gameplay, or that cannot be read, refuses the fight.

## The player's game is not ours

The installed game, its saves, its profile and its unlocks are read-only inputs, with one exception: content a recording needs and the player has not unlocked may be unlocked for that recorded run, and the player's unlocks are put back as they were when it ends.
The exception exists because the runs most worth playing are Ascension 10 runs by players with nearly everything unlocked, and the unlock state changes what a run deals, so a fight is reproduced exactly only under the same unlocks the recording machine had.
Playing from a recording writes nothing to saves, stats or run history, and that is measured with a ledger of the game's files rather than asserted.
Recording a player's own run changes nothing about how it saves or counts.
The mod writes only under its own directory, through one writer, and never writes a Steam id, machine path, profile id or hardware.
A multiplayer game gets nothing from this mod, not even an indicator, because someone in that game never installed it.
No path permanently edits a save, a profile, an unlock, a build or a game mode, including to make a recording playable.

## Use the game's own machinery

Every decision the mod replays goes through a command the retail client itself calls, found by reading the game, never reimplemented.
Every surface borrows the game's own scenes, fonts, art and controls, and a surface whose native furniture is missing refuses rather than substituting a default.
What the mod knows about a game build lives in one owner, and a new build is adopted by a written procedure that turns every change into a diff someone reviews.

## Learning without a verdict

A fight is entered at its real start, and a player can practise from any turn of it that the engine restores exactly and verifies against the recording.
A player can also take a different choice than the recording did before a fight, such as another card reward, and play the fight the engine deals from there.
The comparison shows two fights side by side, won or lost, and never scores, ranks or says which line was better.
A recorded answer is never shown before the player asks for it, and never withheld once they do.
There is no solver and no candidate search, and a view across many players, such as how often an option was taken, is not ruled out but is not planned and never becomes a verdict on one player's line.

## Recordings belong to the player

Every singleplayer run is recorded on the player's machine by default, whole, refused rather than repaired where the record has a hole, and the player can turn recording off.
Recording is on by default because a run library is expected to record runs and sharing needs recordings to exist.
A player marks a fight worth playing at the moment it ends, and the mark travels with the run so whoever opens it knows where to stand.
Ordinary play, such as Save and Quit, Continue or a reload, never costs a player their recording, though a run that cannot be verified is kept but not shared.
The project may run an official sharing service; sharing to it is opt-in in the mod's settings, never prompted at the start of a run, and nothing is sent before the player turns it on.
Sharing is a deliberate act per run and never automatic, because the community catalogue holds runs worth someone's time: a player shares a run when it meets that bar and gives it the metadata that makes it findable.
A run a player played on from a different choice than the recording is a real run they may share, credited to them and linked to the run it branched from, though not in the first release.
Nothing else leaves the player's machine unless they opt in, including diagnostics about a refused or broken recording.

## Scope

Runmobile is not an undo mod for a player's own live run, not a fight builder, not a solver or coach, and not a multiplayer tool; recording a multiplayer run is a non-goal even when everyone in it consents.
It is not a video extractor: a handful of runs reconstructed from creators' videos bootstrap the community catalogue, and from there the catalogue grows from players' own uploads; an extractor is built only if launch needs it.
A view of runs outside the game, such as a web page for a shared run, is not ruled out and is not on the critical path; the in-client path comes first.
It makes a recording playable by recreating the recording machine's conditions for that run, including its unlocks, and never by changing game rules or the player's lasting progress.
It claims only what is built; a planned feature is marked as coming soon until it exists.
Each concept has one owner, and a new capability extends that owner or is not built; a superseded path is removed rather than left beside its replacement.
Nothing extracted from the game or from a source video is ever committed.

A change aligns when it helps players keep, share or play from real runs, makes a recorded fight more exactly reachable, verifies more of what a recording claims through the real engine, makes a refusal clearer to the player, or brings the mod closer to the game's own look and commands.
A change should be resisted when it approximates a replay, trusts a proxy instead of the engine, writes outside the mod's directory, sends anything the player did not opt into, uploads a run the player did not choose to share, judges a player's play, adds a second owner for an existing concept, or draws anything in a game the mod should leave alone.

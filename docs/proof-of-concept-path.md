# The path to a proof of concept the captain can try

This document names the direct path from the merged replay arbiter to a first proof of
concept somebody can personally run against one recent NaveGreed VOD, and what is
actually runnable at each step along the way.

It is a plan, not a contract.
[Comparison direction](comparison-direction.md) owns what a replay result has to keep
and where a replay may start; [environment identity](environment-identity.md) owns
what makes two runs the same run.
Nothing here overrides either.

## The loop the first proof has to close

1. Establish and verify the run's identity: the exact seed, the game mode, the build
   the VOD was recorded on, Ascension 10, and the unlock prerequisites - against the
   player's own installed game and profile.
2. Start, or reset to, the entire captured combat.
3. Let the player fight it to the end.
4. Produce a useful whole-combat comparison against the VOD's solution to that same
   fight.

The unit is the whole fight, and the boundary is combat start.
That is a decision with teeth and it rules out a turn-level reset, a pre-turn branch,
and a solver; see [comparison direction](comparison-direction.md).

## What is already proven

Each of these is computed rather than concluded, and `gate` is where they are
assembled into one verdict.

- **The seed, established without reading it.** `verify-seed` regenerates each
  candidate's Act 1 map through the real engine and compares topology against the map
  transcribed from the video.
- **Environment identity and player prerequisites.** `preflight` compares build,
  content hash, acts, unlock categories and - reading a real profile - ascension
  availability, and refuses with in-game remediation rather than replaying into a
  mismatch. It never writes to a save, a profile or the install.
- **Headless replay of the covered history through the real shipped engine**, with 47
  VOD-observed values reproduced and every checkpoint compared field by field.
- **Determinism across fresh processes**, and rejection of four deliberately corrupted
  histories, two of which pass every arithmetic check the frames allow.
- **The combat-start snapshot**, derived, cached under a key bound to the history that
  produced it, and re-derived in a fresh process to be read at all.
- **A stored result that keeps the shape of the fight**, not only its last frame:
  `VerificationReport.Trace` samples canonical state either side of every action.
- **That the relevant mods do not alter deterministic RNG.** `baselib-reachability`
  instruments every `PowerCmd.Apply` in this exact history and proves its detector with
  an injected negative control. This is the one-time conclusion; there is no recurring
  mod-by-mod inference machinery and none is wanted.

## What this change established, with direct evidence

Three things were unknown, and driving the real engine settled them.

**A whole combat does complete in the headless host.** Driven from run start through
Neow and the first map node, the opening fight of seed `P1L0TTRA1NER` resolves to a
victory on turn 5 with the player at 64 of 80 health, and the run continues onto the
next floor with its combat rewards. Before this, no fixture had ever carried a fight
past its opening turn, and `docs/comparison-direction.md` said so.

**The arbiter could not see that it had happened.** `combat.in_progress` was projected
from `PlayerCombatState is not null`, and that object outlives the fight: after the
enemy died it still read `true`, with the player's turn phase at `None`.
Every quantity the comparison needs - total turns, net health change, final health - is defined over a *completed* fight, so a result that cannot tell a finished fight from a live one cannot carry any of them honestly.
`CombatManager.IsInProgress` is the signal that
actually tracks the fight, and it read `false` with `IsOverOrEnding` true at exactly
the right moment.

**The engine's own combat history cannot be the store.** `CombatManager.History` does
carry round-stamped, attributed entries while a fight is running - damage received with
its dealer and card source, potions used, monster moves. It is also cleared when the
combat ends: read after the victory, it held zero entries. So it is not a source a
stored result can consult afterwards, and the comparison contract derives from the
replay trace, which is kept.

## The slices, in dependency order

### S1 - Complete-combat replay, and the comparison contract - done

Make a finished fight observable and give its two projections one owner.

- `combat.outcome` in the canonical state, sourced from the engine's own combat
  lifecycle, and `combat.in_progress` corrected to mean the fight is live.
- The synthetic engine fixture carried through to the end of its first combat, so
  there is a real completed fight to compute over.
- `CombatProjection` in `Sts2PilotTrainer.Replay`: the combat summary and the turn
  detail, derived from the trace, kept apart, computing nothing that ranks a line.
- `CombatComparison`: two completed fights whose complete canonical combat-start
  snapshot digests match, put side by side.
- `combat-compare`, which does all of that from the command line.

**Runnable now:** `./scripts/arbiter combat-compare <a> <b>` prints the summary and
turn detail for two completed fights and their differences, and `combat-snapshot` no
longer calls a finished fight an active one.
This is step 4 of the loop, with engine-produced lines standing in for a human's -
which is what can be honest before a mod host exists.
S2 added the recording's own fight as another completed side; every side is still
engine-replayed, because a human's fight cannot be captured until S3 and S4 land.
[demo/DEMO.md](../demo/DEMO.md) has it with its real output.

### S2 - The VOD's complete first combat in the manifest - done

The shipped manifest covered five actions: Neow, the map move, two cards and the end
of turn one.
A comparison against "the VOD solution" needs the VOD's whole fight, read from the
video the same way those five were, with the same provenance and timestamps.

- Six more actions, read from the recording at 3840x2160 and carrying the timestamp
  that lets anyone re-check them: two Defends on turn 2, the second Hellraiser on turn
  3, the two turns those ended, and the Bash that killed the enemy on turn 4. Eleven
  actions, four turns, one victory.
- Six more checkpoints, and 47 observed values compared field by field, up from 20.
  The ones that carry the reconstruction are the two turn-start hands: Hellraiser
  plays every Strike the draw turns up, so the hand a turn opens with is a fact about
  where the shuffle put five cards, and the video shows it.
- `covered-fight` in the publication gate: the reproduced history has to cover a
  fight from its combat start to the end of that fight. Read through
  `CombatProjection.CoverageOf`, the same reader the projection refuses on, so the
  gate and the comparison cannot disagree about whether a fight ended.
- The negative controls nominate the turn-1 Defend. Without a nomination they damage
  the last play, which in a fight replayed to its end is the killing blow - and
  omitting the killing blow leaves a shorter history that is self-consistent.

This was authoring one manifest, not building an ingestion pipeline, and it stayed
that way. No code in this repository reads a video: the frames were read at source
resolution and what they show was written into the manifest by hand, each value with
the timestamp that lets anybody open the public recording and disagree.

**Runnable now:** `./scripts/arbiter gate manifests/navegreed-OJ-6QXhNgdg.replay.json`
returns `PUBLISHABLE` over a complete fight, and `combat-compare` puts the VOD's real
solution on one side.
The only second line of that fight is itself: nobody has played it in a retail client,
so the comparison against the recording is the recording, and it says so.

### S3 - The in-game mod host

`Preflight.EvaluateLiveGame` is already the API a host must call before showing a
player anything, and nothing has ever called it from inside the retail process.
`preflight-live` demonstrates it in the headless sandbox and refuses by design, because
the sandbox profile is empty.

This slice is the mod that loads in the shipped game and calls it against the player's
real profile and run, refusing with the same actionable remediation when the game
cannot faithfully represent the VOD.

**Runnable when it lands:** the captain launches Slay the Spire 2 with the mod and is
told, in game, whether his install and profile can represent this VOD - and if not,
exactly what to play to fix it.

### S4 - Start or reset the captured combat, in the live game

Construct the run at the VOD's identity inside the retail process and enter the
captured fight at the combat-start boundary.
The snapshot machinery already defines that boundary and refuses a drifted one.

**Runnable when it lands:** the captain presses a button and is standing in the
NaveGreed fight, at Ascension 10, with the same hand.

### S5 - The player's own fight, compared

Capture the player's completed fight as a trace through the same projection, feed both
sides to the S1 contract, and show him the result.

**Runnable when it lands:** the loop is closed. The captain fights the VOD's fight and
reads how his fight differed from NaveGreed's.

### Later, and only a hypothesis

The turn-indexed chart of player-versus-VOD health loss with potion markers is recorded in
[comparison direction](comparison-direction.md) as an interface hypothesis.
It is not a commitment, and discarding it is a good outcome.

## Known limits that no slice above removes

**The source game mode is not identified.** Standard and custom-with-no-modifiers agree
at every observed checkpoint, and every single modifier of the build's 17 changes one.
The gate reports path-specific parity across the enumerated configurations, not an
identification, and it says so.

**Damage absorbed by block is not included in health lost.** Enemy health lost is the
decrease in enemy hit points, so enemy block depletion is not counted. Player block
is reset at the start of a turn and the trace samples either side of an action rather
than inside one, so player health lost likewise reports only the damage that got
through.

**Nothing here guarantees a retail player has passed the live gate.** No mod host
exists yet; that is S3, and until it does every live claim is a claim about a headless
process.

**No comparison has two independent lines of the recording's fight.** The recording is
one completed side; the other side of a comparison against it can only be the
recording again, because the second line is the player's and capturing it is S5.
Authoring one instead would be inventing a decision nobody made.

## Deliberately not built

No turn-level reset or branching. No solver. No generalized VOD ingestion and no
multi-VOD support. No charting. No broad interface work. No presentation designed
around rare permanent card removal.

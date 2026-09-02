# Environment identity: what has to match, and what matching proves

A replay is only exact if it happens in the same environment the run happened in.
This is the list of what "the same environment" turns out to mean, why each field is
on it, and — the part that matters more — what a full set of matches still does not
establish.

Two of these fields are here because a replay that looked correct turned out not to
be. They were found by measurement, not by design, which is the honest reason to
trust the list more than the first version of it.

## The fields

| Field | Where it comes from | Why it is identity |
|---|---|---|
| `build_version` | The game's version overlay; the local `release_info.json` | Content and balance change between builds. There is no migration path. |
| `build_date_utc` | The same overlay line | Disambiguates a version. The overlay renders the release timestamp **in UTC**: a build stamped `2026-08-13T17:39-07:00` shows as `2026.08.14`. Comparing in local time is off by a day. |
| `game_mode` | Not rendered anywhere | Persisted by the game on every run and every save, and it changes run setup. Daily and custom runs carry modifiers. |
| `seed` | The version overlay | The obvious one. See [seed verification](#verifying-a-seed-without-reading-it). |
| `content_hash` | The overlay's `HASH` line | The game's own model-id database hash — the value its multiplayer layer compares between peers. See [what the hash covers](#what-the-content-hash-covers). |
| `ascension` | The in-run badge, and the end-of-run summary | Changes enemy health and intent damage. Measured: the same encounter rolls 57 HP / 4 damage at ascension 0–6 and 59 HP / 6 damage at 7+. |
| `character` | The player sprite and starting deck | Obvious. |
| `mods` | The overlay reports a **count**; the identities came from a separate investigation | A named environment, kept next to the hash rather than replaced by it. See [the mod environment](#the-mod-environment). |
| **`acts`** | **The act's name on the map screen** | **See below. This one is easy to miss and produces a completely different run.** |

## The game-mode limit

The source video never renders its game mode.
The manifest records `standard` as an inference, not an observation.

The real-engine mode-discrimination probe replays the reconstructed prefix under standard, custom with no modifiers, and daily without its date-selected modifiers.
All three match every observed checkpoint and every canonical field except the recorded `run.game_mode`; their full final-state digests differ.
A behavior-changing custom modifier proves the detector catches terminal divergence, and a reordered-history control proves it catches checkpoint divergence even when the terminal state converges.

Those controls validate the instrument, but they do not identify the source configuration.
Custom mode may carry other modifiers, and the probe cannot bind the date-selected modifiers of a real daily run: a daily's modifier set comes from a remote time server (`TimeServer.FetchDailyTime`), so what a daily on a given date actually carried is not knowable from a local install.

A seed cannot settle it either.
An earlier version of this manifest excused daily mode on the grounds that daily seeds are date-derived and this one is not.
That is false for this build.
`SeedHelper.GetRandomSeed` has exactly one caller — `StartRunLobby.BeginRunForAllPlayersIfAllReady`, the lobby path every mode shares — and no code path anywhere in the assembly derives a seed from a date.
Daily runs get their seed the same way every other run does.

What the probe can do is bound the space.
Every modifier the build offers is replayed as a daily against the reconstructed history and sorted by what it changes: an observed checkpoint, no canonical field beyond the recorded `run.game_mode`, or another canonical field while leaving every checkpoint intact.
Only that third case leaves the mode genuinely open, because only it is consistent with the recording and inconsistent with this replay.
For this VOD all seventeen change an observed checkpoint, so no single-modifier daily is consistent with the footage, and the gate accepts path-specific parity across the enumerated space.
It is not an identification: standard and custom-with-no-modifiers remain indistinguishable in the recording and agree in every canonical field except the recorded `run.game_mode`, which makes their full final-state digests differ.
Combinations of modifiers are not enumerated, and the report records that limit.

And one input is a field that no video can fill in:

| Field | Why it is identity |
|---|---|
| **unlocks** | The game generates a run's content against the player's unlock state. Nothing in a video shows it, so the manifest records a *requirement* - complete - rather than an observation, and the preflight checks the environment that is about to replay actually meets it. See [the progress problem](#the-progress-problem). |

## The act variant

`v0.111.0` ships **two acts at index 0**: `ACT.OVERGROWTH` and `ACT.UNDERDOCKS`.
`ACT.OVERGROWTH` is the default. The source video's map screen is titled
**Underdocks**.

Taking "the default act at each index" produced a run that:

- generated the **same Act 1 map**, node for node;
- generated a **completely different set of encounters**, so the first fight was a
  59-health Fuzzy Wurm Crawler instead of the 42-health enemy the video shows.

The map was identical because map topology is not generated from the run's shared
RNG stream at all — `StandardActMap.CreateFor` constructs a **fresh RNG from the run
seed**. So the map is a function of the seed and nothing else nearby, and it cannot
detect a substituted act.

That is the general shape of the hazard: a field can be identity, be invisible in
the most obvious cross-check, and still change every fight in the run. The act is
recorded as identity, and it is read from the map screen, which names it.

## What the content hash covers

The hash is a checksum over the model-id database. It covers content contributed by
mods that declare themselves gameplay-affecting. It does **not** cover:

- a mod that patches behaviour without adding content;
- a mod that declares itself non-gameplay — the shipping binary carries the warning
  *"There are mods included that do not affect gameplay. Hash may not include all
  IDs."*

So hash equality is a necessary gate and never, on its own, proof of parity. The
local machine has three mods installed and produces hash `1568834832`, the same
value the source video's overlay shows for a differently-modded environment. That
agreement is real evidence about content and no evidence at all about behaviour.

This project's headless host loads **no mods**, which is what makes its hash
meaningful: it is the base game's hash, so a match says the video's environment
agreed with the base game on the content that exists.

## The mod environment

The hash cannot serve as an environment fingerprint, so the environment gets a name
and a membership list instead. For the source video that is `navegreed-2026-08`:

| Mod | Role | Replay risk |
|---|---|---|
| Slay the Relics Exporter | Exports relic and HUD state for a stream overlay to read. | Lowest of the three. It reads state and writes it outward; its purpose is incompatible with changing anything. |
| BaseLib | The community modding framework most StS2 mods build on. | Infrastructure — hooks, config UI, menu surfaces. It adds no gameplay content of its own, but it *is* a patching framework, so "adds nothing" is a claim about this version rather than about the category. |
| Hindsight | Resumes a past run from a chosen floor in run history. | The one that can invalidate a reconstruction outright. Handled by a check rather than by argument — see below. |

The count is observed (the overlay reads `MODDED (3)` throughout). The identities are
**not** readable from the video, which names no mod anywhere; they came from a
separate investigation and the manifest marks them as an inference for that reason.
The validator refuses an environment that lists fewer mods than it reports loaded,
because an unidentified mod is exactly the gap the hash cannot close.

Naming them does not establish that they changed nothing.
What it buys is the ability to reason about each one — and, for the dangerous one, to write a check.
The three utilities are treated as non-gameplay tooling tied to the dated visible build, not as evidence that the VOD is ineligible.
A matching content hash and manifest-authored waiver values still cannot settle BaseLib's behavior by themselves.
The exact BaseLib v3.4.5 target probe uses Harmony on retail `PowerCmd.Apply` and detects its negative control.
It demonstrates that BaseLib clears `SkipNextDurationTick` for a player-applied custom debuff while the unpatched host leaves it set.
The publication gate therefore instruments every `PowerCmd.Apply` invocation in the exact reconstructed history and accepts path-specific parity only when none reaches that branch and an injected affected call proves the detector fires.

## The resumed-run problem

Hindsight resumes a past run from a chosen floor. A resumed run has the **same seed,
build, content hash and acts** as a fresh one. Every environment gate passes. The
replay runs cleanly. And it reconstructs a different run, because the recording does
not start where the history says it does.

No amount of replaying catches this: a resumed run replays perfectly well. It has to
be caught at ingestion, on the recording itself, so a video source must carry
`source.run_start` and it must show all four of:

- the run was not entered from the run history screen;
- no resume dialog appears;
- the first floor observed is 1;
- the first run-timer reading is within 15 seconds of zero.

The game's run timer starts at zero and the map is the first thing shown, so a
from-start recording reads a handful of seconds. A resumed run reads whatever the
original had accumulated. The selected video reads `00:04` on floor 1.

`./scripts/arbiter validate <manifest> --show-rejections` demonstrates the gate
refusing each way a provenance record can be wrong, and
`./scripts/arbiter gate <manifest>` folds it into the single publication verdict.

## The second reading

The end-of-run summary screen re-states the environment — the version overlay is
still rendered on it — around 2,038 seconds after the first reading. The validator
requires the two to agree on seed, build, build date and content hash.

Its value is not legibility; it is the same overlay. Its value is distance. A reading
that drifted cannot agree with itself across most of an hour of footage, and a
recording spliced from two different runs cannot agree at both ends.

The screen also states ascension independently of the in-run badge, and the maximum
health it shows (68) corroborates the opening blessing from the far end of the run:
the character starts at 80 and the blessing taken at 26 seconds costs 12.

What it does **not** show is recorded too, rather than left as an absence: the game
mode is not on this screen, which is why the mode remains an inference.

## The progress problem

The retail client builds a run's unlock state from the player's save progress
(`SaveManager.GenerateUnlockStateFromProgress`), and that state feeds content
generation: which ancients are shuffled, which relics enter the grab bag, which
events survive the epoch filter, and which encounters the act draws.

Measured, on one seed, changing nothing but the progress model:

| Progress model | Shared `UpFront` stream position after setup | First three weak encounters |
|---|---|---|
| everything unlocked | **412** | Fuzzy Wurm Crawler, Shrinker Beetle, Slimes |
| nothing unlocked | **370** | Nibbits, Slimes, Shrinker Beetle |

The map was byte-identical in both cases.

Nothing in a video shows a creator's unlock state, so the two halves of the problem
are answered differently and kept apart.

**What the source player had** is an inference, and stays one. The manifest records
it as `environment.unlocks` with `source: inferred` and the reasoning next to it: the
run on screen is Ascension 10 on Ironclad, through the Underdocks act variant, on a
typed seed. The act variant is the part that is measured rather than argued -
`ACT.UNDERDOCKS.IsUnlocked` returns false under `UnlockState.none` and true under
`UnlockState.all` - so the run being watched could not have been played without that
unlock. The rest is an assumption about an experienced creator, it does real work,
and it is recorded as a caveat on every verification report.

**What the replaying environment has** is not an assumption at all. `Preflight`
reads it and refuses a shortfall, category by category:

```
FAIL unlocks_cards       manifest=596   local=232
FAIL acts_unlocked       manifest=ACT.UNDERDOCKS, ...   local=locked: ACT.UNDERDOCKS
FAIL ascension_unlocked  manifest=ascension 10 available   local=profile ceiling 0 for CHARACTER.IRONCLAD
```

The required counts are read off the build - whatever `UnlockState.all` holds here -
so a game update that adds cards raises the bar without anyone editing a list. The
act check is asked of the act model rather than derived from an epoch name, which
matters because a locked act is the one shortfall a total cannot show: the run would
take the other variant shipped at the same index, and generate different content
behind an identical map.

Where the reading comes from is reported next to the verdict rather than assumed.
`--progress local-profile` reads the save progress of whichever profile the process has.
Inside the retail client that is the player's own, and inside the headless arbiter it is the empty sandbox profile, because the player's save is a read-only input the host never opens.
`preflight-live` runs in the headless host, whose user data is redirected to `build/sandbox` and whose `RunManager` is separate from the retail process.
Its default path therefore reads the empty sandbox profile, finds no active run, and refuses by design; it cannot report on a retail player's state.
Non-demo live evaluation accepts only `local-profile`, so an unlock model supplied by the host cannot masquerade as runtime player state.
The explicit `--demo-start-run` path constructs a synthetic run and permits synthetic progress models only for tests and demonstrations.
The Combat Trainer mod invokes `Preflight.EvaluateLiveHost` inside the retail process before stating whether the selected recording is eligible.
It reads the installed build, the mods the game discovered and the profile used for modded play, while the existing prerequisite and run-identity owners remain authoritative.
It adopts the client through `EngineHost.AdoptRunningGame`; `EngineHost.Start` remains the headless entry point that enables test mode and applies headless patches.
One of the three boundary tests drives the console refusal and verifies that the prepared game inputs and sandbox profile remain unchanged.
Another loads a duplicate game assembly and proves that state refuses before adoption.
The third parses the manifest and proves that the shipped host is non-gameplay, DLL-only and packless.
The host states eligibility and enters no fight; constructing or entering the captured combat remains S4.
`--progress all-unlocked`, the arbiter's ordinary replay default, is the state the headless host constructs the run with, and the report says so rather than calling it a reading of anybody.

The remediation is always the same and always the game's: unlock the rest by playing.
Nothing in this project writes to a save, a profile, an unlock or an installed build,
and there is no flag that would - a tool that edited a player's progress to make a
replay possible would have destroyed the thing the replay was evidence about.

## Verifying a seed without reading it

The seed reaches us as text somebody read off a low-contrast overlay. A reader that
agrees with itself is not a reader that is right: an earlier optical pass on this
video returned `SEXT47K77REK` with per-character confidence 1.0, and it was wrong in
two places.

The check that does not depend on reading: regenerate each candidate seed's Act 1
map through the game's own generator and compare its topology against the map the
video shows. Because map generation is seeded from the run seed alone, this is a
direct test of the seed.

On the four visually indistinguishable candidates:

| Candidate | Observed nodes reproduced |
|---|---|
| `SEXT47K77REK` | 12 of 61 |
| `SEXT47K77RFK` | 19 of 61 |
| `SFXT47K77REK` | 16 of 61 |
| **`SFXT47K77RFK`** | **61 of 61** |

Run it with `./scripts/arbiter verify-seed`.

Two things this check does **not** do, worth stating because it is tempting to lean
on it further than it goes:

- It does not validate the shared RNG stream position. The map comes from a separate
  generator, so a run can match the map and diverge on every encounter.
- It does not validate the act variant, for the same reason.

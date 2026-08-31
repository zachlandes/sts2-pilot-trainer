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

And one that is not a field, because it cannot be observed at all:

| Not a field | Why not |
|---|---|
| **player progress** | The game generates a run's content against the player's unlock state. Nothing in a video shows it. See [the progress problem](#the-progress-problem). |

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

Naming them does not establish that they changed nothing. What it buys is the
ability to reason about each one — and, for the dangerous one, to write a check.

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

Nothing in a video shows a creator's unlock state. This project therefore assumes
**everything unlocked**, records that assumption as a caveat on every verification
report, and treats agreement on generated content as the evidence for it. That is a
real assumption doing real work, and it is not independently established.

It is a mild assumption for an experienced player on a current build, and it would
be a poor one for a newer player's run. A future version should record the progress
model in the manifest rather than defaulting it.

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

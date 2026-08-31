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

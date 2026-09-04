# Finding recordings worth reconstructing

The manifest had one producer — an agent reading frames — and everything upstream of it was
a person asking for that by hand. This is the front of that path: which creators, which
recordings, and whether a recording can be reconstructed at all.

[The comparison direction](comparison-direction.md) owns what a replay result keeps,
[environment identity](environment-identity.md) owns what makes two runs the same run, and
[the proof-of-concept path](proof-of-concept-path.md) owns where this is going. Nothing here
overrides any of them.

## The one rule this path is built on

**Guess cheaply, verify with the engine.**

Nothing in `discover` establishes anything. A seed it recovers is a *candidate* for
`verify-seed`, which regenerates the Act 1 map through the real engine and compares topology.
A build it dates is a *candidate* for `preflight`, which compares against the installed game.
Both are settled by the engine and never here.

That split is what lets screening be cheap and wrong without costing anything. It runs on
metadata a platform hands over free — no download, no frame, no decode — and its job is to
make refusal cheap: a recording with no recoverable seed is not a harder job, it is not a job,
and the seed space is 34¹² ≈ 2.3 × 10¹⁸, far too large to search.

## Creator eligibility is a hard filter, and it runs first

A creator must supply the seed one of exactly two ways:

- **as text in the description** — strictly better, because no character recognition is
  involved and the whole class of confident misreads cannot occur; or
- **on screen, in the game's own version overlay, unoccluded.**

Reading the overlay has already produced a confidently wrong answer on real footage: six
independent median stacks agreed on `SEXT47K77REK` with per-character confidence 1.0 against
a true seed of `SFXT47K77RFK`. Readings that share a source and an engine are one reading
counted six times. `verify-seed` is what caught it.

An occlusion is not a reading difficulty. A webcam over the corner the overlay draws in makes
the value *absent*, and no resolution fixes that — so it is recorded on the profile and
refused before anything is fetched.

## What lives where

| Owner | Responsibility |
| --- | --- |
| `CreatorProfile` | One creator's habits as a bounded adapter: seed source, extraction patterns, occlusions. Data, so a creator changing their format is a file edit. |
| `CandidateScreening` | The decision, over free metadata. Produces candidates or refuses. |
| `PatchCalendar` | Dates a recording from its upload. Lives in `Sts2PilotTrainer.Replay`, not the CLI, because the catalogue's version gating reuses it. |
| `IngestionConfig` / `ingestion/creators.json` | The creator set and the release dates, as data. |
| `Revalidation` | Whether an existing reconstruction still reproduces on a different build. |
| `Commands.Discover` | The only place that reaches the network. Transfers no media. |

The creator set is small and finite on purpose. It is not a registry to be grown; a creator is
added by demonstrating that their recordings can be reconstructed, not by adding a row.

## Dating a recording

Assume the latest beta, **unless a release landed the day of the upload or the day before**,
in which case refuse to pick. A run is played and edited before it is published, so an upload
on patch day says almost nothing about which build the run was on. A build the creator states
in the description beats anything dated this way.

## Revalidation, and why the manifest is never edited

The game ships a minor version roughly every fortnight and most of them change something on
some run's path. `preflight` refuses a build mismatch before it tries anything, so a patch used
to retire the whole catalogue by declaration rather than by measurement.

A patch does not invalidate a recording. It invalidates the *claim* that the recording still
reproduces — and that claim is a separate artifact, a `ReproductionVerdict` keyed to
`(recording, build)`. The manifest says what the recording was made on, permanently.

Rewriting a manifest onto the build being tested was implemented and removed. `source.run_summary`
is a second reading of the environment from the far end of the recording and must agree with
`environment`; rebasing one leaves the other behind, which is indistinguishable from a recording
spliced out of two runs. The only way to silence that is to rewrite observations of the video.
A test pins the refusal.

**Passing every checkpoint is not enough.** A recording whose observed values all still agree
but whose combat-start boundary moved is retired for that build: the fight a player would enter
is not the recorded one.

## Known limits

**A recording with no end-of-run screen cannot become a manifest.** `ValidateRunSummary` requires
ten values, all observed with video timestamps, and a creator who publishes the seed as text but
renders no overlay — or whose upload stops before the summary — has none of them. Screening will
pass such a recording and the schema will refuse it. This is an open decision, deliberately not
settled here: `AGENTS.md` forbids weakening `source.run_summary` on the grounds that the replay
would catch it, and the substitute would have to be argued on its own terms.

**Naming every loaded mod is still required.** `ManifestValidator` refuses a manifest whose mod
list is shorter than its reported count, and refuses an entry with no replay-risk assessment.
Those are load-bearing today and they block a second video recording, because the mods are not
readable from a video. Relaxing them is a separate decision.

**Re-keying must regenerate the recorded fight.** `manifests/<id>.recorded-fight.json` is bound
to the manifest's run id, history hash and combat-start digest, so anything that re-keys a
recording to a new build has to regenerate it in the same step or leave the two disagreeing.

## Running it

```bash
./scripts/arbiter discover                                  # every configured creator
./scripts/arbiter discover --creator JapaneseExport --count 3
./scripts/arbiter discover --from <saved-metadata.json>     # offline, for tests and demos
```

It prints what it would reconstruct and stops. Ingesting is a separate, deliberate step: the
list is for a person to confirm, not a queue that runs itself.

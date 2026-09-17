# The recorder's release bar

The recorder ships when two numbers say it may, computed by `./scripts/arbiter` over every recording on hand.
Neither is a feeling about how much has been tested; each has a denominator somebody else produced - the recordings themselves, and the game assembly - and a build is held to both on every merge and again at release.

## The two numbers

**Parity**, per recording and per decision: a fresh replay of the manifest reproduces the `.journal.jsonl` the recorder wrote beside it, decision for decision - the same verb at the same place, the sampled state it began from and settled into, and the complete digest of each reading - and the first decision where it does not is named with the field.
`./scripts/arbiter parity` computes it; `AGENTS.md` owns what it holds and the two digests it does not.

**Coverage**, per decision point and over the corpus: for every point this build can offer - every verb, reward kind, card-reward alternative, shop shelf, rest option, event and prompt entry point, walked off the assembly by `DecisionSurface` - how many recordings exercise it, with every uncovered point named and every excused one carrying the reason a build is held to.
`./scripts/arbiter coverage` computes it; `DecisionExcusals` is the written excuse for what no committed recording reaches.

The release statement: parity is 100% over every recording on hand, and coverage lists no decision point reachable on this build that no recording exercises, or every such point is named with its excuse.

## The procedure

```bash
./scripts/arbiter parity   --corpus manifests --corpus <copy of the store's recordings/> --out build/evidence/release
./scripts/arbiter coverage --corpus manifests --corpus <copy of the store's recordings/> --out build/evidence/release
```

Both exit 0, or the release does not go.
The store is `user://Runmobile/<platform>/<account>/<profile>/recordings/`, copied by the person and named with `--corpus`; the command derives no path and writes nothing back.
The artifacts under `--out` - `parity.json` and `coverage.json` - are the release evidence, and the release note carries:

- the parity figure as printed, with every recording that holds nothing named by why - no journal, a journal in a schema this build does not read, a continuity the recorder marked broken;
- every point still excused, with its reason.
  An excusal that says no committed recording reaches the point is a placeholder, and at release it is not acceptable for a point a player can reach on this build: such a point is played, recorded and committed, or the release note says why not.
  An excusal that names `GeneratedCoverageTests` is held by a generated walk through the real recorder on every merge and stands.
  A point reached by a recording of the store corpus is printed as `excused and reached by this corpus`; that is progress and not a failure, and it is the list of excusals a committed recording would retire.

What no recording can reach is the same on every build: a card prompt and a net action are not projectable from this format and are counted neither way; `AGENTS.md` names why.

## The first measurement, 2026-09-17

Over `manifests/` and a copy of the author's store on v0.111.0, 17 recordings:

- **Parity: 1 of 15 native recordings compared, at parity.**
  Nine journals are in schemas older than v6, which this build reads only; two recordings have no journal (the two committed native ones, written before their journals were kept); three have a continuity the recorder marked broken; the one v6 journal replays decision for decision.
  Its opening reading's hidden state differs from the replay's, as it does in eleven of the twelve store journals that carry both digests: the retail client's first room changes hidden state between the recorder's opening reading and the first decision, which no decision is held to and which is reported beside the verdict.
- **Coverage: 131 points, 27 covered, 76 excused, 0 uncovered, 28 not projectable; 14 recordings credited, 3 unverified.**
  Of the 76 excused, 6 are held by `GeneratedCoverageTests`, 1 is a reward kind no singleplayer path constructs, and 69 say no committed recording reaches them - 52 of them events, 7 rest options, 4 reward kinds, 3 card-reward alternatives, 2 shop shelves, and the undo, the confirm, the bundle and relic screens, the Crystal Sphere and the act transition among the verbs.
  Eight of those are reached by a store recording and not yet by a committed one.

Two recorder findings from the measurement are not fixed by this standard and stand until the recorder changes: the opening reading above, and a `SkipRewards` in a v5 journal read after the map move that dismissed the loot screen had begun.

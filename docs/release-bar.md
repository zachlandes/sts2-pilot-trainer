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

Over `manifests/` and a copy of the author's whole store on v0.111.0 - every profile's recordings - 29 recordings, 27 of them native.

- **Parity: 1 of 27 native recordings at parity; 2 compared.**
  Twelve journals are in schemas older than v6, which this build reads only; two recordings have no journal (the two committed native ones, written before their journals were kept); eleven have a continuity the recorder marked broken; two carry a v6 journal.
  One of those two replays decision for decision, 8 of 8.
  The other, an ascension-6 run of 294 decisions, diverges at decision 18, a Brain Leech event option that costs health and offers a card: every sampled field agrees and the hidden state does not, because the recorder's after-reading was taken before the retail client rolled the card reward the option opened, and the next decision's own before-reading is the replay's after-state exactly.
  That is the shape `ReplayStep.EndsAFight` already excuses for a fight's loot and nothing excuses for an event's, and it is a recorder finding: the reading a reward-opening decision settles into is taken too early in the retail client.
  It is not fixed here and it fails the bar until the recorder reads that decision once the reward is rolled, or the standard is extended in writing.
  The same run also showed why the opening reading is reported whole and never counted: an ascension that starts the run damaged takes the health after the recorder's opening reading and before the replay's, so the opening's sampled fields now stand beside the verdict with its digest, and the first decision's own before-reading is where both hosts are held.
  The opening's hidden state differs in every store journal that carries both digests, as before.
- **Coverage: 138 points, 32 covered, 78 excused, 0 uncovered, 28 not projectable; 29 recordings, 18 credited, 11 unverified.**
  The seven points the denominator gained are the ancients an act rolls one of - Darv, Nonupeipe, Orobas, Pael, Tanx, Tezcatara, Vakuu - which `DecisionSurface.Events` had left out because the model database keeps them apart from its events; the measurement found Orobas in a store recording and in no walk, which is the check working, and the walk now produces them.
  Thirteen excused points are reached by a credited store recording and by no committed one - eight of the eleven generated ones, the confirm, the act transition and three events - and are counted covered here and printed as `excused and reached by this corpus`; those are the excusals a committed recording would retire first.
  Of the 78 still excused, 3 are held by `GeneratedCoverageTests`, 1 is a reward kind no singleplayer path constructs, 3 are screens the headless host has none for, and the undo, the reroll the driver refuses and the mend a singleplayer run is never offered are one each; the other 68 name a producer the fixture seed's route does not pass - 52 events and the 7 ancients a row per each would need a seed hunted for, the card-removal and special-card rewards, the sacrifice and the six rest options a relic or the Byrdonis Egg adds.

The measurement stands as the recorder's first release verdict: coverage passes and parity does not.
Two recorder findings from it are not fixed by this standard and stand until the recorder changes: the reward-opening event reading above, and a `SkipRewards` in a v5 journal read after the map move that dismissed the loot screen had begun.

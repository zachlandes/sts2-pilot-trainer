# The recorder's release bar

The recorder ships when two numbers say it may, computed by `./scripts/arbiter` over every recording on hand.
Neither is a feeling about how much has been tested; each has a denominator somebody else produced - the recordings themselves, and the game assembly - and a build is held to both on every merge and again at release.

## The two numbers

**Parity**, per recording and per decision: a fresh replay of the manifest reproduces the `.journal.jsonl` the recorder wrote beside it, decision for decision - the same verb at the same place, the sampled state it began from and settled into, and the complete digest of each reading - and the first decision where it does not is named with the field.
`./scripts/arbiter parity` computes it; `AGENTS.md` owns what it holds and the two digests it does not.

**Coverage**, per decision point and over the corpus: for every point this build can offer - every verb, reward kind, card-reward alternative, shop shelf, rest option, event, event option, seam at its timing class and prompt entry point, walked off the assembly by `DecisionSurface` - how many recordings exercise it, with every uncovered point named and every excused one carrying the reason a build is held to and the class the map admits for it.
`./scripts/arbiter coverage` computes it; `DecisionExcusals` is the written excuse for what no committed recording reaches.

The map is seam-centric, because every replay refusal this project has recorded was a seam and never a card effect: `DecisionSurface.ProducerMap` walks the game assembly from every hook a model overrides to the first seam it reaches and lists, per seam and timing class, the content that produces it, with what deals each relic (`DealtBy`) and which acts reach each event (`ReachableIn`); `scripts/producer-map.txt` is that map on this build.
A seam is reached by co-occurrence - a recording that met one of its producers and answered its decision - and is printed as such, never as covered, because the format does not record which producer opened the decision.
Every excusal carries a class, and the map says which classes it admits for each point: `no-producer-on-this-build`, `multiplayer-only`, `screen-without-headless-host`, `retail-only-timing`, `not-replayable` and `reached-by-the-win` are derived from the walks and the host's own tables, `generated` is held by a merge-gate row, and `not-on-the-route` is admissible only where nothing is derived.
An excusal in a class the map does not admit fails the number by name, the way a stale one does.

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
- every point still excused, with its class and its reason.
  An excusal that says no committed recording reaches the point (`not-on-the-route`) is a placeholder, and at release it is not acceptable for a point a player can reach on this build: such a point is played, recorded and committed, or the release note says why not.
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
  Fixed in the recorder since, below; the journal that measured it was written by the recorder that had the defect and still fails the bar on its own evidence.
  The same run also showed why the opening reading is reported whole and never counted: an ascension that starts the run damaged takes the health after the recorder's opening reading and before the replay's, so the opening's sampled fields now stand beside the verdict with its digest, and the first decision's own before-reading is where both hosts are held.
  The opening's hidden state differs in every store journal that carries both digests, as before.
- **Coverage: 138 points, 32 covered, 78 excused, 0 uncovered, 28 not projectable; 29 recordings, 18 credited, 11 unverified.**
  The seven points the denominator gained are the ancients an act rolls one of - Darv, Nonupeipe, Orobas, Pael, Tanx, Tezcatara, Vakuu - which `DecisionSurface.Events` had left out because the model database keeps them apart from its events; the measurement found Orobas in a store recording and in no walk, which is the check working, and the walk now produces them.
  Thirteen excused points are reached by a credited store recording and by no committed one - eight of the eleven generated ones, the confirm, the act transition and three events - and are counted covered here and printed as `excused and reached by this corpus`; those are the excusals a committed recording would retire first.
  Of the 78 still excused, 3 are held by `GeneratedCoverageTests`, 1 is a reward kind no singleplayer path constructs, 3 are screens the headless host has none for, and the undo, the reroll the driver refuses and the mend a singleplayer run is never offered are one each; the other 68 name a producer the fixture seed's route does not pass - 52 events and the 7 ancients a row per each would need a seed hunted for, the card-removal and special-card rewards, the sacrifice and the six rest options a relic or the Byrdonis Egg adds.

The measurement stands as the recorder's first release verdict: coverage passes and parity does not.

The denominator was widened on 2026-09-17 to 522 points over `manifests/`, when the map became seam-centric: the Architect as the 65th event, 305 event options (every option of every event, ancient, Neow and the Architect, the runtime-built keys of nine events derived the way their code builds them, none of which a committed recording names, because all three were written before the recorder wrote `option_key`) and 78 seams at their timing classes, six of which the committed corpus reaches by co-occurrence.
The figure over the store copy is re-measured at the next release procedure; the 78 excusals above kept their sentences and gained their classes, the undo's class corrected to `multiplayer-only` by the driver's own measurement, and every new point is excused by id - the Architect's as `reached-by-the-win`, the two seams answered only on stood-in screens as `screen-without-headless-host`, the rest as `not-on-the-route` until the walks of the staged path reach them.
Two recorder findings came out of it, the reward-opening event reading above and a `SkipRewards` in a v5 journal read after the map move that dismissed the loot screen had begun; both are fixed in the recorder, below, and neither by the standard.

## The two recorder findings, fixed 2026-09-17

Both were readings taken at the wrong instant relative to the engine's own work, and both showed only in the retail client, where that work spans real time.
Neither was fixed by widening what parity holds; each is a change to when the recorder reads, held by `RecorderTimingTests` against `TraceParity.Compare`, the oracle `parity` runs, with each test failing on the old reading and passing on the new.
[in-game-host.md](in-game-host.md) owns the mechanism of each.

- **An event option's reward, rolled after an animation.**
  `EventSynchronizer.ChooseLocalOption` returns nothing, so the recorder settled the decision on the action queue alone, and the queue is idle while an option waits on an animation.
  Brain Leech's RIP loses the health, awaits the player creature's hit animation and only then rolls the card reward it offers; the reading was taken during the animation, with the reward not yet rolled, and every sampled field agreed with the replay while the hidden state did not.
  The recorder now reads the option's own task on its way past and waits for it as it waits for any decision's work, except where that work has handed the run to the player - a rewards set on offer, or the Crystal Sphere's screen up - which is where the replay's own drain reads the same state.
  A store recording of this event made by the fixed recorder is what retires the divergence in the number; the ascension-6 journal was written by the recorder that had the defect and keeps its own evidence.
- **A `SkipRewards` declined by the map move.**
  A terminal loot screen is walked away from, and what declines the leftovers is `BeforeLeavingRoom` from inside the move, after `EnterMapPointInternal` has advanced the act floor and the coordinate to the node being walked to and before the total floor moves.
  The recorder read the skip there - `run.act_floor` and `run.map_coord` a node ahead of `run.total_floor`, on decisions 13 and 24 of `native-AA002GCMU2G8-20260915-085317`, and on every skip in every store journal of this build - and no replay holds that state, because the driver declines the set as its own decision before the move.
  The recorder now reads a set the move declines from the move's own before-reading on both sides of the decision, which is the state the driver declines it from; the skip changes nothing the projection reads.
  The ascension-6 journal shows the reading it would have written: at both of its skips (decisions 144 and 164) the move's before-digest is the digest the decision before the skip settled into, which is the state the replay's own skip begins from and leaves.
  On this build the v5 journal is one the standard does not read, so the finding is retired in the number only by recordings the fixed recorder makes.

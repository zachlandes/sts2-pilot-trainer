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
The figure over the store copy is re-measured at the next release procedure; the 78 excusals above kept their sentences and gained their classes, the undo's class corrected to `multiplayer-only` by the driver's own measurement, and every new point is excused by id - the Architect's as `reached-by-the-win`, the two seams answered only on stood-in screens as `screen-without-headless-host`, the three dolls keyed by a localized title as `not-replayable`, the rest as `not-on-the-route` until the walks of the staged path reach them.
Two recorder findings came out of it, the reward-opening event reading above and a `SkipRewards` in a v5 journal read after the map move that dismissed the loot screen had begun; both are fixed in the recorder, below, and neither by the standard.

## The ancient rows, 2026-09-17

The third stage of the coverage design put a row on the merge gate for every option of every ancient act 2 and act 3 open on - Orobas, Pael and Tezcatara in the Hive, Nonupeipe, Tanx and Vakuu in Glory, ten relics each - on a run whose acts list is that act alone (`GeneratedCoverageTests.AncientRows`).
The engine builds such a run the way it builds the won-run proof's one-act run, it opens on the act's ancient rather than on Neow, and the row's seed is hunted so the ancient is rolled and offers the relic; the reading is the same `SeedHunt` opening, tens of milliseconds per seed, and every row found its seed inside the first six candidates whose ancient offered the relic.
A generated-only acts list is admissible as coverage evidence on the footing of that proof, never listed or shared, and every excusal a row holds says so in its own words (`DecisionExcusals.GeneratedByAnActFirstRow`); no declared or fake starting inventory was built, because every producer here is reachable from state the game itself produced, which is the standing decision of the design review.
The rows retire the six ancients, their sixty options, the ancients' own option seam, the seams the relics they deal produce - Lord's Parasol's removal as the merchant is entered, Toasty Mittens' exhaust and Choices Paradox's grid at the turn's start, Sea Glass's grid on being obtained - and the sacrifice Pael's Wing adds to a card reward.
The three rest options an ancient's relic adds - the clone, the cook and the kindle - are the seams the rows fall short of: a rest site is two or three fights from the ancient, and the journey's mechanical line does not survive them at starter strength, on none of 400 hunted seeds per relic (Glory's first fight ended every Meat Cleaver walk; the Hive's second or third ended every Pael's Growth and Pumpkin Candle walk).
Each keeps an excusal that says so (`DecisionExcusals.BeyondTheLinesSurvival`), the row obtains the relic and retires its option and its other seams, and `GeneratedCoverageTests` holds the excusal to that sentence; a line that survives act 2 and 3 at starter strength is a claim about how to play that the journey refuses to make, so what retires the three is the retail soak or a survival seed of the fifth stage's kind.
Darv, the ancient every act's question mark can roll and no act opens on, keeps its excusal with that reason, as do its twelve relics, the card-removal reward whose ancient card only its Dusty Tome deals, and the special-card reward the Lantern Key or a thief's death deals: each is a question-mark hunt like an event's.
Three items the third stage was to retire are deferred from this change to the follow-up event-rows change, ratified 2026-09-17, and stay owed until it goes in: the card-removal reward (`card_removal`, the Forbidden Grimoire that only Darv's Dusty Tome deals), the special-card reward (`special_card`, the Lantern Key from a Hive question mark's event, or a thief's death), and the seventeen events only the Hive and Glory roll.
Reaching any of them is the question-mark walk - a route into a `?`, an event's pages answered, a seed hunted so the map rolls that event - which every event row of the fourth stage needs as well, so the rows for all of them go in on that mechanism; each keeps its `not-on-the-route` excusal until then, and from the day that change's rows land they are held on every merge as these rows are.
One recorder finding came out of the rows, fixed beside them and invisible to the committed corpus:

- **A purchase the engine makes for itself.**
  Lord's Parasol buys the whole shop as the merchant is entered, through `MerchantEntry.OnTryPurchaseWrapper` with `ignoreCost` set, which no button press sets, from inside the map move's own work.
  The recorder wrote each as a purchase the player made, ahead of the move that opened the shop - a move is written once the engine has settled at the other end - and the replay refused the first of them as a purchase in the monster room the move left; on another seed a potion entry the relic bought carried no model the recorder could name, and the recording stopped `unmapped` at a decision the player never made.
  A purchase the engine makes for itself is not a decision: `RunRecorder.ShopPurchased` reads the flag and records nothing for such a call, the replay's own move reproduces the purchases, and the removal the relic then opens is a card selection queued behind the move the way a hand-draw prompt's is.
  `ReplayRefusalRegressionTests` holds the walk into the shop to a recording with no purchase in it and to parity, and that row and test are a headless proof only.
  In the retail client the relic's purchases run on scene-tree timers, unawaited from `AfterRoomEntered`, so the map move into the shop can settle and be written before they and the relic's forced removal prompt have happened; the move's after-reading and the prompt's place in the journal are a known limitation owed to the retail-timing follow-up, and the recorder's settle rule is not changed by this.

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

## The producer rows, 2026-09-17

The second stage of the coverage design put a row on the merge gate for every relic that produces a seam and that a run of act 1 deals from game-produced state: the twelve Neow offers that produce one and the fifteen relics the run's own bag deals at a chest or the merchant's shelf, each on a seed hunted so the run deals that relic (`GeneratedCoverageTests.ProducerRows`, `SeedHunt`).
Massive Scroll, the thirteenth Neow producer, is admitted by its own `IsAllowed` only to a run with another player in it, which the recorder never records; `DecisionSurface.OfferedOnlyWithAnotherPlayer` reads that off the IL and its blessing is excused `multiplayer-only`.
The rows retire the excusals the design named - `SelectBundleFromScreen`, `ConfirmCardScreen`, the lift and the dig, the second card reward Prayer Wheel and White Star add, and twenty seams - and the twelve blessings they take; the sacrifice, the clone, the cook and the kindle are an ancient's relics and wait on the act-first walks of the third stage.
Two recorder findings came out of the rows, both fixed beside them and both invisible to the committed corpus:

- **A rewards set offered inside a purchase's, a rest's or a claim's own work.**
  Orrery and the Cauldron bought from a shop, a heal under Tiny Mailbox and a relic claimed off Neow's Bones offer a set from inside their own task, and that task finishes only once the set is answered by the decisions recorded after it.
  The recorder waited for it and refused every such purchase, rest and claim as unsettled, in its own words; the driver awaited the same task on the one thread the answering decisions arrive on.
  Both now read the decision as settled once the set is on offer, the way an event option's was already read (`RunRecorder.HandedToThePlayerDuring`, `RunDriver.SettleOrHandOver`), and the rows for those four relics hold each recording to a fresh replay through `TraceParity`.
- **A potion thrown at one of two enemies.**
  The fight observer wrote a drink without its target, and the driver refuses a targeted potion in a fight with two enemies alive unless the recording names one; the Orrery hunt's walk drank a Fire Potion that way and its replay was refused.
  The observer now writes `target_index` for a drink the way it does for a play (`PlayerFightObserver.AddTargetIndex`).

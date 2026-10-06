# Recorder/driver mechanisms behind the unreached inventory

This is the mechanism triage of the 2026-10-03 coverage review, next to [the denominator](decision-coverage.txt).
It maps that review’s points to existing recorder/driver owners and the tests that establish their behaviour, without changing any excusal or coverage credit.
The tests below have different scopes; a prompt-offer test, a refusal test and a recorded walk are not interchangeable evidence.

## What each claim means

- **Mechanism evidence** holds command ingress, offered identities, prompt counts, pauses and settle timing to the engine’s own commands.
  [EngineCommands and the decision ledger](decision-ledger.txt) account for ingress; recorder/driver tests exercise it.
  Explicit diagnostic setups may force a branch or inject state, but establish only the behaviour stated for that setup.
- **Representative recorded runs** cross materially different seams through the real recorder and are replayed through `TraceParity`.
  A row with another producer using the same mechanism is evidence for that mechanism, not a claim that this inventory point was reached.
- **Per-recording reproducibility** remains a check on the actual exported run: publication/preflight, replayed samples and digests, every declared boundary and discarded branch, and entry verification on a supported recipient environment.
  Nothing in this table substitutes for those checks or proves a second computer was tested.

The new diagnostics address hook-pause capture and event-resume reward claims.
The existing dummy event rows exercise the timeout path without a reward, so they cannot prove claiming the resumed potion or relic.
The new reward diagnostic forces only the encounter’s timeout reading in capture and replay, then lets the engine build and offer its own rewards.
The doll-title follow-up now captures stable localization identities and replays across changed text; those rows retain their review IDs with the denominator’s current generated excusal.
REROLL remains unsupported, with recorder-stop and driver-refusal tests rather than a route obligation.
The Relic Trader zero-relic PROCEED is only a later classification question.
No new route hunt or exhaustive scenario obligation is added.

## Existing owners

`EngineCommands.VerifyLedger` holds command ingress to the decision ledger; `RunRecorder` observes those members and the driver calls the engine’s own commands.
For card prompts with a choice context, `CardPrompts` reads at the action pause; other prompts read at the entry point, and `CardPromptOffers` derives the offered list.
`RunRecorder.HoldCardPromptAnswers` writes its picks/count and `ManifestCardSelector` consumes the contiguous `CardScreenAnswers`.
`ScreenStandIns` answers only the headless bundle/relic/minigame surfaces, and the retail host remains a separate presentation/timing claim.
`EventOptionWork`, `RunRecorder.HandedToThePlayerDuring` and `RunDriver.SettleOrHandOver` own task completion versus a hand-over inside work.
`RunDriver.Approach` waits for that work before the replay samples, and `ResumeTheEventTheFightWasFoughtIn` performs the engine’s event resume before the next decision is read.
`LootRewards` and the recorded `reward_index` identify a claim in the engine’s offered set, including two rewards of one kind.
`RunSession.MayBeRecorded` owns the singleplayer exclusion; `DecisionSurface` derives locked/no-producer classifications from the assembly.
These are mappings to those owners, not additions to the replay contract.

## Inventory mapping

Each row retains its review ID and the denominator’s kind, identity and excusal class.
The evidence keys link to a test method and its scope below; shared keys mean a shared mechanism, not shared coverage credit.
`CoverageTests.TheMechanismTriageMatchesTheDenominatorAndResolvesEveryEvidenceKey` checks this table even without the game, against the seven selected excusal classes and the evidence keys defined below; it does not check that a named test exists or runs.
A changed point, class or test name requires reviewing this mapping in the same change, rather than generating a new proof claim from a count.

| Review | Kind | Identity | Excusal class | Recorder/driver mechanism | Proving tests | Limit |
| --- | --- | --- | --- | --- | --- | --- |
| U-01 | verb | `UndoEndTurn` | multiplayer-only | session gate; singleplayer undo refusal | [session], [undo] | Outside singleplayer recording |
| U-02 | verb | `SelectRelicFromScreen` | screen-without-headless-host | queued relic prompt id/index; unconsumed-answer refusal | [relic] | No content producer; driver diagnostic only |
| U-03 | verb | `RevealCrystalSphereCell` | screen-without-headless-host | minigame cell ingress; tool and reward settle | [crystal], [crystalroute] | Headless screen stand-in; no retail capture asserted |
| U-04 | reward-kind | `LinkedRewardSet` | no-producer-on-this-build | reward-kind vocabulary exclusion | [shape] | No singleplayer constructor; no consumption proof |
| U-05 | card-reward-alternative | `REROLL` | not-replayable | recorder stop; driver refusal of unsupported follow-up | [reroll], [rerollcapture] | Refusal proved; reroll/follow-up replay support not claimed |
| U-06 | rest-option | `COOK` | not-on-the-route | rest option work; exact-N deck removal prompt | [rest], [event], [exact], [hook] | Composition of rest/pick mechanisms; Cook effects/acquisition not proved |
| U-07 | rest-option | `MEND` | multiplayer-only | session gate; another-player rest target | [session], [locked] | Outside singleplayer recording |
| U-08 | event | `EVENT.FAKE_MERCHANT` | not-projectable | shop purchase ingress in an optionless event | [fake] | Already recorded; missing credit is projection, not replay |
| U-09 | event | `EVENT.GRAVE_OF_THE_FORGOTTEN` | not-on-the-route | event option work; deck enchantment prompt | [event], [enchant], [eventwork] | Same mechanisms; Grave eligibility/acquisition not proved |
| U-10 | event | `EVENT.RELIC_TRADER` | not-on-the-route | event option work; relic reward hand-over | [event], [eventwork], [handed], [claim] | Trader inventory eligibility/trade effects not proved |
| U-11 | event | `EVENT.WAR_HISTORIAN_REPY` | not-on-the-route | event option work; potion/relic reward sets | [event], [eventwork], [gold], [claim] | Lantern Key replacement/third-act route not proved |
| U-12 | event-option | `EVENT.DOLL_ROOM relics.BING_BONG.title` | generated | stable option identity across localized titles | [doll] | Existing route with synthetic localization; no recipient-environment publication proof |
| U-13 | event-option | `EVENT.DOLL_ROOM relics.DAUGHTER_OF_THE_WIND.title` | generated | stable option identity across localized titles | [doll] | Existing route with synthetic localization; no recipient-environment publication proof |
| U-14 | event-option | `EVENT.DOLL_ROOM relics.MR_STRUGGLES.title` | generated | stable option identity across localized titles | [doll] | Existing route with synthetic localization; no recipient-environment publication proof |
| U-15 | event-option | `EVENT.ENDLESS_CONVEYOR ENDLESS_CONVEYOR.pages.ALL.options.LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-16 | event-option | `EVENT.GRAVE_OF_THE_FORGOTTEN GRAVE_OF_THE_FORGOTTEN.pages.INITIAL.options.ACCEPT` | not-on-the-route | event option work; relic acquisition | [event], [claim] | Grave producer not reached |
| U-17 | event-option | `EVENT.GRAVE_OF_THE_FORGOTTEN GRAVE_OF_THE_FORGOTTEN.pages.INITIAL.options.CONFRONT` | not-on-the-route | event option work; deck enchantment pick | [event], [enchant], [exact] | Grave producer not reached |
| U-18 | event-option | `EVENT.GRAVE_OF_THE_FORGOTTEN GRAVE_OF_THE_FORGOTTEN.pages.INITIAL.options.CONFRONT_LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-19 | event-option | `EVENT.LUMINOUS_CHOIR LUMINOUS_CHOIR.pages.INITIAL.options.OFFER_TRIBUTE_LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-20 | event-option | `EVENT.NEOW RELIC.MASSIVE_SCROLL` | multiplayer-only | session gate; multiplayer opening offer | [session], [locked] | Outside singleplayer recording |
| U-21 | event-option | `EVENT.RANWID_THE_ELDER RANWID_THE_ELDER.pages.INITIAL.options.POTION_LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-22 | event-option | `EVENT.RANWID_THE_ELDER RANWID_THE_ELDER.pages.INITIAL.options.RELIC_LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-23 | event-option | `EVENT.RELIC_TRADER PROCEED` | not-on-the-route | event option with no prompt or reward | [event] | Zero-relic fallback classification review deferred |
| U-24 | event-option | `EVENT.RELIC_TRADER RELIC_TRADER.pages.INITIAL.options.BOTTOM` | not-on-the-route | event option work; relic exchange/reward | [event], [eventwork], [handed], [claim] | Trader trade effects/route not proved |
| U-25 | event-option | `EVENT.RELIC_TRADER RELIC_TRADER.pages.INITIAL.options.MIDDLE` | not-on-the-route | event option work; relic exchange/reward | [event], [eventwork], [handed], [claim] | Trader trade effects/route not proved |
| U-26 | event-option | `EVENT.RELIC_TRADER RELIC_TRADER.pages.INITIAL.options.TOP` | not-on-the-route | event option work; relic exchange/reward | [event], [eventwork], [handed], [claim] | Trader trade effects/route not proved |
| U-27 | event-option | `EVENT.SELF_HELP_BOOK SELF_HELP_BOOK.pages.INITIAL.options.NO_OPTIONS` | not-on-the-route | event option completes with no selection | [event] | Empty-deck eligibility/route not proved |
| U-28 | event-option | `EVENT.SELF_HELP_BOOK SELF_HELP_BOOK.pages.INITIAL.options.READ_ENTIRE_BOOK_LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-29 | event-option | `EVENT.SELF_HELP_BOOK SELF_HELP_BOOK.pages.INITIAL.options.READ_PASSAGE_LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-30 | event-option | `EVENT.SELF_HELP_BOOK SELF_HELP_BOOK.pages.INITIAL.options.READ_THE_BACK_LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-31 | event-option | `EVENT.STONE_OF_ALL_TIME STONE_OF_ALL_TIME.pages.INITIAL.options.LIFT_LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-32 | event-option | `EVENT.STONE_OF_ALL_TIME STONE_OF_ALL_TIME.pages.INITIAL.options.PUSH_LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-33 | event-option | `EVENT.SYMBIOTE SYMBIOTE.pages.INITIAL.options.APPROACH_LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-34 | event-option | `EVENT.TEA_MASTER TEA_MASTER.pages.INITIAL.options.BONE_TEA_LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-35 | event-option | `EVENT.TEA_MASTER TEA_MASTER.pages.INITIAL.options.EMBER_TEA_LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-36 | event-option | `EVENT.WAR_HISTORIAN_REPY WAR_HISTORIAN_REPY.pages.INITIAL.options.UNLOCK_CAGE` | not-on-the-route | event option work; relic exchange/acquisition | [event], [claim] | Lantern Key third-act route not proved |
| U-37 | event-option | `EVENT.WAR_HISTORIAN_REPY WAR_HISTORIAN_REPY.pages.INITIAL.options.UNLOCK_CHEST` | not-on-the-route | event option work; multiple reward claims | [event], [eventwork], [gold], [claim] | Repy producer/route not reached |
| U-38 | event-option | `EVENT.WATERLOGGED_SCRIPTORIUM WATERLOGGED_SCRIPTORIUM.pages.INITIAL.options.PRICKLY_SPONGE_LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-39 | event-option | `EVENT.WATERLOGGED_SCRIPTORIUM WATERLOGGED_SCRIPTORIUM.pages.INITIAL.options.TENTACLE_QUILL_LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-40 | event-option | `EVENT.WELCOME_TO_WONGOS WELCOME_TO_WONGOS.pages.INITIAL.options.BARGAIN_BIN_LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-41 | event-option | `EVENT.WELCOME_TO_WONGOS WELCOME_TO_WONGOS.pages.INITIAL.options.FEATURED_ITEM_LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-42 | event-option | `EVENT.WELCOME_TO_WONGOS WELCOME_TO_WONGOS.pages.INITIAL.options.MYSTERY_BOX_LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-43 | event-option | `EVENT.WOOD_CARVINGS WOOD_CARVINGS.pages.INITIAL.options.SNAKE_LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-44 | event-option | `EVENT.ZEN_WEAVER ZEN_WEAVER.pages.INITIAL.options.LOCKED` | not-choosable | locked/no-work option excluded before ingress | [locked] | Structural exclusion, not a recorded decision |
| U-45 | seam | `card-prompt:CardSelectCmd.FromChooseACardScreen(context, cards, player, canSkip) @ MonsterModel.GenerateMoveStateMachine` | not-on-the-route | choose-a-card range prompt; hook pause/answer | [chooseoffer], [choose], [hook] | Monster move-state-machine producer not directly tested |
| U-46 | seam | `card-prompt:CardSelectCmd.FromCombatPile(context, pile, player, prefs) @ AbstractModel.AfterShuffle` | not-on-the-route | draw-pile prompt after shuffle; delayed hook pause | [draw], [hook] | Diagnostic forces this hook; Stratagem acquisition not proved |
| U-47 | seam | `card-prompt:CardSelectCmd.FromCombatPile(context, pile, player, prefs) @ AbstractModel.BeforeHandDraw` | not-on-the-route | exact-three draw-pile prompt before hand draw | [draw], [hook], [exact] | Diagnostic forces this hook; Foregone Conclusion acquisition not proved |
| U-48 | seam | `card-prompt:CardSelectCmd.FromCombatPile(context, pile, player, prefs, filter) @ CardModel.OnPlay` | not-on-the-route | filtered draw-pile prompt during card play | [draw], [pile] | Draw offer plus played-prompt capture; individual producers not proved |
| U-49 | seam | `card-prompt:CardSelectCmd.FromSimpleGridForRewards(context, cards, player, prefs) @ ModifierModel.GenerateNeowOption` | not-on-the-route | reward-grid prompt; exact-N answer list | [grid], [hook], [exact] | List derivation plus multi-pick/count mechanism; modifier route/ten-card deck replacement not proved |
| U-50 | seam | `rest-option:COOK @ AbstractModel.TryModifyRestSiteOptions` | not-on-the-route | rest option work; exact-N deck removal prompt | [rest], [event], [exact], [hook] | Same Cook mechanism as U-06; producer co-occurrence is not coverage |
| U-51 | seam | `reward-kind:card @ ModifierModel.GenerateNeowOption` | not-on-the-route | opening work hands over repeated card rewards | [eventwork], [card], [handed] | Reward/hand-over mechanisms; Draft modifier repetition not proved |
| U-52 | seam | `reward-kind:gold @ AbstractModel.AfterCombatEnd` | not-on-the-route | fight-end reward generation; gold claim | [gold], [eventwork] | AfterCombatEnd amount/generation belongs to engine; producer not met |
| U-53 | seam | `reward-kind:gold @ AbstractModel.TryModifyRewardsLate` | not-on-the-route | late reward modification; gold claim | [gold] | Modifier arithmetic/eligibility not tested by recorder proof |
| U-54 | seam | `reward-kind:potion @ EventModel.Resume` | not-on-the-route | event resume offers potion; claim before-reading | [resume], [resumereward] | Forced reward branch; no natural dummy victory proof |
| U-55 | seam | `reward-kind:relic @ ?` | not-projectable | optionless event reward projection; purchases | [fake] | No event-option credit; already recorded purchases |
| U-56 | seam | `reward-kind:relic @ AbstractModel.TryModifyRewardsLate` | not-on-the-route | late reward substitution; relic claim | [claim], [gold] | Engine decides reward kind; Vintage producer not met |
| U-57 | seam | `reward-kind:relic @ EventModel.Resume` | not-on-the-route | event resume offers relic; claim before-reading | [resume], [resumereward], [claim] | Forced reward branch; no natural dummy victory proof |
| U-58 | seam | `screen:NCrystalSphereScreen.ShowScreen @ EventModel.GenerateInitialOptions` | screen-without-headless-host | event work hands over minigame; cell ingress | [crystal], [crystalroute], [eventwork] | Screen is stood in for headlessly; not retail rendering proof |
| U-59 | seam | `shop-kind:relic @ EventModel.BeforeEventStarted` | not-projectable | optionless event shop purchase; shelf index/id | [fake] | Already recorded purchases; co-occurrence is not projectable |

## Evidence scopes

- [session] — `LiveRunSessionTests.AMultiplayerRunIsRefusedAtAttachForBeingMultiplayer`.
  Exclusion: a multiplayer session is refused at attach; this does not exercise multiplayer decisions.
- [undo] — `ResidueVerbTests.AnEndedTurnGoesThroughTheActionAndTheUndoIsRefusedOnThisBuild`.
  Driver diagnostic: the singleplayer undo is refused after ending the turn through its retail action.
- [relic] — `ResidueVerbTests.ARelicScreenIsAnsweredFromTheManifestAndNoActionOnThisBuildOpensOne`.
  Driver diagnostic: explicitly ask a relic prompt, consume the recorded id and position, and refuse absent/unconsumed answers; no producer or retail capture is proved.
- [crystal] — `ResidueVerbTests.ACrystalSphereIsRevealedFromTheManifestThroughTheStoodInScreen`.
  Driver diagnostic: a constructed minigame checks tools, cells, completion and invalid reveals through the real minigame, without rendering its screen.
- [crystalroute] — `GeneratedCoverageTests.ASecondActEventRowSurvivesTheFirstActOpensTheEventAndReplaysToParity`.
  Recorded walk: the Crystal Sphere PAYMENT_PLAN and UNCOVER_FUTURE rows answer the minigame in the headless host and replay the journal; this is not retail screen evidence.
- [shape] — `DecisionSurfaceTests.TheRewardKindsAreTheSixTheFormatNamesAndTheOneItDoesNot`.
  Structural exclusion: the assembly walk retains LinkedRewardSet by name, outside the format kinds; it does not prove consuming a linked set.
- [reroll] — `CardRewardAlternativeTests.EveryAlternativeTheBuildProducesIsAnsweredAsTheEngineMeansIt`.
  Driver diagnostic/refusal: the engine’s REROLL alternative is refused by name; support is not claimed.
- [rerollcapture] — `RunRecorderStopTests.ARerollStopsAtTheNamedAnswerWithoutBreakingTheWatch`.
  Recorder diagnostic: the engine offers REROLL and its answer stops capture once with integrity unmapped, continuity unchanged and the option identity in the journal.
  No reroll/follow-up replay support is proved.
- [doll] — `DollTitleContractTests.ALocalizedDollChoiceCapturesItsIdentityAndReplaysToParity`.
  Existing second-act recorded route with synthetic localized titles during capture only: every doll choice records its stable table/entry key and offered position, then replays without that localization to TraceParity.
  No inventory or event is injected; no recipient-environment publication proof is claimed.
- [rest] — `GeneratedCoverageTests.AGeneratedWalkReachesThePointRecordsItAndReplaysToParity`.
  Recorded walk: the rest HEAL row proves ChooseRestSiteOption capture, work settling and replay; it does not prove Cook acquisition or its effects.
- [exact] — `RewardAndScreenVerbTests.AScreenThatWantsMoreCardsThanTheManifestSuppliesIsRefused`.
  Driver diagnostic/refusal: an exact-N prompt requires every recorded pick; a missing pick is refused, not filled in.
- [event] — `GeneratedCoverageTests.AnEventRowOpensTheEventChoosesTheOptionAndReplaysToParity`.
  Recorded walks: event id, option key and index, page completion and the engine effects replay to TraceParity; the mapped unreached producer is not thereby reached.
- [enchant] — `CardPromptOfferTests.FromDeckForEnchantmentOffersTheGivenCardsInDeckOrder`.
  Prompt diagnostic: the recorder offer derivation matches the engine selector list for deck enchantment; this alone is not capture-to-replay proof.
- [eventwork] — `RecorderTimingTests.AnEventOptionThatRollsItsRewardAfterAnAnimationIsReadOnceTheRewardIsOnOffer`.
  Recorded route with a held-animation diagnostic: the recorder waits for the option task/reward hand-over, not merely an idle queue, and matches fresh replay.
- [handed] — `ReplayRefusalRegressionTests.TheOptionAfterAHandedOverClaimIsReadOnceTheWorkHasFinished`.
  Diagnostic over a recorded route: delayed option work after its reward claim is awaited before the next before-reading; both samples and digests are held to TraceParity.
- [fake] — `GeneratedCoverageTests.TheFakeMerchantSellsARelicAndReplaysToParity`.
  Recorded walk: Fake Merchant shelf purchases capture and replay to parity despite the event/co-occurrence projection limit; this is not entry at a noncombat floor.
- [locked] — `DecisionSurfaceTests.TheMapDerivesAClassWhereItReadsOneAndAdmitsAPlaceholderOnlyElsewhere`.
  Structural exclusion: the IL-derived no-work locked constructions are not playable choices; no runtime button-press test is claimed.
- [draw] — `CardPromptOfferTests.FromCombatPileOverTheDrawPileOffersItSortedByRarityThenId`.
  Prompt diagnostic: the filtered draw-pile offer matches the engine list and ordering.
- [pile] — `CardPromptCaptureTests.AHeadbuttPromptIsRecordedWhereTheReplayConsumesIt`.
  Staged diagnostic: a played card pauses for a combat-pile prompt, records the offered position/id, and replays the answer to the same complete digest.
- [choose] — `CardPromptCaptureTests.AChooseACardPromptTakenIsRecordedAsItsPickAndAConfirmationOfOne`.
  Staged diagnostic: choose-a-card capture emits a pick plus count, which replay consumes to the same digest; a monster producer is not exercised.
- [chooseoffer] — `CardPromptOfferTests.FromChooseACardScreenOffersTheCardsAsGiven`.
  Prompt diagnostic: choose-a-card offer identity/order matches the engine list.
- [hook] — `CardPromptCaptureTests.ADiagnosticTurnHookPromptIsCapturedAfterItsPauseAndReplayed`.
  New staged diagnostic: injected Stratagem/Foregone Conclusion and uniquely identified draw cards force an AfterShuffle or BeforeHandDraw prompt during EndTurn; the delayed pause reads the current pile, records one or three picks, and fresh staged replay reaches the same before/after digests.
  No natural acquisition, route or rendering is proved.
- [grid] — `CardPromptOfferTests.FromSimpleGridForRewardsOffersTheCreatedCardsInTheOrderGiven`.
  Prompt diagnostic: the reward-grid offer follows the engine-created list; Sealed Deck and its ten-card replacement are not exercised.
- [card] — `GeneratedCoverageTests.AGeneratedWalkReachesThePointRecordsItAndReplaysToParity`.
  Recorded walks: the decline-card row skips then takes the reward, and ordinary walks capture TakeCard through the reward seam; Draft repetition is not exercised.
- [gold] — `ReplayRefusalRegressionTests.TwoGoldRewardsOnOneLootScreenReplay`.
  Recorded walk: two gold rewards are distinguished by reward_index and replay to parity; the producer hook need not become a second claim command.
- [claim] — `GeneratedCoverageTests.AProducerRowDealsTheRelicReachesItsSeamsAndReplaysToParity`.
  Recorded walk: the Neow’s Bones producer row exercises relic claims and pickup hand-overs; Vintage reward substitution itself is engine logic, not reimplemented here.
- [resume] — `ReplayRefusalRegressionTests.TheFirstDecisionAfterAFightInsideAnEventReadsInTheResumedEvent`.
  Recorded act-first walk: an event fight resumes before the next decision’s before-reading, held to TraceParity; the dummy times out here and awards no potion/relic.
- [resumereward] — `ReplayRefusalRegressionTests.ADiagnosticResumedEventRewardIsClaimedAndReplaysToParity`.
  New diagnostic: force BattlewornDummyEventEncounter.RanOutOfTime=false in capture and replay on the existing Glory-alone row; the engine’s Resume offers its potion/relic, ClaimReward is recorded back in the event and matches TraceParity including its before-digest.
  Beating the dummy, ordinary-route and unpatched publication proof are not claimed.

[session]: ../tests/Sts2PilotTrainer.Mod.Tests/LiveRunSessionTests.cs "LiveRunSessionTests.AMultiplayerRunIsRefusedAtAttachForBeingMultiplayer"
[undo]: ../tests/Sts2PilotTrainer.Mod.Tests/ResidueVerbTests.cs "ResidueVerbTests.AnEndedTurnGoesThroughTheActionAndTheUndoIsRefusedOnThisBuild"
[relic]: ../tests/Sts2PilotTrainer.Mod.Tests/ResidueVerbTests.cs "ResidueVerbTests.ARelicScreenIsAnsweredFromTheManifestAndNoActionOnThisBuildOpensOne"
[crystal]: ../tests/Sts2PilotTrainer.Mod.Tests/ResidueVerbTests.cs "ResidueVerbTests.ACrystalSphereIsRevealedFromTheManifestThroughTheStoodInScreen"
[crystalroute]: ../tests/Sts2PilotTrainer.Mod.Tests/GeneratedCoverageTests.cs "GeneratedCoverageTests.ASecondActEventRowSurvivesTheFirstActOpensTheEventAndReplaysToParity"
[shape]: ../tests/Sts2PilotTrainer.Mod.Tests/DecisionSurfaceTests.cs "DecisionSurfaceTests.TheRewardKindsAreTheSixTheFormatNamesAndTheOneItDoesNot"
[reroll]: ../tests/Sts2PilotTrainer.Mod.Tests/CardRewardAlternativeTests.cs "CardRewardAlternativeTests.EveryAlternativeTheBuildProducesIsAnsweredAsTheEngineMeansIt"
[rerollcapture]: ../tests/Sts2PilotTrainer.Mod.Tests/RunRecorderStopTests.cs "RunRecorderStopTests.ARerollStopsAtTheNamedAnswerWithoutBreakingTheWatch"
[doll]: ../tests/Sts2PilotTrainer.Mod.Tests/DollTitleContractTests.cs "DollTitleContractTests.ALocalizedDollChoiceCapturesItsIdentityAndReplaysToParity"
[rest]: ../tests/Sts2PilotTrainer.Mod.Tests/GeneratedCoverageTests.cs "GeneratedCoverageTests.AGeneratedWalkReachesThePointRecordsItAndReplaysToParity"
[exact]: ../tests/Sts2PilotTrainer.Arbiter.Tests/RewardAndScreenVerbTests.cs "RewardAndScreenVerbTests.AScreenThatWantsMoreCardsThanTheManifestSuppliesIsRefused"
[event]: ../tests/Sts2PilotTrainer.Mod.Tests/GeneratedCoverageTests.cs "GeneratedCoverageTests.AnEventRowOpensTheEventChoosesTheOptionAndReplaysToParity"
[enchant]: ../tests/Sts2PilotTrainer.Mod.Tests/CardPromptOfferTests.cs "CardPromptOfferTests.FromDeckForEnchantmentOffersTheGivenCardsInDeckOrder"
[eventwork]: ../tests/Sts2PilotTrainer.Mod.Tests/RecorderTimingTests.cs "RecorderTimingTests.AnEventOptionThatRollsItsRewardAfterAnAnimationIsReadOnceTheRewardIsOnOffer"
[handed]: ../tests/Sts2PilotTrainer.Mod.Tests/ReplayRefusalRegressionTests.cs "ReplayRefusalRegressionTests.TheOptionAfterAHandedOverClaimIsReadOnceTheWorkHasFinished"
[fake]: ../tests/Sts2PilotTrainer.Mod.Tests/GeneratedCoverageTests.cs "GeneratedCoverageTests.TheFakeMerchantSellsARelicAndReplaysToParity"
[locked]: ../tests/Sts2PilotTrainer.Mod.Tests/DecisionSurfaceTests.cs "DecisionSurfaceTests.TheMapDerivesAClassWhereItReadsOneAndAdmitsAPlaceholderOnlyElsewhere"
[draw]: ../tests/Sts2PilotTrainer.Mod.Tests/CardPromptOfferTests.cs "CardPromptOfferTests.FromCombatPileOverTheDrawPileOffersItSortedByRarityThenId"
[pile]: ../tests/Sts2PilotTrainer.Mod.Tests/CardPromptCaptureTests.cs "CardPromptCaptureTests.AHeadbuttPromptIsRecordedWhereTheReplayConsumesIt"
[choose]: ../tests/Sts2PilotTrainer.Mod.Tests/CardPromptCaptureTests.cs "CardPromptCaptureTests.AChooseACardPromptTakenIsRecordedAsItsPickAndAConfirmationOfOne"
[chooseoffer]: ../tests/Sts2PilotTrainer.Mod.Tests/CardPromptOfferTests.cs "CardPromptOfferTests.FromChooseACardScreenOffersTheCardsAsGiven"
[hook]: ../tests/Sts2PilotTrainer.Mod.Tests/CardPromptCaptureTests.cs "CardPromptCaptureTests.ADiagnosticTurnHookPromptIsCapturedAfterItsPauseAndReplayed"
[grid]: ../tests/Sts2PilotTrainer.Mod.Tests/CardPromptOfferTests.cs "CardPromptOfferTests.FromSimpleGridForRewardsOffersTheCreatedCardsInTheOrderGiven"
[card]: ../tests/Sts2PilotTrainer.Mod.Tests/GeneratedCoverageTests.cs "GeneratedCoverageTests.AGeneratedWalkReachesThePointRecordsItAndReplaysToParity"
[gold]: ../tests/Sts2PilotTrainer.Mod.Tests/ReplayRefusalRegressionTests.cs "ReplayRefusalRegressionTests.TwoGoldRewardsOnOneLootScreenReplay"
[claim]: ../tests/Sts2PilotTrainer.Mod.Tests/GeneratedCoverageTests.cs "GeneratedCoverageTests.AProducerRowDealsTheRelicReachesItsSeamsAndReplaysToParity"
[resume]: ../tests/Sts2PilotTrainer.Mod.Tests/ReplayRefusalRegressionTests.cs "ReplayRefusalRegressionTests.TheFirstDecisionAfterAFightInsideAnEventReadsInTheResumedEvent"
[resumereward]: ../tests/Sts2PilotTrainer.Mod.Tests/ReplayRefusalRegressionTests.cs "ReplayRefusalRegressionTests.ADiagnosticResumedEventRewardIsClaimedAndReplaysToParity"

<!-- Generated from the code. Do not edit; see the note below. -->

# The manifest format, verb by verb

Internal engineering reference.
It is not part of what this project publishes and nothing a player sees is written from it.

Every word below is derived from the two declarations that decide the answers,
so it cannot describe a format the code does not enforce:

* `src/Sts2PilotTrainer.Replay/ManifestValidator.cs` decides which arguments a recorded decision may carry.
* `src/Sts2PilotTrainer.Engine/EngineCommands.cs` decides which of the game's own commands the arbiter issues for it.

It answers one question per verb: which arguments that decision may carry, and which
command runs it.
Rules that relate two arguments to each other, and everything in a manifest outside
`actions[]`, are the validator's and are not restated here.

Regenerate it with `./scripts/format-reference.sh`, and commit the result in the
change that moved either declaration.
CI regenerates and compares, so a stale copy fails the build rather than misleading a reader.
Editing this file by hand only moves that failure to somebody else's branch.

## Every verb this build replays

| Verb | Required arguments | Engine command |
|---|---|---|
| [`ChooseNeowBlessing`](#chooseneowblessing) | `option_index` | `EventSynchronizer.ChooseLocalOption` |
| [`MapMove`](#mapmove) | `act`, `row`, `column` | `RunManager.EnterMapCoord` |
| [`PlayCard`](#playcard) | `card_id`, `hand_index` | `PlayCardAction..ctor` |
| [`EndTurn`](#endturn) | none | `PlayerCmd.EndTurn` |
| [`ChooseEventOption`](#chooseeventoption) | `event_id`, `option_index` | `EventSynchronizer.ChooseLocalOption` |
| [`ClaimReward`](#claimreward) | `reward_type` | `RewardsSetSynchronizer.SelectLocalReward` |
| [`TakeCard`](#takecard) | `card_id`, `option_index` | `RewardsSetSynchronizer.SelectLocalReward` |
| [`SkipRewards`](#skiprewards) | none | `RewardsSetSynchronizer.SkipLocalRewardsSet` |
| [`SelectCardFromScreen`](#selectcardfromscreen) | `card_id`, `option_index` | `ICardSelector.GetSelectedCards` |
| [`UsePotion`](#usepotion) | `potion_id`, `slot_index` | `PotionModel.EnqueueManualUse` |
| [`DiscardPotion`](#discardpotion) | `potion_id`, `slot_index` | `DiscardPotionGameAction..ctor` |
| [`ShopPurchase`](#shoppurchase) | `kind` | `MerchantEntry.OnTryPurchaseWrapper` |
| [`ProceedToNextAct`](#proceedtonextact) | none | `ActChangeSynchronizer.SetLocalPlayerReady` |
| [`TakeChestRelic`](#takechestrelic) | `relic_id`, `option_index` | `TreasureRoomRelicSynchronizer.PickRelicLocally` |
| [`SkipChestRelic`](#skipchestrelic) | none | `TreasureRoomRelicSynchronizer.SkipRelicLocally` |
| [`ChooseRestSiteOption`](#chooserestsiteoption) | `option_id`, `option_index` | `RestSiteSynchronizer.ChooseLocalOption` |

## `ChooseNeowBlessing`

| Argument | Presence | Value |
|---|---|---|
| `option_index` | required | non-negative integer, canonical and within Int32 |

`EventSynchronizer.ChooseLocalOption`, called by the driver. The opening blessing is an event like any other; only its option list is special.

## `MapMove`

| Argument | Presence | Value |
|---|---|---|
| `act` | required | non-negative integer, canonical and within Int32 |
| `row` | required | non-negative integer, canonical and within Int32 |
| `column` | required | non-negative integer, canonical and within Int32 |
| `negative_control_alternative_column` | negative control | non-negative integer, canonical and within Int32 |

`RunManager.EnterMapCoord`, called by the driver. Headlessly this is the whole move. Inside the retail client it is the middle of one, and the host supplies the screen's own travel; see docs/in-game-host.md.

## `PlayCard`

| Argument | Presence | Value |
|---|---|---|
| `card_id` | required | non-empty string |
| `hand_index` | required | non-negative integer, canonical and within Int32 |
| `target_index` | optional | non-negative integer, canonical and within Int32 |
| `negative_control_substitute_card_id` | negative control | non-empty string |
| `negative_control_substitute_hand_index` | negative control | non-negative integer, canonical and within Int32 |

`PlayCardAction..ctor`, called by the driver. Enqueued on the run's own action queue, which is what a clicked card does.

## `EndTurn`

Carries no arguments. Any argument at all is refused.

`PlayerCmd.EndTurn`, called by the driver. canBackOut is false: a recorded turn end was not taken back.

## `ChooseEventOption`

The event id is required and the opening blessing's is not: which event a floor generates is a consequence of the whole history before it, and an option index means nothing without the event it indexes.

| Argument | Presence | Value |
|---|---|---|
| `event_id` | required | non-empty string |
| `option_index` | required | non-negative integer, canonical and within Int32 |

`EventSynchronizer.ChooseLocalOption`, called by the driver. The same member, with the event's own id checked first.

## `ClaimReward`

| Argument | Presence | Value |
|---|---|---|
| `reward_type` | required | one of `gold`, `potion` |

`RewardsSetSynchronizer.SelectLocalReward`, called by the driver. The reward is found by the kind the loot screen names, never by position.

## `TakeCard`

| Argument | Presence | Value |
|---|---|---|
| `card_id` | required | non-empty string |
| `option_index` | required | non-negative integer, canonical and within Int32 |
| `negative_control_alternative_card_id` | negative control | string |
| `negative_control_alternative_option_index` | negative control | non-negative integer, canonical and within Int32 |

`RewardsSetSynchronizer.SelectLocalReward`, called by the driver. The same member for the card reward, whose own screen is then answered through ICardSelector.

## `SkipRewards`

Carries no arguments. Any argument at all is refused.

`RewardsSetSynchronizer.SkipLocalRewardsSet`, called by the driver. Dismissing a loot screen with something still on it is a decision, so it has a verb.

## `SelectCardFromScreen`

| Argument | Presence | Value |
|---|---|---|
| `card_id` | required | non-empty string |
| `option_index` | required | non-negative integer, canonical and within Int32 |
| `negative_control_alternative_option_index` | negative control | non-negative integer, canonical and within Int32 |

`ICardSelector.GetSelectedCards`, answered rather than called. The engine asks. The driver queues the manifest's picks before the action that opens the screen and confirms afterwards that a screen consumed each one.

## `UsePotion`

| Argument | Presence | Value |
|---|---|---|
| `potion_id` | required | non-empty string |
| `slot_index` | required | non-negative integer, canonical and within Int32 |
| `target_index` | optional | non-negative integer, canonical and within Int32 |

`PotionModel.EnqueueManualUse`, called by the driver. What the potion holder in the retail client calls when a potion is dragged onto a target.

## `DiscardPotion`

| Argument | Presence | Value |
|---|---|---|
| `potion_id` | required | non-empty string |
| `slot_index` | required | non-negative integer, canonical and within Int32 |

`DiscardPotionGameAction..ctor`, called by the driver. Enqueued on the run's own queue, which is what the potion popup's discard button does.

## `ShopPurchase`

The id and the index are required for four of the five kinds and refused for the fifth, which is checked below where the kind is known: a card removal buys a service and has nothing to name.

| Argument | Presence | Value |
|---|---|---|
| `kind` | required | one of `character_card`, `colorless_card`, `relic`, `potion`, `card_removal` |
| `option_index` | optional | non-negative integer, canonical and within Int32 |
| `card_id` | optional | non-empty string |
| `relic_id` | optional | non-empty string |
| `potion_id` | optional | non-empty string |

`MerchantEntry.OnTryPurchaseWrapper`, called by the driver. One member for all five kinds, because the merchant's own entries are what differ. A card removal reaches OneOffSynchronizer.DoLocalMerchantCardRemoval through it, and its screen is answered as any other card screen is.

What a purchase must name depends on what it bought.
An argument a kind does not have is refused as firmly as a missing one.

| `kind` | Also required | Refused |
|---|---|---|
| `character_card` | `card_id`, `option_index` | `relic_id`, `potion_id` |
| `colorless_card` | `card_id`, `option_index` | `relic_id`, `potion_id` |
| `relic` | `relic_id`, `option_index` | `card_id`, `potion_id` |
| `potion` | `potion_id`, `option_index` | `card_id`, `relic_id` |
| `card_removal` | nothing | `card_id`, `relic_id`, `potion_id`, `option_index` |

## `ProceedToNextAct`

Carries no arguments. Any argument at all is refused.

`ActChangeSynchronizer.SetLocalPlayerReady`, called by the driver. A vote rather than a call. RunManager.EnterNextAct is what it leads to, and calling that directly would skip the act floor the vote advances.

## `TakeChestRelic`

| Argument | Presence | Value |
|---|---|---|
| `relic_id` | required | non-empty string |
| `option_index` | required | non-negative integer, canonical and within Int32 |

`TreasureRoomRelicSynchronizer.PickRelicLocally`, called by the driver. The chest's relics were rolled by the engine when the room was entered.

## `SkipChestRelic`

Carries no arguments. Any argument at all is refused.

`TreasureRoomRelicSynchronizer.SkipRelicLocally`, called by the driver. The engine's own name for leaving the relic, and its own way of recording that.

## `ChooseRestSiteOption`

Named as well as positioned, for the reason a played card is: which options a rest site offers is a consequence of the run that reached it, so an index alone names nothing that can be checked.

| Argument | Presence | Value |
|---|---|---|
| `option_id` | required | non-empty string |
| `option_index` | required | non-negative integer, canonical and within Int32 |

`RestSiteSynchronizer.ChooseLocalOption`, called by the driver. The option is found by the id it declares, never by position: which options a rest site offers depends on the run that reached it.

## Verbs the format names and this build refuses

The alphabet is closed, so a decision nobody implemented has a name rather than being absent.
A manifest that uses one is refused at validation, before an engine is spent on it.

### `SelectHandCards`

Nothing of its own to map. Every card screen this build opens - over the hand, the deck or a pile - is answered through the one ICardSelector seam, by position in the list that screen offered, which SelectCardFromScreen already names. A prompt over the hand offers a filtered subset of it, so a hand position would not even be the right coordinate. A second verb here would be a second name for one thing.

### `CloseShop`

Nothing to map. The merchant is a room the run leaves by moving on the map, and the shop screen's own proceed button only hides the screen - MerchantRoom.Exit returns immediately under the headless flag. Closing a shop is presentation, like ProceedToMap.

### `ProceedToMap`

Returning to the map is presentation, not a decision: the engine is already standing wherever the previous action left it. See docs/proof-of-concept-path.md.

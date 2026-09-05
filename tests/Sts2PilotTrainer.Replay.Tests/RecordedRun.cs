using System.Globalization;

namespace Sts2PilotTrainer.Replay.Tests;

/// <summary>
/// A run the recorder could have written, built the way the recorder builds one.
///
/// Every value goes in through <see cref="RunCapture"/> and every negative-control
/// nomination through the same <see cref="Corruption"/> rule the mod's patches call,
/// so what comes out is what a recording is rather than a hand-written manifest that
/// resembles one. A fixture assembled any other way could carry a nomination no
/// recorder writes, which is exactly how a recording that could never pass the
/// publication gate went unnoticed.
///
/// The decisions are the shortest set that gives every one of
/// <see cref="Corruption.All"/> something to damage, and that list is a requirement on
/// the evidence run somebody plays as well as on this fixture: an opening blessing, an
/// event whose option opens a card screen offering more than it takes, a map move from
/// a node with a reachable sibling, at least two card plays with one of them aimed at
/// an enemy, a claimed reward, and a card reward that offered more than one card.
/// </summary>
internal static class RecordedRun
{
    internal const string Seed = "SFXT47K77RFK";

    /// <summary>The recording, as the manifest a recorder would finalise at run end.</summary>
    internal static ReplayManifest Manifest()
    {
        var capture = Captured();
        capture.Finish("abandoned");
        return capture.ToManifest();
    }

    internal static RunCapture Captured()
    {
        var capture = RunCapture.Begin(new RunRecordingStart
        {
            RunId = "native-SFXT47K77RFK-20260905-030000",
            RecorderVersion = "runmobile-recorder/fixture",
            Identity = Identity(),
            State = Floor(1),
            Digest = Digest(-1),
            RunClockMs = 0,
        });

        capture.Record(ActionVerb.ChooseNeowBlessing, Args(("option_index", "0")), Floor(1), Digest(0));

        capture.Record(
            ActionVerb.ChooseEventOption,
            Args(("event_id", "EVENT.WATERLOGGED_SCRIPTORIUM"), ("option_index", "2")),
            Floor(1),
            Digest(1));

        // The screen that event opened offered four cards and the player enchanted one,
        // so three positions are still free for a control to point at.
        capture.Record(
            ActionVerb.SelectCardFromScreen,
            ScreenPick("CARD.DEFEND_IRONCLAD", chosen: 1, offeredCount: 4),
            Floor(1),
            Digest(2));

        capture.Record(ActionVerb.MapMove, Move(act: 0, row: 1, column: 3, reachable: [1, 3]), InFight(2), Digest(3));

        capture.Record(
            ActionVerb.PlayCard,
            Args(("card_id", "CARD.BASH"), ("hand_index", "0"), ("target_index", "0")),
            InFight(2, enemyHp: 30),
            Digest(4));
        capture.Record(ActionVerb.EndTurn, Args(), InFight(2, turn: 2, enemyHp: 30, hp: 58), Digest(5));
        capture.Record(
            ActionVerb.PlayCard,
            Args(("card_id", "CARD.STRIKE_IRONCLAD"), ("hand_index", "1")),
            Won(2, hp: 58),
            Digest(6));

        capture.Record(ActionVerb.ClaimReward, Args(("reward_type", "gold")), Won(2, hp: 58), Digest(7));
        capture.Record(
            ActionVerb.TakeCard,
            Reward(["CARD.POMMEL_STRIKE", "CARD.TREMBLE", "CARD.WHIRLWIND"], taken: 0),
            Won(2, hp: 58),
            Digest(8));

        return capture;
    }

    /// <summary>A map move with the sibling node the same node also led to.</summary>
    private static IReadOnlyDictionary<string, string> Move(
        int act, int row, int column, IReadOnlyList<int> reachable)
    {
        var args = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["act"] = Number(act),
            ["row"] = Number(row),
            ["column"] = Number(column),
        };
        if (Corruption.NominateColumn(column, reachable) is { } alternative)
        {
            args[Corruption.AlternativeColumn] = Number(alternative);
        }

        return args;
    }

    /// <summary>A card taken off a reward, with another card that reward offered.</summary>
    private static IReadOnlyDictionary<string, string> Reward(IReadOnlyList<string> offered, int taken)
    {
        var args = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["card_id"] = offered[taken],
            ["option_index"] = Number(taken),
        };
        if (Corruption.NominateCard(offered, taken) is { } alternative)
        {
            args[Corruption.AlternativeCardId] = alternative.CardId;
            args[Corruption.AlternativeOptionIndex] = Number(alternative.OptionIndex);
        }

        return args;
    }

    /// <summary>One answer to a card screen, with a position nobody took off it.</summary>
    private static IReadOnlyDictionary<string, string> ScreenPick(string cardId, int chosen, int offeredCount)
    {
        var args = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["card_id"] = cardId,
            ["option_index"] = Number(chosen),
        };
        if (Corruption.NominateScreenOption(offeredCount, [chosen]) is { } alternative)
        {
            args[Corruption.AlternativeOptionIndex] = Number(alternative);
        }

        return args;
    }

    private static RunIdentityReading Identity() => new()
    {
        BuildVersion = "v0.111.0",
        BuildDateUtc = "2026.08.14",
        ContentHash = "1568834832",
        GameMode = "standard",
        Seed = Seed,
        Ascension = 10,
        Character = "CHARACTER.IRONCLAD",
        Acts = ["ACT.UNDERDOCKS"],
        Unlocks = new UnlockStateInventory
        {
            Epochs = ["EPOCH.ONE"],
            EncountersSeen = ["ENCOUNTER.TEST"],
            Runs = 137,
        },
        Mods = ModEnvironment.AsRecorded(
            [new LocalMod("Runmobile", "Runmobile", "0.1.0", AffectsGameplay: false, "Loaded")]),
    };

    private static IReadOnlyDictionary<string, string> Floor(int floor) => new Dictionary<string, string>(
        StringComparer.Ordinal)
    {
        ["combat.in_progress"] = "false",
        ["combat.outcome"] = "none",
        ["run.total_floor"] = Number(floor),
        ["run.act_floor"] = Number(floor),
        ["player.hp"] = "68",
        ["player.max_hp"] = "68",
    };

    /// <summary>Two enemies, because a play only names a target where more than one is
    /// alive - and a play that named one is what <c>target-the-other-enemy</c> needs.</summary>
    private static IReadOnlyDictionary<string, string> InFight(
        int floor, int turn = 1, int enemyHp = 42, int hp = 68) => new Dictionary<string, string>(
        StringComparer.Ordinal)
    {
        ["combat.in_progress"] = "true",
        ["combat.outcome"] = "in_progress",
        ["combat.turn"] = Number(turn),
        ["combat.encounter"] = "ENCOUNTER.TEST",
        ["combat.enemy_count"] = "2",
        ["combat.enemy.0.model"] = "MONSTER.TEST",
        ["combat.enemy.0.hp"] = Number(enemyHp),
        ["combat.enemy.1.model"] = "MONSTER.TEST",
        ["combat.enemy.1.hp"] = Number(enemyHp),
        ["run.total_floor"] = Number(floor),
        ["run.act_floor"] = Number(floor),
        ["player.hp"] = Number(hp),
        ["player.max_hp"] = "68",
    };

    private static IReadOnlyDictionary<string, string> Won(int floor, int hp) => new Dictionary<string, string>(
        StringComparer.Ordinal)
    {
        ["combat.in_progress"] = "false",
        ["combat.outcome"] = "victory",
        ["combat.turn"] = "2",
        ["combat.encounter"] = "ENCOUNTER.TEST",
        ["combat.enemy_count"] = "0",
        ["run.total_floor"] = Number(floor),
        ["run.act_floor"] = Number(floor),
        ["player.hp"] = Number(hp),
        ["player.max_hp"] = "68",
    };

    private static string Digest(int seq) =>
        "sha256:" + (seq + 1).ToString("x2", CultureInfo.InvariantCulture).PadLeft(64, 'a');

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static IReadOnlyDictionary<string, string> Args(params (string Key, string Value)[] args) =>
        args.ToDictionary(arg => arg.Key, arg => arg.Value, StringComparer.Ordinal);
}

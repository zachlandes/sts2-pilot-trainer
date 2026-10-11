using System.Text.RegularExpressions;

namespace EncounterIntel;

// These annotations describe the inspected build, not arbitrary monster code
public static class ReviewedExamples
{
    private const string Quantity = @"(?<base>\d+)(?: \(\{\{Asc2\|(?<tier>\d+)\|(?<higher>\d+)\}\}\))?";

    public static IReadOnlyList<MonsterRule> Monsters(WikiCatalogue wiki)
    {
        var nibbit = wiki.Enemy("Nibbit");
        var phrog = wiki.Enemy("Phrog Parasite");
        var wriggler = wiki.Enemy("Wriggler");
        var spinner = wiki.Enemy("Sludge Spinner");
        var nibbitMoves = new[]
        {
            Move(nibbit, "Butt", "BUTT_MOVE", Attack("ButtDamage")),
            Move(nibbit, "Hesitant Slice", "SLICE_MOVE", Attack("SliceDamage"), Block("SliceBlock")),
            Move(nibbit, "Hiss", "HISS_MOVE", Strength("HissStrengthGain")),
        };
        var phrogMoves = new[]
        {
            Move(phrog, "Infect", "INFECT_MOVE", Infection("constant:_infestAmt")),
            Move(phrog, "Lash", "LASH_MOVE", Attack("LashDamage")),
        };
        var wrigglerMoves = new[]
        {
            // The wiki's move table omits this death-spawned opening
            new MoveRule("SPAWNED_MOVE", "Spawned", [new MoveEffect(EffectKind.Stun, EffectTarget.Self, null, new(1), 1, null, "reviewed:1")]),
            Move(wriggler, "Nasty Bite", "NASTY_BITE_MOVE", Attack("BiteDamage")),
            Move(wriggler, "Wriggle", "WRIGGLE_MOVE", Infection("reviewed:1"), Strength("constant:_wriggleStrength")),
        };
        var spinnerMoves = new[]
        {
            Move(spinner, "Oil Spray", "OIL_SPRAY_MOVE", Attack("OilSprayDamage"), Weak("reviewed:1")),
            Move(spinner, "Slam", "SLAM_MOVE", Attack("SlamDamage")),
            Move(spinner, "Rage", "RAGE_MOVE", Attack("RageDamage"), Strength("reviewed:3")),
        };
        return
        [
            Rule(nibbit, "MONSTER.NIBBIT", nibbitMoves,
                [new(OpeningCondition.Alone, "BUTT_MOVE"), new(OpeningCondition.Back, "HISS_MOVE"), new(OpeningCondition.Front, "SLICE_MOVE")],
                Cycle("BUTT_MOVE", "SLICE_MOVE", "HISS_MOVE")),
            Rule(phrog, "MONSTER.PHROG_PARASITE", phrogMoves,
                [new(OpeningCondition.Always, "INFECT_MOVE")], Cycle("INFECT_MOVE", "LASH_MOVE"), [new("POWER.INFESTED_POWER", 4)]),
            Rule(wriggler, "MONSTER.WRIGGLER", wrigglerMoves,
                [new(OpeningCondition.StartStunned, "SPAWNED_MOVE"), new(OpeningCondition.SlotOneOrThree, "NASTY_BITE_MOVE"), new(OpeningCondition.SlotTwoOrFour, "WRIGGLE_MOVE")],
                [new("SPAWNED_MOVE", "INIT_MOVE"), .. Cycle("NASTY_BITE_MOVE", "WRIGGLE_MOVE")]),
            Rule(spinner, "MONSTER.SLUDGE_SPINNER", spinnerMoves,
                [new(OpeningCondition.Always, "OIL_SPRAY_MOVE")],
                spinnerMoves.SelectMany(from => spinnerMoves.Select(to => new TransitionRule(from.StateId, to.StateId, 1, true))).ToArray()),
        ];
    }

    public static IReadOnlyList<EncounterRule> Encounters() =>
    [
        new("ENCOUNTER.NIBBITS_WEAK", [new("MONSTER.NIBBIT", 1, [])], []),
        new("ENCOUNTER.NIBBITS_NORMAL", [new("MONSTER.NIBBIT", 2, ["front", "back"])], []),
        new("ENCOUNTER.PHROG_PARASITE_ELITE", [new("MONSTER.PHROG_PARASITE", 1, [])],
            [new("Phrog dies without removal prevention (Infested)",
                [new("MONSTER.WRIGGLER", 4, ["wriggler1", "wriggler2", "wriggler3", "wriggler4"], true)], true)]),
        new("ENCOUNTER.SLUDGE_SPINNER_WEAK", [new("MONSTER.SLUDGE_SPINNER", 1, [])], []),
    ];

    private static MonsterRule Rule(WikiEnemy enemy, string id, IReadOnlyList<MoveRule> moves,
        IReadOnlyList<OpeningRule> openings, IReadOnlyList<TransitionRule> transitions, IReadOnlyList<PowerReading>? powers = null)
    {
        var low = Hp(enemy.BaseHp);
        var high = Hp(enemy.AscHp);
        return new MonsterRule(id, enemy.Name, new(low.Min, 8, high.Min), new(low.Max, 8, high.Max), moves, openings, transitions, powers ?? []);
    }

    private static (int Min, int Max) Hp(string? text)
    {
        var match = Regex.Match(text ?? "", @"^(\d+)(?:-(\d+))?$");
        if (!match.Success) throw new IntelRefusal("An example's wiki HP range is unsupported.");
        return (Number(match.Groups[1].Value), Number(match.Groups[2].Success ? match.Groups[2].Value : match.Groups[1].Value));
    }

    private sealed record EffectPattern(string Expression, EffectKind Kind, EffectTarget Target, string? Entity, string? Pile, string Binding);
    private static EffectPattern Attack(string binding) => new("Deals " + Quantity + @" damage(?: [×x](?<hits>\d+))?\.", EffectKind.Attack, EffectTarget.Players, null, null, binding);
    private static EffectPattern Block(string binding) => new("Gains " + Quantity + @" \{\{KW2\|Block\}\}\.", EffectKind.Block, EffectTarget.Self, null, null, binding);
    private static EffectPattern Strength(string binding) => new("Gains " + Quantity + @" \{\{BD2\|Strength\}\}\.", EffectKind.Power, EffectTarget.Self, "POWER.STRENGTH_POWER", null, binding);
    private static EffectPattern Weak(string binding) => new("Applies " + Quantity + @" \{\{BD2\|Weak\}\}\.", EffectKind.Power, EffectTarget.Players, "POWER.WEAK_POWER", null, binding);
    private static EffectPattern Infection(string binding) => new("Shuffles " + Quantity + @" \{\{C2\|Infection\}\} into your discard pile\.", EffectKind.AddCard, EffectTarget.Players, "CARD.INFECTION", "discard", binding);

    private static MoveRule Move(WikiEnemy enemy, string name, string state, params EffectPattern[] patterns)
    {
        var entries = enemy.Moves.Where(move => move.Name == name).ToArray();
        if (entries.Length != 1)
            throw new IntelRefusal($"The wiki example {enemy.Name}.{name} has {entries.Length} entries; exactly one is required.");
        var text = entries[0].Text;
        var effects = new List<MoveEffect>();
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(text, "^" + pattern.Expression);
            if (!match.Success) throw new IntelRefusal($"Unrecognized wiki effect: {enemy.Name}.{name}: {text}");
            var amount = new AscendingValue(Number(match.Groups["base"].Value),
                match.Groups["tier"].Success ? Number(match.Groups["tier"].Value) : null,
                match.Groups["higher"].Success ? Number(match.Groups["higher"].Value) : null);
            effects.Add(new MoveEffect(pattern.Kind, pattern.Target, pattern.Entity, amount,
                match.Groups["hits"].Success ? Number(match.Groups["hits"].Value) : 1, pattern.Pile, pattern.Binding));
            text = text[match.Length..].TrimStart();
        }
        if (text.Length != 0) throw new IntelRefusal($"Unrecognized trailing wiki effect: {enemy.Name}.{name}: {text}");
        return new MoveRule(state, name, effects.ToArray());
    }

    private static int Number(string text) => int.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    private static TransitionRule[] Cycle(params string[] states) => states.Select((state, index) => new TransitionRule(state, states[(index + 1) % states.Length])).ToArray();
}

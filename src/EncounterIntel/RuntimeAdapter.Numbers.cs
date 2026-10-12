using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;

namespace EncounterIntel;

public static partial class RuntimeAdapter
{
    public static IReadOnlyList<CheckedMonster> ReadWorkedExamples(WikiCatalogue? catalogue = null)
    {
        var run = CurrentRun() ?? throw new IntelRefusal("There is no run for ascension-sensitive numbers.");
        var models = new Dictionary<string, MonsterModel>(StringComparer.Ordinal)
        {
            ["MONSTER.NIBBIT"] = ModelDb.Monster<Nibbit>(),
            ["MONSTER.PHROG_PARASITE"] = ModelDb.Monster<PhrogParasite>(),
            ["MONSTER.WRIGGLER"] = ModelDb.Monster<Wriggler>(),
            ["MONSTER.SLUDGE_SPINNER"] = ModelDb.Monster<SludgeSpinner>(),
        };
        return ReviewedExamples.Monsters(catalogue ?? WikiCatalogue.Read()).Select(rule => Check(rule, models[rule.Id], run.AscensionLevel)).ToArray();
    }

    private static CheckedMonster Check(MonsterRule rule, MonsterModel model, int ascension)
    {
        RequireNative(model);
        if (model.Id.ToString() != rule.Id)
            throw new IntelRefusal("The numeric cross-check is not bound to this monster.");
        var values = new List<ResolvedQuantity>
        {
            new("min_hp", rule.MinHp.At(ascension), model.MinInitialHp),
            new("max_hp", rule.MaxHp.At(ascension), model.MaxInitialHp),
        };
        foreach (var move in rule.Moves)
        {
            foreach (var effect in move.Effects)
            {
                var property = effect.GameProperty ?? throw new IntelRefusal("An example effect has no numeric authority.");
                var authority = property.StartsWith("reviewed:", StringComparison.Ordinal) ? QuantityAuthority.BuildReviewedLiteral
                    : property.StartsWith("constant:", StringComparison.Ordinal) ? QuantityAuthority.GameConstant : QuantityAuthority.LiveGetter;
                values.Add(new($"{move.StateId}.{effect.Kind}.{effect.EntityId ?? "amount"}", effect.Amount.At(ascension), GameValue(model, property), authority));
                if (effect.Kind == EffectKind.Attack)
                {
                    // Only Phrog has multiple hits in the reviewed examples
                    var hits = model is PhrogParasite ? GameValue(model, "constant:_lashRepeat") : 1;
                    values.Add(new($"{move.StateId}.hits", effect.Hits, hits,
                        model is PhrogParasite ? QuantityAuthority.GameConstant : QuantityAuthority.BuildReviewedLiteral));
                }
            }
        }
        return new CheckedMonster(rule, values);
    }

    private static int GameValue(MonsterModel model, string binding)
    {
        const BindingFlags members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        if (binding.StartsWith("reviewed:", StringComparison.Ordinal))
            return int.Parse(binding[9..], System.Globalization.CultureInfo.InvariantCulture);
        if (binding.StartsWith("constant:", StringComparison.Ordinal))
        {
            var field = model.GetType().GetField(binding[9..], members);
            if (field is not { IsLiteral: true } || field.GetRawConstantValue() is not int value)
                throw new IntelRefusal($"The inspected constant {model.Id}.{binding} is absent.");
            return value;
        }
        PropertyInfo? property = null;
        for (var type = model.GetType(); type is not null && property is null; type = type.BaseType)
            property = type.GetProperty(binding, members | BindingFlags.DeclaredOnly);
        if (property?.GetValue(model) is not int number)
            throw new IntelRefusal($"The inspected numeric getter {model.Id}.{binding} is absent.");
        return number;
    }
}

namespace EncounterIntel;

public enum ForecastRoomType { Monster, Elite, Treasure, Shop, Event }
public enum NormalEncounterPhase { Weak, Regular }
public enum PoolBasis { PhaseDefinitions, FirstRunScript }

public sealed record RawRoomOdds(float Monster, float Elite, float Treasure, float Shop);
public sealed record RoomChance(ForecastRoomType Type, double Probability);
public sealed record RoomRollInterval(ForecastRoomType Type, float InclusiveThreshold);

/// <summary>The next draw's intervals, not an encounter distribution or a route forecast</summary>
public sealed record UnknownRoomProjection(
    string Rule,
    ForecastRoomType Fallback,
    IReadOnlyList<RoomRollInterval> Intervals,
    IReadOnlyList<RoomChance> Chances)
{
    public ForecastRoomType OutcomeAt(float draw)
    {
        if (!float.IsFinite(draw) || draw < 0 || draw > 1)
            throw new ArgumentOutOfRangeException(nameof(draw));
        return Intervals.FirstOrDefault(interval => draw <= interval.InclusiveThreshold)?.Type ?? Fallback;
    }
}

/// <summary>Pure projection of the inspected beta's ordered float accumulation</summary>
public static class UnknownRoomRules
{
    public static UnknownRoomProjection Project(RawRoomOdds odds, IReadOnlySet<ForecastRoomType> allowed,
        bool firstRun, int priorUnknownVisits)
    {
        if (priorUnknownVisits < 0) throw new IntelRefusal("The visible unknown-room history is invalid.");
        // The tutorial returns before filters, hooks, RNG and adaptive updates
        if (firstRun && priorUnknownVisits <= 2)
        {
            var forced = priorUnknownVisits == 2 ? ForecastRoomType.Monster : ForecastRoomType.Event;
            return new("first-run scripted room", forced, [], Chances(new Dictionary<ForecastRoomType, double> { [forced] = 1 }));
        }
        if (allowed.Count == 0 || allowed.Any(type => !Enum.IsDefined(type)))
            throw new IntelRefusal("No recognized fallback room type is available.");
        var fallback = allowed.Contains(ForecastRoomType.Event) ? ForecastRoomType.Event : allowed.Min();
        var raw = new[] { odds.Monster, odds.Elite, odds.Treasure, odds.Shop };
        if (raw.Any(value => !float.IsFinite(value))) throw new IntelRefusal("The current room odds are not finite.");
        var types = new[] { ForecastRoomType.Monster, ForecastRoomType.Elite, ForecastRoomType.Treasure, ForecastRoomType.Shop };
        var intervals = new List<RoomRollInterval>();
        var mass = new Dictionary<ForecastRoomType, double>();
        float cumulative = 0;
        double accounted = 0;
        for (var i = 0; i < raw.Length; i++)
        {
            if (!allowed.Contains(types[i]) || raw[i] < 0) continue;
            // Float addition and ordering are observable at a boundary; do not sum in double
            cumulative += raw[i];
            intervals.Add(new(types[i], cumulative));
            var end = Math.Clamp((double)cumulative, 0, 1);
            mass[types[i]] = end - accounted;
            accounted = end;
        }
        mass[fallback] = mass.GetValueOrDefault(fallback) + 1 - accounted;
        return new("filtered cumulative room odds", fallback, intervals.ToArray(), Chances(mass));
    }

    private static RoomChance[] Chances(IReadOnlyDictionary<ForecastRoomType, double> mass) =>
        Enum.GetValues<ForecastRoomType>().Select(type => new RoomChance(type, mass.GetValueOrDefault(type))).ToArray();
}

public sealed record UnknownRoomForecast(BuildIdentity Build, string ActId, int ActIndex, int Column, int Row,
    RawRoomOdds RawOdds, int PriorUnknownVisits, bool FirstRun, bool ShopBlacklisted,
    IReadOnlyList<string> RecognizedHooks, UnknownRoomProjection Projection)
{
    public string Scope => "This question mark as the next visit, with no intervening decisions; Event can still offer a fight.";
}

/// <summary>Public definition candidates, never remaining-bag probabilities</summary>
public sealed record EncounterPoolForecast(BuildIdentity Build, int Ascension, CatalogueProvenance Provenance,
    string ActId, int ActIndex, int NormalVisits, int WeakEncounterCount, NormalEncounterPhase NormalPhase,
    PoolBasis NormalBasis, IReadOnlyList<EncounterLookup> NormalPool,
    PoolBasis EliteBasis, IReadOnlyList<EncounterLookup> ElitePool)
{
    public string Scope => "Upcoming normal and elite definition pools, not an exact distribution or remaining bag; nested event fights are separate.";
}

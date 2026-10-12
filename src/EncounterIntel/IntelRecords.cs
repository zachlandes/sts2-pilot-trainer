using System.Text.Json;

namespace EncounterIntel;

public sealed record WikiSource(string Module, string Url, int Revision, string FetchedAt, string Sha256);
public sealed record WikiAttribution(string Creator, string Source, string License, string LicenseUrl, string PolicyUrl, string Changes);
public sealed record WikiQuantity(string Kind, int Offset, string Text, AscendingValue Amount);
public sealed record WikiMove(string? Name, string Text, IReadOnlyList<string> AscText, IReadOnlyList<string> Icons,
    IReadOnlyList<WikiQuantity> Quantities);
public sealed record WikiPattern(string? Text, string? Gap);
public sealed record WikiPhase(string Trigger, string Relation, string Text, string Origin,
    IReadOnlyList<string> MonsterNames, IReadOnlyList<string> Gaps);
public sealed record WikiMember(string WikiLink, int Count);
public sealed record WikiReference(string EnemyName, string SourceModule);
public sealed record WikiEncounter(string Id, string Location, string Text, string EvidenceKind, IReadOnlyList<WikiMember> Members,
    bool IsEvent, IReadOnlyList<string> Gaps, IReadOnlyList<WikiReference> References);
public sealed record WikiEnemy(string Name, string SourceModule, string? Kind, string? BaseHp, string? AscHp,
    string? StartsWith, string? InPartyWith, IReadOnlyList<WikiMove> Moves, IReadOnlyDictionary<string, JsonElement> AdditionalFields,
    IReadOnlyList<string> EncounterIds, IReadOnlyList<WikiPhase> Phases, WikiPattern Pattern, IReadOnlyList<string> Gaps);
public sealed record WikiCatalogue(int Schema, WikiAttribution Attribution, IReadOnlyList<WikiSource> Sources,
    IReadOnlyList<WikiEnemy> Enemies, IReadOnlyList<WikiEncounter> Encounters)
{
    public static WikiCatalogue Read()
    {
        using var stream = typeof(WikiCatalogue).Assembly.GetManifestResourceStream("EncounterIntel.wiki-enemies.json")
            ?? throw new IntelRefusal("The pinned wiki catalogue is missing.");
        var catalogue = JsonSerializer.Deserialize<WikiCatalogue>(stream, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        }) ?? throw new IntelRefusal("The pinned wiki catalogue is unreadable.");
        if (catalogue.Schema != 2 || catalogue.Sources.Count != 8 || catalogue.Enemies.Count == 0)
            throw new IntelRefusal("The wiki catalogue has an unsupported schema or an incomplete source receipt.");
        return catalogue;
    }

    public WikiEnemy Enemy(string name) => Enemies.SingleOrDefault(enemy => enemy.Name == name)
        ?? throw new IntelRefusal($"The pinned wiki catalogue has no enemy named {name}.");
}

public sealed class IntelRefusal(string message) : InvalidOperationException(message);
public sealed record BuildIdentity(string Version, string BuildDate, string Commit, string AssemblySha256);
public sealed record EncounterDefinition(string Id, IReadOnlyList<string> PossibleMonsterIds);
public sealed record ActIntel(string Id, int Index, IReadOnlyList<EncounterDefinition> Weak,
    IReadOnlyList<EncounterDefinition> Regular, IReadOnlyList<EncounterDefinition> Elites,
    IReadOnlyList<EncounterDefinition> BossPool, IReadOnlyList<string> SelectedBosses);
public enum BoardKind { None, Combat, EventLayout }
public sealed record IntentReading(string Kind, int? TotalDamage, int? Hits);
public sealed record PowerReading(string Id, decimal Amount);
public sealed record EnemyReading(string Id, string? Slot, int Hp, int MaxHp, int Block,
    IReadOnlyList<PowerReading> Powers, string? AnnouncedMove, IReadOnlyList<IntentReading> Intents);
public sealed record BoardReading(BoardKind Kind, string? EncounterId, IReadOnlyList<EnemyReading> Enemies);
public sealed record IntelReading(BuildIdentity Build, int Ascension, ActIntel Act, BoardReading Board);

public sealed record AscendingValue(int Base, int? Threshold = null, int? Higher = null)
{
    public int At(int ascension) => Threshold is { } threshold && ascension >= threshold
        ? Higher ?? throw new IntelRefusal("An ascension value has no higher quantity.") : Base;
}
public enum EffectKind { Attack, Block, Power, AddCard, Stun }
public enum EffectTarget { Self, Players }
public sealed record MoveEffect(EffectKind Kind, EffectTarget Target, string? EntityId, AscendingValue Amount,
    int Hits, string? Pile, string? GameProperty);
public sealed record MoveRule(string StateId, string Name, IReadOnlyList<MoveEffect> Effects);
public enum OpeningCondition { Always, Alone, Back, Front, StartStunned, SlotOneOrThree, SlotTwoOrFour }
public sealed record OpeningRule(OpeningCondition Condition, string StateId);
public sealed record TransitionRule(string From, string To, int Weight = 1, bool CannotRepeat = false);
public sealed record RosterMember(string MonsterId, int Count, IReadOnlyList<string> Slots, bool StartStunned = false);
public sealed record EncounterPhase(string Trigger, IReadOnlyList<RosterMember> Roster, bool PreventsCombatEnd = false);
public sealed record EncounterRule(string Id, IReadOnlyList<RosterMember> Opening, IReadOnlyList<EncounterPhase> LaterPhases);
public sealed record MonsterRule(string Id, string WikiName, AscendingValue MinHp, AscendingValue MaxHp,
    IReadOnlyList<MoveRule> Moves, IReadOnlyList<OpeningRule> Openings, IReadOnlyList<TransitionRule> Transitions,
    IReadOnlyList<PowerReading> InitialPowers)
{
    public string SemanticAssemblySha256 => SupportedBuild.AssemblySha256;
}
public enum QuantityAuthority { LiveGetter, GameConstant, BuildReviewedLiteral, ConstructedGraph }
public sealed record ResolvedQuantity(string Field, int Wiki, int Game, QuantityAuthority Authority = QuantityAuthority.LiveGetter)
{
    public bool Mismatch => Wiki != Game;
}
public sealed record CheckedMonster(MonsterRule Rule, IReadOnlyList<ResolvedQuantity> Quantities)
{
    public bool Mismatch => Quantities.Any(value => value.Mismatch);
}

namespace EncounterIntel;

public enum LookupState { Ready, NotReady, NoRun, NotOnBoard, Refused }

public sealed class LookupResult<T> where T : class
{
    public LookupState State { get; }
    public T? Value { get; }
    public string? Reason { get; }

    internal LookupResult(T value) => (State, Value) = (LookupState.Ready, value);
    internal LookupResult(LookupState state, string reason) => (State, Reason) = (state, reason);
}

// Keep localization and asset references native without loading or copying game resources
public sealed record NativeText(string Table, string Key)
{
    public IReadOnlyDictionary<string, decimal> Numbers { get; init; } = new Dictionary<string, decimal>();
}
public sealed record NativeMonster(string Id, NativeText Name, string VisualsScene,
    IReadOnlyDictionary<string, NativeText> MoveNames);
public sealed record CatalogueProvenance(WikiAttribution Attribution, IReadOnlyList<WikiSource> Sources,
    IReadOnlyList<string> Gaps);
public sealed record MonsterLookup(NativeMonster Native, CatalogueMonster Reference);
public sealed record EncounterLookup(NativeText Name, IReadOnlyList<string> MapAssets,
    CatalogueEncounter Reference, IReadOnlyList<MonsterLookup> PossibleMembers);
public sealed record ActLookup(BuildIdentity Build, int Ascension, CatalogueProvenance Provenance,
    string Id, int Index, NativeText Name,
    IReadOnlyList<EncounterLookup> Elites, IReadOnlyList<EncounterLookup> BossPool,
    IReadOnlyList<EncounterLookup> SelectedBosses);
public sealed record EnemyLookup(BuildIdentity Build, int Ascension, CatalogueProvenance Provenance,
    BoardKind BoardKind, string? EncounterId,
    MonsterLookup Static, NativeMonster CurrentNative, EnemyReading Instance);
public sealed record BoardLookup(BuildIdentity Build, int Ascension, BoardKind Kind, string? EncounterId,
    CatalogueProvenance? Provenance,
    IReadOnlyList<EnemyLookup> Enemies);

internal sealed class IntelUnavailable(LookupState state, string message) : IntelRefusal(message)
{
    internal LookupState State { get; } = state;
}

using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace EncounterIntel;

[ModInitializer(nameof(Initialize))]
public static class EncounterIntelMod
{
    // Initialization precedes the model database; lookup is explicitly on demand
    public static void Initialize() => Log.Info("Encounter Intel data prototype loaded; no UI or gameplay patches installed.");

    public static LookupResult<ActLookup> CurrentAct() => RuntimeAdapter.ReadActLookup();
    public static LookupResult<BoardLookup> CurrentBoard() => RuntimeAdapter.ReadBoardLookup();
    public static LookupResult<EnemyLookup> Enemy(MegaCrit.Sts2.Core.Entities.Creatures.Creature enemy) => RuntimeAdapter.ReadEnemyLookup(enemy);
    public static IntelReading Lookup() => RuntimeAdapter.ReadCurrent();
    public static IReadOnlyList<CheckedMonster> WorkedExamples() => RuntimeAdapter.ReadWorkedExamples();
    public static CatalogueReading Catalogue() => RuntimeAdapter.ReadCatalogue();
    public static PatternAtlas Patterns() => RuntimeAdapter.ReadPatterns();
}

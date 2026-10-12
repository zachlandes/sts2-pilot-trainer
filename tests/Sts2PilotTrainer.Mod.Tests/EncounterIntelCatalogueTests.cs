using EncounterIntel;
using MegaCrit.Sts2.Core.Models;

namespace Sts2PilotTrainer.Arbiter.Tests;

public sealed partial class EncounterIntelTests
{
    [Fact]
    public void EveryPinnedEnemyAndEncounterRetainsTypedEditorialEvidenceOrANamedGap()
    {
        var wiki = WikiCatalogue.Read();
        Assert.Equal(109, wiki.Enemies.Count);
        Assert.All(wiki.Enemies, enemy =>
        {
            Assert.Contains(wiki.Sources, source => source.Module == enemy.SourceModule);
            Assert.True(enemy.Moves.Count > 0 || enemy.Gaps.Count > 0);
            Assert.NotNull(enemy.Pattern.Gap);
            Assert.All(enemy.EncounterIds, id => Assert.Contains(wiki.Encounters, encounter => encounter.Id == id));
        });
        Assert.All(wiki.Encounters, encounter =>
        {
            Assert.NotEmpty(encounter.References);
            Assert.NotEmpty(encounter.Text);
            Assert.NotEmpty(encounter.Gaps);
            Assert.All(encounter.References, reference => Assert.Contains(wiki.Enemies,
                enemy => enemy.Name == reference.EnemyName && enemy.SourceModule == reference.SourceModule && enemy.EncounterIds.Contains(encounter.Id)));
            Assert.All(encounter.Members, member => Assert.True(member.Count > 0));
        });
        var vegetation = Assert.Single(wiki.Encounters, encounter => encounter.EvidenceKind == "icon-row" && encounter.Location.Contains("Dense Vegetation", StringComparison.Ordinal));
        Assert.True(vegetation.IsEvent);
        Assert.Equal(4, Assert.Single(vegetation.Members).Count);
        Assert.Contains(wiki.Enemy("Phrog Parasite").Phases, phase => phase.Trigger == "death" && phase.MonsterNames.Contains("Wriggler"));
        Assert.Contains(wiki.Enemy("Gremlin Merc").Phases, phase => phase.Trigger == "death" && phase.MonsterNames.Contains("Fat Gremlin"));
        Assert.NotEmpty(wiki.Enemy("Knight Gang").Gaps);
        Assert.NotEmpty(wiki.Enemy("Nibbit").Moves.Single(move => move.Name == "Butt").Quantities);
    }

    [GameTheory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void FullCatalogueNamesEveryRegisteredDefinitionWithoutChangingTheRun(int ascension)
    {
        WithRun(ascension, ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"], session =>
        {
            var before = StateReceipt(session);
            var files = ProfileReceipt();
            var reading = RuntimeAdapter.ReadCatalogue();
            Assert.Equal(before, StateReceipt(session));
            Assert.Equal(files, ProfileReceipt());
            Assert.Equal(SupportedBuild.AssemblySha256, reading.Build.AssemblySha256);
            foreach (var model in ModelDb.All.Where(model => model is MonsterModel or EncounterModel))
                Assert.Contains(reading.Coverage, row => row.Id == model.Id.ToString());
            Assert.All(reading.Monsters, monster =>
            {
                Assert.True(monster.Moves.Count > 0 || monster.Gaps.Count > 0);
                Assert.NotNull(monster.Pattern);
                Assert.NotEmpty(monster.PatternComparison.Reason);
                if (monster.PatternComparison.Standing == "unavailable") Assert.Null(monster.PatternComparison.SourceText);
                else
                {
                    Assert.NotEmpty(monster.PatternComparison.SourceText!);
                    Assert.NotNull(monster.PatternComparison.Source);
                }
                Assert.Contains(reading.Coverage, row => row.Id == monster.Id && row.Standing == monster.Standing);
            });
            Assert.All(reading.Coverage.Where(row => row.Standing != CatalogueStanding.Catalogued), row => Assert.NotEmpty(row.Reasons));
            Assert.Contains(reading.Encounters, encounter => encounter.Id == "ENCOUNTER.DENSE_VEGETATION_EVENT_ENCOUNTER");
            Assert.Contains(reading.Encounters, encounter => encounter.Id == "ENCOUNTER.PUNCH_OFF_EVENT_ENCOUNTER");
            Assert.Contains(reading.Encounters, encounter => encounter.Id == "ENCOUNTER.MYSTERIOUS_KNIGHT_EVENT_ENCOUNTER");
            Assert.All(reading.Encounters, encounter => Assert.NotEmpty(encounter.DamageSchedules));
            var nibbits = reading.Encounters.Single(encounter => encounter.Id == "ENCOUNTER.NIBBITS_NORMAL").DamageSchedules[0];
            var nibbit = Assert.Single(nibbits.PossibleMembers);
            Assert.Equal(new[] { "SLICE_MOVE", "HISS_MOVE" }, nibbit.OpeningAlternatives.Select(variant => variant.OpeningMove));
            Assert.Contains(reading.PatternGaps, gap => gap.Contains("source receipt:", StringComparison.Ordinal));
            Assert.Equal(3, reading.Monsters.Count(monster => monster.Wiki.Any(enemy => enemy.Name == "Decimillipede")));
            var subject = reading.Monsters.Single(monster => monster.Id == "MONSTER.TEST_SUBJECT");
            Assert.Equal(3, subject.Wiki.Count);
            Assert.DoesNotContain(subject.Quantities, value => value.Mismatch);
            var phrog = reading.Encounters.Single(encounter => encounter.Id == "ENCOUNTER.PHROG_PARASITE_ELITE");
            Assert.Equal(4, Assert.Single(Assert.Single(phrog.ReviewedRule!.LaterPhases).Roster).Count);
            Assert.Equal(CatalogueStanding.Catalogued, phrog.Standing);
            Assert.Equal(CatalogueStanding.Catalogued, reading.Monsters.Single(monster => monster.Id == "MONSTER.NIBBIT").Standing);
            Assert.Contains(reading.Coverage, row => row.Kind == "wiki-encounter");
            Assert.Contains(reading.Coverage, row => row.Kind == "wiki-enemy" && row.Id == "Doormaker" && row.Standing == CatalogueStanding.Unsupported);
            Assert.Contains(reading.Monsters, monster => monster.Id == "MONSTER.SCROLL_OF_BITING" && monster.MayBeOutdated);
            Evidence($"catalogue-a{ascension}", reading);
        });
    }

    [GameFact]
    public void FullCatalogueKeepsWikiAndLiveValuesAndFlagsAnEditedUnreviewedNumber()
    {
        WithRun(9, ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"], _ =>
        {
            var wiki = WikiCatalogue.Read();
            var edited = wiki with
            {
                Enemies = wiki.Enemies.Select(enemy => enemy.Name != "Waterfall Giant" ? enemy : enemy with
                {
                    AscHp = "999",
                    Moves = enemy.Moves.Select(move => move.Name != "Stomp" ? move : move with
                    {
                        Quantities = move.Quantities.Select(quantity => quantity with { Amount = new(999) }).ToArray(),
                    }).ToArray(),
                }).ToArray(),
            };
            var monster = RuntimeAdapter.ReadCatalogue(edited).Monsters.Single(row => row.Id == "MONSTER.WATERFALL_GIANT");
            Assert.True(monster.MayBeOutdated);
            Assert.NotNull(monster.Warning);
            Assert.Equal(CatalogueStanding.Flagged, monster.Standing);
            Assert.Contains(monster.Quantities, quantity => quantity.Wiki == 999 && quantity.Game == 250 && quantity.Mismatch);
            Assert.Contains(monster.Moves.SelectMany(move => move.Quantities), quantity => quantity.Wiki == 999 && quantity.Game == 16 && quantity.Mismatch);
            Assert.Contains(monster.Moves.SelectMany(move => move.Quantities), quantity => quantity.Wiki == 999 && quantity.Authority == QuantityAuthority.ConstructedGraph && quantity.Mismatch);
            Evidence("catalogue-edited-number", monster);
        });
    }

    [GameFact]
    public void MissingEnemyAndUnknownMoveBindingAreNamedRatherThanSilentlyEmpty()
    {
        WithRun(0, ["ACT.OVERGROWTH", "ACT.HIVE", "ACT.GLORY"], _ =>
        {
            var wiki = WikiCatalogue.Read();
            var edited = wiki with
            {
                Enemies = wiki.Enemies.Where(enemy => enemy.Name != "Waterfall Giant").Select(enemy => enemy.Name != "Rocket" ? enemy : enemy with
                {
                    Moves = enemy.Moves.Select(move => move with { Name = "Unrecognized wiki move" }).ToArray(),
                }).ToArray(),
            };
            var reading = RuntimeAdapter.ReadCatalogue(edited);
            var missing = reading.Monsters.Single(row => row.Id == "MONSTER.WATERFALL_GIANT");
            Assert.Equal(CatalogueStanding.Unsupported, missing.Standing);
            Assert.NotEmpty(missing.Gaps);
            var rocket = reading.Monsters.Single(row => row.Id == "MONSTER.ROCKET");
            Assert.NotEmpty(rocket.Moves);
            Assert.All(rocket.Moves, move => Assert.NotEmpty(move.Gaps));
            Assert.Empty(rocket.Moves.SelectMany(move => move.Quantities));
        });
    }
}

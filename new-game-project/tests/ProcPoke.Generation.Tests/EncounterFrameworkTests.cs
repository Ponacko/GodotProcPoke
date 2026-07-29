using ProcPoke.Generation;
using ProcPoke.Generation.Encounters;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// Tickets 6a/6b: encounter table shapes, filled regional pools, base wild levels, and the shared
/// <see cref="LevelCurve"/>.
/// </summary>
public class EncounterFrameworkTests
{
    private static GeneratedRegion Generate(ulong seed, int badges = 8)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);

    private static readonly HashSet<AreaArchetype> LandArchetypes =
    [
        AreaArchetype.Route, AreaArchetype.Forest, AreaArchetype.StandardCave,
        AreaArchetype.MountainPath, AreaArchetype.DeepCave, AreaArchetype.VictoryRoad, AreaArchetype.Tower,
    ];
    private static readonly HashSet<AreaArchetype> NoWild =
        [AreaArchetype.StartTown, AreaArchetype.Town, AreaArchetype.League, AreaArchetype.VillainHideout];

    [Theory]
    [InlineData(4, 14)]
    [InlineData(8, 14)]
    [InlineData(12, 14)]
    public void GymAceRunsFromFourteenToFifty(int gymCount, int firstAce)
    {
        Assert.Equal(firstAce, LevelCurve.GymAce(0, gymCount));
        Assert.Equal(50, LevelCurve.GymAce(gymCount - 1, gymCount));
        for (var i = 1; i < gymCount; i++)
            Assert.True(LevelCurve.GymAce(i, gymCount) >= LevelCurve.GymAce(i - 1, gymCount), "GymAce must be non-decreasing");
    }

    [Fact]
    public void GymAceSingleGymIsFifty() => Assert.Equal(50, LevelCurve.GymAce(0, 1));

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void TableShapesAndLevelsAcrossTheCorpus(int badges)
    {
        var checkedCount = 0;
        var waterSeen = 0;

        for (ulong seed = 1; seed <= 200; seed++)
        {
            var region = Generate(seed, badges);
            var enc = region.Encounters;
            var waterGated = region.Gating.BiomeRequirements
                .Where(r => r.Tag is "water" or "deep-water" or "sea").Select(r => r.AreaId).ToHashSet();

            foreach (var area in region.Graph.Areas)
            {
                var entry = enc.Of(area.Id);
                if (NoWild.Contains(area.Archetype))
                {
                    Assert.Null(entry); // no wild tables for towns/League/hideouts
                    continue;
                }

                Assert.NotNull(entry);
                var methods = entry!.Tables.Select(t => t.Method).ToList();

                // Every non-town area is a Land archetype in our model, so it always has a Land table.
                Assert.Contains(AreaArchetype.Route, LandArchetypes); // sanity of the fixture
                Assert.True(LandArchetypes.Contains(area.Archetype), $"unexpected wild archetype {area.Archetype}");
                Assert.Contains(EncounterMethod.Land, methods);

                var isWater = region.Biomes.Of(area.Id) == Biomes.Biome.Water || waterGated.Contains(area.Id);
                if (isWater)
                {
                    Assert.Contains(EncounterMethod.Surf, methods);
                    Assert.Contains(EncounterMethod.Fishing, methods);
                    waterSeen++;
                }
                else
                {
                    Assert.DoesNotContain(EncounterMethod.Surf, methods);
                    Assert.DoesNotContain(EncounterMethod.Fishing, methods);
                }

                foreach (var table in entry.Tables)
                {
                    var layout = table.Method switch
                    {
                        EncounterMethod.Land => SlotLayouts.Land,
                        EncounterMethod.Surf => SlotLayouts.Surf,
                        EncounterMethod.Fishing => SlotLayouts.Fishing,
                        _ => throw new ArgumentOutOfRangeException(),
                    };
                    Assert.Equal(layout, table.Slots.Select(s => s.Percent));
                    Assert.Equal(100, table.Slots.Sum(s => s.Percent));
                    Assert.Equal(0, table.HiddenAbilityChance);
                    Assert.All(table.Slots, slot =>
                    {
                        Assert.Contains(slot.SpeciesId, region.Dex.Entries.Select(e => e.SpeciesId));
                        Assert.DoesNotContain(slot.SpeciesId, region.Dex.FossilFamilySpecies);
                        Assert.False(TestData.Data.Species[slot.SpeciesId].IsLegendary);
                        Assert.False(TestData.Data.Species[slot.SpeciesId].IsMythical);
                        // Stated as equalities: these are exact, and as an InRange the lower bound overtook
                        // the upper one as soon as the wild curve reached down to level 3.
                        Assert.Equal(Math.Max(2, entry.BaseLevel - 2), slot.MinLevel);
                        Assert.Equal(entry.BaseLevel + 2, slot.MaxLevel);
                    });
                }

                var availability = AvailabilityOrder.Of(region.Graph, GatingGenerator.OffSpineAnchors(region.Graph));
                var fraction = availability.Count <= 1
                    ? 1
                    : availability.Select((id, index) => (id, index)).First(x => x.id == area.Id).index
                        / (availability.Count - 1.0);
                var shouldOverlay = area.Archetype is AreaArchetype.Route or AreaArchetype.Forest
                    && fraction >= 0.5;
                Assert.Equal(shouldOverlay, entry.Overlay is not null);
                if (entry.Overlay is not null)
                {
                    Assert.Equal(SlotLayouts.Land, entry.Overlay.Slots.Select(s => s.Percent));
                    Assert.Equal(0.5, entry.Overlay.HiddenAbilityChance);
                    Assert.All(entry.Overlay.Slots, slot =>
                    {
                        Assert.Equal(entry.BaseLevel + 3, slot.MinLevel);
                        Assert.Equal(entry.BaseLevel + 7, slot.MaxLevel);
                    });
                }
            }
            checkedCount++;
        }
        Assert.True(checkedCount > 0, "empty corpus — nothing was checked");
        Assert.True(waterSeen > 0, "no water area encountered — Surf/Fishing branch never tested");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void WildLevelIsNonDecreasingAlongAvailability(int badges)
    {
        // With no jitter yet, the base curve (with the dungeon +2 removed) is non-decreasing along
        // first-availability order.
        var checkedCount = 0;

        for (ulong seed = 1; seed <= 200; seed++)
        {
            var region = Generate(seed, badges);
            var order = AvailabilityOrder.Of(region.Graph, GatingGenerator.OffSpineAnchors(region.Graph));
            var prev = 0;
            foreach (var areaId in order)
            {
                var entry = region.Encounters.Of(areaId);
                if (entry is null) continue;
                // Ask the curve which archetypes it bumps rather than restating the set here.
                var archetype = region.Graph[areaId].Archetype;
                var baseLevel = entry.BaseLevel - (LevelCurve.HasDungeonBump(archetype) ? 2 : 0);
                Assert.True(baseLevel >= prev,
                    $"seed {seed}/{badges}: area {areaId} base level {baseLevel} < previous {prev}");
                prev = baseLevel;
            }
            checkedCount++;
        }
        Assert.True(checkedCount > 0, "empty corpus — nothing was checked");
    }

    [Fact]
    public void SlotLayoutsSumToOneHundred()
    {
        Assert.Equal(100, SlotLayouts.Land.Sum());
        Assert.Equal(12, SlotLayouts.Land.Count);
        Assert.Equal(100, SlotLayouts.Surf.Sum());
        Assert.Equal(100, SlotLayouts.Fishing.Sum());
    }

    [Fact]
    public void EncountersAreDeterministic()
    {
        foreach (var seed in new ulong[] { 7, 42, 999 })
        {
            var a = Generate(seed).Encounters;
            var b = Generate(seed).Encounters;
            Assert.Equal(a.ByArea.Keys.OrderBy(k => k), b.ByArea.Keys.OrderBy(k => k));
            foreach (var id in a.ByArea.Keys)
            {
                Assert.Equal(a.ByArea[id].BaseLevel, b.ByArea[id].BaseLevel);
                Assert.Equal(a.ByArea[id].Tables.Select(t => t.Method), b.ByArea[id].Tables.Select(t => t.Method));
                Assert.Equal(a.ByArea[id].Tables.Select(TableSignature), b.ByArea[id].Tables.Select(TableSignature));
                Assert.Equal(TableSignature(a.ByArea[id].Overlay), TableSignature(b.ByArea[id].Overlay));
            }
        }
    }

    private static string? TableSignature(EncounterTable? table)
        => table is null
            ? null
            : $"{table.Method}|{table.HiddenAbilityChance}|{string.Join(',', table.Slots.Select(s => $"{s.Percent}/{s.SpeciesId}/{s.MinLevel}/{s.MaxLevel}"))}";
}

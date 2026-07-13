using ProcPoke.Generation;
using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// Biome-pass invariants (ADR-0004): every area is biomed, terrain-bound gates are honoured as fixed
/// points, and dungeon archetypes carry their expected terrain.
/// </summary>
public class BiomeTests
{
    private static GeneratedRegion Generate(ulong seed, int badges = 8)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges });

    [Fact]
    public void EveryAreaHasABiome()
    {
        var region = Generate(42);
        foreach (var area in region.Graph.Areas)
            Assert.True(region.Biomes.ByArea.ContainsKey(area.Id));
    }

    [Fact]
    public void DungeonArchetypesCarryExpectedBiomes()
    {
        var region = Generate(42);
        foreach (var area in region.Graph.Areas)
        {
            var biome = region.Biomes.Of(area.Id);
            switch (area.Archetype)
            {
                case AreaArchetype.Forest: Assert.Equal(Biome.Forest, biome); break;
                case AreaArchetype.StandardCave or AreaArchetype.DeepCave: Assert.Equal(Biome.Cave, biome); break;
                case AreaArchetype.MountainPath or AreaArchetype.VictoryRoad: Assert.Equal(Biome.Mountain, biome); break;
            }
        }
    }

    [Fact]
    public void TownsAreUrban()
    {
        var region = Generate(42);
        foreach (var area in region.Graph.Areas.Where(a => a.Archetype is AreaArchetype.Town or AreaArchetype.StartTown))
            Assert.Equal(Biome.Urban, region.Biomes.Of(area.Id));
    }

    [Fact]
    public void GenerationIsDeterministicWithBiomes()
    {
        Assert.Equal(
            Generate(2026).Biomes.ByArea.OrderBy(kv => kv.Key).ToList(),
            Generate(2026).Biomes.ByArea.OrderBy(kv => kv.Key).ToList());
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void TerrainRequirementsAreAlwaysHonoured(int badges)
    {
        for (ulong seed = 1; seed <= 2000; seed++)
        {
            var region = Generate(seed, badges);

            // Every area biomed.
            Assert.Equal(region.Graph.Areas.Count, region.Biomes.ByArea.Count);

            // Every terrain-bound gate's fixed point is respected.
            foreach (var (areaId, tag) in region.Gating.BiomeRequirements)
            {
                var expected = tag is "desert" ? Biome.Desert : Biome.Water;
                Assert.Equal(expected, region.Biomes.Of(areaId));
            }
        }
    }
}

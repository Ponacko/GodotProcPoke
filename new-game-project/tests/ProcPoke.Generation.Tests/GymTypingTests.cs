using ProcPoke.Data;
using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Debug;
using ProcPoke.Generation.Identity;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>Gym/Elite Four/Champion typing invariants (§7.1, ticket 3b): gym types are distinct, each
/// gym's type traces back to its own or a neighbouring biome's §6.2 affinity, the Elite Four are distinct
/// from each other and the gyms, and the Champion is always typeless.</summary>
public class GymTypingTests
{
    private static GeneratedRegion Generate(ulong seed, int badges = 8)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);

    private static HashSet<PokeType> AffinityUnion(RegionGraph graph, BiomeMap biomes, int areaId)
    {
        var union = new HashSet<PokeType>(BiomeTypeAffinity.Table[biomes.Of(areaId)]);
        foreach (var neighbor in graph.Neighbors(areaId))
            union.UnionWith(BiomeTypeAffinity.Table[biomes.Of(neighbor.Id)]);
        return union;
    }

    [Fact]
    public void ChampionIsAlwaysTypeless()
    {
        Assert.True(Generate(42).Identity.ChampionIsTypeless);
    }

    [Fact]
    public void GenerationIsDeterministicWithIdentity()
    {
        Assert.Equal(
            Generate(2026).Identity.GymTypes.OrderBy(kv => kv.Key).ToList(),
            Generate(2026).Identity.GymTypes.OrderBy(kv => kv.Key).ToList());
        Assert.Equal(Generate(2026).Identity.EliteFourTypes, Generate(2026).Identity.EliteFourTypes);
    }

    [Fact]
    public void RegionGraphTextPrintsGymAndEliteFourTypes()
    {
        var region = Generate(42);
        var text = RegionGraphText.Render(region.Graph, region.Gating, region.Biomes, region.Names, region.Identity);
        Assert.Contains("Elite Four:", text);
        Assert.Contains("gym]", text);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void GymTypesAreDistinctAndTraceToBiomeAffinityUnlessExhausted(int badges)
    {
        // A gym's type must come from its own/neighbouring biome affinity UNLESS every candidate in that
        // union was already claimed by an earlier gym (the pass's documented fallback) — reconstruct the
        // "taken so far" set in the same critical-path order the pass assigns in, to tell a real affinity
        // hit apart from an unjustified one.
        var gymsChecked = 0;
        for (ulong seed = 1; seed <= 2000; seed++)
        {
            var region = Generate(seed, badges);
            var gymTypes = region.Identity.GymTypes.Values.ToList();
            Assert.Equal(gymTypes.Count, gymTypes.Distinct().Count());

            var gymCitiesInOrder = region.Graph.CriticalPath
                .Where(a => a.Archetype == AreaArchetype.Town).OrderBy(a => a.PathIndex).ToList();
            var takenSoFar = new HashSet<PokeType>();
            foreach (var city in gymCitiesInOrder)
            {
                var type = region.Identity.GymTypes[city.Id];
                var union = AffinityUnion(region.Graph, region.Biomes, city.Id);
                var tracesToAffinity = union.Contains(type);
                var affinityFullyExhausted = union.Count == 0 || union.All(takenSoFar.Contains);

                Assert.True(tracesToAffinity || affinityFullyExhausted,
                    $"seed {seed}/{badges}: gym at area {city.Id} assigned {type}, but affinity {string.Join(",", union)} " +
                    $"isn't exhausted (taken so far: {string.Join(",", takenSoFar)})");

                takenSoFar.Add(type);
                gymsChecked++;
            }
        }
        Assert.True(gymsChecked > 0, "no gyms were exercised across the corpus — nothing was tested");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void EliteFourAreDistinctFromEachOtherAndFromGyms(int badges)
    {
        for (ulong seed = 1; seed <= 2000; seed++)
        {
            var region = Generate(seed, badges);
            var e4 = region.Identity.EliteFourTypes;
            Assert.Equal(4, e4.Count);
            Assert.Equal(e4.Count, e4.Distinct().Count());
            Assert.Empty(e4.Intersect(region.Identity.GymTypes.Values));
        }
    }
}

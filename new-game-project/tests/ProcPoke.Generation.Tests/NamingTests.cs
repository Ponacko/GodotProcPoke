using ProcPoke.Generation.Debug;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>The naming pass invariants (GDD §8, ticket 3a): every city/dungeon is named, routes keep the
/// mainline <c>Route N</c> numbering in critical-path order, and no generated name ever matches the baked
/// blocklist or collides with another area in the same region.</summary>
public class NamingTests
{
    private static GeneratedRegion Generate(ulong seed, int badges = 8)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);

    [Fact]
    public void EveryAreaIsNamedAndRoutesAreNumberedInPathOrder()
    {
        var region = Generate(42);
        foreach (var area in region.Graph.Areas)
            Assert.True(region.Names.ByArea.ContainsKey(area.Id), $"area {area.Id} ({area.Archetype}) has no name");

        var routesInPathOrder = region.Graph.Areas
            .Where(a => a.Archetype == AreaArchetype.Route)
            .OrderBy(a => a.PathIndex)
            .ToList();
        for (var i = 0; i < routesInPathOrder.Count; i++)
            Assert.Equal($"Route {i + 1}", region.Names.Of(routesInPathOrder[i].Id));
    }

    [Fact]
    public void GenerationIsDeterministicWithNames()
    {
        Assert.Equal(
            Generate(2026).Names.ByArea.OrderBy(kv => kv.Key).ToList(),
            Generate(2026).Names.ByArea.OrderBy(kv => kv.Key).ToList());
        Assert.Equal(Generate(2026).Names.Motif, Generate(2026).Names.Motif);
    }

    [Fact]
    public void RegionGraphTextPrintsTheGeneratedNames()
    {
        var region = Generate(42);
        var text = RegionGraphText.Render(region.Graph, region.Gating, region.Biomes, region.Names);
        Assert.Contains(region.Names.Of(region.Graph.StartAreaId), text);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void NamesAreNeverBlockedAndNeverCollideWithinARegion(int badges)
    {
        var blocklist = TestData.Data.NameBlocklist.ToHashSet();
        var checkedCount = 0;
        for (ulong seed = 1; seed <= 2000; seed++)
        {
            var region = Generate(seed, badges);
            var byId = region.Graph.Areas.ToDictionary(a => a.Id);
            var seenInRegion = new HashSet<string>();
            foreach (var (areaId, name) in region.Names.ByArea)
            {
                // "Route N" is the mainline numbering convention, not a motif-blended name — it's exempt
                // from the blocklist (every official game already has a "Route 1").
                if (byId[areaId].Archetype != AreaArchetype.Route)
                    Assert.DoesNotContain(name, blocklist);
                Assert.True(seenInRegion.Add(name), $"seed {seed}/{badges}: duplicate name '{name}' within region");
                checkedCount++;
            }
        }
        Assert.True(checkedCount > 0, "no areas were named across the corpus — nothing was tested");
    }
}

using ProcPoke.Generation;
using ProcPoke.Generation.Debug;
using ProcPoke.Generation.Gating;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// The lock-and-key invariants (§4.3, §4.5, ADR-0002). The single-seed facts pin behaviour; the fuzz
/// theory asserts the solvability guarantee — the one that must never fail — over a broad seed corpus.
/// </summary>
public class GatingTests
{
    private static GeneratedRegion Generate(ulong seed, int badges = 8)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges });

    /// <summary>Number of gym towns reachable at or before a spine edge = badges available there.</summary>
    private static int TownsUpTo(RegionGraph g, int edge)
        => g.CriticalPath.Take(edge + 1).Count(a => a.Archetype == AreaArchetype.Town);

    /// <summary>The earliest critical-path index at which an area is reachable (its own index, or its anchor's).</summary>
    private static int EarliestReachable(RegionGraph g, int areaId)
    {
        var area = g[areaId];
        if (area.OnCriticalPath) return area.PathIndex;
        return g.Neighbors(areaId).Where(n => n.OnCriticalPath).Select(n => n.PathIndex).Min();
    }

    [Fact]
    public void GenerationIsDeterministic()
    {
        var a = RegionGraphText.Render(Generate(2026).Graph, Generate(2026).Gating);
        var b = RegionGraphText.Render(Generate(2026).Graph, Generate(2026).Gating);
        Assert.Equal(a, b);
    }

    [Fact]
    public void GuaranteedClassicsAlwaysAppear()
    {
        var classes = Generate(42).Gating.Gates.Select(g => g.Obstacle).ToHashSet();
        Assert.Contains(ObstacleClass.CutTree, classes);
        Assert.Contains(ObstacleClass.SurfWater, classes);
        Assert.Contains(ObstacleClass.Boulder, classes);
    }

    [Fact]
    public void GateBudgetScalesWithRegionSize()
    {
        // ~1 gate per 1–1.5 route segments; ~9–10 at the default 8 badges (§4.3 rule 5).
        Assert.InRange(Generate(42, 8).Gating.Gates.Count, 8, 12);
        Assert.True(Generate(42, 12).Gating.Gates.Count >= Generate(42, 4).Gating.Gates.Count);
    }

    [Fact]
    public void EachObstacleClassAppearsAtMostOnce()
    {
        var classes = Generate(7).Gating.Gates.Select(g => g.Obstacle).ToList();
        Assert.Equal(classes.Count, classes.Distinct().Count());
    }

    [Fact]
    public void KeysAreReachableStrictlyBeforeTheirGate()
    {
        var region = Generate(42);
        foreach (var gate in region.Gating.Gates)
            Assert.True(EarliestReachable(region.Graph, gate.KeyAreaId) <= gate.BlockPathIndex,
                $"{gate.KeyName}'s key is not reachable before its gate");
    }

    [Fact]
    public void HmBadgePrerequisitesAreEarnableBeforeTheirGate()
    {
        var region = Generate(42);
        foreach (var gate in region.Gating.Gates.Where(g => g.BadgePrerequisite is not null))
            Assert.True(gate.BadgePrerequisite <= TownsUpTo(region.Graph, gate.BlockPathIndex),
                $"{gate.KeyName} needs badge {gate.BadgePrerequisite} but too few gyms precede its gate");
    }

    [Fact]
    public void ItemKeysCarryNoBadgePrerequisite()
    {
        foreach (var gate in Generate(42).Gating.Gates.Where(g => g.KeyKind == KeyKind.KeyItem))
            Assert.Null(gate.BadgePrerequisite);
    }

    [Fact]
    public void TerrainBoundObstaclesEmitBiomeRequirements()
    {
        var region = Generate(42);
        var reqs = region.Gating.BiomeRequirements.ToList();
        // Surf is guaranteed and water-bound, so at least one requirement must exist.
        Assert.NotEmpty(reqs);
        foreach (var (areaId, tag) in reqs)
        {
            Assert.False(string.IsNullOrEmpty(tag));
            Assert.Contains(region.Graph.Areas, a => a.Id == areaId);
        }
    }

    [Fact]
    public void FlyIsPlacedButIsNotAGate()
    {
        var region = Generate(42);
        Assert.InRange(region.Gating.Fly.BadgePrerequisite, 1, 8);
        // Fly is a travel utility, never an obstacle key.
        Assert.DoesNotContain(region.Gating.Gates, g => g.KeyName == ObstacleTable.FlyKeyName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    public void EverySeedIsSolvable(int badges)
    {
        // The guarantee that must never break. Also proves no circular locks / unreachable keys —
        // any such flaw stalls the validator's fixpoint short of the League.
        for (ulong seed = 1; seed <= 2000; seed++)
        {
            var region = Generate(seed, badges);
            Assert.True(GatingValidator.IsSolvable(region.Graph, region.Gating),
                $"seed {seed}/{badges} is not solvable");

            foreach (var gate in region.Gating.Gates)
            {
                Assert.True(EarliestReachable(region.Graph, gate.KeyAreaId) <= gate.BlockPathIndex);
                if (gate.BadgePrerequisite is int b)
                    Assert.True(b <= TownsUpTo(region.Graph, gate.BlockPathIndex));
            }
        }
    }
}

using ProcPoke.Generation;
using ProcPoke.Generation.Debug;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// The topology-pass invariants (§4.2). The single-seed facts are spelled out; the fuzz theory then
/// asserts the structural invariants hold on a broad seed corpus across badge counts — the constructive
/// guarantee, checked empirically the way Phase 2's fuzz suite will at scale.
/// </summary>
public class TopologyTests
{
    private static RegionGraph Generate(ulong seed, int badges = 8)
    {
        var settings = new GenerationSettings { Seed = seed, BadgeCount = badges };
        return TopologyGenerator.Generate(settings, new RngStreams(seed));
    }

    private static int MaxConsecutivePlains(RegionGraph g)
    {
        var max = 0;
        var run = 0;
        foreach (var area in g.CriticalPath)
        {
            run = area.IsPlain ? run + 1 : 0;
            max = Math.Max(max, run);
        }
        return max;
    }

    [Fact]
    public void GenerationIsDeterministic()
    {
        var a = RegionGraphText.Render(Generate(2026));
        var b = RegionGraphText.Render(Generate(2026));
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentSeedsProduceDifferentRegions()
    {
        var a = RegionGraphText.Render(Generate(1));
        var b = RegionGraphText.Render(Generate(2));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void SpineStartsAtStartTownAndEndsAtLeague()
    {
        var g = Generate(7);
        var path = g.CriticalPath;
        Assert.Equal(AreaArchetype.StartTown, path[0].Archetype);
        Assert.Equal(AreaArchetype.League, path[^1].Archetype);
        Assert.Equal(g.StartAreaId, path[0].Id);
        Assert.Equal(g.LeagueAreaId, path[^1].Id);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void SpineHasOneTownPerBadge(int badges)
    {
        var g = Generate(123, badges);
        var towns = g.CriticalPath.Count(a => a.Archetype == AreaArchetype.Town);
        Assert.Equal(badges, towns);
    }

    [Fact]
    public void NeverMoreThanTwoConsecutivePlainAreas()
    {
        // §4.2's defining pacing mandate.
        Assert.True(MaxConsecutivePlains(Generate(42)) <= 2);
    }

    [Fact]
    public void HasBranchesAndAtLeastOneLoopBack()
    {
        var g = Generate(42);
        Assert.NotEmpty(g.OffSpineAreas);
        Assert.Contains(g.Connections, c => c.IsLoopBack);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    public void InvariantsHoldAcrossFuzzCorpus(int badges)
    {
        // Every seed in the corpus must satisfy the structural invariants by construction.
        for (ulong seed = 1; seed <= 2000; seed++)
        {
            var g = Generate(seed, badges);

            Assert.True(g.IsConnected(), $"seed {seed}/{badges}: not connected");
            Assert.True(MaxConsecutivePlains(g) <= 2, $"seed {seed}/{badges}: >2 consecutive plains");
            Assert.True(g.OffSpineAreas.Count > 0, $"seed {seed}/{badges}: no branches");
            Assert.Contains(g.Connections, c => c.IsLoopBack);
            Assert.Equal(AreaArchetype.StartTown, g.CriticalPath[0].Archetype);
            Assert.Equal(AreaArchetype.League, g.CriticalPath[^1].Archetype);
        }
    }
}

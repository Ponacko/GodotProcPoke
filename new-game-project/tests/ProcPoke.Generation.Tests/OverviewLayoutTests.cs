using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Gating;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// Overview-layout invariants (2b): every area lands on exactly one grid cell, no two areas share a cell,
/// the critical path runs left→right in path order, and every off-spine area sits adjacent (one row off)
/// its anchor's column.
/// </summary>
public class OverviewLayoutTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void EveryAreaPlacedExactlyOnceWithNoOverlap(int badges)
    {
        for (ulong seed = 1; seed <= 500; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges });
            var cells = OverviewLayout.Plan(region.Graph);

            Assert.Equal(region.Graph.Areas.Count, cells.Count);
            Assert.Equal(region.Graph.Areas.Select(a => a.Id).OrderBy(id => id),
                cells.Select(c => c.AreaId).OrderBy(id => id));

            var positions = cells.Select(c => (c.Col, c.Row)).ToList();
            Assert.Equal(positions.Count, positions.Distinct().Count());
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void CriticalPathRunsLeftToRightInPathOrder(int badges)
    {
        for (ulong seed = 1; seed <= 500; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges });
            var cells = OverviewLayout.Plan(region.Graph).ToDictionary(c => c.AreaId);

            foreach (var area in region.Graph.CriticalPath)
            {
                var cell = cells[area.Id];
                Assert.Equal(area.PathIndex, cell.Col);
                Assert.Equal(0, cell.Row);
            }
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void OffSpineAreaSharesItsAnchorsColumn(int badges)
    {
        var offSpineChecked = 0;
        for (ulong seed = 1; seed <= 500; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges });
            var cells = OverviewLayout.Plan(region.Graph).ToDictionary(c => c.AreaId);
            var anchors = GatingGenerator.OffSpineAnchors(region.Graph);

            foreach (var area in region.Graph.OffSpineAreas)
            {
                var cell = cells[area.Id];
                Assert.NotEqual(0, cell.Row); // off-spine areas never sit in the critical-path row
                if (anchors.TryGetValue(area.Id, out var anchorPathIndex))
                {
                    Assert.Equal(anchorPathIndex, cell.Col);
                    offSpineChecked++;
                }
            }
        }
        Assert.True(offSpineChecked > 0, "no off-spine area was exercised across the corpus — nothing was tested");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void EveryConnectionsEndpointsAreBothPlaced(int badges)
    {
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges });
            var placedIds = OverviewLayout.Plan(region.Graph).Select(c => c.AreaId).ToHashSet();

            foreach (var c in region.Graph.Connections)
            {
                Assert.Contains(c.AreaA, placedIds);
                Assert.Contains(c.AreaB, placedIds);
            }
        }
    }

    [Fact]
    public void DeterministicAcrossRepeatedCalls()
    {
        var region = RegionGenerator.Generate(new GenerationSettings { Seed = 42, BadgeCount = 8 });
        var first = OverviewLayout.Plan(region.Graph);
        var second = OverviewLayout.Plan(region.Graph);
        Assert.Equal(first, second);
    }
}

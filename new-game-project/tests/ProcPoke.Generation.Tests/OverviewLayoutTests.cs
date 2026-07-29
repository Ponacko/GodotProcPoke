using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Gating;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// Overview-layout invariants (2b). The layout is no longer invented here — it is the embedding the topology
/// pass chose (<c>Area.Cell</c>), so these tests check the read-out is faithful and, above all, that the
/// picture is *drawable*: every connection joins two areas that share a border.
/// <para>
/// The earlier version of this suite asserted the critical path ran along row 0 with one column per path
/// index, and that off-spine areas stacked above and below their anchor's column. Those assertions passed on
/// a layout that rendered every region as a single corridor running east and drew connections between areas
/// many columns apart — they pinned the defect in place, so they are gone.
/// </para>
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
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);
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
    public void ReportsTheCellTheTopologyPassChose(int badges)
    {
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);
            var cells = OverviewLayout.Plan(region.Graph).ToDictionary(c => c.AreaId);

            foreach (var area in region.Graph.Areas)
            {
                Assert.Equal(area.Cell.Col, cells[area.Id].Col);
                Assert.Equal(area.Cell.Row, cells[area.Id].Row);
            }
        }
    }

    /// <summary>
    /// The invariant the old layout could not hold: a connection means a shared border. This is what rules
    /// out a tower joined to both the 4th and the 9th area of the spine — a graph edge no 2-D layout can draw.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void EveryConnectionJoinsAreasOnAdjacentCells(int badges)
    {
        for (ulong seed = 1; seed <= 500; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);
            var cells = OverviewLayout.Plan(region.Graph).ToDictionary(c => c.AreaId);

            foreach (var c in region.Graph.Connections)
            {
                var (a, b) = (cells[c.AreaA], cells[c.AreaB]);
                var distance = Math.Abs(a.Col - b.Col) + Math.Abs(a.Row - b.Row);
                Assert.True(distance == 1,
                    $"seed {seed}/{badges}: areas {c.AreaA} ({a.Col},{a.Row}) and {c.AreaB} ({b.Col},{b.Row}) "
                    + $"are connected but {distance} cells apart — the connection cannot be laid out");
            }
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void OffSpineAreaBordersItsAnchor(int badges)
    {
        var offSpineChecked = 0;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);
            var anchors = GatingGenerator.OffSpineAnchors(region.Graph);

            foreach (var area in region.Graph.OffSpineAreas)
            {
                Assert.True(anchors.ContainsKey(area.Id),
                    $"seed {seed}/{badges}: off-spine area {area.Id} has no critical-path neighbour");

                var anchor = region.Graph.CriticalPath.First(a => a.PathIndex == anchors[area.Id]);
                Assert.True(area.Cell.IsAdjacentTo(anchor.Cell),
                    $"seed {seed}/{badges}: area {area.Id} at {area.Cell} does not border its anchor {anchor.Cell}");
                offSpineChecked++;
            }
        }
        Assert.True(offSpineChecked > 0, "no off-spine area was exercised across the corpus — nothing was tested");
    }

    [Fact]
    public void DeterministicAcrossRepeatedCalls()
    {
        var region = RegionGenerator.Generate(new GenerationSettings { Seed = 42, BadgeCount = 8 }, TestData.Data);
        var first = OverviewLayout.Plan(region.Graph);
        var second = OverviewLayout.Plan(region.Graph);
        Assert.Equal(first, second);
    }
}

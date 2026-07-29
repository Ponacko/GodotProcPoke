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

    // ---- spatial invariants -------------------------------------------------

    /// <summary>Consecutive spine areas must share a border, or the spine cannot be walked on a 2-D map.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public void SpineStepsBetweenBorderingCells(int badges)
    {
        for (ulong seed = 1; seed <= 1000; seed++)
        {
            var path = Generate(seed, badges).CriticalPath;
            for (var i = 0; i < path.Count - 1; i++)
                Assert.True(path[i].Cell.IsAdjacentTo(path[i + 1].Cell),
                    $"seed {seed}/{badges}: path {i} at {path[i].Cell} does not border {i + 1} at {path[i + 1].Cell}");
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public void NoTwoAreasShareACellAndEveryConnectionBorders(int badges)
    {
        for (ulong seed = 1; seed <= 1000; seed++)
        {
            var g = Generate(seed, badges);

            var cells = g.Areas.Select(a => a.Cell).ToList();
            Assert.Equal(cells.Count, cells.Distinct().Count());

            foreach (var c in g.Connections)
                Assert.True(g[c.AreaA].Cell.IsAdjacentTo(g[c.AreaB].Cell),
                    $"seed {seed}/{badges}: connection {c.AreaA}–{c.AreaB} spans "
                    + $"{g[c.AreaA].Cell.ManhattanTo(g[c.AreaB].Cell)} cells");
        }
    }

    /// <summary>
    /// The region must occupy two dimensions rather than reading as one long corridor in a single direction.
    /// The spine's bounding box has to be at least 2 cells on both axes, and the walk has to change heading
    /// at least twice — for badge counts of 4 and up the box the walk is bounded to makes a straight or
    /// single-elbow spine impossible, so this is a construction guarantee, not a statistical one.
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    [InlineData(16)]
    public void SpineWandersInTwoDimensions(int badges)
    {
        for (ulong seed = 1; seed <= 1000; seed++)
        {
            var path = Generate(seed, badges).CriticalPath;
            var cells = path.Select(a => a.Cell).ToList();

            var colExtent = cells.Max(c => c.Col) - cells.Min(c => c.Col) + 1;
            var rowExtent = cells.Max(c => c.Row) - cells.Min(c => c.Row) + 1;
            Assert.True(colExtent >= 2 && rowExtent >= 2,
                $"seed {seed}/{badges}: spine bounding box is {colExtent}x{rowExtent} — a straight line");

            var headings = new List<Heading>();
            for (var i = 0; i < cells.Count - 1; i++) headings.Add(cells[i].HeadingTo(cells[i + 1]));
            var changes = headings.Where((h, i) => i > 0 && h != headings[i - 1]).Count();
            Assert.True(changes >= 2, $"seed {seed}/{badges}: spine changes heading only {changes} time(s)");
        }
    }

    /// <summary>
    /// Regions should not all wander the same way. Across a corpus the spine's shape has to actually vary —
    /// several distinct bounding boxes, and both axes used as the long one.
    /// </summary>
    [Fact]
    public void SpineShapeVariesAcrossSeeds()
    {
        var shapes = new HashSet<(int Cols, int Rows)>();
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var cells = Generate(seed).CriticalPath.Select(a => a.Cell).ToList();
            shapes.Add((cells.Max(c => c.Col) - cells.Min(c => c.Col) + 1,
                cells.Max(c => c.Row) - cells.Min(c => c.Row) + 1));
        }

        Assert.True(shapes.Count >= 4, $"only {shapes.Count} distinct spine bounding boxes across 300 seeds");
        Assert.Contains(shapes, s => s.Cols > s.Rows);
        Assert.Contains(shapes, s => s.Rows > s.Cols);
    }
}

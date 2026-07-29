using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Debug;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

public class TownCarverTests
{
    [Fact]
    public void FirstTownLayoutsVaryAcrossSeeds()
    {
        var layouts = new HashSet<string>();
        for (ulong seed = 1; seed <= 50; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = 8 }, TestData.Data);
            var town = region.Graph.CriticalPath.First(a => a.Archetype is AreaArchetype.StartTown or AreaArchetype.Town);
            layouts.Add(AsciiRenderer.Render(region.Carved[town.Id]));
        }
        Assert.True(layouts.Count >= 2, "first-town building layouts did not vary across the seed corpus");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void TownBuildingDoorsAreReachableFromTheSpine(int badges)
    {
        var checkedCount = 0;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);
            foreach (var area in region.Graph.Areas.Where(a => a.Archetype is AreaArchetype.StartTown or AreaArchetype.Town))
            {
                var carved = region.Carved[area.Id];
                var spine = Reachable(carved.Grid, (1, carved.Grid.Height / 2));
                var doors = FindInteriorWarps(carved.Grid);
                checkedCount++;
                Assert.NotEmpty(doors);
                foreach (var door in doors)
                    Assert.Contains(door, spine);
            }
        }
        Assert.True(checkedCount > 0, "no town areas were exercised across the corpus");
    }

    private static IReadOnlyList<(int X, int Y)> FindInteriorWarps(TileGrid grid)
    {
        var warps = new List<(int X, int Y)>();
        for (var y = 1; y < grid.Height - 1; y++)
            for (var x = 1; x < grid.Width - 1; x++)
                if (grid[x, y] == LogicalTile.Warp) warps.Add((x, y));
        return warps;
    }

    private static HashSet<(int X, int Y)> Reachable(TileGrid grid, (int X, int Y) start)
    {
        var seen = new HashSet<(int X, int Y)> { start };
        var queue = new Queue<(int X, int Y)>([start]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in Neighbours(current))
                if (grid.InBounds(next.X, next.Y) && grid[next.X, next.Y].IsWalkable() && seen.Add(next)) queue.Enqueue(next);
        }
        return seen;
    }

    private static IEnumerable<(int X, int Y)> Neighbours((int X, int Y) cell)
    {
        yield return (cell.X + 1, cell.Y);
        yield return (cell.X - 1, cell.Y);
        yield return (cell.X, cell.Y + 1);
        yield return (cell.X, cell.Y - 1);
    }
}

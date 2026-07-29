using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

public class CaveCarverTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void CavesHaveRoomsItemsBouldersAndReachableTransitBends(int badges)
    {
        var checkedCaves = 0;
        var checkedTransitCaves = 0;

        for (ulong seed = 1; seed <= 200; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);
            foreach (var area in region.Graph.Areas.Where(a => a.Archetype is
                         AreaArchetype.StandardCave or AreaArchetype.MountainPath or AreaArchetype.VictoryRoad or
                         AreaArchetype.DeepCave))
            {
                var carved = region.Carved[area.Id];
                checkedCaves++;

                Assert.True(RoomComponents(carved.Grid) >= 2, $"seed {seed}/{badges} area {area.Id}: fewer than two rooms");
                Assert.Equal(1, carved.Grid.Count(LogicalTile.ItemBall));
                Assert.True(carved.Grid.Count(LogicalTile.Boulder) >= 3,
                    $"seed {seed}/{badges} area {area.Id}: fewer than three boulders");
                var item = Find(carved.Grid, LogicalTile.ItemBall);
                Assert.True(RoomMask(carved.Grid)[item.X, item.Y],
                    $"seed {seed}/{badges} area {area.Id}: item is outside a room");

                if (!area.OnCriticalPath || area.Archetype is not
                        (AreaArchetype.StandardCave or AreaArchetype.MountainPath or AreaArchetype.VictoryRoad))
                    continue;

                checkedTransitCaves++;
                var from = carved.Openings.First(o => o.X == 0);
                var to = carved.Openings.First(o => o.X == carved.Grid.Width - 1);
                var path = ShortestPath(carved, from, to);
                Assert.NotEmpty(path);
                Assert.Contains(path.Zip(path.Skip(1)), pair => pair.First.X != pair.Second.X);
                Assert.Contains(path.Zip(path.Skip(1)), pair => pair.First.Y != pair.Second.Y);
            }
        }

        Assert.True(checkedCaves > 0, "no cave areas were exercised across the corpus");
        Assert.True(checkedTransitCaves > 0, "no transit caves were exercised across the corpus");
    }

    private static bool[,] RoomMask(TileGrid grid)
    {
        var mask = new bool[grid.Width, grid.Height];
        for (var y = 1; y < grid.Height - 3; y++)
            for (var x = 1; x < grid.Width - 4; x++)
            {
                var open = true;
                for (var dy = 0; dy < 3 && open; dy++)
                    for (var dx = 0; dx < 4; dx++)
                        open &= grid[x + dx, y + dy].IsWalkable();
                if (!open) continue;
                for (var dy = 0; dy < 3; dy++)
                    for (var dx = 0; dx < 4; dx++)
                        mask[x + dx, y + dy] = true;
            }
        return mask;
    }

    private static int RoomComponents(TileGrid grid)
    {
        var mask = RoomMask(grid);
        var seen = new bool[grid.Width, grid.Height];
        var components = 0;
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                if (!mask[x, y] || seen[x, y]) continue;
                components++;
                var queue = new Queue<(int X, int Y)>([(x, y)]);
                seen[x, y] = true;
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    foreach (var next in Neighbours(current))
                        if (grid.InBounds(next.X, next.Y) && mask[next.X, next.Y] && !seen[next.X, next.Y])
                        {
                            seen[next.X, next.Y] = true;
                            queue.Enqueue(next);
                        }
                }
            }
        return components;
    }

    private static (int X, int Y) Find(TileGrid grid, LogicalTile tile)
    {
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
                if (grid[x, y] == tile) return (x, y);
        throw new InvalidOperationException($"tile {tile} not found");
    }

    private static IReadOnlyList<(int X, int Y)> ShortestPath(
        CarvedArea carved, (int X, int Y) from, (int X, int Y) to)
    {
        var parents = new Dictionary<(int X, int Y), (int X, int Y)> { [from] = from };
        var queue = new Queue<(int X, int Y)>([from]);
        var gateTiles = carved.GateTiles.ToHashSet();
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == to) break;
            foreach (var next in Neighbours(current))
            {
                if (!carved.Grid.InBounds(next.X, next.Y) || parents.ContainsKey(next)) continue;
                if (!carved.Grid[next.X, next.Y].IsWalkable() && !gateTiles.Contains(next)) continue;
                parents[next] = current;
                queue.Enqueue(next);
            }
        }

        if (!parents.ContainsKey(to)) return [];
        var path = new List<(int X, int Y)>();
        for (var current = to; ; current = parents[current])
        {
            path.Add(current);
            if (current == from) break;
        }
        path.Reverse();
        return path;
    }

    private static IEnumerable<(int X, int Y)> Neighbours((int X, int Y) cell)
    {
        yield return (cell.X + 1, cell.Y);
        yield return (cell.X - 1, cell.Y);
        yield return (cell.X, cell.Y + 1);
        yield return (cell.X, cell.Y - 1);
    }
}

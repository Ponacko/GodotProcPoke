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

                // Asserted against the chambers the carver actually built. Scanning the finished tiles for
                // open blocks instead reported two rooms as one whenever the corridor joining them fell
                // inside both scan windows — a property of the window, not of the cave.
                Assert.True(carved.Rooms.Count >= 2, $"seed {seed}/{badges} area {area.Id}: fewer than two rooms");
                foreach (var room in carved.Rooms)
                    Assert.True(room.Width >= 4 && room.Height >= 3,
                        $"seed {seed}/{badges} area {area.Id}: room {room} is too small to be a chamber");
                foreach (var (a, b) in carved.Rooms.SelectMany((r, i) => carved.Rooms.Skip(i + 1).Select(o => (r, o))))
                    Assert.False(a.Touches(b), $"seed {seed}/{badges} area {area.Id}: rooms {a} and {b} are not separate");

                Assert.Equal(1, carved.Grid.Count(LogicalTile.ItemBall));
                Assert.True(carved.Grid.Count(LogicalTile.Boulder) >= 3,
                    $"seed {seed}/{badges} area {area.Id}: fewer than three boulders");
                var item = Find(carved.Grid, LogicalTile.ItemBall);
                Assert.True(carved.Rooms.Any(r => r.Contains(item.X, item.Y)),
                    $"seed {seed}/{badges} area {area.Id}: item is outside a room");

                if (!area.OnCriticalPath || area.Archetype is not
                        (AreaArchetype.StandardCave or AreaArchetype.MountainPath or AreaArchetype.VictoryRoad))
                    continue;

                // The through-passage runs between the two spine openings, whichever borders those are —
                // a transit cave the spine crosses top-to-bottom has no left or right opening at all.
                var from = SpineOpenings.Entry(region, area);
                var to = SpineOpenings.Exit(region, area);
                if (from is null || to is null) continue;

                checkedTransitCaves++;
                var path = ShortestPath(carved, from.Value, to.Value);
                Assert.NotEmpty(path);
                Assert.Contains(path.Zip(path.Skip(1)), pair => pair.First.X != pair.Second.X);
                Assert.Contains(path.Zip(path.Skip(1)), pair => pair.First.Y != pair.Second.Y);
            }
        }

        Assert.True(checkedCaves > 0, "no cave areas were exercised across the corpus");
        Assert.True(checkedTransitCaves > 0, "no transit caves were exercised across the corpus");
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

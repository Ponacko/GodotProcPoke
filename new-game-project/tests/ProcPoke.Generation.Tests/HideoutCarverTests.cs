using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

public class HideoutCarverTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void HideoutsHaveFourRoomsBossLootAndReachableGrunts(int badges)
    {
        var checkedCount = 0;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);
            foreach (var area in region.Graph.Areas.Where(a => a.Archetype == AreaArchetype.VillainHideout))
            {
                var carved = region.Carved[area.Id];
                checkedCount++;
                Assert.Single(carved.Openings);
                Assert.True(RoomComponents(carved.Grid) >= 4, $"seed {seed}/{badges} area {area.Id}: fewer than four rooms");
                Assert.True(carved.Grid.Count(LogicalTile.TrainerPost) >= 3);
                Assert.Equal(1, carved.Grid.Count(LogicalTile.ItemBall));

                var entrance = carved.Openings[0];
                var reachable = Reachable(carved.Grid, entrance);
                foreach (var post in Find(carved.Grid, LogicalTile.TrainerPost))
                    Assert.Contains(post, reachable);
                Assert.Contains(Find(carved.Grid, LogicalTile.ItemBall)[0], reachable);

                var item = Find(carved.Grid, LogicalTile.ItemBall)[0];
                Assert.True(item.X >= 20 && item.X <= 25 && item.Y >= 2 && item.Y <= 5,
                    $"seed {seed}/{badges} area {area.Id}: item is not in the top-right boss room");
            }
        }
        Assert.True(checkedCount > 0, "no villain hideouts were exercised across the corpus");
    }

    private static bool[,] RoomMask(TileGrid grid)
    {
        var mask = new bool[grid.Width, grid.Height];
        for (var y = 1; y < grid.Height - 3; y++)
            for (var x = 1; x < grid.Width - 4; x++)
            {
                var open = true;
                for (var dy = 0; dy < 3 && open; dy++)
                    for (var dx = 0; dx < 4; dx++) open &= grid[x + dx, y + dy].IsWalkable();
                if (!open) continue;
                for (var dy = 0; dy < 3; dy++)
                    for (var dx = 0; dx < 4; dx++) mask[x + dx, y + dy] = true;
            }
        return mask;
    }

    private static int RoomComponents(TileGrid grid)
    {
        var mask = RoomMask(grid);
        var seen = new bool[grid.Width, grid.Height];
        var count = 0;
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
            {
                if (!mask[x, y] || seen[x, y]) continue;
                count++;
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
        return count;
    }

    private static IReadOnlyList<(int X, int Y)> Find(TileGrid grid, LogicalTile tile)
    {
        var found = new List<(int X, int Y)>();
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
                if (grid[x, y] == tile) found.Add((x, y));
        return found;
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

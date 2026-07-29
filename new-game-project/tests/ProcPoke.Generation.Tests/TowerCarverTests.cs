using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

public class TowerCarverTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void TowersHaveAlternatingStairGapsAndReachableTopReward(int badges)
    {
        var checkedCount = 0;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);
            foreach (var area in region.Graph.Areas.Where(a => a.Archetype == AreaArchetype.Tower))
            {
                var carved = region.Carved[area.Id];
                checkedCount++;
                Assert.Single(carved.Openings);

                var gaps = SeparatorGaps(carved.Grid);
                Assert.True(gaps.Count >= 2, $"seed {seed}/{badges} area {area.Id}: fewer than two stair walls");
                var bottomUp = gaps.OrderByDescending(g => g.Y).ToList();
                for (var i = 1; i < bottomUp.Count; i++)
                    Assert.NotEqual(bottomUp[i - 1].X > carved.Grid.Width / 2, bottomUp[i].X > carved.Grid.Width / 2);

                var item = Find(carved.Grid, LogicalTile.ItemBall);
                var post = Find(carved.Grid, LogicalTile.TrainerPost);
                var topWall = gaps.Min(g => g.Y);
                Assert.True(item.Y < topWall && post.Y < topWall, $"seed {seed}/{badges} area {area.Id}: reward is not in top band");
                Assert.Contains(item, Reachable(carved.Grid, carved.Openings[0]));
            }
        }
        Assert.True(checkedCount > 0, "no Tower areas were exercised across the corpus");
    }

    private static IReadOnlyList<(int X, int Y)> SeparatorGaps(TileGrid grid)
    {
        var gaps = new List<(int X, int Y)>();
        for (var y = 1; y < grid.Height - 1; y++)
        {
            var walkable = Enumerable.Range(1, grid.Width - 2).Where(x => grid[x, y].IsWalkable()).ToList();
            if (walkable.Count == 1 && Enumerable.Range(1, grid.Width - 2).All(x => x == walkable[0] || grid[x, y] == LogicalTile.Wall))
                gaps.Add((walkable[0], y));
        }
        return gaps;
    }

    private static (int X, int Y) Find(TileGrid grid, LogicalTile tile)
    {
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
                if (grid[x, y] == tile) return (x, y);
        throw new InvalidOperationException($"tile {tile} not found");
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

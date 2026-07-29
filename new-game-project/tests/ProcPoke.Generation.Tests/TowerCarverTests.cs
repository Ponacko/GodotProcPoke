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

                // The climb runs away from whichever border the entrance is on, so separators are lines of
                // constant canonical u — reading them off the grid's rows only worked while every tower was
                // entered from the bottom.
                var frame = EntranceFrame(carved);
                var gaps = SeparatorGaps(carved.Grid, frame);
                Assert.True(gaps.Count >= 2, $"seed {seed}/{badges} area {area.Id}: fewer than two stair walls");

                // Walking up from the entrance, consecutive stair gaps must sit on opposite sides.
                var fromEntrance = gaps.OrderByDescending(g => g.U).ToList();
                for (var i = 1; i < fromEntrance.Count; i++)
                    Assert.NotEqual(fromEntrance[i - 1].V > frame.V / 2, fromEntrance[i].V > frame.V / 2);

                var item = Find(carved.Grid, LogicalTile.ItemBall);
                var post = Find(carved.Grid, LogicalTile.TrainerPost);
                var lastWall = gaps.Min(g => g.U);
                Assert.True(frame.Unmap(item.X, item.Y).U < lastWall && frame.Unmap(post.X, post.Y).U < lastWall,
                    $"seed {seed}/{badges} area {area.Id}: reward is not in the top band");
                Assert.Contains(item, Reachable(carved.Grid, carved.Openings[0]));
            }
        }
        Assert.True(checkedCount > 0, "no Tower areas were exercised across the corpus");
    }

    /// <summary>The grid seen from the tower's entrance border, which is the axis the floors stack along.</summary>
    private static CarveFrame EntranceFrame(CarvedArea carved)
    {
        var (w, h) = (carved.Grid.Width, carved.Grid.Height);
        var (x, y) = carved.Openings[0];
        var edge = x == 0 ? EdgeSide.Left : x == w - 1 ? EdgeSide.Right : y == 0 ? EdgeSide.Top : EdgeSide.Bottom;
        return new CarveFrame(edge, w, h);
    }

    /// <summary>Canonical u lines that are solid wall but for a single walkable gap — the stair walls.</summary>
    private static IReadOnlyList<(int U, int V)> SeparatorGaps(TileGrid grid, CarveFrame f)
    {
        var gaps = new List<(int U, int V)>();
        for (var u = 1; u < f.U - 1; u++)
        {
            var span = Enumerable.Range(1, f.V - 2).ToList();
            var walkable = span.Where(v => f.Read(grid, u, v).IsWalkable()).ToList();
            if (walkable.Count == 1 && span.All(v => v == walkable[0] || f.Read(grid, u, v) == LogicalTile.Wall))
                gaps.Add((u, walkable[0]));
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

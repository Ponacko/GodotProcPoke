using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

public class LeagueCarverTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void LeagueHasFivePostsAndFourDoorwayGauntlet(int badges)
    {
        var checkedCount = 0;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);
            var league = region.Graph.Areas.Single(a => a.Archetype == AreaArchetype.League);
            var carved = region.Carved[league.Id];
            checkedCount++;

            Assert.Equal(5, carved.Grid.Count(LogicalTile.TrainerPost));
            var entrance = carved.Openings.Single(o => o.X == 0);
            var thronePost = FindPosts(carved.Grid).OrderBy(p => p.X).Last();
            var path = ShortestPath(carved.Grid, entrance, thronePost);
            Assert.NotEmpty(path);

            var doorwayCount = path.Count(tile => tile.X > 0 && tile.X < carved.Grid.Width - 1
                && carved.Grid[tile.X, tile.Y].IsWalkable()
                && carved.Grid[tile.X, tile.Y - 1] == LogicalTile.Wall
                && carved.Grid[tile.X, tile.Y + 1] == LogicalTile.Wall);
            Assert.True(doorwayCount >= 4, $"seed {seed}/{badges}: only {doorwayCount} League doorways on the throne path");
        }
        Assert.True(checkedCount > 0, "no League areas were exercised across the corpus");
    }

    private static IReadOnlyList<(int X, int Y)> FindPosts(TileGrid grid)
    {
        var posts = new List<(int X, int Y)>();
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
                if (grid[x, y] == LogicalTile.TrainerPost) posts.Add((x, y));
        return posts;
    }

    private static IReadOnlyList<(int X, int Y)> ShortestPath(
        TileGrid grid, (int X, int Y) from, (int X, int Y) to)
    {
        var parents = new Dictionary<(int X, int Y), (int X, int Y)> { [from] = from };
        var queue = new Queue<(int X, int Y)>([from]);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == to) break;
            foreach (var next in Neighbours(current))
            {
                if (!grid.InBounds(next.X, next.Y) || parents.ContainsKey(next) || !grid[next.X, next.Y].IsWalkable())
                    continue;
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

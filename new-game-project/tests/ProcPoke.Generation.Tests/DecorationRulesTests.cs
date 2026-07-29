using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

public class DecorationRulesTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void RoutesAndForestsSatisfyDecorationRules(int badges)
    {
        var checkedRoutes = 0;
        var checkedForests = 0;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);
            foreach (var area in region.Graph.Areas.Where(a => a.Archetype is AreaArchetype.Route or AreaArchetype.Forest))
            {
                var carved = region.Carved[area.Id];
                var spineY = carved.Openings.First(o => o.X == carved.Grid.Width - 1).Y;
                var ledges = Find(carved.Grid, LogicalTile.Ledge);
                foreach (var ledge in ledges)
                {
                    Assert.True(ledge.Y > spineY, $"seed {seed}/{badges} area {area.Id}: ledge is not south of spine");
                    Assert.True(carved.Grid[ledge.X, ledge.Y - 1].IsWalkable());
                    Assert.True(carved.Grid[ledge.X, ledge.Y + 1].IsWalkable());
                }

                var item = Assert.Single(Find(carved.Grid, LogicalTile.ItemBall));
                Assert.NotEqual(spineY, item.Y);
                Assert.True(NonWalkableNeighbours(carved.Grid, item) >= 2,
                    $"seed {seed}/{badges} area {area.Id}: item is not in a nook");

                var posts = Find(carved.Grid, LogicalTile.TrainerPost);
                Assert.InRange(posts.Count, 1, 2);
                foreach (var post in posts)
                {
                    Assert.InRange(Math.Abs(post.Y - spineY), 0, 2);
                    Assert.True(HasSightline(carved.Grid, post, spineY),
                        $"seed {seed}/{badges} area {area.Id}: trainer has no spine sightline");
                }

                if (area.Archetype == AreaArchetype.Route) checkedRoutes++;
                else checkedForests++;
            }
        }
        Assert.True(checkedRoutes > 0, "no routes were exercised across the corpus");
        Assert.True(checkedForests > 0, "no forests were exercised across the corpus");
    }

    [Fact]
    public void GateApproachRestoresTownBuildingWalls()
    {
        var gatedTownCount = 0;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = 8 }, TestData.Data);
            foreach (var gate in region.Gating.Gates)
            {
                var area = region.Graph.CriticalPath.First(a => a.PathIndex == gate.BlockPathIndex);
                if (area.Archetype is not (AreaArchetype.StartTown or AreaArchetype.Town)) continue;
                var carved = AreaCarver.Carve(area, region.Biomes.Of(area.Id), new RngStreams(seed).Stream("carve", area.Id),
                    region.Openings, gate);
                gatedTownCount++;
                foreach (var wall in carved.BuildingWallTiles)
                    Assert.Equal(LogicalTile.Wall, carved.Grid[wall.X, wall.Y]);
            }
        }
        Assert.True(gatedTownCount > 0, "no gated town was exercised across the corpus");
    }

    private static IReadOnlyList<(int X, int Y)> Find(TileGrid grid, LogicalTile tile)
    {
        var found = new List<(int X, int Y)>();
        for (var y = 0; y < grid.Height; y++)
            for (var x = 0; x < grid.Width; x++)
                if (grid[x, y] == tile) found.Add((x, y));
        return found;
    }

    private static int NonWalkableNeighbours(TileGrid grid, (int X, int Y) cell)
        => new[] { (cell.X + 1, cell.Y), (cell.X - 1, cell.Y), (cell.X, cell.Y + 1), (cell.X, cell.Y - 1) }
            .Count(p => !grid.InBounds(p.Item1, p.Item2) || !grid[p.Item1, p.Item2].IsWalkable());

    private static bool HasSightline(TileGrid grid, (int X, int Y) post, int spineY)
    {
        if (post.Y == spineY) return true;
        for (var y = Math.Min(post.Y, spineY); y <= Math.Max(post.Y, spineY); y++)
            if (!grid[post.X, y].IsWalkable()) return false;
        return true;
    }
}

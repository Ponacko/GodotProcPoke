using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>Ticket 10: fixed-cell world composition, matched seams, and area-origin fidelity.</summary>
public class WorldCanvasTests
{
    private static GeneratedRegion Generate(ulong seed, int badges)
        => RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void CanvasUsesLargestFootprintAndCellOrigins(int badges)
    {
        var checkedCount = 0;
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var region = Generate(seed, badges);
            var world = region.World;
            var cells = OverviewLayout.Plan(region.Graph).ToDictionary(cell => cell.AreaId);
            var minCol = cells.Values.Min(cell => cell.Col);
            var minRow = cells.Values.Min(cell => cell.Row);

            Assert.Equal(region.Carved.Values.Max(area => area.Grid.Width), world.CellWidth);
            Assert.Equal(region.Carved.Values.Max(area => area.Grid.Height), world.CellHeight);
            Assert.Equal(world.Columns * world.CellWidth, world.Grid.Width);
            Assert.Equal(world.Rows * world.CellHeight, world.Grid.Height);

            foreach (var area in region.Graph.Areas)
            {
                var expected = new WorldOrigin(
                    (cells[area.Id].Col - minCol) * world.CellWidth,
                    (cells[area.Id].Row - minRow) * world.CellHeight);
                Assert.Equal(expected, world.OriginOf(area.Id));

                var source = region.Carved[area.Id].Grid;
                for (var y = 0; y < source.Height; y++)
                for (var x = 0; x < source.Width; x++)
                {
                    var actual = world.Grid[expected.X + x, expected.Y + y];
                    // An unpaired edge Warp is intentionally sealed in the world layer; the enclosed carver
                    // exposed only its entrance, so preserving that orphan would create a false crossing.
                    if (source[x, y] == LogicalTile.Warp && actual == LogicalTile.Wall) continue;
                    Assert.Equal(source[x, y], actual);
                }
            }

            checkedCount++;
        }

        Assert.True(checkedCount > 0, "empty corpus — nothing was checked");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void MatchedOpeningsCollapseToAdjacentWarpPairs(int badges)
    {
        var checkedCount = 0;
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var region = Generate(seed, badges);
            foreach (var connection in region.Graph.Connections)
            {
                var a = region.Graph[connection.AreaA];
                var b = region.Graph[connection.AreaB];
                var openingA = Opening(region, a.Id, b.Id);
                var openingB = Opening(region, b.Id, a.Id);
                var localA = openingA.TileOn(region.Carved[a.Id].Grid.Width, region.Carved[a.Id].Grid.Height);
                var localB = openingB.TileOn(region.Carved[b.Id].Grid.Width, region.Carved[b.Id].Grid.Height);
                var sourceA = region.Carved[a.Id].Grid[localA.X, localA.Y] == LogicalTile.Warp;
                var sourceB = region.Carved[b.Id].Grid[localB.X, localB.Y] == LogicalTile.Warp;
                if (!sourceA || !sourceB) continue;

                var boundaryA = Boundary(region, a, b, openingA.Offset);
                var boundaryB = Boundary(region, b, a, openingB.Offset);
                Assert.Equal(LogicalTile.Warp, region.World.Grid[boundaryA.X, boundaryA.Y]);
                Assert.Equal(LogicalTile.Warp, region.World.Grid[boundaryB.X, boundaryB.Y]);
                Assert.Equal(1, Math.Abs(boundaryA.X - boundaryB.X) + Math.Abs(boundaryA.Y - boundaryB.Y));
                checkedCount++;
            }
        }

        Assert.True(checkedCount > 0, "no matched world seams were produced across the corpus");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void WorldWalkingReachesEveryGeneratedArea(int badges)
    {
        var checkedCount = 0;
        for (ulong seed = 1; seed <= 100; seed++)
        {
            var region = Generate(seed, badges);
            var start = FirstWalkable(region, region.Graph.StartAreaId);
            var reachable = Reachable(region.World, start);

            foreach (var area in region.Graph.Areas)
            {
                var point = FirstWalkable(region, area.Id);
                if (!reachable.Contains(point))
                {
                    var links = region.Graph.ConnectionsOf(area.Id).Select(connection =>
                    {
                        var other = connection.Other(area.Id);
                        var opening = Opening(region, area.Id, other);
                        var local = opening.TileOn(region.Carved[area.Id].Grid.Width, region.Carved[area.Id].Grid.Height);
                        var neighborArea = region.Graph[other];
                        var boundary = Boundary(region, area, neighborArea, opening.Offset);
                        var boundaryTile = region.World.Grid[boundary.X, boundary.Y];
                        var neighborOpening = Opening(region, other, area.Id);
                        var neighborLocal = neighborOpening.TileOn(region.Carved[other].Grid.Width, region.Carved[other].Grid.Height);
                        var neighborSource = region.Carved[other].Grid[neighborLocal.X, neighborLocal.Y];
                        var neighborBoundary = Boundary(region, neighborArea, area, neighborOpening.Offset);
                        return $"{area.Id}->{other} {opening.Edge}@{opening.Offset} " +
                               $"tile={region.Carved[area.Id].Grid[local.X, local.Y]} " +
                               $"boundary=({boundary.X},{boundary.Y})/{boundaryTile}; " +
                               $"other={neighborSource}@({neighborBoundary.X},{neighborBoundary.Y})/" +
                               region.World.Grid[neighborBoundary.X, neighborBoundary.Y];
                    });
                    Assert.Fail($"seed {seed}/{badges}: area {area.Id} ({area.Archetype}) at {point} is not reachable; " +
                                $"reachable areas={string.Join(',', region.Graph.Areas.Where(other =>
                                    reachable.Contains(FirstWalkable(region, other.Id))).Select(other => other.Id))}; " +
                                string.Join(", ", links));
                }
            }

            checkedCount++;
        }

        Assert.True(checkedCount > 0, "empty corpus — nothing was checked");
    }

    [Fact]
    public void WorldCanvasIsDeterministic()
    {
        var first = Generate(999, 8).World;
        var second = Generate(999, 8).World;

        Assert.Equal(first.CellWidth, second.CellWidth);
        Assert.Equal(first.CellHeight, second.CellHeight);
        Assert.Equal(first.Columns, second.Columns);
        Assert.Equal(first.Rows, second.Rows);
        Assert.Equal(first.Origins, second.Origins);
        Assert.Equal(first.GateTiles, second.GateTiles);

        for (var y = 0; y < first.Grid.Height; y++)
        for (var x = 0; x < first.Grid.Width; x++)
            Assert.Equal(first.Grid[x, y], second.Grid[x, y]);
    }

    private static AreaOpening Opening(GeneratedRegion region, int areaId, int neighborId)
        => region.Openings.EdgesOf(areaId).Single(opening => opening.NeighborAreaId == neighborId);

    private static (int X, int Y) FirstWalkable(GeneratedRegion region, int areaId)
    {
        var origin = region.World.OriginOf(areaId);
        var carved = region.Carved[areaId];
        foreach (var connection in region.Graph.ConnectionsOf(areaId))
        {
            var opening = Opening(region, areaId, connection.Other(areaId));
            var local = opening.TileOn(carved.Grid.Width, carved.Grid.Height);
            if (carved.Grid[local.X, local.Y] == LogicalTile.Warp)
                return (origin.X + local.X, origin.Y + local.Y);
        }

        throw new InvalidOperationException($"area {areaId} has no carved map opening");
    }

    private static IReadOnlySet<(int X, int Y)> Reachable(WorldCanvasMap world, (int X, int Y) start)
    {
        var seen = new HashSet<(int X, int Y)> { start };
        var queue = new Queue<(int X, int Y)>([start]);
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            foreach (var next in new[] { (x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1) })
            {
                if (!world.Grid.InBounds(next.Item1, next.Item2) ||
                    (!world.Grid[next.Item1, next.Item2].IsWalkable() &&
                     !world.GateTiles.Contains(next))) continue;
                if (seen.Add(next)) queue.Enqueue(next);
            }
        }

        return seen;
    }

    private static (int X, int Y) Boundary(GeneratedRegion region, Area area, Area neighbor, int offset)
    {
        var origin = region.World.OriginOf(area.Id);
        return area.Cell.HeadingTo(neighbor.Cell) switch
        {
            Heading.East => (origin.X + region.World.CellWidth - 1, origin.Y + offset),
            Heading.West => (origin.X, origin.Y + offset),
            Heading.South => (origin.X + offset, origin.Y + region.World.CellHeight - 1),
            _ => (origin.X + offset, origin.Y),
        };
    }
}

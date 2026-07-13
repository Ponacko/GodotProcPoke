using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Debug;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// Route-carver invariants (ADR-0001): the spine is walkable end to end by construction, the area is
/// framed and connected to its neighbours by warps, and carving is deterministic.
/// </summary>
public class CarvingTests
{
    private static IEnumerable<CarvedArea> CarveRoutes(ulong seed, int badges = 8)
    {
        var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges });
        var streams = new RngStreams(seed);
        foreach (var route in region.Graph.CriticalPath.Where(a => a.Archetype == AreaArchetype.Route))
            yield return RouteCarver.Carve(route, region.Biomes.Of(route.Id), streams.Stream("carve", route.Id));
    }

    private static IEnumerable<CarvedArea> CarveAll(ulong seed, int badges = 8)
    {
        var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges });
        var streams = new RngStreams(seed);
        foreach (var area in region.Graph.Areas)
            yield return AreaCarver.Carve(area, region.Biomes.Of(area.Id), streams.Stream("carve", area.Id));
    }

    /// <summary>Breadth-first walkable reachability between two grid cells.</summary>
    private static bool Reachable(TileGrid g, (int X, int Y) from, (int X, int Y) to)
    {
        var seen = new bool[g.Width, g.Height];
        var q = new Queue<(int X, int Y)>();
        q.Enqueue(from);
        seen[from.X, from.Y] = true;
        int[] dx = [1, -1, 0, 0];
        int[] dy = [0, 0, 1, -1];

        while (q.Count > 0)
        {
            var (x, y) = q.Dequeue();
            if ((x, y) == to) return true;
            for (var d = 0; d < 4; d++)
            {
                int nx = x + dx[d], ny = y + dy[d];
                if (g.InBounds(nx, ny) && !seen[nx, ny] && g[nx, ny].IsWalkable())
                {
                    seen[nx, ny] = true;
                    q.Enqueue((nx, ny));
                }
            }
        }
        return false;
    }

    [Fact]
    public void CarvingIsDeterministic()
    {
        var a = CarveRoutes(2026).Select(AsciiRenderer.Render).ToList();
        var b = CarveRoutes(2026).Select(AsciiRenderer.Render).ToList();
        Assert.Equal(a, b);
    }

    [Fact]
    public void RoutesHaveTallGrassAndTwoOpenings()
    {
        foreach (var carved in CarveRoutes(42))
        {
            Assert.Equal(2, carved.Openings.Count);
            Assert.True(carved.Grid.Count(LogicalTile.TallGrass) > 0, "route has no wild-encounter grass");
            foreach (var (x, y) in carved.Openings)
                Assert.Equal(LogicalTile.Warp, carved.Grid[x, y]);
        }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void SpineIsWalkableEndToEnd(int badges)
    {
        // The chokepoint guarantee starts here: every carved route is traversable entry → exit.
        for (ulong seed = 1; seed <= 500; seed++)
            foreach (var carved in CarveRoutes(seed, badges))
                Assert.True(Reachable(carved.Grid, carved.Openings[0], carved.Openings[1]),
                    $"seed {seed}/{badges} area {carved.AreaId}: spine not walkable end to end");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void EveryCarvedAreaHasMutuallyReachableOpenings(int badges)
    {
        // Across all archetypes: a player entering any opening can reach every other opening / door —
        // no area strands its own connections, and every building door is reachable.
        for (ulong seed = 1; seed <= 400; seed++)
            foreach (var carved in CarveAll(seed, badges))
            {
                var first = carved.Openings[0];
                foreach (var opening in carved.Openings.Skip(1))
                    Assert.True(Reachable(carved.Grid, first, opening),
                        $"seed {seed}/{badges} area {carved.AreaId}: openings not mutually reachable");
            }
    }
}

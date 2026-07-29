using ProcPoke.Generation;
using ProcPoke.Generation.Carving;
using ProcPoke.Generation.Debug;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;
using Xunit;

namespace ProcPoke.Generation.Tests;

/// <summary>
/// Route-carver invariants (ADR-0001): the spine is walkable end to end by construction, the area is
/// framed and connected to its neighbours by warps, and carving is deterministic. Gates carved onto
/// chokepoints (ADR-0002) are verified in both modes: blocking while locked, passable once cleared.
/// </summary>
public class CarvingTests
{
    // The region now owns its carved maps (ticket 2c) — read them straight off region.Carved rather than
    // re-carving. CarvingIsDeterministic below still re-carves independently and asserts it matches.
    private static IEnumerable<CarvedArea> CarveRoutes(ulong seed, int badges = 8)
    {
        var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);
        foreach (var route in region.Graph.CriticalPath.Where(a => a.Archetype == AreaArchetype.Route))
            yield return region.Carved[route.Id];
    }

    private static IEnumerable<CarvedArea> CarveAll(ulong seed, int badges = 8)
    {
        var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);
        foreach (var area in region.Graph.Areas)
            yield return region.Carved[area.Id];
    }

    /// <summary>Breadth-first walkable reachability; tiles in <paramref name="cleared"/> count as passable.</summary>
    private static bool Reachable(TileGrid g, (int X, int Y) from, (int X, int Y) to,
        IReadOnlySet<(int X, int Y)>? cleared = null)
    {
        bool Passable(int x, int y) => g[x, y].IsWalkable() || (cleared?.Contains((x, y)) ?? false);
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
                if (g.InBounds(nx, ny) && !seen[nx, ny] && Passable(nx, ny))
                {
                    seen[nx, ny] = true;
                    q.Enqueue((nx, ny));
                }
            }
        }
        return false;
    }

    private static HashSet<(int X, int Y)> GateSet(CarvedArea a) => [.. a.GateTiles];

    [Fact]
    public void CarvingIsDeterministic()
    {
        // The region carves its maps inside the pipeline; an independent re-carve from the same seed's
        // "carve/<areaId>" stream must reproduce every area byte-for-byte (ADR-0005 stream isolation).
        const ulong seed = 2026;
        var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = 8 }, TestData.Data);
        var streams = new RngStreams(seed);
        foreach (var area in region.Graph.Areas)
        {
            var reCarved = AreaCarver.Carve(area, region.Biomes.Of(area.Id), streams.Stream("carve", area.Id),
                region.Openings, AreaCarver.GateOnExitOf(area, region.Gating),
                GateGeometry.SpineExitSideOf(region.Graph, area));
            Assert.Equal(AsciiRenderer.Render(region.Carved[area.Id]), AsciiRenderer.Render(reCarved));
        }
    }

    [Fact]
    public void RoutesHaveTallGrassAndAtLeastTwoOpenings()
    {
        // Every route carries its two spine (or spine/Warp) edges; a route anchoring a branch or
        // loop-back connection carries an extra opening on top of those (ADR-0001/2a edge alignment).
        foreach (var carved in CarveRoutes(42))
        {
            Assert.True(carved.Openings.Count >= 2, $"area {carved.AreaId}: fewer than two openings");
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
        // The structural spine — gates treated as cleared — is always traversable entry → exit.
        for (ulong seed = 1; seed <= 500; seed++)
            foreach (var carved in CarveRoutes(seed, badges))
                Assert.True(Reachable(carved.Grid, carved.Openings[0], carved.Openings[1], GateSet(carved)),
                    $"seed {seed}/{badges} area {carved.AreaId}: spine not walkable end to end (cleared)");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void EveryCarvedAreaHasMutuallyReachableOpenings(int badges)
    {
        // Across all archetypes: entering any opening, a player can reach every other opening / door once
        // gates are cleared — no area strands its own connections, every building door stays reachable.
        for (ulong seed = 1; seed <= 400; seed++)
            foreach (var carved in CarveAll(seed, badges))
            {
                var cleared = GateSet(carved);
                var first = carved.Openings[0];
                foreach (var opening in carved.Openings.Skip(1))
                    Assert.True(Reachable(carved.Grid, first, opening, cleared),
                        $"seed {seed}/{badges} area {carved.AreaId}: openings not mutually reachable");
            }
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void GatesBlockTheSpineUntilCleared(int badges)
    {
        // ADR-0001/0002 at tile granularity: on every gated area the exit is unreachable from the entry
        // while the gate tiles are walls, and reachable once they are cleared.
        var gatedSeen = 0;
        for (ulong seed = 1; seed <= 400; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = badges }, TestData.Data);
            foreach (var area in region.Graph.Areas)
            {
                var carved = region.Carved[area.Id];
                if (carved.GateTiles.Count == 0) continue;

                // The gated exit is the opening facing the next critical-path area — not whatever sits on the
                // right edge, which since the spine started wandering may be a branch the gate never blocks.
                var exit = SpineOpenings.Exit(region, area);
                Assert.NotNull(exit);

                var elsewhere = carved.Openings.Where(o => o != exit.Value).ToList();
                if (elsewhere.Count == 0) continue; // gated area with no other way in: nothing to walk from

                gatedSeen++;
                var cleared = GateSet(carved);
                foreach (var entry in elsewhere)
                {
                    Assert.False(Reachable(carved.Grid, entry, exit.Value),
                        $"seed {seed}/{badges} area {area.Id}: gate did not block the spine while locked");
                    Assert.True(Reachable(carved.Grid, entry, exit.Value, cleared),
                        $"seed {seed}/{badges} area {area.Id}: spine not passable once the gate is cleared");
                }
            }
        }
        Assert.True(gatedSeen > 0, "no gated areas were produced across the corpus — nothing was tested");
    }

    [Fact]
    public void GateObstacleTilesMatchTheObstacleClass()
    {
        // Every gate tile is the obstacle class's expected tile; terrain-bound water gates carve a full
        // water span (ADR-0004), so the gate reads as its own terrain rather than a lone misplaced tile.
        var seen = new HashSet<LogicalTile>();
        var waterSpans = 0;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed }, TestData.Data);
            foreach (var gate in region.Gating.Gates)
            {
                var area = region.Graph.CriticalPath.First(a => a.PathIndex == gate.BlockPathIndex);
                var carved = AreaCarver.Carve(area, region.Biomes.Of(area.Id),
                    new RngStreams(seed).Stream("carve", area.Id), region.Openings, gate,
                    GateGeometry.SpineExitSideOf(region.Graph, area));
                if (carved.GateTiles.Count == 0) continue;

                var expected = GateCarver.TileFor(gate.Obstacle);
                foreach (var (gx, gy) in carved.GateTiles)
                    Assert.Equal(expected, carved.Grid[gx, gy]);
                seen.Add(expected);

                if (expected == LogicalTile.Water)
                {
                    // A river is a span, not a point — every water gate clears as one contiguous crossing.
                    Assert.True(carved.GateTiles.Count > 1, "water gate should carve a span, not a single tile");
                    waterSpans++;
                }
            }
        }
        Assert.Contains(LogicalTile.CutTree, seen);   // Cut is guaranteed in every seed
        Assert.Contains(LogicalTile.Boulder, seen);   // Strength is guaranteed in every seed
        Assert.Contains(LogicalTile.Water, seen);     // Surf is guaranteed in every seed
        Assert.True(waterSpans > 0, "no terrain-bound water gate was exercised");
    }

    /// <summary>
    /// A carved edge opening must face an area that is actually there. The spine can now leave an area on
    /// any border, so a carver that always opens its Left and Right edges would punch walkable tiles onto a
    /// border with nothing behind it — a hole leading off the map.
    /// </summary>
    [Fact]
    public void CarvedOpeningsOnlyFaceRealNeighbours()
    {
        var offenders = new List<string>();
        for (ulong seed = 1; seed <= 60; seed++)
        {
            var region = RegionGenerator.Generate(new GenerationSettings { Seed = seed, BadgeCount = 8 }, TestData.Data);
            var byCell = region.Graph.Areas.ToDictionary(a => a.Cell, a => a.Id);

            foreach (var area in region.Graph.Areas)
            {
                var carved = region.Carved[area.Id];
                var (w, h) = (carved.Grid.Width, carved.Grid.Height);
                foreach (var (x, y) in carved.Openings)
                {
                    Heading? heading = x == 0 ? Heading.West : x == w - 1 ? Heading.East
                        : y == 0 ? Heading.North : y == h - 1 ? Heading.South : null;
                    if (heading is null) continue;
                    if (!byCell.ContainsKey(area.Cell.Step(heading.Value)))
                        offenders.Add($"seed {seed}: area {area.Id} ({area.Archetype}) opens {heading} onto nothing");
                }
            }
        }
        Assert.True(offenders.Count == 0,
            $"{offenders.Count} openings face no neighbour:\n  " + string.Join("\n  ", offenders.Take(12)));
    }
}

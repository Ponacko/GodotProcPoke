using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Carves a Route spine-first (ADR-0001): a guaranteed walkable corridor follows the entry-to-exit travel
/// axis in a <see cref="CarveFrame"/> and turns as an L when the area turns on the region lattice. Every
/// other opening reaches that protected corridor through a short stub, so all connections remain mutually
/// reachable. Decoration is then applied
/// outward under placement rules that read hand-crafted — tall grass straddles the path, tree clumps and a
/// ledge break up the flanks, a trainer watches the path, and an item hides in a pocket. Biome tints the
/// fill (forest = denser trees/grass, desert = sand, mountain = rock walls).
/// </summary>
public static class RouteCarver
{
    /// <summary>The grid plus the bits every opening/decoration helper below needs: which tiles are the
    /// guaranteed-connectivity spine (never overwritten by decoration) and what the open floor tile is.</summary>
    private sealed class Canvas(TileGrid grid, LogicalTile floor, int spineY)
    {
        public TileGrid Grid { get; } = grid;
        public LogicalTile Floor { get; } = floor;
        public int SpineY { get; } = spineY;
        public HashSet<(int X, int Y)> Protected { get; } = [];
    }

    public static CarvedArea Carve(
        Area area, Biome biome, Pcg32 rng, IReadOnlyList<AreaOpening> planned,
        EdgeSide? spineExit, EdgeSide? spineEntry = null)
    {
        var (w, h) = CarveKit.Dimensions(area.Size);
        var border = biome is Biome.Mountain or Biome.Cave ? LogicalTile.Wall : LogicalTile.Tree;
        var floor = biome == Biome.Desert ? LogicalTile.Sand : LogicalTile.Ground;

        var grid = new TileGrid(w, h, floor);

        // Border ring.
        for (var x = 0; x < w; x++) { grid[x, 0] = border; grid[x, h - 1] = border; }
        for (var y = 0; y < h; y++) { grid[0, y] = border; grid[w - 1, y] = border; }

        // TrunkRow remains the world-space decoration anchor for compatibility with the route decoration
        // contract. The actual protected travel corridor is axis-aware and may include a vertical leg.
        var trunkY = CarveKit.TrunkRow(planned, spineExit, h);
        var canvas = new Canvas(grid, floor, trunkY);

        // Only borders that face a neighbour are opened. TravelCorridor writes the entry→exit trunk in the
        // exit-oriented frame, including its turning leg, and connects branch openings to that trunk.
        var openings = TravelCorridor.Carve(
            grid, planned, spineEntry, spineExit, floor, canvas.Protected, halfWidth: 0);

        // Keep decoration off the line a gate barrier would wall over, or the item/post placed there is lost.
        foreach (var tile in CarveKit.BarrierLine(spineExit, w, h)) canvas.Protected.Add(tile);

        var nearOpening = OpeningNeighbourhood(openings);

        var grassPatches = biome == Biome.Forest ? 4 : 3;
        var treeClumps = biome == Biome.Forest ? 5 : biome == Biome.Desert ? 1 : 3;
        var spineY = canvas.SpineY;

        // Tall grass straddling the path.
        for (var i = 0; i < grassPatches; i++)
        {
            var cx = rng.NextInt(4, w - 4);
            var cy = spineY + rng.NextInt(-2, 3);
            Stamp(canvas, cx - 2, cy - 1, cx + 2, cy + 1, LogicalTile.TallGrass, allowCorridor: true);
        }

        // Tree/rock clumps on the flanks only (never on a protected connectivity tile).
        var clumpTile = biome is Biome.Mountain or Biome.Cave ? LogicalTile.Wall : LogicalTile.Tree;
        // The trunk row is wherever the neighbour's opening put it, so a flank can be too thin to hold a
        // clump. Use whichever side has room, and place none at all if neither does.
        var upperRoom = spineY - 1 > 2;
        var lowerRoom = h - 2 > spineY + 2;
        for (var i = 0; i < treeClumps && (upperRoom || lowerRoom); i++)
        {
            var wantUpper = rng.Chance(0.5);
            var upper = upperRoom && (wantUpper || !lowerRoom);
            var cy = upper ? rng.NextInt(2, spineY - 1) : rng.NextInt(spineY + 2, h - 2);
            var cx = rng.NextInt(3, w - 3);
            Stamp(canvas, cx - 1, cy - 1, cx + 1, cy, clumpTile, allowCorridor: false);
        }

        var ledgeRow = TravelCorridor.WorldSouthLedgeRow(grid, planned, spineEntry, spineExit);
        PlaceLedges(grid, canvas, rng, nearOpening, ledgeRow);
        DecorationKit.PlaceItemNook(grid, floor, clumpTile, spineY, rng, canvas.Protected, nearOpening);
        DecorationKit.PlaceTrainerPosts(grid, floor, clumpTile, spineY, 1 + rng.NextInt(2), rng,
            canvas.Protected, nearOpening);

        return new CarvedArea { AreaId = area.Id, Grid = grid, Openings = openings, TrunkRow = spineY };
    }

    /// <summary>Stamps a rectangle of a tile, staying inside the border and (optionally) off protected connectivity tiles.</summary>
    private static void Stamp(Canvas c, int x0, int y0, int x1, int y1, LogicalTile tile, bool allowCorridor)
    {
        var g = c.Grid;
        for (var y = Math.Max(1, y0); y <= Math.Min(g.Height - 2, y1); y++)
            for (var x = Math.Max(1, x0); x <= Math.Min(g.Width - 2, x1); x++)
                if (allowCorridor || !c.Protected.Contains((x, y))) g[x, y] = tile;
    }

    private static void PlaceLedges(
        TileGrid grid, Canvas canvas, Pcg32 rng, IReadOnlySet<(int X, int Y)> nearOpening, int? ledgeRow)
    {
        if (ledgeRow is null) return; // Purely vertical travel has no east-west trunk to decorate southward.
        var minY = Math.Max(canvas.SpineY + 2, ledgeRow.Value + 2);
        var maxY = Math.Min(grid.Height - 2, ledgeRow.Value + 4);
        if (minY > maxY) return; // An edge-aligned horizontal leg has no legal south flank.

        var runs = 1 + rng.NextInt(2);
        for (var run = 0; run < runs; run++)
        {
            var placed = false;
            for (var attempt = 0; attempt < 30 && !placed; attempt++)
            {
                var length = 4 + rng.NextInt(5);
                var y = rng.NextInt(minY, maxY + 1);
                var x0 = rng.NextInt(2, grid.Width - length - 1);
                var valid = true;
                for (var x = x0; x < x0 + length; x++)
                {
                    if (!grid[x, y].IsWalkable() || canvas.Protected.Contains((x, y)) || nearOpening.Contains((x, y))
                        || !grid[x, y - 1].IsWalkable() || !grid[x, y + 1].IsWalkable())
                    {
                        valid = false;
                        break;
                    }
                }
                if (!valid) continue;
                for (var x = x0; x < x0 + length; x++) grid[x, y] = LogicalTile.Ledge;
                placed = true;
            }

            if (!placed)
            {
                const int length = 4;
                for (var y = minY; y <= maxY && !placed; y++)
                    for (var x0 = 2; x0 <= grid.Width - length - 2 && !placed; x0++)
                    {
                        var valid = Enumerable.Range(x0, length)
                            .All(x => !canvas.Protected.Contains((x, y)) && !nearOpening.Contains((x, y)));
                        if (!valid) continue;
                        for (var x = x0; x < x0 + length; x++)
                        {
                            if (!canvas.Protected.Contains((x, y - 1))) grid[x, y - 1] = LogicalTile.Ground;
                            if (!canvas.Protected.Contains((x, y + 1))) grid[x, y + 1] = LogicalTile.Ground;
                            grid[x, y] = LogicalTile.Ledge;
                        }
                        placed = true;
                    }
            }
        }
    }

    private static HashSet<(int X, int Y)> OpeningNeighbourhood(IReadOnlyList<(int X, int Y)> openings)
    {
        var near = new HashSet<(int X, int Y)>();
        foreach (var (x, y) in openings)
            for (var dx = -1; dx <= 1; dx++)
                for (var dy = -1; dy <= 1; dy++) near.Add((x + dx, y + dy));
        return near;
    }
}

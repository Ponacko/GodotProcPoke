using ProcPoke.Generation.Biomes;
using ProcPoke.Generation.Rng;
using ProcPoke.Generation.Topology;

namespace ProcPoke.Generation.Carving;

/// <summary>
/// Carves a Route as a horizontal area, spine-first (ADR-0001): a guaranteed walkable corridor along the
/// exit opening's row is laid first and never violated — every other opening (the entry, and any
/// branch/loop-back openings on the top or bottom edge) reaches it through a short protected stub, so
/// left↔right traversal and every other edge are always mutually reachable. Decoration is then applied
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

    public static CarvedArea Carve(Area area, Biome biome, Pcg32 rng, IReadOnlyList<AreaOpening> planned)
    {
        var (w, h) = CarveKit.Dimensions(area.Size);
        var border = biome is Biome.Mountain or Biome.Cave ? LogicalTile.Wall : LogicalTile.Tree;
        var floor = biome == Biome.Desert ? LogicalTile.Sand : LogicalTile.Ground;

        var grid = new TileGrid(w, h, floor);

        // Border ring.
        for (var x = 0; x < w; x++) { grid[x, 0] = border; grid[x, h - 1] = border; }
        for (var y = 0; y < h; y++) { grid[0, y] = border; grid[w - 1, y] = border; }

        // The spine row is the exit opening's row (edge-aligned with the next area) — the one guaranteed
        // walkable corridor every other opening reaches through a stub, kept clear of decoration below.
        var canvas = new Canvas(grid, floor, planned.OffsetOr(EdgeSide.Right, h / 2));
        for (var x = 1; x < w - 1; x++) { grid[x, canvas.SpineY] = floor; canvas.Protected.Add((x, canvas.SpineY)); }

        var openings = new List<(int X, int Y)>
        {
            OpenEdge(canvas, EdgeSide.Left, planned.OffsetOr(EdgeSide.Left, h / 2)),
            OpenEdge(canvas, EdgeSide.Right, canvas.SpineY),
        };
        foreach (var o in planned.Where(o => o.Edge is EdgeSide.Top or EdgeSide.Bottom))
            openings.Add(OpenEdge(canvas, o.Edge, o.Offset));

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
        for (var i = 0; i < treeClumps; i++)
        {
            var upper = rng.Chance(0.5);
            var cy = upper ? rng.NextInt(2, spineY - 1) : rng.NextInt(spineY + 2, h - 2);
            var cx = rng.NextInt(3, w - 3);
            Stamp(canvas, cx - 1, cy - 1, cx + 1, cy, clumpTile, allowCorridor: false);
        }

        // One ledge run in the lower flank (a shortcut you drop down toward the entrance).
        var ledgeY = Math.Min(h - 2, spineY + 2);
        var ledgeX0 = rng.NextInt(3, w / 2);
        for (var x = ledgeX0; x < ledgeX0 + rng.NextInt(4, 8) && x < w - 1; x++)
            if (grid[x, ledgeY].IsWalkable()) grid[x, ledgeY] = LogicalTile.Ledge;

        // A trainer watching the path.
        PlaceOnFloor(grid, rng.NextInt(6, w - 6), spineY + (rng.Chance(0.5) ? -1 : 1), LogicalTile.TrainerPost, floor);

        // An item tucked into a flank pocket.
        PlaceOnFloor(grid, rng.NextInt(3, w - 3), rng.Chance(0.5) ? 2 : h - 3, LogicalTile.ItemBall, floor);

        return new CarvedArea { AreaId = area.Id, Grid = grid, Openings = openings };
    }

    /// <summary>Punches one edge opening through the border and connects it into the spine row.</summary>
    private static (int X, int Y) OpenEdge(Canvas c, EdgeSide edge, int offset)
    {
        var g = c.Grid;
        var (w, h) = (g.Width, g.Height);

        if (edge is EdgeSide.Left or EdgeSide.Right)
        {
            var x = edge == EdgeSide.Left ? 0 : w - 1;
            for (var dy = -1; dy <= 1; dy++)
                if (g.InBounds(x, offset + dy)) g[x, offset + dy] = c.Floor;
            g[x, offset] = LogicalTile.Warp;
            ConnectColumn(c, edge == EdgeSide.Left ? 1 : w - 2, offset);
            return (x, offset);
        }

        var y = edge == EdgeSide.Top ? 0 : h - 1;
        g[offset, y] = LogicalTile.Warp;
        ConnectColumn(c, offset, edge == EdgeSide.Top ? 1 : h - 2);
        return (offset, y);
    }

    /// <summary>Carves a protected floor stub down one column between <paramref name="fromY"/> and the
    /// spine row — an opening's link to the guaranteed corridor.</summary>
    private static void ConnectColumn(Canvas c, int x, int fromY)
    {
        var (lo, hi) = fromY <= c.SpineY ? (fromY, c.SpineY) : (c.SpineY, fromY);
        for (var y = lo; y <= hi; y++)
            if (c.Grid.InBounds(x, y)) { c.Grid[x, y] = c.Floor; c.Protected.Add((x, y)); }
    }

    /// <summary>Stamps a rectangle of a tile, staying inside the border and (optionally) off protected connectivity tiles.</summary>
    private static void Stamp(Canvas c, int x0, int y0, int x1, int y1, LogicalTile tile, bool allowCorridor)
    {
        var g = c.Grid;
        for (var y = Math.Max(1, y0); y <= Math.Min(g.Height - 2, y1); y++)
            for (var x = Math.Max(1, x0); x <= Math.Min(g.Width - 2, x1); x++)
                if (allowCorridor || !c.Protected.Contains((x, y))) g[x, y] = tile;
    }

    private static void PlaceOnFloor(TileGrid g, int x, int y, LogicalTile tile, LogicalTile floor)
    {
        if (g.InBounds(x, y) && g[x, y] == floor) g[x, y] = tile;
    }
}
